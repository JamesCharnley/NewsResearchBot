#nullable enable
using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PiSignalWatch.Config;
using PiSignalWatch.Models;
using PiSignalWatch.Outputs;
using Xunit;

public class DiscordOutputTests
{
    [Fact]
    public void TrimsSummaryToDiscordLimit()
    {
        var longText = new string('a', 2100);

        var result = DiscordWebhookOutput.TrimToDiscordLimit(longText);

        Assert.Equal(2000, result.Length);
        Assert.EndsWith("...", result);
    }

    [Fact]
    public async Task SendsTrimmedPayloadToWebhook()
    {
        var handler = new CaptureHandler();
        var httpClient = new HttpClient(handler);
        var httpFactory = new FakeHttpClientFactory(httpClient);

        var output = new DiscordWebhookOutput(
            httpFactory,
            Options.Create(new AppSettings
            {
                Outputs = new OutputConfig
                {
                    DiscordWebhookUrl = "https://example.com/webhook"
                }
            }),
            NullLogger<DiscordWebhookOutput>.Instance);

        await output.SendAsync(new Digest { Summary = new string('b', 2200) }, default);

        Assert.NotNull(handler.Body);
        using var json = JsonDocument.Parse(handler.Body!);
        var content = json.RootElement.GetProperty("content").GetString();
        Assert.Equal(2000, content!.Length);
    }

    [Fact]
    public void UsesFallbackWhenSummaryIsEmpty()
    {
        var result = DiscordWebhookOutput.EnsureMessageContent("   ");
        Assert.Equal("[Digest summary was empty]", result);
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private sealed class FakeHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }
}
