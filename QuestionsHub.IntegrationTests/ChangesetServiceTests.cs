using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using QuestionsHub.Blazor.Data;
using QuestionsHub.Blazor.Domain;
using QuestionsHub.Blazor.Infrastructure.AgentApi;
using QuestionsHub.Blazor.Infrastructure.AgentApi.Changesets;
using QuestionsHub.IntegrationTests.Infrastructure;
using Xunit;

namespace QuestionsHub.IntegrationTests;

/// <summary>
/// The transactional changeset service against PostgreSQL: atomic apply with audit and real ids,
/// dry runs, rollback, idempotent retries, version conflicts, access, limits and races.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class ChangesetServiceTests(PostgresFixture database)
{
    private QuestionsHubAppFactory CreateFactory(params (string Key, int Value)[] limits)
    {
        database.SkipIfUnavailable();
        var settings = limits.ToDictionary(
            l => $"RateLimits:{l.Key}",
            l => (string?)l.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return new QuestionsHubAppFactory(database, settings);
    }

    /// <summary>A principal exactly as the token authentication handler builds it.</summary>
    private static async Task<ClaimsPrincipal> Agent(
        QuestionsHubAppFactory factory, string userId, IReadOnlyCollection<int>? packageIds = null)
    {
        var raw = await TestData.CreateToken(factory, userId, TokenScope.ReadWrite, packageIds);
        using var scope = factory.Services.CreateScope();
        var validated = await scope.ServiceProvider.GetRequiredService<PersonalAccessTokenService>().Validate(raw);
        return PersonalAccessTokenAuthenticationHandler.CreatePrincipal(validated!);
    }

    private static ChangesetRequest Request(string operationsJson, Guid? requestId = null, bool dryRun = false,
        string? expectedVersion = null, string? summary = null)
    {
        using var document = JsonDocument.Parse(operationsJson);
        return new ChangesetRequest(dryRun ? requestId : requestId ?? Guid.NewGuid(), expectedVersion, summary, dryRun,
            document.RootElement.Clone());
    }

    private static async Task<ChangesetResult> Execute(QuestionsHubAppFactory factory, ClaimsPrincipal agent, int packageId, ChangesetRequest request)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<PackageChangesetService>().Execute(agent, packageId, request);
    }

    private static async Task<T> Query<T>(QuestionsHubAppFactory factory, Func<QuestionsHubDbContext, Task<T>> query)
    {
        using var scope = factory.Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<QuestionsHubDbContext>());
    }

    private static async Task<Question> FirstQuestion(QuestionsHubAppFactory factory, int packageId) =>
        await Query(factory, db => db.Questions.Include(q => q.Authors).AsNoTracking()
            .Where(q => q.Tour.PackageId == packageId && q.Tour.OrderIndex == 0)
            .OrderBy(q => q.OrderIndex).FirstAsync());

    private static async Task<string> CurrentVersion(QuestionsHubAppFactory factory, int packageId) =>
        await Query(factory, async db => PackageGraph.Fingerprint(
            await db.Packages.AsNoTracking().WithEditableGraph().FirstAsync(p => p.Id == packageId)));

    [SkippableFact]
    public async Task Apply_SavesChanges_AndAnAuditRowWithRealIds()
    {
        await using var factory = CreateFactory();
        var editor = await TestBrowser.CreateUser(factory, "Editor");
        var package = await TestData.CreatePackage(factory, editor.Id);
        var agent = await Agent(factory, editor.Id);
        var question = await FirstQuestion(factory, package.Id);
        var newAuthor = $"Нова{Guid.NewGuid():N}"[..20];

        var result = await Execute(factory, agent, package.Id, Request($$"""
            [ { "op": "updateQuestion", "questionId": {{question.Id}}, "set": { "text": "Виправлений текст" } },
              { "op": "setQuestionAuthors", "questionId": {{question.Id}}, "authors": [ { "firstName": "Авторка", "lastName": "{{newAuthor}}" } ] } ]
            """, summary: "Виправлення за листом"));

        result.Status.Should().Be(ChangesetStatus.Ok, result.Error);
        var response = result.Response!;
        response.ChangesetId.Should().NotBeNull();
        response.DryRun.Should().BeFalse();
        response.Changes.Should().HaveCount(2);
        response.VersionAfter.Should().Be(await CurrentVersion(factory, package.Id), "versionAfter matches a fresh read");

        (await FirstQuestion(factory, package.Id)).Text.Should().Be("Виправлений текст");

        var audit = await Query(factory, db => db.PackageChangesets.AsNoTracking().SingleAsync(c => c.Id == response.ChangesetId));
        audit.PackageId.Should().Be(package.Id);
        audit.UserId.Should().Be(editor.Id);
        audit.TokenName.Should().Be("test");
        audit.Summary.Should().Be("Виправлення за листом");
        audit.OperationCount.Should().Be(2);
        audit.VersionBefore.Should().Be(response.VersionBefore);
        var stored = JsonSerializer.Deserialize<List<ChangeDto>>(audit.ChangesJson, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        stored.Single(c => c.Field == "authors").After!.AsArray().Single()!["id"]!.GetValue<int>()
            .Should().BeGreaterThan(0, "the new author's generated id is in the audit");
    }

    [SkippableFact]
    public async Task DryRun_PreviewsWithoutWritingAnything()
    {
        await using var factory = CreateFactory();
        var editor = await TestBrowser.CreateUser(factory, "Editor");
        var package = await TestData.CreatePackage(factory, editor.Id);
        var agent = await Agent(factory, editor.Id);
        var question = await FirstQuestion(factory, package.Id);
        var lastName = $"Чернетка{Guid.NewGuid():N}"[..20];
        var versionBefore = await CurrentVersion(factory, package.Id);

        var result = await Execute(factory, agent, package.Id, Request($$"""
            [ { "op": "updateQuestion", "questionId": {{question.Id}}, "set": { "text": "Попередній перегляд" } },
              { "op": "setQuestionAuthors", "questionId": {{question.Id}}, "authors": [ { "firstName": "Тимчасова", "lastName": "{{lastName}}" } ] },
              { "op": "setTags", "tags": [ "тег{{lastName}}" ] } ]
            """, dryRun: true));

        result.Status.Should().Be(ChangesetStatus.Ok, result.Error);
        result.Response!.DryRun.Should().BeTrue();
        result.Response.ChangesetId.Should().BeNull();
        result.Response.VersionBefore.Should().Be(versionBefore);
        result.Response.Changes.Should().HaveCount(3);

        (await FirstQuestion(factory, package.Id)).Text.Should().Be("Перше питання");
        (await Query(factory, db => db.Authors.AnyAsync(a => a.LastName == lastName))).Should().BeFalse();
        (await Query(factory, db => db.Tags.AnyAsync(t => t.Name.EndsWith(lastName)))).Should().BeFalse();
        (await Query(factory, db => db.PackageChangesets.AnyAsync(c => c.PackageId == package.Id))).Should().BeFalse();
        (await CurrentVersion(factory, package.Id)).Should().Be(versionBefore);
    }

    [SkippableFact]
    public async Task InvalidOperation_RollsBackEverything()
    {
        await using var factory = CreateFactory();
        var editor = await TestBrowser.CreateUser(factory, "Editor");
        var package = await TestData.CreatePackage(factory, editor.Id);
        var agent = await Agent(factory, editor.Id);
        var question = await FirstQuestion(factory, package.Id);

        var result = await Execute(factory, agent, package.Id, Request($$"""
            [ { "op": "updateQuestion", "questionId": {{question.Id}}, "set": { "text": "Не має зберегтися" } },
              { "op": "updateQuestion", "questionId": {{question.Id}}, "set": { "answer": null } } ]
            """));

        result.Status.Should().Be(ChangesetStatus.Invalid);
        result.OperationIndex.Should().Be(1);
        (await FirstQuestion(factory, package.Id)).Text.Should().Be("Перше питання");
        (await Query(factory, db => db.PackageChangesets.AnyAsync(c => c.PackageId == package.Id))).Should().BeFalse();
    }

    [SkippableFact]
    public async Task Retry_WithTheSameRequestId_ReturnsTheStoredResult_WithoutApplyingAgain()
    {
        await using var factory = CreateFactory();
        var editor = await TestBrowser.CreateUser(factory, "Editor");
        var package = await TestData.CreatePackage(factory, editor.Id);
        var agent = await Agent(factory, editor.Id);
        var question = await FirstQuestion(factory, package.Id);
        var requestId = Guid.NewGuid();
        var json = $$"""[ { "op": "updateQuestion", "questionId": {{question.Id}}, "set": { "comment": "Раз" } } ]""";

        var first = await Execute(factory, agent, package.Id, Request(json, requestId));
        var retry = await Execute(factory, agent, package.Id, Request(json, requestId));

        first.Status.Should().Be(ChangesetStatus.Ok, first.Error);
        retry.Status.Should().Be(ChangesetStatus.Ok, retry.Error);
        retry.Response!.Replayed.Should().BeTrue();
        retry.Response.ChangesetId.Should().Be(first.Response!.ChangesetId);
        retry.Response.VersionBefore.Should().Be(first.Response.VersionBefore);
        retry.Response.VersionAfter.Should().Be(first.Response.VersionAfter);
        retry.Response.TotalQuestions.Should().Be(first.Response.TotalQuestions);
        var web = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        JsonSerializer.Serialize(retry.Response.Changes, web).Should().Be(JsonSerializer.Serialize(first.Response.Changes, web));
        JsonSerializer.Serialize(retry.Response.Warnings, web).Should().Be(JsonSerializer.Serialize(first.Response.Warnings, web));
        (await Query(factory, db => db.PackageChangesets.CountAsync(c => c.PackageId == package.Id))).Should().Be(1);
    }

    [SkippableFact]
    public async Task RequestIdReused_ForADifferentBody_IsAConflict()
    {
        await using var factory = CreateFactory();
        var editor = await TestBrowser.CreateUser(factory, "Editor");
        var package = await TestData.CreatePackage(factory, editor.Id);
        var agent = await Agent(factory, editor.Id);
        var question = await FirstQuestion(factory, package.Id);
        var requestId = Guid.NewGuid();

        await Execute(factory, agent, package.Id, Request($$"""[ { "op": "updateQuestion", "questionId": {{question.Id}}, "set": { "comment": "А" } } ]""", requestId));
        var reused = await Execute(factory, agent, package.Id, Request($$"""[ { "op": "updateQuestion", "questionId": {{question.Id}}, "set": { "comment": "Б" } } ]""", requestId));

        reused.Status.Should().Be(ChangesetStatus.Conflict);
        (await FirstQuestion(factory, package.Id)).Comment.Should().Be("А");
    }

    [SkippableFact]
    public async Task Apply_RequiresARequestId()
    {
        await using var factory = CreateFactory();
        var editor = await TestBrowser.CreateUser(factory, "Editor");
        var package = await TestData.CreatePackage(factory, editor.Id);
        var agent = await Agent(factory, editor.Id);
        using var document = JsonDocument.Parse("""[ { "op": "setSharedEditors", "value": true } ]""");

        var result = await Execute(factory, agent, package.Id,
            new ChangesetRequest(null, null, null, DryRun: false, document.RootElement.Clone()));

        result.Status.Should().Be(ChangesetStatus.Invalid);
        result.Error.Should().Contain("requestId");
    }

    [SkippableFact]
    public async Task ExpectedVersion_StaleIsAConflict_CurrentIsApplied()
    {
        await using var factory = CreateFactory();
        var editor = await TestBrowser.CreateUser(factory, "Editor");
        var package = await TestData.CreatePackage(factory, editor.Id);
        var agent = await Agent(factory, editor.Id);
        var question = await FirstQuestion(factory, package.Id);
        var version = await CurrentVersion(factory, package.Id);

        // Someone edits the package in the UI after the agent read it.
        await Query(factory, async db =>
        {
            (await db.Questions.FirstAsync(q => q.Id == question.Id)).Comment = "Зміна з редактора";
            return await db.SaveChangesAsync();
        });

        var stale = await Execute(factory, agent, package.Id, Request(
            $$"""[ { "op": "updateQuestion", "questionId": {{question.Id}}, "set": { "comment": "Агент" } } ]""", expectedVersion: version));
        stale.Status.Should().Be(ChangesetStatus.Conflict);
        (await FirstQuestion(factory, package.Id)).Comment.Should().Be("Зміна з редактора");

        var current = await Execute(factory, agent, package.Id, Request(
            $$"""[ { "op": "updateQuestion", "questionId": {{question.Id}}, "set": { "comment": "Агент" } } ]""",
            expectedVersion: await CurrentVersion(factory, package.Id)));
        current.Status.Should().Be(ChangesetStatus.Ok, current.Error);
    }

    [SkippableFact]
    public async Task PackagesOutsideRightsOrAllowlist_AreNotFound()
    {
        await using var factory = CreateFactory();
        var editor = await TestBrowser.CreateUser(factory, "Editor");
        var other = await TestBrowser.CreateUser(factory, "Editor");
        var own = await TestData.CreatePackage(factory, editor.Id);
        var foreign = await TestData.CreatePackage(factory, other.Id);
        var json = """[ { "op": "updatePackage", "set": { "title": "Захоплено" } } ]""";

        var editorAgent = await Agent(factory, editor.Id);
        (await Execute(factory, editorAgent, foreign.Id, Request(json))).Status.Should().Be(ChangesetStatus.NotFound);
        (await Execute(factory, editorAgent, 999_999, Request(json))).Status.Should().Be(ChangesetStatus.NotFound);

        var restrictedAdmin = await Agent(factory, await TestData.AdminId(factory), packageIds: [own.Id]);
        (await Execute(factory, restrictedAdmin, foreign.Id, Request(json))).Status.Should().Be(ChangesetStatus.NotFound);

        (await Query(factory, db => db.Packages.AsNoTracking().FirstAsync(p => p.Id == foreign.Id))).Title.Should().Be("Тестовий пакет");
    }

    [SkippableFact]
    public async Task TotalQuestions_IsRecounted()
    {
        await using var factory = CreateFactory();
        var editor = await TestBrowser.CreateUser(factory, "Editor");
        var package = await TestData.CreatePackage(factory, editor.Id);
        await Query(factory, async db =>
        {
            (await db.Packages.FirstAsync(p => p.Id == package.Id)).TotalQuestions = 99; // drifted counter
            return await db.SaveChangesAsync();
        });
        var agent = await Agent(factory, editor.Id);

        var result = await Execute(factory, agent, package.Id, Request("""[ { "op": "updatePackage", "set": { "description": "Опис" } } ]"""));

        result.Response!.TotalQuestions.Should().Be(3);
        (await Query(factory, db => db.Packages.AsNoTracking().FirstAsync(p => p.Id == package.Id))).TotalQuestions.Should().Be(3);
    }

    [SkippableFact]
    public async Task WriteBudget_IsPerToken_AndCountsDryRuns()
    {
        await using var factory = CreateFactory(("AgentWritePerMinute", 1));
        var editor = await TestBrowser.CreateUser(factory, "Editor");
        var package = await TestData.CreatePackage(factory, editor.Id);
        var agent = await Agent(factory, editor.Id);
        var otherAgent = await Agent(factory, editor.Id);
        var json = """[ { "op": "updatePackage", "set": { "description": "Опис" } } ]""";

        (await Execute(factory, agent, package.Id, Request(json, dryRun: true))).Status.Should().Be(ChangesetStatus.Ok);
        (await Execute(factory, agent, package.Id, Request(json))).Status.Should().Be(ChangesetStatus.RateLimited);
        (await Execute(factory, otherAgent, package.Id, Request(json))).Status.Should().Be(ChangesetStatus.Ok);
    }

    [SkippableFact]
    public async Task ConcurrentChangesets_OnOnePackage_AreBothApplied()
    {
        await using var factory = CreateFactory();
        var editor = await TestBrowser.CreateUser(factory, "Editor");
        var package = await TestData.CreatePackage(factory, editor.Id);
        var agent = await Agent(factory, editor.Id);
        var questions = await Query(factory, db => db.Questions.AsNoTracking()
            .Where(q => q.Tour.PackageId == package.Id).OrderBy(q => q.Id).Select(q => q.Id).ToListAsync());

        var results = await Task.WhenAll(
            Execute(factory, agent, package.Id, Request($$"""[ { "op": "updateQuestion", "questionId": {{questions[0]}}, "set": { "comment": "Перший" } } ]""")),
            Execute(factory, agent, package.Id, Request($$"""[ { "op": "updateQuestion", "questionId": {{questions[1]}}, "set": { "comment": "Другий" } } ]""")));

        results.Should().OnlyContain(r => r.Status == ChangesetStatus.Ok);
        var comments = await Query(factory, db => db.Questions.AsNoTracking()
            .Where(q => questions.Contains(q.Id)).Select(q => q.Comment).ToListAsync());
        comments.Should().Contain(["Перший", "Другий"]);
        // Serialized: whichever ran second started from the other's result.
        var (a, b) = (results[0].Response!, results[1].Response!);
        (a.VersionAfter == b.VersionBefore || b.VersionAfter == a.VersionBefore).Should().BeTrue();
    }

    [SkippableFact]
    public async Task NewAuthor_CreatedConcurrentlyElsewhere_IsAConflict_NotAnError()
    {
        await using var factory = CreateFactory();
        var editor = await TestBrowser.CreateUser(factory, "Editor");
        var package = await TestData.CreatePackage(factory, editor.Id);
        var agent = await Agent(factory, editor.Id);
        var question = await FirstQuestion(factory, package.Id);
        var lastName = $"Гонка{Guid.NewGuid():N}"[..20];

        // Another writer inserts the same author and keeps its transaction open: the changeset's
        // insert of that name blocks on the unique index, then fails once the other side commits.
        // (Raw connection: the app's retrying execution strategy forbids ad-hoc EF transactions.)
        await using var other = new NpgsqlConnection(database.ConnectionString);
        await other.OpenAsync();
        await using var otherTransaction = await other.BeginTransactionAsync();
        await using (var insert = new NpgsqlCommand(
            "INSERT INTO \"Authors\" (\"FirstName\", \"LastName\") VALUES ('Одночасний', @last)", other, otherTransaction))
        {
            insert.Parameters.AddWithValue("last", lastName);
            await insert.ExecuteNonQueryAsync();
        }

        var changeset = Execute(factory, agent, package.Id, Request($$"""
            [ { "op": "setQuestionAuthors", "questionId": {{question.Id}}, "authors": [ { "firstName": "Одночасний", "lastName": "{{lastName}}" } ] } ]
            """));
        await WaitForALockWait(database.ConnectionString!);
        await otherTransaction.CommitAsync();
        var result = await changeset;

        result.Status.Should().Be(ChangesetStatus.Conflict, result.Error);
        (await FirstQuestion(factory, package.Id)).Authors.Should().NotContain(a => a.LastName == lastName);
    }

    [SkippableFact]
    public async Task StructuralChangeset_ReportsCreatedIds_AndAuditsDeletedContent()
    {
        await using var factory = CreateFactory();
        var editor = await TestBrowser.CreateUser(factory, "Editor");
        var package = await TestData.CreatePackage(factory, editor.Id);
        var agent = await Agent(factory, editor.Id);
        var tours = await Query(factory, db => db.Tours.AsNoTracking().Where(t => t.PackageId == package.Id)
            .OrderBy(t => t.OrderIndex).Select(t => t.Id).ToListAsync());
        var first = await FirstQuestion(factory, package.Id);

        var result = await Execute(factory, agent, package.Id, Request($$"""
            [ { "op": "deleteQuestion", "questionId": {{first.Id}} },
              { "op": "addQuestion", "tourId": {{tours[0]}}, "position": 0, "set": { "text": "Замість першого", "answer": "А" } },
              { "op": "addTour", "set": { "preamble": "Додатковий тур" }, "questions": [ { "set": { "text": "Д1", "answer": "1" } } ] },
              { "op": "deleteTour", "tourId": {{tours[1]}} } ]
            """));

        result.Status.Should().Be(ChangesetStatus.Ok, result.Error);
        var response = result.Response!;
        response.Created.Select(c => (c.OperationIndex, c.Entity)).Should().Equal((1, "question"), (2, "tour"), (2, "question"));
        response.Created.Should().OnlyContain(c => c.Id > 0, "ids are real after saving");
        response.TotalQuestions.Should().Be(3);
        response.VersionAfter.Should().Be(await CurrentVersion(factory, package.Id));

        (await Query(factory, db => db.Questions.AnyAsync(q => q.Id == first.Id))).Should().BeFalse();
        (await Query(factory, db => db.Tours.AnyAsync(t => t.Id == tours[1]))).Should().BeFalse();
        (await Query(factory, db => db.Blocks.AnyAsync(b => b.TourId == tours[1]))).Should().BeFalse();

        var audit = await Query(factory, db => db.PackageChangesets.AsNoTracking().SingleAsync(c => c.Id == response.ChangesetId));
        var stored = JsonSerializer.Deserialize<List<ChangeDto>>(audit.ChangesJson, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        stored.Single(c => c.Kind == "deleted" && c.Entity == "question").Snapshot!["text"]!.GetValue<string>().Should().Be("Перше питання");
        stored.Single(c => c.Kind == "deleted" && c.Entity == "tour").Snapshot!["blocks"]!.AsArray().Should().ContainSingle();
        stored.Where(c => c.Kind == "added").Should().OnlyContain(c => c.Id > 0 && c.Snapshot!["id"]!.GetValue<int>() == c.Id);
    }

    [SkippableFact]
    public async Task DeletionSnapshots_GetTheRealIdsOfAuthorsCreatedInTheSameChangeset()
    {
        await using var factory = CreateFactory();
        var editor = await TestBrowser.CreateUser(factory, "Editor");
        var package = await TestData.CreatePackage(factory, editor.Id);
        var agent = await Agent(factory, editor.Id);
        var first = await FirstQuestion(factory, package.Id);
        var tours = await Query(factory, db => db.Tours.AsNoTracking().Where(t => t.PackageId == package.Id)
            .OrderBy(t => t.OrderIndex).Select(t => t.Id).ToListAsync());
        var suffix = Guid.NewGuid().ToString("N")[..10];

        var result = await Execute(factory, agent, package.Id, Request($$"""
            [ { "op": "setQuestionAuthors", "questionId": {{first.Id}}, "authors": [ { "firstName": "Свіжий", "lastName": "А{{suffix}}" } ] },
              { "op": "deleteQuestion", "questionId": {{first.Id}} },
              { "op": "setTourEditors", "tourId": {{tours[1]}}, "authors": [ { "firstName": "Свіжий", "lastName": "Б{{suffix}}" } ] },
              { "op": "deleteTour", "tourId": {{tours[1]}} } ]
            """));

        result.Status.Should().Be(ChangesetStatus.Ok, result.Error);
        var audit = await Query(factory, db => db.PackageChangesets.AsNoTracking().SingleAsync(c => c.Id == result.Response!.ChangesetId));
        var stored = JsonSerializer.Deserialize<List<ChangeDto>>(audit.ChangesJson, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        stored.Single(c => c.Kind == "deleted" && c.Entity == "question").Snapshot!["authors"]!.AsArray()
            .Single()!["id"]!.GetValue<int>().Should().BeGreaterThan(0);
        stored.Single(c => c.Kind == "deleted" && c.Entity == "tour").Snapshot!["editors"]!.AsArray()
            .Single()!["id"]!.GetValue<int>().Should().BeGreaterThan(0);
    }

    [SkippableFact]
    public async Task StructuralChangeset_OnAPackageWithResults_IsRejected()
    {
        await using var factory = CreateFactory();
        var editor = await TestBrowser.CreateUser(factory, "Editor");
        var package = await TestData.CreatePackage(factory, editor.Id);
        await Query(factory, async db =>
        {
            db.ResultsSources.Add(new ResultsSource { PackageId = package.Id, Url = "https://rating.example/2" });
            return await db.SaveChangesAsync();
        });
        var agent = await Agent(factory, editor.Id);
        var first = await FirstQuestion(factory, package.Id);

        var structural = await Execute(factory, agent, package.Id, Request($$"""[ { "op": "deleteQuestion", "questionId": {{first.Id}} } ]"""));
        var field = await Execute(factory, agent, package.Id, Request($$"""[ { "op": "updateQuestion", "questionId": {{first.Id}}, "set": { "comment": "OK" } } ]"""));

        structural.Status.Should().Be(ChangesetStatus.Invalid);
        structural.Error.Should().Contain("results");
        field.Status.Should().Be(ChangesetStatus.Ok, field.Error);
    }

    [SkippableFact]
    public async Task UserCanEdit_UsesCurrentDatabaseRoles()
    {
        await using var factory = CreateFactory();
        var owner = await TestBrowser.CreateUser(factory, "Editor");
        var admin = await TestBrowser.CreateUser(factory, "Admin");
        var plain = await TestBrowser.CreateUser(factory, role: null);
        var package = await TestData.CreatePackage(factory, owner.Id);

        async Task<bool> CanEdit(string? userId)
        {
            using var scope = factory.Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<PackageChangesetService>().UserCanEdit(userId, package.Id);
        }

        (await CanEdit(owner.Id)).Should().BeTrue();
        (await CanEdit(admin.Id)).Should().BeTrue();
        (await CanEdit(plain.Id)).Should().BeFalse();
        (await CanEdit(null)).Should().BeFalse();

        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<ApplicationUser>>();
            var user = (await users.FindByIdAsync(admin.Id))!;
            await users.RemoveFromRoleAsync(user, "Admin");
            await users.AddToRoleAsync(user, "Editor");
        }

        (await CanEdit(admin.Id)).Should().BeFalse("a demoted admin keeps only own packages, whatever the session says");
    }

    [SkippableFact]
    public async Task TagNames_AreUniqueCaseInsensitively_InTheDatabase()
    {
        database.SkipIfUnavailable();
        var name = $"Наука{Guid.NewGuid():N}"[..20];
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();

        // Make sure the migrations ran (a factory starts the app once).
        await using (var factory = CreateFactory())
            _ = factory.Services;

        await using (var first = new NpgsqlCommand("INSERT INTO \"Tags\" (\"Name\") VALUES (@n)", connection))
        {
            first.Parameters.AddWithValue("n", name);
            await first.ExecuteNonQueryAsync();
        }

        await using var second = new NpgsqlCommand("INSERT INTO \"Tags\" (\"Name\") VALUES (@n)", connection);
        second.Parameters.AddWithValue("n", name.ToUpperInvariant());
        var act = () => second.ExecuteNonQueryAsync();

        (await act.Should().ThrowAsync<PostgresException>()).Which.ConstraintName.Should().Be("IX_Tags_Name_Lower");
    }

    [SkippableFact]
    public async Task NewTag_CreatedConcurrentlyInAnotherCase_IsAConflict()
    {
        await using var factory = CreateFactory();
        var editor = await TestBrowser.CreateUser(factory, "Editor");
        var package = await TestData.CreatePackage(factory, editor.Id);
        var agent = await Agent(factory, editor.Id);
        var name = $"Тег{Guid.NewGuid():N}"[..20];

        await using var other = new NpgsqlConnection(database.ConnectionString);
        await other.OpenAsync();
        await using var otherTransaction = await other.BeginTransactionAsync();
        await using (var insert = new NpgsqlCommand("INSERT INTO \"Tags\" (\"Name\") VALUES (@n)", other, otherTransaction))
        {
            insert.Parameters.AddWithValue("n", name.ToUpperInvariant());
            await insert.ExecuteNonQueryAsync();
        }

        var changeset = Execute(factory, agent, package.Id, Request($$"""[ { "op": "setTags", "tags": [ "{{name}}" ] } ]"""));
        await WaitForALockWait(database.ConnectionString!);
        await otherTransaction.CommitAsync();
        var result = await changeset;

        result.Status.Should().Be(ChangesetStatus.Conflict, result.Error);
    }

    /// <summary>Polls pg_stat_activity until some backend waits on a lock (the changeset's blocked insert).</summary>
    private static async Task WaitForALockWait(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            await using var command = new NpgsqlCommand(
                "SELECT count(*) FROM pg_stat_activity WHERE wait_event_type = 'Lock' AND datname = current_database()", connection);
            if ((long)(await command.ExecuteScalarAsync())! > 0)
                return;
            await Task.Delay(50);
        }

        throw new TimeoutException("The changeset never blocked on the competing insert.");
    }

    [SkippableFact]
    public async Task Replay_AfterLosingRights_IsNotFound()
    {
        await using var factory = CreateFactory();
        var owner = await TestBrowser.CreateUser(factory, "Editor");
        var admin = await TestBrowser.CreateUser(factory, "Admin");
        var package = await TestData.CreatePackage(factory, owner.Id);
        var agent = await Agent(factory, admin.Id);
        var requestId = Guid.NewGuid();
        var json = """[ { "op": "updatePackage", "set": { "description": "Від адміна" } } ]""";
        (await Execute(factory, agent, package.Id, Request(json, requestId))).Status.Should().Be(ChangesetStatus.Ok);

        // Demoted to editor: the same token now acts with editor rights.
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<ApplicationUser>>();
            var user = (await users.FindByIdAsync(admin.Id))!;
            await users.RemoveFromRoleAsync(user, "Admin");
            await users.AddToRoleAsync(user, "Editor");
        }
        var demoted = await Agent(factory, admin.Id);
        var sameToken = new ClaimsPrincipal(new ClaimsIdentity(
            demoted.Claims.Where(c => c.Type != AgentClaims.TokenId)
                .Append(agent.FindFirst(AgentClaims.TokenId)!), PersonalAccessTokenAuthenticationOptions.Scheme));

        var replay = await Execute(factory, sameToken, package.Id, Request(json, requestId));

        replay.Status.Should().Be(ChangesetStatus.NotFound);
        replay.Response.Should().BeNull("the stored diff of a package outside the caller's rights is not returned");
    }

    [SkippableFact]
    public async Task RequestId_ReusedOnAnotherEditablePackage_IsAConflict()
    {
        await using var factory = CreateFactory();
        var editor = await TestBrowser.CreateUser(factory, "Editor");
        var first = await TestData.CreatePackage(factory, editor.Id);
        var second = await TestData.CreatePackage(factory, editor.Id);
        var agent = await Agent(factory, editor.Id);
        var requestId = Guid.NewGuid();
        var json = """[ { "op": "updatePackage", "set": { "description": "Опис" } } ]""";

        (await Execute(factory, agent, first.Id, Request(json, requestId))).Status.Should().Be(ChangesetStatus.Ok);
        (await Execute(factory, agent, second.Id, Request(json, requestId))).Status.Should().Be(ChangesetStatus.Conflict);
    }

    [SkippableFact]
    public async Task FailureWhileWritingTheAudit_RollsBackTheAlreadySavedMutations()
    {
        database.SkipIfUnavailable();
        await using var factory = new QuestionsHubAppFactory(database, configureServices: services =>
            services.ConfigureDbContext<QuestionsHubDbContext>(options => options.AddInterceptors(new FailAuditInsert())));
        var editor = await TestBrowser.CreateUser(factory, "Editor");
        var package = await TestData.CreatePackage(factory, editor.Id);
        var agent = await Agent(factory, editor.Id);
        var question = await FirstQuestion(factory, package.Id);
        var lastName = $"Відкат{Guid.NewGuid():N}"[..20];

        var act = () => Execute(factory, agent, package.Id, Request($$"""
            [ { "op": "updateQuestion", "questionId": {{question.Id}}, "set": { "text": "Не має лишитися" } },
              { "op": "setQuestionAuthors", "questionId": {{question.Id}}, "authors": [ { "firstName": "Відкат", "lastName": "{{lastName}}" } ] } ]
            """));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*audit*");
        (await FirstQuestion(factory, package.Id)).Text.Should().Be("Перше питання", "the first SaveChanges is rolled back");
        (await Query(factory, db => db.Authors.AnyAsync(a => a.LastName == lastName))).Should().BeFalse();
        (await Query(factory, db => db.PackageChangesets.AnyAsync(c => c.PackageId == package.Id))).Should().BeFalse();
    }

    /// <summary>Fails the second save of a changeset (the audit row), after the mutations were saved.</summary>
    private sealed class FailAuditInsert : Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor
    {
        public override ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int>> SavingChangesAsync(
            Microsoft.EntityFrameworkCore.Diagnostics.DbContextEventData eventData,
            Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<PackageChangeset>().Any(e => e.State == EntityState.Added))
                throw new InvalidOperationException("Simulated failure while writing the audit row.");

            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
