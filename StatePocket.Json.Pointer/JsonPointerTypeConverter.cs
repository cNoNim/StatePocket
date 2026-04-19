using System.ComponentModel;
using System.Globalization;

namespace StatePocket.Json.Pointer;

public sealed class JsonPointerTypeConverter : TypeConverter
{
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType)
    {
        return sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);
    }

    public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType)
    {
        return destinationType == typeof(string) || base.CanConvertTo(context, destinationType);
    }

    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
    {
        if (value is not string stringValue)
        {
            return base.ConvertFrom(context, culture, value);
        }
        return JsonPointer.TryParse(stringValue, out var pointer)
          ? pointer
          : throw new FormatException($"Invalid JSON Pointer path '{stringValue}'.");
    }

    public override object? ConvertTo(
        ITypeDescriptorContext? context,
        CultureInfo? culture,
        object? value,
        Type destinationType
    )
    {
        if (destinationType == typeof(string)
         && value is JsonPointer pointer)
        {
            return pointer.ToString();
        }
        return base.ConvertTo(
            context,
            culture,
            value,
            destinationType
        );
    }
}
