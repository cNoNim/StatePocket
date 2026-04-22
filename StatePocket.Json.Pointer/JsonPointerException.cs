namespace StatePocket.Json.Pointer;

public sealed class JsonPointerException : Exception
{
    internal JsonPointerException(string message) : base(message) {}
    internal JsonPointerException(string message, Exception innerException) : base(message, innerException) {}
}
