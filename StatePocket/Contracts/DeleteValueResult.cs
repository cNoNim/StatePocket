using System.ComponentModel;
using System.Text.Json;

namespace StatePocket.Contracts;

internal sealed record DeleteValueResult
{
    [Description("Namespace the key was deleted from.")]
    public required string Namespace { get; init; }
    [Description("Deleted key.")]
    public required string Key { get; init; }
    [Description("True when a live key was deleted.")]
    public required bool Deleted { get; init; }
    [Description("Deleted JSON value. Present only when deleted is true.")]
    public JsonElement? DeletedValue { get; init; }
}
