namespace QuestionsHub.Blazor.Infrastructure.Media;

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
        services.Configure<MediaUploadOptions>(
            configuration.GetSection(MediaUploadOptions.SectionName));

        // Set uploads path based on environment
        services.PostConfigure<MediaUploadOptions>(options =>
        {
            options.UploadsPath = environment.IsDevelopment()
                ? Path.Combine(Directory.GetCurrentDirectory(), "..", "uploads")
                : "/app/uploads";
        });

        services.AddScoped<MediaService>();

        return services;
    }
}

