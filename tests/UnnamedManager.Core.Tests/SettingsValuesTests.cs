using UnnamedManager.Core.Config;
using Xunit;

namespace UnnamedManager.Core.Tests;

/// S-047: values typed in Settings.
public class SettingsValuesTests
{
    [Theory]
    [InlineData(-200, 0)]
    [InlineData(350.4, 350)]
    [InlineData(1e9, 60_000)]
    [InlineData(double.NaN, 0)]
    public void ADelayOrTimeoutIsNeverNegative(double typed, int kept) => Assert.Equal(kept, SettingsValues.Milliseconds(typed));

    [Theory]
    [InlineData(-4, 0)]
    [InlineData(7, 8)]
    [InlineData(9, 10)]
    [InlineData(100, 64)]
    public void TheGapRuns0To64InStepsOf2(double typed, double kept) => Assert.Equal(kept, SettingsValues.Gap(typed));

    [Fact]
    public void ACancelOrBackKeyIsOneKeyAndNotTheOther()
    {
        Assert.Null(SettingsValues.RuntimeKeyProblem(Chord.Parse("Q")!.Value, "Backspace"));
        Assert.NotNull(SettingsValues.RuntimeKeyProblem(Chord.Parse("Ctrl+Q")!.Value, "Backspace"));
        Assert.NotNull(SettingsValues.RuntimeKeyProblem(Chord.Parse("Backspace")!.Value, "backspace"));
    }
}
