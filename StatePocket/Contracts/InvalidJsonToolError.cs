namespace StatePocket.Contracts;

internal sealed record InvalidJsonToolError : ToolError
{
    public string? Path { get; init; }
}
