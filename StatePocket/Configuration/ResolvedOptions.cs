using System.Collections.ObjectModel;

namespace StatePocket.Configuration;

internal sealed class ResolvedOptions(string databasePath, IReadOnlyCollection<string> enabledTools)
{
    public string DatabasePath { get; } = databasePath;
    public IReadOnlyCollection<string> EnabledTools { get; } =
        new ReadOnlyCollection<string>([.. enabledTools.Order(StringComparer.Ordinal)]);
}
