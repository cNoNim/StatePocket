namespace StatePocket.Json.Path;

public sealed class JsonPathException : Exception
{
    internal JsonPathException(string message) : base(message) {}
    internal JsonPathException(string message, Exception innerException) : base(message, innerException) {}
}
