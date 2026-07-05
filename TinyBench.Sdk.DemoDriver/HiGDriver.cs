using TinyBench.Sdk.Core;

namespace TinyBench.Sdk.DemoDriver;

[TinyDriver]
public sealed class HiGDriver
{
    private bool connected;
    private bool doorOpen;
    private bool rotorLoaded;
    private bool spinning;
    private int speedRcf;

    [TinyConnect]
    public void Connect(string host, int port, string? serialNumber = null)
    {
        connected = true;
    }

    [TinyDisconnect]
    public void Disconnect()
    {
        connected = false;
        spinning = false;
    }

    [TinyCommand]
    public void OpenDoor(out bool doorOpen)
    {
        EnsureConnected();
        this.doorOpen = true;
        doorOpen = this.doorOpen;
    }

    [TinyCommand]
    public void CloseDoor(out bool doorOpen)
    {
        EnsureConnected();
        this.doorOpen = false;
        doorOpen = this.doorOpen;
    }

    [TinyCommand]
    public void LoadRotor(out bool rotorLoaded)
    {
        EnsureConnected();
        if (!doorOpen)
        {
            throw new InvalidOperationException("Open the door before loading the rotor.");
        }

        this.rotorLoaded = true;
        rotorLoaded = this.rotorLoaded;
    }

    [TinyCommand]
    public void Spin(
        int targetRcf,
        int durationSeconds,
        out string[] spinResult,
        CancellationToken cancellationToken = default)
    {
        EnsureConnected();
        if (doorOpen)
        {
            throw new InvalidOperationException("Close the door before spinning.");
        }

        if (!rotorLoaded)
        {
            throw new InvalidOperationException("Load the rotor before spinning.");
        }

        spinning = true;
        speedRcf = targetRcf;
        Thread.Sleep(TimeSpan.FromMilliseconds(Math.Min(durationSeconds, 1) * 100));
        cancellationToken.ThrowIfCancellationRequested();
        spinning = false;

        spinResult = [$"targetRcf={targetRcf}", $"durationSeconds={durationSeconds}", "completed=true"];
    }

    [TinyCommand]
    public void ReadStatus(out string[] status)
    {
        status =
        [
            $"connected={connected}",
            $"doorOpen={doorOpen}",
            $"rotorLoaded={rotorLoaded}",
            $"spinning={spinning}",
            $"speedRcf={speedRcf}"
        ];
    }

    private void EnsureConnected()
    {
        if (!connected)
        {
            throw new InvalidOperationException("Connect to the HiG before running commands.");
        }
    }
}
