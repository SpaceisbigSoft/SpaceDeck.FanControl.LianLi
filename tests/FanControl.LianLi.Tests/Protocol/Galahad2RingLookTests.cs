#if ENABLE_LIGHTING
using System;
using System.Linq;
using FanControl.LianLi.Protocol;
using Xunit;

namespace FanControl.LianLi.Tests.Protocol;

public class Galahad2RingLookTests {
    private static readonly RgbColor Red = new RgbColor(255, 0, 0);

    [Fact]
    public void ALook_KeepsWhatItWasMadeWith_AsACopy() {
        var colors = new[] { Red, new RgbColor(0, 0, 255) };

        var look = new Galahad2RingLook(Galahad2RingMode.Runway, 3, 2, Galahad2RingLook.CounterClockwise, colors);
        colors[0] = default;

        Assert.Equal(Galahad2RingMode.Runway, look.Mode);
        Assert.Equal(3, look.Speed);
        Assert.Equal(2, look.Brightness);
        Assert.Equal(1, look.Direction);
        Assert.Equal(new[] { Red, new RgbColor(0, 0, 255) }, look.Colors);
    }

    [Fact]
    public void DirectOf_IsTwelveLedsOfOneColour() {
        Galahad2RingLook look = Galahad2RingLook.DirectOf(Red);

        Assert.Equal(Galahad2RingMode.Direct, look.Mode);
        Assert.Equal(12, look.Colors.Count);
        Assert.All(look.Colors, c => Assert.Equal(Red, c));
    }

    [Theory]
    [InlineData(-1, 0, 0)]
    [InlineData(5, 0, 0)]
    [InlineData(0, -1, 0)]
    [InlineData(0, 5, 0)]
    [InlineData(0, 0, 2)]
    [InlineData(0, 0, -1)]
    public void ValuesTheCoolerDoesNotTake_AreRejected(int speed, int brightness, int direction)
        => Assert.Throws<ArgumentOutOfRangeException>(() => new Galahad2RingLook(Galahad2RingMode.Static, speed, brightness, direction, new[] { Red }));

    [Fact]
    public void AnUnknownMode_IsRejected()
        => Assert.Throws<ArgumentOutOfRangeException>(() => new Galahad2RingLook((Galahad2RingMode)99, 0, 0, 0, new[] { Red }));

    [Fact]
    public void TheColours_MustFitTheMode() {
        Assert.Throws<ArgumentNullException>(() => new Galahad2RingLook(Galahad2RingMode.Static, 0, 0, 0, null!));
        Assert.Throws<ArgumentException>(() => new Galahad2RingLook(Galahad2RingMode.Direct, 0, 0, 0, Enumerable.Repeat(Red, 11).ToArray()));
        Assert.Throws<ArgumentException>(() => new Galahad2RingLook(Galahad2RingMode.Static, 0, 0, 0, Enumerable.Repeat(Red, 5).ToArray()));
        Assert.Empty(new Galahad2RingLook(Galahad2RingMode.Rainbow, 0, 0, 0, Array.Empty<RgbColor>()).Colors);
    }
}
#endif
