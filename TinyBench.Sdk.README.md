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
[TinyDriver("bionex-hig")]
public sealed class HiGDriver
{
    [TinyConnect]
    public void Connect(string host, int port) { }

    [TinyDisconnect]
    public void Disconnect() { }

    [TinyCommand("open-door")]
    public bool OpenDoor() => true;
}
```

Exposed methods may only use the TinyBench value language:

- `string`
- `int`
- `float`, `double`
- `bool`
- arrays of `string`, `int`, `float`, `double`, or `bool`
- `CancellationToken` as an injected parameter
- `void`, `Task`, `ValueTask`, `Task<T>`, or `ValueTask<T>` return shapes

## Generated Docs

Build the driver, then scaffold docs:

```powershell
dotnet build .\TinyBench.Sdk.DemoDriver\TinyBench.Sdk.DemoDriver.csproj
dotnet run --project .\TinyBench.Sdk.Tools\TinyBench.Sdk.Tools.csproj -- --project .\TinyBench.Sdk.DemoDriver
```

The tool writes docs into the driver project:

```text
TinyBench.Sdk.DemoDriver/
  .tinybench/
    driver.yaml
    connection.yaml
    commands/
      open-door.yaml
      spin.yaml
```

Existing YAML files are not overwritten. The intent is that generated docs become human-owned files, keeping documentation separate from driver code.

Use `--force` only when you want to regenerate scaffold files after changing method signatures:

```powershell
dotnet run --project .\TinyBench.Sdk.Tools\TinyBench.Sdk.Tools.csproj -- --project .\TinyBench.Sdk.DemoDriver --force
```
