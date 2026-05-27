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
            foreach (var col in _collectors.Where(x => x.IsEnabled(_cfg)))
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
}
