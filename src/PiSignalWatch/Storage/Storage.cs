namespace PiSignalWatch.Storage;

using System.Text.Json;
using Microsoft.Extensions.Options;
using PiSignalWatch.Config;
using PiSignalWatch.Models;

public interface IStorageProvider
{
    Task SaveRawItemsAsync(string source, IReadOnlyCollection<SourceItem> items, CancellationToken ct);
    Task SaveProcessedItemsAsync(IReadOnlyCollection<ProcessedItem> items, CancellationToken ct);
    Task SaveDigestAsync(Digest digest, CancellationToken ct);
}

public class JsonFileStorageProvider(IOptions<AppSettings> o) : IStorageProvider
{
    private string Root => o.Value.DataFolder;
    private static readonly JsonSerializerOptions J = new() { WriteIndented = true };
    private static readonly string SharedDataCacheRoot = Path.Combine(Directory.GetCurrentDirectory(), "shareddatacache");

    public Task SaveRawItemsAsync(string source, IReadOnlyCollection<SourceItem> items, CancellationToken ct) =>
        Write(Path.Combine(Root, "raw", source, $"{DateTime.UtcNow:yyyyMMddHHmmss}.json"), items, ct);

    public async Task SaveProcessedItemsAsync(IReadOnlyCollection<ProcessedItem> items, CancellationToken ct)
    {
        var processedPath = Path.Combine(Root, "processed", $"{DateTime.UtcNow:yyyyMMddHHmmss}.json");
        await Write(processedPath, items, ct);
        await UpdateSharedDataCacheAsync(processedPath, ct);
    }

    public Task SaveDigestAsync(Digest d, CancellationToken ct) =>
        Write(Path.Combine(Root, "digests", $"{DateTime.UtcNow:yyyyMMddHHmmss}.json"), d, ct);

    private static async Task Write<T>(string p, T obj, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        await File.WriteAllTextAsync(p, JsonSerializer.Serialize(obj, J), ct);
    }

    private static Task UpdateSharedDataCacheAsync(string processedPath, CancellationToken ct)
    {
        Directory.CreateDirectory(SharedDataCacheRoot);

        foreach (var existingJson in Directory.GetFiles(SharedDataCacheRoot, "*.json"))
        {
            File.Delete(existingJson);
        }

        var destinationPath = Path.Combine(SharedDataCacheRoot, Path.GetFileName(processedPath));
        File.Copy(processedPath, destinationPath, overwrite: true);

        return Task.CompletedTask;
    }
}

public class StateStore(IOptions<AppSettings> o)
{
    string Root => Path.Combine(o.Value.DataFolder, "state");

    public async Task<HashSet<string>> LoadSeenItemsAsync(CancellationToken ct)
    {
        var p = Path.Combine(Root, "seen-items.json");
        if (!File.Exists(p)) return new();
        return JsonSerializer.Deserialize<HashSet<string>>(await File.ReadAllTextAsync(p, ct)) ?? new();
    }

    public async Task SaveSeenItemsAsync(HashSet<string> s, CancellationToken ct)
    {
        Directory.CreateDirectory(Root);
        await File.WriteAllTextAsync(Path.Combine(Root, "seen-items.json"), JsonSerializer.Serialize(s), ct);
    }

    public async Task<Dictionary<string, string>> LoadCollectorStateAsync(CancellationToken ct)
    {
        var p = Path.Combine(Root, "collector-state.json");
        if (!File.Exists(p)) return new();
        return JsonSerializer.Deserialize<Dictionary<string, string>>(await File.ReadAllTextAsync(p, ct)) ?? new();
    }

    public async Task SaveCollectorStateAsync(Dictionary<string, string> s, CancellationToken ct)
    {
        Directory.CreateDirectory(Root);
        await File.WriteAllTextAsync(Path.Combine(Root, "collector-state.json"), JsonSerializer.Serialize(s), ct);
    }
}
