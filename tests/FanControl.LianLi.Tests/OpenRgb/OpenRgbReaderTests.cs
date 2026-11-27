#if ENABLE_LIGHTING
using System;
using FanControl.LianLi.OpenRgb;
using FanControl.LianLi.Protocol;
using Xunit;

namespace FanControl.LianLi.Tests.OpenRgb;

/// <summary>Reading what a client sent: each field, and every way it can fall short.</summary>
public class OpenRgbReaderTests {
    [Fact]
    public void EachField_IsReadAsWritten() {
        byte[] bytes = new OpenRgbWriter()
            .UInt32(0)
            .UInt16(0xBEEF)
            .UInt32(0xFFFFFFFF)
            .String("Name")
            .Color(new RgbColor(1, 2, 3))
            .Colors(new[] { new RgbColor(4, 5, 6), new RgbColor(7, 8, 9) })
            .ToArray();
        bytes[0] = (byte)bytes.Length;
        var reader = new OpenRgbReader(bytes);

        reader.Size();
        Assert.Equal(0xBEEF, reader.UInt16());
        Assert.Equal(0xFFFFFFFFL, reader.UInt32());
        Assert.Equal("Name", reader.String());
        Assert.Equal(new RgbColor(1, 2, 3), reader.Color());
        Assert.Equal(new[] { new RgbColor(4, 5, 6), new RgbColor(7, 8, 9) }, reader.Colors());
        reader.End();
    }

    [Theory]
    [InlineData(new byte[] { 0x01 })]
    public void AShortInteger_IsAFormatFault(byte[] bytes) {
        Assert.Equal("a payload shorter than its fields", Assert.Throws<FormatException>(() => new OpenRgbReader(bytes).UInt16()).Message);
        Assert.Throws<FormatException>(() => new OpenRgbReader(bytes).UInt32());
        Assert.Throws<FormatException>(() => new OpenRgbReader(bytes).Color());
    }

    [Fact]
    public void AStringPastThePayload_OrWithoutItsZero_OrOfLengthZero_IsAFormatFault() {
        Assert.Throws<FormatException>(() => new OpenRgbReader(new byte[] { 0x05, 0x00, 0x41 }).String());
        Assert.Equal(
            "a string without its terminating zero",
            Assert.Throws<FormatException>(() => new OpenRgbReader(new byte[] { 0x02, 0x00, 0x41, 0x42 }).String()).Message);
        Assert.Throws<FormatException>(() => new OpenRgbReader(new byte[] { 0x00, 0x00 }).String());
    }

    [Fact]
    public void ColoursPastThePayload_AreAFormatFault()
        => Assert.Throws<FormatException>(() => new OpenRgbReader(new byte[] { 0x02, 0x00, 1, 2, 3, 0 }).Colors());

    [Fact]
    public void ASizeThatIsNotThePayloads_IsAFormatFault()
        => Assert.Equal(
            "a size field that is not the payload's length",
            Assert.Throws<FormatException>(() => new OpenRgbReader(new byte[] { 5, 0, 0, 0 }).Size()).Message);

    [Fact]
    public void BytesLeftOver_AreAFormatFault() {
        var reader = new OpenRgbReader(new byte[] { 1, 0, 9 });
        _ = reader.UInt16();

        Assert.Equal("bytes past the end of its fields", Assert.Throws<FormatException>(() => reader.End()).Message);
    }

    [Fact]
    public void NullData_IsRejected()
        => Assert.Throws<ArgumentNullException>(() => new OpenRgbReader(null!));
}
#endif
