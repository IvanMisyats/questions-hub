using System.Text;

namespace QuestionsHub.Blazor.Infrastructure;

/// <summary>
/// The project's public links and contact address, shown in the site footer and on the disclaimer page.
/// </summary>
public static class SiteLinks
{
    /// <summary>Public Telegram channel with site updates.</summary>
    public const string UpdatesChannelUrl = "https://t.me/questions_com_ua";

    /// <summary>The project's source code.</summary>
    public const string GitHubUrl = "https://github.com/IvanMisyats/questions-hub";

    /// <summary>Monobank jar collecting donations for hosting.</summary>
    public const string DonationJarUrl = "https://send.monobank.ua/jar/9Bzt7pJXXe";

    /// <summary>
    /// Contact address. It reaches the admin through Cloudflare Email Routing; the inbox it forwards to
    /// is configured in Cloudflare only. Pages render it with <see cref="Protect"/>.
    /// </summary>
    public const string ContactEmail = "admin@questions.com.ua";

    /// <summary>
    /// Invite link of the Telegram discussion chat, stored already protected (<see cref="Protect"/>) so the
    /// URL is in neither this public repository nor the page HTML; <c>protected-links.js</c> decodes it
    /// only when someone uses the link. To replace the link (e.g. after revoking it in Telegram), run
    /// <c>$s='https://t.me/+…'; [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes(-join $s[($s.Length-1)..0]))</c>
    /// in PowerShell and paste the result here.
    /// </summary>
    public const string DiscussionChatProtected = "aVZ6TWpabmotTGZnSERsZCsvZW0udC8vOnNwdHRo";

    /// <summary>
    /// Hides an ASCII value (URL, e-mail address) from scrapers that read page HTML: Base64 of the
    /// reversed string. <c>protected-links.js</c> reverses it in the browser.
    /// </summary>
    public static string Protect(string value)
    {
        var chars = value.ToCharArray();
        Array.Reverse(chars);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(chars));
    }

    /// <summary>The inverse of <see cref="Protect"/> (the browser does this in <c>protected-links.js</c>).</summary>
    public static string Unprotect(string protectedValue)
    {
        var chars = Encoding.UTF8.GetString(Convert.FromBase64String(protectedValue)).ToCharArray();
        Array.Reverse(chars);
        return new string(chars);
    }
}
