using StatePocket.Errors;

namespace StatePocket.Storage;

internal sealed class KvStoreBusyException(string message, Exception innerException)
    : ToolBusyException(message, innerException);
