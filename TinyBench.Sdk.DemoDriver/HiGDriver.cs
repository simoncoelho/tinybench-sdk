using TinyBench.Sdk.Core;

namespace TinyBench.Sdk.DemoDriver;

[TinyDriver("bionex-hig")]
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

    [TinyCommand("open-door")]
    public bool OpenDoor()
    {
        EnsureConnected();
        doorOpen = true;
        return doorOpen;
    }

    [TinyCommand("close-door")]
    public bool CloseDoor()
    {
        EnsureConnected();
        doorOpen = false;
        return doorOpen;
    }

    [TinyCommand("load-rotor")]
    public bool LoadRotor()
    {
        EnsureConnected();
        if (!doorOpen)
        {
            throw new InvalidOperationException("Open the door before loading the rotor.");
        }

        rotorLoaded = true;
        return rotorLoaded;
    }

    [TinyCommand("spin")]
    public string[] Spin(
        int targetRcf,
        int durationSeconds,
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

        return [$"targetRcf={targetRcf}", $"durationSeconds={durationSeconds}", "completed=true"];
    }

    [TinyCommand("read-status")]
    public string[] ReadStatus() =>
    [
        $"connected={connected}",
        $"doorOpen={doorOpen}",
        $"rotorLoaded={rotorLoaded}",
        $"spinning={spinning}",
        $"speedRcf={speedRcf}"
    ];

    private void EnsureConnected()
    {
        if (!connected)
        {
            throw new InvalidOperationException("Connect to the HiG before running commands.");
        }
    }
}
