using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using QuestionsHub.Blazor.Data;
using QuestionsHub.Blazor.Domain;
using QuestionsHub.Blazor.Infrastructure.Media;

namespace QuestionsHub.Blazor.Infrastructure.Import;

/// <summary>
/// Orchestrates the package import process.
/// </summary>
public class PackageImportService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly PackageImportOptions _options;
    private readonly MediaUploadOptions _mediaOptions;
    private readonly ILogger<PackageImportService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public PackageImportService(
        IServiceScopeFactory scopeFactory,
        IOptions<PackageImportOptions> options,
        IOptions<MediaUploadOptions> mediaOptions,
        ILogger<PackageImportService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _mediaOptions = mediaOptions.Value;
        _logger = logger;
    }

    /// <summary>
    /// Creates a new import job for the given file.
    /// </summary>
    /// <param name="type">Game type chosen by the user at upload; selects the DOCX parser.</param>
    public async Task<PackageImportJob> Enqueue(
        string ownerId,
        string fileName,
        Stream fileStream,
        long fileSize,
        PackageType type = PackageType.Www,
        CancellationToken ct = default)
    {
        // The name comes from the client: keep only the bare file name, never a path
        fileName = Path.GetFileName(fileName.Replace('\\', '/'));
        if (string.IsNullOrWhiteSpace(fileName) || fileName is "." or "..")
        {
            throw new ValidationException("Некоректна назва файлу");
        }

        // Validate
        if (!_options.IsExtensionAllowed(fileName))
        {
            throw new ValidationException(
                $"Непідтримуваний формат файлу. Дозволені: {string.Join(", ", _options.AllowedExtensions.Distinct(StringComparer.OrdinalIgnoreCase))}");
        }

        if (fileSize > _options.MaxFileSizeBytes)
        {
            throw new ValidationException(
                $"Файл занадто великий. Максимальний розмір: {PackageImportOptions.FormatFileSize(_options.MaxFileSizeBytes)}");
        }

        if (fileSize == 0)
        {
            throw new ValidationException("Файл порожній");
        }

        // Create job
        var jobId = Guid.NewGuid();
        var jobFolder = GetJobFolder(jobId);
        var inputFolder = Path.Combine(jobFolder, "input");
        Directory.CreateDirectory(inputFolder);

        var inputFilePath = Path.Combine(inputFolder, fileName);
        var relativeInputPath = Path.Combine("jobs", jobId.ToString(), "input", fileName);

        try
        {
            // Save file
            await using (var output = new FileStream(inputFilePath, FileMode.CreateNew, FileAccess.Write))
            {
                await fileStream.CopyToAsync(output, ct);
            }

            // Create job record
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<QuestionsHubDbContext>();

            var job = new PackageImportJob
            {
                Id = jobId,
                OwnerId = ownerId,
                Type = type,
                CreatedAt = DateTime.UtcNow,
                Status = ImportJobStatus.Queued,
                InputFileName = fileName,
                InputFilePath = relativeInputPath,
                InputFileSizeBytes = fileSize
            };

            // Not cancellable once the file is on disk: a cancellation surfacing after the commit would
            // otherwise delete the input of a job that exists.
            db.PackageImportJobs.Add(job);
            await db.SaveChangesAsync(CancellationToken.None);

            _logger.LogInformation("Created import job: {JobId} for file: {FileName}", jobId, fileName);

            return job;
        }
        catch
        {
            try { Directory.Delete(jobFolder, recursive: true); }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed to clean up import folder {JobId}", jobId); }
            throw;
        }
    }

    /// <summary>
    /// Processes an import job through all steps.
    /// </summary>
    public async Task Process(Guid jobId, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuestionsHubDbContext>();
        var importer = scope.ServiceProvider.GetRequiredService<PackageDbImporter>();

        var job = await db.PackageImportJobs.FindAsync([jobId], ct);
        if (job == null)
        {
            _logger.LogError("Job not found: {JobId}", jobId);
            return;
        }

        try
        {
            var jobFolder = GetJobFolder(jobId);
            if (job.PackageId.HasValue)
            {
                ImportJobLifecycle.Succeed(job);
                await db.SaveChangesAsync(CancellationToken.None);
                return;
            }
            var workingFolder = Path.Combine(jobFolder, "working");
            var assetsFolder = Path.Combine(jobFolder, "assets");
            var outputFolder = Path.Combine(jobFolder, "output");

            Directory.CreateDirectory(workingFolder);
            Directory.CreateDirectory(assetsFolder);
            Directory.CreateDirectory(outputFolder);

            var inputPath = Path.Combine(_mediaOptions.UploadsPath, job.InputFilePath);

            var extension = Path.GetExtension(job.InputFileName).ToLowerInvariant();

            ParseResult parseResult;

            if (extension == ".qhub")
            {
                parseResult = await ProcessQhub(scope, inputPath, assetsFolder, outputFolder, db, job, ct);

                // For .qhub the gameType in package.json is authoritative; the upload zone is only a hint
                if (parseResult.Type != job.Type)
                {
                    parseResult.Warnings.Insert(0,
                        $"Тип гри у файлі ({parseResult.Type.DisplayName()}) відрізняється від обраного поля імпорту — використано тип з файлу");
                }
            }
            else
            {
                parseResult = await ProcessDocx(scope, inputPath, assetsFolder, workingFolder, outputFolder, db, job, ct);
            }

            // Check if we got valid structure
            if (parseResult.Tours.Count == 0 || parseResult.TotalQuestions == 0)
            {
                throw new ParsingException("Не вдалося визначити структуру пакету. Перевірте формат документа.");
            }

            // Import to database
            await UpdateProgress(db, job, "Importing", 70, ct);

            var package = await importer.Import(parseResult, job.OwnerId, jobId, assetsFolder, ct);

            // The importer commits the job link and success with the package. Persist immediately
            // without the request token as well; no optional finalization may change this outcome.
            job.PackageId = package.Id;
            ImportJobLifecycle.Succeed(job);
            await db.SaveChangesAsync(CancellationToken.None);

            await SaveOriginalPackageFile(job, package.Id, ct);

            _logger.LogInformation("Job completed successfully: {JobId}, Package: {PackageId}", jobId, package.Id);
        }
        catch (Exception ex) when (job.PackageId.HasValue)
        {
            _logger.LogWarning(ex, "Package {PackageId} committed; optional finalization interrupted for job {JobId}", job.PackageId, jobId);
        }
        catch (ImportException ex)
        {
            await HandleJobError(db, job, ex.UserMessage, ex.ToString(), ex.IsRetriable);
            ct.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            await HandleJobError(db, job, "Обробку було скасовано", null, false);
            throw; // Let the worker distinguish timeout from shutdown.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error processing job: {JobId}", jobId);
            await HandleJobError(db, job, "Неочікувана помилка при обробці", ex.ToString(), false);
            ct.ThrowIfCancellationRequested();
        }
    }

    private async Task<ParseResult> ProcessQhub(
        IServiceScope scope,
        string inputPath,
        string assetsFolder,
        string outputFolder,
        QuestionsHubDbContext db,
        PackageImportJob job,
        CancellationToken ct)
    {
        var qhubExtractor = scope.ServiceProvider.GetRequiredService<QhubExtractor>();

        await UpdateProgress(db, job, "Extracting", 30, ct);

        var parseResult = await qhubExtractor.Extract(inputPath, assetsFolder, ct);

        // Save parse result for debugging
        var outputJsonPath = Path.Combine(outputFolder, "package_import.json");
        await File.WriteAllTextAsync(outputJsonPath, JsonSerializer.Serialize(parseResult, JsonOptions), ct);

        return parseResult;
    }

    private async Task<ParseResult> ProcessDocx(
        IServiceScope scope,
        string inputPath,
        string assetsFolder,
        string workingFolder,
        string outputFolder,
        QuestionsHubDbContext db,
        PackageImportJob job,
        CancellationToken ct)
    {
        var extractor = scope.ServiceProvider.GetRequiredService<DocxExtractor>();

        // Step 1: Extract content from DOCX
        await UpdateProgress(db, job, "Extracting", 20, ct);

        var extractionResult = await extractor.Extract(inputPath, assetsFolder, ct);

        // Save extraction result for debugging
        var extractedJsonPath = Path.Combine(workingFolder, "extracted.json");
        await File.WriteAllTextAsync(extractedJsonPath, JsonSerializer.Serialize(extractionResult, JsonOptions), ct);

        // Step 2: Parse structure with the parser matching the game type chosen at upload
        await UpdateProgress(db, job, "Parsing", 50, ct);

        var parseResult = job.Type == PackageType.Shvager
            ? scope.ServiceProvider.GetRequiredService<ShvagerParser>()
                .Parse(extractionResult.Blocks, extractionResult.Assets)
            : scope.ServiceProvider.GetRequiredService<PackageParser>()
                .Parse(extractionResult.Blocks, extractionResult.Assets);

        // Merge extraction warnings into parse result (so they're saved with the job)
        if (extractionResult.Warnings.Count > 0)
        {
            parseResult.Warnings.InsertRange(0, extractionResult.Warnings);
        }

        // Save parse result for debugging
        var outputJsonPath = Path.Combine(outputFolder, "package_import.json");
        await File.WriteAllTextAsync(outputJsonPath, JsonSerializer.Serialize(parseResult, JsonOptions), ct);

        return parseResult;
    }

    private async Task UpdateProgress(
        QuestionsHubDbContext db,
        PackageImportJob job,
        string step,
        int progress,
        CancellationToken ct)
    {
        job.CurrentStep = step;
        job.Progress = progress;
        await db.SaveChangesAsync(ct);
        _logger.LogDebug("Job {JobId}: {Step} ({Progress}%)", job.Id, step, progress);
    }

    private async Task HandleJobError(
        QuestionsHubDbContext db,
        PackageImportJob job,
        string errorMessage,
        string? errorDetails,
        bool isRetriable)
    {
        // Failure persistence must survive cancellation, including cancellation racing with another error.
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            // Detach all tracked entities except the job itself to avoid
            // re-saving invalid entities from a failed import transaction.
            foreach (var entry in db.ChangeTracker.Entries().ToList())
            {
                if (!ReferenceEquals(entry.Entity, job))
                    entry.State = Microsoft.EntityFrameworkCore.EntityState.Detached;
            }

            ImportJobLifecycle.SetError(job, errorMessage, errorDetails);

            if (isRetriable && job.Attempts < _options.MaxRetryAttempts)
            {
                // Schedule retry
                var delay = GetRetryDelay(job.Attempts);
                job.NextRetryAt = DateTime.UtcNow + delay;
                job.FinishedAt = null;
                job.Status = ImportJobStatus.Failed;
                _logger.LogWarning(
                    "Job {JobId} failed (attempt {Attempt}/{Max}), will retry in {Delay}",
                    job.Id, job.Attempts, _options.MaxRetryAttempts, delay);
            }
            else
            {
                ImportJobLifecycle.Fail(job, errorMessage);
                _logger.LogError("Job {JobId} failed permanently: {Error}", job.Id, errorMessage);
            }

            await db.SaveChangesAsync(cleanup.Token);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save error status for job {JobId}: {Error}", job.Id, errorMessage);
        }
    }

    private static TimeSpan GetRetryDelay(int attempt)
    {
        return attempt switch
        {
            1 => TimeSpan.FromSeconds(30),
            2 => TimeSpan.FromMinutes(2),
            _ => TimeSpan.FromMinutes(5)
        };
    }

    private async Task SaveOriginalPackageFile(PackageImportJob job, int packageId, CancellationToken ct)
    {
        try
        {
            var inputPath = Path.Combine(_mediaOptions.UploadsPath, job.InputFilePath);
            var packagesFolder = Path.Combine(_mediaOptions.UploadsPath, _mediaOptions.PackagesFolder, packageId.ToString(CultureInfo.InvariantCulture));
            Directory.CreateDirectory(packagesFolder);

            var extension = Path.GetExtension(job.InputFileName);
            var destPath = Path.Combine(packagesFolder, $"original{extension}");

            await using var source = File.OpenRead(inputPath);
            await using var dest = File.Create(destPath);
            await source.CopyToAsync(dest, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save original package file");
            // Non-fatal error
        }
    }

    private string GetJobFolder(Guid jobId)
    {
        return Path.Combine(_mediaOptions.UploadsPath, _options.JobsFolder, jobId.ToString());
    }

    public bool IsRetryPending(PackageImportJob job) => ImportJobLifecycle.IsRetryPending(job, _options.MaxRetryAttempts);

    public bool IsUnfinished(PackageImportJob job) => ImportJobLifecycle.IsUnfinished(job, _options.MaxRetryAttempts);

    /// <summary>One of the user's jobs, or null (another user's job included).</summary>
    public async Task<PackageImportJob?> GetJobForUser(string userId, Guid jobId, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuestionsHubDbContext>();
        return await db.PackageImportJobs.AsNoTracking().SingleOrDefaultAsync(j => j.Id == jobId && j.OwnerId == userId, ct);
    }

    /// <summary>The user's queued, running and retry-pending jobs.</summary>
    public async Task<int> CountUnfinishedJobsForUser(string userId, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuestionsHubDbContext>();
        return await db.PackageImportJobs.Where(j => j.OwnerId == userId)
            .Where(ImportJobLifecycle.Unfinished(_options.MaxRetryAttempts)).CountAsync(ct);
    }

    /// <summary>Latest 20 owned jobs (the agent API's import list).</summary>
    public async Task<List<PackageImportJob>> GetRecentJobsForUser(string userId, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuestionsHubDbContext>();
        return await db.PackageImportJobs.AsNoTracking().Where(j => j.OwnerId == userId)
            .OrderByDescending(j => j.CreatedAt).ThenByDescending(j => j.Id).Take(20).ToListAsync(ct);
    }

    /// <summary>
    /// Gets active (in-progress) import jobs for a user.
    /// </summary>
    public async Task<List<PackageImportJob>> GetActiveJobsForUser(string userId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuestionsHubDbContext>();

        return await db.PackageImportJobs.Where(j => j.OwnerId == userId).Where(ImportJobLifecycle.Unfinished(_options.MaxRetryAttempts))
            .OrderByDescending(j => j.CreatedAt)
            .ToListAsync();
    }

    /// <summary>
    /// Gets all active (in-progress) import jobs (for admins).
    /// </summary>
    public async Task<List<PackageImportJob>> GetActiveJobs()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuestionsHubDbContext>();

        return await db.PackageImportJobs.Where(ImportJobLifecycle.Unfinished(_options.MaxRetryAttempts))
            .OrderByDescending(j => j.CreatedAt)
            .ToListAsync();
    }

    /// <summary>
    /// Gets import jobs by their IDs.
    /// </summary>
    public async Task<List<PackageImportJob>> GetJobsByIds(List<Guid> jobIds)
    {
        if (jobIds.Count == 0)
            return [];

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuestionsHubDbContext>();

        return await db.PackageImportJobs
            .Where(j => jobIds.Contains(j.Id))
            .OrderByDescending(j => j.CreatedAt)
            .ToListAsync();
    }
}
