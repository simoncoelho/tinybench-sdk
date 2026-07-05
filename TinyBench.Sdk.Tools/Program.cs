using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using TinyBench.Sdk.Core;

var options = Options.Parse(args);
if (options.ShowHelp)
{
    Options.PrintHelp();
    return 0;
}

var projectPath = Path.GetFullPath(options.ProjectPath ?? Directory.GetCurrentDirectory());
var assemblyPath = options.AssemblyPath is null
    ? FindDefaultAssembly(projectPath)
    : Path.GetFullPath(options.AssemblyPath);
var outputPath = Path.GetFullPath(options.OutputPath ?? Path.Combine(projectPath, ".tinybench"));

if (!File.Exists(assemblyPath))
{
    Console.Error.WriteLine($"Driver assembly was not found: {assemblyPath}");
    Console.Error.WriteLine("Build the driver project first, or pass --assembly <path>.");
    return 1;
}

Directory.CreateDirectory(outputPath);
Directory.CreateDirectory(Path.Combine(outputPath, "commands"));

var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(assemblyPath);
var drivers = assembly.GetTypes()
    .Where(type => type.GetCustomAttribute<TinyDriverAttribute>() is not null)
    .ToArray();

if (drivers.Length == 0)
{
    Console.Error.WriteLine($"No [{nameof(TinyDriverAttribute).Replace("Attribute", "")}] class found in {assemblyPath}.");
    return 1;
}

foreach (var driverType in drivers)
{
    WriteDriverYaml(outputPath, driverType);
    WriteConnectionYaml(outputPath, driverType);
    WriteCommandYaml(outputPath, driverType);
}

Console.WriteLine($"TinyBench docs synced in {outputPath}");
return 0;

static string FindDefaultAssembly(string projectPath)
{
    var projectFile = Directory.EnumerateFiles(projectPath, "*.csproj").SingleOrDefault()
        ?? throw new InvalidOperationException($"No project file found in {projectPath}.");
    var assemblyName = Path.GetFileNameWithoutExtension(projectFile) + ".dll";
    return Path.Combine(projectPath, "bin", "Debug", "net10.0", assemblyName);
}

static void WriteDriverYaml(string outputPath, Type driverType)
{
    var path = Path.Combine(outputPath, "driver.yaml");
    var existing = Options.Current.Force ? EmptyValues() : ReadTopLevelValues(path);
    var requiredSoftware = Options.Current.Force ? null : ReadBlock(path, "requiredSoftware");
    var driver = driverType.GetCustomAttribute<TinyDriverAttribute>()!;
    var displayName = Preserve(existing, "displayName", "TODO");
    var manufacturer = Preserve(existing, "manufacturer", "TODO");
    var model = Preserve(existing, "model", "TODO");
    var version = Preserve(existing, "version", "0.1.0");
    var description = Preserve(existing, "description", "TODO");
    var setupInstructions = Preserve(existing, "setupInstructions", "TODO");
    var yaml = new StringBuilder()
        .AppendLine("# TinyBench driver documentation.")
        .AppendLine("# TinyBench.Sdk.Tools keeps generated fields current and preserves editable metadata when possible.")
        .AppendLine($"id: {Yaml(driver.Id)}")
        .AppendLine($"class: {Yaml(driverType.FullName ?? driverType.Name)}")
        .AppendLine($"displayName: {displayName}")
        .AppendLine($"manufacturer: {manufacturer}")
        .AppendLine($"model: {model}")
        .AppendLine($"version: {version}")
        .AppendLine($"description: {description}")
        .AppendLine("requiredSoftware:");

    if (requiredSoftware is { Count: > 0 })
    {
        foreach (var line in requiredSoftware)
        {
            yaml.AppendLine(line);
        }
    }
    else
    {
        yaml.AppendLine("  - TODO");
    }

    yaml.AppendLine($"setupInstructions: {setupInstructions}");
    WriteSynced(path, yaml.ToString());
}

static void WriteConnectionYaml(string outputPath, Type driverType)
{
    var path = Path.Combine(outputPath, "connection.yaml");
    var existing = Options.Current.Force ? EmptyItems() : ReadItemValues(path, "parameters");
    var connect = driverType.GetMethods().FirstOrDefault(method => method.GetCustomAttribute<TinyConnectAttribute>() is not null);
    var disconnect = driverType.GetMethods().FirstOrDefault(method => method.GetCustomAttribute<TinyDisconnectAttribute>() is not null);
    var yaml = new StringBuilder()
        .AppendLine("# Connection docs stay separate from driver code.")
        .AppendLine("# TinyBench.Sdk.Tools keeps method and parameter structure current.")
        .AppendLine($"connectMethod: {Yaml(connect?.Name ?? "TODO")}")
        .AppendLine($"disconnectMethod: {Yaml(disconnect?.Name ?? "TODO")}")
        .AppendLine("parameters:");

    if (connect is null || connect.GetParameters().Length == 0)
    {
        yaml.AppendLine("  []");
    }
    else
    {
        foreach (var parameter in connect.GetParameters().Where(parameter => parameter.ParameterType != typeof(CancellationToken)))
        {
            var name = parameter.Name ?? "input";
            var metadata = existing.TryGetValue(name, out var values) ? values : EmptyMutableValues();
            var displayName = Preserve(metadata, "displayName", "TODO");
            var description = Preserve(metadata, "description", "TODO");
            yaml.AppendLine($"  - name: {Yaml(name)}");
            AppendType(yaml, parameter.ParameterType);
            yaml.AppendLine($"    displayName: {displayName}");
            yaml.AppendLine($"    description: {description}");
            yaml.AppendLine($"    required: {(!parameter.HasDefaultValue).ToString().ToLowerInvariant()}");
            if (parameter.HasDefaultValue)
            {
                yaml.AppendLine($"    default: {Yaml(parameter.DefaultValue)}");
            }
        }
    }

    WriteSynced(path, yaml.ToString());
}

static void WriteCommandYaml(string outputPath, Type driverType)
{
    var commandsPath = Path.Combine(outputPath, "commands");
    Directory.CreateDirectory(commandsPath);
    var discovered = driverType.GetMethods()
        .Where(method => method.GetCustomAttribute<TinyCommandAttribute>() is not null)
        .Select(method => (Method: method, Command: method.GetCustomAttribute<TinyCommandAttribute>()!))
        .ToArray();
    var commandIds = discovered.Select(item => item.Command.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

    foreach (var existingPath in Directory.EnumerateFiles(commandsPath, "*.yaml"))
    {
        var id = Path.GetFileNameWithoutExtension(existingPath);
        if (!commandIds.Contains(id))
        {
            File.Delete(existingPath);
            Console.WriteLine($"removed   {existingPath}");
        }
    }

    foreach (var (method, command) in discovered)
    {
        WriteOneCommandYaml(commandsPath, method, command);
    }
}

static void WriteOneCommandYaml(string commandsPath, MethodInfo method, TinyCommandAttribute command)
{
    var path = Path.Combine(commandsPath, command.Id + ".yaml");
    var topLevel = Options.Current.Force ? EmptyValues() : ReadTopLevelValues(path);
    var existingInputs = Options.Current.Force ? EmptyItems() : ReadItemValues(path, "inputs");
    var existingOutputs = Options.Current.Force ? EmptyItems() : ReadItemValues(path, "outputs");
    var sila = Options.Current.Force ? EmptyValues() : ReadNestedValues(path, "sila");
    var displayName = Preserve(topLevel, "displayName", "TODO");
    var description = Preserve(topLevel, "description", "TODO");
    var yaml = new StringBuilder()
        .AppendLine("# Command documentation. Keep business logic in C#, keep docs here.")
        .AppendLine("# TinyBench.Sdk.Tools keeps id, method, inputs, and outputs current.")
        .AppendLine($"id: {Yaml(command.Id)}")
        .AppendLine($"method: {Yaml(method.Name)}")
        .AppendLine($"displayName: {displayName}")
        .AppendLine($"description: {description}")
        .AppendLine("inputs:");

    var inputs = method.GetParameters().Where(parameter => parameter.ParameterType != typeof(CancellationToken)).ToArray();
    if (inputs.Length == 0)
    {
        yaml.AppendLine("  []");
    }
    else
    {
        foreach (var parameter in inputs)
        {
            var name = parameter.Name ?? "input";
            var metadata = existingInputs.TryGetValue(name, out var values) ? values : EmptyMutableValues();
            var inputDisplayName = Preserve(metadata, "displayName", "TODO");
            var inputDescription = Preserve(metadata, "description", "TODO");
            yaml.AppendLine($"  - name: {Yaml(name)}");
            AppendType(yaml, parameter.ParameterType);
            yaml.AppendLine($"    displayName: {inputDisplayName}");
            yaml.AppendLine($"    description: {inputDescription}");
            yaml.AppendLine($"    required: {(!parameter.HasDefaultValue).ToString().ToLowerInvariant()}");
            if (parameter.HasDefaultValue)
            {
                yaml.AppendLine($"    default: {Yaml(parameter.DefaultValue)}");
            }
        }
    }

    yaml.AppendLine("outputs:");
    var output = CreateOutput(method);
    if (output is null)
    {
        yaml.AppendLine("  []");
    }
    else
    {
        var metadata = existingOutputs.TryGetValue(output.Value.Name, out var values) ? values : EmptyMutableValues();
        var outputDisplayName = Preserve(metadata, "displayName", "TODO");
        var outputDescription = Preserve(metadata, "description", "TODO");
        yaml.AppendLine($"  - name: {Yaml(output.Value.Name)}");
        AppendType(yaml, output.Value.Type);
        yaml.AppendLine($"    displayName: {outputDisplayName}");
        yaml.AppendLine($"    description: {outputDescription}");
    }

    var silaFeature = Preserve(sila, "feature", "TODO");
    var silaCommand = Preserve(sila, "command", "TODO");
    var silaDescription = Preserve(sila, "description", "TODO");
    yaml.AppendLine("sila:");
    yaml.AppendLine($"  feature: {silaFeature}");
    yaml.AppendLine($"  command: {silaCommand}");
    yaml.AppendLine($"  description: {silaDescription}");

    WriteSynced(path, yaml.ToString());
}

static Type UnwrapReturn(Type type)
{
    if (IsAsyncReturn(type))
    {
        throw new InvalidOperationException("TinyBench SDK methods must be synchronous and cannot return Task or ValueTask.");
    }

    return type;
}

static bool IsAsyncReturn(Type type) =>
    type == typeof(Task) ||
    type == typeof(ValueTask) ||
    type.IsGenericType &&
    (type.GetGenericTypeDefinition() == typeof(Task<>) || type.GetGenericTypeDefinition() == typeof(ValueTask<>));

static (string Name, Type Type)? CreateOutput(MethodInfo method)
{
    var outputType = UnwrapReturn(method.ReturnType);
    return outputType == typeof(void) ? null : ("result", outputType);
}

static void AppendType(StringBuilder yaml, Type type)
{
    type = UnwrapReturn(type);
    var isArray = type.IsArray;
    yaml.AppendLine($"    type: {Yaml(TinyTypeRules.KindFor(type).ToString())}");
    yaml.AppendLine($"    array: {isArray.ToString().ToLowerInvariant()}");
}

static string Yaml(object? value)
{
    if (value is null)
    {
        return "null";
    }

    if (value is bool boolean)
    {
        return boolean.ToString().ToLowerInvariant();
    }

    if (value is int or float or double)
    {
        return Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "0";
    }

    var text = Convert.ToString(value) ?? "";
    return "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
}

static IReadOnlyDictionary<string, string> EmptyValues() => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

static IReadOnlyDictionary<string, Dictionary<string, string>> EmptyItems() =>
    new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

static Dictionary<string, string> EmptyMutableValues() => new(StringComparer.OrdinalIgnoreCase);

static string Preserve(IReadOnlyDictionary<string, string> values, string key, string fallback)
{
    if (!Options.Current.Force && values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
    {
        return value;
    }

    return Yaml(fallback);
}

static IReadOnlyDictionary<string, string> ReadTopLevelValues(string path)
{
    var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    if (!File.Exists(path))
    {
        return values;
    }

    foreach (var line in File.ReadLines(path))
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

        var value = line[(separator + 1)..].Trim();
        if (value.Length > 0)
        {
            values[line[..separator].Trim()] = value;
        }
    }

    return values;
}

static IReadOnlyDictionary<string, string> ReadNestedValues(string path, string section)
{
    var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    if (!File.Exists(path))
    {
        return values;
    }

    var inSection = false;
    foreach (var line in File.ReadLines(path))
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

        var trimmed = line.Trim();
        var separator = trimmed.IndexOf(':');
        if (separator > 0)
        {
            values[trimmed[..separator].Trim()] = trimmed[(separator + 1)..].Trim();
        }
    }

    return values;
}

static IReadOnlyDictionary<string, Dictionary<string, string>> ReadItemValues(string path, string section)
{
    var values = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
    if (!File.Exists(path))
    {
        return values;
    }

    var inSection = false;
    Dictionary<string, string>? current = null;
    foreach (var line in File.ReadLines(path))
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

        var trimmed = line.Trim();
        if (trimmed.StartsWith("- name:", StringComparison.Ordinal))
        {
            var name = trimmed["- name:".Length..].Trim();
            current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["name"] = name
            };
            values[Unquote(name)] = current;
            continue;
        }

        if (current is null)
        {
            continue;
        }

        var separator = trimmed.IndexOf(':');
        if (separator > 0)
        {
            current[trimmed[..separator].Trim()] = trimmed[(separator + 1)..].Trim();
        }
    }

    return values;
}

static IReadOnlyList<string>? ReadBlock(string path, string section)
{
    if (!File.Exists(path))
    {
        return null;
    }

    var lines = new List<string>();
    var inSection = false;
    foreach (var line in File.ReadLines(path))
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

        lines.Add(line);
    }

    return lines;
}

static string Unquote(string value)
{
    value = value.Trim();
    if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
    {
        return value[1..^1].Replace("\\\"", "\"").Replace("\\\\", "\\");
    }

    return value;
}

static void WriteSynced(string path, string content)
{
    var existed = File.Exists(path);
    File.WriteAllText(path, content, Encoding.UTF8);
    Console.WriteLine(existed ? $"synced    {path}" : $"created   {path}");
}

internal sealed record Options(string? ProjectPath, string? AssemblyPath, string? OutputPath, bool Force, bool ShowHelp)
{
    public static Options Current { get; private set; } = new(null, null, null, false, false);

    public static Options Parse(string[] args)
    {
        string? project = null;
        string? assembly = null;
        string? output = null;
        var force = false;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--help" or "-h":
                    Current = new(null, null, null, false, true);
                    return Current;
                case "--force":
                    force = true;
                    break;
                case "--project":
                    project = args[++i];
                    break;
                case "--assembly":
                    assembly = args[++i];
                    break;
                case "--output":
                    output = args[++i];
                    break;
            }
        }

        Current = new(project, assembly, output, force, false);
        return Current;
    }

    public static void PrintHelp()
    {
        Console.WriteLine("TinyBench SDK docs scaffold");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  dotnet run --project TinyBench.Sdk.Tools -- --project <driver-project>");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --project <path>   Driver project folder. Defaults to current directory.");
        Console.WriteLine("  --assembly <path>  Driver assembly. Defaults to bin/Debug/net10.0/<project>.dll.");
        Console.WriteLine("  --output <path>    Docs output. Defaults to <project>/.tinybench.");
        Console.WriteLine("  --force            Regenerate docs without preserving editable metadata.");
    }
}
