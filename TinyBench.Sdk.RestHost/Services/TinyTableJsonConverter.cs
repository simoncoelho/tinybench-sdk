using System.Text.Json;
using System.Text.Json.Serialization;
using TinyBench.Core;

namespace TinyBench.Sdk.RestHost.Services;

/// <summary>Transports table schemas and typed cells without adding JSON dependencies to Core.</summary>
public sealed class TinyTableJsonConverter : JsonConverter<TinyTable>
{
    public override TinyTable Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        try
        {
            var root = document.RootElement;
            var columns = root.GetProperty("columns").EnumerateArray().Select(column =>
            {
                var kindName = column.GetProperty("kind").GetString();
                if (!Enum.TryParse<TinyValueKind>(kindName, out var kind) || !TinyType.IsScalar(kind) || kind.ToString() != kindName)
                    throw new JsonException($"Unknown scalar column kind '{kindName}'.");

                return new TinyTableColumn(
                    column.GetProperty("name").GetString()!,
                    kind,
                    column.TryGetProperty("isNullable", out var nullable) && nullable.GetBoolean());
            }).ToArray();

            var rows = root.GetProperty("rows").EnumerateArray().Select(row =>
            {
                var cells = row.EnumerateArray().ToArray();
                if (cells.Length != columns.Length)
                    throw new JsonException($"Expected {columns.Length} table cells, received {cells.Length}.");
                return cells.Select((cell, index) => ReadCell(cell, columns[index].Kind)).ToArray();
            }).ToArray();
            return new TinyTable(columns, rows);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or KeyNotFoundException or FormatException or OverflowException)
        {
            throw new JsonException($"Invalid TinyBench table: {ex.Message}", ex);
        }
    }

    private static object? ReadCell(JsonElement cell, TinyValueKind kind)
    {
        if (cell.ValueKind == JsonValueKind.Null) return null;
        return kind switch
        {
            TinyValueKind.String => cell.GetString(),
            TinyValueKind.Boolean => cell.GetBoolean(),
            TinyValueKind.Integer => cell.GetInt32(),
            TinyValueKind.Float => cell.GetDouble(),
            _ => throw new JsonException("Table cells must be scalar values.")
        };
    }

    public override void Write(Utf8JsonWriter writer, TinyTable value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteStartArray("columns");
        foreach (var column in value.Columns)
        {
            writer.WriteStartObject();
            writer.WriteString("name", column.Name);
            writer.WriteString("kind", column.Kind.ToString());
            writer.WriteBoolean("isNullable", column.IsNullable);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteStartArray("rows");
        foreach (var row in value.Rows)
        {
            writer.WriteStartArray();
            foreach (var cell in row)
                JsonSerializer.Serialize(writer, cell, options);
            writer.WriteEndArray();
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
    }
}
