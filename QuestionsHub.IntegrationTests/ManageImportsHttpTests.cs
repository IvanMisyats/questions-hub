using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using QuestionsHub.Blazor.Controllers.Api.Manage;
using QuestionsHub.Blazor.Data;
using QuestionsHub.Blazor.Domain;
using QuestionsHub.Blazor.Infrastructure.Import;
using QuestionsHub.Blazor.Infrastructure.Media;
using QuestionsHub.IntegrationTests.Infrastructure;
using Xunit;

namespace QuestionsHub.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public sealed class ManageImportsHttpTests(PostgresFixture database) : IDisposable
{
    private const string Route = "/api/v1/manage/imports";
    private readonly string _files = Path.Combine(Path.GetTempPath(), "qh-import-http-" + Guid.NewGuid().ToString("N"));

    private QuestionsHubAppFactory Factory(bool worker = false, Dictionary<string, string?>? settings = null)
    {
        database.SkipIfUnavailable();
        Directory.CreateDirectory(Path.Combine(_files, "handouts"));
        return new(database, settings, services =>
        {
            services.PostConfigure<MediaUploadOptions>(o => o.UploadsPath = _files);
            if (!worker)
                foreach (var descriptor in services.Where(d => d.ServiceType == typeof(IHostedService)
                    && d.ImplementationType == typeof(PackageImportBackgroundService)).ToArray())
                    services.Remove(descriptor);
        });
    }

    public void Dispose()
    {
        if (Directory.Exists(_files)) Directory.Delete(_files, true);
    }

    private static async Task<HttpClient> Client(QuestionsHubAppFactory factory, string user,
        TokenScope scope = TokenScope.ReadWrite, int[]? packages = null)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", await TestData.CreateToken(factory, user, scope, packages));
        return client;
    }

    private static MultipartFormDataContent Upload(byte[]? bytes = null, string name = "package.qhub", string? type = "www")
    {
        var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(bytes ?? Qhub()), "file", name);
        if (type != null) form.Add(new StringContent(type), "gameType");
        return form;
    }

    private static byte[] Qhub()
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
        using (var writer = new StreamWriter(zip.CreateEntry("package.json").Open(), new UTF8Encoding(false)))
            writer.Write("""{"formatVersion":"1.2","gameType":"www","title":"Імпорт API","tours":[{"number":"1","questions":[{"number":"1","text":"Запитання","answer":"Відповідь"}]}]}""");
        return stream.ToArray();
    }

    private static byte[] Docx()
    {
        using var stream = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document, true))
        {
            var main = doc.AddMainDocumentPart();
            string[] paragraphs = ["Пакет API", "Тур 1", "Питання 1. Запитання?", "Відповідь: Відповідь."];
            main.Document = new Document(new Body(paragraphs.Select(text => new Paragraph(new Run(new Text(text))))));
            main.Document.Save();
        }
        return stream.ToArray();
    }

    private static async Task Error(HttpResponseMessage response, HttpStatusCode status, string? message = null)
    {
        response.StatusCode.Should().Be(status, await response.Content.ReadAsStringAsync());
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("error").GetString().Should().NotBeNullOrEmpty();
        if (message != null) json.GetProperty("error").GetString().Should().Be(message);
    }

    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Upload_WorkerCreatesOwnedReadableDraft(bool docx)
    {
        await using var factory = Factory(worker: true);
        var owner = await TestBrowser.CreateUser(factory, "Editor");
        using var client = await Client(factory, owner.Id);
        using var form = Upload(docx ? Docx() : Qhub(), docx ? "package.docx" : "package.qhub", docx ? "WWW" : "shvager");
        using var accepted = await client.PostAsync(Route, form);
        accepted.StatusCode.Should().Be(HttpStatusCode.Accepted, await accepted.Content.ReadAsStringAsync());
        var job = (await accepted.Content.ReadFromJsonAsync<ManageImportJobDto>())!;
        accepted.Headers.Location!.AbsolutePath.Should().Be($"{Route}/{job.JobId}");
        job.Status.Should().Be("queued");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        while (job.Status is "queued" or "running" or "retrying")
        {
            await Task.Delay(500, timeout.Token);
            job = (await client.GetFromJsonAsync<ManageImportJobDto>(accepted.Headers.Location, timeout.Token))!;
        }
        job.Status.Should().Be("succeeded", job.ErrorMessage);
        job.PackageId.Should().NotBeNull();
        job.NextRetryAt.Should().BeNull();
        job.FinishedAt.Should().NotBeNull();
        if (!docx)
        {
            job.RequestedGameType.Should().Be("shvager");
            job.Warnings.Should().Contain(w => w.Contains("Тип гри у файлі"));
        }
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuestionsHubDbContext>();
        var package = await db.Packages.SingleAsync(p => p.Id == job.PackageId);
        package.OwnerId.Should().Be(owner.Id);
        package.Status.Should().Be(PackageStatus.Draft);
        using var readable = await client.GetAsync($"/api/v1/manage/packages/{job.PackageId}");
        readable.StatusCode.Should().Be(HttpStatusCode.OK);
        (await scope.ServiceProvider.GetRequiredService<PackageImportService>().GetJobsByIds([job.JobId]))
            .Should().ContainSingle();
    }

    [SkippableFact]
    public async Task Rights_AreOwnerOnlyIncludingAdmins_AndRestrictedTokensCannotUseAnyRoute()
    {
        await using var factory = Factory();
        var owner = await TestBrowser.CreateUser(factory, "Editor");
        using var writer = await Client(factory, owner.Id);
        using var form = Upload();
        using var response = await writer.PostAsync(Route, form);
        var job = (await response.Content.ReadFromJsonAsync<ManageImportJobDto>())!;
        var path = $"{Route}/{job.JobId}";
        using var reader = await Client(factory, owner.Id, TokenScope.Read);
        using var list = await reader.GetAsync(Route);
        list.StatusCode.Should().Be(HttpStatusCode.OK);
        using var get = await reader.GetAsync(path);
        get.StatusCode.Should().Be(HttpStatusCode.OK);
        var raw = await get.Content.ReadAsStringAsync();
        raw.Should().NotContain("inputFilePath").And.NotContain("errorDetails");
        using var denied = await reader.PostAsync(Route, new StringContent("body must not be parsed"));
        await Error(denied, HttpStatusCode.Forbidden);

        var package = await TestData.CreatePackage(factory, owner.Id);
        using var restricted = await Client(factory, owner.Id, packages: [package.Id]);
        foreach (var request in new[] { new HttpRequestMessage(HttpMethod.Get, Route),
            new HttpRequestMessage(HttpMethod.Get, path), new HttpRequestMessage(HttpMethod.Post, Route) })
        {
            using (request)
            using (var refusal = await restricted.SendAsync(request))
                await Error(refusal, HttpStatusCode.Forbidden, "Imports need a token that is not limited to specific packages.");
        }
        var other = await TestBrowser.CreateUser(factory, "Editor");
        foreach (var user in new[] { other.Id, await TestData.AdminId(factory) })
        {
            using var foreign = await Client(factory, user);
            using var refusal = await foreign.GetAsync(path);
            await Error(refusal, HttpStatusCode.NotFound, "Import not found.");
            // The seeded admin is shared by the tests of this database: other tests may own jobs too.
            (await foreign.GetFromJsonAsync<ManageImportsResponse>(Route))!.Imports.Select(j => j.JobId).Should().NotContain(job.JobId);
        }
        using var missing = await reader.GetAsync($"{Route}/{Guid.NewGuid()}");
        await Error(missing, HttpStatusCode.NotFound, "Import not found.");
    }

    [SkippableTheory]
    [InlineData("missing-type", 400)]
    [InlineData("invalid-type", 400)]
    [InlineData("duplicate-type", 400)]
    [InlineData("duplicate-file", 400)]
    [InlineData("missing-file", 400)]
    [InlineData("not-multipart", 400)]
    [InlineData("malformed", 400)]
    [InlineData("extension", 422)]
    [InlineData("empty", 422)]
    [InlineData("file-size", 422)]
    [InlineData("request-size", 413)]
    public async Task InvalidUploads_ReturnJson(string kind, int status)
    {
        await using var factory = Factory();
        var owner = await TestBrowser.CreateUser(factory, "Editor");
        using var client = await Client(factory, owner.Id);
        using HttpContent body = kind switch
        {
            "not-multipart" => new StringContent("hello"),
            "malformed" => new StringContent("broken", Encoding.UTF8, "multipart/form-data"),
            "empty" => Upload([]),
            "extension" => Upload(name: "package.txt"),
            "missing-type" => Upload(type: null),
            "invalid-type" => Upload(type: "no"),
            "file-size" => Upload(new byte[50 * 1024 * 1024 + 1]),
            "request-size" => Upload(new byte[55 * 1024 * 1024 + 1]),
            "missing-file" => new MultipartFormDataContent { { new StringContent("www"), "gameType" } },
            _ => Upload()
        };
        if (kind == "duplicate-type") ((MultipartFormDataContent)body).Add(new StringContent("www"), "gameType");
        if (kind == "duplicate-file") ((MultipartFormDataContent)body).Add(new ByteArrayContent(Qhub()), "file", "second.qhub");
        using var response = await client.PostAsync(Route, body);
        await Error(response, (HttpStatusCode)status);
    }

    private static async Task SeedJobs(QuestionsHubAppFactory factory, string owner, params PackageImportJob[] jobs)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuestionsHubDbContext>();
        foreach (var job in jobs) { job.OwnerId = owner; db.PackageImportJobs.Add(job); }
        await db.SaveChangesAsync();
    }

    private static PackageImportJob Job(ImportJobStatus status = ImportJobStatus.Queued) => new()
    {
        Id = Guid.NewGuid(), OwnerId = "seed", InputFileName = "seed.qhub", InputFilePath = "unused",
        CreatedAt = DateTime.UtcNow, Status = status
    };

    [SkippableFact]
    public async Task Admission_CountsRetryPending_AndRunsBeforeFormReading()
    {
        await using var factory = Factory();
        var owner = await TestBrowser.CreateUser(factory, "Editor");
        var retry = Job(ImportJobStatus.Failed);
        retry.NextRetryAt = DateTime.UtcNow.AddHours(1);
        retry.Attempts = 1;
        await SeedJobs(factory, owner.Id, Job(), Job(ImportJobStatus.Running), retry);
        using var client = await Client(factory, owner.Id);
        using var response = await client.PostAsync(Route, new StringContent("not a form"));
        await Error(response, HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter!.Delta.Should().Be(TimeSpan.FromSeconds(60));
        (await client.GetFromJsonAsync<ManageImportJobDto>($"{Route}/{retry.Id}"))!.Status.Should().Be("retrying");
    }

    [SkippableFact]
    public async Task ConcurrentUploads_ThroughDifferentTokens_AdmitOnlyOneLastSlot()
    {
        await using var factory = Factory();
        var owner = await TestBrowser.CreateUser(factory, "Editor");
        await SeedJobs(factory, owner.Id, Job(), Job());
        using var first = await Client(factory, owner.Id);
        using var second = await Client(factory, owner.Id);
        using var a = Upload();
        using var b = Upload();
        var responses = await Task.WhenAll(first.PostAsync(Route, a), second.PostAsync(Route, b));
        try { responses.Select(r => r.StatusCode).Should().BeEquivalentTo([HttpStatusCode.Accepted, HttpStatusCode.TooManyRequests]); }
        finally { foreach (var response in responses) response.Dispose(); }
    }

    [SkippableFact]
    public async Task HourlyBudget_IsSharedByUser_CountsOnlyAdmittedUploads_AndReturnsHourlyRetryAfter()
    {
        await using var factory = Factory(settings: new() { ["RateLimits:AgentImportPerHour"] = "1" });
        var owner = await TestBrowser.CreateUser(factory, "Editor");
        using var first = await Client(factory, owner.Id);
        using var second = await Client(factory, owner.Id);
        using var readOnly = await Client(factory, owner.Id, TokenScope.Read);
        using var refusedForm = Upload();
        using var refused = await readOnly.PostAsync(Route, refusedForm); // refused: does not use the budget
        await Error(refused, HttpStatusCode.Forbidden);
        using var form = Upload();
        using var accepted = await first.PostAsync(Route, form);
        accepted.StatusCode.Should().Be(HttpStatusCode.Accepted);
        using var denied = await second.PostAsync(Route, new StringContent("unused"));
        await Error(denied, HttpStatusCode.TooManyRequests);
        denied.Headers.RetryAfter!.Delta.Should().Be(TimeSpan.FromHours(1));
        var other = await TestBrowser.CreateUser(factory, "Editor");
        using var independent = await Client(factory, other.Id);
        using var independentForm = Upload();
        using var otherAccepted = await independent.PostAsync(Route, independentForm);
        otherAccepted.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    [SkippableFact]
    public async Task List_ReturnsLatestTwenty_AndTerminalFailuresDoNotOccupySlots()
    {
        await using var factory = Factory();
        var owner = await TestBrowser.CreateUser(factory, "Editor");
        var jobs = Enumerable.Range(0, 24).Select(i =>
        {
            var job = Job(ImportJobStatus.Failed);
            job.CreatedAt = DateTime.UtcNow.AddMinutes(-i);
            job.NextRetryAt = DateTime.UtcNow.AddDays(1);
            job.Attempts = 3; // Exhausted, even if an old record still has NextRetryAt.
            return job;
        }).ToArray();
        await SeedJobs(factory, owner.Id, jobs);
        using var client = await Client(factory, owner.Id);
        var list = (await client.GetFromJsonAsync<ManageImportsResponse>(Route))!;
        list.Imports.Select(j => j.JobId).Should().Equal(jobs.Take(20).Select(j => j.Id));
        list.Imports.Should().OnlyContain(j => j.Status == "failed");
        using var form = Upload();
        using var accepted = await client.PostAsync(Route, form);
        accepted.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }
}
