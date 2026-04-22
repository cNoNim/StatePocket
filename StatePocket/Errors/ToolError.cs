using System.Text.Json.Serialization;

namespace StatePocket.Errors;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(InvalidInputToolError), "invalid_input")]
[JsonDerivedType(typeof(InvalidArgumentToolError), "invalid_argument")]
[JsonDerivedType(typeof(InvalidJsonToolError), "invalid_json")]
[JsonDerivedType(typeof(InvalidPointerToolError), "invalid_pointer")]
[JsonDerivedType(typeof(NotFoundToolError), "not_found")]
[JsonDerivedType(typeof(BusyToolError), "busy")]
[JsonDerivedType(typeof(AlreadyExistsToolError), "already_exists")]
[JsonDerivedType(typeof(RevisionConflictToolError), "revision_conflict")]
[JsonDerivedType(typeof(InvalidQueryToolError), "invalid_query")]
[JsonDerivedType(typeof(InvalidPatchToolError), "invalid_patch")]
[JsonDerivedType(typeof(OperationFailedToolError), "operation_failed")]
[JsonDerivedType(typeof(InternalToolError), "internal_error")]
internal abstract record ToolError
{
    public required string Message { get; init; }
    public required bool Retryable { get; init; }
}
