#if ENABLE_LIGHTING
using System;
using System.Collections.Generic;
using System.Linq;
using FanControl.LianLi.OpenRgb;
using FanControl.LianLi.Protocol;
using Xunit;

namespace FanControl.LianLi.Tests.OpenRgb;

/// <summary>
/// The controller description field by field, read back by an independent reader written from
/// lian-li-linux's layout (its <c>build_controller_zones</c>, <c>write_zone</c>, <c>ModeData::write</c>),
/// for every protocol version from 0 to 6.
/// </summary>
public class OpenRgbControllerEncoderTests {
    private static readonly RgbColor[] Leds = Enumerable.Range(0, 12).Select(i => new RgbColor((byte)i, (byte)(i + 100), 7)).ToArray();

    private static OpenRgbModeSetting[] Settings() {
        OpenRgbModeSetting[] settings = OpenRgbRingMode.All.Select(OpenRgbModeSetting.DefaultFor).ToArray();
        settings[6] = new OpenRgbModeSetting(OpenRgbRingMode.All[6], 3, 1, OpenRgbRingMode.Left, new[] { new RgbColor(9, 8, 7), new RgbColor(6, 5, 4) });
        return settings;
    }

    // A reader of its own, so the test does not trust the plugin's reader to check its writer.
    private sealed class Wire {
        private readonly byte[] _data;

        public Wire(byte[] data) => _data = data;

        public int At { get; private set; }

        public long U32() {
            long value = BitConverter.ToUInt32(_data, At);
            At += 4;
            return value;
        }

        public int I32() {
            int value = BitConverter.ToInt32(_data, At);
            At += 4;
            return value;
        }

        public int U16() {
            int value = BitConverter.ToUInt16(_data, At);
            At += 2;
            return value;
        }

        public string Text() {
            int length = U16();
            Assert.Equal(0, _data[At + length - 1]);
            string text = System.Text.Encoding.UTF8.GetString(_data, At, length - 1);
            At += length;
            return text;
        }

        public byte Byte() => _data[At++];

        public (byte R, byte G, byte B) Color() {
            var color = (_data[At], _data[At + 1], _data[At + 2]);
            Assert.Equal(0, _data[At + 3]);
            At += 4;
            return color;
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void TheDescription_HasEveryFieldOfItsVersion(int version) {
        byte[] data = OpenRgbControllerEncoder.Encode("LianLi/0", "2.0.9", 6, Settings(), Leds, version);
        var wire = new Wire(data);

        Assert.Equal(data.Length, wire.U32());
        Assert.Equal(3, wire.U32()); // a cooler
        Assert.Equal("Lian Li GA II LCD", wire.Text());
        if (version >= 1) {
            Assert.Equal("Lian Li", wire.Text());
        }

        Assert.Equal("The ring round the screen of a Lian Li GA II LCD, through FanControl's Lian Li plugin", wire.Text());
        Assert.Equal("2.0.9", wire.Text());
        Assert.Equal("LianLi/0", wire.Text());
        Assert.Equal("FanControl Lian Li plugin: LianLi/0", wire.Text());
        Assert.Equal(9, wire.U16());
        Assert.Equal(6, wire.I32());
        OpenRgbModeSetting[] settings = Settings();
        for (int m = 0; m < 9; m++) {
            OpenRgbRingMode mode = OpenRgbRingMode.All[m];
            Assert.Equal(mode.Name, wire.Text());
            if (version < 6) {
                Assert.Equal(mode.Value, wire.U32());
            }

            Assert.Equal(mode.Flags, wire.U32());
            Assert.Equal(0, wire.U32());
            Assert.Equal(mode.SpeedMaximum, wire.U32());
            if (version >= 3) {
                Assert.Equal(0, wire.U32());
                Assert.Equal(mode.BrightnessMaximum, wire.U32());
            }

            Assert.Equal(mode.ColorsMinimum, wire.U32());
            Assert.Equal(mode.ColorsMaximum, wire.U32());
            Assert.Equal(settings[m].Speed, wire.U32());
            if (version >= 3) {
                Assert.Equal(settings[m].Brightness, wire.U32());
            }

            Assert.Equal(settings[m].Direction, wire.U32());
            Assert.Equal(mode.ColorMode, wire.U32());
            Assert.Equal(settings[m].Colors.Count, wire.U16());
            foreach (RgbColor color in settings[m].Colors) {
                Assert.Equal((color.R, color.G, color.B), wire.Color());
            }
        }

        Assert.Equal(1, wire.U16());
        Assert.Equal("Screen ring", wire.Text());
        Assert.Equal(1, wire.U32()); // linear
        Assert.Equal(12, wire.U32());
        Assert.Equal(12, wire.U32());
        Assert.Equal(12, wire.U32());
        Assert.Equal(0, wire.U16()); // no matrix
        if (version >= 4) {
            Assert.Equal(0, wire.U16()); // no segments
        }

        if (version >= 5) {
            Assert.Equal(0, wire.U32()); // zone flags
        }

        if (version >= 6) {
            Assert.Equal(-1, wire.I32());
            Assert.Equal(0, wire.U16());
            Assert.Equal(string.Empty, wire.Text());
        }

        Assert.Equal(12, wire.U16());
        for (int i = 0; i < 12; i++) {
            Assert.Equal("Screen ring LED " + (i + 1), wire.Text());
            if (version < 6) {
                Assert.Equal(i, wire.U32());
            }
        }

        Assert.Equal(12, wire.U16());
        foreach (RgbColor led in Leds) {
            Assert.Equal((led.R, led.G, led.B), wire.Color());
        }

        if (version >= 5) {
            Assert.Equal(0, wire.U16());
            Assert.Equal(0, wire.U32());
        }

        if (version >= 6) {
            Assert.Equal(string.Empty, wire.Text());
            Assert.Equal(1, wire.U32());
            Assert.Equal(0, wire.Byte());
        }

        Assert.Equal(data.Length, wire.At);
    }

    // lian-li-linux's own fixture for one mode (protocol.rs, mode_fixture_decodes_versioned_fields):
    // a Static mode with flags 16, speed 0 to 4, brightness 0 to 4, no colours, speed 2, brightness 3,
    // direction 1, colour mode 2, laid out per version.
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(6)]
    public void AMode_IsLaidOutAsLianLiLinuxsFixture(int version) {
        var data = new OpenRgbWriter();
        OpenRgbControllerEncoder.WriteMode(data, new OpenRgbModeSetting(OpenRgbRingMode.All[3], 2, 3, 0, new[] { new RgbColor(1, 2, 3) }), version);

        var expected = new List<byte> { 10, 0 };
        expected.AddRange(System.Text.Encoding.ASCII.GetBytes("Breathing"));
        expected.Add(0);
        if (version < 6) {
            expected.AddRange(BitConverter.GetBytes(4u));
        }

        foreach (uint value in new uint[] { 0x151, 0, 4 }) {
            expected.AddRange(BitConverter.GetBytes(value));
        }

        if (version >= 3) {
            expected.AddRange(BitConverter.GetBytes(0u));
            expected.AddRange(BitConverter.GetBytes(4u));
        }

        foreach (uint value in new uint[] { 1, 4, 2 }) {
            expected.AddRange(BitConverter.GetBytes(value));
        }

        if (version >= 3) {
            expected.AddRange(BitConverter.GetBytes(3u));
        }

        foreach (uint value in new uint[] { 0, 2 }) {
            expected.AddRange(BitConverter.GetBytes(value));
        }

        expected.AddRange(new byte[] { 1, 0, 1, 2, 3, 0 });
        Assert.Equal(expected, data.ToArray());
    }

    [Fact]
    public void BadArguments_AreRejected() {
        OpenRgbModeSetting[] settings = Settings();
        Assert.Throws<ArgumentNullException>(() => OpenRgbControllerEncoder.Encode(null!, "v", 0, settings, Leds, 6));
        Assert.Throws<ArgumentNullException>(() => OpenRgbControllerEncoder.Encode("s", null!, 0, settings, Leds, 6));
        Assert.Throws<ArgumentNullException>(() => OpenRgbControllerEncoder.Encode("s", "v", 0, null!, Leds, 6));
        Assert.Throws<ArgumentNullException>(() => OpenRgbControllerEncoder.Encode("s", "v", 0, settings, null!, 6));
        Assert.Throws<ArgumentException>(() => OpenRgbControllerEncoder.Encode("s", "v", 0, settings.Take(7).ToArray(), Leds, 6));
        Assert.Throws<ArgumentException>(() => OpenRgbControllerEncoder.Encode("s", "v", 0, settings, Leds.Take(11).ToArray(), 6));
        Assert.Throws<ArgumentNullException>(() => OpenRgbControllerEncoder.WriteMode(null!, settings[0], 6));
        Assert.Throws<ArgumentNullException>(() => OpenRgbControllerEncoder.WriteMode(new OpenRgbWriter(), null!, 6));
    }
}
#endif
