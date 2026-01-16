using System.Globalization;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using A = DocumentFormat.OpenXml.Drawing;

namespace QuestionsHub.Blazor.Infrastructure.Import;

/// <summary>
/// Extracts text blocks and images from DOCX documents.
/// </summary>
public class DocxExtractor
{
    private readonly ILogger<DocxExtractor> _logger;

    public DocxExtractor(ILogger<DocxExtractor> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Extracts content from a DOCX file.
    /// </summary>
    /// <param name="docxPath">Path to the DOCX file.</param>
    /// <param name="jobId">Job ID for naming extracted assets.</param>
    /// <param name="assetsOutputPath">Directory where images will be saved.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Extraction result with blocks, assets, and warnings.</returns>
    public async Task<ExtractionResult> Extract(
        string docxPath,
        Guid jobId,
        string assetsOutputPath,
        CancellationToken ct)
    {
        var blocks = new List<DocBlock>();
        var assets = new List<AssetReference>();
        var warnings = new List<string>();
        var imageIndex = 0;

        _logger.LogInformation("Extracting DOCX: {Path}", docxPath);

        try
        {
            // Ensure assets directory exists
            Directory.CreateDirectory(assetsOutputPath);

            using var doc = WordprocessingDocument.Open(docxPath, false);

            if (doc.MainDocumentPart?.Document?.Body == null)
            {
                throw new ExtractionException("Документ порожній або пошкоджений");
            }

            var body = doc.MainDocumentPart.Document.Body;
            var blockIndex = 0;

            foreach (var element in body.Elements())
            {
                ct.ThrowIfCancellationRequested();

                if (element is Paragraph para)
                {
                    var block = ExtractParagraph(para, blockIndex, doc.MainDocumentPart);

                    // Extract images from this paragraph
                    foreach (var drawing in para.Descendants<Drawing>())
                    {
                        var asset = await ExtractImage(
                            drawing,
                            doc.MainDocumentPart,
                            jobId,
                            ++imageIndex,
                            assetsOutputPath,
                            ct);

                        if (asset != null)
                        {
                            block.Assets.Add(asset);
                            assets.Add(asset);
                        }
                    }

                    // Only add non-empty blocks
                    if (!string.IsNullOrWhiteSpace(block.Text) || block.Assets.Count > 0)
                    {
                        blocks.Add(block);
                        blockIndex++;
                    }
                }
                else if (element is Table table)
                {
                    var tableBlock = ExtractTable(table, blockIndex);
                    if (!string.IsNullOrWhiteSpace(tableBlock.Text))
                    {
                        blocks.Add(tableBlock);
                        blockIndex++;
                    }
                }
            }

            _logger.LogInformation(
                "Extracted {BlockCount} blocks and {AssetCount} images from DOCX",
                blocks.Count, assets.Count);

            return new ExtractionResult(blocks, assets, warnings);
        }
        catch (OpenXmlPackageException ex)
        {
            _logger.LogError(ex, "Failed to open DOCX file");
            throw new ExtractionException("Файл пошкоджений або має невірний формат", ex);
        }
        catch (FileFormatException ex)
        {
            _logger.LogError(ex, "Invalid DOCX file format");
            throw new ExtractionException("Файл захищений паролем або пошкоджений", ex);
        }
        catch (InvalidDataException ex)
        {
            _logger.LogError(ex, "Invalid data in DOCX file");
            throw new ExtractionException("Файл пошкоджений або має невірний формат", ex);
        }
    }

    private static DocBlock ExtractParagraph(Paragraph para, int index, MainDocumentPart mainPart)
    {
        var text = GetParagraphText(para);

        // Check for list numbering and prepend it to text
        var listPrefix = GetListNumberingPrefix(para, mainPart);
        if (!string.IsNullOrEmpty(listPrefix))
        {
            text = listPrefix + text;
        }

        var styleId = para.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
        var (isBold, isItalic) = GetTextFormatting(para);
        var isHeading = IsHeadingStyle(styleId);
        var fontSizeHalfPoints = GetFontSizeHalfPoints(para, mainPart);

        return new DocBlock
        {
            Index = index,
            Text = text,
            StyleId = styleId,
            IsBold = isBold,
            IsItalic = isItalic,
            IsHeading = isHeading,
            FontSizeHalfPoints = fontSizeHalfPoints,
            Assets = []
        };
    }

    /// <summary>
    /// Gets the list numbering prefix for a paragraph if it's part of a numbered list.
    /// </summary>
    private static string? GetListNumberingPrefix(Paragraph para, MainDocumentPart mainPart)
    {
        var numProps = para.ParagraphProperties?.NumberingProperties;
        if (numProps == null) return null;

        var numId = numProps.NumberingId?.Val?.Value;
        var ilvl = numProps.NumberingLevelReference?.Val?.Value ?? 0;

        if (numId == null || numId == 0) return null;

        var numberingPart = mainPart.NumberingDefinitionsPart;
        if (numberingPart?.Numbering == null) return null;

        // Find the numbering instance
        var numInstance = numberingPart.Numbering.Elements<NumberingInstance>()
            .FirstOrDefault(n => n.NumberID?.Value == numId);
        if (numInstance == null) return null;

        var abstractNumId = numInstance.AbstractNumId?.Val?.Value;
        if (abstractNumId == null) return null;

        // Find the abstract numbering definition
        var abstractNum = numberingPart.Numbering.Elements<AbstractNum>()
            .FirstOrDefault(a => a.AbstractNumberId?.Value == abstractNumId);
        if (abstractNum == null) return null;

        // Find the level definition
        var level = abstractNum.Elements<Level>()
            .FirstOrDefault(l => l.LevelIndex?.Value == ilvl);
        if (level == null) return null;

        // Get the numbering format
        var numFmt = level.NumberingFormat?.Val?.Value ?? NumberFormatValues.Decimal;
        var lvlText = level.LevelText?.Val?.Value ?? "%1.";

        // Calculate the actual number (simplified - assumes sequential from 1)
        // For accurate numbering we'd need to track paragraph positions, but this is good enough
        // since we just need to preserve that there IS a number prefix
        var number = GetListItemNumber(para, numId.Value, ilvl, mainPart);

        // Format the number based on the format type
        var formattedNumber = FormatListNumber(number, numFmt);

        // Apply the level text pattern (e.g., "%1)" or "%1.")
        var prefix = lvlText.Replace($"%{ilvl + 1}", formattedNumber);

        // Add appropriate spacing
        return prefix + "\t";
    }

    private static int GetListItemNumber(Paragraph para, int numId, int ilvl, MainDocumentPart mainPart)
    {
        // Count paragraphs with the same numId and ilvl before this one
        var body = mainPart.Document?.Body;
        if (body == null) return 1;

        var count = 0;
        foreach (var p in body.Elements<Paragraph>())
        {
            if (p == para) break;

            var pNumProps = p.ParagraphProperties?.NumberingProperties;
            if (pNumProps == null) continue;

            var pNumId = pNumProps.NumberingId?.Val?.Value;
            var pIlvl = pNumProps.NumberingLevelReference?.Val?.Value ?? 0;

            if (pNumId == numId && pIlvl == ilvl)
            {
                count++;
            }
        }

        return count + 1;
    }

    private static string FormatListNumber(int number, NumberFormatValues? format)
    {
        if (format == null)
            return number.ToString(CultureInfo.InvariantCulture);

        if (format == NumberFormatValues.Decimal)
            return number.ToString(CultureInfo.InvariantCulture);
        if (format == NumberFormatValues.LowerLetter)
            return ((char)('a' + (number - 1) % 26)).ToString();
        if (format == NumberFormatValues.UpperLetter)
            return ((char)('A' + (number - 1) % 26)).ToString();
        if (format == NumberFormatValues.LowerRoman)
            return ToRoman(number).ToLowerInvariant();
        if (format == NumberFormatValues.UpperRoman)
            return ToRoman(number);

        return number.ToString(CultureInfo.InvariantCulture);
    }

    private static string ToRoman(int number)
    {
        if (number < 1 || number > 3999) return number.ToString(CultureInfo.InvariantCulture);

        var result = new System.Text.StringBuilder();
        var values = new[] { 1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1 };
        var numerals = new[] { "M", "CM", "D", "CD", "C", "XC", "L", "XL", "X", "IX", "V", "IV", "I" };

        for (var i = 0; i < values.Length; i++)
        {
            while (number >= values[i])
            {
                result.Append(numerals[i]);
                number -= values[i];
            }
        }

        return result.ToString();
    }

    private static DocBlock ExtractTable(Table table, int index)
    {
        var lines = new List<string>();

        foreach (var row in table.Elements<TableRow>())
        {
            var cells = new List<string>();
            foreach (var cell in row.Elements<TableCell>())
            {
                var cellText = string.Join(" ", cell.Elements<Paragraph>()
                    .Select(GetParagraphText)
                    .Where(t => !string.IsNullOrWhiteSpace(t)));
                cells.Add(cellText);
            }
            if (cells.Any(c => !string.IsNullOrWhiteSpace(c)))
            {
                lines.Add(string.Join(" | ", cells));
            }
        }

        return new DocBlock
        {
            Index = index,
            Text = string.Join("\n", lines),
            StyleId = null,
            IsBold = false,
            IsItalic = false,
            IsHeading = false,
            Assets = []
        };
    }

    private static string GetParagraphText(Paragraph para)
    {
        var texts = new List<string>();

        // Process all child elements including hyperlinks
        foreach (var child in para.ChildElements)
        {
            switch (child)
            {
                case Run run:
                    ExtractTextFromRun(run, texts);
                    break;
                case Hyperlink hyperlink:
                    // Extract text from runs inside the hyperlink
                    foreach (var run in hyperlink.Elements<Run>())
                    {
                        ExtractTextFromRun(run, texts);
                    }
                    break;
            }
        }

        return string.Join("", texts);
    }

    private static void ExtractTextFromRun(Run run, List<string> texts)
    {
        foreach (var child in run.ChildElements)
        {
            switch (child)
            {
                case Text text:
                    texts.Add(text.Text);
                    break;
                case Break:
                    texts.Add("\n");
                    break;
                case TabChar:
                    texts.Add("\t");
                    break;
            }
        }
    }


    private static (bool IsBold, bool IsItalic) GetTextFormatting(Paragraph para)
    {
        var runs = para.Elements<Run>().ToList();
        if (runs.Count == 0) return (false, false);

        // Check if majority of runs are bold/italic
        var boldCount = runs.Count(r => r.RunProperties?.Bold != null);
        var italicCount = runs.Count(r => r.RunProperties?.Italic != null);

        return (boldCount > runs.Count / 2, italicCount > runs.Count / 2);
    }

    /// <summary>
    /// Gets the font size in half-points from the paragraph.
    /// Checks run properties first, then style definitions.
    /// </summary>
    private static int? GetFontSizeHalfPoints(Paragraph para, MainDocumentPart mainPart)
    {
        // First, check run properties (most specific)
        var runs = para.Elements<Run>().ToList();
        if (runs.Count > 0)
        {
            // Get font size from the first run that has it
            foreach (var run in runs)
            {
                var runFontSize = run.RunProperties?.FontSize?.Val?.Value;
                if (runFontSize != null && int.TryParse(runFontSize, CultureInfo.InvariantCulture, out var size))
                {
                    return size;
                }
            }
        }

        // Check style definition
        var styleId = para.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
        if (!string.IsNullOrEmpty(styleId))
        {
            var styleFontSize = GetStyleFontSize(styleId, mainPart);
            if (styleFontSize.HasValue)
                return styleFontSize.Value;
        }

        return null;
    }

    /// <summary>
    /// Gets the font size from a style definition.
    /// </summary>
    private static int? GetStyleFontSize(string styleId, MainDocumentPart mainPart)
    {
        var stylesPart = mainPart.StyleDefinitionsPart;
        if (stylesPart?.Styles == null) return null;

        var style = stylesPart.Styles.Elements<Style>()
            .FirstOrDefault(s => s.StyleId?.Value == styleId);

        if (style?.StyleRunProperties?.FontSize?.Val?.Value != null)
        {
            if (int.TryParse(style.StyleRunProperties.FontSize.Val.Value, CultureInfo.InvariantCulture, out var size))
                return size;
        }

        // Check if this style is based on another style
        var basedOnStyleId = style?.BasedOn?.Val?.Value;
        if (!string.IsNullOrEmpty(basedOnStyleId))
        {
            return GetStyleFontSize(basedOnStyleId, mainPart);
        }

        return null;
    }

    private static bool IsHeadingStyle(string? styleId)
    {
        if (string.IsNullOrEmpty(styleId)) return false;

        return styleId.StartsWith("Heading", StringComparison.OrdinalIgnoreCase) ||
               styleId.StartsWith("Title", StringComparison.OrdinalIgnoreCase) ||
               styleId.Contains("Heading", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<AssetReference?> ExtractImage(
        Drawing drawing,
        MainDocumentPart mainPart,
        Guid jobId,
        int imageIndex,
        string outputPath,
        CancellationToken ct)
    {
        try
        {
            var blip = drawing.Descendants<A.Blip>().FirstOrDefault();
            if (blip?.Embed?.Value == null) return null;

            var part = mainPart.GetPartById(blip.Embed.Value);
            if (part is not ImagePart imagePart) return null;

            var extension = GetImageExtension(imagePart.ContentType);
            var fileName = $"{jobId:N}_img_{imageIndex:D3}{extension}";
            var filePath = Path.Combine(outputPath, fileName);

            await using var stream = imagePart.GetStream();
            await using var fileStream = File.Create(filePath);
            await stream.CopyToAsync(fileStream, ct);

            var fileInfo = new FileInfo(filePath);

            _logger.LogDebug("Extracted image: {FileName} ({Size} bytes)",
                fileName, fileInfo.Length);

            return new AssetReference
            {
                FileName = fileName,
                RelativeUrl = $"/media/{fileName}",
                ContentType = imagePart.ContentType,
                SizeBytes = fileInfo.Length
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to extract image at index {Index}", imageIndex);
            return null;
        }
    }

    private static string GetImageExtension(string contentType)
    {
        return contentType.ToLowerInvariant() switch
        {
            "image/png" => ".png",
            "image/jpeg" => ".jpeg",
            "image/jpg" => ".jpg",
            "image/gif" => ".gif",
            "image/webp" => ".webp",
            "image/bmp" => ".bmp",
            "image/tiff" => ".tiff",
            _ => ".png" // Default to PNG
        };
    }
}

