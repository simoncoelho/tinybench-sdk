# TinyBench SDK

This is the lightweight authoring path for TinyBench drivers.

The driver author writes plain C# business logic, marks the driver and exposed methods with small TinyBench attributes, then runs the scaffold tool to create editable documentation files in the driver project.

## Projects

- `TinyBench.Sdk.Core`: attributes and tiny value types used by driver libraries.
- `TinyBench.Sdk.Analyzers`: compile-time checks for exposed method signatures.
- `TinyBench.Sdk.Tools`: scaffolds YAML docs from an attributed driver assembly.
- `TinyBench.Sdk.DemoDriver`: HiG demo driver using the SDK.

## Driver Authoring

```csharp
[TinyDriver]
public sealed class HiGDriver
{
    [TinyConnect]
    public void Connect(string host, int port) { }

    [TinyDisconnect]
    public void Disconnect() { }

    [TinyCommand]
    public void OpenDoor(out bool doorOpen)
    {
        doorOpen = true;
    }
}
```

Exposed methods must be `public` and synchronous. They may only use the TinyBench value language:

- `string`
- `int`
- `float`, `double`
- `bool`
- arrays of `string`, `int`, `float`, `double`, or `bool`
- `CancellationToken` as an injected parameter
- `void` return type
- `out` parameters for command outputs

## Synced Docs

Build the driver, then sync docs:

```powershell
dotnet build .\TinyBench.Sdk.DemoDriver\TinyBench.Sdk.DemoDriver.csproj
dotnet run --project .\TinyBench.Sdk.Tools\TinyBench.Sdk.Tools.csproj -- --project .\TinyBench.Sdk.DemoDriver
```

The tool writes and keeps docs current in the driver project:

```text
TinyBench.Sdk.DemoDriver/
  .tinybench/
    driver.yaml
    connection.yaml
    commands/
      open-door.yaml
      spin.yaml
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
