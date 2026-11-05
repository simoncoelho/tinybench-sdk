namespace TinyBench.Core;

/// <summary>An immutable, validated snapshot of columns and rows of scalar values.</summary>
public sealed class TinyTable
{
    /// <summary>Copies and validates the schema and every row. Cells follow column order.</summary>
    public TinyTable(IEnumerable<TinyTableColumn> columns, IEnumerable<IEnumerable<object?>> rows)
    {
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(rows);

        var columnArray = columns.ToArray();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var column in columnArray)
        {
            if (column is null)
                throw new ArgumentException("Columns cannot contain null.", nameof(columns));
            if (!names.Add(column.Name))
                throw new ArgumentException($"Duplicate column '{column.Name}'.", nameof(columns));
        }

        var copiedRows = new List<IReadOnlyList<object?>>();
        foreach (var row in rows)
        {
            if (row is null)
                throw new ArgumentException("Rows cannot contain null.", nameof(rows));
            var cells = row.ToArray();
            if (cells.Length != columnArray.Length)
                throw new ArgumentException($"Row {copiedRows.Count} has {cells.Length} cells; expected {columnArray.Length}.", nameof(rows));

            for (var index = 0; index < cells.Length; index++)
            {
                var column = columnArray[index];
                var valid = cells[index] is null
                    ? column.IsNullable
                    : TinyType.AcceptsScalar(column.Kind, cells[index]);
                if (!valid)
                    throw new ArgumentException($"Row {copiedRows.Count}, column '{column.Name}' requires {column.Kind}{(column.IsNullable ? " or null" : " (non-null)")}.", nameof(rows));
            }

            copiedRows.Add(Array.AsReadOnly(cells));
        }

        Columns = Array.AsReadOnly(columnArray);
        Rows = copiedRows.AsReadOnly();
    }

    /// <summary>The ordered, immutable column schema.</summary>
    public IReadOnlyList<TinyTableColumn> Columns { get; }

    /// <summary>The immutable rows, with cells in column order.</summary>
    public IReadOnlyList<IReadOnlyList<object?>> Rows { get; }
}
