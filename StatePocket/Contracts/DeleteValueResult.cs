using System.ComponentModel;

namespace StatePocket.Contracts;

internal sealed record DeleteValueResult
{
    [Description("Namespace the key was deleted from.")]
    public required string Namespace { get; init; }
    [Description("Deleted key.")]
    public required string Key { get; init; }
}
