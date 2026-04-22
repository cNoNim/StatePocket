using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace StatePocket.Hosting;

internal sealed class ValidatedMcpTool(
    McpServerTool innerTool,
    Action<RequestContext<CallToolRequestParams>> validateRequest
) : DelegatingMcpServerTool(innerTool)
{
    public override ValueTask<CallToolResult> InvokeAsync(
        RequestContext<CallToolRequestParams> request,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(request);
        validateRequest(request);
        return base.InvokeAsync(request, cancellationToken);
    }
}
