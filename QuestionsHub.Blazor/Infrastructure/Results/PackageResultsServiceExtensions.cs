namespace QuestionsHub.Blazor.Infrastructure.Results;

/// <summary>
/// Extension methods for registering tournament-results services.
/// </summary>
public static class PackageResultsServiceExtensions
{
    /// <summary>Named HttpClient used for all external results-loading requests.</summary>
    public const string HttpClientName = "ResultsLoader";

    /// <summary>
    /// Adds tournament-results loading services to the service collection.
    /// </summary>
    public static IServiceCollection AddPackageResults(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<ResultsOptions>(
            configuration.GetSection(ResultsOptions.SectionName));

        services.AddScoped<RatingResultsClient>();
        services.AddScoped<OpenQuizResultsClient>();
        services.AddScoped<PackageResultsService>();

        services.AddHttpClient(HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("QuestionsHub/1.0");
        });

        return services;
    }
}
