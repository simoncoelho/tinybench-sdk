namespace TinyBench.Sdk.RestHost.Metadata;

public static class TinyBenchDefinitionReader
{
    public static TinyBenchDefinition Read(string docsPath)
    {
        var driver = ReadDriver(Path.Combine(docsPath, "driver.yaml"));
        var connection = ReadConnection(Path.Combine(docsPath, "connection.yaml"));
        var commandsPath = Path.Combine(docsPath, "commands");
        var commands = Directory.Exists(commandsPath)
            ? Directory.EnumerateFiles(commandsPath, "*.yaml")
                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .Select(ReadCommand)
                .ToArray()
            : [];

        return new TinyBenchDefinition(driver, connection, commands);
    }

    private static TinyDriverDocument ReadDriver(string path)
    {
        var document = TinyYaml.Read(path);
        return new TinyDriverDocument(
            document.Value("id"),
            document.Value("class"),
            document.Value("displayName"),
            document.Value("manufacturer"),
            document.Value("model"),
            document.Value("version"),
            document.Value("description"),
            document.List("requiredSoftware"),
            document.Value("setupInstructions"));
    }

    private static TinyConnectionDocument ReadConnection(string path)
    {
        var document = TinyYaml.Read(path);
        return new TinyConnectionDocument(
            document.Value("connectMethod"),
            document.Value("disconnectMethod"),
            document.Parameters("parameters"));
    }

    private static TinyCommandDocument ReadCommand(string path)
    {
        var document = TinyYaml.Read(path);
        var sila = document.NestedValues("sila");
        return new TinyCommandDocument(
            document.Value("id"),
            document.Value("method"),
            document.Value("displayName"),
            document.Value("description"),
            document.Parameters("inputs"),
            document.Parameters("outputs"),
            new TinySilaDocument(
                sila.GetValueOrDefault("feature", ""),
                sila.GetValueOrDefault("command", ""),
                sila.GetValueOrDefault("description", "")));
    }
}
