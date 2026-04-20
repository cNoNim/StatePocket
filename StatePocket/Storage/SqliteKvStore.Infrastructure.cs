using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace StatePocket.Storage;

internal sealed partial class SqliteKvStore
{
    private Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        return OpenConnectionCoreAsync(CreateConnection(null), cancellationToken);
    }

    private Task<SqliteConnection> OpenWriteConnectionAsync(CancellationToken cancellationToken)
    {
        return OpenConnectionCoreAsync(CreateConnection(SqliteBusyTimeoutSeconds), cancellationToken);
    }

    private SqliteConnection CreateConnection(int? defaultTimeoutSeconds)
    {
        SqliteConnectionStringBuilder builder = new()
        {
            DataSource = resolvedOptions.DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate
        };
        if (defaultTimeoutSeconds is not null)
        {
            builder.DefaultTimeout = defaultTimeoutSeconds.Value;
        }
        return new SqliteConnection(builder.ToString());
    }

    private static async Task<SqliteConnection> OpenConnectionCoreAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken
    )
    {
        await connection.OpenAsync(cancellationToken)
                        .ConfigureAwait(false);
        return connection;
    }

    private static PageResult<T> CreatePageResult<T>(List<T> items, int limit, Func<T, string> cursorSelector)
    {
        if (items.Count <= limit)
        {
            return new PageResult<T>
            {
                Items = items,
                NextCursor = null
            };
        }
        var nextCursor = cursorSelector(items[limit - 1]);
        items.RemoveAt(items.Count - 1);
        return new PageResult<T>
        {
            Items = items,
            NextCursor = nextCursor
        };
    }

    private static async Task ExecuteWriteAsync(Func<Task> action)
    {
        try
        {
            await action()
               .ConfigureAwait(false);
        }
        catch (SqliteException exception) when (IsWriteContention(exception))
        {
            throw new KvStoreBusyException(BusyMessage, exception);
        }
    }

    private static async Task<T> ExecuteWriteAsync<T>(Func<Task<T>> action)
    {
        try
        {
            return await action()
               .ConfigureAwait(false);
        }
        catch (SqliteException exception) when (IsWriteContention(exception))
        {
            throw new KvStoreBusyException(BusyMessage, exception);
        }
    }

    private static string NormalizeNamespace(string? @namespace)
    {
        return @namespace ?? DefaultNamespace;
    }

    private static bool IsWriteContention(SqliteException exception)
    {
        return exception.SqliteErrorCode is SqliteBusyErrorCode or SqliteLockedErrorCode;
    }

    private static JsonElement ParseJson(string rawJson)
    {
        using var document = JsonDocument.Parse(rawJson);
        return document.RootElement.Clone();
    }

    private static JsonNode? ParseNode(string rawJson)
    {
        return JsonNode.Parse(rawJson);
    }

    private static bool IsLiveAt(string? expiresAt, string now)
    {
        return expiresAt is null || string.CompareOrdinal(expiresAt, now) > 0;
    }

    private static string FormatTimestamp(DateTimeOffset value)
    {
        return value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);
    }
}
