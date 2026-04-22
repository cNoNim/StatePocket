namespace StatePocket.Json.Patch;

public sealed class JsonPatchException : Exception
{
    internal JsonPatchException(string message) : base(message) {}
    internal JsonPatchException(string message, Exception innerException) : base(message, innerException) {}
}
