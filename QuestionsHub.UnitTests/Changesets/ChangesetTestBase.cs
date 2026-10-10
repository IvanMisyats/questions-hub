using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using QuestionsHub.Blazor.Data;
using QuestionsHub.Blazor.Domain;
using QuestionsHub.Blazor.Infrastructure;
using QuestionsHub.Blazor.Infrastructure.AgentApi;
using QuestionsHub.Blazor.Infrastructure.AgentApi.Changesets;
using QuestionsHub.UnitTests.TestInfrastructure;

namespace QuestionsHub.UnitTests.Changesets;

/// <summary>
/// Seeds packages in an in-memory database and runs the changeset engine against a tracked graph
/// loaded in a fresh context, like the real service does.
/// </summary>
public abstract class ChangesetTestBase : IDisposable
{
    protected readonly InMemoryDbContextFactory DbFactory = new();

    public void Dispose()
    {
        using var context = DbFactory.CreateDbContext();
        context.Database.EnsureDeleted();
        GC.SuppressFinalize(this);
    }

    /// <summary>ids of the seeded ЩДК package (see <see cref="SeedWww"/>).</summary>
    protected sealed record WwwIds(int PackageId, int Tour1, int Tour2, int Block, int Q1, int Q2, int Q3, int AnnaId, int PetroId);

    /// <summary>ids of the seeded Своя гра package (see <see cref="SeedShvager"/>).</summary>
    protected sealed record ShvagerIds(int PackageId, int Theme1, int Theme2, int[] Theme1Questions, int[] Theme2Questions, int AnnaId);

    /// <summary>
    /// ЩДК, Global numbering. Tour 1 (editor Анна): Q1 «1» (author Анна), Q2 «2» (no author).
    /// Tour 2: block (editor Петро) with Q3 «3» (author Петро).
    /// </summary>
    protected async Task<WwwIds> SeedWww()
    {
        using var context = DbFactory.CreateDbContext();
        var anna = new Author { FirstName = "Анна", LastName = "Коваль" };
        var petro = new Author { FirstName = "Петро", LastName = "Мельник" };
        var block = new Block { OrderIndex = 0, Name = "Блок", Editors = [petro] };
        var q1 = new Question { OrderIndex = 0, Number = "1", Text = "Перше", Answer = "А", Comment = "Коментар", Authors = [anna] };
        var q2 = new Question { OrderIndex = 1, Number = "2", Text = "Друге", Answer = "Б" };
        var q3 = new Question { OrderIndex = 0, Number = "3", Text = "Третє", Answer = "В", Block = block, Authors = [petro] };
        var tour1 = new Tour { OrderIndex = 0, Number = "1", Editors = [anna], Questions = [q1, q2] };
        var tour2 = new Tour { OrderIndex = 1, Number = "2", Blocks = [block], Questions = [q3] };
        var package = new Package
        {
            Title = "Пакет ЩДК", Type = PackageType.Www, NumberingMode = QuestionNumberingMode.Global,
            TotalQuestions = 3, Tours = [tour1, tour2]
        };

        context.Packages.Add(package);
        await context.SaveChangesAsync();
        return new WwwIds(package.Id, tour1.Id, tour2.Id, block.Id, q1.Id, q2.Id, q3.Id, anna.Id, petro.Id);
    }

    /// <summary>
    /// Своя гра. Theme 1 (no editor): 5 questions 10–50, the first authored by Анна, the rest author-less.
    /// Theme 2 (editor Анна): 2 author-less questions 10, 20.
    /// </summary>
    protected async Task<ShvagerIds> SeedShvager()
    {
        using var context = DbFactory.CreateDbContext();
        var anna = new Author { FirstName = "Анна", LastName = "Шваг" };
        var theme1 = new Tour
        {
            OrderIndex = 0, Number = "1", Title = "Тема перша",
            Questions = Enumerable.Range(0, 5).Select(i => new Question
            {
                OrderIndex = i, Number = ((i + 1) * 10).ToString(System.Globalization.CultureInfo.InvariantCulture),
                Text = $"Питання {i}", Answer = "В", Authors = i == 0 ? [anna] : []
            }).ToList()
        };
        var theme2 = new Tour
        {
            OrderIndex = 1, Number = "2", Title = "Тема друга", Editors = [anna],
            Questions =
            [
                new Question { OrderIndex = 0, Number = "10", Text = "А", Answer = "А" },
                new Question { OrderIndex = 1, Number = "20", Text = "Б", Answer = "Б" },
            ]
        };
        var package = new Package { Title = "Своя гра", Type = PackageType.Shvager, TotalQuestions = 7, Tours = [theme1, theme2] };

        context.Packages.Add(package);
        await context.SaveChangesAsync();
        return new ShvagerIds(package.Id, theme1.Id, theme2.Id,
            theme1.Questions.OrderBy(q => q.OrderIndex).Select(q => q.Id).ToArray(),
            theme2.Questions.OrderBy(q => q.OrderIndex).Select(q => q.Id).ToArray(),
            anna.Id);
    }

    /// <summary>A loaded, tracked graph and an engine over it. Dispose the context when done.</summary>
    protected sealed record Session(QuestionsHubDbContext Context, Package Package, ChangesetEngine Engine) : IDisposable
    {
        public void Dispose() => Context.Dispose();
    }

    protected async Task<Session> Open(int packageId, bool hasResults = false)
    {
        var context = DbFactory.CreateDbContext();
        var package = await context.Packages.WithEditableGraph().FirstAsync(p => p.Id == packageId);
        var engine = new ChangesetEngine(context, package, new PackageRenumberingService(DbFactory), hasResults);
        return new Session(context, package, engine);
    }

    /// <summary>Parses a JSON array of operations and applies it.</summary>
    protected static async Task Apply(Session session, string operationsJson)
    {
        using var document = JsonDocument.Parse(operationsJson);
        var operations = ChangesetParser.Parse(document.RootElement);
        await session.Engine.Apply(operations);
    }

    /// <summary>Applies and saves in one go; returns the change DTOs.</summary>
    protected async Task<List<ChangeDto>> ApplyAndSave(int packageId, string operationsJson)
    {
        using var session = await Open(packageId);
        await Apply(session, operationsJson);
        await session.Context.SaveChangesAsync();
        return ChangeFormatter.ToDtos(session.Engine.Changes, session.Package, session.Context);
    }

    protected async Task<Question> LoadQuestion(int id)
    {
        using var context = DbFactory.CreateDbContext();
        return await context.Questions.Include(q => q.Authors).AsNoTracking().FirstAsync(q => q.Id == id);
    }

    protected async Task<Package> LoadPackage(int id)
    {
        using var context = DbFactory.CreateDbContext();
        return await context.Packages.AsNoTracking().WithEditableGraph().FirstAsync(p => p.Id == id);
    }
}
