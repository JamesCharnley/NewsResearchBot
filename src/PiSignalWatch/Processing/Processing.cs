namespace PiSignalWatch.Processing;

using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PiSignalWatch.Config;
using PiSignalWatch.Models;
using PiSignalWatch.Utilities;

public interface IProcessor
{
    Task<List<ProcessedItem>> ProcessAsync(List<ProcessedItem> items, AppSettings cfg, CancellationToken ct);
}

public class KeywordMatcher : IProcessor
{
    public Task<List<ProcessedItem>> ProcessAsync(List<ProcessedItem> items, AppSettings cfg, CancellationToken ct)
    {
        foreach (var i in items)
        {
            var txt = (i.Source.Title + " " + i.Source.Content).ToLowerInvariant();
            i.TopicMatches = cfg.Topics
                .Select(t => new TopicMatch
                {
                    Topic = t.Name,
                    MatchedKeywords = t.Keywords.Where(k => txt.Contains(k.ToLowerInvariant())).ToList()
                })
                .Where(x => x.MatchedKeywords.Any())
                .ToList();
        }

        return Task.FromResult(items);
    }
}

public class Deduplicator : IProcessor
{
    readonly HashSet<string> _seen = new();

    public Task<List<ProcessedItem>> ProcessAsync(List<ProcessedItem> items, AppSettings cfg, CancellationToken ct)
    {
        foreach (var i in items)
        {
            var k = $"{i.Source.Url}|{i.Source.Id}|{Hashing.Sha256(i.Source.Title)}|{Hashing.Sha256(i.Source.Content)}";
            i.IsDuplicate = !_seen.Add(k);
        }

        return Task.FromResult(items.Where(x => !x.IsDuplicate).ToList());
    }
}

public class RelevanceScorer : IProcessor
{
    readonly IDateTimeProvider _d;

    public RelevanceScorer(IDateTimeProvider d)
    {
        _d = d;
    }

    public Task<List<ProcessedItem>> ProcessAsync(List<ProcessedItem> items, AppSettings cfg, CancellationToken ct)
    {
        foreach (var i in items)
        {
            var kw = i.TopicMatches.Sum(x => x.MatchedKeywords.Count) * 2;
            var weight = cfg.SourceWeights.TryGetValue(i.Source.SourceType, out var w) ? w : 1;
            var multiplier = cfg.SourceWeightMultipliers.TryGetValue(i.Source.SourceType, out var m) ? Math.Clamp(m, 0d, 1d) : 1;
            var age = Math.Max(1, (_d.UtcNow - i.Source.PublishedAt).TotalHours);
            i.Score = kw * weight * multiplier + (24 / age);
        }

        return Task.FromResult(items.OrderByDescending(x => x.Score).ToList());
    }
}

public class DigestBuilder
{
    public Digest Build(List<ProcessedItem> items) => new() { Items = items.Take(50).ToList() };
}

public class OpenAiSummariser(IHttpClientFactory f, ILogger<OpenAiSummariser> log)
{
    public async Task<string> SummariseAsync(List<ProcessedItem> items, AppSettings cfg, CancellationToken ct)
    {
        if (!cfg.OpenAi.Enabled) return Fallback(items);

        var key = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (string.IsNullOrWhiteSpace(key)) return Fallback(items);

        try
        {
            var c = f.CreateClient();
            c.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", key);

            var prompt = BuildPrompt(items);
            log.LogInformation("Summary prompt sent to GPT:\n{prompt}", prompt);
            Console.WriteLine($"Summary prompt sent to GPT:\n{prompt}");

            var body = new { model = cfg.OpenAi.Model, input = prompt };
            var res = await c.PostAsync(
                "https://api.openai.com/v1/responses",
                new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
                ct);
            res.EnsureSuccessStatusCode();
            var s = await res.Content.ReadAsStringAsync(ct);
            return FormatDiscordMessage(s);
        }
        catch
        {
            return Fallback(items);
        }
    }

    public string BuildPrompt(List<ProcessedItem> items) =>
        "Summarize by topic with key links:\n" +
        string.Join("\n", items.Take(20).Select(i => $"- {string.Join(',', i.TopicMatches.Select(t => t.Topic))}: {i.Source.Title} {i.Source.Url}"));

    static string FormatDiscordMessage(string responseJson)
    {
        using var doc = JsonDocument.Parse(responseJson);
        var root = doc.RootElement;

        var responseObject = root.TryGetProperty("object", out var objectEl) ? objectEl.GetString() ?? "" : "response";
        var responseStatus = root.TryGetProperty("status", out var statusEl) ? statusEl.GetString() ?? "" : "unknown";
        var responseId = root.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? "" : "unknown";

        var outputType = "message";
        var outputStatus = "unknown";
        var outputId = "unknown";
        var outputText = "";

        if (root.TryGetProperty("output", out var outputEl) && outputEl.ValueKind == JsonValueKind.Array && outputEl.GetArrayLength() > 0)
        {
            var firstOutput = outputEl[0];
            outputType = firstOutput.TryGetProperty("type", out var typeEl) ? typeEl.GetString() ?? outputType : outputType;
            outputStatus = firstOutput.TryGetProperty("status", out var outputStatusEl) ? outputStatusEl.GetString() ?? outputStatus : outputStatus;
            outputId = firstOutput.TryGetProperty("id", out var outputIdEl) ? outputIdEl.GetString() ?? outputId : outputId;

            if (firstOutput.TryGetProperty("content", out var contentEl) && contentEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var content in contentEl.EnumerateArray())
                {
                    if (content.TryGetProperty("type", out var contentTypeEl)
                        && contentTypeEl.GetString() == "output_text"
                        && content.TryGetProperty("text", out var textEl))
                    {
                        outputText = textEl.GetString() ?? "";
                        break;
                    }
                }
            }
        }

        return $"{responseObject}\n{responseStatus}\n{responseId}\n\n\n\n{outputType}\n{outputStatus}\n{outputId}\n{outputText}";
    }

    string Fallback(List<ProcessedItem> items) =>
        string.Join("\n", items
            .GroupBy(i => i.TopicMatches.FirstOrDefault()?.Topic ?? "General")
            .Select(g => $"{g.Key}: {string.Join("; ", g.Take(5).Select(x => x.Source.Title + " (" + x.Source.Url + ")"))}"));
}
