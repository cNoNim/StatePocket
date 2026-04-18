using System.Collections.ObjectModel;

namespace StatePocket.Configuration;

internal sealed class ResolvedOptions
{
    public ResolvedOptions(string databasePath, IReadOnlyCollection<string> enabledTools)
    {
        DatabasePath = databasePath;
        EnabledTools =
            new ReadOnlyCollection<string>([.. enabledTools.OrderBy(static tool => tool, StringComparer.Ordinal)]);
    }

    public string DatabasePath { get; }
    public IReadOnlyCollection<string> EnabledTools { get; }
}
