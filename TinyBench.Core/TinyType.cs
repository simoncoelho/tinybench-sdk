namespace TinyBench.Core;

/// <summary>A value kind, with an element kind only for homogeneous scalar arrays.</summary>
public sealed record TinyType
{
    /// <summary>Creates a type. Arrays require a scalar element kind; other kinds forbid one.</summary>
    public TinyType(TinyValueKind kind, TinyValueKind? elementKind = null)
    {
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind));

        if (kind == TinyValueKind.Array)
        {
            if (elementKind is null || !IsScalar(elementKind.Value))
                throw new ArgumentException("Arrays require a scalar element kind.", nameof(elementKind));
        }
        else if (elementKind is not null)
        {
            throw new ArgumentException("Only arrays have an element kind.", nameof(elementKind));
        }

        Kind = kind;
        ElementKind = elementKind;
    }

    /// <summary>The kind of this value.</summary>
    public TinyValueKind Kind { get; }

    /// <summary>The scalar element kind for an array, otherwise null.</summary>
    public TinyValueKind? ElementKind { get; }

    /// <summary>Whether a kind can be used as an array element or table cell.</summary>
    public static bool IsScalar(TinyValueKind kind) => kind is
        TinyValueKind.String or TinyValueKind.Boolean or TinyValueKind.Integer or TinyValueKind.Float;

    /// <summary>Checks a non-null value, including all array elements. Table nullability belongs to columns.</summary>
    public bool Accepts(object? value)
    {
        if (Kind == TinyValueKind.Table)
            return value is TinyTable;

        if (Kind != TinyValueKind.Array)
            return AcceptsScalar(Kind, value);

        // Require a typed vector even when empty; object[], jagged arrays and tables are not scalar arrays.
        var validArray = ElementKind switch
        {
            TinyValueKind.String => value is string[],
            TinyValueKind.Boolean => value is bool[],
            TinyValueKind.Integer => value is int[],
            TinyValueKind.Float => value is float[] or double[],
            _ => false
        };
        return validArray && ((Array)value!).Cast<object?>().All(item => AcceptsScalar(ElementKind!.Value, item));
    }

    internal static bool AcceptsScalar(TinyValueKind kind, object? value) => kind switch
    {
        TinyValueKind.String => value is string,
        TinyValueKind.Boolean => value is bool,
        TinyValueKind.Integer => value is int,
        TinyValueKind.Float => value is double d && double.IsFinite(d) || value is float f && float.IsFinite(f),
        _ => false
    };
}
