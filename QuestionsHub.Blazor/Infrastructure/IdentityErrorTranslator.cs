namespace QuestionsHub.Blazor.Infrastructure;

/// <summary>
/// Helper class for translating Identity error codes to Ukrainian messages.
/// </summary>
public static class IdentityErrorTranslator
{
    /// <summary>
    /// Translates an Identity error code to a Ukrainian message.
    /// </summary>
    public static string TranslateErrorCode(string code, string? defaultDescription = null)
    {
        return code switch
        {
            // Password errors
            "PasswordTooShort" => "Пароль занадто короткий (мінімум 8 символів).",
            "PasswordRequiresDigit" => "Пароль повинен містити цифру.",
            "PasswordRequiresLower" => "Пароль повинен містити малу літеру.",
            "PasswordRequiresUpper" => "Пароль повинен містити велику літеру.",
            "PasswordRequiresNonAlphanumeric" => "Пароль повинен містити спеціальний символ.",
            "PasswordRequiresUniqueChars" => "Пароль повинен містити більше унікальних символів.",
            "PasswordMismatch" => "Паролі не співпадають.",

            // Token errors
            "InvalidToken" => "Посилання недійсне або застаріло.",

            // User errors
            "DuplicateUserName" => "Користувач з таким email вже існує.",
            "DuplicateEmail" => "Користувач з таким email вже існує.",
            "InvalidEmail" => "Невірний формат email.",
            "InvalidUserName" => "Невірне ім'я користувача.",
            "UserNotFound" => "Користувача не знайдено.",

            // Lockout
            "UserLockedOut" => "Обліковий запис заблоковано. Спробуйте пізніше.",

            // Custom validation errors (used in AuthController)
            "FirstNameRequired" => "Ім'я обов'язкове.",
            "LastNameRequired" => "Прізвище обов'язкове.",
            "EmailRequired" => "Email обов'язковий.",
            "PasswordRequired" => "Пароль обов'язковий.",

            // Default
            _ => defaultDescription ?? $"Помилка: {code}"
        };
    }

    /// <summary>
    /// Translates multiple Identity error codes to a single Ukrainian message string.
    /// </summary>
    public static string TranslateErrors(IEnumerable<Microsoft.AspNetCore.Identity.IdentityError> errors)
    {
        var messages = errors
            .Select(e => TranslateErrorCode(e.Code, e.Description))
            .Distinct();
        return string.Join(" ", messages);
    }

    /// <summary>
    /// Translates comma-separated error codes to a single Ukrainian message string.
    /// Used for error codes passed via query string.
    /// </summary>
    public static string TranslateErrorCodes(string commaSeparatedCodes)
    {
        var codes = commaSeparatedCodes.Split(',', StringSplitOptions.RemoveEmptyEntries);
        var messages = codes
            .Select(code => TranslateErrorCode(code.Trim()))
            .Distinct();
        return string.Join(" ", messages);
    }
}

