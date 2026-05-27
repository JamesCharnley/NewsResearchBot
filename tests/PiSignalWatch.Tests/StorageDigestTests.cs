using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using PiSignalWatch.Config;
using PiSignalWatch.Models;
using PiSignalWatch.Processing;
using PiSignalWatch.Storage;
using Xunit;

public class StorageDigestTests
{
    [Fact]
    public async Task WritesJson()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var storage = new JsonFileStorageProvider(Options.Create(new AppSettings { DataFolder = tempDirectory }));

        await storage.SaveProcessedItemsAsync([new() { Source = new() { Title = "x" } }], default);

        Assert.True(Directory.GetFiles(Path.Combine(tempDirectory, "processed")).Length > 0);
    }

    [Fact]
    public void BuildsDigest()
    {
        var digestBuilder = new DigestBuilder();
        var digest = digestBuilder.Build([new() { Source = new() { Title = "x" } }]);

        Assert.Single(digest.Items);
    }
}
