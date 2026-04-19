using System.Text.Json;
using System.Text.Json.Nodes;
using StatePocket.Json.Patch.Exceptions;

namespace StatePocket.Json.Patch.Tests.ReferenceSuite;

public sealed class ReferenceSuiteTests
{
    public static TheoryData<string, string, string, string, string> SuccessCases => BuildSuccessCases();
    public static TheoryData<string, string, string, string> ErrorCases => BuildErrorCases();

    [Fact]
    public void LoadAll_TracksFullReferenceInventory()
    {
        var cases = ReferenceSuiteLoader.LoadAll();
        var errorCases = cases.Where(static testCase => testCase.ShouldThrow)
                              .ToArray();
        var successCases = cases.Where(static testCase => !testCase.ShouldThrow)
                                .ToArray();
        Assert.Equal(94, cases.Count);
        Assert.Equal(24, errorCases.Length);
        Assert.Equal(70, successCases.Length);
    }

    [Theory]
    [MemberData(nameof(SuccessCases))]
    public void Apply_ReferenceSuiteSuccessCases_Succeeds(
        string sourceFile,
        string comment,
        string documentJson,
        string patchJson,
        string expectedJson
    )
    {
        _ = sourceFile;
        _ = comment;
        var document = ParseNode(documentJson);
        var patchDocument = ParseJsonPatch(patchJson);
        var result = patchDocument.Apply(document);
        AssertJson(expectedJson, result);
        AssertJson(documentJson, document);
    }

    [Theory]
    [MemberData(nameof(ErrorCases))]
    public void Apply_ReferenceSuiteErrorCases_Throws(
        string sourceFile,
        string comment,
        string documentJson,
        string patchJson
    )
    {
        _ = sourceFile;
        _ = comment;
        var document = ParseNode(documentJson);
        var exception = Record.Exception(() =>
            {
                var patchDocument = ParseJsonPatch(patchJson);
                _ = patchDocument.Apply(document);
            }
        );
        Assert.NotNull(exception);
        Assert.True(
            exception is JsonPatchException or JsonException,
            $"Expected JsonPatchException or JsonException but got '{exception.GetType().Name}'."
        );
        AssertJson(documentJson, document);
    }

    private static TheoryData<string, string, string, string, string> BuildSuccessCases()
    {
        TheoryData<string, string, string, string, string> rows = [];
        foreach (var testCase in ReferenceSuiteLoader.LoadAll()
                                                     .Where(static testCase => testCase is
                                                          {
                                                              ShouldThrow: false
                                                          }
                                                      ))
        {
            rows.Add(
                testCase.SourceFile,
                testCase.Comment,
                testCase.DocumentJson,
                testCase.PatchJson,
                testCase.ExpectedJson
            );
        }
        return rows;
    }

    private static TheoryData<string, string, string, string> BuildErrorCases()
    {
        TheoryData<string, string, string, string> rows = [];
        foreach (var testCase in ReferenceSuiteLoader.LoadAll()
                                                     .Where(static testCase => testCase is
                                                          {
                                                              ShouldThrow: true
                                                          }
                                                      ))
        {
            rows.Add(
                testCase.SourceFile,
                testCase.Comment,
                testCase.DocumentJson,
                testCase.PatchJson
            );
        }
        return rows;
    }

    private static JsonPatch ParseJsonPatch(string patchJson)
    {
        return JsonSerializer.Deserialize<JsonPatch>(patchJson)
            ?? throw new InvalidOperationException("Patch JSON must deserialize.");
    }

    private static void AssertJson(string expectedJson, JsonNode? actual)
    {
        var expected = ParseNode(expectedJson);
        Assert.True(
            JsonNode.DeepEquals(expected, actual),
            $"Expected JSON '{expectedJson}' but got '{actual?.ToJsonString() ?? "null"}'."
        );
    }

    private static JsonNode? ParseNode(string json)
    {
        return JsonNode.Parse(json);
    }
}
