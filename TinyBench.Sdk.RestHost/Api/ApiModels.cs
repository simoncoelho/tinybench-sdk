using System.Text.Json;

namespace TinyBench.Sdk.RestHost.Api;

public sealed record ConnectRequest(Dictionary<string, JsonElement>? Parameters);

public sealed record StartRunRequest(Dictionary<string, JsonElement>? Inputs);

public sealed record ApiError(string Error, string Detail);
