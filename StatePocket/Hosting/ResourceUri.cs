using System.Reflection;

namespace StatePocket.Hosting;

internal static class ResourceUri
{
    private const string ResourceUriSchemeMetadataKey = "ResourceUriScheme";
    private static readonly Lazy<string> UriScheme = new(LoadUriScheme);
    private static string Scheme => UriScheme.Value;

    internal static string Format(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return $"{Scheme}://{value.TrimStart('/')}";
    }

    internal static bool HasScheme(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return value.StartsWith($"{Scheme}://", StringComparison.Ordinal);
    }

    private static string LoadUriScheme()
    {
        var metadata = typeof(McpHostFactory).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                                             .ToDictionary(
                                                  static attribute => attribute.Key,
                                                  static attribute => attribute.Value,
                                                  StringComparer.Ordinal
                                              );
        if (!metadata.TryGetValue(ResourceUriSchemeMetadataKey, out var resourceUriScheme)
         || string.IsNullOrWhiteSpace(resourceUriScheme))
        {
            throw new InvalidOperationException($"Assembly metadata '{ResourceUriSchemeMetadataKey}' is required.");
        }
        var normalized = resourceUriScheme.Trim();
        if (normalized.Contains("://", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Assembly metadata '{ResourceUriSchemeMetadataKey}' must not include '://'."
            );
        }
        return normalized;
    }
}
