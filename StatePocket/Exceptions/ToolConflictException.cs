namespace StatePocket.Exceptions;

internal abstract class ToolConflictException(
    string message,
    string @namespace,
    string key,
    Exception? innerException = null
) : ToolErrorException(message, innerException)
{
    protected string Namespace { get; } = @namespace;
    protected string Key { get; } = key;
}
