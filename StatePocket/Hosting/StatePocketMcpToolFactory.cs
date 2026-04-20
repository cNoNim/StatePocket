using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using StatePocket.Contracts;
using StatePocket.Json.Patch;
using StatePocket.Json.Pointer;
using StatePocket.Tools;

namespace StatePocket.Hosting;

internal static class StatePocketMcpToolFactory
{
    private static readonly JsonSerializerOptions SerializerOptions = CreateSerializerOptions();

    internal static McpServerTool Create(Delegate method, IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(method);
        return Create(method.Method, method.Target, services);
    }

    internal static McpServerTool Create(MethodInfo method, object? target, IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(services);
        var tool = McpServerTool.Create(
            method,
            target,
            new McpServerToolCreateOptions
            {
                Services = services,
                SerializerOptions = SerializerOptions,
                SchemaCreateOptions = CreateSchemaCreateOptions()
            }
        );
        ApplyToolInputSchemaOverrides(tool, method);
        return tool;
    }

    private static JsonObject CreateAnyJsonSchema(JsonObject? existing)
    {
        var schema = CloneObject(existing);
        schema["type"] = new JsonArray(
            JsonValue.Create("object"),
            JsonValue.Create("array"),
            JsonValue.Create("string"),
            JsonValue.Create("number"),
            JsonValue.Create("boolean"),
            JsonValue.Create("null")
        );
        return schema;
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

    private static JsonObject CreateSetValueMutualExclusionSchema()
    {
        return new JsonObject
        {
            ["not"] = new JsonObject
            {
                ["required"] = new JsonArray(JsonValue.Create("expectedRevision"), JsonValue.Create("ifAbsent")),
                ["properties"] = new JsonObject
                {
                    ["expectedRevision"] = new JsonObject
                    {
                        ["not"] = new JsonObject
                        {
                            ["type"] = JsonValue.Create("null")
                        }
                    },
                    ["ifAbsent"] = new JsonObject
                    {
                        ["const"] = JsonValue.Create(true)
                    }
                }
            }
        };
    }

    private static JsonObject CreateQueryValuesEqualsRequiresQuerySchema()
    {
        return new JsonObject
        {
            ["if"] = new JsonObject
            {
                ["required"] = new JsonArray(JsonValue.Create("equals"))
            },
            ["then"] = new JsonObject
            {
                ["required"] = new JsonArray(JsonValue.Create("query")),
                ["properties"] = new JsonObject
                {
                    ["query"] = new JsonObject
                    {
                        ["not"] = new JsonObject
                        {
                            ["type"] = JsonValue.Create("null")
                        }
                    }
                }
            }
        };
    }

    private static JsonObject ApplyToolInputRootSchemaOverrides(JsonObject objectSchema, string toolName)
    {
        List<JsonNode> constraints = [];
        if (string.Equals(toolName, SetValueTool.ToolName, StringComparison.Ordinal))
        {
            constraints.Add(CreateSetValueMutualExclusionSchema());
        }
        if (string.Equals(toolName, QueryValuesTool.ToolName, StringComparison.Ordinal))
        {
            constraints.Add(CreateQueryValuesEqualsRequiresQuerySchema());
        }
        if (constraints.Count == 0)
        {
            return objectSchema;
        }
        var transformedSchema = CloneObject(objectSchema);
        var allOf = transformedSchema["allOf"] as JsonArray ?? [];
        foreach (var constraint in constraints)
        {
            allOf.Add(constraint);
        }
        transformedSchema["allOf"] = allOf;
        return transformedSchema;
    }

    private static void ApplyToolInputSchemaOverrides(McpServerTool tool, MethodInfo method)
    {
        var toolName = method.GetCustomAttribute<McpServerToolAttribute>()
                            ?.Name
                    ?? method.Name;
        var schemaNode = JsonNode.Parse(tool.ProtocolTool.InputSchema.GetRawText());
        if (schemaNode is not JsonObject objectSchema)
        {
            return;
        }
        var transformedSchema = ApplyToolInputRootSchemaOverrides(objectSchema, toolName);
        using var document = JsonDocument.Parse(transformedSchema.ToJsonString());
        tool.ProtocolTool.InputSchema = document.RootElement.Clone();
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
        options.TypeInfoResolverChain.Insert(0, ToolResultJsonContext.Default);
        options.TypeInfoResolverChain.Insert(0, JsonPatchJsonContext.Default);
        return options;
    }
}
