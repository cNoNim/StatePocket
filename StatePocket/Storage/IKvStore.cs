using System.Text.Json;
using StatePocket.Json.Patch;

namespace StatePocket.Storage;

internal interface IKvStore
{
    public Task SetValueAsync(
        string? @namespace,
        string key,
        JsonElement value,
        long? ttlSeconds,
        CancellationToken cancellationToken
    );

    public Task<KvValue?> GetValueAsync(string? @namespace, string key, CancellationToken cancellationToken);

    public Task<IReadOnlyDictionary<string, KvValue>> GetValuesAsync(
        string? @namespace,
        IReadOnlyList<string> keys,
        CancellationToken cancellationToken
    );

    public Task<PageResult<KeyValuePair<string, KvValue>>> ListValuesPageAsync(
        string? @namespace,
        string? pattern,
        string? cursor,
        int limit,
        CancellationToken cancellationToken
    );

    public Task<PageResult<string>> ListNamespacesPageAsync(
        string? pattern,
        string? cursor,
        int limit,
        CancellationToken cancellationToken
    );

    public Task<PageResult<string>> ListKeysPageAsync(
        string? @namespace,
        string? pattern,
        string? cursor,
        int limit,
        CancellationToken cancellationToken
    );

    public Task<bool> PatchValueAsync(
        string? @namespace,
        string key,
        JsonPatch patch,
        CancellationToken cancellationToken
    );

    public Task<bool> DeleteValueAsync(string? @namespace, string key, CancellationToken cancellationToken);
    public Task PurgeExpiredAsync(CancellationToken cancellationToken);
}
