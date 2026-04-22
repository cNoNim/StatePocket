using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using StatePocket.Json.Patch;
using StatePocket.Json.Pointer;
using StatePocket.Serialization;

namespace StatePocket.Hosting;

internal static class McpToolFactory
{
    private static readonly JsonSerializerOptions SerializerOptions = CreateSerializerOptions();

    internal static McpServerTool Create(Delegate method, IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(method);
        return Create(method.Method, method.Target, services);
    }

    private static ToolErrorHandlingMcpServerTool Create(MethodInfo method, object? target, IServiceProvider services)
    {
        return new ToolErrorHandlingMcpServerTool(CreateRaw(method, target, services));
    }

    internal static McpServerTool CreateRaw(MethodInfo method, object? target, IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(services);
        return McpServerTool.Create(
            method,
            target,
            new McpServerToolCreateOptions
            {
                Services = services,
                SerializerOptions = SerializerOptions,
                SchemaCreateOptions = CreateSchemaCreateOptions()
            }
        );
    }

    private static JsonObject CreateAnyJsonSchema(JsonObject? existing)
    {
        return CloneObject(existing);
    }

    private static JsonObject CreatePatchItemSchema()
    {
        return new JsonObject
        {
            ["oneOf"] = new JsonArray(
                CreateOperationSchema("add", true, false),
                CreateOperationSchema("remove", false, false),
                CreateOperationSchema("replace", true, false),
                CreateOperationSchema("move", false, true),
                CreateOperationSchema("copy", false, true),
                CreateOperationSchema("test", true, false)
            )
        };
    }

    private static JsonObject CreateOperationSchema(string operation, bool requiresValue, bool requiresFrom)
    {
        return new JsonObject
        {
            ["type"] = JsonValue.Create("object"),
            ["properties"] = CreateJsonPatchOperationProperties(operation, requiresValue, requiresFrom),
            ["required"] = CreateRequiredProperties(requiresValue, requiresFrom)
        };
    }

    private static JsonObject CreateJsonPatchOperationProperties(
        string operation,
        bool requiresValue,
        bool requiresFrom
    )
    {
        var opPropertyName = ToCamelCase(nameof(JsonPatchOperation.Op));
        var pathPropertyName = ToCamelCase(nameof(JsonPatchOperation.Path));
        var valuePropertyName = ToCamelCase(nameof(ValueOperation.Value));
        var fromPropertyName = ToCamelCase(nameof(FromOperation.From));
        JsonObject properties = new()
        {
            [opPropertyName] = new JsonObject
            {
                ["type"] = JsonValue.Create("string"),
                ["const"] = JsonValue.Create(operation)
            },
            [pathPropertyName] = new JsonObject
            {
                ["type"] = JsonValue.Create("string")
            }
        };
        if (requiresValue)
        {
            properties[valuePropertyName] = CreateAnyJsonSchema(null);
        }
        if (requiresFrom)
        {
            properties[fromPropertyName] = new JsonObject
            {
                ["type"] = JsonValue.Create("string")
            };
        }
        return properties;
    }

    private static JsonArray CreateRequiredProperties(bool requiresValue, bool requiresFrom)
    {
        return (requiresValue, requiresFrom) switch
        {
            (true, false) => new JsonArray(
                JsonValue.Create(ToCamelCase(nameof(JsonPatchOperation.Op))),
                JsonValue.Create(ToCamelCase(nameof(JsonPatchOperation.Path))),
                JsonValue.Create(ToCamelCase(nameof(ValueOperation.Value)))
            ),
            (false, true) => new JsonArray(
                JsonValue.Create(ToCamelCase(nameof(JsonPatchOperation.Op))),
                JsonValue.Create(ToCamelCase(nameof(JsonPatchOperation.Path))),
                JsonValue.Create(ToCamelCase(nameof(FromOperation.From)))
            ),
            _ => new JsonArray(
                JsonValue.Create(ToCamelCase(nameof(JsonPatchOperation.Op))),
                JsonValue.Create(ToCamelCase(nameof(JsonPatchOperation.Path)))
            )
        };
    }

    private static string ToCamelCase(string value)
    {
        return string.IsNullOrEmpty(value)
          ? value
          : string.Create(
                value.Length,
                value,
                static (buffer, source) =>
                {
                    buffer[0] = char.ToLowerInvariant(source[0]);
                    source.AsSpan(1)
                          .CopyTo(buffer[1..]);
                }
            );
    }

    private static JsonNode TransformToolInputSchemaNode(AIJsonSchemaCreateContext context, JsonNode schema)
    {
        schema = JsonPointerSchemaExtensions.TransformJsonPointerSchema(context.TypeInfo.Type, schema);
        var schemaType = Nullable.GetUnderlyingType(context.TypeInfo.Type) ?? context.TypeInfo.Type;
        if (schemaType == typeof(JsonElement))
        {
            return CreateAnyJsonSchema(schema as JsonObject);
        }
        if (!context.Path.IsEmpty
         || schema is not JsonObject objectSchema)
        {
            return schema;
        }
        if (schemaType == typeof(JsonPatch))
        {
            return CreatePatchParameterSchema(objectSchema);
        }
        return schema;
    }

    private static JsonObject CreatePatchParameterSchema(JsonObject? existing)
    {
        var schema = CloneObject(existing);
        schema["type"] = JsonValue.Create("array");
        schema["items"] = CreatePatchItemSchema();
        return schema;
    }

    private static JsonObject CloneObject(JsonObject? schema)
    {
        return schema is null
          ? []
          : schema.DeepClone()
                  .AsObject();
    }

    private static AIJsonSchemaCreateOptions CreateSchemaCreateOptions()
    {
        return new AIJsonSchemaCreateOptions
        {
            TransformSchemaNode = static (context, schema) => TransformToolInputSchemaNode(context, schema)
        };
    }

    private static JsonSerializerOptions CreateSerializerOptions()
    {
        JsonSerializerOptions options = new(McpJsonUtilities.DefaultOptions)
        {
            AllowDuplicateProperties = false
        };
        options.TypeInfoResolverChain.Insert(0, ToolArgumentJsonContext.Default);
        options.TypeInfoResolverChain.Insert(0, ToolResultJsonContext.Default);
        options.TypeInfoResolverChain.Insert(0, JsonPatchJsonContext.Default);
        return options;
    }
}
