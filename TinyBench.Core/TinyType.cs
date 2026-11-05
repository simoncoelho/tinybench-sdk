using System.Collections.ObjectModel;

namespace TinyBench.Core;

/// <summary>A value kind, with an element kind only for homogeneous scalar arrays.</summary>
public sealed record TinyType
{
    /// <summary>
    /// The supported C# representations of TinyBench values. SDK runtime checks and compiler
    /// analyzers consume this same immutable registry, including nullable scalars and arrays.
    /// </summary>
    public static IReadOnlyDictionary<Type, TinyType> ClrTypes { get; } = CreateClrTypes();

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

        // Checking the registered CLR type also rejects untyped, jagged and multidimensional arrays.
        return value is Array array && ClrTypes.TryGetValue(value.GetType(), out var type) && type == this
            && array.Cast<object?>().All(item => AcceptsScalar(ElementKind!.Value, item));
    }

    internal static bool AcceptsScalar(TinyValueKind kind, object? value) =>
        value is not null && IsScalar(kind)
        && ClrTypes.TryGetValue(value.GetType(), out var type) && type.Kind == kind
        && (value is not double d || double.IsFinite(d))
        && (value is not float f || float.IsFinite(f));

    private static IReadOnlyDictionary<Type, TinyType> CreateClrTypes()
    {
        var scalars = new Dictionary<Type, TinyValueKind>
        {
            [typeof(string)] = TinyValueKind.String,
            [typeof(bool)] = TinyValueKind.Boolean,
            [typeof(int)] = TinyValueKind.Integer,
            [typeof(float)] = TinyValueKind.Float,
            [typeof(double)] = TinyValueKind.Float
        };
        var types = new Dictionary<Type, TinyType>();
        foreach (var (clrType, kind) in scalars)
        {
            var scalar = new TinyType(kind);
            types.Add(clrType, scalar);
            types.Add(clrType.MakeArrayType(), new TinyType(TinyValueKind.Array, kind));
            // Nullable scalar parameters are supported by the SDK; array elements remain non-nullable.
            if (clrType.IsValueType)
                types.Add(typeof(Nullable<>).MakeGenericType(clrType), scalar);
        }
        types.Add(typeof(TinyTable), new TinyType(TinyValueKind.Table));
        return new ReadOnlyDictionary<Type, TinyType>(types);
    }
}
