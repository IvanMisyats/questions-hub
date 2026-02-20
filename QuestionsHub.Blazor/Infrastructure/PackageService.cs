using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using QuestionsHub.Blazor.Data;
using QuestionsHub.Blazor.Infrastructure.Auth;
using QuestionsHub.Blazor.Infrastructure.Media;

namespace QuestionsHub.Blazor.Infrastructure;

/// <summary>
/// Service for package operations including deletion with artifact cleanup.
/// </summary>
public class PackageService
{
    private readonly IDbContextFactory<QuestionsHubDbContext> _dbContextFactory;
    private readonly MediaUploadOptions _mediaOptions;
    private readonly ILogger<PackageService> _logger;
    private readonly AccessControlService _accessControl;

    public PackageService(
        IDbContextFactory<QuestionsHubDbContext> dbContextFactory,
        IOptions<MediaUploadOptions> mediaOptions,
        ILogger<PackageService> logger,
        AccessControlService accessControl)
    {
        _dbContextFactory = dbContextFactory;
        _mediaOptions = mediaOptions.Value;
        _logger = logger;
        _accessControl = accessControl;
    }

    /// <summary>
    /// Deletes a package and all its associated artifacts (images, files, import job).
    /// </summary>
    /// <param name="packageId">The ID of the package to delete.</param>
    /// <returns>Result indicating success or failure with details.</returns>
    public async Task<DeleteResult> DeletePackage(int packageId)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync();

        var package = await context.Packages
            .Include(p => p.Tours)
                .ThenInclude(t => t.Questions)
            .FirstOrDefaultAsync(p => p.Id == packageId);

        if (package == null)
        {
            return new DeleteResult(false, "Пакет не знайдено.");
        }

        // Check authorization using AccessControlService
        if (!await _accessControl.CanDeletePackage(package))
        {
            return new DeleteResult(false, "Ви не маєте прав на видалення цього пакету.");
        }

        // Collect all asset URLs to delete
        var assetUrls = new List<string>();
        foreach (var tour in package.Tours)
        {
            foreach (var question in tour.Questions)
            {
                if (!string.IsNullOrEmpty(question.HandoutUrl))
                    assetUrls.Add(question.HandoutUrl);
                if (!string.IsNullOrEmpty(question.CommentAttachmentUrl))
                    assetUrls.Add(question.CommentAttachmentUrl);
            }
        }

        _logger.LogInformation(
            "Deleting package {PackageId} with {AssetCount} assets",
            packageId, assetUrls.Count);

        // Delete assets from filesystem
        var deletedAssets = 0;
        var handoutsPath = Path.Combine(_mediaOptions.UploadsPath, _mediaOptions.HandoutsFolder);

        foreach (var url in assetUrls.Distinct())
        {
            // URL format: /media/{filename}
            var fileName = Path.GetFileName(url);
            if (string.IsNullOrEmpty(fileName)) continue;

            var filePath = Path.Combine(handoutsPath, fileName);

            try
            {
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                    deletedAssets++;
                    _logger.LogDebug("Deleted asset: {FilePath}", filePath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete asset: {FilePath}", filePath);
                // Continue with other assets even if one fails
            }
        }

        // Delete package folder if exists (for original uploaded files)
        var packageFolderPath = Path.Combine(_mediaOptions.UploadsPath, _mediaOptions.PackagesFolder, packageId.ToString(CultureInfo.InvariantCulture));
        try
        {
            if (Directory.Exists(packageFolderPath))
            {
                Directory.Delete(packageFolderPath, recursive: true);
                _logger.LogDebug("Deleted package folder: {FolderPath}", packageFolderPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete package folder: {FolderPath}", packageFolderPath);
        }

        // Find and delete associated import job
        var importJob = await context.PackageImportJobs
            .FirstOrDefaultAsync(j => j.PackageId == packageId);

        if (importJob != null)
        {
            // Delete job working folder
            await DeleteJobFolder(importJob.Id);
            context.PackageImportJobs.Remove(importJob);
            _logger.LogDebug("Deleted import job: {JobId}", importJob.Id);
        }

        // Delete package from database (cascades to tours and questions)
        context.Packages.Remove(package);
        await context.SaveChangesAsync();

        _logger.LogInformation(
            "Package {PackageId} deleted. Removed {DeletedAssets} assets.",
            packageId, deletedAssets);

        return new DeleteResult(true, null, deletedAssets);
    }

    /// <summary>
    /// Deletes an import job and its associated files.
    /// Only allows deletion of failed, cancelled, or orphaned jobs.
    /// </summary>
    /// <param name="jobId">The ID of the job to delete.</param>
    /// <param name="requestingUserId">The ID of the user requesting deletion.</param>
    /// <param name="isAdmin">Whether the requesting user is an admin.</param>
    public async Task<DeleteResult> DeleteImportJob(Guid jobId, string requestingUserId, bool isAdmin)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync();

        var job = await context.PackageImportJobs
            .FirstOrDefaultAsync(j => j.Id == jobId);

        if (job == null)
        {
            return new DeleteResult(false, "Завдання не знайдено.");
        }

        // Check authorization
        if (!isAdmin && job.OwnerId != requestingUserId)
        {
            return new DeleteResult(false, "Ви не маєте прав на видалення цього завдання.");
        }

        // Don't allow deleting jobs that are still running
        if (job.Status is Domain.ImportJobStatus.Queued or Domain.ImportJobStatus.Running)
        {
            return new DeleteResult(false, "Неможливо видалити завдання, яке ще виконується.");
        }

        // If the job created a package, don't allow deleting the job directly
        // (user should delete the package instead, which will also delete the job)
        if (job.PackageId != null)
        {
            var packageExists = await context.Packages.AnyAsync(p => p.Id == job.PackageId);
            if (packageExists)
            {
                return new DeleteResult(false, "Видаліть спочатку пакет, створений цим завданням.");
            }
        }

        _logger.LogInformation("Deleting import job {JobId}", jobId);

        // Delete job working folder
        await DeleteJobFolder(jobId);

        // Delete job from database
        context.PackageImportJobs.Remove(job);
        await context.SaveChangesAsync();

        _logger.LogInformation("Import job {JobId} deleted", jobId);

        return new DeleteResult(true, null);
    }

    private Task DeleteJobFolder(Guid jobId)
    {
        var jobsPath = Path.Combine(_mediaOptions.UploadsPath, "jobs", jobId.ToString());

        try
        {
            if (Directory.Exists(jobsPath))
            {
                Directory.Delete(jobsPath, recursive: true);
                _logger.LogDebug("Deleted job folder: {FolderPath}", jobsPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete job folder: {FolderPath}", jobsPath);
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// Result of a package or job delete operation.
/// </summary>
public record DeleteResult(bool Success, string? ErrorMessage, int DeletedAssets = 0);

