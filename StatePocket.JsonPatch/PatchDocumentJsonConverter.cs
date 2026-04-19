using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace StatePocket.JsonPatch;

internal sealed class PatchDocumentJsonConverter : JsonConverter<PatchDocument>
{
    private static readonly JsonSerializerOptions ReadOptions = CreateReadOptions();

    public override PatchDocument Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var operations = JsonSerializer.Deserialize<PatchOperation?[]>(ref reader, ReadOptions);
        if (operations is null)
        {
            throw new JsonException("Patch document must be a JSON array.");
        }
        foreach (var operation in operations)
        {
            if (operation is null)
            {
                throw new JsonException("Patch operation must be a JSON object.");
            }
        }
        var nonNullOperations = new PatchOperation[operations.Length];
        for (var i = 0; i < operations.Length; i++)
        {
            nonNullOperations[i] =
                operations[i] ?? throw new UnreachableException("Null patch operations are filtered above.");
        }
        return new PatchDocument(nonNullOperations);
    }

    public override void Write(Utf8JsonWriter writer, PatchDocument value, JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(writer, value.Operations, options);
    }

    private static JsonSerializerOptions CreateReadOptions()
    {
        JsonSerializerOptions options = new()
        {
            AllowDuplicateProperties = false,
            AllowOutOfOrderMetadataProperties = true
        };
        options.TypeInfoResolverChain.Insert(0, JsonPatchJsonContext.Default);
        return options;
    }
}
