using System.Text.Json;
using TinyBench.Sdk.RestHost.Driver;
using TinyBench.Sdk.RestHost.Execution;
using TinyBench.Sdk.RestHost.Services;

namespace TinyBench.Sdk.RestHost.Api;

public static class TinyBenchEndpoints
{
    public static WebApplication MapTinyBenchApi(this WebApplication app)
    {
        app.MapGet("/", () => Results.Redirect("/swagger"))
            .WithTags("Definition")
            .ExcludeFromDescription();

        app.MapGet("/v1/definition", (LoadedDriver driver) => driver.Definition)
            .WithTags("Definition")
            .WithSummary("Get the complete TinyBench driver definition.");

        app.MapGet("/v1/device", (LoadedDriver driver) => driver.Definition.Driver)
            .WithTags("Device")
            .WithSummary("Get device metadata.");

        app.MapGet("/v1/connection", (LoadedDriver driver) => driver.Definition.Connection)
            .WithTags("Connection")
            .WithSummary("Get connection metadata.");

        app.MapPost("/v1/connection/connect", (ConnectRequest request, ConnectionInvoker connection) =>
        {
            try
            {
                connection.Connect(request.Parameters ?? []);
                return Results.Ok(new { connected = true });
            }
            catch (TinyBenchRestException ex)
            {
                return ex.ToResult();
            }
        })
            .WithTags("Connection")
            .WithSummary("Connect the hosted driver.");

        app.MapPost("/v1/connection/disconnect", (ConnectionInvoker connection) =>
        {
            try
            {
                connection.Disconnect();
                return Results.Ok(new { connected = false });
            }
            catch (TinyBenchRestException ex)
            {
                return ex.ToResult();
            }
        })
            .WithTags("Connection")
            .WithSummary("Disconnect the hosted driver.");

        app.MapGet("/v1/commands", (LoadedDriver driver) => driver.Definition.Commands)
            .WithTags("Commands")
            .WithSummary("List commands exposed by the driver.");

        app.MapGet("/v1/commands/{commandId}", (string commandId, LoadedDriver driver) =>
        {
            return driver.Commands.TryGetValue(commandId, out var command)
                ? Results.Ok(command.Metadata)
                : Results.NotFound(new ApiError("command_not_found", $"Unknown command '{commandId}'."));
        })
            .WithTags("Commands")
            .WithSummary("Get one command definition.");

        app.MapPost("/v1/commands/{commandId}/runs", async (
            string commandId,
            StartRunRequest request,
            CommandExecutor executor) =>
        {
            var result = await executor.TryStartAsync(
                commandId,
                request.Inputs ?? new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase));

            if (result.IsStarted)
            {
                return Results.Accepted($"/v1/runs/{result.Run!.Id}", result.Run);
            }

            if (result.IsNotFound)
            {
                return Results.NotFound(new ApiError("command_not_found", result.Error ?? "Unknown command."));
            }

            return Results.Conflict(new ApiError("command_already_running", result.Error ?? "A command is already running."));
        })
            .WithTags("Runs")
            .WithSummary("Start a command run.");

        app.MapGet("/v1/runs", (CommandExecutor executor) => executor.GetRuns())
            .WithTags("Runs")
            .WithSummary("List command runs.");

        app.MapGet("/v1/runs/{runId}", (string runId, CommandExecutor executor) =>
        {
            var run = executor.GetRun(runId);
            return run is null
                ? Results.NotFound(new ApiError("run_not_found", $"Unknown run '{runId}'."))
                : Results.Ok(run);
        })
            .WithTags("Runs")
            .WithSummary("Get one command run.");

        app.MapPost("/v1/runs/{runId}/cancel", (string runId, CommandExecutor executor) =>
        {
            return executor.Cancel(runId)
                ? Results.Accepted($"/v1/runs/{runId}")
                : Results.NotFound(new ApiError("run_not_found", $"Unknown run '{runId}'."));
        })
            .WithTags("Runs")
            .WithSummary("Request command cancellation.");

        return app;
    }
}
