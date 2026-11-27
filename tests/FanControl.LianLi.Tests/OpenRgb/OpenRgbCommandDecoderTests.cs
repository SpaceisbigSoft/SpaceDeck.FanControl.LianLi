#if ENABLE_LIGHTING
using System;
using System.Collections.Generic;
using System.Linq;
using FanControl.LianLi.OpenRgb;
using FanControl.LianLi.Protocol;
using Xunit;

namespace FanControl.LianLi.Tests.OpenRgb;

/// <summary>Each command a client can send the ring, decoded, and each way it can be malformed.</summary>
public class OpenRgbCommandDecoderTests {
    private static readonly RgbColor Red = new RgbColor(255, 0, 0);

    private static RgbColor[] Twelve() => Enumerable.Range(0, 12).Select(i => new RgbColor((byte)i, 1, 2)).ToArray();

    private static byte[] Sized(Action<OpenRgbWriter> fields) {
        var writer = new OpenRgbWriter().UInt32(0);
        fields(writer);
        return writer.Patch(0, writer.Length).ToArray();
    }

    // UPDATEMODE as a client of version writes it: the size, the index, the mode's description.
    internal static byte[] ModePayload(int index, OpenRgbModeSetting setting, long version)
        => Sized(w => {
            w.UInt32(index);
            OpenRgbControllerEncoder.WriteMode(w, setting, version);
        });

    // A mode description with every field as a client could fill it in.
    private static byte[] RawMode(int index, string name, long speed, long brightness, long direction, long colorMode, IReadOnlyList<RgbColor> colors)
        => Sized(w => {
            w.UInt32(index).String(name).UInt32(0).UInt32(0).UInt32(0).UInt32(4).UInt32(0).UInt32(4).UInt32(0).UInt32(4);
            w.UInt32(speed).UInt32(brightness).UInt32(direction).UInt32(colorMode).Colors(colors);
        });

    private static string Fault(Func<object> decode) => Assert.Throws<FormatException>(decode).Message;

    [Fact]
    public void UpdateLeds_IsAColourForEachLed() {
        RgbColor[] leds = Twelve();

        Assert.Equal(leds, OpenRgbCommandDecoder.Leds(Sized(w => w.Colors(leds))));
        Assert.Equal(
            "a colour count that is not the ring's twelve LEDs",
            Fault(() => OpenRgbCommandDecoder.Leds(Sized(w => w.Colors(leds.Take(11).ToArray())))));
        Assert.Throws<FormatException>(() => OpenRgbCommandDecoder.Leds(Sized(w => w.Colors(leds).Byte(0))));
        Assert.Throws<FormatException>(() => OpenRgbCommandDecoder.Leds(new OpenRgbWriter().UInt32(99).Colors(leds).ToArray()));
    }

    [Fact]
    public void UpdateZoneLeds_IsTheOneZonesColours() {
        RgbColor[] leds = Twelve();

        Assert.Equal(leds, OpenRgbCommandDecoder.ZoneLeds(Sized(w => w.UInt32(0).Colors(leds))));
        Assert.Equal("a zone the ring does not have", Fault(() => OpenRgbCommandDecoder.ZoneLeds(Sized(w => w.UInt32(1).Colors(leds)))));
        Assert.Throws<FormatException>(() => OpenRgbCommandDecoder.ZoneLeds(Sized(w => w.UInt32(0).Colors(leds).Byte(1))));
    }

    [Fact]
    public void UpdateSingleLed_IsOneLedsColour() {
        Assert.Equal(11, OpenRgbCommandDecoder.SingleLed(new OpenRgbWriter().UInt32(11).Color(Red).ToArray(), out RgbColor color));
        Assert.Equal(Red, color);
        Assert.Equal("an LED the ring does not have", Fault(() => OpenRgbCommandDecoder.SingleLed(new OpenRgbWriter().UInt32(12).Color(Red).ToArray(), out _)));
        Assert.Throws<FormatException>(() => OpenRgbCommandDecoder.SingleLed(new OpenRgbWriter().UInt32(1).Color(Red).Byte(0).ToArray(), out _));
    }

    [Fact]
    public void SetCustomMode_CarriesNothing() {
        OpenRgbCommandDecoder.CustomMode(Array.Empty<byte>());
        Assert.Throws<FormatException>(() => OpenRgbCommandDecoder.CustomMode(new byte[] { 0 }));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(6)]
    public void UpdateMode_ReadsTheModeInItsVersionsLayout(int version) {
        var meteor = new OpenRgbModeSetting(OpenRgbRingMode.All[7], 3, 1, OpenRgbRingMode.Left, new[] { Red, Red });

        OpenRgbModeSetting decoded = OpenRgbCommandDecoder.Mode(ModePayload(7, meteor, version), version, out int index);

        Assert.Equal(7, index);
        Assert.Same(OpenRgbRingMode.All[7], decoded.Mode);
        Assert.Equal(3, decoded.Speed);
        Assert.Equal(version >= 3 ? 1 : 4, decoded.Brightness); // older clients send no brightness
        Assert.Equal(OpenRgbRingMode.Left, decoded.Direction);
        Assert.Equal(new[] { Red, Red }, decoded.Colors);
    }

    [Fact]
    public void UpdateMode_TakesASpeedOrBrightnessPastTheTopAsTheTop_AndIgnoresADirectionWhereThereIsNone() {
        OpenRgbModeSetting breathing = OpenRgbCommandDecoder.Mode(RawMode(3, "Breathing", 9, 99, 7, 2, new[] { Red }), 6 - 1, out _);
        Assert.Equal((4, 4, 0), (breathing.Speed, breathing.Brightness, breathing.Direction));

        OpenRgbModeSetting direct = OpenRgbCommandDecoder.Mode(RawMode(0, "Direct", 3, 3, 0, 1, Array.Empty<RgbColor>()), 5, out _);
        Assert.Equal((0, 0), (direct.Speed, direct.Brightness));
    }

    [Fact]
    public void UpdateMode_ThatTheRingCannotTake_IsAFormatFault() {
        Assert.Equal("a mode the ring does not have", Fault(() => OpenRgbCommandDecoder.Mode(RawMode(9, "Direct", 0, 0, 0, 1, Array.Empty<RgbColor>()), 5, out _)));
        Assert.Equal("a mode whose name is not its index's", Fault(() => OpenRgbCommandDecoder.Mode(RawMode(2, "Direct", 0, 0, 0, 2, new[] { Red }), 5, out _)));
        Assert.Equal("a mode with colours other than its own", Fault(() => OpenRgbCommandDecoder.Mode(RawMode(2, "Static", 0, 0, 0, 1, new[] { Red }), 5, out _)));
        Assert.Equal("a mode with more or fewer colours than it takes", Fault(() => OpenRgbCommandDecoder.Mode(RawMode(2, "Static", 0, 0, 0, 2, Array.Empty<RgbColor>()), 5, out _)));
        Assert.Throws<FormatException>(() => OpenRgbCommandDecoder.Mode(RawMode(2, "Static", 0, 0, 0, 2, Enumerable.Repeat(Red, 5).ToArray()), 5, out _));
        Assert.Equal("a direction the ring does not have", Fault(() => OpenRgbCommandDecoder.Mode(RawMode(6, "Runway", 0, 0, 2, 2, new[] { Red }), 5, out _)));
        Assert.Throws<FormatException>(() => OpenRgbCommandDecoder.Mode(RawMode(6, "Runway", 0, 0, 0, 2, new[] { Red }).Take(20).ToArray(), 5, out _));
    }

    [Fact]
    public void UpdateMode_WithBytesLeftOver_IsAFormatFault() {
        byte[] payload = ModePayload(2, OpenRgbModeSetting.DefaultFor(OpenRgbRingMode.All[2]), 4).Concat(new byte[] { 0 }).ToArray();
        payload[0]++;

        Assert.Equal("bytes past the end of its fields", Fault(() => OpenRgbCommandDecoder.Mode(payload, 4, out _)));
    }
}
#endif
