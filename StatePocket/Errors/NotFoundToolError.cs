namespace StatePocket.Errors;

internal sealed record NotFoundToolError : ToolError
{
    public required string Namespace { get; init; }
    public required string Key { get; init; }
}
