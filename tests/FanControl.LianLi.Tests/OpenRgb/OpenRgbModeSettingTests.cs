#if ENABLE_LIGHTING
using System;
using System.Linq;
using FanControl.LianLi.OpenRgb;
using FanControl.LianLi.Protocol;
using Xunit;

namespace FanControl.LianLi.Tests.OpenRgb;

public class OpenRgbModeSettingTests {
    private static readonly RgbColor Red = new RgbColor(255, 0, 0);
    private static readonly RgbColor White = new RgbColor(255, 255, 255);
    private static readonly RgbColor[] Leds = Enumerable.Range(0, 12).Select(i => new RgbColor((byte)i, 0, 0)).ToArray();

    private static OpenRgbRingMode Mode(string name) => OpenRgbRingMode.All.Single(m => m.Name == name);

    [Fact]
    public void TheDefaults_AreMiddleSpeedFullBrightnessClockwiseAndWhite() {
        OpenRgbModeSetting runway = OpenRgbModeSetting.DefaultFor(Mode("Runway"));
        Assert.Equal((2, 4, OpenRgbRingMode.Right), (runway.Speed, runway.Brightness, runway.Direction));
        Assert.Equal(new[] { White }, runway.Colors);

        OpenRgbModeSetting direct = OpenRgbModeSetting.DefaultFor(Mode("Direct"));
        Assert.Equal((0, 0, 0), (direct.Speed, direct.Brightness, direct.Direction));
        Assert.Empty(direct.Colors);
    }

    [Fact]
    public void ASetting_ShowsItsLook_LeftBeingCounterClockwise() {
        var meteor = new OpenRgbModeSetting(Mode("Meteor"), 3, 1, OpenRgbRingMode.Left, new[] { Red });

        Galahad2RingLook look = meteor.Look(Leds);

        Assert.Equal(Galahad2RingMode.Meteor, look.Mode);
        Assert.Equal((3, 1, Galahad2RingLook.CounterClockwise), (look.Speed, look.Brightness, look.Direction));
        Assert.Equal(new[] { Red }, look.Colors);
        Assert.Equal(Galahad2RingLook.Clockwise, new OpenRgbModeSetting(Mode("Meteor"), 3, 1, OpenRgbRingMode.Right, new[] { Red }).Look(Leds).Direction);
    }

    [Fact]
    public void ADirectSetting_ShowsTheLeds_AtFullBrightness_AndAModeWithoutDirectionGoesClockwise() {
        Galahad2RingLook direct = OpenRgbModeSetting.DefaultFor(Mode("Direct")).Look(Leds);
        Assert.Equal(Leds, direct.Colors);
        Assert.Equal(4, direct.Brightness);

        Galahad2RingLook staticLook = new OpenRgbModeSetting(Mode("Static"), 0, 2, 0, new[] { Red }).Look(Leds);
        Assert.Equal(Galahad2RingLook.Clockwise, staticLook.Direction);
        Assert.Equal(2, staticLook.Brightness);
    }

    [Fact]
    public void TheLConnectSetting_PutsNoLookOnTheRing() {
        OpenRgbModeSetting lConnect = OpenRgbModeSetting.DefaultFor(Mode("L-Connect"));

        Assert.Equal((0, 0, 0), (lConnect.Speed, lConnect.Brightness, lConnect.Direction));
        Assert.Empty(lConnect.Colors);
        Assert.Throws<InvalidOperationException>(() => lConnect.Look(Leds));
    }

    [Fact]
    public void From_IsTheSettingOfTheModeThatShowsALook() {
        OpenRgbModeSetting breathing = OpenRgbModeSetting.From(
            new Galahad2RingLook(Galahad2RingMode.Breathing, 1, 3, Galahad2RingLook.CounterClockwise, new[] { Red }));
        Assert.Equal("Breathing", breathing.Mode.Name);
        Assert.Equal((1, 3, 0), (breathing.Speed, breathing.Brightness, breathing.Direction));

        OpenRgbModeSetting runway = OpenRgbModeSetting.From(
            new Galahad2RingLook(Galahad2RingMode.Runway, 4, 4, Galahad2RingLook.CounterClockwise, Array.Empty<RgbColor>()));
        Assert.Equal(OpenRgbRingMode.Left, runway.Direction);
        Assert.Equal(new[] { White }, runway.Colors);
        Assert.Equal(
            OpenRgbRingMode.Right,
            OpenRgbModeSetting.From(new Galahad2RingLook(Galahad2RingMode.Runway, 4, 4, Galahad2RingLook.Clockwise, new[] { Red })).Direction);

        OpenRgbModeSetting direct = OpenRgbModeSetting.From(Galahad2RingLook.DirectOf(Red));
        Assert.Equal((0, 0), (direct.Speed, direct.Brightness));
        Assert.Empty(direct.Colors);

        OpenRgbModeSetting rainbow = OpenRgbModeSetting.From(new Galahad2RingLook(Galahad2RingMode.Rainbow, 2, 2, 0, new[] { Red }));
        Assert.Empty(rainbow.Colors);
    }

    [Theory]
    [InlineData("Static", 1, 4, 0, 1)]
    [InlineData("Static", 0, 5, 0, 1)]
    [InlineData("Static", -1, 4, 0, 1)]
    [InlineData("Static", 0, -1, 0, 1)]
    [InlineData("Static", 0, 4, 1, 1)]
    [InlineData("Runway", 2, 4, 2, 1)]
    public void ValuesTheModeDoesNotTake_AreRejected(string name, int speed, int brightness, int direction, int colors)
        => Assert.Throws<ArgumentOutOfRangeException>(() => new OpenRgbModeSetting(Mode(name), speed, brightness, direction, Enumerable.Repeat(Red, colors).ToArray()));

    [Fact]
    public void ColoursTheModeDoesNotTake_AreRejected() {
        Assert.Throws<ArgumentException>(() => new OpenRgbModeSetting(Mode("Static"), 0, 4, 0, Array.Empty<RgbColor>()));
        Assert.Throws<ArgumentException>(() => new OpenRgbModeSetting(Mode("Static"), 0, 4, 0, Enumerable.Repeat(Red, 5).ToArray()));
        Assert.Throws<ArgumentNullException>(() => new OpenRgbModeSetting(Mode("Static"), 0, 4, 0, null!));
        Assert.Throws<ArgumentNullException>(() => new OpenRgbModeSetting(null!, 0, 4, 0, new[] { Red }));
        Assert.Throws<ArgumentNullException>(() => OpenRgbModeSetting.DefaultFor(null!));
        Assert.Throws<ArgumentNullException>(() => OpenRgbModeSetting.From(null!));
        Assert.Throws<ArgumentNullException>(() => OpenRgbModeSetting.DefaultFor(Mode("Static")).Look(null!));
    }
}
#endif
