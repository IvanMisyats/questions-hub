namespace QuestionsHub.Blazor.Infrastructure;

/// <summary>
/// Configuration settings for email service (Mailjet).
/// </summary>
public class EmailSettings
{
    public const string SectionName = "Email";

    /// <summary>
    /// Mailjet API Key (public key).
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Mailjet API Secret (private key).
    /// </summary>
    public string ApiSecret { get; set; } = string.Empty;

    /// <summary>
    /// Sender email address (must be verified in Mailjet).
    /// </summary>
    public string SenderEmail { get; set; } = string.Empty;

    /// <summary>
    /// Sender display name.
    /// </summary>
    public string SenderName { get; set; } = string.Empty;

    /// <summary>
    /// Base URL of the site (for generating email links).
    /// </summary>
    public string SiteUrl { get; set; } = string.Empty;
}

