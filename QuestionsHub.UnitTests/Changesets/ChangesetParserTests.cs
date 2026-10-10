using System.Text.Json;
using FluentAssertions;
using QuestionsHub.Blazor.Infrastructure.AgentApi.Changesets;
using Xunit;

namespace QuestionsHub.UnitTests.Changesets;

public class ChangesetParserTests
{
    private static List<ChangesetOperation> Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return ChangesetParser.Parse(document.RootElement);
    }

    private static ChangesetValidationException Failure(string json)
    {
        var act = () => Parse(json);
        return act.Should().Throw<ChangesetValidationException>().Which;
    }

    [Fact]
    public void ParsesEveryFieldOperation_CaseInsensitively()
    {
        var ops = Parse("""
            [
              { "op": "updateQuestion", "questionId": 1, "set": { "text": "T", "comment": null } },
              { "OP": "SetQuestionAuthors", "QuestionId": 1, "authors": [ { "id": 3 }, { "firstName": "А", "lastName": "Б" } ] },
              { "op": "updateTour", "tourId": 2, "set": { "preamble": "P" } },
              { "op": "setTourEditors", "tourId": 2, "authors": [] },
              { "op": "updateBlock", "blockId": 4, "set": { "name": "N" } },
              { "op": "setBlockEditors", "blockId": 4, "authors": [ { "id": 1 } ] },
              { "op": "updatePackage", "set": { "title": "X", "playedFrom": "2025-11-15" } },
              { "op": "setPackageEditors", "authors": [] },
              { "op": "setSharedEditors", "value": true },
              { "op": "setNumberingMode", "mode": "manual" },
              { "op": "setTags", "tags": [ "2025" ] }
            ]
            """);

        ops.Select(o => o.GetType().Name).Should().Equal(
            "UpdateQuestionOp", "SetQuestionAuthorsOp", "UpdateTourOp", "SetTourEditorsOp", "UpdateBlockOp",
            "SetBlockEditorsOp", "UpdatePackageOp", "SetPackageEditorsOp", "SetSharedEditorsOp",
            "SetNumberingModeOp", "SetTagsOp");
        ops.Select(o => o.Index).Should().Equal(Enumerable.Range(0, 11));

        var authors = ((SetQuestionAuthorsOp)ops[1]).Authors;
        authors.Should().Equal(new AuthorRef(3, null, null), new AuthorRef(null, "А", "Б"));

        var set = ((UpdateQuestionOp)ops[0]).Set;
        set.Has("TEXT").Should().BeTrue();
        set.GetString("comment").Should().BeNull();
        set.Has("answer").Should().BeFalse();
    }

    [Theory]
    [InlineData("""{}""", null)]
    [InlineData("""[]""", null)]
    [InlineData("""[ { "op": "updateQuestion", "questionId": 1, "set": { "text": "x" } }, { "op": "explode" } ]""", 1)]
    [InlineData("""[ { "op": "updateQuestion", "questionID": 1, "questionId": 2, "set": { "text": "x" } } ]""", 0)]
    [InlineData("""[ { "op": "updateQuestion", "questionId": 1, "set": { "text": "x" }, "extra": 1 } ]""", 0)]
    [InlineData("""[ { "op": "updateQuestion", "questionId": "1", "set": { "text": "x" } } ]""", 0)]
    [InlineData("""[ { "op": "updateQuestion", "questionId": 1, "set": {} } ]""", 0)]
    [InlineData("""[ { "op": "updateQuestion", "questionId": 1 } ]""", 0)]
    [InlineData("""[ { "op": "setQuestionAuthors", "questionId": 1, "authors": [ { "id": 1, "firstName": "А" } ] } ]""", 0)]
    [InlineData("""[ { "op": "setQuestionAuthors", "questionId": 1, "authors": [ {} ] } ]""", 0)]
    [InlineData("""[ { "op": "setQuestionAuthors", "questionId": 1, "authors": [ { "id": 3, "ID": 17 } ] } ]""", 0)]
    [InlineData("""[ { "op": "setQuestionAuthors", "questionId": 1, "authors": [ { "firstName": "А", "FirstName": "Б", "lastName": "В" } ] } ]""", 0)]
    [InlineData("""[ { "op": "setQuestionAuthors", "questionId": 1, "authors": [ "Анна" ] } ]""", 0)]
    [InlineData("""[ { "op": "setSharedEditors", "value": "yes" } ]""", 0)]
    [InlineData("""[ { "op": "setTags", "tags": [ 5 ] } ]""", 0)]
    [InlineData("""[ { "op": "deleteQuestion", "questionId": 0 } ]""", 0)]
    [InlineData("""[ { "op": "addQuestion", "tourId": 1, "blockId": 0 } ]""", 0)]
    [InlineData("""[ { "op": "addQuestion", "tourId": 1, "position": -1 } ]""", 0)]
    [InlineData("""[ { "op": "moveTour", "tourId": 1 } ]""", 0)]
    [InlineData("""[ { "op": "addTour", "questions": [ { "text": "x" } ] } ]""", 0)]
    [InlineData("""[ { "op": "addTour", "questions": {} } ]""", 0)]
    public void RejectsMalformedRequests_WithTheOperationIndex(string json, int? expectedIndex)
    {
        Failure(json).OperationIndex.Should().Be(expectedIndex);
    }

    [Fact]
    public void RejectsTooManyOperations()
    {
        var ops = string.Join(",", Enumerable.Repeat("""{ "op": "setSharedEditors", "value": true }""", ChangesetParser.MaxOperations + 1));
        Failure($"[{ops}]").OperationIndex.Should().BeNull();
    }

    [Fact]
    public void RejectsTooManyAuthors()
    {
        var authors = string.Join(",", Enumerable.Range(1, ChangesetParser.MaxAuthorRefs + 1).Select(i => $$"""{ "id": {{i}} }"""));
        Failure($$"""[ { "op": "setPackageEditors", "authors": [{{authors}}] } ]""").OperationIndex.Should().Be(0);
    }

    [Fact]
    public void FieldSet_RejectsUnknownKeysAndWrongTypes()
    {
        var set = ((UpdateQuestionOp)Parse("""[ { "op": "updateQuestion", "questionId": 1, "set": { "txt": "x", "answer": 5 } } ]""")[0]).Set;

        var unknown = () => set.EnsureOnly("text", "answer");
        unknown.Should().Throw<ChangesetValidationException>().WithMessage("*txt*");

        var wrongType = () => set.GetString("answer");
        wrongType.Should().Throw<ChangesetValidationException>();
    }
}
