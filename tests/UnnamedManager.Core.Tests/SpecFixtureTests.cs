using Xunit;
using System.Text.Json;
using UnnamedManager.Core.Config;

namespace UnnamedManager.Core.Tests;

/// Runs every fixture in spec/fixtures/manifest.json that applies to Windows
/// (S-001, S-002): valid ones load with exactly their warnings, invalid ones
/// fail with their rule and subject.
public class SpecFixtureTests
{
    /// Fixtures known to fail, each with the story that makes it pass. A pending
    /// fixture that starts passing fails the test, so the list stays honest.
    private static readonly Dictionary<string, string> Pending = new()
    {
    };

    /// Found from this file's path: the build output lives outside the repo (Directory.Build.props).
    public static string SpecFolder => FindSpec();

    private static string FindSpec([System.Runtime.CompilerServices.CallerFilePath] string here = "")
    {
        {
            var dir = new DirectoryInfo(Path.GetDirectoryName(here)!);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "spec", "fixtures", "manifest.json"))) dir = dir.Parent;
            return Path.Combine(dir?.FullName ?? throw new InvalidOperationException("No spec/ folder above the test output. Run git submodule update --init."), "spec");
        }
    }

    public sealed record Expected(string Rule, string? Subject);
    public sealed record Fixture(string File, bool Valid, Expected[] Errors, Expected[] Warnings);

    public static IEnumerable<Fixture> WindowsFixtures()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(SpecFolder, "fixtures", "manifest.json")));
        foreach (var f in manifest.RootElement.GetProperty("fixtures").EnumerateArray())
        {
            if (f.TryGetProperty("platforms", out var platforms) && !platforms.EnumerateArray().Any(p => p.GetString() == "windows")) continue;
            static Expected[] List(JsonElement f, string name) => f.TryGetProperty(name, out var list)
                ? list.EnumerateArray().Select(e => new Expected(e.GetProperty("rule").GetString()!, e.TryGetProperty("subject", out var s) ? s.GetString() : null)).ToArray()
                : [];
            yield return new Fixture(f.GetProperty("file").GetString()!, f.GetProperty("valid").GetBoolean(), List(f, "errors"), List(f, "warnings"));
        }
    }

    public static TheoryData<string> Files() => new(WindowsFixtures().Select(f => f.File));

    /// Why the fixture doesn't behave as the manifest says, or null when it does.
    private static string? Mismatch(Fixture fixture)
    {
        var result = ConfigLoader.LoadFile(Path.Combine(SpecFolder, "fixtures", fixture.File));
        if (fixture.Valid)
        {
            if (!result.IsValid) return $"should load, but: {string.Join(" | ", result.Errors.Select(e => $"[{e.Rule}] {e.Message}"))}";
            var got = result.Loaded!.Warnings.Select(w => $"{w.Rule} {w.Subject}").Order().ToList();
            var want = fixture.Warnings.Select(w => $"{w.Rule} {w.Subject}").Order().ToList();
            return got.SequenceEqual(want) ? null : $"warnings: got [{string.Join(", ", got)}], want [{string.Join(", ", want)}]";
        }
        if (result.IsValid) return "should fail, but loaded";
        foreach (var want in fixture.Errors)
        {
            if (!result.Errors.Any(e => e.Rule == want.Rule && (want.Subject is null || e.Subject == want.Subject)))
                return $"want [{want.Rule}] {want.Subject}, got {string.Join(" | ", result.Errors.Select(e => $"[{e.Rule}] {e.Subject}: {e.Message}"))}";
        }
        return null;
    }

    [Theory]
    [MemberData(nameof(Files))]
    public void FixtureBehavesAsTheManifestSays(string file)
    {
        var fixture = WindowsFixtures().Single(f => f.File == file);
        var mismatch = Mismatch(fixture);
        if (Pending.TryGetValue(file, out var story))
        {
            Assert.True(mismatch is not null, $"{file} passes now: take it off the pending list ({story}).");
            return;
        }
        Assert.True(mismatch is null, $"{file}: {mismatch}");
    }

    [Fact]
    public void TheSpecIsPinnedToV15()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(SpecFolder, "fixtures", "manifest.json")));
        Assert.Equal("v15", manifest.RootElement.GetProperty("spec").GetString());
    }

    [Fact]
    public void EveryPendingFixtureExists() =>
        Assert.All(Pending.Keys, file => Assert.Contains(file, WindowsFixtures().Select(f => f.File)));
}
