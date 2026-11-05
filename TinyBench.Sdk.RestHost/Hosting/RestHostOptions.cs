namespace TinyBench.Sdk.RestHost.Hosting;

public sealed record RestHostOptions(string? DriverAssemblyPath, string? DocsPath, bool ShowHelp)
{
    public static RestHostOptions Parse(string[] args)
    {
        string? driverAssembly = null;
        string? docs = null;
        var showHelp = false;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--help" or "-h":
                    showHelp = true;
                    break;
                case "--driver-assembly":
                    driverAssembly = args[++i];
                    break;
                case "--docs":
                    docs = args[++i];
                    break;
            }
        }

        return new RestHostOptions(driverAssembly, docs, showHelp);
    }

    public static void PrintHelp()
    {
        Console.WriteLine("TinyBench SDK REST host");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  dotnet run --project TinyBench.Sdk.RestHost -- --driver-assembly <driver.dll> --docs <driver/.tinybench>");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --driver-assembly <path>  Compiled driver DLL to host.");
        Console.WriteLine("  --docs <path>             TinyBench docs folder. Defaults to <driver-dll-folder>/.tinybench.");
    }
}
