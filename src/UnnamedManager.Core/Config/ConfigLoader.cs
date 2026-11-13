namespace UnnamedManager.Core.Config;

/// The result of reading the config: a loaded config, or the errors that stopped it.
public sealed record LoadResult(LoadedConfig? Loaded, IReadOnlyList<ConfigIssue> Errors)
{
    public bool IsValid => Loaded is not null;
}

/// Reads the config file and turns every failure into sentences.
public static class ConfigLoader
{
    public const string SchemaFileName = "config.schema.json";

    /// Reads and checks JSON text. Tests pass a registry with families of their own.
    public static LoadResult Load(string json, ActionRegistry? registry = null)
    {
        Config config;
        try
        {
            config = new ConfigReader(registry ?? ActionRegistry.Standard).Read(json);
        }
        catch (ConfigProblem problem)
        {
            return new LoadResult(null, [problem.ToIssue()]);
        }
        var (loaded, errors) = ConfigValidator.Validate(config);
        return new LoadResult(loaded, errors);
    }

    /// Reads the file. A missing file is an error here; the app writes the
    /// starter config first (<see cref="WriteStarterIfMissing"/>).
    public static LoadResult LoadFile(string path)
    {
        try
        {
            return Load(File.ReadAllText(path));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new LoadResult(null, [new ConfigIssue(Severity.Error, $"Can't read {path}: {e.Message}")]);
        }
    }

    /// Writes the starter config and the schema next to it when no config file
    /// exists. Never writes over a file. Returns true when it wrote one.
    public static bool WriteStarterIfMissing(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        try
        {
            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
            using var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false));
            writer.Write(StarterJson);
        }
        catch (IOException) when (File.Exists(path))
        {
            return false;
        }
        WriteSchema(path);
        return true;
    }

    /// Writes config.schema.json next to the config when it's missing or out of
    /// date, so the `$schema` line finds it.
    public static void WriteSchema(string configPath)
    {
        var target = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(configPath))!, SchemaFileName);
        var schema = SchemaJson;
        if (File.Exists(target) && File.ReadAllText(target) == schema) return;
        File.WriteAllText(target, schema, new System.Text.UTF8Encoding(false));
    }

    /// The spec's schema, built into the library from spec/config.schema.json.
    public static string SchemaJson
    {
        get
        {
            using var stream = typeof(ConfigLoader).Assembly.GetManifestResourceStream(SchemaFileName)!;
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
    }

    /// One leader with two bindings, File Explorer and Windows Terminal (S-002), and Show Help on Hyper+H (S-051).
    public const string StarterJson = """
        {
          "$schema": "./config.schema.json",
          "version": 1,
          "entries": [
            { "keys": "Hyper+Space", "title": "Commands" },
            { "keys": "Hyper+Space F", "title": "File Explorer", "action": "open-explorer" },
            { "keys": "Hyper+Space T", "title": "Terminal", "action": "open-terminal" },
            { "keys": "Hyper+H", "title": "Help", "action": "show-help" }
          ],
          "actions": {
            "open-explorer": { "type": "application", "exe": "explorer.exe" },
            "open-terminal": { "type": "application", "appId": "Microsoft.WindowsTerminal_8wekyb3d8bbwe!App" },
            "show-help": { "type": "help" }
          }
        }

        """;
}
