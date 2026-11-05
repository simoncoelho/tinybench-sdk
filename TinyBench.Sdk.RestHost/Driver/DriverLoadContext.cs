using System.Reflection;
using System.Runtime.Loader;
using TinyBench.Core;

namespace TinyBench.Sdk.RestHost.Driver;

public sealed class DriverLoadContext(string driverAssemblyPath) : AssemblyLoadContext(isCollectible: false)
{
    private readonly AssemblyDependencyResolver resolver = new(driverAssemblyPath);

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        // Contract values must have the same identity in the driver and in the host.
        var coreAssembly = typeof(TinyTable).Assembly;
        if (AssemblyName.ReferenceMatchesDefinition(assemblyName, coreAssembly.GetName()))
            return coreAssembly;

        var assemblyPath = resolver.ResolveAssemblyToPath(assemblyName);
        return assemblyPath is null ? null : LoadFromAssemblyPath(assemblyPath);
    }
}
