namespace StatePocket.Contracts;

internal sealed record InvalidPatchToolError : ToolError
{
    public string? Argument { get; init; }
    public string? Path { get; init; }
    public long? LineNumber { get; init; }
    public long? BytePositionInLine { get; init; }
}
