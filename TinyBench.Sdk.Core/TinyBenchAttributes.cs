namespace TinyBench.Sdk.Core;

/// <summary>
/// Marks the root class for a TinyBench driver library.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class TinyDriverAttribute(string id) : Attribute
{
    /// <summary>Stable driver identifier used by generated documentation and adapters.</summary>
    public string Id { get; } = id;
}

/// <summary>
/// Marks the method that opens the device or vendor SDK connection.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class TinyConnectAttribute : Attribute;

/// <summary>
/// Marks the method that closes the device or vendor SDK connection.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class TinyDisconnectAttribute : Attribute;

/// <summary>
/// Marks a method as a command that can be exposed by TinyBench tooling.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class TinyCommandAttribute(string id) : Attribute
{
    /// <summary>Stable command identifier.</summary>
    public string Id { get; } = id;
}
