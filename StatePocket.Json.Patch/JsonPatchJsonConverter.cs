using System.Text.Json;
using System.Text.Json.Serialization;

namespace StatePocket.Json.Patch;

internal sealed class JsonPatchJsonConverter : JsonConverter<JsonPatch>
{
    private static readonly JsonPatchJsonContext ReadContext = CreateReadContext();

    public override JsonPatch Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var operations = JsonSerializer.Deserialize(ref reader, ReadContext.NullableJsonPatchOperationArrayTypeInfo)
                      ?? throw new JsonException("Patch document must be a JSON array.");
        var nonNullOperations = new JsonPatchOperation[operations.Length];
        for (var i = 0; i < operations.Length; i++)
        {
            nonNullOperations[i] = operations[i] ?? throw new JsonException("Patch operation must be a JSON object.");
        }
        return new JsonPatch(nonNullOperations);
    }

    public override void Write(Utf8JsonWriter writer, JsonPatch value, JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(
            writer,
            value.Operations,
            JsonPatchJsonContext.Default.IReadOnlyListJsonPatchOperation
        );
    }

    private static JsonPatchJsonContext CreateReadContext()
    {
        JsonSerializerOptions options = new()
        {
            AllowDuplicateProperties = false,
            AllowOutOfOrderMetadataProperties = true
        };
        return new JsonPatchJsonContext(options);
    }
}
