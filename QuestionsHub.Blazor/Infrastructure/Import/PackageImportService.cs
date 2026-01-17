using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using QuestionsHub.Blazor.Data;
using QuestionsHub.Blazor.Domain;

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
        PackageImportOptions options,
        MediaUploadOptions mediaOptions,
        ILogger<PackageImportService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _mediaOptions = mediaOptions;
        _logger = logger;
    }

    /// <summary>
    /// Creates a new import job for the given file.
    /// </summary>
    public async Task<PackageImportJob> Enqueue(
        string ownerId,
        string fileName,
        Stream fileStream,
        long fileSize)
    {
        // Validate
        if (!_options.IsExtensionAllowed(fileName))
        {
            throw new ValidationException(
                $"Непідтримуваний формат файлу. Дозволені: {string.Join(", ", _options.AllowedExtensions)}");
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

        // Save file
        await using (var output = File.Create(inputFilePath))
        {
            await fileStream.CopyToAsync(output);
        }

        // Create job record
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuestionsHubDbContext>();

        var job = new PackageImportJob
        {
            Id = jobId,
            OwnerId = ownerId,
            CreatedAt = DateTime.UtcNow,
            Status = ImportJobStatus.Queued,
            InputFileName = fileName,
            InputFilePath = relativeInputPath,
            InputFileSizeBytes = fileSize
        };

        db.PackageImportJobs.Add(job);
        await db.SaveChangesAsync();

        _logger.LogInformation("Created import job: {JobId} for file: {FileName}", jobId, fileName);

        return job;
    }

    /// <summary>
    /// Processes an import job through all steps.
    /// </summary>
    public async Task Process(Guid jobId, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuestionsHubDbContext>();
        var extractor = scope.ServiceProvider.GetRequiredService<DocxExtractor>();
        var parser = scope.ServiceProvider.GetRequiredService<PackageParser>();
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
            var workingFolder = Path.Combine(jobFolder, "working");
            var assetsFolder = Path.Combine(jobFolder, "assets");
            var outputFolder = Path.Combine(jobFolder, "output");

            Directory.CreateDirectory(workingFolder);
            Directory.CreateDirectory(assetsFolder);
            Directory.CreateDirectory(outputFolder);

            var inputPath = Path.Combine(_mediaOptions.UploadsPath, job.InputFilePath);
            var docxPath = inputPath;

            // Step 1: Extract content from DOCX
            await UpdateProgress(db, job, "Extracting", 20, ct);

            var extractionResult = await extractor.Extract(docxPath, assetsFolder, ct);

            // Save extraction result for debugging
            var extractedJsonPath = Path.Combine(workingFolder, "extracted.json");
            await File.WriteAllTextAsync(extractedJsonPath, JsonSerializer.Serialize(extractionResult, JsonOptions), ct);

            // Step 3: Parse structure
            await UpdateProgress(db, job, "Parsing", 50, ct);

            var parseResult = parser.Parse(extractionResult.Blocks, extractionResult.Assets);

            // Save parse result for debugging
            var outputJsonPath = Path.Combine(outputFolder, "package_import.json");
            await File.WriteAllTextAsync(outputJsonPath, JsonSerializer.Serialize(parseResult, JsonOptions), ct);

            // Check if we got valid structure
            if (parseResult.Tours.Count == 0 || parseResult.TotalQuestions == 0)
            {
                throw new ParsingException("Не вдалося визначити структуру пакету. Перевірте формат документа.");
            }

            // Step 4: LLM normalization (skip in MVP, use rules-only)
            // TODO: Implement LLM fallback when confidence is low

            // Step 5: Import to database
            await UpdateProgress(db, job, "Importing", 70, ct);

            var package = await importer.Import(parseResult, job.OwnerId, jobId, assetsFolder, ct);

            // Step 6: Finalize
            await UpdateProgress(db, job, "Finalizing", 90, ct);

            // Save original file to packages folder
            await SaveOriginalPackageFile(job, package.Id, ct);

            // Save warnings
            if (parseResult.Warnings.Count > 0)
            {
                job.WarningsJson = JsonSerializer.Serialize(parseResult.Warnings);
            }

            // Mark success
            job.PackageId = package.Id;
            job.Status = ImportJobStatus.Succeeded;
            job.FinishedAt = DateTime.UtcNow;
            job.CurrentStep = null;
            job.Progress = 100;

            await db.SaveChangesAsync(ct);

            _logger.LogInformation("Job completed successfully: {JobId}, Package: {PackageId}", jobId, package.Id);
        }
        catch (ImportException ex)
        {
            await HandleJobError(db, job, ex.UserMessage, ex.ToString(), ex.IsRetriable, ct);
        }
        catch (OperationCanceledException)
        {
            await HandleJobError(db, job, "Обробку було скасовано", null, false, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error processing job: {JobId}", jobId);
            await HandleJobError(db, job, "Неочікувана помилка при обробці", ex.ToString(), false, ct);
        }
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
        bool isRetriable,
        CancellationToken ct)
    {
        job.ErrorMessage = errorMessage;
        job.ErrorDetails = errorDetails;

        if (isRetriable && job.Attempts < _options.MaxRetryAttempts)
        {
            // Schedule retry
            var delay = GetRetryDelay(job.Attempts);
            job.NextRetryAt = DateTime.UtcNow + delay;
            job.Status = ImportJobStatus.Failed;
            _logger.LogWarning(
                "Job {JobId} failed (attempt {Attempt}/{Max}), will retry in {Delay}",
                job.Id, job.Attempts, _options.MaxRetryAttempts, delay);
        }
        else
        {
            job.Status = ImportJobStatus.Failed;
            job.FinishedAt = DateTime.UtcNow;
            job.NextRetryAt = null;
            _logger.LogError("Job {JobId} failed permanently: {Error}", job.Id, errorMessage);
        }

        await db.SaveChangesAsync(ct);
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

    /// <summary>
    /// Gets import jobs for a user.
    /// </summary>
    public async Task<List<PackageImportJob>> GetJobsForUser(string userId, int limit = 10)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuestionsHubDbContext>();

        return await db.PackageImportJobs
            .Where(j => j.OwnerId == userId)
            .OrderByDescending(j => j.CreatedAt)
            .Take(limit)
            .ToListAsync();
    }

    /// <summary>
    /// Gets all import jobs (for admins).
    /// </summary>
    public async Task<List<PackageImportJob>> GetAllJobs(int limit = 50)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuestionsHubDbContext>();

        return await db.PackageImportJobs
            .OrderByDescending(j => j.CreatedAt)
            .Take(limit)
            .ToListAsync();
    }
}

