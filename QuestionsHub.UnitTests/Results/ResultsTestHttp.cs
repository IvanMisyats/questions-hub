using System.Net;
using System.Text;

namespace QuestionsHub.UnitTests.Results;

/// <summary>
/// Routing fake for HttpClient: responds to requests whose URL contains a registered
/// substring (routes are checked in registration order — register specific ones first).
/// </summary>
internal sealed class FakeResultsHandler : HttpMessageHandler
{
    private readonly List<(string UrlContains, HttpStatusCode Status, string Body)> _routes = [];

    public List<string> RequestedUrls { get; } = [];
    public List<string> RequestBodies { get; } = [];

    public FakeResultsHandler Add(string urlContains, string body, HttpStatusCode status = HttpStatusCode.OK)
    {
        _routes.Add((urlContains, status, body));
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = request.RequestUri!.ToString();
        RequestedUrls.Add(url);
        if (request.Content != null)
        {
            RequestBodies.Add(await request.Content.ReadAsStringAsync(cancellationToken));
        }

        foreach (var (urlContains, status, body) in _routes)
        {
            if (url.Contains(urlContains, StringComparison.Ordinal))
            {
                return new HttpResponseMessage(status)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json")
                };
            }
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("") };
    }
}

internal sealed class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
}

internal static class ResultsFixtures
{
    public static string Read(string fileName)
    {
        return File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "Results", fileName));
    }
}
