using System.Globalization;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace QuestionsHub.Blazor.Infrastructure.Api;

public class ApiKeyAuthenticationHandler : AuthenticationHandler<ApiKeyAuthenticationOptions>
{
    private readonly ApiKeyService _apiKeyService;

    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<ApiKeyAuthenticationOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        ApiKeyService apiKeyService)
        : base(options, logger, encoder)
    {
        _apiKeyService = apiKeyService;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(ApiKeyAuthenticationOptions.HeaderName, out var headerValue))
            return AuthenticateResult.NoResult();

        var rawKey = headerValue.ToString();
        if (string.IsNullOrWhiteSpace(rawKey))
            return AuthenticateResult.Fail("API key is empty.");

        var client = await _apiKeyService.Validate(rawKey);
        if (client == null)
            return AuthenticateResult.Fail("Invalid or revoked API key.");

        // Fire-and-forget LastUsedAt update
        _ = _apiKeyService.UpdateLastUsed(client.Id);

        var claims = new[]
        {
            new Claim("api_client_id", client.Id.ToString(CultureInfo.InvariantCulture)),
            new Claim("api_client_name", client.Name)
        };

        var identity = new ClaimsIdentity(claims, ApiKeyAuthenticationOptions.Scheme);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, ApiKeyAuthenticationOptions.Scheme);

        return AuthenticateResult.Success(ticket);
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = 401;
        Response.ContentType = "application/json";
        return Response.WriteAsync("""{"error":"Missing or invalid API key. Provide X-API-Key header."}""");
    }
}
