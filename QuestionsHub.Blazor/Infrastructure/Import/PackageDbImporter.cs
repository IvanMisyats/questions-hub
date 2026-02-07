using System.Text.RegularExpressions;

using QuestionsHub.Blazor.Data;
using QuestionsHub.Blazor.Domain;

namespace QuestionsHub.Blazor.Infrastructure.Import;

/// <summary>
/// Imports parsed package data into the database.
/// </summary>
public partial class PackageDbImporter
{
    private readonly QuestionsHubDbContext _db;
    private readonly MediaUploadOptions _mediaOptions;
    private readonly AuthorService _authorService;
    private readonly ILogger<PackageDbImporter> _logger;

    public PackageDbImporter(
        QuestionsHubDbContext db,
        MediaUploadOptions mediaOptions,
        AuthorService authorService,
        ILogger<PackageDbImporter> logger)
    {
        _db = db;
        _mediaOptions = mediaOptions;
        _authorService = authorService;
        _logger = logger;
    }

    /// <summary>
    /// Imports a parsed package structure into the database.
    /// </summary>
    /// <param name="parseResult">Parsed package data.</param>
    /// <param name="ownerId">ID of the user who owns the package.</param>
    /// <param name="jobId">Import job ID.</param>
    /// <param name="jobAssetsPath">Path to extracted assets.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Created package entity.</returns>
    public async Task<Package> Import(
        ParseResult parseResult,
        string ownerId,
        Guid jobId,
        string jobAssetsPath,
        CancellationToken ct)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(ct);

        try
        {
            _logger.LogInformation("Importing package: {Title}", parseResult.Title);

            // Create package
            var package = new Package
            {
                Title = parseResult.Title ?? "Імпортований пакет",
                Description = parseResult.Description,
                Preamble = parseResult.Preamble,
                Status = PackageStatus.Draft,
                OwnerId = ownerId,
                TotalQuestions = parseResult.TotalQuestions,
                NumberingMode = parseResult.NumberingMode
            };

            _db.Packages.Add(package);
            await _db.SaveChangesAsync(ct);

            // Create tours
            foreach (var tourDto in parseResult.Tours)
            {
                var tour = new Tour
                {
                    Number = tourDto.Number,
                    OrderIndex = tourDto.OrderIndex,
                    IsWarmup = tourDto.IsWarmup,
                    Preamble = tourDto.Preamble,
                    PackageId = package.Id,
                    Editors = await ResolveAuthors(tourDto.Editors, ct)
                };

                _db.Tours.Add(tour);
                await _db.SaveChangesAsync(ct);

                // If tour has blocks, import blocks with their questions
                if (tourDto.Blocks.Count > 0)
                {
                    // Use a single counter across all blocks so OrderIndex is globally sequential within the tour
                    var tourQuestionOrderIndex = 0;

                    foreach (var blockDto in tourDto.Blocks)
                    {
                        var block = new Block
                        {
                            Name = blockDto.Name,
                            OrderIndex = blockDto.OrderIndex,
                            Preamble = blockDto.Preamble,
                            TourId = tour.Id,
                            Editors = await ResolveAuthors(blockDto.Editors, ct)
                        };

                        _db.Blocks.Add(block);
                        await _db.SaveChangesAsync(ct);

                        // Create questions for this block
                        foreach (var questionDto in blockDto.Questions)
                        {
                            var question = await CreateQuestion(
                                questionDto, tourQuestionOrderIndex++, tour.Id, block.Id, jobAssetsPath, ct);
                            _db.Questions.Add(question);
                        }

                        await _db.SaveChangesAsync(ct);
                    }
                }
                else
                {
                    // No blocks - import questions directly to tour
                    var orderIndex = 0;
                    foreach (var questionDto in tourDto.Questions)
                    {
                        var question = await CreateQuestion(
                            questionDto, orderIndex++, tour.Id, null, jobAssetsPath, ct);
                        _db.Questions.Add(question);
                    }

                    await _db.SaveChangesAsync(ct);
                }
            }

            await transaction.CommitAsync(ct);

            _logger.LogInformation(
                "Package imported: ID={PackageId}, Tours={TourCount}, Questions={QuestionCount}",
                package.Id, parseResult.Tours.Count, parseResult.TotalQuestions);

            return package;
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(ct);
            _logger.LogError(ex, "Failed to import package");
            throw new DatabaseImportException("Не вдалося зберегти пакет в базу даних", ex);
        }
    }

    private async Task<Question> CreateQuestion(
        QuestionDto questionDto,
        int orderIndex,
        int tourId,
        int? blockId,
        string jobAssetsPath,
        CancellationToken ct)
    {
        return new Question
        {
            OrderIndex = orderIndex,
            Number = questionDto.Number,
            HostInstructions = TruncateIfNeeded(questionDto.HostInstructions, 1000),
            Text = questionDto.Text,
            HandoutText = questionDto.HandoutText,
            HandoutUrl = await ResolveAssetUrl(questionDto.HandoutAssetFileName, jobAssetsPath, ct),
            Answer = TruncateIfNeeded(questionDto.Answer, 1000) ?? "",
            AcceptedAnswers = TruncateIfNeeded(questionDto.AcceptedAnswers, 1000),
            RejectedAnswers = TruncateIfNeeded(questionDto.RejectedAnswers, 1000),
            Comment = questionDto.Comment,
            CommentAttachmentUrl = await ResolveAssetUrl(questionDto.CommentAssetFileName, jobAssetsPath, ct),
            Source = questionDto.Source,
            TourId = tourId,
            BlockId = blockId,
            Authors = await ResolveAuthors(questionDto.Authors, ct)
        };
    }

    private async Task<List<Author>> ResolveAuthors(List<string> authorNames, CancellationToken ct)
    {
        var authors = new List<Author>();

        foreach (var name in authorNames.Distinct())
        {
            if (string.IsNullOrWhiteSpace(name)) continue;

            var (firstName, lastName) = ParseAuthorName(name);
            if (firstName == null || lastName == null)
            {
                _logger.LogWarning("Invalid author name format, skipping: {Name}", name);
                continue;
            }

            var author = await _authorService.GetOrCreateAuthor(_db, firstName, lastName);
            authors.Add(author);
            _logger.LogDebug("Resolved author: {FirstName} {LastName}", firstName, lastName);
        }

        return authors;
    }

    [GeneratedRegex(@"\s*\([^)]+\)\s*$")]
    private static partial Regex CityInParenthesesRegex();

    /// <summary>
    /// Parses and validates author name.
    /// Author name must consist of exactly two words (first name and surname).
    /// City in parentheses and trailing dot are optional.
    /// </summary>
    /// <returns>Tuple of (FirstName, LastName) or (null, null) if invalid.</returns>
    internal static (string? FirstName, string? LastName) ParseAuthorName(string fullName)
    {
        // Remove city in parentheses: "Ім'я Прізвище (Київ)" -> "Ім'я Прізвище"
        fullName = CityInParenthesesRegex().Replace(fullName, "").Trim();

        // Remove trailing punctuation
        fullName = fullName.TrimEnd('.', ',', ';');

        var parts = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        // Author name must be exactly two words
        if (parts.Length != 2)
            return (null, null);

        var firstName = parts[0];
        var lastName = parts[1];

        // Validate each part: must be a single word without spaces
        // (already guaranteed by split, but checking for edge cases with special characters)
        if (!IsValidNamePart(firstName) || !IsValidNamePart(lastName))
            return (null, null);

        return (firstName, lastName);
    }

    /// <summary>
    /// Validates a name part (first name or last name).
    /// Must be a non-empty word without digits and common sentence characters.
    /// </summary>
    private static bool IsValidNamePart(string namePart)
    {
        if (string.IsNullOrWhiteSpace(namePart))
            return false;

        // Name should not contain digits
        if (namePart.Any(char.IsDigit))
            return false;

        // Name should not contain typical sentence punctuation (except apostrophe for Ukrainian names like О'Ніл)
        if (namePart.Any(c => c is ':' or '?' or '!' or '"' or '«' or '»'))
            return false;

        // Name should start with a letter (uppercase preferred but not enforced)
        if (!char.IsLetter(namePart[0]))
            return false;

        return true;
    }

    private async Task<string?> ResolveAssetUrl(
        string? assetFileName,
        string jobAssetsPath,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(assetFileName)) return null;

        var sourcePath = Path.Combine(jobAssetsPath, assetFileName);
        if (!File.Exists(sourcePath))
        {
            _logger.LogWarning("Asset file not found: {Path}", sourcePath);
            return null;
        }

        // Move asset to handouts folder
        var handoutsPath = Path.Combine(_mediaOptions.UploadsPath, _mediaOptions.HandoutsFolder);
        Directory.CreateDirectory(handoutsPath);

        var destPath = Path.Combine(handoutsPath, assetFileName);

        try
        {
            // Copy instead of move to allow retries
            await using var source = File.OpenRead(sourcePath);
            await using var dest = File.Create(destPath);
            await source.CopyToAsync(dest, ct);

            return $"/media/{assetFileName}";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to copy asset: {FileName}", assetFileName);
            return null;
        }
    }

    private static string? TruncateIfNeeded(string? text, int maxLength)
    {
        if (string.IsNullOrEmpty(text)) return text;
        return text.Length <= maxLength ? text : text[..maxLength];
    }
}

