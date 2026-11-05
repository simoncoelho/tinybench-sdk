namespace TinyBench.Sdk.RestHost.Metadata;

public sealed class TinyYaml
{
    private readonly string[] lines;

    private TinyYaml(string[] lines)
    {
        this.lines = lines;
    }

    public static TinyYaml Read(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("TinyBench metadata file was not found.", path);
        }

        return new TinyYaml(File.ReadAllLines(path));
    }

    public string Value(string key, string fallback = "")
    {
        foreach (var line in lines)
        {
            if (line.Length == 0 || char.IsWhiteSpace(line[0]) || line.StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }

            var separator = line.IndexOf(':');
            if (separator <= 0)
            {
                continue;
            }

            if (string.Equals(line[..separator].Trim(), key, StringComparison.OrdinalIgnoreCase))
            {
                return Unquote(line[(separator + 1)..].Trim());
            }
        }

        return fallback;
    }

    public IReadOnlyList<string> List(string section)
    {
        var values = new List<string>();
        foreach (var line in SectionLines(section))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("- ", StringComparison.Ordinal))
            {
                values.Add(Unquote(trimmed[2..].Trim()));
            }
        }

        return values;
    }

    public IReadOnlyDictionary<string, string> NestedValues(string section)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in SectionLines(section))
        {
            var trimmed = line.Trim();
            var separator = trimmed.IndexOf(':');
            if (separator > 0)
            {
                values[trimmed[..separator].Trim()] = Unquote(trimmed[(separator + 1)..].Trim());
            }
        }

        return values;
    }

    public IReadOnlyList<TinyParameterDocument> Parameters(string section)
    {
        var parameters = new List<TinyParameterDocument>();
        Dictionary<string, string>? current = null;

        foreach (var line in SectionLines(section))
        {
            var trimmed = line.Trim();
            if (trimmed == "[]")
            {
                break;
            }

            if (trimmed.StartsWith("- name:", StringComparison.Ordinal))
            {
                if (current is not null)
                {
                    parameters.Add(ToParameter(current));
                }

                current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["name"] = Unquote(trimmed["- name:".Length..].Trim())
                };
                continue;
            }

            if (current is null)
            {
                continue;
            }

            var separator = trimmed.IndexOf(':');
            if (separator > 0)
            {
                current[trimmed[..separator].Trim()] = Unquote(trimmed[(separator + 1)..].Trim());
            }
        }

        if (current is not null)
        {
            parameters.Add(ToParameter(current));
        }

        return parameters;
    }

    private IEnumerable<string> SectionLines(string section)
    {
        var inSection = false;
        foreach (var line in lines)
        {
            if (!inSection)
            {
                inSection = string.Equals(line.Trim(), section + ":", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (line.Length == 0)
            {
                continue;
            }

            if (!line.StartsWith("  ", StringComparison.Ordinal))
            {
                break;
            }

            yield return line;
        }
    }

    private static TinyParameterDocument ToParameter(IReadOnlyDictionary<string, string> values) =>
        new(
            values.GetValueOrDefault("name", ""),
            values.GetValueOrDefault("type", ""),
            bool.TryParse(values.GetValueOrDefault("array", "false"), out var array) && array,
            values.GetValueOrDefault("displayName", ""),
            values.GetValueOrDefault("description", ""),
            bool.TryParse(values.GetValueOrDefault("required", "false"), out var required) && required,
            values.TryGetValue("default", out var defaultValue) ? defaultValue : null);

    private static string Unquote(string value)
    {
        value = value.Trim();
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
        {
            return value[1..^1].Replace("\\\"", "\"").Replace("\\\\", "\\");
        }

        return value;
    }
}
