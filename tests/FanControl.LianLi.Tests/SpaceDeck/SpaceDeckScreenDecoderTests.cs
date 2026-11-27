using System;
using FanControl.LianLi.SpaceDeck;
using FanControl.LianLi.Tests.Fakes;
using Xunit;

namespace FanControl.LianLi.Tests.SpaceDeck;

/// <summary>Every rule of the screen pipe's framing, each broken on its own.</summary>
public class SpaceDeckScreenDecoderTests {
    [Fact]
    public void AValidMessage_ReadsItsIdLengthIdAndPayloadLength() {
        Assert.Equal(8, SpaceDeckScreenDecoder.ReadHeader(ScreenMessage.Header(8)));
        byte[] idAndLength = ScreenMessage.IdAndLength("LianLi/0", 0x01020304 & 0x3FFFF);

        Assert.Equal("LianLi/0", SpaceDeckScreenDecoder.ReadId(idAndLength));
        Assert.Equal(0x01020304 & 0x3FFFF, SpaceDeckScreenDecoder.ReadPayloadLength(idAndLength));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(128)]
    public void TheIdLength_MayBeOneTo128(int length)
        => Assert.Equal(length, SpaceDeckScreenDecoder.ReadHeader(ScreenMessage.Header(length)));

    [Theory]
    [InlineData(0)]
    [InlineData(129)]
    [InlineData(0x0101)]
    public void AnIdLengthOutOfRange_IsRefused(int length)
        => Assert.Equal(
            "an id length outside 1 to 128",
            Assert.Throws<FormatException>(() => SpaceDeckScreenDecoder.ReadHeader(ScreenMessage.Header(length))).Message);

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void AWrongMagicByte_IsRefused(int at) {
        byte[] header = ScreenMessage.Header(8);
        header[at] ^= 0x20;

        Assert.Equal("a message that does not start with SDSF", Assert.Throws<FormatException>(() => SpaceDeckScreenDecoder.ReadHeader(header)).Message);
    }

    [Fact]
    public void AnotherVersionOrKind_IsRefused() {
        Assert.Equal(
            "a message of a version other than 1",
            Assert.Throws<FormatException>(() => SpaceDeckScreenDecoder.ReadHeader(ScreenMessage.Header(8, version: 2))).Message);
        Assert.Equal(
            "a message of an unknown kind",
            Assert.Throws<FormatException>(() => SpaceDeckScreenDecoder.ReadHeader(ScreenMessage.Header(8, kind: 2))).Message);
    }

    [Theory]
    [InlineData("LianLi 0")]
    [InlineData("LianLi/é")]
    public void AnIdThatBreaksTheIdRule_IsRefused(string id)
        => Assert.Equal(
            "an id that breaks the id rule",
            Assert.Throws<FormatException>(() => SpaceDeckScreenDecoder.ReadId(IdAndLength(id))).Message);

    [Fact]
    public void AnIdByteAboveAscii_IsRefused() {
        byte[] idAndLength = ScreenMessage.IdAndLength("LianLi/0", 10);
        idAndLength[7] = 0xB0;

        Assert.Throws<FormatException>(() => SpaceDeckScreenDecoder.ReadId(idAndLength));
    }

    [Theory]
    [InlineData(1L)]
    [InlineData(262144L)]
    public void ThePayloadLength_MayBeOneTo256KiB(long length)
        => Assert.Equal(length, SpaceDeckScreenDecoder.ReadPayloadLength(ScreenMessage.IdAndLength("a", length)));

    [Theory]
    [InlineData(0L)]
    [InlineData(262145L)]
    [InlineData(0x80000000L)]
    [InlineData(0xFFFFFFFFL)]
    public void APayloadLengthOutOfRange_IsRefused(long length)
        => Assert.Equal(
            "a payload length outside 1 to 262144",
            Assert.Throws<FormatException>(() => SpaceDeckScreenDecoder.ReadPayloadLength(ScreenMessage.IdAndLength("a", length))).Message);

    [Fact]
    public void AJpeg_StartsFFD8AndEndsFFD9() {
        SpaceDeckScreenDecoder.CheckJpeg(new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 });
        SpaceDeckScreenDecoder.CheckJpeg(ScreenMessage.Jpeg(1000));
    }

    [Theory]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xD9 })]
    [InlineData(new byte[] { 0xFE, 0xD8, 0xFF, 0xD9 })]
    [InlineData(new byte[] { 0xFF, 0xD9, 0xFF, 0xD9 })]
    [InlineData(new byte[] { 0xFF, 0xD8, 0x00, 0xFF, 0xD8 })]
    [InlineData(new byte[] { 0xFF, 0xD8, 0x00, 0xFE, 0xD9 })]
    [InlineData(new byte[] { 0xFF })]
    public void APayloadThatIsNotAJpeg_IsRefused(byte[] payload)
        => Assert.Equal(
            "a payload that is not a JPEG (FF D8 ... FF D9)",
            Assert.Throws<FormatException>(() => SpaceDeckScreenDecoder.CheckJpeg(payload)).Message);

    [Fact]
    public void BadArguments_AreRejected() {
        Assert.Throws<ArgumentNullException>(() => SpaceDeckScreenDecoder.ReadHeader(null!));
        Assert.Throws<ArgumentException>(() => SpaceDeckScreenDecoder.ReadHeader(new byte[7]));
        Assert.Throws<ArgumentNullException>(() => SpaceDeckScreenDecoder.ReadId(null!));
        Assert.Throws<ArgumentException>(() => SpaceDeckScreenDecoder.ReadId(new byte[4]));
        Assert.Throws<ArgumentNullException>(() => SpaceDeckScreenDecoder.ReadPayloadLength(null!));
        Assert.Throws<ArgumentNullException>(() => SpaceDeckScreenDecoder.CheckJpeg(null!));
    }

    private static byte[] IdAndLength(string id) {
        var bytes = new byte[id.Length + 4];
        for (int i = 0; i < id.Length; i++) {
            bytes[i] = (byte)id[i];
        }

        bytes[id.Length] = 10;
        return bytes;
    }
}
