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

    [Fact]
    public async Task SourceWeightMultiplierScalesKeywordScore()
    {
        var scorer = new RelevanceScorer(new FixedDateTimeProvider(new DateTimeOffset(2026, 1, 1, 2, 0, 0, TimeSpan.Zero)));
        var baseConfig = new AppSettings
        {
            SourceWeights = new Dictionary<string, double> { ["rss"] = 1.0 }
        };
        var scaledConfig = new AppSettings
        {
            SourceWeights = new Dictionary<string, double> { ["rss"] = 1.0 },
            SourceWeightMultipliers = new Dictionary<string, double> { ["rss"] = 0.5 }
        };

        var item = new ProcessedItem
        {
            Source = new() { SourceType = "rss", PublishedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) },
            TopicMatches = [new() { Topic = "D", MatchedKeywords = ["ai", "defense"] }]
        };

        var baseScore = (await scorer.ProcessAsync([item], baseConfig, default))[0].Score;
        var scaledScore = (await scorer.ProcessAsync([item], scaledConfig, default))[0].Score;

        Assert.True(scaledScore < baseScore);
    }

    [Fact]
    public async Task SourceWeightMultiplierIsClampedToZeroAndOne()
    {
        var scorer = new RelevanceScorer(new FixedDateTimeProvider(new DateTimeOffset(2026, 1, 1, 2, 0, 0, TimeSpan.Zero)));
        var highConfig = new AppSettings
        {
            SourceWeights = new Dictionary<string, double> { ["reddit"] = 1.0 },
            SourceWeightMultipliers = new Dictionary<string, double> { ["reddit"] = 5.0 }
        };
        var zeroConfig = new AppSettings
        {
            SourceWeights = new Dictionary<string, double> { ["reddit"] = 1.0 },
            SourceWeightMultipliers = new Dictionary<string, double> { ["reddit"] = -0.1 }
        };

        var item = new ProcessedItem
        {
            Source = new() { SourceType = "reddit", PublishedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) },
            TopicMatches = [new() { Topic = "D", MatchedKeywords = ["ai", "defense"] }]
        };

        var highScore = (await scorer.ProcessAsync([item], highConfig, default))[0].Score;
        var zeroScore = (await scorer.ProcessAsync([item], zeroConfig, default))[0].Score;

        Assert.Equal(16, highScore, 5);
        Assert.Equal(12, zeroScore, 5);
    }

    private sealed class FakeDateTimeProvider : IDateTimeProvider
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    }

    private sealed class FixedDateTimeProvider(DateTimeOffset now) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow => now;
    }
}
