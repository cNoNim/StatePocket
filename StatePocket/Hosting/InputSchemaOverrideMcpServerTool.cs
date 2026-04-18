using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace StatePocket.Hosting;

internal sealed class InputSchemaOverrideMcpServerTool : DelegatingMcpServerTool
{
    public InputSchemaOverrideMcpServerTool(McpServerTool innerTool, JsonElement inputSchema) : base(innerTool)
    {
        var protocolTool = innerTool.ProtocolTool;
        ProtocolTool = new Tool
        {
            Name = protocolTool.Name,
            Title = protocolTool.Title,
            Description = protocolTool.Description,
            InputSchema = inputSchema,
            OutputSchema = protocolTool.OutputSchema,
            Annotations = protocolTool.Annotations,
            Icons = protocolTool.Icons,
            Meta = protocolTool.Meta
        };
    }

    public override Tool ProtocolTool { get; }
}
