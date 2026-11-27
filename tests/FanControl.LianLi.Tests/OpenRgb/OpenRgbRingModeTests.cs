#if ENABLE_LIGHTING
using System;
using System.Linq;
using FanControl.LianLi.OpenRgb;
using FanControl.LianLi.Protocol;
using Xunit;

namespace FanControl.LianLi.Tests.OpenRgb;

public class OpenRgbRingModeTests {
    [Fact]
    public void TheModes_AreDirectOffTheCoolersEffectsAndLConnect_InOrder() {
        Assert.Equal(
            new[] { "Direct", "Off", "Static", "Breathing", "Rainbow", "Rainbow Morph", "Runway", "Meteor", "L-Connect" },
            OpenRgbRingMode.All.Select(m => m.Name));
        Assert.Equal(new[] { 0, 0, 3, 4, 1, 2, 5, 6, 0 }, OpenRgbRingMode.All.Select(m => m.Value));
        Assert.All(OpenRgbRingMode.All, m => Assert.NotEqual(0, m.Flags & OpenRgbRingMode.ManualSaveFlag));
    }

    [Fact]
    public void EachMode_HasWhatTheCoolerTakesForIt() {
        OpenRgbRingMode direct = OpenRgbRingMode.All[0];
        Assert.Equal(OpenRgbRingMode.PerLedColorFlag | OpenRgbRingMode.ManualSaveFlag, direct.Flags);
        Assert.Equal(OpenRgbRingMode.PerLedColors, direct.ColorMode);
        Assert.False(direct.HasSpeed);
        Assert.False(direct.HasBrightness);
        Assert.False(direct.HasDirection);
        Assert.Equal(0, direct.SpeedMaximum);
        Assert.Equal(0, direct.BrightnessMaximum);

        OpenRgbRingMode meteor = OpenRgbRingMode.All[7];
        Assert.Equal(Galahad2RingMode.Meteor, meteor.RingMode);
        Assert.True(meteor.HasSpeed);
        Assert.True(meteor.HasBrightness);
        Assert.True(meteor.HasDirection);
        Assert.Equal(4, meteor.SpeedMaximum);
        Assert.Equal(4, meteor.BrightnessMaximum);
        Assert.Equal((1, 4), (meteor.ColorsMinimum, meteor.ColorsMaximum));
        Assert.Equal(OpenRgbRingMode.ModeSpecificColors, meteor.ColorMode);
        Assert.Equal(0x153, meteor.Flags);

        OpenRgbRingMode rainbow = OpenRgbRingMode.All[4];
        Assert.Equal(OpenRgbRingMode.NoColors, rainbow.ColorMode);
        Assert.Equal(0x113, rainbow.Flags);
        Assert.Equal(0x150, OpenRgbRingMode.All[2].Flags); // Static: brightness, its own colours, no speed

        OpenRgbRingMode lConnect = OpenRgbRingMode.All[8];
        Assert.Equal(OpenRgbRingMode.LConnectName, lConnect.Name);
        Assert.Null(lConnect.RingMode);
        Assert.True(lConnect.HandsBackToLConnect);
        Assert.Equal(OpenRgbRingMode.ManualSaveFlag, lConnect.Flags);
        Assert.Equal((0, 0), (lConnect.ColorsMinimum, lConnect.ColorsMaximum));
        Assert.Equal(OpenRgbRingMode.NoColors, lConnect.ColorMode);
        Assert.All(OpenRgbRingMode.All.Take(8), m => Assert.False(m.HandsBackToLConnect));
    }

    [Fact]
    public void IndexOf_FindsTheModeOfEachLook() {
        for (int i = 0; i < OpenRgbRingMode.All.Count - 1; i++) {
            Assert.Equal(i, OpenRgbRingMode.IndexOf(OpenRgbRingMode.All[i].RingMode.Value));
        }

        Assert.Throws<ArgumentOutOfRangeException>(() => OpenRgbRingMode.IndexOf((Galahad2RingMode)99));
    }
}
#endif
