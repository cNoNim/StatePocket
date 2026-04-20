namespace StatePocket.Storage;

internal sealed class KvStoreConflictException(string message, long? currentRevision = null) : Exception(message)
{
    public long? CurrentRevision { get; } = currentRevision;
}
