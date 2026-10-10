using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Text.Unicode;
using Microsoft.Extensions.DependencyInjection.Extensions;
using QuestionsHub.Blazor.Domain;
using QuestionsHub.Blazor.Infrastructure.AgentApi.Mcp;
using QuestionsHub.Blazor.Infrastructure.RateLimiting;

namespace QuestionsHub.Blazor.Infrastructure.AgentApi;

public static class AgentApiServiceExtensions
{
    /// <summary>
    /// Registers personal access tokens: the token service, the bearer authentication scheme and the
    /// <see cref="AgentPolicies"/>. Both policies authenticate with the token scheme only.
    /// </summary>
    public static IServiceCollection AddAgentApi(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<PersonalAccessTokenService>();
        services.AddScoped<Changesets.PackageChangesetService>();
        services.AddScoped<AgentPackageReader>();

        services.AddAuthentication()
            .AddScheme<PersonalAccessTokenAuthenticationOptions, PersonalAccessTokenAuthenticationHandler>(
                PersonalAccessTokenAuthenticationOptions.Scheme, _ => { });

        services.AddAuthorizationBuilder()
            .AddPolicy(AgentPolicies.Read, policy => policy
                .AddAuthenticationSchemes(PersonalAccessTokenAuthenticationOptions.Scheme)
                .RequireAuthenticatedUser()
                .RequireRole("Editor", "Admin"))
            .AddPolicy(AgentPolicies.Write, policy => policy
                .AddAuthenticationSchemes(PersonalAccessTokenAuthenticationOptions.Scheme)
                .RequireAuthenticatedUser()
                .RequireRole("Editor", "Admin")
                .RequireClaim(AgentClaims.Scope, nameof(TokenScope.ReadWrite)));

        // MCP server for agents: Streamable HTTP, stateless (no server-side sessions to pin or expire).
        services.AddMcpServer(options => options.ServerInfo = new() { Name = "questions-hub", Version = "1.0" })
            .WithHttpTransport(options => options.Stateless = true)
            .WithTools<AgentMcpTools>(new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                // camelCase like the REST API; reflection-based contracts for the tool DTOs.
                TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
                // Keep Cyrillic as-is in tool results: \uXXXX escapes make Ukrainian text several times
                // longer (and harder to read) for the model. The JSON-RPC envelope still escapes safely.
                Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
            });

        return services;
    }

    /// <summary>
    /// Maps the MCP endpoint at <c>/mcp</c> with the same protection as the REST agent API: token
    /// scheme only (<see cref="AgentPolicies.Read"/>; changes also need the readWrite scope, checked by
    /// the tool), per-IP admission and the per-token read budget (the write budget is enforced by the
    /// changeset service).
    /// </summary>
    public static IEndpointRouteBuilder MapAgentMcp(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapMcp("/mcp")
            .RequireAuthorization(AgentPolicies.Read)
            .RequireRateLimiting(RateLimitPolicies.IpManage)
            .WithMetadata(new ClientRateLimitAttribute(RateLimitBudgets.AgentRead));
        return endpoints;
    }
}
