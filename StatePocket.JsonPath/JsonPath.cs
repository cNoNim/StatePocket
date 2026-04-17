using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace StatePocket.JsonPath;

public sealed partial class JsonPath
{
    private readonly SelectorSegment[] _segments;
    private readonly string _selector;

    public JsonPath(string selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        _selector = selector;
        _segments = ParseSegments(selector);
    }

    public IReadOnlyList<JsonPathMatch> Evaluate(JsonElement document)
    {
        var currentValues = EvaluateSegments(
            document,
            document,
            "$",
            _segments
        );
        return [.. currentValues.Select(static value => new JsonPathMatch(value.Value.Clone(), value.NormalizedPath))];
    }

    public override string ToString()
    {
        return _selector;
    }

    public static bool DeepEquals(JsonElement left, JsonElement right)
    {
        return JsonElementEqualityComparer.Equals(left, right);
    }

    private static SelectorSegment[] ParseSegments(string selector)
    {
        if (selector.Length == 0
         || selector[0] != '$')
        {
            throw new JsonPathException($"Invalid JSONPath selector '{selector}'.");
        }
        return ParseSegmentsCore(selector);
    }

    private static SelectorSegment[] ParseFilterQuerySegments(string selector)
    {
        if (selector.Length == 0
         || (selector[0] != '$' && selector[0] != '@'))
        {
            throw new JsonPathException($"Invalid JSONPath selector '{selector}'.");
        }
        return ParseSegmentsCore(selector);
    }

    private static SelectorSegment[] ParseSegmentsCore(string selector)
    {
        var segments = new List<SelectorSegment>();
        for (var index = 1; index < selector.Length;)
        {
            var start = index;
            while (index < selector.Length
                && selector[index] is ' ' or '\t' or '\n' or '\r')
            {
                index++;
            }
            if (index >= selector.Length)
            {
                if (start != index)
                {
                    throw new JsonPathException($"Invalid JSONPath selector '{selector}'.");
                }
                break;
            }
            index = selector[index] switch
            {
                '.' when index + 1 < selector.Length && selector[index + 1] == '.' => ParseDescendantSegment(
                    selector,
                    index,
                    segments
                ),
                '.' => ParseDotSegment(selector, index, segments),
                '[' => ParseBracketSegment(selector, index, segments),
                _ => throw new JsonPathException($"Invalid JSONPath selector '{selector}'.")
            };
        }
        return [.. segments];
    }

    private static int ParseDotSegment(string selector, int index, ICollection<SelectorSegment> segments)
    {
        index++;
        if (index >= selector.Length)
        {
            throw new JsonPathException($"Invalid JSONPath selector '{selector}'.");
        }
        switch (selector[index])
        {
            case '*':
                segments.Add(new WildcardSegment());
                return index + 1;
            case var _ when !IsIdentifierStart(selector[index]):
                throw new JsonPathException($"Invalid JSONPath selector '{selector}'.");
        }
        var start = index;
        index++;
        while (index < selector.Length
            && IsIdentifierPart(selector[index]))
        {
            index++;
        }
        segments.Add(new NameSegment(selector[start..index]));
        return index;
    }

    private static int ParseDescendantSegment(string selector, int index, ICollection<SelectorSegment> segments)
    {
        index += 2;
        if (index >= selector.Length)
        {
            throw new JsonPathException($"Invalid JSONPath selector '{selector}'.");
        }
        SelectorSegment innerSegment;
        switch (selector[index])
        {
            case '*':
                innerSegment = new WildcardSegment();
                index++;
                break;
            case '[':
                var closeIndex = FindClosingBracket(selector, index);
                innerSegment = ParseBracketContent(selector[(index + 1)..closeIndex], selector);
                index = closeIndex + 1;
                break;
            case var _ when IsIdentifierStart(selector[index]):
                var start = index;
                index++;
                while (index < selector.Length
                    && IsIdentifierPart(selector[index]))
                {
                    index++;
                }
                innerSegment = new NameSegment(selector[start..index]);
                break;
            default:
                throw new JsonPathException($"Invalid JSONPath selector '{selector}'.");
        }
        segments.Add(new DescendantSegment(innerSegment));
        return index;
    }

    private static int ParseBracketSegment(string selector, int index, ICollection<SelectorSegment> segments)
    {
        var closeIndex = FindClosingBracket(selector, index);
        segments.Add(ParseBracketContent(selector[(index + 1)..closeIndex], selector));
        return closeIndex + 1;
    }

    private static SelectorSegment ParseBracketContent(string content, string selector)
    {
        if (content.Length == 0)
        {
            throw new JsonPathException($"Invalid JSONPath selector '{selector}'.");
        }
        var selectorParts = SplitSelectorList(content)
                           .Select(TrimWhitespace)
                           .ToArray();
        if (selectorParts.Any(static part => part.Length == 0))
        {
            throw new JsonPathException($"Invalid JSONPath selector '{selector}'.");
        }
        var parsedSelectors = selectorParts.Select(part => ParseBracketSelector(part, selector))
                                           .ToArray();
        return parsedSelectors.Length == 1 ? parsedSelectors[0] : new UnionSegment(parsedSelectors);
    }

    private static SelectorSegment ParseBracketSelector(string selectorPart, string selector)
    {
        return selectorPart[0] switch
        {
            '?' => ParseFilterSegment(selectorPart, selector),
            '*' when selectorPart.Length == 1 => new WildcardSegment(),
            '"' or '\'' => ParseQuotedNameSegment(selectorPart, selector),
            _ when selectorPart.Contains(':', StringComparison.Ordinal) => ParseSliceSegment(selectorPart, selector),
            _ => ParseIndexSegment(selectorPart, selector)
        };
    }

    private static FilterSegment ParseFilterSegment(string content, string selector)
    {
        var expression = content[1..];
        return expression.Length != 0
          ? new FilterSegment(new FilterExpressionParser(expression, selector).Parse())
          : throw new JsonPathException($"Invalid JSONPath selector '{selector}'.");
    }

    private static PathQuery ParseFilterQuery(string selectorPart, string selector)
    {
        var normalizedSelectorPart = TrimWhitespace(selectorPart);
        if (normalizedSelectorPart.Length == 0)
        {
            throw new JsonPathException($"Invalid JSONPath selector '{selector}'.");
        }
        return normalizedSelectorPart[0] switch
        {
            '$' or '@' => new PathQuery(normalizedSelectorPart[0], ParseFilterQuerySegments(normalizedSelectorPart)),
            _ => throw new JsonPathException($"Invalid JSONPath selector '{selector}'.")
        };
    }

    private static int FindComparisonOperator(string expression)
    {
        var inString = false;
        var quote = '\0';
        var nestedBracketCount = 0;
        var nestedParenthesisCount = 0;
        for (var index = 0; index < expression.Length - 1; index++)
        {
            var current = expression[index];
            if (inString)
            {
                if (current == '\\')
                {
                    index++;
                    continue;
                }
                if (current == quote)
                {
                    inString = false;
                }
                continue;
            }
            switch (current)
            {
                case '"' or '\'':
                    inString = true;
                    quote = current;
                    continue;
                case '[':
                    nestedBracketCount++;
                    continue;
                case ']':
                    nestedBracketCount--;
                    continue;
                case '(':
                    nestedParenthesisCount++;
                    continue;
                case ')':
                    nestedParenthesisCount--;
                    continue;
            }
            if (nestedBracketCount == 0
             && nestedParenthesisCount == 0)
            {
                switch (current)
                {
                    case '=' or '!' when expression[index + 1] == '=':
                    case '<' or '>':
                        return index;
                }
            }
        }
        return -1;
    }

    private static FilterOperand ParseFilterOperand(string content, string selector)
    {
        if (content.Length == 0)
        {
            throw new JsonPathException($"Invalid JSONPath selector '{selector}'.");
        }
        return content[0] switch
        {
            '@' or '$' => new QueryOperand(ParseFilterQuery(content, selector)),
            '"' or '\'' => new LiteralOperand(JsonValue(content, selector)),
            't' when content == "true" => new LiteralOperand(
                JsonDocument.Parse("[true]")
                            .RootElement[0]
                            .Clone()
            ),
            'f' when content == "false" => new LiteralOperand(
                JsonDocument.Parse("[false]")
                            .RootElement[0]
                            .Clone()
            ),
            'n' when content == "null" => new LiteralOperand(
                JsonDocument.Parse("[null]")
                            .RootElement[0]
                            .Clone()
            ),
            _ when TryParseFunctionOperand(content, selector, out var operand) => operand,
            '-' or >= '0' and <= '9' => new LiteralOperand(ParseJsonLiteral(content, selector)),
            _ => throw new JsonPathException($"Invalid JSONPath selector '{selector}'.")
        };
    }

    private static bool TryParseFunctionOperand(
        string content,
        string selector,
        [NotNullWhen(true)] out FilterOperand? operand
    )
    {
        operand = null;
        var openParenthesisIndex = content.IndexOf('(');
        if (openParenthesisIndex <= 0
         || content[^1] != ')')
        {
            return false;
        }
        var name = content[..openParenthesisIndex];
        if (name.Any(static character => !char.IsAsciiLetter(character)))
        {
            return false;
        }
        var arguments = SplitFunctionArguments(content[(openParenthesisIndex + 1)..^1]);
        operand = name switch
        {
            "count" => ParseCountOperand(arguments, selector),
            "length" => ParseLengthOperand(arguments, selector),
            "value" => ParseValueOperand(arguments, selector),
            _ => throw new JsonPathException($"Invalid JSONPath selector '{selector}'.")
        };
        return true;
    }

    private static CountOperand ParseCountOperand(string[] arguments, string selector)
    {
        return arguments.Length == 1
            && ParseFilterOperand(TrimWhitespace(arguments[0]), selector) is QueryOperand queryOperand
          ? new CountOperand(queryOperand.Query)
          : throw new JsonPathException($"Invalid JSONPath selector '{selector}'.");
    }

    private static LengthOperand ParseLengthOperand(string[] arguments, string selector)
    {
        if (arguments.Length != 1)
        {
            throw new JsonPathException($"Invalid JSONPath selector '{selector}'.");
        }
        var argument = ParseFilterOperand(TrimWhitespace(arguments[0]), selector);
        return argument is QueryOperand
        {
            Query.IsSingular: false
        }
          ? throw new JsonPathException($"Invalid JSONPath selector '{selector}'.")
          : new LengthOperand(argument);
    }

    private static ValueOperand ParseValueOperand(string[] arguments, string selector)
    {
        return arguments.Length == 1
            && ParseFilterOperand(TrimWhitespace(arguments[0]), selector) is QueryOperand queryOperand
          ? new ValueOperand(queryOperand.Query)
          : throw new JsonPathException($"Invalid JSONPath selector '{selector}'.");
    }

    private static bool TryParseFunctionExpression(
        string content,
        string selector,
        [NotNullWhen(true)] out FilterExpression? expression
    )
    {
        expression = null;
        var openParenthesisIndex = content.IndexOf('(');
        if (openParenthesisIndex <= 0
         || content[^1] != ')')
        {
            return false;
        }
        var name = content[..openParenthesisIndex];
        if (name.Any(static character => !char.IsAsciiLetter(character)))
        {
            return false;
        }
        var arguments = SplitFunctionArguments(content[(openParenthesisIndex + 1)..^1]);
        expression = name switch
        {
            "match" => ParseRegexFunctionExpression(arguments, selector, true),
            "search" => ParseRegexFunctionExpression(arguments, selector, false),
            _ => null
        };
        return expression is not null;
    }

    private static RegexMatchExpression ParseRegexFunctionExpression(
        string[] arguments,
        string selector,
        bool requireFullMatch
    )
    {
        if (arguments.Length != 2)
        {
            throw new JsonPathException($"Invalid JSONPath selector '{selector}'.");
        }
        return new RegexMatchExpression(
            ParseFilterOperand(TrimWhitespace(arguments[0]), selector),
            ParseFilterOperand(TrimWhitespace(arguments[1]), selector),
            requireFullMatch,
            selector
        );
    }

    private static JsonElement JsonValue(string content, string selector)
    {
        using var document =
            JsonDocument.Parse($$"""[{{ToJsonStringLiteral(ParseQuotedString(content, selector))}}]""");
        return document.RootElement[0]
                       .Clone();
    }

    private static JsonElement ParseJsonLiteral(string content, string selector)
    {
        try
        {
            using var document = JsonDocument.Parse($"[{content}]");
            return document.RootElement[0]
                           .Clone();
        }
        catch (JsonException)
        {
            throw new JsonPathException($"Invalid JSONPath selector '{selector}'.");
        }
    }

    private static string ParseQuotedString(string content, string selector)
    {
        var index = 1;
        var quote = content[0];
        var builder = new StringBuilder();
        while (index < content.Length)
        {
            var current = content[index];
            switch (current)
            {
                case var _ when current == quote:
                    return index == content.Length - 1
                      ? builder.ToString()
                      : throw new JsonPathException($"Invalid JSONPath selector '{selector}'.");
                case '\\':
                    index++;
                    builder.Append(
                        index < content.Length
                          ? ParseEscape(
                                content,
                                ref index,
                                selector,
                                quote
                            )
                          : throw new JsonPathException($"Invalid JSONPath selector '{selector}'.")
                    );
                    continue;
            }
            if (current < ' ')
            {
                throw new JsonPathException($"Invalid JSONPath selector '{selector}'.");
            }
            if (char.IsSurrogate(current))
            {
                if (!char.IsHighSurrogate(current)
                 || index + 1 >= content.Length
                 || !char.IsLowSurrogate(content[index + 1]))
                {
                    throw new JsonPathException($"Invalid JSONPath selector '{selector}'.");
                }
                builder.Append(current);
                builder.Append(content[index + 1]);
                index += 2;
                continue;
            }
            builder.Append(current);
            index++;
        }
        throw new JsonPathException($"Invalid JSONPath selector '{selector}'.");
    }

    private static string ToJsonStringLiteral(string value)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStringValue(value);
        }
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static JsonElement ToJsonInt32Element(int value)
    {
        using var document = JsonDocument.Parse(value.ToString(CultureInfo.InvariantCulture));
        return document.RootElement.Clone();
    }

    private static int FindClosingBracket(string selector, int openIndex)
    {
        var inString = false;
        var nestedBracketCount = 0;
        var quote = '\0';
        for (var index = openIndex + 1; index < selector.Length; index++)
        {
            var current = selector[index];
            if (inString)
            {
                if (current == '\\')
                {
                    index++;
                    continue;
                }
                if (current == quote)
                {
                    inString = false;
                }
                continue;
            }
            switch (current)
            {
                case '"' or '\'':
                    inString = true;
                    quote = current;
                    break;
                case '[':
                    nestedBracketCount++;
                    break;
                case ']':
                    if (nestedBracketCount == 0)
                    {
                        return index;
                    }
                    nestedBracketCount--;
                    break;
            }
        }
        throw new JsonPathException($"Invalid JSONPath selector '{selector}'.");
    }

    private static string[] SplitSelectorList(string content)
    {
        List<string> parts = [];
        var inString = false;
        var nestedBracketCount = 0;
        var nestedParenthesisCount = 0;
        var quote = '\0';
        var start = 0;
        for (var index = 0; index < content.Length; index++)
        {
            var current = content[index];
            if (inString)
            {
                if (current == '\\')
                {
                    index++;
                    continue;
                }
                if (current == quote)
                {
                    inString = false;
                }
                continue;
            }
            switch (current)
            {
                case '"' or '\'':
                    inString = true;
                    quote = current;
                    break;
                case '[':
                    nestedBracketCount++;
                    break;
                case ']':
                    nestedBracketCount--;
                    break;
                case '(':
                    nestedParenthesisCount++;
                    break;
                case ')':
                    nestedParenthesisCount--;
                    break;
                case ',':
                    if (nestedBracketCount == 0
                     && nestedParenthesisCount == 0)
                    {
                        parts.Add(content[start..index]);
                        start = index + 1;
                    }
                    break;
            }
        }
        parts.Add(content[start..]);
        return [.. parts];
    }

    private static string[] SplitFunctionArguments(string content)
    {
        if (content.Length == 0)
        {
            return [];
        }
        List<string> parts = [];
        var inString = false;
        var nestedBracketCount = 0;
        var nestedParenthesisCount = 0;
        var quote = '\0';
        var start = 0;
        for (var index = 0; index < content.Length; index++)
        {
            var current = content[index];
            if (inString)
            {
                if (current == '\\')
                {
                    index++;
                    continue;
                }
                if (current == quote)
                {
                    inString = false;
                }
                continue;
            }
            switch (current)
            {
                case '"' or '\'':
                    inString = true;
                    quote = current;
                    break;
                case '[':
                    nestedBracketCount++;
                    break;
                case ']':
                    nestedBracketCount--;
                    break;
                case '(':
                    nestedParenthesisCount++;
                    break;
                case ')':
                    nestedParenthesisCount--;
                    break;
                case ',' when nestedBracketCount == 0 && nestedParenthesisCount == 0:
                    parts.Add(content[start..index]);
                    start = index + 1;
                    break;
            }
        }
        parts.Add(content[start..]);
        return [.. parts];
    }

    private static NameSegment ParseQuotedNameSegment(string content, string selector)
    {
        return new NameSegment(ParseQuotedString(content, selector));
    }

    private static string ParseEscape(
        string content,
        ref int index,
        string selector,
        char quote
    )
    {
        var current = content[index];
        index++;
        return current switch
        {
            '"' when quote == '"' => "\"",
            '\'' when quote == '\'' => "'",
            '\\' => "\\",
            '/' => "/",
            'b' => "\b",
            'f' => "\f",
            'n' => "\n",
            'r' => "\r",
            't' => "\t",
            'u' => ParseUnicodeEscape(content, ref index, selector),
            _ => throw new JsonPathException($"Invalid JSONPath selector '{selector}'.")
        };
    }

    private static string ParseUnicodeEscape(string content, ref int index, string selector)
    {
        var codeUnit = ParseUnicodeCodeUnit(content, ref index, selector);
        var character = (char)codeUnit;
        if (char.IsLowSurrogate(character))
        {
            throw new JsonPathException($"Invalid JSONPath selector '{selector}'.");
        }
        if (!char.IsHighSurrogate(character))
        {
            return character.ToString();
        }
        if (index + 6 > content.Length
         || content[index] != '\\'
         || content[index + 1] != 'u')
        {
            throw new JsonPathException($"Invalid JSONPath selector '{selector}'.");
        }
        index += 2;
        var lowCodeUnit = ParseUnicodeCodeUnit(content, ref index, selector);
        return char.IsLowSurrogate((char)lowCodeUnit)
          ? char.ConvertFromUtf32(char.ConvertToUtf32((char)codeUnit, (char)lowCodeUnit))
          : throw new JsonPathException($"Invalid JSONPath selector '{selector}'.");
    }

    private static ushort ParseUnicodeCodeUnit(string content, ref int index, string selector)
    {
        if (index + 4 > content.Length)
        {
            throw new JsonPathException($"Invalid JSONPath selector '{selector}'.");
        }
        var hex = content.Substring(index, 4);
        if (!ushort.TryParse(
                hex,
                NumberStyles.AllowHexSpecifier,
                CultureInfo.InvariantCulture,
                out var codeUnit
            ))
        {
            throw new JsonPathException($"Invalid JSONPath selector '{selector}'.");
        }
        index += 4;
        return codeUnit;
    }

    private static IndexSegment ParseIndexSegment(string content, string selector)
    {
        return new IndexSegment(ParseIntegerLiteral(content, selector, true));
    }

    private static SliceSegment ParseSliceSegment(string content, string selector)
    {
        var parts = content.Split(':');
        if (parts.Length is < 2 or > 3)
        {
            throw new JsonPathException($"Invalid JSONPath selector '{selector}'.");
        }
        var start = ParseOptionalIntegerLiteral(TrimWhitespace(parts[0]), selector);
        var end = ParseOptionalIntegerLiteral(TrimWhitespace(parts[1]), selector);
        var step = parts.Length == 3 ? ParseOptionalIntegerLiteral(TrimWhitespace(parts[2]), selector) : null;
        return new SliceSegment(start, end, step);
    }

    private static long ParseIntegerLiteral(string content, string selector, bool allowZero)
    {
        if (content.Length == 0
         || content[0] == '+')
        {
            throw new JsonPathException($"Invalid JSONPath selector '{selector}'.");
        }
        var start = 0;
        switch (content[0])
        {
            case '-':
                start = 1;
                if (content.Length == 1
                 || content[start] == '0')
                {
                    throw new JsonPathException($"Invalid JSONPath selector '{selector}'.");
                }
                break;
            case '0':
                if (!allowZero)
                {
                    throw new JsonPathException($"Invalid JSONPath selector '{selector}'.");
                }
                if (content.Length > 1)
                {
                    throw new JsonPathException($"Invalid JSONPath selector '{selector}'.");
                }
                break;
            case var _ when !char.IsAsciiDigit(content[0]):
                throw new JsonPathException($"Invalid JSONPath selector '{selector}'.");
        }
        for (var index = start; index < content.Length; index++)
        {
            if (!char.IsAsciiDigit(content[index]))
            {
                throw new JsonPathException($"Invalid JSONPath selector '{selector}'.");
            }
        }
        if (!long.TryParse(
                content,
                NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out var parsedIndex
            ))
        {
            throw new JsonPathException($"Invalid JSONPath selector '{selector}'.");
        }
        return parsedIndex is >= -9007199254740991L and <= 9007199254740991L
          ? parsedIndex
          : throw new JsonPathException($"Invalid JSONPath selector '{selector}'.");
    }

    private static long? ParseOptionalIntegerLiteral(string content, string selector)
    {
        return content.Length == 0 ? null : ParseIntegerLiteral(content, selector, true);
    }

    private static IEnumerable<long> EvaluateSlice(
        long? start,
        long? end,
        long? step,
        int length
    )
    {
        var resolvedStep = step ?? 1;
        switch (Math.Sign(resolvedStep))
        {
            case 0:
                yield break;
            case 1:
                var lower = start.HasValue ? NormalizeSliceBound(start.Value, length, true) : 0;
                var upper = end.HasValue ? NormalizeSliceBound(end.Value, length, true) : length;
                for (var index = lower; index < upper; index += resolvedStep)
                {
                    yield return index;
                }
                yield break;
            default:
                var descendingLower = start.HasValue ? NormalizeSliceBound(start.Value, length, false) : length - 1;
                var descendingUpper = end.HasValue ? NormalizeSliceBound(end.Value, length, false) : -1;
                for (var index = descendingLower; index > descendingUpper; index += resolvedStep)
                {
                    yield return index;
                }
                yield break;
        }
    }

    private static long NormalizeSliceBound(long value, int length, bool positiveStep)
    {
        var normalized = value < 0 ? value + length : value;
        return positiveStep switch
        {
            true when normalized < 0 => 0,
            true when normalized > length => length,
            true => normalized,
            false when normalized < 0 => -1,
            false when normalized >= length => length - 1,
            false => normalized
        };
    }

    private static bool IsIdentifierStart(char character)
    {
        return character == '_' || char.IsLetter(character) || character > 127;
    }

    private static string TrimWhitespace(string value)
    {
        return value.Trim(
            ' ',
            '\t',
            '\n',
            '\r'
        );
    }

    private static bool IsIdentifierPart(char character)
    {
        return character == '_' || char.IsLetterOrDigit(character) || character > 127;
    }

    private static string NormalizeName(string name)
    {
        var builder = new StringBuilder();
        foreach (var character in name)
        {
            if (character < ' ')
            {
                builder.Append(
                    character switch
                    {
                        '\b' => "\\b",
                        '\f' => "\\f",
                        '\n' => "\\n",
                        '\r' => "\\r",
                        '\t' => "\\t",
                        _ => $@"\u{((int)character).ToString("x4", CultureInfo.InvariantCulture)}"
                    }
                );
                continue;
            }
            builder.Append(
                character switch
                {
                    '\\' => "\\\\",
                    '\'' => "\\'",
                    _ => character.ToString()
                }
            );
        }
        return $"['{builder}']";
    }

    private static bool TryGetSingularValue(FilterValue value, out JsonElement result)
    {
        if (value is {
                         Kind: FilterValueKind.Json
                     }
                   or
                     {
                         Kind: FilterValueKind.NodeList,
                         Values.Length: 1
                     })
        {
            result = value.Values[0];
            return true;
        }
        result = default;
        return false;
    }

    private static bool TryGetSingularString(FilterValue value, [NotNullWhen(true)] out string? result)
    {
        if (TryGetSingularValue(value, out var singularValue)
         && singularValue.ValueKind == JsonValueKind.String
         && singularValue.GetString() is
                {} stringValue)
        {
            result = stringValue;
            return true;
        }
        result = null;
        return false;
    }

    private static bool TryGetConstantString(FilterOperand operand, [NotNullWhen(true)] out string? result)
    {
        if (operand is LiteralOperand
            {
                Value.ValueKind: JsonValueKind.String
            } literalOperand
         && literalOperand.Value.GetString() is
                {} stringValue)
        {
            result = stringValue;
            return true;
        }
        result = null;
        return false;
    }

    private static Regex? TryCreateConstantRegex(FilterOperand pattern, bool requireFullMatch, string selector)
    {
        return TryGetConstantString(pattern, out var constantPattern)
          ? CreateRegex(constantPattern, requireFullMatch, selector)
          : null;
    }

    private static Regex CreateRegex(string pattern, bool requireFullMatch, string selector)
    {
        var normalizedPattern = NormalizeRegexPattern(pattern);
        var effectivePattern = requireFullMatch ? $@"\A(?:{normalizedPattern})\z" : normalizedPattern;
        try
        {
            return new Regex(effectivePattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        }
        catch (ArgumentException)
        {
            throw new JsonPathException($"Invalid JSONPath selector '{selector}'.");
        }
    }

    private static string NormalizeRegexPattern(string pattern)
    {
        var builder = new StringBuilder(pattern.Length);
        var inCharacterClass = false;
        for (var index = 0; index < pattern.Length; index++)
        {
            var current = pattern[index];
            if (current == '\\')
            {
                builder.Append(current);
                if (index + 1 < pattern.Length)
                {
                    builder.Append(pattern[index + 1]);
                    index++;
                }
                continue;
            }
            switch (current)
            {
                case '[' when !inCharacterClass:
                    inCharacterClass = true;
                    builder.Append(current);
                    continue;
                case ']' when inCharacterClass:
                    inCharacterClass = false;
                    builder.Append(current);
                    continue;
                case '.' when !inCharacterClass:
                    builder.Append(@"(?:[^\r\n\p{Cs}]|[\uD800-\uDBFF][\uDC00-\uDFFF])");
                    continue;
                default:
                    builder.Append(current);
                    continue;
            }
        }
        return builder.ToString();
    }
}
