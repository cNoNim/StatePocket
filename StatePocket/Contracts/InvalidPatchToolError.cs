namespace StatePocket.Contracts;

internal sealed record InvalidPatchToolError : ToolError
{
    public string? Argument { get; init; }
    public string? Path { get; init; }
}
