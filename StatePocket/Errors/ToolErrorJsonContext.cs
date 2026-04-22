using System.Text.Json.Serialization;

namespace StatePocket.Errors;

[JsonSourceGenerationOptions(
    GenerationMode = JsonSourceGenerationMode.Metadata,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true
)]
[JsonSerializable(typeof(ToolError))]
[JsonSerializable(typeof(InvalidInputToolError))]
[JsonSerializable(typeof(InvalidArgumentToolError))]
[JsonSerializable(typeof(InvalidJsonToolError))]
[JsonSerializable(typeof(NotFoundToolError))]
[JsonSerializable(typeof(BusyToolError))]
[JsonSerializable(typeof(AlreadyExistsToolError))]
[JsonSerializable(typeof(RevisionConflictToolError))]
[JsonSerializable(typeof(InvalidQueryToolError))]
[JsonSerializable(typeof(InvalidPatchToolError))]
[JsonSerializable(typeof(OperationFailedToolError))]
[JsonSerializable(typeof(InternalToolError))]
internal sealed partial class ToolErrorJsonContext : JsonSerializerContext;
