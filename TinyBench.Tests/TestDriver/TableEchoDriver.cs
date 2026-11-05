using TinyBench.Core;
using TinyBench.Sdk.Core;

namespace TinyBench.Tests.Fixtures;

[TinyDriver]
public sealed class TableEchoDriver
{
    [TinyConnect]
    public void Connect() { }

    [TinyDisconnect]
    public void Disconnect() { }

    [TinyCommand]
    public void Echo(TinyTable input, double scale, out TinyTable result) => result = input;

    [TinyCommand]
    public void InvalidCells(out string[] result) => result = [null!];

    [TinyCommand]
    public void InvalidFloat(out double result) => result = double.NaN;

    [TinyCommand]
    public void NullTable(out TinyTable result) => result = null!;
}
