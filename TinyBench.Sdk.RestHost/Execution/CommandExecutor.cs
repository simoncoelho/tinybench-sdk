using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using TinyBench.Sdk.Core;
using TinyBench.Sdk.RestHost.Driver;
using TinyBench.Sdk.RestHost.Services;

namespace TinyBench.Sdk.RestHost.Execution;

public sealed class CommandExecutor(LoadedDriver driver)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly ConcurrentDictionary<string, CommandRun> runs = new(StringComparer.OrdinalIgnoreCase);
    private int runSequence;

    public IReadOnlyCollection<CommandRunSnapshot> GetRuns() =>
        runs.Values
            .OrderByDescending(run => run.CreatedAt)
            .Select(run => run.ToSnapshot())
            .ToArray();

    public CommandRunSnapshot? GetRun(string runId) =>
        runs.TryGetValue(runId, out var run) ? run.ToSnapshot() : null;

    public async Task<StartRunResult> TryStartAsync(string commandId, IReadOnlyDictionary<string, JsonElement> inputs)
    {
        if (!driver.Commands.TryGetValue(commandId, out var command))
        {
            return StartRunResult.NotFound($"Unknown command '{commandId}'.");
        }

        if (!await gate.WaitAsync(0))
        {
            return StartRunResult.Conflict("A command is already running.");
        }

        var runId = $"run-{Interlocked.Increment(ref runSequence):0000}";
        var run = new CommandRun(runId, command.Metadata.Id, DateTimeOffset.UtcNow);
        runs[runId] = run;
        _ = Task.Run(() => ExecuteAsync(command, run, inputs));
        return StartRunResult.Started(run.ToSnapshot());
    }

    public bool Cancel(string runId)
    {
        if (!runs.TryGetValue(runId, out var run))
        {
            return false;
        }

        run.RequestCancel();
        return true;
    }

    private async Task ExecuteAsync(LoadedCommand command, CommandRun run, IReadOnlyDictionary<string, JsonElement> inputs)
    {
        try
        {
            run.MarkRunning();
            var parameters = command.Method.GetParameters();
            var arguments = BuildArguments(parameters, inputs, run.CancellationToken);

            await Task.Run(() => command.Method.Invoke(driver.Instance, arguments), run.CancellationToken);

            var outputs = ReadOutputs(parameters, arguments);
            run.MarkCompleted(outputs);
        }
        catch (OperationCanceledException)
        {
            run.MarkCanceled();
        }
        catch (TargetInvocationException ex) when (ex.InnerException is OperationCanceledException)
        {
            run.MarkCanceled();
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            run.MarkFailed(ex.InnerException.Message);
        }
        catch (Exception ex)
        {
            run.MarkFailed(ex.Message);
        }
        finally
        {
            gate.Release();
        }
    }

    private static object?[] BuildArguments(ParameterInfo[] parameters, IReadOnlyDictionary<string, JsonElement> inputs, CancellationToken cancellationToken)
    {
        var arguments = new object?[parameters.Length];
        for (var i = 0; i < parameters.Length; i++)
        {
            var parameter = parameters[i];
            if (parameter.IsOut)
            {
                arguments[i] = null;
                continue;
            }

            if (parameter.ParameterType == typeof(CancellationToken))
            {
                arguments[i] = cancellationToken;
                continue;
            }

            if (parameter.Name is not null && inputs.TryGetValue(parameter.Name, out var value))
            {
                arguments[i] = TinyJsonInputs.Convert(parameter.ParameterType, value);
                continue;
            }

            if (parameter.HasDefaultValue)
            {
                arguments[i] = parameter.DefaultValue;
                continue;
            }

            throw new InvalidOperationException($"Missing required input '{parameter.Name}'.");
        }

        return arguments;
    }

    private static Dictionary<string, object?> ReadOutputs(ParameterInfo[] parameters, object?[] arguments)
    {
        var outputs = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < parameters.Length; i++)
        {
            if (parameters[i].IsOut)
            {
                var outputType = parameters[i].ParameterType.GetElementType()!;
                if (!TinyTypeRules.AcceptsValue(outputType, arguments[i]))
                    throw new InvalidOperationException($"Output '{parameters[i].Name}' does not match the TinyBench value model.");
                outputs[parameters[i].Name ?? $"output{i}"] = arguments[i];
            }
        }

        return outputs;
    }

}

public sealed record StartRunResult(bool IsStarted, bool IsNotFound, string? Error, CommandRunSnapshot? Run)
{
    public static StartRunResult Started(CommandRunSnapshot run) => new(true, false, null, run);

    public static StartRunResult NotFound(string error) => new(false, true, error, null);

    public static StartRunResult Conflict(string error) => new(false, false, error, null);
}
