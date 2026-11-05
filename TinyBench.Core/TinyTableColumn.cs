namespace TinyBench.Core;

/// <summary>A named scalar column. Null cells must be explicitly enabled.</summary>
public sealed record TinyTableColumn
{
    /// <summary>Creates a column with a non-empty name and a scalar kind.</summary>
    public TinyTableColumn(string name, TinyValueKind kind, bool isNullable = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!TinyType.IsScalar(kind))
            throw new ArgumentException("Table columns must have a scalar kind.", nameof(kind));

        Name = name;
        Kind = kind;
        IsNullable = isNullable;
    }

    /// <summary>The case-sensitive column name, unique within its table.</summary>
    public string Name { get; }

    /// <summary>The scalar kind shared by every non-null cell.</summary>
    public TinyValueKind Kind { get; }

    /// <summary>Whether cells in this column can be null.</summary>
    public bool IsNullable { get; }
}
