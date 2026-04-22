namespace StatePocket.Errors;

internal sealed record InvalidInputToolError : ToolError
{
    public string? Argument { get; init; }
    public string? Path { get; init; }
}
