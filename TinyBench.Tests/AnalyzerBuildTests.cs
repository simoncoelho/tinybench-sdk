using System.Diagnostics;
using System.Reflection;
using System.Xml.Linq;
using NUnit.Framework;

namespace TinyBench.Tests;

public class AnalyzerBuildTests
{
    [TestCase("TinyBench.Core.TinyTable", true)]
    [TestCase("decimal", false)]
    public async Task CompilerLoadsAnalyzerAndItsCoreDependency(string type, bool valid)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "TinyBench.slnx")))
            root = root.Parent;
        Assert.That(root, Is.Not.Null, "Could not locate the repository for project-reference integration testing.");

        var directory = Path.Combine(Path.GetTempPath(), "tinybench-analyzer-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var projectPath = Path.Combine(directory, "Consumer.csproj");
            new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"),
                new XElement("PropertyGroup",
                    new XElement("TargetFramework", "net10.0")),
                new XElement("ItemGroup",
                    new XElement("ProjectReference", new XAttribute("Include",
                        Path.Combine(root!.FullName, "TinyBench.Sdk.Core", "TinyBench.Sdk.Core.csproj"))),
                    new XElement("ProjectReference", new XAttribute("Include",
                        Path.Combine(root.FullName, "TinyBench.Sdk.Analyzers", "TinyBench.Sdk.Analyzers.csproj")),
                        new XAttribute("OutputItemType", "Analyzer"), new XAttribute("ReferenceOutputAssembly", "false"))))
                .Save(projectPath);
            File.WriteAllText(Path.Combine(directory, "Driver.cs"), $$"""
                using TinyBench.Sdk.Core;
                [TinyDriver]
                public class Driver
                {
                    [TinyCommand]
                    public void Echo({{type}} value, out {{type}} result) { result = value; }
                }
                """);

            var startInfo = new ProcessStartInfo("dotnet")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            var configuration = typeof(AnalyzerBuildTests).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()!.Configuration;
            foreach (var argument in new[] { "build", projectPath, "--nologo", "--configuration", configuration,
                         "-p:BuildProjectReferences=false", "-p:UseSharedCompilation=false", "-warnaserror" })
                startInfo.ArgumentList.Add(argument);
            using var process = Process.Start(startInfo)!;
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw; }
            var log = (await output) + (await error);
            Assert.That(log, Does.Not.Contain("AD0001").And.Not.Contain("CS8032"), log);
            if (valid)
                Assert.That(process.ExitCode, Is.Zero, log);
            else
            {
                Assert.That(process.ExitCode, Is.Not.Zero, log);
                Assert.That(log, Does.Contain("error TBSDK001"), log);
            }
        }
        finally
        {
            // Delete only the unique directory created by this test directly under the system temp directory.
            if (Path.GetDirectoryName(Path.GetFullPath(directory)) == Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar))
                Directory.Delete(directory, recursive: true);
        }
    }
}
