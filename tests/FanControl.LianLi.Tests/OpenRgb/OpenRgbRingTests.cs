#if ENABLE_LIGHTING
using System;
using System.Linq;
using FanControl.LianLi.Devices;
using FanControl.LianLi.OpenRgb;
using FanControl.LianLi.Protocol;
using FanControl.LianLi.Tests.Fakes;
using Xunit;

namespace FanControl.LianLi.Tests.OpenRgb;

/// <summary>The ring's state as OpenRGB sees it, and the looks it hands the cooler's worker.</summary>
public class OpenRgbRingTests {
    private static readonly RgbColor Red = new RgbColor(255, 0, 0);
    private static readonly RgbColor White = new RgbColor(255, 255, 255);

    private readonly CoolerHandoff _handoff = new CoolerHandoff(new FakeClock());

    [Fact]
    public void ARingWithNoLookYet_IsDirectInWhite_AndHandsNothingOver() {
        var ring = new OpenRgbRing("LianLi/0", _handoff);

        Assert.Equal("LianLi/0", ring.Id);
        Assert.Equal(Galahad2RingMode.Direct, ring.Look!.Mode);
        Assert.All(ring.Look!.Colors, c => Assert.Equal(White, c));
        Assert.Null(_handoff.PeekLook());
    }

    [Fact]
    public void ARing_StartsFromTheCoolersLastLook() {
        _handoff.RestoreLook(new Galahad2RingLook(Galahad2RingMode.Runway, 1, 2, Galahad2RingLook.CounterClockwise, new[] { Red }));

        var ring = new OpenRgbRing("LianLi/0", _handoff);

        Galahad2RingLook look = ring.Look!;
        Assert.Equal(Galahad2RingMode.Runway, look.Mode);
        Assert.Equal((1, 2, 1), (look.Speed, look.Brightness, look.Direction));
        Assert.Equal(new[] { Red }, look.Colors);
        byte[] description = ring.Describe("v", 6);
        Assert.Equal(description.Length, BitConverter.ToInt32(description, 0));
    }

    [Fact]
    public void ARing_StartsFromADirectLastLook_WithItsLeds() {
        RgbColor[] leds = Enumerable.Range(0, 12).Select(i => new RgbColor((byte)i, 0, 0)).ToArray();
        _handoff.RestoreLook(new Galahad2RingLook(Galahad2RingMode.Direct, 0, 4, 0, leds));

        var ring = new OpenRgbRing("LianLi/0", _handoff);

        Assert.Equal(leds, ring.Look!.Colors);
    }

    [Fact]
    public void SetMode_ShowsTheMode() {
        var ring = new OpenRgbRing("LianLi/0", _handoff);

        ring.SetMode(2, new OpenRgbModeSetting(OpenRgbRingMode.All[2], 0, 3, 0, new[] { Red }));

        Galahad2RingLook shown = _handoff.PeekLook()!;
        Assert.Equal(Galahad2RingMode.Static, shown.Mode);
        Assert.Equal(3, shown.Brightness);
        Assert.Equal(Galahad2RingMode.Static, ring.Look!.Mode);
    }

    [Fact]
    public void TheLConnectMode_HandsTheRingBack_AndShowsNoLookOfItsOwn() {
        var ring = new OpenRgbRing("LianLi/0", _handoff);
        ring.SetMode(2, new OpenRgbModeSetting(OpenRgbRingMode.All[2], 0, 3, 0, new[] { Red }));

        ring.SetMode(8, OpenRgbModeSetting.DefaultFor(OpenRgbRingMode.All[8]));

        Assert.Null(ring.Look);
        Assert.Null(_handoff.PeekLook());
        Assert.Null(_handoff.LastLook);
        Assert.True(_handoff.TakeHandBack());
        byte[] description = ring.Describe("v", 6);
        Assert.Equal(description.Length, BitConverter.ToInt32(description, 0));

        ring.SetLeds(Enumerable.Repeat(Red, 12).ToArray());
        Assert.Null(_handoff.PeekLook()); // not Direct, so the LEDs are only kept
        ring.SetCustomMode();
        Assert.Equal(Galahad2RingMode.Direct, _handoff.PeekLook()!.Mode);
    }

    [Fact]
    public void Leds_AreShownOnlyInDirect_AndKeptForIt() {
        var ring = new OpenRgbRing("LianLi/0", _handoff);
        ring.SetMode(4, OpenRgbModeSetting.DefaultFor(OpenRgbRingMode.All[4]));
        _handoff.Taken(_handoff.PeekLook()!);

        ring.SetLeds(Enumerable.Repeat(Red, 12).ToArray());
        ring.SetLed(3, White);
        Assert.Null(_handoff.PeekLook());

        ring.SetCustomMode();
        Galahad2RingLook direct = _handoff.PeekLook()!;
        Assert.Equal(Galahad2RingMode.Direct, direct.Mode);
        Assert.Equal(White, direct.Colors[3]);
        Assert.Equal(Red, direct.Colors[4]);

        ring.SetLed(4, White);
        Assert.Equal(White, _handoff.PeekLook()!.Colors[4]);
        ring.SetLeds(Enumerable.Repeat(White, 12).ToArray());
        Assert.All(_handoff.PeekLook()!.Colors, c => Assert.Equal(White, c));
    }

    [Fact]
    public void BadArguments_AreRejected() {
        var ring = new OpenRgbRing("LianLi/0", _handoff);
        OpenRgbModeSetting setting = OpenRgbModeSetting.DefaultFor(OpenRgbRingMode.All[2]);
        Assert.Throws<ArgumentNullException>(() => new OpenRgbRing(null!, _handoff));
        Assert.Throws<ArgumentNullException>(() => new OpenRgbRing("LianLi/0", null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => ring.SetMode(-1, setting));
        Assert.Throws<ArgumentOutOfRangeException>(() => ring.SetMode(9, setting));
        Assert.Throws<ArgumentNullException>(() => ring.SetMode(2, null!));
        Assert.Throws<ArgumentException>(() => ring.SetMode(3, setting));
        Assert.Throws<ArgumentNullException>(() => ring.SetLeds(null!));
        Assert.Throws<ArgumentException>(() => ring.SetLeds(new[] { Red }));
        Assert.Throws<ArgumentOutOfRangeException>(() => ring.SetLed(-1, Red));
        Assert.Throws<ArgumentOutOfRangeException>(() => ring.SetLed(12, Red));
    }
}
#endif
