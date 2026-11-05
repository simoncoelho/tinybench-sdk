using TinyBench.Core;

namespace TinyBench.Sdk.Driver.Framework;

/// <summary>Uses the SDK's shared C# mapping for the driver model.</summary>
public static class TinyTypeRules
{
    public static bool IsAllowed(Type type) => TinyBench.Sdk.Core.TinyTypeRules.IsAllowed(type);
    public static TinyType Describe(Type type) => TinyBench.Sdk.Core.TinyTypeRules.Describe(type);
    public static TinyValueKind KindFor(Type type) => TinyBench.Sdk.Core.TinyTypeRules.KindFor(type);
    public static bool IsArray(Type type) => TinyBench.Sdk.Core.TinyTypeRules.IsArray(type);
}
