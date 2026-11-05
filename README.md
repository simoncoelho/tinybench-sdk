# TinyBench SDK

TinyBench SDK is a lightweight authoring path for device drivers.

The goal is to keep driver code plain and small:

1. Write a stateful C# class for the device.
2. Add `[TinyDriver]` to the driver class.
3. Add `[TinyConnect]`, `[TinyDisconnect]`, and `[TinyCommand]` to the methods you want tooling to expose.
4. Keep exposed methods public, synchronous, and inside TinyBench's small value language.
5. Run the scaffold tool to create editable docs in `.tinybench`.

The driver should not need to know about SiLA, FDL, protobuf, or gRPC. SiLA-facing metadata lives in generated YAML files that can be edited separately from the driver implementation.

## Projects

- `TinyBench.Core`: shared scalar, array, and table value model used by the SDK and platform.
- `TinyBench.Sdk.Core`: driver authoring attributes and C# mappings to the shared value model.
- `TinyBench.Sdk.Analyzers`: .NET 10 compiler checks for exposed signatures, using Core's shared type registry.
- `TinyBench.Sdk.Tools`: generates `.tinybench` YAML documentation from attributed driver assemblies.
- `TinyBench.Sdk.RestHost`: wraps a compiled TinyBench driver DLL with a REST API.
- `TinyBench.Workbench`: React Flow node editor for adding REST-hosted devices and running simple workflows.
- `TinyBench.Sdk.DemoDriver`: HiG demo driver using the SDK.
- `TinyBench.Sdk.PlateReaderDemoDriver`: second demo driver for multi-device workflow testing.

## Driver Authoring

```csharp
using TinyBench.Sdk.Core;

[TinyDriver]
public sealed class HiGDriver
{
    [TinyConnect]
    public void Connect(string host, int port, string? serialNumber = null)
    {
        // Open vendor SDK / hardware connection.
    }

    [TinyDisconnect]
    public void Disconnect()
    {
        // Close vendor SDK / hardware connection.
    }

    [TinyCommand]
    public void OpenDoor(out bool doorOpen)
    {
        // Business logic stays here.
        doorOpen = true;
    }

    [TinyCommand]
    public void Spin(
        int targetRcf,
        int durationSeconds,
        out string[] spinResult,
        CancellationToken cancellationToken = default)
    {
        // Business logic stays here.
        spinResult = [$"targetRcf={targetRcf}", $"durationSeconds={durationSeconds}", "completed=true"];
    }
}
```

## Allowed Method Types

Exposed TinyBench methods must be `public` and synchronous. They may only use:

- `string`
- `bool`
- `int`
- `double`
- `float`
- arrays of `string`, `bool`, `int`, `double`, or `float`
- `TinyBench.Core.TinyTable` for tables with typed scalar columns and explicit per-column nullability
- optional injected `CancellationToken`
- `void` return type
- `out` parameters for command outputs

The analyzer reports unsupported exposed signatures at compile time.

The SDK and analyzer both read `TinyType.ClrTypes` from Core. The analyzer requires
a .NET 10-compatible compiler host; see [analyzer setup](TinyBench.Sdk.Analyzers/README.md).

The shared model distinguishes `Integer` from `Float`; both C# `float` and `double`
map to `Float`. Arrays must be one-dimensional scalar arrays. Table cells may be
null only in columns declared nullable; arrays and nested tables cannot be cells.
See [the Core value model](TinyBench.Core/README.md) for the contract, table examples,
and migration details. The plate-reader demo returns a table from `ReadAbsorbance`.

## Generate Docs

Build the driver first:

```powershell
dotnet build .\TinyBench.Sdk.DemoDriver\TinyBench.Sdk.DemoDriver.csproj
```

Sync docs:

```powershell
dotnet run --project .\TinyBench.Sdk.Tools\TinyBench.Sdk.Tools.csproj -- --project .\TinyBench.Sdk.DemoDriver
```

The tool writes and keeps docs current inside the driver project:

```text
TinyBench.Sdk.DemoDriver/
  .tinybench/
    driver.yaml
    connection.yaml
    commands/
      OpenDoor.yaml
      CloseDoor.yaml
      LoadRotor.yaml
      spin.yaml
      ReadStatus.yaml
```

Each run syncs generated structure from the compiled driver:

- removed commands delete stale command YAML files
- renamed or added parameters update command YAML inputs
- changed `out` parameters update command YAML outputs
- editable metadata such as `displayName`, `description`, and `sila` fields is preserved when the matching command/input/output still exists

Use `--force` when you want to regenerate docs without preserving editable metadata:

```powershell
dotnet run --project .\TinyBench.Sdk.Tools\TinyBench.Sdk.Tools.csproj -- --project .\TinyBench.Sdk.DemoDriver --force
```

## Documentation Layer

Generated command YAML contains the information that should not live in driver code:

```yaml
id: "Spin"
method: "Spin"
displayName: TODO
description: TODO
inputs:
  - name: "targetRcf"
    type: "Integer"
    array: false
    displayName: TODO
    description: TODO
    required: true
outputs:
  - name: "spinResult"
    type: "String"
    array: true
    displayName: TODO
    description: TODO
sila:
  feature: TODO
  command: TODO
  description: TODO
```

This keeps implementation code and external interface documentation separate, while still letting tooling generate SiLA-facing files later.

## REST Host

Build a driver, sync its `.tinybench` docs, then point the REST host at the compiled driver DLL:

```powershell
dotnet run --project .\TinyBench.Sdk.RestHost\TinyBench.Sdk.RestHost.csproj -- `
  --driver-assembly .\TinyBench.Sdk.DemoDriver\bin\Debug\net10.0\TinyBench.Sdk.DemoDriver.dll `
  --docs .\TinyBench.Sdk.DemoDriver\.tinybench
```

The host exposes the driver through a small REST API:

- `GET /v1/definition`
- `GET /v1/device`
- `GET /v1/connection`
- `POST /v1/connection/connect`
- `POST /v1/connection/disconnect`
- `GET /v1/commands`
- `GET /v1/commands/{commandId}`
- `POST /v1/commands/{commandId}/runs`
- `GET /v1/runs`
- `GET /v1/runs/{runId}`
- `POST /v1/runs/{runId}/cancel`

Only one command run is allowed at a time by default. If another command is already running, new run requests return `409 Conflict`.

Swagger is available at `/swagger` when the REST host is running.

## Workbench

Run one REST host per available device:

```powershell
dotnet run --project .\TinyBench.Sdk.RestHost\TinyBench.Sdk.RestHost.csproj --urls http://localhost:5093 -- `
  --driver-assembly .\TinyBench.Sdk.DemoDriver\bin\Debug\net10.0\TinyBench.Sdk.DemoDriver.dll `
  --docs .\TinyBench.Sdk.DemoDriver\.tinybench

dotnet run --project .\TinyBench.Sdk.RestHost\TinyBench.Sdk.RestHost.csproj --urls http://localhost:5094 -- `
  --driver-assembly .\TinyBench.Sdk.PlateReaderDemoDriver\bin\Debug\net10.0\TinyBench.Sdk.PlateReaderDemoDriver.dll `
  --docs .\TinyBench.Sdk.PlateReaderDemoDriver\.tinybench
```

Then start the React Flow workbench:

```powershell
cd .\TinyBench.Workbench
npm install
npm run dev
```

Open `http://127.0.0.1:5089`. Add the available devices you want in the system, connect them, drag command nodes or variable nodes onto the graph, then connect variable outputs to command input handles or command output handles back into variables. Workflows run command nodes from top to bottom and show final outputs on each node.
