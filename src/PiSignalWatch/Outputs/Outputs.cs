namespace PiSignalWatch.Outputs;

using System.Net;
using System.Net.Mail;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using PiSignalWatch.Config;
using PiSignalWatch.Models;

public interface IOutputChannel
{
    string Name { get; }
    bool IsEnabled(AppSettings c);
    Task SendAsync(Digest digest, CancellationToken ct);
}

public class DiscordWebhookOutput(IHttpClientFactory f, IOptions<AppSettings> o) : IOutputChannel
{
    private const int DiscordMessageLimit = 2000;

    public string Name => "discord";
    public bool IsEnabled(AppSettings c) => c.Outputs.EnableDiscord;

    public async Task SendAsync(Digest d, CancellationToken ct)
    {
        var url = Environment.GetEnvironmentVariable("DISCORD_WEBHOOK_URL") ?? o.Value.Outputs.DiscordWebhookUrl;
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        var payload = new { content = TrimToDiscordLimit(d.Summary) };
        var response = await f.CreateClient().PostAsync(
            url,
            new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
            ct);

        response.EnsureSuccessStatusCode();
    }

    public static string TrimToDiscordLimit(string summary)
    {
        var value = summary ?? string.Empty;
        if (value.Length <= DiscordMessageLimit)
        {
            return value;
        }

        const string suffix = "...";
        return value[..(DiscordMessageLimit - suffix.Length)] + suffix;
    }
}

public class TelegramOutput(IHttpClientFactory f, IOptions<AppSettings> o) : IOutputChannel
{
    public string Name => "telegram";
    public bool IsEnabled(AppSettings c) => c.Outputs.EnableTelegram;

    public async Task SendAsync(Digest d, CancellationToken ct)
    {
        var token = Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN");
        var chat = o.Value.Outputs.TelegramChatId;
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(chat))
        {
            return;
        }

        var url = $"https://api.telegram.org/bot{token}/sendMessage";
        var payload = new { chat_id = chat, text = d.Summary };
        await f.CreateClient().PostAsync(
            url,
            new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
            ct);
    }
}

public class EmailSmtpOutput(IOptions<AppSettings> o) : IOutputChannel
{
    public string Name => "email";
    public bool IsEnabled(AppSettings c) => c.Outputs.EnableEmail;

    public async Task SendAsync(Digest d, CancellationToken ct)
    {
        var c = o.Value.Outputs;
        var pwd = Environment.GetEnvironmentVariable("EMAIL_SMTP_PASSWORD");
        if (string.IsNullOrWhiteSpace(c.EmailHost)
            || string.IsNullOrWhiteSpace(c.EmailUsername)
            || string.IsNullOrWhiteSpace(pwd)
            || string.IsNullOrWhiteSpace(c.EmailTo)
            || string.IsNullOrWhiteSpace(c.EmailFrom))
        {
            return;
        }

        using var smtp = new SmtpClient(c.EmailHost, c.EmailPort)
        {
            Credentials = new NetworkCredential(c.EmailUsername, pwd),
            EnableSsl = true
        };
        using var msg = new MailMessage(c.EmailFrom, c.EmailTo, "PiSignalWatch Digest", d.Summary);
        await smtp.SendMailAsync(msg, ct);
    }
}
