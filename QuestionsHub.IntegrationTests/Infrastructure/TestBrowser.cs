using System.Net;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using QuestionsHub.Blazor.Domain;

namespace QuestionsHub.IntegrationTests.Infrastructure;

/// <summary>A user created for a test, with the credentials to log in.</summary>
public record TestUser(string Id, string Email, string Password);

/// <summary>Helpers for acting like a signed-in browser against the test host.</summary>
public static partial class TestBrowser
{
    public const string AdminEmail = "admin@test.local";
    public const string AdminPassword = "TestAdmin123!";

    /// <summary>HTTPS client with a cookie jar (auth cookies are Secure-only).</summary>
    public static HttpClient Create(QuestionsHubAppFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = true
        });

    /// <summary>Creates a confirmed user, optionally in <paramref name="role"/>.</summary>
    public static async Task<TestUser> CreateUser(QuestionsHubAppFactory factory, string? role)
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var email = $"{Guid.NewGuid():N}@test.local";
        const string password = "Passw0rd!x";
        var user = new ApplicationUser
        {
            FirstName = "Олена",
            LastName = role ?? "Користувач",
            UserName = email,
            Email = email,
            EmailConfirmed = true
        };
        (await users.CreateAsync(user, password)).Succeeded.Should().BeTrue();
        if (role != null)
            (await users.AddToRoleAsync(user, role)).Succeeded.Should().BeTrue();

        return new TestUser(user.Id, email, password);
    }

    /// <summary>Signs the browser in through the real login form (antiforgery token included).</summary>
    public static async Task Login(HttpClient browser, string email, string password)
    {
        var page = await browser.GetStringAsync("/Account/Login");
        var antiforgery = AntiforgeryField().Match(page).Groups[1].Value;
        antiforgery.Should().NotBeEmpty();

        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["email"] = email,
            ["password"] = password,
            ["__RequestVerificationToken"] = WebUtility.HtmlDecode(antiforgery),
        });
        using var response = await browser.PostAsync("/api/Auth/login", form);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().Be("/", "the login must succeed");
    }

    /// <summary>Server-rendered HTML of <paramref name="url"/> with HTML entities decoded (Blazor encodes dynamic Cyrillic text).</summary>
    public static async Task<string> GetDecodedHtml(HttpClient browser, string url) =>
        WebUtility.HtmlDecode(await browser.GetStringAsync(url));

    [GeneratedRegex("name=\"__RequestVerificationToken\" value=\"([^\"]+)\"")]
    private static partial Regex AntiforgeryField();
}
