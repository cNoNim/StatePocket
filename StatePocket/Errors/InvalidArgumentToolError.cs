namespace StatePocket.Errors;

internal sealed record InvalidArgumentToolError : ToolError
{
    public string? Argument { get; init; }
}
