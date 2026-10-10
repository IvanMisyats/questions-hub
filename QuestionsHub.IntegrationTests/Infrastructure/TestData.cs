using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using QuestionsHub.Blazor.Data;
using QuestionsHub.Blazor.Domain;
using QuestionsHub.Blazor.Infrastructure.AgentApi;

namespace QuestionsHub.IntegrationTests.Infrastructure;

/// <summary>Seeds packages and tokens directly through the app's services/DbContext.</summary>
public static class TestData
{
    /// <summary>
    /// A ЩДК package: tour 1 with two root questions, tour 2 with a block holding one question.
    /// Each call creates its own uniquely named author (authors are unique by name).
    /// </summary>
    public static async Task<Package> CreatePackage(
        QuestionsHubAppFactory factory, string? ownerId, PackageStatus status = PackageStatus.Draft, string title = "Тестовий пакет")
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuestionsHubDbContext>();

        var author = new Author { FirstName = "Автор", LastName = Guid.NewGuid().ToString("N")[..12] };
        var block = new Block { OrderIndex = 0, Name = "Блок А", Editors = [author] };
        var blockQuestion = new Question
        {
            OrderIndex = 0, Number = "3", Text = "Питання в блоці", Answer = "Відповідь 3", Block = block, Authors = [author]
        };

        var package = new Package
        {
            Title = title,
            OwnerId = ownerId,
            Status = status,
            TotalQuestions = 3,
            PackageEditors = [],
            Tours =
            [
                new Tour
                {
                    OrderIndex = 0,
                    Number = "1",
                    Editors = [author],
                    Questions =
                    [
                        new Question { OrderIndex = 0, Number = "1", Text = "Перше питання", Answer = "Відповідь 1", Authors = [author] },
                        new Question { OrderIndex = 1, Number = "2", Text = "Друге питання", Answer = "Відповідь 2", Source = "Джерело", Authors = [author] },
                    ]
                },
                new Tour { OrderIndex = 1, Number = "2", Blocks = [block], Questions = [blockQuestion] }
            ]
        };

        db.Packages.Add(package);
        await db.SaveChangesAsync();
        return package;
    }

    /// <summary>Creates a token for <paramref name="userId"/> and returns the raw secret.</summary>
    public static async Task<string> CreateToken(
        QuestionsHubAppFactory factory, string userId, TokenScope scope = TokenScope.ReadWrite, IReadOnlyCollection<int>? packageIds = null)
    {
        using var serviceScope = factory.Services.CreateScope();
        var result = await serviceScope.ServiceProvider.GetRequiredService<PersonalAccessTokenService>()
            .Create(userId, new CreateTokenRequest("test", scope, 30, packageIds));
        result.Success.Should().BeTrue(result.ErrorMessage);
        return result.RawToken!;
    }

    /// <summary>The seeded admin's user id.</summary>
    public static async Task<string> AdminId(QuestionsHubAppFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuestionsHubDbContext>();
        return db.Users.Single(u => u.Email == TestBrowser.AdminEmail).Id;
    }
}
