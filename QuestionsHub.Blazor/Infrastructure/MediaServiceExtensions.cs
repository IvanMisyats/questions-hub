namespace QuestionsHub.Blazor.Infrastructure;

/// <summary>
/// Extension methods for registering media services.
/// </summary>
public static class MediaServiceExtensions
{
    /// <summary>
    /// Adds media upload and serving services to the service collection.
    /// </summary>
    public static IServiceCollection AddMediaServices(
        this IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        // Configure media upload options
        var options = new MediaUploadOptions();
        configuration.GetSection(MediaUploadOptions.SectionName).Bind(options);

        // Set uploads path based on environment
        options.UploadsPath = environment.IsDevelopment()
            ? Path.Combine(Directory.GetCurrentDirectory(), "..", "uploads")
            : "/app/uploads";

        services.AddSingleton(options);
        services.AddScoped<MediaService>();

        return services;
    }
}

