namespace StatePocket.Configuration;

internal static class ToolNames
{
    public const string SetValue = "set_value";
    public const string GetValue = "get_value";
    public const string GetValues = "get_values";
    public const string QueryValues = "query_values";
    public const string ListNamespaces = "list_namespaces";
    public const string ListKeys = "list_keys";
    public const string DeleteValue = "delete_value";
    public const string PatchValue = "patch_value";
    public static IReadOnlyCollection<string> All { get; } =
    [
        SetValue,
        GetValue,
        GetValues,
        QueryValues,
        ListNamespaces,
        ListKeys,
        DeleteValue,
        PatchValue
    ];
}
