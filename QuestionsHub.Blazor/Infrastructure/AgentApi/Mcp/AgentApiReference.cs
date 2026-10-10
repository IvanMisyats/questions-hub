namespace QuestionsHub.Blazor.Infrastructure.AgentApi.Mcp;

/// <summary>
/// What the MCP server tells agents about itself: the instructions sent when a client connects, and the
/// full reference — the "Agent API" section of docs/API.md, embedded at build time (between the
/// <c>agent-reference:start</c> / <c>agent-reference:end</c> markers), so docs and server never drift.
/// </summary>
public static class AgentApiReference
{
    public const string ResourceUri = "questions-hub://docs/agent-api";

    private const string ResourceName = "QuestionsHub.Docs.API.md";
    private const string StartMarker = "<!-- agent-reference:start";
    private const string EndMarker = "<!-- agent-reference:end -->";

    private static readonly Lazy<string> Reference = new(Load);

    /// <summary>The "Agent API" documentation section (markdown).</summary>
    public static string Text => Reference.Value;

    /// <summary>Sent to every client on connect (MCP <c>instructions</c>); kept short, points to the reference.</summary>
    public const string Instructions = """
        questions-hub edits packages of Ukrainian quiz questions on questions.com.ua on behalf of the token's user.
        Game types: «Що? Де? Коли?» (ЩДК, gameType "www") — tours of numbered questions; «Своя гра» (gameType "shvager") — every tour is a theme (title = theme name) and a question's number is its value by position (normally 10, 20, 30, 40, 50).
        Structure: package → tours → optional blocks → questions. People name questions by tour and displayed number ("тур 2, питання 17"); look the ids up with get_package, never guess them.
        Question fields (editor label → field): Текст text, Відповідь answer, Залік acceptedAnswers, Незалік rejectedAnswers, Коментар comment, Джерело source, Автори authors, Роздатка handoutText, Вказівка ведучому hostInstructions, Форма answerForm. Media cannot be changed here. Keep the Ukrainian wording exactly as given.

        Workflow:
        1. whoami — the token's scope (read / readWrite) and the packages it may touch.
        2. list_packages, then get_package — note its 'version'.
        3. Preview: apply_changeset with the operations, dryRun true (the default) and expectedVersion = version. Check the diff and warnings. Errors name the operation index and what it accepts.
        4. Apply: the same call with dryRun false, the same expectedVersion and a new requestId (a UUID; reuse it only to retry the very same request after a network error).
        5. On a version conflict the package changed meanwhile: get_package again and rebuild.

        Authors are {"id": n} (from search_authors or the package tree) or {"firstName": "…", "lastName": "…"} (an existing author with exactly that name is reused, otherwise created) — prefer ids for existing people.
        Author, editor and tag lists are replaced as a whole: to add one co-author, send the current authors plus the new one ([] clears the list).
        A preview warns when an author or tag will be created (no exact name match) — check the spelling, or use search_authors (a full name works) to reference an existing person by id.
        'set' replaces a field's whole value: to add to Залік or a comment, send the current text plus the addition. Stress marks (о́) are kept exactly as sent.
        In ЩДК the warmup restarts its numbering at 1, so identify a question by its tour and number together.
        Adding, deleting or moving questions and tours is refused when the package has tournament results (hasResults). Ask the user to close the package's editor tab while you work.
        Every operation, field, rule, limit, the response and the errors: call get_api_reference (also the resource questions-hub://docs/agent-api).
        """;

    private static string Load()
    {
        using var stream = typeof(AgentApiReference).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource {ResourceName} is missing.");
        using var reader = new StreamReader(stream);
        return Extract(reader.ReadToEnd());
    }

    /// <summary>The text between the markers (the start marker's line excluded).</summary>
    public static string Extract(string markdown)
    {
        var start = markdown.IndexOf(StartMarker, StringComparison.Ordinal);
        var end = markdown.IndexOf(EndMarker, StringComparison.Ordinal);
        if (start < 0 || end < start)
            throw new InvalidOperationException("The agent reference markers are missing in docs/API.md.");

        var afterStartLine = markdown.IndexOf('\n', start) + 1;
        return markdown[afterStartLine..end].Trim();
    }
}
