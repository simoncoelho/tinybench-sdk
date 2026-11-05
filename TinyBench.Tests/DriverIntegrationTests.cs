using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using NUnit.Framework;
using TinyBench.Core;
using TinyBench.Sdk.RestHost.Driver;
using TinyBench.Sdk.RestHost.Execution;
using TinyBench.Sdk.RestHost.Services;
using TinyBench.Tests.Fixtures;

namespace TinyBench.Tests;

public class DriverIntegrationTests
{
    [Test]
    public async Task ScaffoldedDriverCanReceiveAndReturnTablesAcrossTheLoadContext()
    {
        var docs = Path.Combine(Path.GetTempPath(), "tinybench-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(docs);
        try
        {
            await Scaffold(docs);
            var commandPath = Path.Combine(docs, "commands", "Echo.yaml");
            var yaml = File.ReadAllText(commandPath);
            Assert.That(yaml, Does.Contain("type: \"Table\""));
            Assert.That(yaml, Does.Contain("type: \"Float\""));
            Assert.That(yaml, Does.Not.Contain("type: \"Double\""));
            File.WriteAllText(commandPath, yaml.Replace("displayName: \"TODO\"", "displayName: \"Preserved label\""));
            await Scaffold(docs);
            Assert.That(File.ReadAllText(commandPath), Does.Contain("displayName: \"Preserved label\""));

            var driver = DriverLoader.Load(typeof(TableEchoDriver).Assembly.Location, docs);
            Assert.That(driver.Commands["Echo"].Method.GetParameters()[0].ParameterType, Is.SameAs(typeof(TinyTable)));
            new ConnectionInvoker(driver).Connect(new Dictionary<string, JsonElement>());
            var executor = new CommandExecutor(driver);
            using var input = JsonDocument.Parse("""
                {"columns":[{"name":"signal","kind":"Float","isNullable":true}],"rows":[[0.25],[null]]}
                """);
            var started = await executor.TryStartAsync("Echo", new Dictionary<string, JsonElement>
            {
                ["input"] = input.RootElement.Clone(),
                ["scale"] = JsonSerializer.SerializeToElement(1.0)
            });
            Assert.That(started.IsStarted, Is.True);
            var run = started.Run!;
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (run.Status is "Queued" or "Running" && DateTime.UtcNow < deadline)
            {
                await Task.Delay(10);
                run = executor.GetRun(run.Id)!;
            }
            Assert.That(run.Status, Is.EqualTo("Completed"), run.Error);
            var output = (TinyTable)run.Outputs["result"]!;
            Assert.That(output.Rows[0][0], Is.EqualTo(0.25));
            Assert.That(output.Rows[1][0], Is.Null);
            Assert.That(output.Columns[0].IsNullable, Is.True);

            foreach (var command in new[] { "InvalidCells", "InvalidFloat", "NullTable" })
            {
                var invalid = await executor.TryStartAsync(command, new Dictionary<string, JsonElement>());
                Assert.That(invalid.IsStarted, Is.True);
                var invalidRun = invalid.Run!;
                var invalidDeadline = DateTime.UtcNow.AddSeconds(10);
                while (invalidRun.Status is "Queued" or "Running" && DateTime.UtcNow < invalidDeadline)
                {
                    await Task.Delay(10);
                    invalidRun = executor.GetRun(invalidRun.Id)!;
                }
                Assert.That(invalidRun.Status, Is.EqualTo("Failed"));
                Assert.That(invalidRun.Error, Does.Contain("does not match the TinyBench value model"));
            }
        }
        finally
        {
            // This directory is uniquely created by this test under the system temp directory.
            if (Path.GetDirectoryName(Path.GetFullPath(docs)) == Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar))
                Directory.Delete(docs, recursive: true);
        }
    }

    private static async Task Scaffold(string docs)
    {
        var toolPath = Assembly.Load("TinyBench.Sdk.Tools").Location;
        var startInfo = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add(toolPath);
        startInfo.ArgumentList.Add("--assembly");
        startInfo.ArgumentList.Add(typeof(TableEchoDriver).Assembly.Location);
        startInfo.ArgumentList.Add("--output");
        startInfo.ArgumentList.Add(docs);
        using var process = Process.Start(startInfo)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw; }
        Assert.That(process.ExitCode, Is.Zero, (await output) + (await error));
    }
}
