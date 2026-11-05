using TinyBench.Core;

namespace TinyBench.Sdk.Core;

/// <summary>Maps driver-facing C# types to the shared TinyBench value model.</summary>
public static class TinyTypeRules
{
    /// <summary>Returns whether a type is an allowed driver value.</summary>
    public static bool IsAllowed(Type type) => TryDescribe(type) is not null;

    /// <summary>Returns the shared type description, including an array's element kind.</summary>
    public static TinyType Describe(Type type) => TryDescribe(type)
        ?? throw new ArgumentOutOfRangeException(nameof(type), type.FullName, "Type is not allowed by TinyBench.");

    /// <summary>Validates a driver value, preserving the SDK's nullable scalar inputs and outputs.</summary>
    public static bool AcceptsValue(Type type, object? value)
    {
        var description = Describe(type);
        if (value is null)
            return type == typeof(string) || Nullable.GetUnderlyingType(type) is not null;
        return description.Accepts(value);
    }

    /// <summary>Returns the value kind, or element kind for the existing YAML array representation.</summary>
    public static TinyValueKind KindFor(Type type)
    {
        var description = Describe(type);
        return description.ElementKind ?? description.Kind;
    }

    /// <summary>Returns whether a type is an allowed scalar array.</summary>
    public static bool IsArray(Type type) => TryDescribe(type)?.Kind == TinyValueKind.Array;

    private static TinyType? TryDescribe(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return TinyType.ClrTypes.GetValueOrDefault(type);
    }
}
