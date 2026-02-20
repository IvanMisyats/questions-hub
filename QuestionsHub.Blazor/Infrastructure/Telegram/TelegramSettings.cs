namespace QuestionsHub.Blazor.Infrastructure.Telegram;

/// <summary>
/// Configuration settings for Telegram channel notifications.
/// </summary>
public class TelegramSettings
{
    public const string SectionName = "Telegram";

    /// <summary>
    /// Telegram Bot API token (from @BotFather).
    /// When empty, notifications are disabled.
    /// </summary>
    public string BotToken { get; set; } = string.Empty;

    /// <summary>
    /// Telegram channel ID or username (e.g. "@questions_com_ua").
    /// </summary>
    public string ChannelId { get; set; } = string.Empty;

    /// <summary>
    /// Base URL of the site (for generating links in messages).
    /// </summary>
    public string SiteUrl { get; set; } = string.Empty;
}
