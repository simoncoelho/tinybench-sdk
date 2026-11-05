using System.Reflection;
using TinyBench.Sdk.RestHost.Metadata;

namespace TinyBench.Sdk.RestHost.Driver;

public sealed class LoadedDriver
{
    public LoadedDriver(Type type, object instance, string attributeId, TinyBenchDefinition definition)
    {
        Type = type;
        Instance = instance;
        AttributeId = attributeId;
        Definition = definition;
        ConnectMethod = FindMethod(definition.Connection.ConnectMethod);
        DisconnectMethod = FindMethod(definition.Connection.DisconnectMethod);
        Commands = definition.Commands.ToDictionary(
            command => command.Id,
            command => new LoadedCommand(command, FindRequiredMethod(command.Method)),
            StringComparer.OrdinalIgnoreCase);
    }

    public Type Type { get; }

    public object Instance { get; }

    public string AttributeId { get; }

    public TinyBenchDefinition Definition { get; }

    public MethodInfo? ConnectMethod { get; }

    public MethodInfo? DisconnectMethod { get; }

    public IReadOnlyDictionary<string, LoadedCommand> Commands { get; }

    private MethodInfo? FindMethod(string? methodName)
    {
        if (string.IsNullOrWhiteSpace(methodName) || string.Equals(methodName, "TODO", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return Type.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public);
    }

    private MethodInfo FindRequiredMethod(string methodName) =>
        FindMethod(methodName)
        ?? throw new InvalidOperationException($"Command method '{methodName}' was not found on {Type.FullName}.");
}

public sealed record LoadedCommand(TinyCommandDocument Metadata, MethodInfo Method);
