using System.Text.Json;
using System.Text.Json.Nodes;
using StatePocket.JsonPatch.Exceptions;

namespace StatePocket.JsonPatch.Tests;

public sealed class PatchDocumentTests
{
    [Fact]
    public void Apply_ReplaceRoot_ReturnsReplacementDocument()
    {
        var document = ParseNode("{\"name\":\"old\"}");
        var replacement = ParseNode("{\"name\":\"new\"}");
        PatchDocument patchDocument = new([new PatchOperation(PatchOperationType.Replace, "", replacement)]);
        var result = patchDocument.Apply(document);
        AssertJson("{\"name\":\"new\"}", result);
        AssertJson("{\"name\":\"old\"}", document);
    }

    [Fact]
    public void Apply_AddObjectMember_ReturnsDocumentWithNewMember()
    {
        var document = ParseNode("{\"name\":\"statepocket\"}");
        PatchDocument patchDocument =
            new([new PatchOperation(PatchOperationType.Add, "/version", JsonValue.Create(1))]);
        var result = patchDocument.Apply(document);
        AssertJson("{\"name\":\"statepocket\",\"version\":1}", result);
    }

    [Fact]
    public void Apply_AddNestedObjectMember_ReturnsDocumentWithNewMember()
    {
        var document = ParseNode("{\"meta\":{\"name\":\"statepocket\"}}");
        PatchDocument patchDocument =
            new([new PatchOperation(PatchOperationType.Add, "/meta/version", JsonValue.Create(1))]);
        var result = patchDocument.Apply(document);
        AssertJson("{\"meta\":{\"name\":\"statepocket\",\"version\":1}}", result);
    }

    [Fact]
    public void Apply_AddArrayElementAtIndex_InsertsElement()
    {
        var document = ParseNode("{\"items\":[\"a\",\"c\"]}");
        PatchDocument patchDocument =
            new([new PatchOperation(PatchOperationType.Add, "/items/1", JsonValue.Create("b"))]);
        var result = patchDocument.Apply(document);
        AssertJson("{\"items\":[\"a\",\"b\",\"c\"]}", result);
    }

    [Fact]
    public void Apply_AddArrayElementAtEnd_AppendsElement()
    {
        var document = ParseNode("{\"items\":[\"a\",\"b\"]}");
        PatchDocument patchDocument =
            new([new PatchOperation(PatchOperationType.Add, "/items/-", JsonValue.Create("c"))]);
        var result = patchDocument.Apply(document);
        AssertJson("{\"items\":[\"a\",\"b\",\"c\"]}", result);
    }

    [Fact]
    public void Apply_RemoveObjectMember_RemovesMember()
    {
        var document = ParseNode("{\"name\":\"statepocket\",\"version\":1}");
        PatchDocument patchDocument = new([new PatchOperation(PatchOperationType.Remove, "/version")]);
        var result = patchDocument.Apply(document);
        AssertJson("{\"name\":\"statepocket\"}", result);
        AssertJson("{\"name\":\"statepocket\",\"version\":1}", document);
    }

    [Fact]
    public void Apply_RemoveArrayElement_RemovesElement()
    {
        var document = ParseNode("{\"items\":[\"a\",\"b\",\"c\"]}");
        PatchDocument patchDocument = new([new PatchOperation(PatchOperationType.Remove, "/items/1")]);
        var result = patchDocument.Apply(document);
        AssertJson("{\"items\":[\"a\",\"c\"]}", result);
    }

    [Fact]
    public void Apply_ReplaceExistingScalar_ReplacesValue()
    {
        var document = ParseNode("{\"meta\":{\"name\":\"statepocket\"}}");
        PatchDocument patchDocument = new(
            [new PatchOperation(PatchOperationType.Replace, "/meta/name", JsonValue.Create("updated"))]
        );
        var result = patchDocument.Apply(document);
        AssertJson("{\"meta\":{\"name\":\"updated\"}}", result);
    }

    [Fact]
    public void Apply_TestMatchingValue_ReturnsUnchangedDocument()
    {
        var document = ParseNode("{\"meta\":{\"name\":\"statepocket\"}}");
        PatchDocument patchDocument = new(
            [new PatchOperation(PatchOperationType.Test, "/meta/name", JsonValue.Create("statepocket"))]
        );
        var result = patchDocument.Apply(document);
        AssertJson("{\"meta\":{\"name\":\"statepocket\"}}", result);
        AssertJson("{\"meta\":{\"name\":\"statepocket\"}}", document);
    }

    [Fact]
    public void Apply_FailedTest_ThrowsWithoutMutatingOriginalDocument()
    {
        var document = ParseNode("{\"meta\":{\"name\":\"statepocket\",\"version\":1}}");
        PatchDocument patchDocument = new(
            [
                new PatchOperation(PatchOperationType.Replace, "/meta/version", JsonValue.Create(2)),
                new PatchOperation(PatchOperationType.Test, "/meta/name", JsonValue.Create("other"))
            ]
        );
        Assert.Throws<JsonPatchException>(() => _ = patchDocument.Apply(document));
        AssertJson("{\"meta\":{\"name\":\"statepocket\",\"version\":1}}", document);
    }

    [Fact]
    public void Apply_CopyObjectMember_CopiesValue()
    {
        var document = ParseNode("{\"meta\":{\"name\":\"statepocket\"}}");
        PatchDocument patchDocument = new([new PatchOperation(PatchOperationType.Copy, "/metaCopy", from: "/meta")]);
        var result = patchDocument.Apply(document);
        AssertJson("{\"meta\":{\"name\":\"statepocket\"},\"metaCopy\":{\"name\":\"statepocket\"}}", result);
        AssertJson("{\"meta\":{\"name\":\"statepocket\"}}", document);
    }

    [Fact]
    public void Apply_MoveObjectMember_MovesValue()
    {
        var document = ParseNode("{\"meta\":{\"name\":\"statepocket\"},\"other\":{}}");
        PatchDocument patchDocument =
            new([new PatchOperation(PatchOperationType.Move, "/other/name", from: "/meta/name")]);
        var result = patchDocument.Apply(document);
        AssertJson("{\"meta\":{},\"other\":{\"name\":\"statepocket\"}}", result);
        AssertJson("{\"meta\":{\"name\":\"statepocket\"},\"other\":{}}", document);
    }

    [Fact]
    public void Apply_MoveValueIntoOwnChild_ThrowsWithoutMutatingOriginalDocument()
    {
        var document = ParseNode("{\"meta\":{\"name\":\"statepocket\"}}");
        PatchDocument patchDocument = new([new PatchOperation(PatchOperationType.Move, "/meta/child", from: "/meta")]);
        Assert.Throws<JsonPatchException>(() => _ = patchDocument.Apply(document));
        AssertJson("{\"meta\":{\"name\":\"statepocket\"}}", document);
    }

    [Fact]
    public void Apply_MoveSamePathWithMissingSource_Throws()
    {
        var document = ParseNode("{\"meta\":{\"name\":\"statepocket\"}}");
        PatchDocument patchDocument = new([new PatchOperation(PatchOperationType.Move, "/missing", from: "/missing")]);
        Assert.Throws<JsonPatchException>(() => _ = patchDocument.Apply(document));
        AssertJson("{\"meta\":{\"name\":\"statepocket\"}}", document);
    }

    [Fact]
    public void Parse_InvalidJsonPointer_Throws()
    {
        using var document = JsonDocument.Parse("""[{ "op": "replace", "path": "foo", "value": 1 }]""");
        Assert.Throws<JsonPatchException>(() => _ = PatchDocument.Parse(document.RootElement));
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
