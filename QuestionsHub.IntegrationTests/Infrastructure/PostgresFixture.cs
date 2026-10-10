using DotNet.Testcontainers.Builders;
using Testcontainers.PostgreSql;
using Xunit;

namespace QuestionsHub.IntegrationTests.Infrastructure;

/// <summary>
/// One PostgreSQL 16 container shared by every test in the <see cref="IntegrationCollection"/>.
/// The app applies its own EF migrations on startup, so the database starts empty.
/// When Docker is not available the fixture records why and tests skip instead of failing.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _container;

    /// <summary>Connection string of the running container (null when unavailable).</summary>
    public string? ConnectionString { get; private set; }

    /// <summary>Why the database is unavailable, or null when it is running.</summary>
    public string? UnavailableReason { get; private set; }

    public async Task InitializeAsync()
    {
        // Same setup as the db-setup service in docker-compose.yml: hunspell dictionaries in
        // tsearch_data, then the extension and FTS scripts (user permissions are not needed —
        // tests connect as the superuser).
        var dictionaries = Path.Combine(RepoRoot, "db", "dictionaries");
        var dictionary = await File.ReadAllBytesAsync(Path.Combine(dictionaries, "uk_UA.dic"));
        var affix = await File.ReadAllBytesAsync(Path.Combine(dictionaries, "uk_UA.aff"));

        try
        {
            _container = new PostgreSqlBuilder("postgres:16-alpine")
                .WithDatabase("questionshub_test")
                .WithResourceMapping(dictionary, "/usr/local/share/postgresql/tsearch_data/uk_ua.dict")
                .WithResourceMapping(affix, "/usr/local/share/postgresql/tsearch_data/uk_ua.affix")
                .Build();

            await _container.StartAsync();
        }
        catch (DockerUnavailableException ex) when (!IsCi)
        {
            // Only a missing Docker engine skips, and never on CI: any other setup failure fails
            // the tests instead of silently disabling them.
            UnavailableReason = $"Docker is not available, integration tests skipped: {ex.Message}";
            return;
        }

        foreach (var script in new[] { "01-extensions.sql", "03-fts-setup.sql" })
        {
            var sql = (await File.ReadAllTextAsync(Path.Combine(RepoRoot, "db", "scripts", script))).TrimStart('﻿');
            var result = await _container.ExecScriptAsync(sql);
            if (result.ExitCode != 0)
                throw new InvalidOperationException($"{script} failed: {result.Stderr}");
        }

        ConnectionString = _container.GetConnectionString();
    }

    /// <summary>GitHub Actions (and most CI systems) set <c>CI=true</c>.</summary>
    private static bool IsCi =>
        string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>The repository root (the folder containing <c>db/scripts</c>).</summary>
    private static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "db", "scripts")))
                dir = dir.Parent;

            return dir?.FullName ?? throw new DirectoryNotFoundException("Repository root with db/scripts not found.");
        }
    }

    public async Task DisposeAsync()
    {
        if (_container != null)
            await _container.DisposeAsync();
    }

    /// <summary>Skips the calling test when the database container is not running.</summary>
    public void SkipIfUnavailable() => Skip.If(UnavailableReason != null, UnavailableReason);
}

[CollectionDefinition(Name)]
public sealed class IntegrationCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "Integration";
}
