using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization.Metadata;

namespace StatePocket.Json.Pointer.Tests;

public sealed class JsonPointerSchemaTests
{
    [Fact]
    public void JsonSchemaExporter_MapsJsonPointerToString()
    {
        JsonSerializerOptions serializerOptions = new()
        {
            TypeInfoResolver = new DefaultJsonTypeInfoResolver()
        };
        var schema = serializerOptions.GetJsonSchemaAsNode(
            typeof(PointerContainer),
            new JsonSchemaExporterOptions().UseJsonPointerSchema()
        );
        Assert.NotNull(schema);
        Assert.Equal(
            "string",
            schema["properties"]?[nameof(PointerContainer.Path)]?["type"]
              ?.GetValue<string>()
        );
    }

    [Fact]
    public void TransformJsonPointerSchema_MapsJsonPointerToString()
    {
        var transformed = JsonPointerSchemaExtensions.TransformJsonPointerSchema(
            typeof(JsonPointer),
            new JsonObject
            {
                ["description"] = JsonValue.Create("custom")
            }
        );
        Assert.Equal(
            "string",
            transformed["type"]
              ?.GetValue<string>()
        );
        Assert.Equal(
            "custom",
            transformed["description"]
              ?.GetValue<string>()
        );
    }

    private sealed class PointerContainer
    {
        public required JsonPointer Path { get; init; }
    }
}
