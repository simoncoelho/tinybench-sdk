using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using NUnit.Framework;
using TinyBench.Core;
using TinyBench.Sdk.Analyzers;
using TinyBench.Sdk.Core;

namespace TinyBench.Tests;

public class SignatureAnalyzerTests
{
    [TestCase("string", true)]
    [TestCase("bool", true)]
    [TestCase("int", true)]
    [TestCase("float", true)]
    [TestCase("double", true)]
    [TestCase("int?", true)]
    [TestCase("string[]", true)]
    [TestCase("bool[]", true)]
    [TestCase("int[]", true)]
    [TestCase("float[]", true)]
    [TestCase("double[]", true)]
    [TestCase("TinyTable", true)]
    [TestCase("TinyTable[]", false)]
    [TestCase("int[,]", false)]
    [TestCase("int[][]", false)]
    [TestCase("int?[]", false)]
    [TestCase("object[]", false)]
    [TestCase("decimal", false)]
    public async Task InputsAndOutputsFollowTheValueModel(string type, bool allowed)
    {
        var source = $$"""
            using TinyBench.Core;
            using TinyBench.Sdk.Core;
            [TinyDriver]
            public class Driver
            {
                [TinyCommand]
                public void Echo({{type}} value, out {{type}} result) { result = value; }
            }
            """;
        var diagnostics = await Analyze(source);
        Assert.That(diagnostics.Select(item => item.Id),
            allowed ? Is.Empty : Is.EqualTo(new[] { "TBSDK001", "TBSDK001" }));
    }

    [Test]
    public async Task AsyncVoidIsNotASynchronousCommand()
    {
        var diagnostics = await Analyze("""
            using TinyBench.Sdk.Core;
            public class Driver { [TinyCommand] public async void Run() { await System.Threading.Tasks.Task.Yield(); } }
            """);
        Assert.That(diagnostics.Select(item => item.Id), Is.EqualTo(new[] { "TBSDK004" }));
    }

    private static async Task<ImmutableArray<Diagnostic>> Analyze(string source)
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Append(typeof(TinyTable).Assembly.Location)
            .Append(typeof(TinyCommandAttribute).Assembly.Location)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create("SignatureTest", [CSharpSyntaxTree.ParseText(source)], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.That(compilation.GetDiagnostics().Where(item => item.Severity == DiagnosticSeverity.Error), Is.Empty);
        return await compilation.WithAnalyzers([new TinySignatureAnalyzer()]).GetAnalyzerDiagnosticsAsync();
    }
}
