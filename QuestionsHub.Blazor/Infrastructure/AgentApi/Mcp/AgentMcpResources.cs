using System.ComponentModel;
using ModelContextProtocol.Server;

namespace QuestionsHub.Blazor.Infrastructure.AgentApi.Mcp;

/// <summary>The Agent API reference and the .qhub format as MCP resources, for clients that browse resources (same texts as get_api_reference and get_qhub_format).</summary>
[McpServerResourceType]
public sealed class AgentMcpResources
{
    [McpServerResource(UriTemplate = AgentApiReference.ResourceUri, Name = "agent-api-reference",
        Title = "Questions Hub Agent API reference", MimeType = "text/markdown")]
    [Description("Package model and field glossary, every changeset operation with its fields and rules, limits, the response format and errors.")]
    public static string Reference() => AgentApiReference.Text;

    [McpServerResource(UriTemplate = QhubFormatReference.ResourceUri, Name = "qhub-format",
        Title = "The .qhub package format", MimeType = "text/markdown")]
    [Description("The .qhub package format (package.json + assets/) and the JSON Schema of package.json, for converting a document before an import.")]
    public static string QhubFormat() => QhubFormatReference.Text;
}
