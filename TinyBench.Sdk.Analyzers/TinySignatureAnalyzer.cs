using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TinyBench.Sdk.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class TinySignatureAnalyzer : DiagnosticAnalyzer
{
    public const string UnsupportedParameterId = "TBSDK001";
    public const string UnsupportedReturnId = "TBSDK002";
    public const string NonPublicMethodId = "TBSDK003";
    public const string AsyncMethodId = "TBSDK004";

    private static readonly DiagnosticDescriptor UnsupportedParameter = new(
        UnsupportedParameterId,
        "TinyBench exposed methods can only accept tiny value types",
        "Parameter '{0}' uses unsupported type '{1}'",
        "TinyBench.Sdk",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor UnsupportedReturn = new(
        UnsupportedReturnId,
        "TinyBench exposed methods can only return tiny value types",
        "Return type '{0}' is not supported",
        "TinyBench.Sdk",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor NonPublicMethod = new(
        NonPublicMethodId,
        "TinyBench exposed methods must be public",
        "Method '{0}' must be public",
        "TinyBench.Sdk",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor AsyncMethod = new(
        AsyncMethodId,
        "TinyBench exposed methods must be synchronous",
        "Method '{0}' must be synchronous and cannot return Task or ValueTask",
        "TinyBench.Sdk",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(UnsupportedParameter, UnsupportedReturn, NonPublicMethod, AsyncMethod);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(AnalyzeMethod, SymbolKind.Method);
    }

    private static void AnalyzeMethod(SymbolAnalysisContext context)
    {
        var method = (IMethodSymbol)context.Symbol;
        if (!IsExposed(method))
        {
            return;
        }

        if (method.DeclaredAccessibility != Accessibility.Public)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                NonPublicMethod,
                method.Locations[0],
                method.Name));
        }

        foreach (var parameter in method.Parameters)
        {
            if (!IsAllowedParameter(parameter.Type))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    UnsupportedParameter,
                    parameter.Locations.FirstOrDefault() ?? method.Locations[0],
                    parameter.Name,
                    parameter.Type.ToDisplayString()));
            }
        }

        if (IsAsyncReturn(method.ReturnType))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                AsyncMethod,
                method.Locations[0],
                method.Name));
            return;
        }

        if (!IsAllowedReturn(method.ReturnType))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                UnsupportedReturn,
                method.Locations[0],
                method.ReturnType.ToDisplayString()));
        }
    }

    private static bool IsExposed(IMethodSymbol method) =>
        method.GetAttributes().Any(attribute =>
            IsTinyAttribute(attribute, "TinyCommandAttribute") ||
            IsTinyAttribute(attribute, "TinyConnectAttribute") ||
            IsTinyAttribute(attribute, "TinyDisconnectAttribute"));

    private static bool IsTinyAttribute(AttributeData attribute, string name) =>
        string.Equals(attribute.AttributeClass?.Name, name, StringComparison.Ordinal) &&
        string.Equals(attribute.AttributeClass?.ContainingNamespace.ToDisplayString(), "TinyBench.Sdk.Core", StringComparison.Ordinal);

    private static bool IsAsyncReturn(ITypeSymbol type) =>
        IsType(type, "System.Threading.Tasks.Task") ||
        IsType(type, "System.Threading.Tasks.ValueTask") ||
        type is INamedTypeSymbol named &&
        named.IsGenericType &&
        (IsType(named, "System.Threading.Tasks.Task") || IsType(named, "System.Threading.Tasks.ValueTask"));

    private static bool IsAllowedReturn(ITypeSymbol type) =>
        type.SpecialType == SpecialType.System_Void ||
        IsAllowedTinyType(type);

    private static bool IsAllowedParameter(ITypeSymbol type) =>
        IsType(type, "System.Threading.CancellationToken") || IsAllowedTinyType(type);

    private static bool IsAllowedTinyType(ITypeSymbol type)
    {
        if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
        {
            type = nullable.TypeArguments[0];
        }

        if (type is IArrayTypeSymbol array)
        {
            type = array.ElementType;
        }

        return type.SpecialType is
            SpecialType.System_String or
            SpecialType.System_Int32 or
            SpecialType.System_Single or
            SpecialType.System_Double or
            SpecialType.System_Boolean;
    }

    private static bool IsType(ITypeSymbol type, string metadataName) =>
        string.Equals(type.OriginalDefinition.ToDisplayString(), metadataName, StringComparison.Ordinal) ||
        string.Equals(type.ToDisplayString(), metadataName, StringComparison.Ordinal);
}
