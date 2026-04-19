using System.Text.Json;
using System.Text.Json.Nodes;
using StatePocket.JsonPatch.Exceptions;
using StatePocket.JsonPointer;

namespace StatePocket.JsonPatch.Tests;

public sealed class StrictParsingTests
{
    [Theory]
    [InlineData(PatchOperationType.Add)]
    [InlineData(PatchOperationType.Replace)]
    [InlineData(PatchOperationType.Test)]
    public void Deserialize_MissingValueForValueOperations_ThrowsJsonException(PatchOperationType operationType)
    {
        var exception = Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<PatchDocument>($"[{{\"op\":\"{ToJsonName(operationType)}\",\"path\":\"/x\"}}]")
        );
        Assert.Contains("value", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(PatchOperationType.Move)]
    [InlineData(PatchOperationType.Copy)]
    public void Deserialize_MissingFromForMoveOrCopy_ThrowsJsonException(PatchOperationType operationType)
    {
        var exception = Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<PatchDocument>($"[{{\"op\":\"{ToJsonName(operationType)}\",\"path\":\"/x\"}}]")
        );
        Assert.Contains("from", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Deserialize_NullPatchOperation_ThrowsJsonException()
    {
        var exception = Assert.Throws<JsonException>(static () => JsonSerializer.Deserialize<PatchDocument>("[null]"));
        Assert.Contains("object", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("/01")]
    [InlineData("/+1")]
    public void Apply_InvalidArrayIndexFormat_Throws(string path)
    {
        var patch = new PatchDocument([PatchOperation.Add(path, JsonValue.Create("x"))]);
        Assert.Throws<JsonPatchException>(() => _ = patch.Apply(ParseNode("""["a"]""")));
    }

    [Theory]
    [InlineData("name")]
    [InlineData("foo")]
    public void Constructor_InvalidJsonPointer_ThrowsJsonPointerException(string path)
    {
        Assert.Throws<JsonPointerException>(() =>
            _ = new PatchDocument([PatchOperation.Replace(path, JsonValue.Create(1))])
        );
    }

    private static JsonNode ParseNode(string json)
    {
        return JsonNode.Parse(json) ?? throw new InvalidOperationException("JSON must parse.");
    }

    private static string ToJsonName(PatchOperationType operationType)
    {
        return operationType switch
        {
            PatchOperationType.Add => "add",
            PatchOperationType.Remove => "remove",
            PatchOperationType.Replace => "replace",
            PatchOperationType.Move => "move",
            PatchOperationType.Copy => "copy",
            PatchOperationType.Test => "test",
            _ => throw new InvalidOperationException($"Unsupported operation type '{operationType}'.")
        };
    }
}
