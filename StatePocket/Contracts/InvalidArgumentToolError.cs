namespace StatePocket.Contracts;

internal sealed record InvalidArgumentToolError : ToolError
{
    public string? Argument { get; init; }
}
