using System.Globalization;
using System.Text;
using System.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using QuestionsHub.Blazor.Data;

namespace QuestionsHub.Blazor.Infrastructure.Telegram;

/// <summary>
/// Sends notifications to a Telegram channel when packages are published.
/// </summary>
public class TelegramNotificationService(
    IDbContextFactory<QuestionsHubDbContext> contextFactory,
    IHttpClientFactory httpClientFactory,
    IOptions<TelegramSettings> settings,
    ILogger<TelegramNotificationService> logger)
{
    private readonly TelegramSettings _settings = settings.Value;

    /// <summary>
    /// Sends a notification about a newly published package to the Telegram channel.
    /// Fire-and-forget: errors are logged but never thrown.
    /// </summary>
    public async Task NotifyPackagePublished(int packageId)
    {
        if (string.IsNullOrWhiteSpace(_settings.BotToken))
        {
            logger.LogDebug("Telegram bot token is not configured — skipping notification");
            return;
        }

        try
        {
            await using var context = await contextFactory.CreateDbContextAsync();
            var package = await context.Packages
                .Include(p => p.PackageEditors)
                .Include(p => p.Tags)
                .Include(p => p.Tours)
                    .ThenInclude(t => t.Editors)
                .Include(p => p.Tours)
                    .ThenInclude(t => t.Blocks)
                        .ThenInclude(b => b.Editors)
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == packageId);

            if (package == null)
            {
                logger.LogWarning("Cannot send Telegram notification: package {PackageId} not found", packageId);
                return;
            }

            var message = BuildMessage(package);
            await SendMessage(message);

            logger.LogInformation("Telegram notification sent for package {PackageId} \"{Title}\"",
                packageId, package.Title);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to send Telegram notification for package {PackageId}", packageId);
        }
    }

    /// <summary>
    /// Builds the HTML-formatted Telegram message for a published package.
    /// </summary>
    internal string BuildMessage(Domain.Package package)
    {
        var sb = new StringBuilder();
        var siteUrl = _settings.SiteUrl.TrimEnd('/');

        // Line 1: Опубліковано: <a href="...">Title</a> (18+)
        var packageUrl = $"{siteUrl}/package/{package.Id}";
        var titleText = HtmlEncode(package.Title);
        var has18Plus = package.Tags.Any(t =>
            t.Name.Equals("18+", StringComparison.OrdinalIgnoreCase));

        sb.Append(CultureInfo.InvariantCulture, $"Опубліковано: <a href=\"{packageUrl}\">{titleText}</a>");
        if (has18Plus)
        {
            sb.Append(" (18+)");
        }

        // Line 2: Редактори: editor links
        var editors = package.Editors.ToList();
        if (editors.Count > 0)
        {
            sb.AppendLine();
            sb.Append("Редактори: ");

            var editorLinks = editors.Select(e =>
            {
                var editorUrl = $"{siteUrl}/editor/{e.Id}";
                var name = HtmlEncode(e.FullName);
                return $"<a href=\"{editorUrl}\">{name}</a>";
            });

            sb.Append(string.Join(", ", editorLinks));
        }

        return sb.ToString();
    }

    private async Task SendMessage(string htmlText)
    {
        var url = $"https://api.telegram.org/bot{_settings.BotToken}/sendMessage";

        using var client = httpClientFactory.CreateClient();
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["chat_id"] = _settings.ChannelId,
            ["text"] = htmlText,
            ["parse_mode"] = "HTML",
            ["disable_web_page_preview"] = "true"
        });

        var response = await client.PostAsync(url, content);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            logger.LogWarning(
                "Telegram API returned {StatusCode}: {Body}",
                (int)response.StatusCode, body);
        }
    }

    private static string HtmlEncode(string text)
    {
        return HttpUtility.HtmlEncode(text);
    }
}
