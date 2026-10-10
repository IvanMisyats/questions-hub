using System.ComponentModel;
using ModelContextProtocol.Server;

namespace QuestionsHub.Blazor.Infrastructure.AgentApi.Mcp;

/// <summary>The Agent API reference as an MCP resource, for clients that browse resources (same text as get_api_reference).</summary>
[McpServerResourceType]
public sealed class AgentMcpResources
{
    [McpServerResource(UriTemplate = AgentApiReference.ResourceUri, Name = "agent-api-reference",
        Title = "Questions Hub Agent API reference", MimeType = "text/markdown")]
    [Description("Package model and field glossary, every changeset operation with its fields and rules, limits, the response format and errors.")]
    public static string Reference() => AgentApiReference.Text;
}
