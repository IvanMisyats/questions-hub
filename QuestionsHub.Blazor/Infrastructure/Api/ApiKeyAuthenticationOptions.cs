using Microsoft.AspNetCore.Authentication;

namespace QuestionsHub.Blazor.Infrastructure.Api;

public class ApiKeyAuthenticationOptions : AuthenticationSchemeOptions
{
    public const string Scheme = "ApiKey";
    public const string HeaderName = "X-API-Key";
}
