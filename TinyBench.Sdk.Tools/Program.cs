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

Console.WriteLine($"TinyBench docs scaffolded in {outputPath}");
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
    var driver = driverType.GetCustomAttribute<TinyDriverAttribute>()!;
    var yaml = new StringBuilder()
        .AppendLine("# Human-owned TinyBench driver documentation.")
        .AppendLine("# This file is created once by TinyBench.Sdk.Tools. Edit it directly.")
        .AppendLine($"id: {Yaml(driver.Id)}")
        .AppendLine($"class: {Yaml(driverType.FullName ?? driverType.Name)}")
        .AppendLine("displayName: TODO")
        .AppendLine("manufacturer: TODO")
        .AppendLine("model: TODO")
        .AppendLine("version: 0.1.0")
        .AppendLine("description: TODO")
        .AppendLine("requiredSoftware:")
        .AppendLine("  - TODO")
        .AppendLine("setupInstructions: TODO")
        .ToString();

    WriteIfMissing(Path.Combine(outputPath, "driver.yaml"), yaml);
}

static void WriteConnectionYaml(string outputPath, Type driverType)
{
    var connect = driverType.GetMethods().FirstOrDefault(method => method.GetCustomAttribute<TinyConnectAttribute>() is not null);
    var disconnect = driverType.GetMethods().FirstOrDefault(method => method.GetCustomAttribute<TinyDisconnectAttribute>() is not null);
    var yaml = new StringBuilder()
        .AppendLine("# Connection docs stay separate from driver code.")
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
            yaml.AppendLine($"  - name: {Yaml(parameter.Name ?? "input")}");
            AppendType(yaml, parameter.ParameterType);
            yaml.AppendLine($"    displayName: TODO");
            yaml.AppendLine($"    description: TODO");
            yaml.AppendLine($"    required: {(!parameter.HasDefaultValue).ToString().ToLowerInvariant()}");
            if (parameter.HasDefaultValue)
            {
                yaml.AppendLine($"    default: {Yaml(parameter.DefaultValue)}");
            }
        }
    }

    WriteIfMissing(Path.Combine(outputPath, "connection.yaml"), yaml.ToString());
}

static void WriteCommandYaml(string outputPath, Type driverType)
{
    foreach (var method in driverType.GetMethods().Where(method => method.GetCustomAttribute<TinyCommandAttribute>() is not null))
    {
        var command = method.GetCustomAttribute<TinyCommandAttribute>()!;
        var yaml = new StringBuilder()
            .AppendLine("# Command documentation. Keep business logic in C#, keep docs here.")
            .AppendLine($"id: {Yaml(command.Id)}")
            .AppendLine($"method: {Yaml(method.Name)}")
            .AppendLine("displayName: TODO")
            .AppendLine("description: TODO")
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
                yaml.AppendLine($"  - name: {Yaml(parameter.Name ?? "input")}");
                AppendType(yaml, parameter.ParameterType);
                yaml.AppendLine($"    displayName: TODO");
                yaml.AppendLine($"    description: TODO");
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
            yaml.AppendLine($"  - name: {Yaml(output.Value.Name)}");
            AppendType(yaml, output.Value.Type);
            yaml.AppendLine($"    displayName: TODO");
            yaml.AppendLine($"    description: TODO");
        }

        yaml.AppendLine("sila:");
        yaml.AppendLine("  feature: TODO");
        yaml.AppendLine("  command: TODO");
        yaml.AppendLine("  description: TODO");

        WriteIfMissing(Path.Combine(outputPath, "commands", command.Id + ".yaml"), yaml.ToString());
    }
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

static void WriteIfMissing(string path, string content)
{
    if (File.Exists(path) && !Options.Current.Force)
    {
        Console.WriteLine($"unchanged {path}");
        return;
    }

    File.WriteAllText(path, content, Encoding.UTF8);
    Console.WriteLine($"created   {path}");
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
        Console.WriteLine("  --force            Overwrite existing generated YAML files.");
    }
}
