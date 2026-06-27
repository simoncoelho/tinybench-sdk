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

- `TinyBench.Sdk.Core`: tiny driver authoring attributes and value rules.
- `TinyBench.Sdk.Analyzers`: compile-time checks for exposed method signatures.
- `TinyBench.Sdk.Tools`: generates `.tinybench` YAML documentation from attributed driver assemblies.
- `TinyBench.Sdk.DemoDriver`: HiG demo driver using the SDK.

## Driver Authoring

```csharp
using TinyBench.Sdk.Core;

[TinyDriver("bionex-hig")]
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

    [TinyCommand("open-door")]
    public bool OpenDoor()
    {
        // Business logic stays here.
        return true;
    }

    [TinyCommand("spin")]
    public string[] Spin(int targetRcf, int durationSeconds, CancellationToken cancellationToken = default)
    {
        // Business logic stays here.
        return [$"targetRcf={targetRcf}", $"durationSeconds={durationSeconds}", "completed=true"];
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
- optional injected `CancellationToken`
- `void` or a direct scalar/array return value

The analyzer reports unsupported exposed signatures at compile time.

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
      open-door.yaml
      close-door.yaml
      load-rotor.yaml
      spin.yaml
      read-status.yaml
```

Each run syncs generated structure from the compiled driver:

- removed commands delete stale command YAML files
- renamed or added parameters update command YAML inputs
- changed return types update command YAML outputs
- editable metadata such as `displayName`, `description`, and `sila` fields is preserved when the matching command/input/output still exists

Use `--force` when you want to regenerate docs without preserving editable metadata:

```powershell
dotnet run --project .\TinyBench.Sdk.Tools\TinyBench.Sdk.Tools.csproj -- --project .\TinyBench.Sdk.DemoDriver --force
```

## Documentation Layer

Generated command YAML contains the information that should not live in driver code:

```yaml
id: "spin"
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
  - name: "result"
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
