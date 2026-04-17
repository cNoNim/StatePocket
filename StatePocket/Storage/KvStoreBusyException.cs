using ModelContextProtocol;

namespace StatePocket.Storage;

internal sealed class KvStoreBusyException(string message, Exception innerException)
    : McpException(message, innerException);
