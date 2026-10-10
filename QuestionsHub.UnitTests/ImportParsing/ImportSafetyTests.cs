using System.Net;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using QuestionsHub.Blazor.Data;
using QuestionsHub.Blazor.Infrastructure.Import;
using QuestionsHub.Blazor.Infrastructure.Media;
using QuestionsHub.UnitTests.TestInfrastructure;
using Xunit;

namespace QuestionsHub.UnitTests.ImportParsing;

/// <summary>Guards for data taken from uploaded packages: asset names, download destinations, upload names.</summary>
public class ImportSafetyTests
{
    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("104.16.0.1")]
    [InlineData("141.95.51.1")]
    [InlineData("2606:4700::1")]
    [InlineData("::ffff:8.8.8.8")]
    public void PublicAddresses_AreAllowed(string address)
    {
        PublicAddressFilter.IsPublic(IPAddress.Parse(address)).Should().BeTrue();
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("0.0.0.0")]
    [InlineData("10.1.2.3")]
    [InlineData("172.17.0.2")]       // Docker bridge
    [InlineData("172.31.255.255")]
    [InlineData("192.168.1.10")]
    [InlineData("169.254.169.254")]  // cloud metadata
    [InlineData("100.64.0.1")]       // CGNAT
    [InlineData("192.0.2.1")]
    [InlineData("198.18.0.1")]
    [InlineData("224.0.0.1")]
    [InlineData("255.255.255.255")]
    [InlineData("::1")]
    [InlineData("::")]
    [InlineData("::ffff:127.0.0.1")]
    [InlineData("::ffff:10.0.0.1")]
    [InlineData("fe80::1")]
    [InlineData("fc00::1")]
    [InlineData("fd12:3456::1")]
    [InlineData("ff02::1")]
    [InlineData("64:ff9b::7f00:1")]  // NAT64 of 127.0.0.1
    [InlineData("2002:7f00:1::1")]   // 6to4 of 127.0.0.1
    [InlineData("2001:0:7f00:1::1")] // Teredo
    [InlineData("2001:db8::1")]
    public void InternalAndSpecialAddresses_AreRefused(string address)
    {
        PublicAddressFilter.IsPublic(IPAddress.Parse(address)).Should().BeFalse();
    }

    [Theory]
    [InlineData("http://127.0.0.1:9/image.png")]
    [InlineData("http://localhost:9/image.png")]
    [InlineData("http://[::1]:9/image.png")]
    public async Task DownloadClient_RefusesInternalHosts_BeforeConnecting(string url)
    {
        using var client = new HttpClient(PublicAddressFilter.CreateHandler());

        var act = () => client.GetAsync(url);

        (await act.Should().ThrowAsync<HttpRequestException>()).Which.Message.Should().Contain("недоступна");
    }

    [Theory]
    [InlineData("handout.png", true)]
    [InlineData("Photo 1.JPG", true)]
    [InlineData("clip.mp4", true)]
    [InlineData("../handout.png", false)]
    [InlineData("a/handout.png", false)]
    [InlineData("a\\handout.png", false)]
    [InlineData("/etc/handout.png", false)]
    [InlineData("drawing.svg", false)]
    [InlineData("page.html", false)]
    [InlineData("handout", false)]
    [InlineData("..", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void MediaFileNamesFromUploads_MustBeBareNamesOfAllowedTypes(string? name, bool allowed)
    {
        MediaSecurityOptions.IsAllowedMediaFileName(name).Should().Be(allowed);
    }
}

public class ImportUploadNameTests : IDisposable
{
    private readonly string _uploads = Path.Combine(Path.GetTempPath(), "qh-upload-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_uploads))
            Directory.Delete(_uploads, recursive: true);
    }

    private PackageImportService CreateService()
    {
        var factory = new InMemoryDbContextFactory();
        var services = new ServiceCollection();
        services.AddScoped<QuestionsHubDbContext>(_ => factory.CreateDbContext());
        return new PackageImportService(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new PackageImportOptions()),
            Options.Create(new MediaUploadOptions { UploadsPath = _uploads }),
            NullLogger<PackageImportService>.Instance);
    }

    [Theory]
    [InlineData("../../../escaped.docx")]
    [InlineData("..\\..\\..\\escaped.docx")]
    [InlineData("/tmp/escaped.docx")]
    public async Task UploadName_WithAPath_IsStoredInsideTheJobFolder(string name)
    {
        using var content = new MemoryStream([1, 2, 3]);

        var job = await CreateService().Enqueue("owner", name, content, content.Length);

        job.InputFileName.Should().Be("escaped.docx");
        job.InputFilePath.Should().Be(Path.Combine("jobs", job.Id.ToString(), "input", "escaped.docx"));
        File.Exists(Path.Combine(_uploads, job.InputFilePath)).Should().BeTrue();
        Directory.GetFiles(_uploads, "escaped.docx", SearchOption.AllDirectories).Should().ContainSingle();
    }

    [Theory]
    [InlineData("..")]
    [InlineData("dir/")]
    public async Task UploadName_WithoutAFileName_IsRejected(string name)
    {
        using var content = new MemoryStream([1, 2, 3]);

        var act = () => CreateService().Enqueue("owner", name, content, content.Length);

        await act.Should().ThrowAsync<ValidationException>();
    }
}
