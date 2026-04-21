namespace StatePocket.Contracts;

internal sealed record InvalidJsonToolError : ToolError
{
    public string? Path { get; init; }
    public long? LineNumber { get; init; }
    public long? BytePositionInLine { get; init; }
}
