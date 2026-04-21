using System.Collections;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using StatePocket.Errors;
using StatePocket.Hosting;

namespace StatePocket.Tests.Hosting;

public sealed class ToolErrorHandlingMcpServerToolTests
{
    [Fact]
    public async Task InvokeAsync_ReturnsStructuredErrorResultForToolErrorException()
    {
        var tool = new ToolErrorHandlingMcpServerTool(
            new ThrowingMcpServerTool(new ToolValidationException("format must be 'text' or 'json'.", "format"))
        );
        var result = await tool.InvokeAsync(null!, CancellationToken.None);
        var structuredContent = AssertStructuredErrorResult(result, "invalid_input");
        Assert.Equal(
            "format",
            structuredContent.GetProperty("argument")
                             .GetString()
        );
    }

    [Fact]
    public async Task InvokeAsync_ReturnsTextErrorResultForMcpException()
    {
        var tool = new ToolErrorHandlingMcpServerTool(new ThrowingMcpServerTool(new McpException("boom")));
        var result = await tool.InvokeAsync(null!, CancellationToken.None);
        var structuredContent = AssertStructuredErrorResult(result, "operation_failed");
        Assert.Equal(
            "boom",
            structuredContent.GetProperty("message")
                             .GetString()
        );
    }

    [Fact]
    public async Task InvokeAsync_TreatsWrappedMcpExceptionAsOperationFailed()
    {
        var tool = new ToolErrorHandlingMcpServerTool(
            new ThrowingMcpServerTool(
                new McpException("outer message", new ToolValidationException("inner message", "format"))
            )
        );
        var result = await tool.InvokeAsync(null!, CancellationToken.None);
        var structuredContent = AssertStructuredErrorResult(result, "operation_failed");
        Assert.Equal(
            "outer message",
            structuredContent.GetProperty("message")
                             .GetString()
        );
    }

    [Fact]
    public async Task InvokeAsync_ReturnsInvalidJsonPayloadForJsonException()
    {
        var tool = new ToolErrorHandlingMcpServerTool(
            new ThrowingMcpServerTool(
                new JsonException(
                    "Invalid JSON value.",
                    "$.value",
                    3,
                    14
                )
            )
        );
        var result = await tool.InvokeAsync(null!, CancellationToken.None);
        var structuredContent = AssertStructuredErrorResult(result, "invalid_json");
        Assert.Equal(
            "$.value",
            structuredContent.GetProperty("path")
                             .GetString()
        );
        Assert.Equal(
            3,
            structuredContent.GetProperty("lineNumber")
                             .GetInt64()
        );
        Assert.Equal(
            14,
            structuredContent.GetProperty("bytePositionInLine")
                             .GetInt64()
        );
    }

    [Fact]
    public async Task InvokeAsync_ReturnsInvalidArgumentPayloadForArgumentException()
    {
        var tool = new ToolErrorHandlingMcpServerTool(
            new ThrowingMcpServerTool(
                new ArgumentException(
                    "value is required.",
                    nameof(DictionaryEntry.Value)
                       .ToLowerInvariant()
                )
            )
        );
        var result = await tool.InvokeAsync(null!, CancellationToken.None);
        var structuredContent = AssertStructuredErrorResult(result, "invalid_argument");
        Assert.Equal(
            "value",
            structuredContent.GetProperty("argument")
                             .GetString()
        );
    }

    [Fact]
    public async Task InvokeAsync_PropagatesMcpProtocolException()
    {
        var tool = new ToolErrorHandlingMcpServerTool(
            new ThrowingMcpServerTool(new McpProtocolException("bad request", McpErrorCode.InvalidParams))
        );
        var exception = await Assert.ThrowsAsync<McpProtocolException>(() => tool
                                                                            .InvokeAsync(null!, CancellationToken.None)
                                                                            .AsTask()
        );
        Assert.Equal("bad request", exception.Message);
        Assert.Equal(McpErrorCode.InvalidParams, exception.ErrorCode);
    }

    private static JsonElement AssertStructuredErrorResult(CallToolResult result, string expectedKind)
    {
        Assert.True(result.IsError);
        var content = Assert.Single(result.Content);
        var text = Assert.IsType<TextContentBlock>(content);
        var structuredContent = result.StructuredContent
                             ?? throw new InvalidOperationException("Expected structured content.");
        Assert.Equal(
            expectedKind,
            structuredContent.GetProperty("kind")
                             .GetString()
        );
        using var contentDocument = JsonDocument.Parse(text.Text);
        Assert.Equal(structuredContent.GetRawText(), contentDocument.RootElement.GetRawText());
        return structuredContent;
    }

    private sealed class ThrowingMcpServerTool(Exception exception) : McpServerTool
    {
        public override Tool ProtocolTool { get; } = new()
        {
            Name = "throwing_tool"
        };
        public override IReadOnlyList<object> Metadata { get; } = [];

        public override ValueTask<CallToolResult> InvokeAsync(
            RequestContext<CallToolRequestParams> request,
            CancellationToken cancellationToken = default
        )
        {
            return ValueTask.FromException<CallToolResult>(exception);
        }
    }
}
