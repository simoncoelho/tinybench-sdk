namespace TinyBench.Sdk.Core;

/// <summary>
/// Shared type rules used by analyzers and scaffolding tools.
/// </summary>
public static class TinyTypeRules
{
    private static readonly HashSet<Type> AllowedTypes =
    [
        typeof(string),
        typeof(int),
        typeof(float),
        typeof(double),
        typeof(bool)
    ];

    /// <summary>Returns true when a C# type is part of the TinyBench SDK value language.</summary>
    public static bool IsAllowed(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (type.IsArray)
        {
            type = type.GetElementType()!;
        }

        return AllowedTypes.Contains(type);
    }

    /// <summary>Maps an allowed C# type to a TinyBench value kind.</summary>
    public static TinyValueKind KindFor(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (type.IsArray)
        {
            type = type.GetElementType()!;
        }

        if (type == typeof(string)) return TinyValueKind.String;
        if (type == typeof(int)) return TinyValueKind.Integer;
        if (type == typeof(double)) return TinyValueKind.Double;
        if (type == typeof(float)) return TinyValueKind.Float;
        if (type == typeof(bool)) return TinyValueKind.Boolean;
        throw new ArgumentOutOfRangeException(nameof(type), type.FullName, "Type is not allowed by TinyBench.");
    }

    /// <summary>Returns true when a type is an array of tiny values.</summary>
    public static bool IsArray(Type type) => (Nullable.GetUnderlyingType(type) ?? type).IsArray;
}
