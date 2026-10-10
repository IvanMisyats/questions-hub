using System.Text.Json;
using FluentAssertions;
using QuestionsHub.Blazor.Infrastructure.AgentApi.Changesets;
using QuestionsHub.Blazor.Infrastructure.AgentApi.Mcp;
using Xunit;

namespace QuestionsHub.UnitTests.Changesets;

/// <summary>The MCP server documents itself: reference, instructions and tool description cover every operation.</summary>
public class AgentApiReferenceTests
{
    public static TheoryData<string> Operations()
    {
        var data = new TheoryData<string>();
        foreach (var op in ChangesetParser.OperationNames)
            data.Add(op);
        return data;
    }

    private static ChangesetValidationException Failure(string json)
    {
        using var document = JsonDocument.Parse(json);
        var element = document.RootElement.Clone();
        var act = () => ChangesetParser.Parse(element);
        return act.Should().Throw<ChangesetValidationException>().Which;
    }

    [Fact]
    public void Reference_IsTheAgentApiSectionOfTheDocs()
    {
        var text = AgentApiReference.Text;

        text.Should().StartWith("## Agent API");
        text.Should().Contain("#### Package model").And.Contain("### MCP").And.Contain("`get_api_reference`");
        text.Should().NotContain("Agent API implementation", "implementation notes are for developers, not agents");
        text.Should().NotContain("agent-reference");
    }

    [Theory]
    [MemberData(nameof(Operations))]
    public void EveryOperation_IsDocumented_InTheReferenceAndTheToolDescription(string op)
    {
        AgentApiReference.Text.Should().Contain($"`{op}`");
        AgentMcpTools.ApplyChangesetDescription.Should().Contain($"{op}:");
    }

    [Theory]
    [MemberData(nameof(Operations))]
    public void EveryListedOperation_IsKnownToTheParser(string op)
    {
        Failure($$"""[ { "op": "{{op}}", "unexpected": 1 } ]""").Message.Should().NotContain("Unknown operation");
    }

    [Fact]
    public void Instructions_PointToTheReference()
    {
        AgentApiReference.Instructions.Should().Contain("get_api_reference").And.Contain(AgentApiReference.ResourceUri)
            .And.Contain("dryRun").And.Contain("expectedVersion").And.Contain("requestId");
    }

    [Fact]
    public void UnknownOperation_ListsTheOperations()
    {
        var error = Failure("""[ { "op": "renameQuestion", "questionId": 1 } ]""");

        error.Message.Should().Contain("Unknown operation 'renameQuestion'").And.Contain("updateQuestion").And.Contain("setTourType");
    }

    [Fact]
    public void UnknownProperty_ListsWhatTheOperationTakes()
    {
        var error = Failure("""[ { "op": "moveQuestion", "questionId": 1, "tourId": 2, "index": 0 } ]""");

        error.Message.Should().Contain("Unknown property(ies): index").And.Contain("questionId, tourId, blockId, position");
    }

    [Fact]
    public void UnknownProperty_InANewTourQuestion_NamesTheEntry()
    {
        var error = Failure("""[ { "op": "addTour", "questions": [ { "set": { "text": "T" } }, { "text": "T" } ] } ]""");

        error.Message.Should().Contain("Unknown property(ies) in 'questions[1]': text. It takes: set, authors.");
    }

    [Fact]
    public void ListReplacement_IsExplained_WhereAgentsLook()
    {
        AgentApiReference.Instructions.Should().Contain("replaced as a whole");
        AgentMcpTools.ApplyChangesetDescription.Should().Contain("REPLACE the whole list");
        AgentApiReference.Text.Should().Contain("**replace the whole list**");
    }

    [Fact]
    public void Extract_RequiresTheMarkers()
    {
        AgentApiReference.Extract("a\n<!-- agent-reference:start x -->\n## Agent API\nbody\n<!-- agent-reference:end -->\nrest")
            .Should().Be("## Agent API\nbody");

        var act = () => AgentApiReference.Extract("## Agent API without markers");
        act.Should().Throw<InvalidOperationException>();
    }
}
