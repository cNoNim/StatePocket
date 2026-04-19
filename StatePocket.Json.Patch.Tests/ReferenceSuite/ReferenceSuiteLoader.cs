using System.Text.Json;

namespace StatePocket.Json.Patch.Tests.ReferenceSuite;

internal static class ReferenceSuiteLoader
{
    private static readonly Lazy<List<ReferenceSuiteCase>> AllCases = new(LoadCases);

    public static IReadOnlyList<ReferenceSuiteCase> LoadAll()
    {
        return AllCases.Value;
    }

    private static List<ReferenceSuiteCase> LoadCases()
    {
        List<ReferenceSuiteCase> cases = [];
        foreach (var fixtureFile in GetFixtureFiles())
        {
            using var document = JsonDocument.Parse(File.ReadAllText(fixtureFile.FullName));
            var index = 0;
            foreach (var testCase in document.RootElement.EnumerateArray())
            {
                index++;
                var comment =
                    testCase.TryGetProperty("comment", out var commentElement)
                 && commentElement.ValueKind == JsonValueKind.String
                      ? commentElement.GetString()!
                      : $"{fixtureFile.Name} case #{index}";
                var documentJson = testCase.GetProperty("doc")
                                           .GetRawText();
                var patchJson = testCase.GetProperty("patch")
                                        .GetRawText();
                var shouldThrow = testCase.TryGetProperty("error", out _);
                var expectedJson = testCase.TryGetProperty("expected", out var expectedElement)
                  ? expectedElement.GetRawText()
                  : documentJson;
                cases.Add(
                    new ReferenceSuiteCase(
                        fixtureFile.Name,
                        comment,
                        documentJson,
                        patchJson,
                        expectedJson,
                        shouldThrow
                    )
                );
            }
        }
        return cases;
    }

    private static IEnumerable<FileInfo> GetFixtureFiles()
    {
        DirectoryInfo fixturesDirectory = new(Path.Combine(AppContext.BaseDirectory, "Fixtures", "ReferenceSuite"));
        return fixturesDirectory.EnumerateFiles("*.json", SearchOption.TopDirectoryOnly)
                                .OrderBy(static file => file.Name, StringComparer.Ordinal);
    }
}
