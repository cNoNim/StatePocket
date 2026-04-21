using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using StatePocket.Contracts;

namespace StatePocket.Errors;

internal sealed class ToolInvalidArgumentException(
    string message,
    string? argument = null,
    Exception? innerException = null
) : ToolErrorException(message, innerException)
{
    public static void ThrowIfNull(
        [NotNull] object? argument,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null,
        string? message = null
    )
    {
        if (argument is null)
        {
            throw new ToolInvalidArgumentException(message ?? $"{paramName ?? "Value"} must not be null.", paramName);
        }
    }

    public static void ThrowIfContainsNull<T>(
        IEnumerable<T?>? argument,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null,
        string? message = null
    )
        where T : class
    {
        if (argument is null)
        {
            return;
        }
        if (argument.Any(static item => item is null))
        {
            throw new ToolInvalidArgumentException(
                message ?? $"{paramName ?? "Value"} must not contain null values.",
                paramName
            );
        }
    }

    public static void ThrowIfEmptyOrWhitespace(
        string? argument,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null,
        string? message = null
    )
    {
        if (argument is null)
        {
            return;
        }
        if (argument.AsSpan()
                    .Trim()
                    .IsEmpty)
        {
            throw new ToolInvalidArgumentException(
                message ?? $"{paramName ?? "Value"} must not be empty or whitespace.",
                paramName
            );
        }
    }

    public static void ThrowIfCountExceeds<T>(
        IReadOnlyCollection<T>? argument,
        int maxCount,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null,
        string? message = null
    )
    {
        if (argument is null)
        {
            return;
        }
        if (argument.Count > maxCount)
        {
            throw new ToolInvalidArgumentException(
                message ?? $"{paramName ?? "Value"} must contain less than or equal to {maxCount} items.",
                paramName
            );
        }
    }

    public static void ThrowIfOutOfRange<T>(
        T? argument,
        T minimum,
        T maximum,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
        where T : struct, IComparable<T>
    {
        if (!argument.HasValue)
        {
            return;
        }
        if (argument.Value.CompareTo(minimum) < 0)
        {
            throw new ToolInvalidArgumentException(
                $"{paramName ?? "Value"} must be greater than or equal to {minimum}.",
                paramName
            );
        }
        if (argument.Value.CompareTo(maximum) > 0)
        {
            throw new ToolInvalidArgumentException(
                $"{paramName ?? "Value"} must be less than or equal to {maximum}.",
                paramName
            );
        }
    }

    public override ToolError ToPayload()
    {
        return new InvalidArgumentToolError
        {
            Message = Message,
            Retryable = false,
            Argument = argument
        };
    }
}
