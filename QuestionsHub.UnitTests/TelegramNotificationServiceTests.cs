using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using QuestionsHub.Blazor.Domain;
using QuestionsHub.Blazor.Infrastructure;
using QuestionsHub.Blazor.Infrastructure.Telegram;
using QuestionsHub.UnitTests.TestInfrastructure;
using Xunit;

namespace QuestionsHub.UnitTests;

public class TelegramNotificationServiceTests : IDisposable
{
    private readonly InMemoryDbContextFactory _dbFactory;
    private readonly TelegramNotificationService _service;

    public TelegramNotificationServiceTests()
    {
        _dbFactory = new InMemoryDbContextFactory();
        var settings = Options.Create(new TelegramSettings
        {
            BotToken = "test-token",
            ChannelId = "@test_channel",
            SiteUrl = "https://questions.com.ua"
        });
        var httpClientFactory = new TestHttpClientFactory();
        var logger = NullLogger<TelegramNotificationService>.Instance;
        _service = new TelegramNotificationService(_dbFactory, httpClientFactory, settings, logger);
    }

    public void Dispose()
    {
        using var context = _dbFactory.CreateDbContext();
        context.Database.EnsureDeleted();
    }

    [Fact]
    public void BuildMessage_BasicPackage_ContainsTitleLink()
    {
        var package = CreatePackage("Турнір ЛУК 2025", id: 42);

        var message = _service.BuildMessage(package);

        message.Should().Contain("<a href=\"https://questions.com.ua/package/42\">Турнір ЛУК 2025</a>");
        message.Should().StartWith("Опубліковано:");
    }

    [Fact]
    public void BuildMessage_PackageWith18PlusTag_Appends18Plus()
    {
        var package = CreatePackage("Дорослий пакет", id: 10,
            tags: [new Tag { Id = 1, Name = "18+" }]);

        var message = _service.BuildMessage(package);

        message.Should().Contain("Дорослий пакет</a> (18+)");
    }

    [Fact]
    public void BuildMessage_PackageWithout18PlusTag_NoSuffix()
    {
        var package = CreatePackage("Звичайний пакет", id: 5,
            tags: [new Tag { Id = 1, Name = "Спорт" }]);

        var message = _service.BuildMessage(package);

        message.Should().NotContain("(18+)");
    }

    [Fact]
    public void BuildMessage_WithEditors_ContainsEditorLinks()
    {
        var package = CreatePackage("Пакет", id: 7, sharedEditors: true,
            editors:
            [
                new Author { Id = 65, FirstName = "Андрій", LastName = "Гахун" },
                new Author { Id = 163, FirstName = "Сергій", LastName = "Рева" }
            ]);

        var message = _service.BuildMessage(package);

        message.Should().Contain("Редактори:");
        message.Should().Contain("<a href=\"https://questions.com.ua/editor/65\">Андрій Гахун</a>");
        message.Should().Contain("<a href=\"https://questions.com.ua/editor/163\">Сергій Рева</a>");
    }

    [Fact]
    public void BuildMessage_NoEditors_NoEditorsLine()
    {
        var package = CreatePackage("Пакет без редакторів", id: 1);

        var message = _service.BuildMessage(package);

        message.Should().NotContain("Редактори:");
    }

    [Fact]
    public void BuildMessage_HtmlSpecialCharsInTitle_AreEncoded()
    {
        var package = CreatePackage("Тест <script> & \"цитата\"", id: 3);

        var message = _service.BuildMessage(package);

        message.Should().Contain("Тест &lt;script&gt; &amp; &quot;цитата&quot;</a>");
    }

    [Fact]
    public void BuildMessage_SiteUrlTrailingSlash_Normalized()
    {
        var settings = Options.Create(new TelegramSettings
        {
            BotToken = "test-token",
            ChannelId = "@test",
            SiteUrl = "https://questions.com.ua/"
        });
        var service = new TelegramNotificationService(
            _dbFactory,
            new TestHttpClientFactory(),
            settings,
            NullLogger<TelegramNotificationService>.Instance);

        var package = CreatePackage("Пакет", id: 5);

        var message = service.BuildMessage(package);

        message.Should().Contain("https://questions.com.ua/package/5");
        message.Should().NotContain("https://questions.com.ua//package/5");
    }

    private static Package CreatePackage(
        string title,
        int id = 1,
        bool sharedEditors = true,
        List<Author>? editors = null,
        List<Tag>? tags = null)
    {
        return new Package
        {
            Id = id,
            Title = title,
            SharedEditors = sharedEditors,
            PackageEditors = editors ?? [],
            Tags = tags ?? [],
            Tours = [],
            Status = PackageStatus.Published
        };
    }

    /// <summary>
    /// Minimal IHttpClientFactory for testing (not used for message building tests).
    /// </summary>
    private class TestHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            return new HttpClient();
        }
    }
}
