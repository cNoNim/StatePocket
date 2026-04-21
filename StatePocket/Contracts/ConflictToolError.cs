namespace StatePocket.Contracts;

internal abstract record ConflictToolError : ToolError
{
    public required string Namespace { get; init; }
    public required string Key { get; init; }
}
