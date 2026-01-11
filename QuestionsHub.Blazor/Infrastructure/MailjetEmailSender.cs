using Mailjet.Client;
using Mailjet.Client.TransactionalEmails;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using QuestionsHub.Blazor.Domain;

namespace QuestionsHub.Blazor.Infrastructure;

/// <summary>
/// Email sender implementation using Mailjet API.
/// Implements IEmailSender for ApplicationUser for ASP.NET Core Identity integration.
/// </summary>
public class MailjetEmailSender : IEmailSender<ApplicationUser>
{
    private readonly EmailSettings _settings;
    private readonly ILogger<MailjetEmailSender> _logger;

    public MailjetEmailSender(IOptions<EmailSettings> settings, ILogger<MailjetEmailSender> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    /// <summary>
    /// Sends an email confirmation link to the user.
    /// </summary>
    public async Task SendConfirmationLinkAsync(ApplicationUser user, string email, string confirmationLink)
    {
        var subject = "Підтвердження електронної пошти - База запитань ЩДК";
        var htmlBody = $"""
            <html>
            <body style="font-family: Arial, sans-serif; line-height: 1.6; color: #333;">
                <div style="max-width: 600px; margin: 0 auto; padding: 20px;">
                    <h2 style="color: #0d6efd;">Вітаємо, {user.FirstName}!</h2>
                    <p>Дякуємо за реєстрацію на сайті «База українських запитань ЩДК».</p>
                    <p>Для підтвердження вашої електронної адреси натисніть на кнопку нижче:</p>
                    <p style="text-align: center; margin: 30px 0;">
                        <a href="{confirmationLink}"
                           style="background-color: #0d6efd; color: white; padding: 12px 30px;
                                  text-decoration: none; border-radius: 5px; display: inline-block;">
                            Підтвердити email
                        </a>
                    </p>
                    <p>Або скопіюйте це посилання у браузер:</p>
                    <p style="word-break: break-all; color: #666; font-size: 14px;">{confirmationLink}</p>
                    <hr style="border: none; border-top: 1px solid #eee; margin: 30px 0;">
                    <p style="color: #999; font-size: 12px;">
                        Якщо ви не реєструвались на нашому сайті, просто проігноруйте цей лист.
                    </p>
                    <p style="color: #999; font-size: 12px;">
                        База українських запитань «Що? Де? Коли?»<br>
                        <a href="{_settings.SiteUrl}" style="color: #0d6efd;">{_settings.SiteUrl}</a>
                    </p>
                </div>
            </body>
            </html>
            """;

        await SendEmailAsync(email, subject, htmlBody);
    }

    /// <summary>
    /// Sends a password reset link to the user.
    /// </summary>
    public async Task SendPasswordResetLinkAsync(ApplicationUser user, string email, string resetLink)
    {
        var subject = "Скидання пароля - База запитань ЩДК";
        var htmlBody = $"""
            <html>
            <body style="font-family: Arial, sans-serif; line-height: 1.6; color: #333;">
                <div style="max-width: 600px; margin: 0 auto; padding: 20px;">
                    <h2 style="color: #0d6efd;">Скидання пароля</h2>
                    <p>Вітаємо, {user.FirstName}!</p>
                    <p>Ви отримали цей лист, тому що запросили скидання пароля для вашого облікового запису.</p>
                    <p>Для встановлення нового пароля натисніть на кнопку нижче:</p>
                    <p style="text-align: center; margin: 30px 0;">
                        <a href="{resetLink}"
                           style="background-color: #0d6efd; color: white; padding: 12px 30px;
                                  text-decoration: none; border-radius: 5px; display: inline-block;">
                            Скинути пароль
                        </a>
                    </p>
                    <p>Або скопіюйте це посилання у браузер:</p>
                    <p style="word-break: break-all; color: #666; font-size: 14px;">{resetLink}</p>
                    <p style="color: #dc3545; font-weight: bold;">
                        ⚠️ Посилання дійсне протягом 24 годин.
                    </p>
                    <hr style="border: none; border-top: 1px solid #eee; margin: 30px 0;">
                    <p style="color: #999; font-size: 12px;">
                        Якщо ви не запитували скидання пароля, просто проігноруйте цей лист.
                        Ваш пароль залишиться без змін.
                    </p>
                    <p style="color: #999; font-size: 12px;">
                        База українських запитань «Що? Де? Коли?»<br>
                        <a href="{_settings.SiteUrl}" style="color: #0d6efd;">{_settings.SiteUrl}</a>
                    </p>
                </div>
            </body>
            </html>
            """;

        await SendEmailAsync(email, subject, htmlBody);
    }

    /// <summary>
    /// Sends a password reset code to the user (alternative to link).
    /// Not typically used, but required by IEmailSender interface.
    /// </summary>
    public async Task SendPasswordResetCodeAsync(ApplicationUser user, string email, string resetCode)
    {
        var subject = "Код скидання пароля - База запитань ЩДК";
        var htmlBody = $"""
            <html>
            <body style="font-family: Arial, sans-serif; line-height: 1.6; color: #333;">
                <div style="max-width: 600px; margin: 0 auto; padding: 20px;">
                    <h2 style="color: #0d6efd;">Код скидання пароля</h2>
                    <p>Вітаємо, {user.FirstName}!</p>
                    <p>Ваш код для скидання пароля:</p>
                    <p style="text-align: center; margin: 30px 0;">
                        <span style="background-color: #f8f9fa; padding: 15px 30px;
                                     font-size: 24px; font-family: monospace; letter-spacing: 3px;
                                     border: 1px solid #dee2e6; border-radius: 5px;">
                            {resetCode}
                        </span>
                    </p>
                    <p style="color: #dc3545; font-weight: bold;">
                        ⚠️ Код дійсний протягом 24 годин.
                    </p>
                    <hr style="border: none; border-top: 1px solid #eee; margin: 30px 0;">
                    <p style="color: #999; font-size: 12px;">
                        Якщо ви не запитували скидання пароля, просто проігноруйте цей лист.
                    </p>
                </div>
            </body>
            </html>
            """;

        await SendEmailAsync(email, subject, htmlBody);
    }

    /// <summary>
    /// Sends an email using Mailjet API.
    /// </summary>
    private async Task SendEmailAsync(string toEmail, string subject, string htmlBody)
    {
        try
        {
            var client = new MailjetClient(_settings.ApiKey, _settings.ApiSecret);

            var email = new TransactionalEmailBuilder()
                .WithFrom(new SendContact(_settings.SenderEmail, _settings.SenderName))
                .WithTo(new SendContact(toEmail))
                .WithSubject(subject)
                .WithHtmlPart(htmlBody)
                .Build();

            var response = await client.SendTransactionalEmailAsync(email);

            if (response.Messages.Length > 0 && response.Messages[0].Status == "success")
            {
                _logger.LogInformation("Email sent successfully to {Email}", toEmail);
            }
            else
            {
                var errorMessage = response.Messages.Length > 0
                    ? string.Join(", ", response.Messages[0].Errors?.Select(e => e.ErrorMessage) ?? [])
                    : "Unknown error";
                _logger.LogError("Failed to send email to {Email}: {Error}", toEmail, errorMessage);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception while sending email to {Email}", toEmail);
            throw;
        }
    }
}

