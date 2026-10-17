using System.Text.RegularExpressions;

namespace QuestionsHub.Blazor.Infrastructure.Export;

/// <summary>File names for downloaded packages.</summary>
public static partial class ExportFileName
{
    /// <summary>
    /// <paramref name="title"/> as a file name: characters invalid in file names become underscores,
    /// runs of underscores collapse, at most 100 characters; "package" when nothing is left.
    /// </summary>
    public static string FromTitle(string title)
    {
        var sanitized = InvalidFileNameCharsRegex().Replace(title, "_");
        sanitized = MultipleUnderscoresRegex().Replace(sanitized, "_").Trim('_');

        if (sanitized.Length > 100)
            sanitized = sanitized[..100].TrimEnd('_');

        return string.IsNullOrWhiteSpace(sanitized) ? "package" : sanitized;
    }

    [GeneratedRegex(@"[<>:""/\\|?*\x00-\x1F]")]
    private static partial Regex InvalidFileNameCharsRegex();

    [GeneratedRegex(@"_+")]
    private static partial Regex MultipleUnderscoresRegex();
}
