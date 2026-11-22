using UnnamedManager.Core.Keyboard;
using Xunit;

namespace UnnamedManager.Core.Tests;

// S-098: a big Home in a few columns.
public class HomeLayoutTests
{
    private static string Pack(params int[] entries) => string.Join(" | ", HomeLayout.Pack(entries).Select(c => string.Join(",", c)));

    [Fact]
    public void EightGroupsMakeAFewColumnsTheCurrentOneFirst() => Assert.Equal("0 | 1,2,3,4 | 5,6,7", Pack(2, 2, 2, 2, 2, 2, 2, 2));

    [Fact]
    public void TheOthersArePackedInOrderUpToTwelveRows() => Assert.Equal("0 | 1 | 2,3 | 4", Pack(1, 7, 4, 6, 1));

    [Fact]
    public void AGroupIsNeverSplit() => Assert.Equal("0 | 1 | 2", Pack(20, 15, 1));

    [Fact]
    public void OneGroupIsOneColumn() => Assert.Equal("0", Pack(14));
}
