#if ENABLE_LIGHTING
using System;
using System.Collections.Generic;
using System.Linq;
using FanControl.LianLi.Protocol;
using Xunit;

namespace FanControl.LianLi.Tests.Protocol;

/// <summary>The screen ring's packets byte for byte: 0x83 for an effect or off, 0x14 on the screen path for a direct look.</summary>
public class Galahad2RingEncoderTests {
    private static readonly RgbColor Red = new RgbColor(255, 0, 0);
    private static readonly RgbColor Green = new RgbColor(0, 255, 0);

    // A 0x83 command packet: report 0x01, the command, three zero bytes, the payload length, the payload, zeros.
    private static byte[] PumpLight(params byte[] payload) {
        var packet = new byte[64];
        packet[0] = 0x01;
        packet[1] = 0x83;
        packet[5] = (byte)payload.Length;
        Array.Copy(payload, 0, packet, 6, payload.Length);
        return packet;
    }

    [Fact]
    public void AStaticLook_IsOne0x83_OnTheOuterScope_FromTheCoolersMcu() {
        var look = new Galahad2RingLook(Galahad2RingMode.Static, 0, 4, Galahad2RingLook.Clockwise, new[] { Red, Green });

        byte[] packet = Assert.Single(Galahad2RingEncoder.Encode(look));

        Assert.Equal(
            PumpLight(0x01, 0x03, 0x04, 0x00, 255, 0, 0, 0, 255, 0, 0, 0, 0, 0, 0, 0, 0x00, 0x00, 0x00),
            packet);
        Assert.Equal(1, Galahad2RingEncoder.RingScope);
    }

    [Theory]
    [InlineData((int)Galahad2RingMode.Rainbow, 0x01)]
    [InlineData((int)Galahad2RingMode.RainbowMorph, 0x02)]
    [InlineData((int)Galahad2RingMode.Static, 0x03)]
    [InlineData((int)Galahad2RingMode.Breathing, 0x04)]
    [InlineData((int)Galahad2RingMode.Runway, 0x05)]
    [InlineData((int)Galahad2RingMode.Meteor, 0x06)]
    public void EachEffect_HasTheCoolersModeByte_SpeedBrightnessAndDirection(int effect, int modeByte) {
        var mode = (Galahad2RingMode)effect;
        RgbColor[] colors = mode == Galahad2RingMode.Rainbow || mode == Galahad2RingMode.RainbowMorph
            ? Array.Empty<RgbColor>()
            : new[] { Red, Green, new RgbColor(0, 0, 255), new RgbColor(1, 2, 3) };
        var look = new Galahad2RingLook(mode, 3, 2, Galahad2RingLook.CounterClockwise, colors);

        byte[] packet = Assert.Single(Galahad2RingEncoder.Encode(look));

        var payload = new byte[19];
        payload[0] = 0x01;
        payload[1] = (byte)modeByte;
        payload[2] = 2;
        payload[3] = 3;
        for (int i = 0; i < colors.Length; i++) {
            payload[4 + (i * 3)] = colors[i].R;
            payload[5 + (i * 3)] = colors[i].G;
            payload[6 + (i * 3)] = colors[i].B;
        }

        payload[16] = 0x01;
        Assert.Equal(PumpLight(payload), packet);
    }

    [Fact]
    public void AnOffLook_IsADarkDisabledStatic0x83() {
        var look = new Galahad2RingLook(Galahad2RingMode.Off, 4, 4, Galahad2RingLook.CounterClockwise, new[] { Red });

        byte[] packet = Assert.Single(Galahad2RingEncoder.Encode(look));

        Assert.Equal(PumpLight(0x01, 0x03, 0x00, 0x00, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0x00, 0x01, 0x00), packet);
    }

    [Fact]
    public void ADirectLook_Is0x14OnTheScreenPath_EightEmptySlotsThenEachLed() {
        RgbColor[] leds = Enumerable.Range(0, 12).Select(i => new RgbColor((byte)(i * 10), (byte)(i + 1), (byte)(200 - i))).ToArray();
        var look = new Galahad2RingLook(Galahad2RingMode.Direct, 0, 4, Galahad2RingLook.Clockwise, leds);

        byte[] packet = Assert.Single(Galahad2RingEncoder.Encode(look));

        var expected = new byte[1024];
        expected[0] = 0x02;
        expected[1] = 0x14;
        expected[5] = 61;  // the whole payload: (12 + 8) x 3 + 1
        expected[10] = 61; // all of it in this packet
        expected[11] = 0x00; // the screen path; bytes 12-35 are the eight unused slots
        for (int i = 0; i < 12; i++) {
            expected[36 + (i * 3)] = leds[i].R;
            expected[37 + (i * 3)] = leds[i].G;
            expected[38 + (i * 3)] = leds[i].B;
        }

        Assert.Equal(expected, packet);
    }

    [Fact]
    public void ANullLook_IsRejected()
        => Assert.Throws<ArgumentNullException>(() => Galahad2RingEncoder.Encode(null!));
}
#endif
