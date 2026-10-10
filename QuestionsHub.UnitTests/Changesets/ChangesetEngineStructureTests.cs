using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using QuestionsHub.Blazor.Domain;
using QuestionsHub.Blazor.Infrastructure.AgentApi.Changesets;
using Xunit;

namespace QuestionsHub.UnitTests.Changesets;

/// <summary>Structural operations: adding, deleting and moving questions and tours, tour types.</summary>
public class ChangesetEngineStructureTests : ChangesetTestBase
{
    private static async Task<ChangesetValidationException> Rejects(Session session, string json)
    {
        var act = () => Apply(session, json);
        return (await act.Should().ThrowAsync<ChangesetValidationException>()).Which;
    }

    /// <summary>Tour → questions as (number, text) in display order (blocks first, by block order).</summary>
    private async Task<List<(string Tour, string Number, string Text)>> Layout(int packageId)
    {
        var package = await LoadPackage(packageId);
        return package.Tours.OrderBy(t => t.OrderIndex)
            .SelectMany(t => t.Questions
                .OrderBy(q => q.BlockId == null ? int.MaxValue : t.Blocks.Single(b => b.Id == q.BlockId).OrderIndex)
                .ThenBy(q => q.OrderIndex)
                .Select(q => (t.Number, q.Number, q.Text)))
            .ToList();
    }

    #region addQuestion

    [Fact]
    public async Task AddQuestion_Appends_PrefillsTourEditors_AndRenumbers()
    {
        var ids = await SeedWww();

        var changes = await ApplyAndSave(ids.PackageId, $$"""
            [ { "op": "addQuestion", "tourId": {{ids.Tour1}}, "set": { "text": " Нове ", "answer": "Так" } } ]
            """);

        (await Layout(ids.PackageId)).Should().Equal(
            ("1", "1", "Перше"), ("1", "2", "Друге"), ("1", "3", "Нове"), ("2", "4", "Третє"));

        var added = changes.Single(c => c.Kind == "added");
        added.OperationIndex.Should().Be(0);
        added.Id.Should().NotBeNull();
        added.Label.Should().Be("Тур 1, запитання 3");
        added.Snapshot!["text"]!.GetValue<string>().Should().Be("Нове");
        added.Snapshot["authors"]!.AsArray().Single()!["name"]!.GetValue<string>().Should().Be("Анна Коваль");
        changes.Should().Contain(c => c.Id == ids.Q3 && c.Field == "number" && c.OperationIndex == null);
        (await LoadPackage(ids.PackageId)).TotalQuestions.Should().Be(4);
    }

    [Fact]
    public async Task AddQuestion_AtAPosition_ShiftsAndRecordsSiblings()
    {
        var ids = await SeedWww();

        var changes = await ApplyAndSave(ids.PackageId, $$"""
            [ { "op": "addQuestion", "tourId": {{ids.Tour1}}, "position": 0, "set": { "text": "Нульове", "answer": "А" } } ]
            """);

        (await Layout(ids.PackageId)).Take(3).Select(x => x.Text).Should().Equal("Нульове", "Перше", "Друге");
        changes.Should().Contain(c => c.Id == ids.Q1 && c.Field == "orderIndex" && c.OperationIndex == 0
            && c.Before!.GetValue<int>() == 0 && c.After!.GetValue<int>() == 1);
    }

    [Fact]
    public async Task AddQuestion_WithAuthors_UsesThem_AndTheEditorsTextRules()
    {
        var ids = await SeedWww();

        await ApplyAndSave(ids.PackageId, $$"""
            [ { "op": "addQuestion", "tourId": {{ids.Tour1}}, "authors": [ { "id": {{ids.PetroId}} } ],
                "set": { "text": "Пам'ять", "answer": "А", "source": "https://x/Пам'ять" } } ]
            """);

        var package = await LoadPackage(ids.PackageId);
        var question = package.Tours.Single(t => t.Id == ids.Tour1).Questions.Single(q => q.Text.StartsWith("Пам", StringComparison.Ordinal));
        question.Text.Should().Be("Памʼять");
        question.Source.Should().Be("https://x/Пам'ять");
        question.Authors.Select(a => a.Id).Should().Equal(ids.PetroId);
    }

    [Fact]
    public async Task AddQuestion_TourWithBlocks_NeedsOneOfItsBlocks_AndPrefillsBlockEditors()
    {
        var ids = await SeedWww();
        using (var session = await Open(ids.PackageId))
        {
            (await Rejects(session, $$"""[ { "op": "addQuestion", "tourId": {{ids.Tour2}} } ]""")).Message.Should().Contain("blockId");
            (await Rejects(session, $$"""[ { "op": "addQuestion", "tourId": {{ids.Tour1}}, "blockId": {{ids.Block}} } ]"""))
                .Message.Should().Contain("does not belong");
        }

        await ApplyAndSave(ids.PackageId, $$"""[ { "op": "addQuestion", "tourId": {{ids.Tour2}}, "blockId": {{ids.Block}} } ]""");

        var block = (await LoadPackage(ids.PackageId)).Tours.Single(t => t.Id == ids.Tour2);
        var added = block.Questions.Single(q => q.Id != ids.Q3);
        added.BlockId.Should().Be(ids.Block);
        added.Authors.Select(a => a.Id).Should().Equal(ids.PetroId);
    }

    [Fact]
    public async Task AddQuestion_Shvager_ValuesFollowPositions()
    {
        var ids = await SeedShvager();

        await ApplyAndSave(ids.PackageId, $$"""
            [ { "op": "addQuestion", "tourId": {{ids.Theme2}}, "position": 0, "set": { "text": "Нове", "answer": "Н" } } ]
            """);

        var theme = (await LoadPackage(ids.PackageId)).Tours.Single(t => t.Id == ids.Theme2);
        theme.Questions.OrderBy(q => q.OrderIndex).Select(q => (q.Number, q.Text)).Should().Equal(("10", "Нове"), ("20", "А"), ("30", "Б"));
    }

    #endregion

    #region deleteQuestion / moveQuestion

    [Fact]
    public async Task DeleteQuestion_Renumbers_AndKeepsAFullSnapshot()
    {
        var ids = await SeedWww();

        var changes = await ApplyAndSave(ids.PackageId, $$"""[ { "op": "deleteQuestion", "questionId": {{ids.Q1}} } ]""");

        (await Layout(ids.PackageId)).Should().Equal(("1", "1", "Друге"), ("2", "2", "Третє"));
        (await LoadPackage(ids.PackageId)).TotalQuestions.Should().Be(2);

        var deleted = changes.Single(c => c.Kind == "deleted");
        deleted.Id.Should().Be(ids.Q1);
        deleted.Label.Should().Be("Тур 1, запитання 1");
        deleted.Snapshot!["text"]!.GetValue<string>().Should().Be("Перше");
        deleted.Snapshot["comment"]!.GetValue<string>().Should().Be("Коментар");
        deleted.Snapshot["tourId"]!.GetValue<int>().Should().Be(ids.Tour1);
        deleted.Snapshot["authors"]!.AsArray().Single()!["id"]!.GetValue<int>().Should().Be(ids.AnnaId);
    }

    [Fact]
    public async Task DeleteQuestion_Twice_IsRejected()
    {
        var ids = await SeedWww();
        using var session = await Open(ids.PackageId);

        (await Rejects(session, $$"""
            [ { "op": "deleteQuestion", "questionId": {{ids.Q1}} }, { "op": "deleteQuestion", "questionId": {{ids.Q1}} } ]
            """)).OperationIndex.Should().Be(1);
    }

    [Fact]
    public async Task MoveQuestion_IntoAnotherToursBlock_RecordsTheLocation()
    {
        var ids = await SeedWww();

        var changes = await ApplyAndSave(ids.PackageId, $$"""
            [ { "op": "moveQuestion", "questionId": {{ids.Q1}}, "tourId": {{ids.Tour2}}, "blockId": {{ids.Block}}, "position": 0 } ]
            """);

        (await Layout(ids.PackageId)).Should().Equal(("1", "1", "Друге"), ("2", "2", "Перше"), ("2", "3", "Третє"));
        var moved = await LoadQuestion(ids.Q1);
        moved.BlockId.Should().Be(ids.Block);

        var location = changes.Single(c => c.Field == "location");
        location.Before!["tourId"]!.GetValue<int>().Should().Be(ids.Tour1);
        location.Before["blockId"].Should().BeNull();
        location.After!["tourId"]!.GetValue<int>().Should().Be(ids.Tour2);
        location.After["blockId"]!.GetValue<int>().Should().Be(ids.Block);
        location.After["position"]!.GetValue<int>().Should().Be(0);
    }

    [Fact]
    public async Task MoveQuestion_WithinItsTour()
    {
        var ids = await SeedWww();

        await ApplyAndSave(ids.PackageId, $$"""[ { "op": "moveQuestion", "questionId": {{ids.Q2}}, "tourId": {{ids.Tour1}}, "position": 0 } ]""");

        (await Layout(ids.PackageId)).Take(2).Should().Equal(("1", "1", "Друге"), ("1", "2", "Перше"));
    }

    #endregion

    #region Tours

    [Fact]
    public async Task AddTour_WithInlineQuestions_AtTheStart()
    {
        var ids = await SeedWww();

        var changes = await ApplyAndSave(ids.PackageId, """
            [ { "op": "addTour", "position": 0, "set": { "preamble": "Тестери" },
                "editors": [ { "firstName": "Нова", "lastName": "Редакторка" } ],
                "questions": [ { "set": { "text": "А1", "answer": "1" } }, { "set": { "text": "А2", "answer": "2" } } ] } ]
            """);

        (await Layout(ids.PackageId)).Select(x => (x.Tour, x.Number, x.Text)).Should().Equal(
            ("1", "1", "А1"), ("1", "2", "А2"), ("2", "3", "Перше"), ("2", "4", "Друге"), ("3", "5", "Третє"));

        var package = await LoadPackage(ids.PackageId);
        var newTour = package.Tours.Single(t => t.OrderIndex == 0);
        newTour.Preamble.Should().Be("Тестери");
        newTour.Questions.Should().OnlyContain(q => q.Authors.Single().LastName == "Редакторка", "inline questions inherit the tour editors");

        changes.Where(c => c.Kind == "added").Select(c => c.Entity).Should().Equal("tour", "question", "question");
        changes.First(c => c.Kind == "added").Snapshot!["questions"]!.AsArray().Should().HaveCount(2);
    }

    [Fact]
    public async Task AddTour_TypeRules()
    {
        var www = await SeedWww();
        var shvager = await SeedShvager();

        await ApplyAndSave(www.PackageId, """[ { "op": "addTour", "type": "warmup" } ]""");
        using (var session = await Open(www.PackageId))
            (await Rejects(session, """[ { "op": "addTour", "type": "warmup" } ]""")).Message.Should().Contain("setTourType");
        using (var session = await Open(shvager.PackageId))
            (await Rejects(session, """[ { "op": "addTour", "type": "shootout" } ]""")).Message.Should().Contain("Своя гра");

        var package = await LoadPackage(www.PackageId);
        var warmup = package.Tours.Single(t => t.Type == TourType.Warmup);
        warmup.OrderIndex.Should().Be(0, "renumbering puts the warmup first");
        warmup.Number.Should().Be("0");

        await ApplyAndSave(shvager.PackageId, """[ { "op": "addTour", "set": { "title": "Нова тема" } } ]""");
        (await LoadPackage(shvager.PackageId)).Tours.OrderBy(t => t.OrderIndex).Last().Title.Should().Be("Нова тема");
    }

    [Fact]
    public async Task DeleteTour_RemovesItsBlocksAndQuestions_WithADeepSnapshot()
    {
        var ids = await SeedWww();

        var changes = await ApplyAndSave(ids.PackageId, $$"""[ { "op": "deleteTour", "tourId": {{ids.Tour2}} } ]""");

        using var check = DbFactory.CreateDbContext();
        (await check.Questions.AnyAsync(q => q.Id == ids.Q3)).Should().BeFalse();
        (await check.Blocks.AnyAsync(b => b.Id == ids.Block)).Should().BeFalse();
        (await LoadPackage(ids.PackageId)).TotalQuestions.Should().Be(2);

        var snapshot = changes.Single(c => c.Kind == "deleted").Snapshot!;
        snapshot["blocks"]!.AsArray().Single()!["editors"]!.AsArray().Should().ContainSingle();
        snapshot["questions"]!.AsArray().Single()!["text"]!.GetValue<string>().Should().Be("Третє");
    }

    [Fact]
    public async Task MoveTour_Reorders_AndRenumbers()
    {
        var ids = await SeedWww();

        await ApplyAndSave(ids.PackageId, $$"""[ { "op": "moveTour", "tourId": {{ids.Tour2}}, "position": 0 } ]""");

        (await Layout(ids.PackageId)).Should().Equal(("1", "1", "Третє"), ("2", "2", "Перше"), ("2", "3", "Друге"));
    }

    [Fact]
    public async Task SetTourType_Warmup_GoesFirst_AndAnotherWarmupBecomesRegular()
    {
        var ids = await SeedWww();
        await ApplyAndSave(ids.PackageId, $$"""[ { "op": "setTourType", "tourId": {{ids.Tour1}}, "type": "warmup" } ]""");

        var changes = await ApplyAndSave(ids.PackageId, $$"""[ { "op": "setTourType", "tourId": {{ids.Tour2}}, "type": "warmup" } ]""");

        var package = await LoadPackage(ids.PackageId);
        package.Tours.Single(t => t.Id == ids.Tour2).Type.Should().Be(TourType.Warmup);
        package.Tours.Single(t => t.Id == ids.Tour2).OrderIndex.Should().Be(0);
        package.Tours.Single(t => t.Id == ids.Tour1).Type.Should().Be(TourType.Regular);
        changes.Where(c => c.Field == "type").Should().HaveCount(2).And.OnlyContain(c => c.OperationIndex == 0);
    }

    [Fact]
    public async Task SetTourType_Shvager_OnlyRegular()
    {
        var ids = await SeedShvager();
        using var session = await Open(ids.PackageId);

        (await Rejects(session, $$"""[ { "op": "setTourType", "tourId": {{ids.Theme1}}, "type": "warmup" } ]"""))
            .Message.Should().Contain("Своя гра");
    }

    #endregion

    #region Regressions from review

    [Fact]
    public async Task QuestionAdded_ThenRemovedWithItsTour_LeavesNoTrace()
    {
        var ids = await SeedWww();

        using (var session = await Open(ids.PackageId))
        {
            await Apply(session, $$"""
                [ { "op": "addQuestion", "tourId": {{ids.Tour1}}, "set": { "text": "Тимчасове", "answer": "А" } },
                  { "op": "deleteTour", "tourId": {{ids.Tour1}} } ]
                """);
            var preview = ChangeFormatter.ToDtos(session.Engine.Changes, session.Package, session.Context);
            preview.Should().NotContain(c => c.Kind == "added");
            preview.Single(c => c.Kind == "deleted").Snapshot!["questions"]!.AsArray()
                .Select(q => q!["text"]!.GetValue<string>()).Should().Equal("Перше", "Друге");
        }

        var changes = await ApplyAndSave(ids.PackageId, $$"""
            [ { "op": "addQuestion", "tourId": {{ids.Tour1}}, "set": { "text": "Тимчасове", "answer": "А" } },
              { "op": "deleteTour", "tourId": {{ids.Tour1}} } ]
            """);
        changes.Should().NotContain(c => c.Kind == "added");
        using var check = DbFactory.CreateDbContext();
        (await check.Questions.AnyAsync(q => q.Text == "Тимчасове")).Should().BeFalse();
    }

    [Fact]
    public async Task RecordsOfEntitiesDeletedLater_KeepTheirLabels()
    {
        var ids = await SeedWww();

        var changes = await ApplyAndSave(ids.PackageId, $$"""
            [ { "op": "updateQuestion", "questionId": {{ids.Q3}}, "set": { "comment": "Перед видаленням" } },
              { "op": "updateBlock", "blockId": {{ids.Block}}, "set": { "preamble": "П" } },
              { "op": "deleteTour", "tourId": {{ids.Tour2}} } ]
            """);

        changes.Single(c => c.Field == "comment").Label.Should().Be("Тур 2, запитання 3");
        changes.Single(c => c.Field == "preamble").Label.Should().Be("Тур 2, блок «Блок»");
        changes.Single(c => c.Kind == "deleted").Label.Should().Be("Тур 2");
    }

    [Fact]
    public async Task ManualNumbering_NewQuestionKeepsThePlaceholderNumber()
    {
        var ids = await SeedWww();

        await ApplyAndSave(ids.PackageId, $$"""
            [ { "op": "setNumberingMode", "mode": "manual" }, { "op": "addQuestion", "tourId": {{ids.Tour1}} } ]
            """);

        (await LoadPackage(ids.PackageId)).Tours.Single(t => t.Id == ids.Tour1).Questions
            .Single(q => q.Id != ids.Q1 && q.Id != ids.Q2).Number.Should().Be("0", "the editor does the same in Manual mode");
    }

    [Fact]
    public async Task Shvager_PinnedValues_SurviveStructuralChanges()
    {
        var ids = await SeedShvager();
        using (var context = DbFactory.CreateDbContext())
        {
            (await context.Questions.FirstAsync(q => q.Id == ids.Theme2Questions[1])).Number = "10-30";
            await context.SaveChangesAsync();
        }

        await ApplyAndSave(ids.PackageId, $$"""[ { "op": "addQuestion", "tourId": {{ids.Theme2}}, "position": 0 } ]""");

        var theme = (await LoadPackage(ids.PackageId)).Tours.Single(t => t.Id == ids.Theme2);
        theme.Questions.OrderBy(q => q.OrderIndex).Select(q => q.Number).Should().Equal("10", "20", "10-30");
    }

    [Fact]
    public async Task Positions_BeyondTheEnd_Append_AndMovesWithinAContainerReportPositions()
    {
        var ids = await SeedWww();

        var changes = await ApplyAndSave(ids.PackageId, $$"""
            [ { "op": "addQuestion", "tourId": {{ids.Tour1}}, "position": 99, "set": { "text": "Кінець", "answer": "К" } },
              { "op": "moveQuestion", "questionId": {{ids.Q2}}, "tourId": {{ids.Tour1}}, "position": 0 } ]
            """);

        (await Layout(ids.PackageId)).Take(3).Select(x => x.Text).Should().Equal("Друге", "Перше", "Кінець");
        var location = changes.Single(c => c.Field == "location");
        location.Before!["position"]!.GetValue<int>().Should().Be(1);
        location.After!["position"]!.GetValue<int>().Should().Be(0);
        location.After["tourId"]!.GetValue<int>().Should().Be(ids.Tour1);
    }

    [Fact]
    public async Task BlockToBlockMove_ThenDeletingTheTour_SnapshotsTheMovedQuestion()
    {
        var ids = await SeedWww();
        int block2;
        using (var context = DbFactory.CreateDbContext())
        {
            var block = new Block { TourId = ids.Tour2, OrderIndex = 1, Name = "Блок 2" };
            context.Blocks.Add(block);
            await context.SaveChangesAsync();
            block2 = block.Id;
        }

        var changes = await ApplyAndSave(ids.PackageId, $$"""
            [ { "op": "moveQuestion", "questionId": {{ids.Q3}}, "tourId": {{ids.Tour2}}, "blockId": {{block2}} },
              { "op": "deleteTour", "tourId": {{ids.Tour2}} } ]
            """);

        var snapshot = changes.Single(c => c.Kind == "deleted").Snapshot!;
        snapshot["blocks"]!.AsArray().Should().HaveCount(2);
        snapshot["questions"]!.AsArray().Single()!["blockId"]!.GetValue<int>().Should().Be(block2);
        using var check = DbFactory.CreateDbContext();
        (await check.Blocks.AnyAsync(b => b.TourId == ids.Tour2)).Should().BeFalse();
    }

    #endregion

    #region Guards

    [Fact]
    public async Task StructuralOperations_AreRejected_WhenResultsAreAttached_FieldEditsAreNot()
    {
        var ids = await SeedWww();

        foreach (var json in new[]
        {
            $$"""[ { "op": "addQuestion", "tourId": {{ids.Tour1}} } ]""",
            $$"""[ { "op": "deleteQuestion", "questionId": {{ids.Q1}} } ]""",
            $$"""[ { "op": "moveQuestion", "questionId": {{ids.Q1}}, "tourId": {{ids.Tour1}}, "position": 1 } ]""",
            """[ { "op": "addTour" } ]""",
            $$"""[ { "op": "deleteTour", "tourId": {{ids.Tour1}} } ]""",
            $$"""[ { "op": "moveTour", "tourId": {{ids.Tour1}}, "position": 1 } ]""",
            $$"""[ { "op": "setTourType", "tourId": {{ids.Tour1}}, "type": "warmup" } ]""",
        })
        {
            using var session = await Open(ids.PackageId, hasResults: true);
            (await Rejects(session, json)).Message.Should().Contain("results", json);
        }

        using var fieldSession = await Open(ids.PackageId, hasResults: true);
        await Apply(fieldSession, $$"""[ { "op": "updateQuestion", "questionId": {{ids.Q1}}, "set": { "comment": "OK" } } ]""");
        fieldSession.Engine.Changes.Should().ContainSingle();
    }

    [Fact]
    public async Task EntitiesCreatedInTheChangeset_CannotBeAddressedById()
    {
        var ids = await SeedWww();
        int nextQuestionId;
        using (var context = DbFactory.CreateDbContext())
            nextQuestionId = await context.Questions.MaxAsync(q => q.Id) + 1;

        using var session = await Open(ids.PackageId);
        (await Rejects(session, $$"""
            [ { "op": "addQuestion", "tourId": {{ids.Tour1}} },
              { "op": "updateQuestion", "questionId": {{nextQuestionId}}, "set": { "text": "x" } } ]
            """)).OperationIndex.Should().Be(1);
    }

    [Fact]
    public async Task DryRun_ReportsCreatedEntitiesWithoutIds()
    {
        var ids = await SeedWww();
        using var session = await Open(ids.PackageId);

        await Apply(session, $$"""[ { "op": "addQuestion", "tourId": {{ids.Tour1}}, "set": { "text": "Т", "answer": "В" } } ]""");

        var added = ChangeFormatter.ToDtos(session.Engine.Changes, session.Package, session.Context).Single(c => c.Kind == "added");
        added.Id.Should().BeNull();
        added.Snapshot!["id"].Should().BeNull();
        added.Label.Should().Be("Тур 1, запитання 3");
    }

    #endregion
}
