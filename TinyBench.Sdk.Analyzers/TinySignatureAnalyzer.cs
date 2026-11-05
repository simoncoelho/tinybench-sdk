using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using TinyBench.Core;

namespace TinyBench.Sdk.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class TinySignatureAnalyzer : DiagnosticAnalyzer
{
    public const string UnsupportedParameterId = "TBSDK001";
    public const string UnsupportedReturnId = "TBSDK002";
    public const string NonPublicMethodId = "TBSDK003";
    public const string AsyncMethodId = "TBSDK004";
    public const string RefParameterId = "TBSDK005";

    private static readonly DiagnosticDescriptor UnsupportedParameter = new(
        UnsupportedParameterId,
        "TinyBench exposed methods can only accept tiny value types",
        "Parameter '{0}' uses unsupported type '{1}'",
        "TinyBench.Sdk",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor UnsupportedReturn = new(
        UnsupportedReturnId,
        "TinyBench exposed methods must return void",
        "Method '{0}' must return void; use out parameters for command outputs",
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

    private static readonly DiagnosticDescriptor RefParameter = new(
        RefParameterId,
        "TinyBench exposed methods cannot use ref or in parameters",
        "Parameter '{0}' uses '{1}', but TinyBench only supports normal inputs and out outputs",
        "TinyBench.Sdk",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(UnsupportedParameter, UnsupportedReturn, NonPublicMethod, AsyncMethod, RefParameter);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(startContext =>
        {
            var supportedTypes = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
            foreach (var clrType in TinyType.ClrTypes.Keys)
            {
                var symbol = ResolveType(startContext.Compilation, clrType);
                if (symbol is not null)
                    supportedTypes.Add(symbol);
            }

            startContext.RegisterSymbolAction(methodContext => AnalyzeMethod(methodContext, supportedTypes), SymbolKind.Method);
        });
    }

    private static void AnalyzeMethod(SymbolAnalysisContext context, HashSet<ITypeSymbol> supportedTypes)
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
            if (parameter.RefKind is RefKind.Ref or RefKind.In)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    RefParameter,
                    parameter.Locations.FirstOrDefault() ?? method.Locations[0],
                    parameter.Name,
                    parameter.RefKind.ToString().ToLowerInvariant()));
                continue;
            }

            if (!IsAllowedParameter(parameter, supportedTypes))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    UnsupportedParameter,
                    parameter.Locations.FirstOrDefault() ?? method.Locations[0],
                    parameter.Name,
                    parameter.Type.ToDisplayString()));
            }
        }

        if (method.IsAsync || IsAsyncReturn(method.ReturnType))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                AsyncMethod,
                method.Locations[0],
                method.Name));
            return;
        }

        if (method.ReturnType.SpecialType != SpecialType.System_Void)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                UnsupportedReturn,
                method.Locations[0],
                method.Name));
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

    private static bool IsAllowedParameter(IParameterSymbol parameter, HashSet<ITypeSymbol> supportedTypes) =>
        supportedTypes.Contains(parameter.Type) ||
        parameter.RefKind != RefKind.Out && IsType(parameter.Type, "System.Threading.CancellationToken");

    // This only translates CLR type identities into compiler symbols; Core owns every allowed type/shape.
    private static ITypeSymbol? ResolveType(Compilation compilation, Type type)
    {
        if (type.IsArray)
        {
            var element = ResolveType(compilation, type.GetElementType()!);
            return element is null ? null : compilation.CreateArrayTypeSymbol(element, type.GetArrayRank());
        }

        if (type.IsGenericType)
        {
            var definition = compilation.GetTypeByMetadataName(type.GetGenericTypeDefinition().FullName!);
            var arguments = type.GetGenericArguments().Select(argument => ResolveType(compilation, argument)).ToArray();
            return definition is null || arguments.Any(argument => argument is null)
                ? null
                : definition.Construct(arguments!);
        }

        return compilation.GetTypeByMetadataName(type.FullName!);
    }

    private static bool IsType(ITypeSymbol type, string metadataName) =>
        string.Equals(type.OriginalDefinition.ToDisplayString(), metadataName, StringComparison.Ordinal) ||
        string.Equals(type.ToDisplayString(), metadataName, StringComparison.Ordinal);
}
