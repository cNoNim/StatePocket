using System.Buffers;
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
        if (!reader.HasValueSequence
         && !reader.ValueIsEscaped)
        {
            if (JsonPointer.TryParse(reader.ValueSpan, null, out var pointer))
            {
                return pointer;
            }
        }
        else
        {
            var maxByteCount = reader.HasValueSequence
              ? checked((int)reader.ValueSequence.Length)
              : reader.ValueSpan.Length;
            byte[]? rented = null;
            var buffer = maxByteCount <= 256
              ? stackalloc byte[maxByteCount]
              : rented = ArrayPool<byte>.Shared.Rent(maxByteCount);
            try
            {
                var bytesWritten = reader.CopyString(buffer);
                if (JsonPointer.TryParse(buffer[..bytesWritten], null, out var pointer))
                {
                    return pointer;
                }
            }
            finally
            {
                if (rented is not null)
                {
                    ArrayPool<byte>.Shared.Return(rented);
                }
            }
        }
        var path = reader.GetString() ?? throw new JsonException("JSON Pointer must be a string.");
        throw new JsonException($"Invalid JSON Pointer path '{path}'.");
    }

    public override void Write(Utf8JsonWriter writer, JsonPointer value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        Span<byte> initialBuffer = stackalloc byte[256];
        if (value.TryFormat(
                initialBuffer,
                out var initialBytesWritten,
                default,
                null
            ))
        {
            writer.WriteStringValue(initialBuffer[..initialBytesWritten]);
            return;
        }
        byte[]? rented = null;
        try
        {
            var bufferSize = initialBuffer.Length * 2;
            while (true)
            {
                rented = ArrayPool<byte>.Shared.Rent(bufferSize);
                if (value.TryFormat(
                        rented,
                        out var bytesWritten,
                        default,
                        null
                    ))
                {
                    writer.WriteStringValue(rented.AsSpan(0, bytesWritten));
                    return;
                }
                ArrayPool<byte>.Shared.Return(rented);
                rented = null;
                bufferSize *= 2;
            }
        }
        finally
        {
            if (rented is not null)
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }
    }
}
