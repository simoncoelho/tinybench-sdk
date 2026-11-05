namespace TinyBench.Sdk.RestHost.Metadata;

public sealed record TinyBenchDefinition(
    TinyDriverDocument Driver,
    TinyConnectionDocument Connection,
    IReadOnlyList<TinyCommandDocument> Commands);

public sealed record TinyDriverDocument(
    string Id,
    string Class,
    string DisplayName,
    string Manufacturer,
    string Model,
    string Version,
    string Description,
    IReadOnlyList<string> RequiredSoftware,
    string SetupInstructions);

public sealed record TinyConnectionDocument(
    string ConnectMethod,
    string DisconnectMethod,
    IReadOnlyList<TinyParameterDocument> Parameters);

public sealed record TinyCommandDocument(
    string Id,
    string Method,
    string DisplayName,
    string Description,
    IReadOnlyList<TinyParameterDocument> Inputs,
    IReadOnlyList<TinyParameterDocument> Outputs,
    TinySilaDocument Sila);

public sealed record TinyParameterDocument(
    string Name,
    string Type,
    bool Array,
    string DisplayName,
    string Description,
    bool Required,
    string? Default);

public sealed record TinySilaDocument(string Feature, string Command, string Description);
