using System.Text.Json;
using ModelContextProtocol;
using StatePocket.Contracts;
using StatePocket.Errors;
using StatePocket.Json.Patch;
using StatePocket.Json.Path;

namespace StatePocket.Hosting;

internal static class ToolErrorFactory
{
    public static ToolError Create(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return exception switch
        {
            ToolErrorException toolError => toolError.ToPayload(),
            JsonException jsonException => CreateInvalidJson(jsonException),
            ArgumentException argumentException => CreateInvalidArgument(argumentException),
            JsonPathException jsonPathException => CreateInvalidQuery(jsonPathException),
            JsonPatchException jsonPatchException => CreateInvalidPatch(jsonPatchException),
            McpException mcpException => CreateOperationFailed(mcpException),
            _ => CreateInternal(exception)
        };
    }

    private static InvalidArgumentToolError CreateInvalidArgument(ArgumentException exception)
    {
        return new InvalidArgumentToolError
        {
            Message = exception.Message,
            Retryable = false,
            Argument = exception.ParamName
        };
    }

    private static InvalidJsonToolError CreateInvalidJson(JsonException exception)
    {
        return new InvalidJsonToolError
        {
            Message = exception.Message,
            Retryable = false,
            Path = exception.Path,
            LineNumber = exception.LineNumber,
            BytePositionInLine = exception.BytePositionInLine
        };
    }

    private static InvalidQueryToolError CreateInvalidQuery(JsonPathException exception)
    {
        return new InvalidQueryToolError
        {
            Message = exception.Message,
            Retryable = false,
            Argument = "query"
        };
    }

    private static InvalidPatchToolError CreateInvalidPatch(JsonPatchException exception)
    {
        return new InvalidPatchToolError
        {
            Message = exception.Message,
            Retryable = false,
            Argument = "patch"
        };
    }

    private static OperationFailedToolError CreateOperationFailed(McpException exception)
    {
        return new OperationFailedToolError
        {
            Message = exception.Message,
            Retryable = false
        };
    }

    private static InternalToolError CreateInternal(Exception _)
    {
        return new InternalToolError
        {
            Message = "An internal error occurred.",
            Retryable = false
        };
    }
}
