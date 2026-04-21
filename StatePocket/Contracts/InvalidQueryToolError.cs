namespace StatePocket.Contracts;

internal sealed record InvalidQueryToolError : ToolError
{
    public string? Argument { get; init; }
}
