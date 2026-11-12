namespace UnnamedManager.Core;

/// The version line in the tray menu (S-009), from the assembly's informational
/// version. build.ps1 adds "+branch.commit" (and ".modified" for a dirty tree) to a
/// local build; a release has none.
public static class AppVersion
{
    /// "Version 0.1.3" for a release; "Version 0.1 (main, ee74e2d, modified)" for a local build.
    public static string Line(string? informational)
    {
        if (string.IsNullOrWhiteSpace(informational)) return "Version unknown";
        var plus = informational.IndexOf('+');
        if (plus < 0) return $"Version {informational}";
        var version = informational[..plus];
        var parts = version.Split('.');
        var shortVersion = parts.Length >= 2 ? $"{parts[0]}.{parts[1]}" : version;
        var build = informational[(plus + 1)..].Split('.', StringSplitOptions.RemoveEmptyEntries);
        // A branch name can hold dots; the commit and "modified" are the last parts.
        var modified = build.Length > 0 && build[^1] == "modified";
        var rest = modified ? build[..^1] : build;
        if (rest.Length < 2) return $"Version {version}";
        var commit = rest[^1];
        var branch = string.Join(".", rest[..^1]);
        return $"Version {shortVersion} ({branch}, {commit}{(modified ? ", modified" : "")})";
    }
}
