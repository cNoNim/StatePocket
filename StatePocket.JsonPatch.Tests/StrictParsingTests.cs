using System.Text.Json;
using StatePocket.JsonPatch.Exceptions;

namespace StatePocket.JsonPatch.Tests;

public sealed class StrictParsingTests
{
    public static TheoryData<string, string> DuplicatePropertyCases =>
    [
        new TheoryDataRow<string, string>(
            "duplicate op",
            """[{ "op": "add", "path": "/x", "value": 1, "op": "remove" }]"""
        ),
        new TheoryDataRow<string, string>(
            "duplicate path",
            """[{ "op": "add", "path": "/x", "value": 1, "path": "/y" }]"""
        ),
        new TheoryDataRow<string, string>(
            "duplicate from",
            """[{ "op": "copy", "from": "/x", "path": "/y", "from": "/z" }]"""
        ),
        new TheoryDataRow<string, string>(
            "duplicate value",
            """[{ "op": "add", "path": "/x", "value": 1, "value": 2 }]"""
        )
    ];
    public static TheoryData<string, string> InvalidArrayIndexCases =>
    [
        new TheoryDataRow<string, string>("leading zero", """[{ "op": "add", "path": "/01", "value": "x" }]"""),
        new TheoryDataRow<string, string>("plus sign", """[{ "op": "add", "path": "/+1", "value": "x" }]""")
    ];

    [Theory]
    [MemberData(nameof(DuplicatePropertyCases))]
    public void Parse_DuplicateKnownProperty_Throws(string comment, string patchJson)
    {
        _ = comment;
        using var document = JsonDocument.Parse(patchJson);
        Assert.Throws<JsonPatchException>(() => _ = PatchDocument.Parse(document.RootElement));
    }

    [Theory]
    [MemberData(nameof(InvalidArrayIndexCases))]
    public void Apply_InvalidArrayIndexFormat_Throws(string comment, string patchJson)
    {
        _ = comment;
        using var document = JsonDocument.Parse(patchJson);
        var patch = PatchDocument.Parse(document.RootElement);
        Assert.Throws<JsonPatchException>(() => _ = patch.Apply(ParseJson("""["a"]""")));
    }

    private static JsonElement ParseJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
