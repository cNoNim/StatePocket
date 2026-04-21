using StatePocket.Contracts;
using StatePocket.Errors;

namespace StatePocket.Tests.Errors;

public sealed class ToolErrorExceptionTests
{
    [Fact]
    public void ToolValidationException_SerializesStructuredContentWithCamelCaseFields()
    {
        ToolValidationException exception = new("format must be 'text' or 'json'.", "format");
        var structuredContent = exception.ToStructuredContent();
        Assert.Equal(
            "invalid_input",
            structuredContent.GetProperty("kind")
                             .GetString()
        );
        Assert.Equal(
            "format must be 'text' or 'json'.",
            structuredContent.GetProperty("message")
                             .GetString()
        );
        Assert.False(
            structuredContent.GetProperty("retryable")
                             .GetBoolean()
        );
        Assert.Equal(
            "format",
            structuredContent.GetProperty("argument")
                             .GetString()
        );
    }

    [Fact]
    public void ToolRevisionConflictException_SerializesConflictMetadata()
    {
        ToolRevisionConflictException exception = new(
            "codex",
            "cas",
            99,
            1
        );
        var structuredContent = exception.ToStructuredContent();
        Assert.Equal(
            "revision_conflict",
            structuredContent.GetProperty("kind")
                             .GetString()
        );
        Assert.Equal(
            "codex",
            structuredContent.GetProperty("namespace")
                             .GetString()
        );
        Assert.Equal(
            "cas",
            structuredContent.GetProperty("key")
                             .GetString()
        );
        Assert.Equal(
            99,
            structuredContent.GetProperty("expectedRevision")
                             .GetInt64()
        );
        Assert.Equal(
            1,
            structuredContent.GetProperty("currentRevision")
                             .GetInt64()
        );
    }

    [Fact]
    public void ToolBusyException_UsesRetryableBusyPayload()
    {
        ToolBusyException exception = new("The database is busy with another write operation. Try again.");
        var payload = Assert.IsType<BusyToolError>(exception.ToPayload());
        Assert.True(payload.Retryable);
        Assert.Equal("The database is busy with another write operation. Try again.", payload.Message);
    }

    [Fact]
    public void ToolInvalidPatchException_SerializesJsonMetadata()
    {
        ToolInvalidPatchException exception = new(
            "Invalid patch JSON.",
            path: "$[0].value",
            lineNumber: 3,
            bytePositionInLine: 14
        );
        var structuredContent = exception.ToStructuredContent();
        Assert.Equal(
            "invalid_patch",
            structuredContent.GetProperty("kind")
                             .GetString()
        );
        Assert.Equal(
            "$[0].value",
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
    public void ToolErrorException_CoversRemainingDerivedTypes()
    {
        ToolErrorException[] exceptions =
        [
            new ToolNotFoundException("codex", "missing"),
            new ToolAlreadyExistsException("codex", "claimed", 1),
            new ToolInvalidArgumentException("format must be 'text' or 'json'.", "format"),
            new ToolInvalidJsonException("value must be valid JSON when format is 'json'."),
            new ToolInvalidQueryException("equals requires query."),
            new ToolInvalidPatchException("Patch document must be a JSON array."),
            new ToolOperationFailedException("Operation failed."),
            new ToolInternalException("Internal error.")
        ];
        var actualKinds = exceptions.Select(static exception => exception.ToStructuredContent()
                                                                         .GetProperty("kind")
                                                                         .GetString()!
                                     )
                                    .ToArray();
        Assert.Equal(
            [
                "not_found",
                "already_exists",
                "invalid_argument",
                "invalid_json",
                "invalid_query",
                "invalid_patch",
                "operation_failed",
                "internal_error"
            ],
            actualKinds
        );
    }
}
