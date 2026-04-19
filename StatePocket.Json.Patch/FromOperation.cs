using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace StatePocket.Json.Patch;

public abstract class FromOperation : JsonPatchOperation
{
    protected FromOperation() {}

    [SetsRequiredMembers]
    protected FromOperation(string from, string path) : base(path) => From = from;

    [JsonPropertyName("from")]
    [JsonPropertyOrder(2)]
    [JsonRequired]
    public required string From { get; init; }
}
