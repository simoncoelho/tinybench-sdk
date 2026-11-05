using System.Text.Json;
using TinyBench.Sdk.Core;

namespace TinyBench.Sdk.RestHost.Services;

public static class TinyJsonInputs
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new TinyTableJsonConverter() }
    };

    public static object? Convert(Type type, JsonElement value)
    {
        var description = TinyTypeRules.Describe(type);
        var result = JsonSerializer.Deserialize(value, type, JsonOptions);
        if (!TinyTypeRules.AcceptsValue(type, result))
            throw new JsonException($"Input must be a non-null {description.Kind} value with valid scalar elements.");
        return result;
    }
}
