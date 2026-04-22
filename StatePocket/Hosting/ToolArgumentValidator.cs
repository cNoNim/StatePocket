using System.Text.Json;
using StatePocket.Exceptions;

namespace StatePocket.Hosting;

internal static class ToolArgumentValidator
{
    public static void ValidateSetValueArguments(IDictionary<string, JsonElement>? arguments)
    {
        ValidateRequiredString(arguments, "key");
        ValidateRequiredString(arguments, "value");
        ValidateOptionalFormat(arguments);
        ValidateOptionalString(arguments, "namespace");
        ValidateOptionalInt64(arguments, "ttlSeconds");
        ValidateOptionalInt64(arguments, "expectedRevision");
        ValidateOptionalBoolean(arguments, "ifAbsent");
    }

    public static void ValidateGetValueArguments(IDictionary<string, JsonElement>? arguments)
    {
        ValidateRequiredString(arguments, "key");
        ValidateOptionalString(arguments, "namespace");
        ValidateOptionalString(arguments, "path");
    }

    public static void ValidateGetValuesArguments(IDictionary<string, JsonElement>? arguments)
    {
        ValidateRequiredStringArray(arguments, "keys");
        ValidateOptionalString(arguments, "namespace");
        ValidateOptionalString(arguments, "path");
    }

    public static void ValidateQueryValuesArguments(IDictionary<string, JsonElement>? arguments)
    {
        ValidateOptionalString(arguments, "namespace");
        ValidateOptionalString(arguments, "pattern");
        ValidateOptionalString(arguments, "query");
        ValidateOptionalNullableString(arguments, "equals");
        ValidateOptionalFormat(arguments);
        ValidateOptionalString(arguments, "path");
        ValidateOptionalInt32(arguments, "limit");
        ValidateOptionalString(arguments, "cursor");
    }

    public static void ValidateListNamespacesArguments(IDictionary<string, JsonElement>? arguments)
    {
        ValidateOptionalString(arguments, "pattern");
        ValidateOptionalInt32(arguments, "limit");
        ValidateOptionalString(arguments, "cursor");
    }

    public static void ValidateListKeysArguments(IDictionary<string, JsonElement>? arguments)
    {
        ValidateOptionalString(arguments, "namespace");
        ValidateOptionalString(arguments, "pattern");
        ValidateOptionalInt32(arguments, "limit");
        ValidateOptionalString(arguments, "cursor");
    }

    public static void ValidateDeleteValueArguments(IDictionary<string, JsonElement>? arguments)
    {
        ValidateRequiredString(arguments, "key");
        ValidateOptionalString(arguments, "namespace");
    }

    public static void ValidatePatchValueArguments(IDictionary<string, JsonElement>? arguments)
    {
        ValidateRequiredString(arguments, "key");
        ValidateRequiredString(arguments, "patch");
        ValidateOptionalString(arguments, "namespace");
    }

    private static JsonElement RequireDefinedArgument(IDictionary<string, JsonElement>? arguments, string argumentName)
    {
        if (arguments is not null
         && arguments.TryGetValue(argumentName, out var value)
         && value.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined)
        {
            return value;
        }
        throw new ToolInvalidArgumentException($"The '{argumentName}' argument is required.", argumentName);
    }

    private static void ValidateRequiredString(IDictionary<string, JsonElement>? arguments, string argumentName)
    {
        var value = RequireDefinedArgument(arguments, argumentName);
        if (value.ValueKind is not JsonValueKind.String)
        {
            throw new ToolInvalidArgumentException($"The '{argumentName}' argument must be a string.", argumentName);
        }
    }

    private static void ValidateRequiredStringArray(IDictionary<string, JsonElement>? arguments, string argumentName)
    {
        var value = RequireDefinedArgument(arguments, argumentName);
        if (value.ValueKind is not JsonValueKind.Array)
        {
            throw new ToolInvalidArgumentException(
                $"The '{argumentName}' argument must be an array of strings.",
                argumentName
            );
        }
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind is not JsonValueKind.String)
            {
                throw new ToolInvalidArgumentException(
                    $"The '{argumentName}' argument must be an array of strings.",
                    argumentName
                );
            }
        }
    }

    private static void ValidateOptionalString(IDictionary<string, JsonElement>? arguments, string argumentName)
    {
        if (TryGetDefinedArgument(arguments, argumentName, out var value)
         && value.ValueKind is not JsonValueKind.String)
        {
            throw new ToolInvalidArgumentException($"The '{argumentName}' argument must be a string.", argumentName);
        }
    }

    private static void ValidateOptionalNullableString(IDictionary<string, JsonElement>? arguments, string argumentName)
    {
        if (TryGetArgument(arguments, argumentName, out var value)
         && value.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined and not JsonValueKind.String)
        {
            throw new ToolInvalidArgumentException(
                $"The '{argumentName}' argument must be a string or null.",
                argumentName
            );
        }
    }

    private static void ValidateOptionalBoolean(IDictionary<string, JsonElement>? arguments, string argumentName)
    {
        if (TryGetDefinedArgument(arguments, argumentName, out var value)
         && value.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
        {
            throw new ToolInvalidArgumentException($"The '{argumentName}' argument must be a boolean.", argumentName);
        }
    }

    private static void ValidateOptionalInt32(IDictionary<string, JsonElement>? arguments, string argumentName)
    {
        if (TryGetDefinedArgument(arguments, argumentName, out var value)
         && (value.ValueKind is not JsonValueKind.Number || !value.TryGetInt32(out _)))
        {
            throw new ToolInvalidArgumentException($"The '{argumentName}' argument must be an integer.", argumentName);
        }
    }

    private static void ValidateOptionalInt64(IDictionary<string, JsonElement>? arguments, string argumentName)
    {
        if (TryGetDefinedArgument(arguments, argumentName, out var value)
         && (value.ValueKind is not JsonValueKind.Number || !value.TryGetInt64(out _)))
        {
            throw new ToolInvalidArgumentException($"The '{argumentName}' argument must be an integer.", argumentName);
        }
    }

    private static void ValidateOptionalFormat(IDictionary<string, JsonElement>? arguments)
    {
        if (!TryGetDefinedArgument(arguments, "format", out var value))
        {
            return;
        }
        if (value.ValueKind is not JsonValueKind.String)
        {
            throw new ToolInvalidArgumentException("format must be 'text' or 'json'.", "format");
        }
        var format = value.GetString();
        if (format is not "text" and not "json")
        {
            throw new ToolInvalidArgumentException("format must be 'text' or 'json'.", "format");
        }
    }

    private static bool TryGetDefinedArgument(
        IDictionary<string, JsonElement>? arguments,
        string argumentName,
        out JsonElement value
    )
    {
        return TryGetArgument(arguments, argumentName, out value)
            && value.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined;
    }

    private static bool TryGetArgument(
        IDictionary<string, JsonElement>? arguments,
        string argumentName,
        out JsonElement value
    )
    {
        if (arguments is not null
         && arguments.TryGetValue(argumentName, out value))
        {
            return true;
        }
        value = default;
        return false;
    }
}
