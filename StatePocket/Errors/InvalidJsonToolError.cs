namespace StatePocket.Errors;

internal sealed record InvalidJsonToolError : ToolError
{
    public string? Path { get; init; }
}
