using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PiSignalWatch.Config;
using PiSignalWatch.Models;
using PiSignalWatch.Processing;
using PiSignalWatch.Utilities;
using Xunit;

public class ProcessingTests
{
    [Fact]
    public async Task KeywordMatcherMatches()
    {
        var processor = new KeywordMatcher();
        var config = new AppSettings
        {
            Topics = [new() { Name = "UFO", Keywords = ["uap"] }]
        };

        var result = await processor.ProcessAsync(
            [new() { Source = new() { Title = "UAP report" } }],
            config,
            default);

        Assert.Single(result[0].TopicMatches);
    }

    [Fact]
    public async Task Deduplicates()
    {
        var deduplicator = new Deduplicator();
        var config = new AppSettings();
        var items = new List<ProcessedItem>
        {
            new() { Source = new() { Id = "1", Url = "u", Title = "t", Content = "c" } },
            new() { Source = new() { Id = "1", Url = "u", Title = "t", Content = "c" } }
        };

        var result = await deduplicator.ProcessAsync(items, config, default);

        Assert.Single(result);
    }

    [Fact]
    public async Task Scores()
    {
        var scorer = new RelevanceScorer(new FakeDateTimeProvider());
        var config = new AppSettings
        {
            Topics = [new() { Name = "D", Keywords = ["ai"] }]
        };

        var item = new ProcessedItem
        {
            Source = new() { Title = "defense ai", PublishedAt = DateTimeOffset.UtcNow.AddHours(-1) },
            TopicMatches = [new() { Topic = "D", MatchedKeywords = ["ai"] }]
        };

        var result = await scorer.ProcessAsync([item], config, default);

        Assert.True(result[0].Score > 0);
    }

    private sealed class FakeDateTimeProvider : IDateTimeProvider
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    }
}
