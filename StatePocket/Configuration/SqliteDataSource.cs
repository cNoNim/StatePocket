namespace StatePocket.Configuration;

internal static class SqliteDataSource
{
    internal static void ValidateSupported(string dataSource)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataSource);
        if (IsInMemory(dataSource))
        {
            throw new ConfigurationException(
                "In-memory SQLite datasources are not supported. Provide --db-path pointing to a database file."
            );
        }
    }

    internal static string FormatForDisplay(string dataSource)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataSource);
        if (IsFileUri(dataSource))
        {
            return dataSource;
        }
        try
        {
            return Path.GetFullPath(dataSource);
        }
        catch (Exception)
        {
            return dataSource;
        }
    }

    internal static string? GetDirectoryPath(string dataSource)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataSource);
        if (!IsFileUri(dataSource))
        {
            return Path.GetDirectoryName(dataSource);
        }
        if (!Uri.TryCreate(dataSource, UriKind.Absolute, out var uri)
         || !uri.IsFile)
        {
            return null;
        }
        return string.IsNullOrEmpty(uri.LocalPath) ? null : Path.GetDirectoryName(uri.LocalPath);
    }

    private static bool IsInMemory(string dataSource)
    {
        if (string.Equals(dataSource, ":memory:", StringComparison.Ordinal))
        {
            return true;
        }
        if (!IsFileUri(dataSource))
        {
            return false;
        }
        var queryStart = dataSource.IndexOf('?', StringComparison.Ordinal);
        var path = queryStart >= 0 ? dataSource[..queryStart] : dataSource;
        if (string.Equals(path, "file::memory:", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        if (queryStart < 0
         || queryStart == dataSource.Length - 1)
        {
            return false;
        }
        foreach (var segment in dataSource[(queryStart + 1)..]
                    .Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separatorIndex = segment.IndexOf('=', StringComparison.Ordinal);
            if (separatorIndex <= 0
             || separatorIndex == segment.Length - 1)
            {
                continue;
            }
            var key = Uri.UnescapeDataString(segment[..separatorIndex]);
            var value = Uri.UnescapeDataString(segment[(separatorIndex + 1)..]);
            if (string.Equals(key, "mode", StringComparison.OrdinalIgnoreCase)
             && string.Equals(value, "memory", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private static bool IsFileUri(string dataSource)
    {
        return dataSource.StartsWith("file:", StringComparison.OrdinalIgnoreCase);
    }
}
