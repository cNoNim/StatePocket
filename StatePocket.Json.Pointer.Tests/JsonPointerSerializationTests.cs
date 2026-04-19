using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace StatePocket.Json.Pointer.Tests;

public sealed partial class JsonPointerSerializationTests
{
    [Fact]
    public void Serialize_WritesJsonString()
    {
        var json = JsonSerializer.Serialize(JsonPointer.Parse("/foo/bar", null));
        Assert.Equal("\"/foo/bar\"", json);
    }

    [Fact]
    public void Deserialize_ReadsJsonString()
    {
        var pointer = JsonSerializer.Deserialize<JsonPointer>("\"/foo/bar\"");
        Assert.Equal("/foo/bar", pointer.ToString());
    }

    [Fact]
    public void SerializeAndDeserialize_EscapedJsonString_RoundTrips()
    {
        var pointer = JsonPointer.Parse("/foo\"bar\\baz", null);
        var json = JsonSerializer.Serialize(pointer);
        var roundTrip = JsonSerializer.Deserialize<JsonPointer>(json);
        Assert.Contains("""/foo""", json);
        Assert.Contains("""bar\\baz""", json);
        Assert.Equal(pointer, roundTrip);
    }

    [Fact]
    public void Deserialize_InvalidPointer_ThrowsJsonException()
    {
        var exception =
            Assert.Throws<JsonException>(static () => JsonSerializer.Deserialize<JsonPointer>("\"foo/bar\""));
        Assert.Contains("Invalid JSON Pointer path 'foo/bar'.", exception.Message);
        Assert.Null(exception.InnerException);
    }

    [Fact]
    public void SerializeAndDeserialize_PropertyType_UsesStringShape()
    {
        var value = new JsonPointerHolder
        {
            Path = JsonPointer.Parse("/foo/bar", null)
        };
        var json = JsonSerializer.Serialize(value);
        var roundTrip = JsonSerializer.Deserialize<JsonPointerHolder>(json);
        Assert.Equal("""{"Path":"/foo/bar"}""", json);
        Assert.Equal(JsonPointer.Parse("/foo/bar", null), roundTrip?.Path);
    }

    [Fact]
    public void SourceGeneratedSerialization_UsesStringShape()
    {
        var value = new JsonPointerHolder
        {
            Path = JsonPointer.Parse("/foo/bar", null)
        };
        var json = JsonSerializer.Serialize(value, JsonPointerTestJsonContext.Default.JsonPointerHolder);
        var roundTrip = JsonSerializer.Deserialize(json, JsonPointerTestJsonContext.Default.JsonPointerHolder);
        Assert.Equal("""{"Path":"/foo/bar"}""", json);
        var typed = Assert.IsType<JsonPointerHolder>(roundTrip);
        Assert.Equal(JsonPointer.Parse("/foo/bar", null), typed.Path);
    }

    [Fact]
    public void TypeConverter_ConvertsToAndFromString()
    {
        var converter = TypeDescriptor.GetConverter(typeof(JsonPointer));
        Assert.True(converter.CanConvertFrom(typeof(string)));
        Assert.True(converter.CanConvertTo(typeof(string)));
        var pointer = Assert.IsType<JsonPointer>(converter.ConvertFromInvariantString("/foo/bar"));
        Assert.Equal(JsonPointer.Parse("/foo/bar", null), pointer);
        Assert.Equal("/foo/bar", converter.ConvertToInvariantString(pointer));
    }

    [Fact]
    public void TypeConverter_InvalidString_ThrowsFormatException()
    {
        var converter = TypeDescriptor.GetConverter(typeof(JsonPointer));
        var exception = Assert.Throws<FormatException>(() => converter.ConvertFromInvariantString("foo/bar"));
        Assert.Equal("Invalid JSON Pointer path 'foo/bar'.", exception.Message);
    }

    public sealed class JsonPointerHolder
    {
        public required JsonPointer Path { get; init; }
    }

    [JsonSerializable(typeof(JsonPointer))]
    [JsonSerializable(typeof(JsonPointerHolder))]
    private sealed partial class JsonPointerTestJsonContext : JsonSerializerContext;
}
