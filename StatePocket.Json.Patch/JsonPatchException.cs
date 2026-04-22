using StatePocket.Json.Pointer;

namespace StatePocket.Json.Patch;

public sealed class JsonPatchException : Exception
{
    internal JsonPatchException(string message, string? operation = null, JsonPointer? targetPath = null) : base(
        message
    )
    {
        Operation = operation;
        TargetPath = targetPath?.ToString();
    }

    internal JsonPatchException(
        string message,
        Exception innerException,
        string? operation = null,
        JsonPointer? targetPath = null
    ) : base(message, innerException)
    {
        Operation = operation;
        TargetPath = targetPath?.ToString();
    }

    public int? OperationIndex { get; internal set; }
    public string? Operation { get; }
    public string? TargetPath { get; }
}
