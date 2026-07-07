using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using QuestionsHub.Blazor.Data;
using QuestionsHub.Blazor.Domain;

namespace QuestionsHub.Blazor.Infrastructure.Results;

/// <summary>
/// Orchestrates tournament-results sources of a package: attaching links, loading/reloading
/// results from platforms, and keeping the precomputed QuestionStats in sync.
/// A failed (re)load never touches previously loaded data.
/// </summary>
public class PackageResultsService(
    IDbContextFactory<QuestionsHubDbContext> contextFactory,
    RatingResultsClient ratingClient,
    OpenQuizResultsClient openQuizClient,
    IOptions<ResultsOptions> options,
    ILogger<PackageResultsService> logger)
{
    /// <summary>Keeps Cyrillic readable in stored jsonb (warnings are Blazor-escaped when displayed).</summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    /// Validates and attaches a results link to a package; the platform is detected from the
    /// link itself. Different URL forms of the same tournament (…/13098, …/13098/statistics,
    /// …/13098/tours) resolve to one external id and are rejected as duplicates.
    /// Throws <see cref="ResultsLoadException"/> with a user-facing message when the link is invalid or duplicated.
    /// </summary>
    public async Task<ResultsSource> AttachSource(int packageId, string url)
    {
        var parsed = ResultsUrlParser.Detect(url);
        var platform = parsed.Platform;

        await using var db = await contextFactory.CreateDbContextAsync();
        var package = await db.Packages.AsNoTracking().FirstOrDefaultAsync(p => p.Id == packageId)
                      ?? throw new ResultsLoadException("Пакет не знайдено.");
        if (package.Type != PackageType.Www)
        {
            throw new ResultsLoadException("Результати підтримуються лише для пакетів «Що?Де?Коли?».");
        }

        var duplicate = await db.ResultsSources.AnyAsync(s => s.PackageId == packageId
            && (s.Url == parsed.Url
                || (parsed.ExternalId != null && s.Platform == platform && s.ExternalId == parsed.ExternalId)));
        if (duplicate)
        {
            throw new ResultsLoadException("Це джерело вже додано до пакету.");
        }

        var source = new ResultsSource
        {
            PackageId = packageId,
            Platform = platform,
            Url = parsed.Url,
            ExternalId = parsed.ExternalId,
            // Public link for external display, when it's already known at attach time.
            // For an OpenQuiz aud link the public res link only becomes known after the first load.
            DisplayUrl = platform switch
            {
                ResultsPlatform.Other => parsed.Url,
                ResultsPlatform.Rating => $"{options.Value.RatingSiteBaseUrl.TrimEnd('/')}/tournament/{parsed.ExternalId}",
                ResultsPlatform.OpenQuiz when parsed.LinkKind == OpenQuizLinkKind.Results => parsed.Url,
                _ => null
            },
            CreatedAt = DateTime.UtcNow
        };

        db.ResultsSources.Add(source);
        await db.SaveChangesAsync();
        return source;
    }

    /// <summary>
    /// Loads (or reloads) results for a source from its platform. On failure the error is stored
    /// on the source and previously loaded results remain intact. Returns the updated source.
    /// </summary>
    public async Task<ResultsSource> LoadSource(int sourceId, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var source = await db.ResultsSources.FirstOrDefaultAsync(s => s.Id == sourceId, cancellationToken)
                     ?? throw new ResultsLoadException("Джерело результатів не знайдено.");
        if (source.Platform == ResultsPlatform.Other)
        {
            throw new ResultsLoadException("Це посилання не завантажується автоматично.");
        }

        var package = await db.Packages
            .AsNoTracking()
            .Include(p => p.Tours)
                .ThenInclude(t => t.Questions)
            .FirstAsync(p => p.Id == source.PackageId, cancellationToken);

        PlatformResults platformResults;
        QuestionStatsMappingResult mapping;
        try
        {
            var parsed = ResultsUrlParser.Parse(source.Platform, source.Url);
            platformResults = source.Platform switch
            {
                ResultsPlatform.Rating => await ratingClient.Load(parsed.ExternalId!, cancellationToken),
                ResultsPlatform.OpenQuiz => await openQuizClient.Load(parsed.ExternalId!, parsed.Token!, parsed.LinkKind, parsed.Title, cancellationToken),
                _ => throw new ResultsLoadException("Це посилання не завантажується автоматично.")
            };
            mapping = QuestionStatsMapper.Map(package, platformResults);
        }
        catch (ResultsLoadException ex)
        {
            logger.LogWarning(ex, "Loading results source {SourceId} ({Platform}) failed", sourceId, source.Platform);
            source.LastAttemptAt = DateTime.UtcNow;
            source.LoadError = Truncate(ex.Message, 1000);
            source.LoadErrorDetail = Truncate(ex.InnerException?.ToString(), 8000);
            source.ResultsAvailableAfter = ex.EmbargoedUntil;
            await db.SaveChangesAsync(cancellationToken);
            return source;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A bug or unexpected payload must land in LoadError, never crash the caller's circuit
            logger.LogError(ex, "Unexpected error loading results source {SourceId} ({Platform})", sourceId, source.Platform);
            source.LastAttemptAt = DateTime.UtcNow;
            source.LoadError = "Несподівана помилка під час завантаження результатів.";
            source.LoadErrorDetail = Truncate(ex.ToString(), 8000);
            source.ResultsAvailableAfter = null;
            await db.SaveChangesAsync(cancellationToken);
            return source;
        }

        var warnings = platformResults.Warnings.Concat(mapping.Warnings).ToList();
        var warningsJson = warnings.Count > 0 ? JsonSerializer.Serialize(warnings, JsonOptions) : null;

        // EnableRetryOnFailure forbids a user-initiated transaction outside an execution strategy,
        // so the whole write is wrapped in one (matching PackageDbImporter). The delegate may be
        // retried on a transient fault, so it starts from a clean change tracker each attempt.
        var strategy = db.Database.CreateExecutionStrategy();
        try
        {
            await strategy.ExecuteAsync(async () =>
            {
                db.ChangeTracker.Clear();
                await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

                // Delete by id only — old rows carry heavy jsonb that RemoveRange doesn't need loaded
                var oldTeamIds = await db.TeamResults
                    .Where(t => t.ResultsSourceId == sourceId)
                    .Select(t => t.Id)
                    .ToListAsync(cancellationToken);
                db.TeamResults.RemoveRange(oldTeamIds.Select(id => new TeamResult { Id = id, Name = "" }));

                foreach (var team in mapping.Teams)
                {
                    db.TeamResults.Add(new TeamResult
                    {
                        ResultsSourceId = sourceId,
                        Name = Truncate(team.Team.Name, 200)!,
                        Town = Truncate(team.Team.Town, 200),
                        ExternalTeamId = team.Team.ExternalTeamId,
                        Points = team.Team.Points,
                        Position = team.Team.Position,
                        ResultsByQuestionJson = team.ResultsByQuestion is { Count: > 0 }
                            ? JsonSerializer.Serialize(team.ResultsByQuestion)
                            : null
                    });
                }

                var tracked = await db.ResultsSources.FirstAsync(s => s.Id == sourceId, cancellationToken);
                var now = DateTime.UtcNow;
                tracked.LoadedAt = now;
                tracked.LastAttemptAt = now;
                tracked.LoadError = null;
                tracked.LoadErrorDetail = null;
                tracked.ResultsAvailableAfter = null;
                tracked.TeamsCount = mapping.Teams.Count;
                tracked.StatsMapped = mapping.StatsMapped;
                tracked.WarningsJson = warningsJson;
                tracked.RawPayload = platformResults.RawPayload;
                tracked.DisplayUrl = platformResults.DisplayUrl ?? tracked.DisplayUrl;
                tracked.Title = Truncate(platformResults.Title, 500) ?? tracked.Title;

                await db.SaveChangesAsync(cancellationToken);
                await RecomputeQuestionStats(db, tracked.PackageId, cancellationToken);
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The transaction rolled back, so previously loaded data is intact. Store the error
            // (with technical detail) on the source so the user sees it instead of a crash.
            logger.LogError(ex, "Saving results for source {SourceId} failed", sourceId);
            db.ChangeTracker.Clear();
            var failed = await db.ResultsSources.FirstAsync(s => s.Id == sourceId, cancellationToken);
            failed.LastAttemptAt = DateTime.UtcNow;
            failed.LoadError = ex is DbUpdateException
                ? "Не вдалося зберегти результати (можливо, одночасне оновлення) — спробуйте ще раз."
                : "Несподівана помилка під час збереження результатів.";
            failed.LoadErrorDetail = Truncate(ex.ToString(), 8000);
            await db.SaveChangesAsync(cancellationToken);
            return failed;
        }

        logger.LogInformation("Loaded results source {SourceId}: {Teams} teams, statsMapped={StatsMapped}",
            sourceId, mapping.Teams.Count, mapping.StatsMapped);

        // The change tracker was cleared inside the strategy; re-read the committed row for the caller.
        return await db.ResultsSources.AsNoTracking().FirstAsync(s => s.Id == sourceId, cancellationToken);
    }

    /// <summary>Deletes a source with its team results and re-aggregates the package stats.</summary>
    public async Task DeleteSource(int sourceId)
    {
        await using var db = await contextFactory.CreateDbContextAsync();
        var packageId = await db.ResultsSources
            .Where(s => s.Id == sourceId)
            .Select(s => (int?)s.PackageId)
            .FirstOrDefaultAsync();
        if (packageId == null)
        {
            return;
        }

        // Wrapped in an execution strategy because EnableRetryOnFailure forbids a bare
        // user-initiated transaction; retry-safe via a clean change tracker per attempt.
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync();

            // Delete the team rows first and commit that, then the source. Doing both in one
            // SaveChanges lets the source's FK cascade race the explicit team deletes (0-rows
            // concurrency error on Postgres); ordering them also means we don't rely on the DB
            // cascade, which the InMemory test provider doesn't emulate for stub deletes.
            var teamIds = await db.TeamResults
                .Where(t => t.ResultsSourceId == sourceId)
                .Select(t => t.Id)
                .ToListAsync();
            db.TeamResults.RemoveRange(teamIds.Select(id => new TeamResult { Id = id, Name = "" }));
            await db.SaveChangesAsync();

            db.ResultsSources.Remove(new ResultsSource { Id = sourceId, Url = "" });
            await RecomputeQuestionStats(db, packageId.Value);
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        });
    }

    /// <summary>
    /// Sources of a package for display (manage card, package header, results page), oldest
    /// first. Projects away RawPayload so list queries never carry platform payloads.
    /// </summary>
    public async Task<List<ResultsSourceInfo>> GetSourcesForPackage(int packageId)
    {
        await using var db = await contextFactory.CreateDbContextAsync();
        return await db.ResultsSources
            .Where(s => s.PackageId == packageId)
            .OrderBy(s => s.Id)
            .Select(s => new ResultsSourceInfo(
                s.Id, s.Platform, s.Url, s.Title, s.DisplayUrl,
                s.LoadedAt, s.LoadError, s.LoadErrorDetail, s.ResultsAvailableAfter,
                s.TeamsCount, s.StatsMapped, s.WarningsJson))
            .ToListAsync();
    }

    /// <summary>
    /// Full re-aggregation of QuestionStats for a package from the stored per-team dictionaries
    /// of all its sources — never incremental, so a reload of one source can't corrupt another's
    /// contribution. Runs inside the caller's transaction; the caller saves changes.
    /// </summary>
    private static async Task RecomputeQuestionStats(QuestionsHubDbContext db, int packageId, CancellationToken cancellationToken = default)
    {
        var questionIds = await db.Questions
            .Where(q => q.Tour.PackageId == packageId)
            .Select(q => q.Id)
            .ToListAsync(cancellationToken);
        var packageQuestionIds = new HashSet<int>(questionIds);

        var oldStats = await db.QuestionStats
            .Where(s => s.Question.Tour.PackageId == packageId)
            .ToListAsync(cancellationToken);
        db.QuestionStats.RemoveRange(oldStats);

        var teamJsons = await db.TeamResults
            .Where(t => t.ResultsSource.PackageId == packageId && t.ResultsByQuestionJson != null)
            .Select(t => t.ResultsByQuestionJson!)
            .ToListAsync(cancellationToken);

        var aggregates = QuestionStatsAggregator.Aggregate(teamJsons, packageQuestionIds);

        foreach (var (questionId, aggregate) in aggregates)
        {
            db.QuestionStats.Add(new QuestionStat
            {
                QuestionId = questionId,
                CorrectCount = aggregate.Correct,
                TotalTeams = aggregate.Total
            });
        }
    }

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        return value.Length <= maxLength ? value : value[..maxLength];
    }
}
