namespace QuestionsHub.Blazor.Infrastructure.AgentApi.Mcp;

/// <summary>
/// The <c>.qhub</c> format for agents that convert a document before importing it: docs/PACKAGE_FORMAT.md
/// followed by the JSON Schema of <c>package.json</c> (docs/package-format.schema.json), both embedded at
/// build time so the server and the docs never drift.
/// </summary>
public static class QhubFormatReference
{
    public const string ResourceUri = "questions-hub://docs/qhub-format";

    private static readonly Lazy<string> Reference = new(Load);

    /// <summary>The format description (markdown) with the schema as a fenced JSON block at the end.</summary>
    public static string Text => Reference.Value;

    private static string Load() => $"""
        {Read("QuestionsHub.Docs.PACKAGE_FORMAT.md").Trim()}

        ## JSON Schema of `package.json`

        ```json
        {Read("QuestionsHub.Docs.package-format.schema.json").Trim()}
        ```
        """;

    private static string Read(string resourceName)
    {
        using var stream = typeof(QhubFormatReference).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource {resourceName} is missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
