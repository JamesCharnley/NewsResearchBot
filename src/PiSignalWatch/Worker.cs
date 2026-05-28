using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PiSignalWatch.Collectors;
using PiSignalWatch.Config;
using PiSignalWatch.Models;
using PiSignalWatch.Outputs;
using PiSignalWatch.Processing;
using PiSignalWatch.Storage;

namespace PiSignalWatch;

public class Worker : BackgroundService
{
    const int TopRssLinksToPromote = 10;

    readonly IEnumerable<ISourceCollector> _collectors;
    readonly IEnumerable<IProcessor> _processors;
    readonly IEnumerable<IOutputChannel> _outputs;
    readonly IStorageProvider _storage;
    readonly DigestBuilder _digestBuilder;
    readonly OpenAiSummariser _summ;
    readonly AppSettings _cfg;
    readonly ILogger<Worker> _log;

    public Worker(
        IEnumerable<ISourceCollector> c,
        IEnumerable<IProcessor> p,
        IEnumerable<IOutputChannel> o,
        IStorageProvider s,
        DigestBuilder d,
        OpenAiSummariser sum,
        IOptions<AppSettings> cfg,
        ILogger<Worker> l)
    {
        _collectors = c;
        _processors = p;
        _outputs = o;
        _storage = s;
        _digestBuilder = d;
        _summ = sum;
        _cfg = cfg.Value;
        _log = l;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var raw = new List<SourceItem>();

            await CollectRssAndPromoteTopLinksAsync(raw, ct);

            foreach (var col in _collectors.Where(x => x.IsEnabled(_cfg) && x.Name != "rss"))
            {
                try
                {
                    var r = await col.CollectAsync(ct);
                    raw.AddRange(r.Items);
                    await _storage.SaveRawItemsAsync(col.Name, r.Items, ct);
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "collector fail {c}", col.Name);
                }
            }

            var processed = raw.Select(x => new ProcessedItem { Source = x }).ToList();
            foreach (var p in _processors)
            {
                processed = await p.ProcessAsync(processed, _cfg, ct);
            }

            var digest = _digestBuilder.Build(processed);
            digest.Summary = await _summ.SummariseAsync(processed, _cfg, ct);

            await _storage.SaveProcessedItemsAsync(processed, ct);
            await _storage.SaveDigestAsync(digest, ct);

            foreach (var o in _outputs.Where(x => x.IsEnabled(_cfg)))
            {
                try
                {
                    await o.SendAsync(digest, ct);
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "output fail {o}", o.Name);
                }
            }

            await Task.Delay(TimeSpan.FromMinutes(_cfg.PollIntervalMinutes), ct);
        }
    }

    async Task CollectRssAndPromoteTopLinksAsync(List<SourceItem> raw, CancellationToken ct)
    {
        var rssCollector = _collectors.FirstOrDefault(x => x.Name == "rss" && x.IsEnabled(_cfg));
        if (rssCollector == null)
        {
            return;
        }

        try
        {
            var rssResult = await rssCollector.CollectAsync(ct);
            await _storage.SaveRawItemsAsync(rssCollector.Name, rssResult.Items, ct);

            var scoredRssItems = rssResult.Items
                .Select(item => new
                {
                    Item = item,
                    Score = ScoreRssByKeywords(item)
                })
                .ToList();

            foreach (var entry in scoredRssItems)
            {
                _log.LogInformation("RSS entry scored {score}: {title} {url}", entry.Score, entry.Item.Title, entry.Item.Url);
            }

            var topScoredRssItems = scoredRssItems
                .Where(x => x.Score > 0)
                .OrderByDescending(x => x.Score)
                .Take(TopRssLinksToPromote)
                .ToList();

            foreach (var entry in topScoredRssItems)
            {
                _log.LogInformation("Top RSS score {score}: {title} {url}", entry.Score, entry.Item.Title, entry.Item.Url);
            }

            var promotedLinks = topScoredRssItems
                .Select(x => x.Item.Url)
                .Where(url => !string.IsNullOrWhiteSpace(url))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (promotedLinks.Count == 0)
            {
                return;
            }

            _cfg.Sources.WebPageUrls = _cfg.Sources.WebPageUrls
                .Concat(promotedLinks)
                .Where(url => !string.IsNullOrWhiteSpace(url))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var webpageUrl in _cfg.Sources.WebPageUrls)
            {
                _log.LogInformation("WebPageUrl: {url}", webpageUrl);
            }

            _log.LogInformation("Promoted {count} RSS links into webpage URLs for this cycle", promotedLinks.Count);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "collector fail {c}", rssCollector.Name);
        }
    }

    int ScoreRssByKeywords(SourceItem item)
    {
        var txt = (item.Title + " " + item.Content).ToLowerInvariant();
        return _cfg.Topics.Sum(t => t.Keywords.Count(k => txt.Contains(k.ToLowerInvariant())));
    }
}
