using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using QuestionsHub.Blazor.Domain;
using QuestionsHub.Blazor.Infrastructure.AgentApi.Changesets;
using Xunit;

namespace QuestionsHub.UnitTests.Changesets;

/// <summary>Field and relationship operations of the changeset engine (no structural changes).</summary>
public class ChangesetEngineFieldTests : ChangesetTestBase
{
    private static async Task<ChangesetValidationException> Rejects(Session session, string json)
    {
        var act = () => Apply(session, json);
        return (await act.Should().ThrowAsync<ChangesetValidationException>()).Which;
    }

    #region updateQuestion

    [Fact]
    public async Task UpdateQuestion_NormalizesLikeTheEditor_AndRecordsOnlyRealChanges()
    {
        var ids = await SeedWww();

        var changes = await ApplyAndSave(ids.PackageId, $$"""
            [ { "op": "updateQuestion", "questionId": {{ids.Q1}}, "set": {
                  "text": "  Пам'ять — це  ",
                  "answer": "А",
                  "source": "https://uk.wikipedia.org/wiki/Пам'ять",
                  "comment": null
            } } ]
            """);

        var question = await LoadQuestion(ids.Q1);
        question.Text.Should().Be("Памʼять - це"); // apostrophe, nbsp and dash normalized, trimmed
        question.Source.Should().Be("https://uk.wikipedia.org/wiki/Пам'ять"); // URL apostrophe kept
        question.Comment.Should().BeNull();
        question.Answer.Should().Be("А");

        changes.Select(c => c.Field).Should().BeEquivalentTo(["text", "source", "comment"], "unchanged 'answer' is not a change");
        var text = changes.Single(c => c.Field == "text");
        text.OperationIndex.Should().Be(0);
        text.Entity.Should().Be("question");
        text.Id.Should().Be(ids.Q1);
        text.Label.Should().Be("Тур 1, запитання 1");
        text.Before!.GetValue<string>().Should().Be("Перше");
        changes.Single(c => c.Field == "comment").After.Should().BeNull();
    }

    [Fact]
    public async Task UpdateQuestion_AbsentKeepsValue_WhitespaceClearsOptional()
    {
        var ids = await SeedWww();

        await ApplyAndSave(ids.PackageId, $$"""[ { "op": "updateQuestion", "questionId": {{ids.Q1}}, "set": { "handoutText": "   " } } ]""");

        var question = await LoadQuestion(ids.Q1);
        question.HandoutText.Should().BeNull();
        question.Comment.Should().Be("Коментар");
    }

    [Fact]
    public async Task UpdateQuestion_NullText_IsRejected_EmptyTextWarns()
    {
        var ids = await SeedWww();
        using var session = await Open(ids.PackageId);

        (await Rejects(session, $$"""[ { "op": "updateQuestion", "questionId": {{ids.Q1}}, "set": { "text": null } } ]"""))
            .OperationIndex.Should().Be(0);

        using var second = await Open(ids.PackageId);
        await Apply(second, $$"""[ { "op": "updateQuestion", "questionId": {{ids.Q1}}, "set": { "answer": " " } } ]""");
        second.Engine.Warnings.Should().ContainSingle().Which.Should().Contain("answer");
    }

    [Fact]
    public async Task UpdateQuestion_RejectsUnknownFields_TooLongValues_AndForeignQuestions()
    {
        var ids = await SeedWww();
        var other = await SeedWww();
        using var session = await Open(ids.PackageId);

        (await Rejects(session, $$"""[ { "op": "updateQuestion", "questionId": {{ids.Q1}}, "set": { "handoutUrl": "/x.png" } } ]"""))
            .Message.Should().Contain("handoutUrl");
        (await Rejects(session, $$"""[ { "op": "updateQuestion", "questionId": {{ids.Q1}}, "set": { "answer": "{{new string('я', 1001)}}" } } ]"""))
            .Message.Should().Contain("1000");
        (await Rejects(session, $$"""[ { "op": "updateQuestion", "questionId": {{other.Q1}}, "set": { "text": "x" } } ]"""))
            .Message.Should().Contain("not found");
    }

    [Fact]
    public async Task UpdateQuestion_Number_OnlyInManualMode()
    {
        var ids = await SeedWww();
        using (var session = await Open(ids.PackageId))
        {
            (await Rejects(session, $$"""[ { "op": "updateQuestion", "questionId": {{ids.Q1}}, "set": { "number": "1а" } } ]"""))
                .Message.Should().Contain("manual");
        }

        await ApplyAndSave(ids.PackageId, $$"""
            [ { "op": "setNumberingMode", "mode": "manual" },
              { "op": "updateQuestion", "questionId": {{ids.Q1}}, "set": { "number": " 1а " } } ]
            """);

        (await LoadQuestion(ids.Q1)).Number.Should().Be("1а");
    }

    [Fact]
    public async Task UpdateQuestion_GameTypeSpecificFields()
    {
        var www = await SeedWww();
        var shvager = await SeedShvager();

        using (var session = await Open(www.PackageId))
        {
            (await Rejects(session, $$"""[ { "op": "updateQuestion", "questionId": {{www.Q1}}, "set": { "answerForm": "таку назву" } } ]"""))
                .Message.Should().Contain("Своя гра");
        }

        using (var session = await Open(shvager.PackageId))
        {
            (await Rejects(session, $$"""[ { "op": "updateQuestion", "questionId": {{shvager.Theme1Questions[0]}}, "set": { "hostInstructions": "роздайте" } } ]"""))
                .Message.Should().Contain("host");
            (await Rejects(session, $$"""[ { "op": "updateQuestion", "questionId": {{shvager.Theme1Questions[0]}}, "set": { "number": "60" } } ]"""))
                .Message.Should().Contain("manual");
        }

        await ApplyAndSave(shvager.PackageId, $$"""[ { "op": "updateQuestion", "questionId": {{shvager.Theme1Questions[0]}}, "set": { "answerForm": "таку назву" } } ]""");
        (await LoadQuestion(shvager.Theme1Questions[0])).AnswerForm.Should().Be("таку назву");
    }

    #endregion

    #region Authors and tags

    [Fact]
    public async Task SetQuestionAuthors_ResolvesByIdAndName_CreatingAMissingAuthorOnce()
    {
        var ids = await SeedWww();

        var changes = await ApplyAndSave(ids.PackageId, $$"""
            [ { "op": "setQuestionAuthors", "questionId": {{ids.Q1}}, "authors": [ { "id": {{ids.PetroId}} }, { "firstName": " Нова ", "lastName": "Авторка" } ] },
              { "op": "setQuestionAuthors", "questionId": {{ids.Q2}}, "authors": [ { "firstName": "Нова", "lastName": "Авторка" }, { "firstName": "Анна", "lastName": "Коваль" } ] } ]
            """);

        using var context = DbFactory.CreateDbContext();
        (await context.Authors.CountAsync(a => a.FirstName == "Нова" && a.LastName == "Авторка")).Should().Be(1);
        (await LoadQuestion(ids.Q1)).Authors.Select(a => a.FullName).Should().BeEquivalentTo(["Петро Мельник", "Нова Авторка"]);
        (await LoadQuestion(ids.Q2)).Authors.Select(a => a.Id).Should().Contain(ids.AnnaId, "an existing name reuses the author");

        var first = changes.Single(c => c.Id == ids.Q1);
        first.Field.Should().Be("authors");
        first.Before!.AsArray().Select(a => a!["name"]!.GetValue<string>()).Should().Equal("Анна Коваль");
        first.After!.AsArray().Select(a => a!["id"]).Should().NotContainNulls("ids are real after saving");
    }

    [Fact]
    public async Task CreatingAnAuthorOrTagByName_IsAWarning_OncePerName()
    {
        var ids = await SeedWww();
        using var session = await Open(ids.PackageId);

        await Apply(session, $$"""
            [ { "op": "setQuestionAuthors", "questionId": {{ids.Q1}}, "authors": [ { "firstName": "Анна", "lastName": "Коваль" }, { "firstName": "Нова", "lastName": "Авторка" } ] },
              { "op": "setQuestionAuthors", "questionId": {{ids.Q2}}, "authors": [ { "firstName": "Нова", "lastName": "Авторка" } ] },
              { "op": "setTags", "tags": [ "Зовсім новий тег" ] } ]
            """);

        session.Engine.Warnings.Should().Equal(
            "New author 'Нова Авторка' will be created (no author with exactly this name exists).",
            "New tag 'Зовсім новий тег' will be created.");
    }

    [Fact]
    public async Task SetQuestionAuthors_UnknownIdOrBlankName_IsRejected()
    {
        var ids = await SeedWww();
        using var session = await Open(ids.PackageId);

        (await Rejects(session, $$"""[ { "op": "setQuestionAuthors", "questionId": {{ids.Q1}}, "authors": [ { "id": 999999 } ] } ]"""))
            .Message.Should().Contain("999999");
        (await Rejects(session, $$"""[ { "op": "setQuestionAuthors", "questionId": {{ids.Q1}}, "authors": [ { "firstName": "Анна", "lastName": " " } ] } ]"""))
            .OperationIndex.Should().Be(0);
    }

    [Fact]
    public async Task SetQuestionAuthors_SameSet_IsNotAChange()
    {
        var ids = await SeedWww();
        using var session = await Open(ids.PackageId);

        await Apply(session, $$"""[ { "op": "setQuestionAuthors", "questionId": {{ids.Q1}}, "authors": [ { "id": {{ids.AnnaId}} }, { "id": {{ids.AnnaId}} } ] } ]""");

        session.Engine.Changes.Should().BeEmpty();
    }

    [Fact]
    public async Task SetTags_ReusesCaseInsensitively_AndCreatesNewOnes()
    {
        var ids = await SeedWww();
        using (var context = DbFactory.CreateDbContext())
        {
            context.Tags.Add(new Tag { Name = "Кубок" });
            await context.SaveChangesAsync();
        }

        await ApplyAndSave(ids.PackageId, """[ { "op": "setTags", "tags": [ "кубок", " 2026 ", "КУБОК" ] } ]""");

        var package = await LoadPackage(ids.PackageId);
        package.Tags.Select(t => t.Name).Should().BeEquivalentTo(["Кубок", "2026"]);
        using var check = DbFactory.CreateDbContext();
        (await check.Tags.CountAsync()).Should().Be(2);
    }

    #endregion

    #region Package, tours, blocks

    [Fact]
    public async Task UpdatePackage_TrimsAndParsesDates_RejectsEmptyTitleAndBadDates()
    {
        var ids = await SeedWww();
        using (var session = await Open(ids.PackageId))
        {
            (await Rejects(session, """[ { "op": "updatePackage", "set": { "title": "  " } } ]""")).Message.Should().Contain("title");
            (await Rejects(session, """[ { "op": "updatePackage", "set": { "playedFrom": "15.11.2025" } } ]""")).Message.Should().Contain("yyyy-MM-dd");
        }

        await ApplyAndSave(ids.PackageId, """[ { "op": "updatePackage", "set": { "title": " Кубок 2025 ", "playedFrom": "2025-11-15", "description": "" } } ]""");

        var package = await LoadPackage(ids.PackageId);
        package.Title.Should().Be("Кубок 2025");
        package.PlayedFrom.Should().Be(new DateOnly(2025, 11, 15));
        package.Description.Should().BeNull();
    }

    [Fact]
    public async Task UpdateTour_TitleIsForShvagerOnly()
    {
        var www = await SeedWww();
        var shvager = await SeedShvager();

        using (var session = await Open(www.PackageId))
        {
            (await Rejects(session, $$"""[ { "op": "updateTour", "tourId": {{www.Tour1}}, "set": { "title": "Назва" } } ]"""))
                .Message.Should().Contain("Своя гра");
        }

        await ApplyAndSave(www.PackageId, $$"""[ { "op": "updateTour", "tourId": {{www.Tour1}}, "set": { "preamble": " Тестери ", "comment": "К" } } ]""");
        var changes = await ApplyAndSave(shvager.PackageId, $$"""[ { "op": "updateTour", "tourId": {{shvager.Theme1}}, "set": { "title": "  Нова  тема  " } } ]""");

        (await LoadPackage(www.PackageId)).Tours.Single(t => t.Id == www.Tour1).Preamble.Should().Be("Тестери");
        (await LoadPackage(shvager.PackageId)).Tours.Single(t => t.Id == shvager.Theme1).Title
            .Should().Be(QuestionsHub.Blazor.Utils.TextNormalizer.Normalize("  Нова  тема  "));
        changes.Single().Label.Should().Be($"Тема 1 «{QuestionsHub.Blazor.Utils.TextNormalizer.Normalize("  Нова  тема  ")}»",
            "Своя гра labels name the theme");
    }

    [Fact]
    public async Task UpdateBlock_AndSetBlockEditors()
    {
        var ids = await SeedWww();

        var changes = await ApplyAndSave(ids.PackageId, $$"""
            [ { "op": "updateBlock", "blockId": {{ids.Block}}, "set": { "name": " Блок Б ", "preamble": "П" } },
              { "op": "setBlockEditors", "blockId": {{ids.Block}}, "authors": [ { "id": {{ids.AnnaId}} } ] } ]
            """);

        var block = (await LoadPackage(ids.PackageId)).Tours.SelectMany(t => t.Blocks).Single();
        block.Name.Should().Be("Блок Б");
        block.Editors.Select(e => e.Id).Should().Equal(ids.AnnaId);
        changes.Should().HaveCount(3);
        changes.Last().Label.Should().Be("Тур 2, блок «Блок Б»");
    }

    #endregion

    #region Cascades (same rules as the editor)

    [Fact]
    public async Task SetTourEditors_Shvager_FillsAuthorlessQuestions_OnlyWhenThemeHadNoEditor()
    {
        var ids = await SeedShvager();

        await ApplyAndSave(ids.PackageId, $$"""
            [ { "op": "setTourEditors", "tourId": {{ids.Theme1}}, "authors": [ { "firstName": "Олег", "lastName": "Тематичний" } ] },
              { "op": "setTourEditors", "tourId": {{ids.Theme2}}, "authors": [ { "firstName": "Олег", "lastName": "Тематичний" } ] } ]
            """);

        var package = await LoadPackage(ids.PackageId);
        var theme1 = package.Tours.Single(t => t.Id == ids.Theme1).Questions.OrderBy(q => q.OrderIndex).ToList();
        theme1[0].Authors.Select(a => a.FirstName).Should().Equal(["Анна"], "a question with its own author keeps it");
        theme1.Skip(1).Should().OnlyContain(q => q.Authors.Single().FirstName == "Олег");

        // Theme 2 already had an editor, so its author-less questions stay empty (as in the editor).
        package.Tours.Single(t => t.Id == ids.Theme2).Questions.Should().OnlyContain(q => q.Authors.Count == 0);
    }

    [Fact]
    public async Task SetTourEditors_Www_NeverCascades()
    {
        var ids = await SeedWww();

        await ApplyAndSave(ids.PackageId, $$"""[ { "op": "setTourEditors", "tourId": {{ids.Tour2}}, "authors": [ { "id": {{ids.AnnaId}} } ] } ]""");

        (await LoadQuestion(ids.Q2)).Authors.Should().BeEmpty();
    }

    [Fact]
    public async Task SetPackageEditors_Shvager_FillsAuthorlessQuestionsOfThemesWithoutEditors()
    {
        var ids = await SeedShvager();

        var changes = await ApplyAndSave(ids.PackageId, """[ { "op": "setPackageEditors", "authors": [ { "firstName": "Головний", "lastName": "Редактор" } ] } ]""");

        var package = await LoadPackage(ids.PackageId);
        package.PackageEditors.Should().ContainSingle();
        package.Tours.Single(t => t.Id == ids.Theme1).Questions.Where(q => q.OrderIndex > 0)
            .Should().OnlyContain(q => q.Authors.Single().FirstName == "Головний");
        package.Tours.Single(t => t.Id == ids.Theme2).Questions
            .Should().OnlyContain(q => q.Authors.Count == 0, "theme 2 has its own editor");
        changes.Should().HaveCount(1 + 4).And.OnlyContain(c => c.OperationIndex == 0);
    }

    [Fact]
    public async Task SetSharedEditors_CopiesEditorsInUse_BlockEditorsForToursWithBlocks()
    {
        var ids = await SeedWww();

        await ApplyAndSave(ids.PackageId, """[ { "op": "setSharedEditors", "value": true } ]""");

        var package = await LoadPackage(ids.PackageId);
        package.SharedEditors.Should().BeTrue();
        package.PackageEditors.Select(a => a.Id).Should().BeEquivalentTo([ids.AnnaId, ids.PetroId]);
    }

    #endregion

    #region Numbering

    [Fact]
    public async Task SetNumberingMode_Renumbers_AndRecordsSideEffects()
    {
        var ids = await SeedWww();

        var changes = await ApplyAndSave(ids.PackageId, """[ { "op": "setNumberingMode", "mode": "perTour" } ]""");

        (await LoadQuestion(ids.Q3)).Number.Should().Be("1");
        changes.Should().ContainSingle(c => c.OperationIndex == 0 && c.Field == "numberingMode");
        var sideEffect = changes.Single(c => c.OperationIndex == null);
        sideEffect.Id.Should().Be(ids.Q3);
        sideEffect.Before!.GetValue<string>().Should().Be("3");
        sideEffect.After!.GetValue<string>().Should().Be("1");
    }

    [Fact]
    public async Task SetNumberingMode_RejectedForShvager_AndForUnknownModes()
    {
        var shvager = await SeedShvager();
        var www = await SeedWww();

        using (var session = await Open(shvager.PackageId))
            (await Rejects(session, """[ { "op": "setNumberingMode", "mode": "manual" } ]""")).Message.Should().Contain("Своя гра");
        using (var session = await Open(www.PackageId))
            (await Rejects(session, """[ { "op": "setNumberingMode", "mode": "roman" } ]""")).OperationIndex.Should().Be(0);
    }

    #endregion

    #region Regressions from review

    [Fact]
    public async Task Renumbering_ThatOverwritesAnExplicitNumber_IsRecorded()
    {
        var ids = await SeedWww();

        var changes = await ApplyAndSave(ids.PackageId, $$"""
            [ { "op": "setNumberingMode", "mode": "manual" },
              { "op": "updateQuestion", "questionId": {{ids.Q1}}, "set": { "number": "99" } },
              { "op": "setNumberingMode", "mode": "global" } ]
            """);

        (await LoadQuestion(ids.Q1)).Number.Should().Be("1");
        var numberChanges = changes.Where(c => c.Id == ids.Q1 && c.Field == "number").ToList();
        numberChanges.Should().HaveCount(2);
        numberChanges[0].OperationIndex.Should().Be(1);
        numberChanges[0].After!.GetValue<string>().Should().Be("99");
        numberChanges[1].OperationIndex.Should().BeNull();
        numberChanges[1].Before!.GetValue<string>().Should().Be("99");
        numberChanges[1].After!.GetValue<string>().Should().Be("1");
    }

    [Fact]
    public async Task SetNumberingMode_SameMode_StillRenumbers_AndRecordsOrderChanges()
    {
        var ids = await SeedWww();
        using (var context = DbFactory.CreateDbContext())
        {
            var q2 = await context.Questions.FirstAsync(q => q.Id == ids.Q2);
            q2.Number = "7";       // inconsistent number
            q2.OrderIndex = 2;     // gap in positions
            await context.SaveChangesAsync();
        }

        var changes = await ApplyAndSave(ids.PackageId, """[ { "op": "setNumberingMode", "mode": "global" } ]""");

        var question = await LoadQuestion(ids.Q2);
        question.Number.Should().Be("2");
        question.OrderIndex.Should().Be(1);
        changes.Should().NotContain(c => c.Field == "numberingMode", "the mode did not change");
        changes.Should().Contain(c => c.Id == ids.Q2 && c.Field == "number" && c.OperationIndex == null);
        changes.Should().Contain(c => c.Id == ids.Q2 && c.Field == "orderIndex"
            && c.Before!.GetValue<int>() == 2 && c.After!.GetValue<int>() == 1);
    }

    [Fact]
    public async Task SetPackageEditors_Shvager_UnchangedEditors_StillFillAuthorlessQuestions()
    {
        var ids = await SeedShvager();
        await ApplyAndSave(ids.PackageId, """[ { "op": "setPackageEditors", "authors": [ { "firstName": "Головний", "lastName": "Редактор" } ] } ]""");
        await ApplyAndSave(ids.PackageId, $$"""[ { "op": "setQuestionAuthors", "questionId": {{ids.Theme1Questions[3]}}, "authors": [] } ]""");

        var changes = await ApplyAndSave(ids.PackageId, """[ { "op": "setPackageEditors", "authors": [ { "firstName": "Головний", "lastName": "Редактор" } ] } ]""");

        (await LoadQuestion(ids.Theme1Questions[3])).Authors.Single().FirstName.Should().Be("Головний");
        changes.Should().ContainSingle().Which.Id.Should().Be(ids.Theme1Questions[3]);
    }

    [Fact]
    public async Task SetSharedEditors_AlreadyOnWithNoEditors_StillCopies()
    {
        var ids = await SeedWww();
        using (var context = DbFactory.CreateDbContext())
        {
            (await context.Packages.FirstAsync(p => p.Id == ids.PackageId)).SharedEditors = true;
            await context.SaveChangesAsync();
        }

        var changes = await ApplyAndSave(ids.PackageId, """[ { "op": "setSharedEditors", "value": true } ]""");

        (await LoadPackage(ids.PackageId)).PackageEditors.Should().HaveCount(2);
        changes.Should().ContainSingle(c => c.Field == "editors");
    }

    [Fact]
    public async Task ManualNumber_MayBeEmpty_LikeInTheEditor()
    {
        var ids = await SeedWww();

        await ApplyAndSave(ids.PackageId, $$"""
            [ { "op": "setNumberingMode", "mode": "manual" },
              { "op": "updateQuestion", "questionId": {{ids.Q1}}, "set": { "number": "  " } } ]
            """);

        (await LoadQuestion(ids.Q1)).Number.Should().BeEmpty();
    }

    [Fact]
    public async Task EmptyRequiredField_WarnsEvenWhenAlreadyEmpty()
    {
        var ids = await SeedWww();
        await ApplyAndSave(ids.PackageId, $$"""[ { "op": "updateQuestion", "questionId": {{ids.Q1}}, "set": { "text": "" } } ]""");

        using var session = await Open(ids.PackageId);
        await Apply(session, $$"""[ { "op": "updateQuestion", "questionId": {{ids.Q1}}, "set": { "text": "" } } ]""");

        session.Engine.Changes.Should().BeEmpty();
        session.Engine.Warnings.Should().ContainSingle();
    }

    [Fact]
    public async Task EarlierDiffEntries_AreNotRewrittenByLaterOperations()
    {
        var ids = await SeedWww();

        var changes = await ApplyAndSave(ids.PackageId, $$"""
            [ { "op": "setQuestionAuthors", "questionId": {{ids.Q2}}, "authors": [ { "id": {{ids.AnnaId}} } ] },
              { "op": "setQuestionAuthors", "questionId": {{ids.Q2}}, "authors": [ { "id": {{ids.PetroId}} } ] } ]
            """);

        changes.Should().HaveCount(2);
        changes[0].After!.AsArray().Single()!["id"]!.GetValue<int>().Should().Be(ids.AnnaId);
        changes[1].Before!.AsArray().Single()!["id"]!.GetValue<int>().Should().Be(ids.AnnaId);
        changes[1].After!.AsArray().Single()!["id"]!.GetValue<int>().Should().Be(ids.PetroId);
    }

    [Fact]
    public async Task FailureAfterEarlierMutations_LeavesNothingOnceTheContextIsDiscarded()
    {
        var ids = await SeedWww();

        using (var session = await Open(ids.PackageId))
        {
            var act = () => Apply(session, $$"""
                [ { "op": "updateQuestion", "questionId": {{ids.Q1}}, "set": { "text": "Змінено" } },
                  { "op": "setQuestionAuthors", "questionId": {{ids.Q1}}, "authors": [ { "firstName": "Новий", "lastName": "Тимчасовий" } ] },
                  { "op": "updateQuestion", "questionId": 999999, "set": { "text": "x" } } ]
                """);
            (await act.Should().ThrowAsync<ChangesetValidationException>()).Which.OperationIndex.Should().Be(2);
        }

        (await LoadQuestion(ids.Q1)).Text.Should().Be("Перше");
        using var check = DbFactory.CreateDbContext();
        (await check.Authors.AnyAsync(a => a.LastName == "Тимчасовий")).Should().BeFalse();
    }

    #endregion

    #region Dry run

    [Fact]
    public async Task WithoutSave_NothingIsPersisted_NotEvenNewAuthorsOrTags()
    {
        var ids = await SeedWww();
        using var countContext = DbFactory.CreateDbContext();
        var authorsBefore = await countContext.Authors.CountAsync();

        using (var session = await Open(ids.PackageId))
        {
            await Apply(session, $$"""
                [ { "op": "updateQuestion", "questionId": {{ids.Q1}}, "set": { "text": "Змінено" } },
                  { "op": "setQuestionAuthors", "questionId": {{ids.Q1}}, "authors": [ { "firstName": "Тимчасовий", "lastName": "Автор" } ] },
                  { "op": "setTags", "tags": [ "тимчасовий" ] },
                  { "op": "setNumberingMode", "mode": "perTour" } ]
                """);
            session.Engine.Changes.Should().NotBeEmpty();

            var dtos = ChangeFormatter.ToDtos(session.Engine.Changes, session.Package, session.Context);
            dtos.Single(c => c.Field == "authors").After!.AsArray().Single()!["id"].Should().BeNull("not saved, no id yet");
        }

        (await LoadQuestion(ids.Q1)).Text.Should().Be("Перше");
        using var check = DbFactory.CreateDbContext();
        (await check.Authors.CountAsync()).Should().Be(authorsBefore);
        (await check.Tags.AnyAsync()).Should().BeFalse();
        (await check.Packages.SingleAsync(p => p.Id == ids.PackageId)).NumberingMode.Should().Be(QuestionNumberingMode.Global);
    }

    #endregion
}
