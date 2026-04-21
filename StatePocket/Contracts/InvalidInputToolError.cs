namespace StatePocket.Contracts;

internal sealed record InvalidInputToolError : ToolError
{
    public string? Argument { get; init; }
    public string? Path { get; init; }
    public long? LineNumber { get; init; }
    public long? BytePositionInLine { get; init; }
}
