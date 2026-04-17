namespace StatePocket.JsonPatch.Tests.ReferenceSuite;

internal sealed record ReferenceSuiteCase(
    string SourceFile,
    string Comment,
    string DocumentJson,
    string PatchJson,
    string ExpectedJson,
    bool ShouldThrow
);
