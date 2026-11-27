#if ENABLE_LIGHTING
using System;
using FanControl.LianLi.OpenRgb;
using Xunit;

namespace FanControl.LianLi.Tests.OpenRgb;

/// <summary>The SDK's 16-byte header: ORGB, the controller index, the packet id, the payload size.</summary>
public class OpenRgbProtocolTests {
    [Fact]
    public void APacket_IsTheHeaderThenThePayload() {
        Assert.Equal(
            new byte[] { 0x4F, 0x52, 0x47, 0x42, 2, 0, 0, 0, 0x4D, 0x04, 0, 0, 3, 0, 0, 0, 9, 8, 7 },
            OpenRgbProtocol.Packet(2, 1101, new byte[] { 9, 8, 7 }));
    }

    [Fact]
    public void AHeader_ReadsBackItsFields() {
        byte[] header = OpenRgbProtocol.Packet(0xFFFFFFFF, 40, Array.Empty<byte>());
        header[12] = 0x00;
        header[13] = 0x00;
        header[14] = 0x01; // 64 KiB exactly

        Assert.Equal(65536, OpenRgbProtocol.ReadHeader(header, out long device, out long packet));
        Assert.Equal(0xFFFFFFFFL, device);
        Assert.Equal(40, packet);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void AHeaderWithoutTheMagic_IsAFormatFault(int at) {
        byte[] header = OpenRgbProtocol.Packet(0, 0, Array.Empty<byte>());
        header[at] = (byte)'X';

        Assert.Equal("a packet that does not start with ORGB", Assert.Throws<FormatException>(() => OpenRgbProtocol.ReadHeader(header, out _, out _)).Message);
    }

    [Fact]
    public void APayloadOver64KiB_IsAFormatFault() {
        byte[] header = OpenRgbProtocol.Packet(0, 0, Array.Empty<byte>());
        header[12] = 0x01;
        header[14] = 0x01;

        Assert.Equal("a packet larger than 64 KiB", Assert.Throws<FormatException>(() => OpenRgbProtocol.ReadHeader(header, out _, out _)).Message);
        header[14] = 0x00;
        header[15] = 0x80;
        Assert.Throws<FormatException>(() => OpenRgbProtocol.ReadHeader(header, out _, out _));
    }

    [Fact]
    public void BadArguments_AreRejected() {
        Assert.Throws<ArgumentNullException>(() => OpenRgbProtocol.ReadHeader(null!, out _, out _));
        Assert.Throws<ArgumentException>(() => OpenRgbProtocol.ReadHeader(new byte[15], out _, out _));
        Assert.Throws<ArgumentNullException>(() => OpenRgbProtocol.Packet(0, 0, null!));
    }
}
#endif
