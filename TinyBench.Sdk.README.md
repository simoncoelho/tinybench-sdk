# TinyBench SDK

This is the lightweight authoring path for TinyBench drivers.

The driver author writes plain C# business logic, marks the driver and exposed methods with small TinyBench attributes, then runs the scaffold tool to create editable documentation files in the driver project.

## Projects

- `TinyBench.Core`: shared scalar, array, and table value model.
- `TinyBench.Sdk.Core`: driver attributes and C# mappings to the shared model.
- `TinyBench.Sdk.Analyzers`: .NET 10 compiler checks using Core's shared type registry.
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
- `TinyBench.Core.TinyTable` for typed scalar tables, with nullability declared per column
- `CancellationToken` as an injected parameter
- `void` return type
- `out` parameters for command outputs

Both `float` and `double` map to the shared `Float` kind; `int` maps to `Integer`.
Arrays are one-dimensional and contain non-null scalar elements. Tables enforce
their column types, row widths, and per-column nullability. See
[TinyBench.Core](TinyBench.Core/README.md) for examples and migration notes.

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
