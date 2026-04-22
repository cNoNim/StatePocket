namespace StatePocket.Errors;

internal sealed record InvalidQueryToolError : ToolError
{
    public string? Argument { get; init; }
}
