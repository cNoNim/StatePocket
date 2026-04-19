using System.Text.Json.Nodes;
using System.Text.Json.Schema;

namespace StatePocket.Json.Pointer;

public static class JsonPointerSchemaExtensions
{
    public static JsonSchemaExporterOptions UseJsonPointerSchema(this JsonSchemaExporterOptions? options)
    {
        options ??= new JsonSchemaExporterOptions();
        var existingTransform = options.TransformSchemaNode;
        return new JsonSchemaExporterOptions
        {
            TreatNullObliviousAsNonNullable = options.TreatNullObliviousAsNonNullable,
            TransformSchemaNode = existingTransform is null
              ? TransformSchemaNode
              : (context, schema) => TransformSchemaNode(context, existingTransform(context, schema))
        };
    }

    public static JsonNode TransformJsonPointerSchema(Type type, JsonNode schema)
    {
        return type == typeof(JsonPointer) ? CreateJsonPointerSchema(schema) : schema;
    }

    private static JsonNode TransformSchemaNode(JsonSchemaExporterContext context, JsonNode schema)
    {
        return TransformJsonPointerSchema(context.TypeInfo.Type, schema);
    }

    private static JsonObject CreateJsonPointerSchema(JsonNode schema)
    {
        var result = schema is not JsonObject objectSchema
          ? []
          : objectSchema.DeepClone()
                        .AsObject();
        result["type"] = JsonValue.Create("string");
        return result;
    }
}
