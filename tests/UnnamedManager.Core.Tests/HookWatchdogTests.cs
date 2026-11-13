using UnnamedManager.Core.Keyboard;
using Xunit;

namespace UnnamedManager.Core.Tests;

// S-055: keep the keyboard hook working.
public class HookWatchdogTests
{
    [Fact]
    public void InputWithNoHookCallSendsAProbe()
    {
        var dog = new HookWatchdog();
        Assert.Equal(WatchStep.Nothing, dog.Check(inputSince: false, hookCalls: 0));
        Assert.Equal(WatchStep.Nothing, dog.Check(inputSince: true, hookCalls: 3));
        Assert.Equal(WatchStep.Probe, dog.Check(inputSince: true, hookCalls: 0));
    }

    [Fact]
    public void AProbeThatArrivesMeansTheMouseMoved()
    {
        var dog = new HookWatchdog();
        Assert.False(dog.ProbeAnswered(seen: true));
        Assert.Equal(WatchStep.Probe, dog.Check(true, 0));
    }

    [Fact]
    public void ALostProbeInstallsAgainOncePerOutage()
    {
        var dog = new HookWatchdog();
        Assert.True(dog.ProbeAnswered(seen: false));
        Assert.Equal(WatchStep.Nothing, dog.Check(true, 0));
        Assert.False(dog.ProbeAnswered(seen: false));
        Assert.Equal(WatchStep.Nothing, dog.Check(true, 2));
        Assert.Equal(WatchStep.Probe, dog.Check(true, 0));
        Assert.True(dog.ProbeAnswered(seen: false));
    }

    [Fact]
    public void ASlowCallbackSaysWhatItWasDoing()
    {
        Assert.False(HookWatchdog.IsSlow(50));
        Assert.True(HookWatchdog.IsSlow(51));
        Assert.Equal("Keyboard hook callback took 63 ms on key 87 down: Consumed, sent the mask key",
            HookWatchdog.SlowLine(62.7, new RawKey(0x87, 0x76, false, false), new HookDecision(true, SendMask: true)));
        Assert.Equal("Keyboard hook callback took 80 ms on key 45 up: Passed",
            HookWatchdog.SlowLine(80, new RawKey(0x45, 0x12, false, true), HookDecision.Pass));
    }
}
