using System.Globalization;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace QuestionsHub.Blazor.Infrastructure.AgentApi;

public class PersonalAccessTokenAuthenticationOptions : AuthenticationSchemeOptions
{
    public const string Scheme = "PersonalAccessToken";
}

/// <summary>
/// Authenticates <c>Authorization: Bearer qh_pat_…</c>. Requests without such a header get
/// <see cref="AuthenticateResult.NoResult"/>, so the scheme never interferes with cookie or API-key
/// authentication; agent endpoints authorize with this scheme only, so cookies are never accepted there.
/// </summary>
public class PersonalAccessTokenAuthenticationHandler(
    IOptionsMonitor<PersonalAccessTokenAuthenticationOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    PersonalAccessTokenService tokenService)
    : AuthenticationHandler<PersonalAccessTokenAuthenticationOptions>(options, logger, encoder)
{
    private const string BearerPrefix = "Bearer ";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
            return AuthenticateResult.NoResult();

        var rawToken = header[BearerPrefix.Length..].Trim();
        if (!rawToken.StartsWith(PersonalAccessTokenService.TokenPrefix, StringComparison.Ordinal))
            return AuthenticateResult.NoResult();

        var validated = await tokenService.Validate(rawToken);
        if (validated == null)
            return AuthenticateResult.Fail("Invalid, expired or revoked token.");

        return AuthenticateResult.Success(new AuthenticationTicket(CreatePrincipal(validated), Scheme.Name));
    }

    /// <summary>The principal an agent request runs as: the token's user, current roles and token claims.</summary>
    public static ClaimsPrincipal CreatePrincipal(ValidatedToken validated)
    {
        var token = validated.Token;
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, validated.User.Id),
            new(ClaimTypes.Name, validated.User.FullName),
            new(AgentClaims.TokenId, token.Id.ToString(CultureInfo.InvariantCulture)),
            new(AgentClaims.TokenName, token.Name),
            new(AgentClaims.Scope, token.Scope.ToString()),
            new(AgentClaims.ExpiresAt, token.ExpiresAt.ToString("O", CultureInfo.InvariantCulture)),
        };
        claims.AddRange(validated.Roles.Select(role => new Claim(ClaimTypes.Role, role)));

        if (token.PackageIds != null)
        {
            claims.Add(new Claim(AgentClaims.Packages,
                string.Join(',', token.PackageIds.Select(id => id.ToString(CultureInfo.InvariantCulture)))));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, PersonalAccessTokenAuthenticationOptions.Scheme));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate = "Bearer";
        Response.ContentType = "application/json";
        return Response.WriteAsync("""{"error":"Missing or invalid token. Provide Authorization: Bearer qh_pat_..."}""");
    }

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        Response.ContentType = "application/json";
        return Response.WriteAsync("""{"error":"The token's scope does not allow this operation."}""");
    }
}
