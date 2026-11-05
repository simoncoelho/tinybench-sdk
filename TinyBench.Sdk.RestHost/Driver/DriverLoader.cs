using System.Reflection;
using TinyBench.Sdk.RestHost.Metadata;

namespace TinyBench.Sdk.RestHost.Driver;

public static class DriverLoader
{
    public static LoadedDriver Load(string assemblyPath, string docsPath)
    {
        assemblyPath = Path.GetFullPath(assemblyPath);
        docsPath = Path.GetFullPath(docsPath);

        if (!File.Exists(assemblyPath))
        {
            throw new FileNotFoundException("Driver assembly was not found.", assemblyPath);
        }

        if (!Directory.Exists(docsPath))
        {
            throw new DirectoryNotFoundException($"TinyBench docs folder was not found: {docsPath}");
        }

        var loadContext = new DriverLoadContext(assemblyPath);
        var assembly = loadContext.LoadFromAssemblyPath(assemblyPath);
        var driverType = assembly.GetTypes()
            .SingleOrDefault(type => GetTinyDriverAttribute(type) is not null)
            ?? throw new InvalidOperationException($"No [TinyDriver] class found in {assemblyPath}.");

        var driverId = GetAttributeId(GetTinyDriverAttribute(driverType));
        var instance = Activator.CreateInstance(driverType)
            ?? throw new InvalidOperationException($"Could not create driver instance for {driverType.FullName}.");
        var definition = TinyBenchDefinitionReader.Read(docsPath);

        return new LoadedDriver(driverType, instance, driverId, definition);
    }

    private static CustomAttributeData? GetTinyDriverAttribute(Type type) =>
        type.GetCustomAttributesData().SingleOrDefault(attribute =>
            string.Equals(attribute.AttributeType.Name, "TinyDriverAttribute", StringComparison.Ordinal) &&
            string.Equals(attribute.AttributeType.Namespace, "TinyBench.Sdk.Core", StringComparison.Ordinal));

    private static string GetAttributeId(CustomAttributeData? attribute)
    {
        if (attribute is null || attribute.ConstructorArguments.Count == 0)
        {
            return "";
        }

        return attribute.ConstructorArguments[0].Value as string ?? "";
    }
}
