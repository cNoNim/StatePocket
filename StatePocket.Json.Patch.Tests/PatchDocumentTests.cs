using System.Text.Json;
using System.Text.Json.Nodes;

namespace StatePocket.Json.Patch.Tests;

public sealed class JsonPatchTests
{
    [Fact]
    public void Apply_ReplaceRoot_ReturnsReplacementDocument()
    {
        var document = ParseNode("{\"name\":\"old\"}");
        var replacement = ParseNode("{\"name\":\"new\"}");
        JsonPatch patchDocument = new([JsonPatchOperation.Replace("", replacement)]);
        var result = patchDocument.Apply(document);
        AssertJson("{\"name\":\"new\"}", result);
        AssertJson("{\"name\":\"old\"}", document);
    }

    [Fact]
    public void Apply_AddObjectMember_ReturnsDocumentWithNewMember()
    {
        var document = ParseNode("{\"name\":\"statepocket\"}");
        JsonPatch patchDocument = new([JsonPatchOperation.Add("/version", JsonValue.Create(1))]);
        var result = patchDocument.Apply(document);
        AssertJson("{\"name\":\"statepocket\",\"version\":1}", result);
    }

    [Fact]
    public void Apply_AddNestedObjectMember_ReturnsDocumentWithNewMember()
    {
        var document = ParseNode("{\"meta\":{\"name\":\"statepocket\"}}");
        JsonPatch patchDocument = new([JsonPatchOperation.Add("/meta/version", JsonValue.Create(1))]);
        var result = patchDocument.Apply(document);
        AssertJson("{\"meta\":{\"name\":\"statepocket\",\"version\":1}}", result);
    }

    [Fact]
    public void Apply_AddArrayElementAtIndex_InsertsElement()
    {
        var document = ParseNode("{\"items\":[\"a\",\"c\"]}");
        JsonPatch patchDocument = new([JsonPatchOperation.Add("/items/1", JsonValue.Create("b"))]);
        var result = patchDocument.Apply(document);
        AssertJson("{\"items\":[\"a\",\"b\",\"c\"]}", result);
    }

    [Fact]
    public void Apply_AddArrayElementAtEnd_AppendsElement()
    {
        var document = ParseNode("{\"items\":[\"a\",\"b\"]}");
        JsonPatch patchDocument = new([JsonPatchOperation.Add("/items/-", JsonValue.Create("c"))]);
        var result = patchDocument.Apply(document);
        AssertJson("{\"items\":[\"a\",\"b\",\"c\"]}", result);
    }

    [Fact]
    public void Apply_RemoveObjectMember_RemovesMember()
    {
        var document = ParseNode("{\"name\":\"statepocket\",\"version\":1}");
        JsonPatch patchDocument = new([JsonPatchOperation.Remove("/version")]);
        var result = patchDocument.Apply(document);
        AssertJson("{\"name\":\"statepocket\"}", result);
        AssertJson("{\"name\":\"statepocket\",\"version\":1}", document);
    }

    [Fact]
    public void Apply_RemoveArrayElement_RemovesElement()
    {
        var document = ParseNode("{\"items\":[\"a\",\"b\",\"c\"]}");
        JsonPatch patchDocument = new([JsonPatchOperation.Remove("/items/1")]);
        var result = patchDocument.Apply(document);
        AssertJson("{\"items\":[\"a\",\"c\"]}", result);
    }

    [Fact]
    public void Apply_ReplaceExistingScalar_ReplacesValue()
    {
        var document = ParseNode("{\"meta\":{\"name\":\"statepocket\"}}");
        JsonPatch patchDocument = new([JsonPatchOperation.Replace("/meta/name", JsonValue.Create("updated"))]);
        var result = patchDocument.Apply(document);
        AssertJson("{\"meta\":{\"name\":\"updated\"}}", result);
    }

    [Fact]
    public void Apply_TestMatchingValue_ReturnsUnchangedDocument()
    {
        var document = ParseNode("{\"meta\":{\"name\":\"statepocket\"}}");
        JsonPatch patchDocument = new([JsonPatchOperation.Test("/meta/name", JsonValue.Create("statepocket"))]);
        var result = patchDocument.Apply(document);
        AssertJson("{\"meta\":{\"name\":\"statepocket\"}}", result);
        AssertJson("{\"meta\":{\"name\":\"statepocket\"}}", document);
    }

    [Fact]
    public void Apply_FailedTest_ThrowsWithoutMutatingOriginalDocument()
    {
        var document = ParseNode("{\"meta\":{\"name\":\"statepocket\",\"version\":1}}");
        JsonPatch patchDocument = new(
            [
                JsonPatchOperation.Replace("/meta/version", JsonValue.Create(2)),
                JsonPatchOperation.Test("/meta/name", JsonValue.Create("other"))
            ]
        );
        Assert.Throws<JsonPatchException>(() => _ = patchDocument.Apply(document));
        AssertJson("{\"meta\":{\"name\":\"statepocket\",\"version\":1}}", document);
    }

    [Fact]
    public void Apply_CopyObjectMember_CopiesValue()
    {
        var document = ParseNode("{\"meta\":{\"name\":\"statepocket\"}}");
        JsonPatch patchDocument = new([JsonPatchOperation.Copy("/meta", "/metaCopy")]);
        var result = patchDocument.Apply(document);
        AssertJson("{\"meta\":{\"name\":\"statepocket\"},\"metaCopy\":{\"name\":\"statepocket\"}}", result);
        AssertJson("{\"meta\":{\"name\":\"statepocket\"}}", document);
    }

    [Fact]
    public void Apply_MoveObjectMember_MovesValue()
    {
        var document = ParseNode("{\"meta\":{\"name\":\"statepocket\"},\"other\":{}}");
        JsonPatch patchDocument = new([JsonPatchOperation.Move("/meta/name", "/other/name")]);
        var result = patchDocument.Apply(document);
        AssertJson("{\"meta\":{},\"other\":{\"name\":\"statepocket\"}}", result);
        AssertJson("{\"meta\":{\"name\":\"statepocket\"},\"other\":{}}", document);
    }

    [Fact]
    public void Apply_MoveValueIntoOwnChild_ThrowsWithoutMutatingOriginalDocument()
    {
        var document = ParseNode("{\"meta\":{\"name\":\"statepocket\"}}");
        JsonPatch patchDocument = new([JsonPatchOperation.Move("/meta", "/meta/child")]);
        Assert.Throws<JsonPatchException>(() => _ = patchDocument.Apply(document));
        AssertJson("{\"meta\":{\"name\":\"statepocket\"}}", document);
    }

    [Fact]
    public void Apply_MoveSamePathWithMissingSource_Throws()
    {
        var document = ParseNode("{\"meta\":{\"name\":\"statepocket\"}}");
        JsonPatch patchDocument = new([JsonPatchOperation.Move("/missing", "/missing")]);
        Assert.Throws<JsonPatchException>(() => _ = patchDocument.Apply(document));
        AssertJson("{\"meta\":{\"name\":\"statepocket\"}}", document);
    }

    [Fact]
    public void Constructor_ExposesTypedOperations()
    {
        JsonPatch patchDocument = new([JsonPatchOperation.Copy("/source", "/target")]);
        var operation = Assert.IsType<CopyOperation>(Assert.Single(patchDocument.Operations));
        Assert.Equal(JsonPatchOperationType.Copy, operation.Op);
        Assert.Equal("/target", operation.Path.ToString());
        Assert.Equal("/source", operation.From.ToString());
    }

    [Fact]
    public void Serialize_JsonPatch_WritesRfc6902Array()
    {
        JsonPatch patchDocument = new([JsonPatchOperation.Replace("/name", JsonValue.Create("new"))]);
        var json = JsonSerializer.Serialize(patchDocument);
        Assert.Equal("""[{"op":"replace","path":"/name","value":"new"}]""", json);
    }

    [Fact]
    public void Deserialize_JsonPatch_ReadsRfc6902Array()
    {
        var patchDocument =
            JsonSerializer.Deserialize<JsonPatch>("""[{ "op": "copy", "from": "/source", "path": "/target" }]""")
         ?? throw new InvalidOperationException("Expected patch document.");
        var operation = Assert.IsType<CopyOperation>(Assert.Single(patchDocument.Operations));
        Assert.Equal(JsonPatchOperationType.Copy, operation.Op);
        Assert.Equal("/target", operation.Path.ToString());
        Assert.Equal("/source", operation.From.ToString());
    }

    [Fact]
    public void Deserialize_JsonPatch_AllowsOpAfterPath()
    {
        var patchDocument =
            JsonSerializer.Deserialize<JsonPatch>("""[{ "path": "/target", "op": "copy", "from": "/source" }]""")
         ?? throw new InvalidOperationException("Expected patch document.");
        var operation = Assert.IsType<CopyOperation>(Assert.Single(patchDocument.Operations));
        Assert.Equal(JsonPatchOperationType.Copy, operation.Op);
        Assert.Equal("/target", operation.Path.ToString());
        Assert.Equal("/source", operation.From.ToString());
    }

    [Fact]
    public void Deserialize_JsonPatch_AllowsExtensionMemberBeforeOp()
    {
        var patchDocument =
            JsonSerializer.Deserialize<JsonPatch>(
                """[{ "comment": "ignored", "op": "add", "path": "/value", "value": 1 }]"""
            )
         ?? throw new InvalidOperationException("Expected patch document.");
        var operation = Assert.IsType<AddOperation>(Assert.Single(patchDocument.Operations));
        Assert.Equal(JsonPatchOperationType.Add, operation.Op);
        Assert.Equal("/value", operation.Path.ToString());
        Assert.Equal("1", operation.Value?.ToJsonString());
    }

    [Fact]
    public void Deserialize_JsonPatch_PreservesExplicitNullValue()
    {
        var patchDocument =
            JsonSerializer.Deserialize<JsonPatch>("""[{ "op": "add", "path": "/value", "value": null }]""")
         ?? throw new InvalidOperationException("Expected patch document.");
        var operation = Assert.IsType<AddOperation>(Assert.Single(patchDocument.Operations));
        Assert.Null(operation.Value);
        var result = patchDocument.Apply(ParseNode("{}"));
        AssertJson("""{"value":null}""", result);
    }

    [Fact]
    public void Deserialize_JsonPatch_IgnoresExtensionMembers()
    {
        var patchDocument =
            JsonSerializer.Deserialize<JsonPatch>(
                """[{ "op": "add", "path": "/value", "value": 1, "comment": "ignored" }]"""
            )
         ?? throw new InvalidOperationException("Expected patch document.");
        var operation = Assert.IsType<AddOperation>(Assert.Single(patchDocument.Operations));
        Assert.Equal(JsonPatchOperationType.Add, operation.Op);
        Assert.Equal("/value", operation.Path.ToString());
        Assert.Equal("1", operation.Value?.ToJsonString());
    }

    [Fact]
    public void JsonPatchOperation_ClonesValueForPublicAccess()
    {
        var operation = JsonPatchOperation.Add("/value", ParseNode("""{"name":"statepocket"}"""));
        var first = operation.Value?.AsObject() ?? throw new InvalidOperationException("Expected object.");
        first["name"] = "mutated";
        Assert.Equal("""{"name":"statepocket"}""", operation.Value?.ToJsonString());
    }

    [Fact]
    public void Apply_AddNullValue_AllowsExplicitJsonNull()
    {
        var document = ParseNode("{}");
        JsonPatch patchDocument = new([JsonPatchOperation.Add("/value", null)]);
        var result = patchDocument.Apply(document);
        AssertJson("""{"value":null}""", result);
    }

    private static void AssertJson(string expectedJson, JsonNode? actual)
    {
        Assert.NotNull(actual);
        Assert.True(
            JsonNode.DeepEquals(ParseNode(expectedJson), actual),
            $"Expected JSON '{expectedJson}' but got '{actual.ToJsonString()}'."
        );
    }

    private static JsonNode ParseNode(string json)
    {
        return JsonNode.Parse(json) ?? throw new InvalidOperationException("JSON must parse.");
    }
}
