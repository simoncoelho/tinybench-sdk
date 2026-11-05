# TinyBench SDK analyzers

The analyzer targets .NET 10 and directly references `TinyBench.Core`.
`TinyType.ClrTypes` defines the accepted C# representations. At compilation start,
the analyzer translates those registered CLR types into Roslyn symbols and uses
symbol equality to validate inputs and outputs. The SDK's runtime type checks
read the same registry. Adding a registered type in Core automatically makes it
available to both consumers; there is no second scalar or array allowlist here.

Method visibility, synchronous execution, `void` returns, parameter direction,
and injected `CancellationToken` handling remain SDK-specific analyzer rules.

## Compiler host

Use the .NET 10 SDK and a compiler/analyzer host running .NET 10 or a compatible
newer runtime. Merely targeting a driver at .NET 10 does not upgrade an IDE's
analyzer host. Older .NET Framework-based analyzer hosts are not supported.
The RS1041 cross-host compatibility warning is suppressed deliberately in the
analyzer project because .NET Standard compatibility is no longer a project goal.

## Project references

Consumers keep the existing reference:

```xml
<ProjectReference Include="..\TinyBench.Sdk.Analyzers\TinyBench.Sdk.Analyzers.csproj"
                  OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
```

`AddCoreAnalyzerDependency` adds the copied `TinyBench.Core.dll` implementation to
the analyzer project outputs returned by MSBuild. Roslyn receives both DLL paths;
a normal application reference alone does not register a compiler-plugin
dependency. This follows the dependency-output approach described in the
[Roslyn project-reference discussion](https://github.com/dotnet/roslyn/discussions/47517).

If this analyzer is later packaged as a NuGet package, include both implementation
DLLs in its analyzer assets. It is currently consumed through project references.

## Tests

The test suite checks every entry in Core's registry against both the SDK and
analyzer. Separate compiler-process tests build a temporary consumer using the
project reference above: a table signature must succeed, and a `decimal`
signature must fail with `TBSDK001`. Analyzer load errors fail these tests too.
