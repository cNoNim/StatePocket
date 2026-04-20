using StatePocket.Configuration;

namespace StatePocket.Storage;

internal sealed partial class SqliteKvStore(ResolvedOptions resolvedOptions, TimeProvider timeProvider) : IKvStore
{
    private const string DefaultNamespace = "default";
    private const string BusyMessage = "The database is busy with another write operation. Try again.";
    private const int SqliteLegacyParameterLimit = 999;
    private const int BatchGetReservedParameterCount = 2;
    private const int MaxBatchGetKeys = SqliteLegacyParameterLimit - BatchGetReservedParameterCount;
    private const int SqliteBusyErrorCode = 5;
    private const int SqliteLockedErrorCode = 6;
    private const int SqliteBusyTimeoutSeconds = 1;
}
