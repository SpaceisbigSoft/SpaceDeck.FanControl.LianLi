#if ENABLE_LIGHTING
using System;
using FanControl.LianLi.OpenRgb;
using FanControl.LianLi.Protocol;
using Xunit;

namespace FanControl.LianLi.Tests.OpenRgb;

/// <summary>The SDK's encoding of each field, byte for byte.</summary>
public class OpenRgbWriterTests {
    [Fact]
    public void EachField_IsWrittenLittleEndian_AsTheSdkReadsIt() {
        byte[] bytes = new OpenRgbWriter()
            .UInt16(0x0102)
            .UInt32(0x03040506)
            .UInt32(-1)
            .Byte(0x07)
            .String("Hi")
            .Color(new RgbColor(1, 2, 3))
            .Colors(new[] { new RgbColor(4, 5, 6) })
            .ToArray();

        Assert.Equal(
            new byte[] {
                0x02, 0x01,
                0x06, 0x05, 0x04, 0x03,
                0xFF, 0xFF, 0xFF, 0xFF,
                0x07,
                0x03, 0x00, (byte)'H', (byte)'i', 0x00,
                1, 2, 3, 0,
                0x01, 0x00, 4, 5, 6, 0,
            },
            bytes);
    }

    [Fact]
    public void AnEmptyString_IsItsZeroAlone()
        => Assert.Equal(new byte[] { 0x01, 0x00, 0x00 }, new OpenRgbWriter().String(string.Empty).ToArray());

    [Fact]
    public void Patch_RewritesFourBytesAlreadyWritten() {
        var writer = new OpenRgbWriter().UInt32(0).UInt16(7);

        Assert.Equal(6, writer.Length);
        Assert.Equal(new byte[] { 6, 0, 0, 0, 7, 0 }, writer.Patch(0, writer.Length).ToArray());
    }

    [Fact]
    public void BadArguments_AreRejected() {
        var writer = new OpenRgbWriter().UInt16(1);
        Assert.Throws<ArgumentOutOfRangeException>(() => writer.UInt16(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => writer.UInt16(65536));
        Assert.Throws<ArgumentNullException>(() => writer.String(null!));
        Assert.Throws<ArgumentNullException>(() => writer.Colors(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => writer.Patch(-1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => writer.Patch(0, 0));
    }
}
#endif
