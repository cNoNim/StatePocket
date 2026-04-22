namespace StatePocket.Errors;

internal sealed record InvalidPatchToolError : ToolError
{
    public string? Argument { get; init; }
    public string? Path { get; init; }
    public int? OperationIndex { get; init; }
    public string? Operation { get; init; }
    public string? TargetPath { get; init; }
}
