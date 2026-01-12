namespace QuestionsHub.Blazor.Infrastructure.Import;

/// <summary>
/// Base exception for package import errors.
/// </summary>
public abstract class ImportException : Exception
{
    /// <summary>
    /// Whether the error is retriable (e.g., timeout, network error).
    /// </summary>
    public bool IsRetriable { get; }

    /// <summary>
    /// User-friendly error message in Ukrainian.
    /// </summary>
    public string UserMessage { get; }

    protected ImportException(string userMessage, string? technicalDetails = null,
        Exception? inner = null, bool isRetriable = false)
        : base(technicalDetails ?? userMessage, inner)
    {
        UserMessage = userMessage;
        IsRetriable = isRetriable;
    }
}

/// <summary>
/// Exception thrown when file validation fails.
/// </summary>
public class ValidationException : ImportException
{
    public ValidationException(string message)
        : base(message, isRetriable: false) { }
}


/// <summary>
/// Exception thrown when DOCX extraction fails.
/// </summary>
public class ExtractionException : ImportException
{
    public ExtractionException(string message, Exception? inner = null)
        : base(message, inner?.Message, inner, isRetriable: false) { }
}

/// <summary>
/// Exception thrown when package structure parsing fails.
/// </summary>
public class ParsingException : ImportException
{
    public ParsingException(string message, Exception? inner = null)
        : base(message, inner?.Message, inner, isRetriable: false) { }
}

/// <summary>
/// Exception thrown when LLM normalization fails.
/// </summary>
public class LLMException : ImportException
{
    public LLMException(string message, Exception? inner = null, bool isRetriable = true)
        : base(message, inner?.Message, inner, isRetriable) { }
}

/// <summary>
/// Exception thrown when database import fails.
/// </summary>
public class DatabaseImportException : ImportException
{
    public DatabaseImportException(string message, Exception? inner = null)
        : base(message, inner?.Message, inner, isRetriable: false) { }
}

