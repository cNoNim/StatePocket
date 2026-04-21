using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace StatePocket.Hosting;

internal sealed class ToolErrorHandlingMcpServerTool(McpServerTool innerTool) : DelegatingMcpServerTool(innerTool)
{
    public override async ValueTask<CallToolResult> InvokeAsync(
        RequestContext<CallToolRequestParams> request,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            return await base.InvokeAsync(request, cancellationToken)
                             .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (McpProtocolException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return ToolErrorResultFactory.Create(exception);
        }
    }
}
