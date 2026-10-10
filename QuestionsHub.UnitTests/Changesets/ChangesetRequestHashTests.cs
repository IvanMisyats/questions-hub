using System.Text.Json;
using FluentAssertions;
using QuestionsHub.Blazor.Infrastructure.AgentApi.Changesets;
using Xunit;

namespace QuestionsHub.UnitTests.Changesets;

public class ChangesetRequestHashTests
{
    private static string Hash(string operations, string? summary = null, string? expectedVersion = null)
    {
        using var document = JsonDocument.Parse(operations);
        return PackageChangesetService.HashRequest(
            new ChangesetRequest(Guid.NewGuid(), expectedVersion, summary, DryRun: false, document.RootElement.Clone()));
    }

    [Fact]
    public void SameRequest_DifferentWhitespaceOrPropertyOrder_SameHash()
    {
        var compact = Hash("""[{"op":"updateQuestion","questionId":1,"set":{"text":"A","comment":null}}]""");
        var reordered = Hash("""
            [ { "set": { "comment": null, "text": "A" },
                "questionId": 1,
                "op": "updateQuestion" } ]
            """);

        reordered.Should().Be(compact);
    }

    [Fact]
    public void DifferentContent_DifferentHash()
    {
        var baseline = Hash("""[{"op":"setTags","tags":["a","b"]}]""", summary: "s");

        Hash("""[{"op":"setTags","tags":["b","a"]}]""", summary: "s").Should().NotBe(baseline, "array order is meaningful");
        Hash("""[{"op":"setTags","tags":["a","b"]}]""", summary: "other").Should().NotBe(baseline);
        Hash("""[{"op":"setTags","tags":["a","b"]}]""", summary: "s", expectedVersion: "v").Should().NotBe(baseline);
        Hash("""[{"op":"setTags","tags":["a","b"]}]""", summary: "  s  ").Should().Be(baseline, "the summary is trimmed when stored");
    }
}
