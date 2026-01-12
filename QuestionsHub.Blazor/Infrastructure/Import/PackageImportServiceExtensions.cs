namespace QuestionsHub.Blazor.Infrastructure.Import;

/// <summary>
/// Extension methods for registering package import services.
/// </summary>
public static class PackageImportServiceExtensions
{
    /// <summary>
    /// Adds package import services to the service collection.
    /// </summary>
    public static IServiceCollection AddPackageImport(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Configure package import options
        var options = new PackageImportOptions();
        configuration.GetSection(PackageImportOptions.SectionName).Bind(options);
        services.AddSingleton(options);

        // Register import services
        services.AddScoped<DocxExtractor>();
        services.AddScoped<PackageParser>();
        services.AddScoped<PackageDbImporter>();
        services.AddScoped<PackageImportService>();


        // Register background services
        services.AddHostedService<StaleJobRecoveryService>();
        services.AddHostedService<PackageImportBackgroundService>();

        return services;
    }
}

