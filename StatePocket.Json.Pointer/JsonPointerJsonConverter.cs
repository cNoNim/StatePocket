using System.Text.Json;
using System.Text.Json.Serialization;

namespace StatePocket.Json.Pointer;

public sealed class JsonPointerJsonConverter : JsonConverter<JsonPointer>
{
    public override JsonPointer Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("JSON Pointer must be a string.");
        }
        var path = reader.GetString() ?? throw new JsonException("JSON Pointer must be a string.");
        return JsonPointer.TryParse(path, out var pointer)
          ? pointer
          : throw new JsonException($"Invalid JSON Pointer path '{path}'.");
    }

    public override void Write(Utf8JsonWriter writer, JsonPointer value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);
        writer.WriteStringValue(value.ToString());
    }
}
