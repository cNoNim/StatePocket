using System.Text.Json;

namespace StatePocket.Json.Path.Tests.ReferenceSuite;

internal static class ComplianceSuiteLoader
{
    private static readonly Lazy<List<ComplianceSuiteCase>> AllCases = new(LoadCases);

    public static IReadOnlyList<ComplianceSuiteCase> LoadAll()
    {
        return AllCases.Value;
    }

    private static List<ComplianceSuiteCase> LoadCases()
    {
        var fixturePath = System.IO.Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "ComplianceSuite",
            "cts.json"
        );
        using var document = JsonDocument.Parse(File.ReadAllText(fixturePath));
        List<ComplianceSuiteCase> cases = [];
        foreach (var testCase in document.RootElement.GetProperty("tests")
                                         .EnumerateArray())
        {
            cases.Add(
                new ComplianceSuiteCase(
                    testCase.GetProperty("name")
                            .GetString()!,
                    testCase.GetProperty("selector")
                            .GetString()!,
                    GetRawTextOrNull(testCase, "document"),
                    GetJsonArrayOrNull(testCase, "result"),
                    GetNestedJsonArrayOrNull(testCase, "results"),
                    GetStringArrayOrNull(testCase, "result_paths"),
                    GetNestedStringArrayOrNull(testCase, "results_paths"),
                    testCase.TryGetProperty("invalid_selector", out var invalidSelectorElement)
                 && invalidSelectorElement.ValueKind == JsonValueKind.True,
                    GetStringArrayOrEmpty(testCase, "tags")
                )
            );
        }
        return cases;
    }

    private static string? GetRawTextOrNull(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) ? property.GetRawText() : null;
    }

    private static string[]? GetJsonArrayOrNull(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }
        return
        [
            .. property.EnumerateArray()
                       .Select(static item => item.GetRawText())
        ];
    }

    private static string[][]? GetNestedJsonArrayOrNull(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }
        return
        [
            .. property.EnumerateArray()
                       .Select(static resultSet => resultSet.EnumerateArray()
                                                            .Select(static item => item.GetRawText())
                                                            .ToArray()
                        )
        ];
    }

    private static string[]? GetStringArrayOrNull(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }
        return
        [
            .. property.EnumerateArray()
                       .Select(static item => item.GetString()!)
        ];
    }

    private static string[][]? GetNestedStringArrayOrNull(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }
        return
        [
            .. property.EnumerateArray()
                       .Select(static resultSet => resultSet.EnumerateArray()
                                                            .Select(static item => item.GetString()!)
                                                            .ToArray()
                        )
        ];
    }

    private static string[] GetStringArrayOrEmpty(JsonElement element, string propertyName)
    {
        return GetStringArrayOrNull(element, propertyName) ?? [];
    }
}
