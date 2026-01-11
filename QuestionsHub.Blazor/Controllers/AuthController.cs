using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using QuestionsHub.Blazor.Domain;

namespace QuestionsHub.Blazor.Controllers;

/// <summary>
/// Handles authentication actions that require HTTP context (cookies).
/// Blazor Server cannot set cookies directly due to WebSocket connection.
/// </summary>
[Route("api/[controller]")]
[ApiController]
public class AuthController : ControllerBase
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IEmailSender<ApplicationUser> _emailSender;

    public AuthController(
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager,
        IEmailSender<ApplicationUser> emailSender)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _emailSender = emailSender;
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromForm] string email, [FromForm] string password, [FromForm] bool rememberMe, [FromForm] string? returnUrl)
    {
        var user = await _userManager.FindByEmailAsync(email);

        // Check if email is confirmed before attempting sign in
        if (user != null && !await _userManager.IsEmailConfirmedAsync(user))
        {
            return Redirect($"/Account/Login?error=emailnotconfirmed&email={Uri.EscapeDataString(email)}&returnUrl={Uri.EscapeDataString(returnUrl ?? "/")}");
        }

        var result = await _signInManager.PasswordSignInAsync(email, password, rememberMe, lockoutOnFailure: true);

        if (result.Succeeded)
        {
            return LocalRedirect(returnUrl ?? "/");
        }

        if (result.IsLockedOut)
        {
            return Redirect($"/Account/Login?error=lockedout&returnUrl={Uri.EscapeDataString(returnUrl ?? "/")}");
        }

        return Redirect($"/Account/Login?error=invalid&returnUrl={Uri.EscapeDataString(returnUrl ?? "/")}");
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register(
        [FromForm] string firstName,
        [FromForm] string lastName,
        [FromForm] string? city,
        [FromForm] string? team,
        [FromForm] string email,
        [FromForm] string password,
        [FromForm] string confirmPassword)
    {
        // Validate required fields
        if (string.IsNullOrWhiteSpace(firstName))
        {
            return Redirect(BuildErrorRedirect("FirstNameRequired", firstName, lastName, city, team, email));
        }

        if (string.IsNullOrWhiteSpace(lastName))
        {
            return Redirect(BuildErrorRedirect("LastNameRequired", firstName, lastName, city, team, email));
        }

        if (string.IsNullOrWhiteSpace(email))
        {
            return Redirect(BuildErrorRedirect("EmailRequired", firstName, lastName, city, team, email));
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            return Redirect(BuildErrorRedirect("PasswordRequired", firstName, lastName, city, team, email));
        }

        // Validate password confirmation
        if (password != confirmPassword)
        {
            return Redirect(BuildErrorRedirect("PasswordMismatch", firstName, lastName, city, team, email));
        }

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            FirstName = firstName,
            LastName = lastName,
            City = city,
            Team = team,
            EmailConfirmed = false // Email needs to be confirmed
        };

        var result = await _userManager.CreateAsync(user, password);

        if (result.Succeeded)
        {
            await _userManager.AddToRoleAsync(user, "User");

            // Generate email confirmation token and send email
            var code = await _userManager.GenerateEmailConfirmationTokenAsync(user);
            var request = HttpContext.Request;
            var callbackUrl = $"{request.Scheme}://{request.Host}/Account/ConfirmEmail?userId={Uri.EscapeDataString(user.Id)}&code={Uri.EscapeDataString(code)}";

            await _emailSender.SendConfirmationLinkAsync(user, email, callbackUrl);

            // Redirect to confirmation page instead of auto-login
            return Redirect($"/Account/RegisterConfirmation?email={Uri.EscapeDataString(email)}");
        }

        var errors = string.Join(",", result.Errors.Select(e => e.Code));
        return Redirect(BuildErrorRedirect(errors, firstName, lastName, city, team, email));
    }

    private static string BuildErrorRedirect(string errorCode, string? firstName, string? lastName, string? city, string? team, string? email)
    {
        var queryParams = new List<string>
        {
            $"error={Uri.EscapeDataString(errorCode)}"
        };

        if (!string.IsNullOrWhiteSpace(firstName))
            queryParams.Add($"firstName={Uri.EscapeDataString(firstName)}");

        if (!string.IsNullOrWhiteSpace(lastName))
            queryParams.Add($"lastName={Uri.EscapeDataString(lastName)}");

        if (!string.IsNullOrWhiteSpace(city))
            queryParams.Add($"city={Uri.EscapeDataString(city)}");

        if (!string.IsNullOrWhiteSpace(team))
            queryParams.Add($"team={Uri.EscapeDataString(team)}");

        if (!string.IsNullOrWhiteSpace(email))
            queryParams.Add($"email={Uri.EscapeDataString(email)}");

        return $"/Account/Register?{string.Join("&", queryParams)}";
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await _signInManager.SignOutAsync();
        return LocalRedirect("/");
    }

    [HttpGet("logout")]
    public async Task<IActionResult> LogoutGet()
    {
        await _signInManager.SignOutAsync();
        return LocalRedirect("/");
    }
}

