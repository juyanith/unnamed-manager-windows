using System.Runtime.InteropServices;
using UnnamedManager.Core.Config;
using Xunit;

namespace UnnamedManager.Core.Tests;

public partial class RunCommandTests
{
    private static readonly PlanContext Me = new(@"C:\Users\me");

    private static ActionPlan.Run Plan(string json)
    {
        var action = ConfigLoader.Load($$"""
            { "version": 1, "entries": [ { "keys": "Ctrl+Alt+K", "title": "K", "action": "a" } ],
              "actions": { "a": {{json}} } }
            """).Loaded!.Config.Action("a")!;
        return (ActionPlan.Run)ActionRegistry.Standard.Family("exec")!.Plan(action, Me)!;
    }

    [Theory]
    [InlineData("plain")]
    [InlineData("two words")]
    [InlineData("")]
    [InlineData("say \"hi\"")]
    [InlineData(@"C:\Program Files\")]
    [InlineData(@"ends\\")]
    [InlineData(@"\\server\share\x y")]
    [InlineData("tab\there")]
    [InlineData(@"a\""b")]
    [InlineData("\"")]
    public void EachArgumentComesBackExactlyFromCommandLineToArgvW(string argument)
    {
        // The program name first, as Windows passes it; CommandLineToArgvW treats it differently.
        var line = "prog.exe " + CommandLine.Join([argument, "next"]);
        var back = Split(line);
        Assert.Equal(["prog.exe", argument, "next"], back);
    }

    [Fact]
    public void OnlyALeadingTildeInEachPartAndInCwdIsExpanded()
    {
        var plan = Plan("""{ "type": "exec", "command": ["~\\bin\\tool.exe", "~/notes", "a~b", "%PATH%"], "cwd": "~\\work" }""");
        Assert.Equal(@"C:\Users\me\bin\tool.exe", plan.Program);
        Assert.Equal([@"C:\Users\me/notes", "a~b", "%PATH%"], plan.Arguments);
        Assert.Equal(@"C:\Users\me\work", plan.WorkingDirectory);
    }

    [Fact]
    public void CwdDefaultsToTheHomeFolderAndEnvIsAdded()
    {
        var plan = Plan("""{ "type": "exec", "command": ["code"], "env": { "A": "1", "PATH_EXTRA": "x y" } }""");
        Assert.Equal(@"C:\Users\me", plan.WorkingDirectory);
        var start = plan.StartInfo();
        Assert.False(start.UseShellExecute);
        Assert.Equal("1", start.Environment["A"]);
        Assert.Equal("x y", start.Environment["PATH_EXTRA"]);
        // Inherited, not replaced.
        Assert.Equal(Environment.GetEnvironmentVariable("PATH"), start.Environment["PATH"]);
    }

    [Fact]
    public void ItWritesCommandThenCwdAndSortedEnv()
    {
        var action = ConfigLoader.Load("""
            { "version": 1, "entries": [ { "keys": "Ctrl+Alt+K", "title": "K", "action": "a" } ],
              "actions": { "a": { "env": { "Z": "1", "A": "2" }, "cwd": "~", "command": ["code", "."], "type": "exec" } } }
            """).Loaded!.Config.Action("a")!;
        var fields = ActionRegistry.Standard.Family("exec")!.Write(action);
        Assert.Equal(["command", "cwd", "env"], fields.Select(f => f.Key));
        Assert.Equal("""{"A":"2","Z":"1"}""", fields[2].Value.ToJsonString());
        Assert.Equal("""["code","."]""", fields[0].Value.ToJsonString());
    }

    private static string[] Split(string line)
    {
        var argv = CommandLineToArgvW(line, out var count);
        try
        {
            return Enumerable.Range(0, count).Select(i => Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv, i * IntPtr.Size))!).ToArray();
        }
        finally { LocalFree(argv); }
    }

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint CommandLineToArgvW(string line, out int count);

    [LibraryImport("kernel32.dll")]
    private static partial nint LocalFree(nint memory);
}
