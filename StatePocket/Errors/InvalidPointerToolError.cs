namespace StatePocket.Errors;

internal sealed record InvalidPointerToolError : ToolError
{
    public string? Argument { get; init; }
}
