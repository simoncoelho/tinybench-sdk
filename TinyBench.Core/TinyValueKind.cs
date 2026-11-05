namespace TinyBench.Core;

/// <summary>The complete set of value kinds understood by TinyBench.</summary>
public enum TinyValueKind
{
    /// <summary>Text.</summary>
    String,
    /// <summary>A true or false value.</summary>
    Boolean,
    /// <summary>A signed 32-bit whole number.</summary>
    Integer,
    /// <summary>A finite floating-point number, represented by float or double in C#.</summary>
    Float,
    /// <summary>A one-dimensional array of a single scalar kind.</summary>
    Array,
    /// <summary>A table of typed scalar columns.</summary>
    Table
}
