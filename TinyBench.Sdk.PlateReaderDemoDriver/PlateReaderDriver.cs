using TinyBench.Sdk.Core;
using TinyBench.Core;

namespace TinyBench.Sdk.PlateReaderDemoDriver;

[TinyDriver]
public sealed class PlateReaderDriver
{
    private bool connected;
    private double temperatureCelsius = 22.0;
    private string lastPlate = "none";

    [TinyConnect]
    public void Connect(string host, int port, string? instrumentId = null)
    {
        connected = true;
    }

    [TinyDisconnect]
    public void Disconnect()
    {
        connected = false;
    }

    [TinyCommand]
    public void SetTemperature(double targetCelsius, out double actualCelsius)
    {
        EnsureConnected();
        temperatureCelsius = targetCelsius;
        actualCelsius = temperatureCelsius;
    }

    [TinyCommand]
    public void ReadAbsorbance(string plateId, int wavelengthNm, out TinyTable measurements)
    {
        EnsureConnected();
        lastPlate = plateId;
        measurements = new TinyTable(
            [
                new("plateId", TinyValueKind.String),
                new("wavelengthNm", TinyValueKind.Integer),
                new("well", TinyValueKind.String),
                new("absorbance", TinyValueKind.Float, isNullable: true)
            ],
            [
                [plateId, wavelengthNm, "A1", 0.126],
                [plateId, wavelengthNm, "A2", 0.133],
                [plateId, wavelengthNm, "B1", 0.119],
                [plateId, wavelengthNm, "B2", null]
            ]);
    }

    [TinyCommand]
    public void ReadStatus(out string[] status)
    {
        status =
        [
            $"connected={connected}",
            $"temperatureCelsius={temperatureCelsius}",
            $"lastPlate={lastPlate}"
        ];
    }

    private void EnsureConnected()
    {
        if (!connected)
        {
            throw new InvalidOperationException("Connect to the plate reader before running commands.");
        }
    }
}
