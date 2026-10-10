using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using QuestionsHub.Blazor.Data;

namespace QuestionsHub.IntegrationTests.Infrastructure;

/// <summary>
/// Hosts the real application in-process against the test container database.
/// Runs in the Development environment (relative media/keys folders, permissive AllowedHosts) and
/// trusts <c>X-Forwarded-For</c> like production (<c>ASPNETCORE_FORWARDEDHEADERS_ENABLED</c>), so
/// tests can simulate client IPs. All settings are per host — nothing is written to the process
/// environment.
/// </summary>
public sealed class QuestionsHubAppFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString;
    private readonly Dictionary<string, string?> _settings;
    private readonly Action<IServiceCollection>? _configureServices;

    /// <param name="configureServices">Extra service registrations for this host only (e.g. fault-injecting interceptors).</param>
    public QuestionsHubAppFactory(
        PostgresFixture database,
        IDictionary<string, string?>? settings = null,
        Action<IServiceCollection>? configureServices = null)
    {
        _configureServices = configureServices;
        _connectionString = database.ConnectionString
            ?? throw new InvalidOperationException("The test database is not running.");

        // Development resolves media to <current dir>/../uploads; startup requires the folder.
        Directory.CreateDirectory(Path.Combine(Directory.GetCurrentDirectory(), "..", "uploads", "handouts"));

        _settings = new Dictionary<string, string?>(settings ?? new Dictionary<string, string?>())
        {
            ["AdminCredentials:Email"] = "admin@test.local",
            ["AdminCredentials:Password"] = "TestAdmin123!",
        };
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        // UseSetting lands in the builder configuration before Program reads the connection string
        // during service registration (ConfigureAppConfiguration would be too late for that).
        builder.UseSetting("ConnectionStrings:DefaultConnection", _connectionString);
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(_settings));

        builder.ConfigureServices(services =>
        {
            services.AddTransient<IStartupFilter, TrustForwardedForStartupFilter>();

            // Test-only probe controllers (e.g. AgentPolicyProbeController) from this assembly.
            services.AddControllers().AddApplicationPart(typeof(QuestionsHubAppFactory).Assembly);

            _configureServices?.Invoke(services);
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);

        // Guard against silently running against the developer database from appsettings.Development.json.
        using var scope = host.Services.CreateScope();
        var actual = scope.ServiceProvider.GetRequiredService<QuestionsHubDbContext>().Database.GetConnectionString();
        if (actual != _connectionString)
        {
            host.Dispose();
            throw new InvalidOperationException("The app is not using the test container database.");
        }

        return host;
    }

    /// <summary>A client whose requests appear to come from <paramref name="clientIp"/>.</summary>
    public HttpClient CreateClientFrom(string clientIp)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("X-Forwarded-For", clientIp);
        return client;
    }

    /// <summary>
    /// What <c>ASPNETCORE_FORWARDEDHEADERS_ENABLED=true</c> does in production: forwarded headers
    /// processed first, from any source.
    /// </summary>
    private sealed class TrustForwardedForStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            var options = new ForwardedHeadersOptions
            {
                ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
            };
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();

            app.UseForwardedHeaders(options);
            next(app);
        };
    }
}
