using System;
using FanControl.LianLi.Devices;
using FanControl.LianLi.Tests.Fakes;
using Xunit;

namespace FanControl.LianLi.Tests.Devices;

public class CoolerHandoffsTests {
    [Fact]
    public void EachIndex_HasOneHandoff_ForTheLifeOfTheRegistry() {
        var handoffs = new CoolerHandoffs(new FakeClock());

        CoolerHandoff first = handoffs.For(0);

        Assert.Same(first, handoffs.For(0));
        Assert.NotSame(first, handoffs.For(1));
    }

    [Fact]
    public void ANullClock_IsRejected()
        => Assert.Throws<ArgumentNullException>(() => new CoolerHandoffs(null!));
}
