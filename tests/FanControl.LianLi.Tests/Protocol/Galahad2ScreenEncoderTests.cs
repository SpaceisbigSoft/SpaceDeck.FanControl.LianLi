using System;
using System.Collections.Generic;
using System.Linq;
using FanControl.LianLi.Protocol;
using Xunit;

namespace FanControl.LianLi.Tests.Protocol;

/// <summary>
/// The Vision LCD's B packets byte for byte: report id, command, the whole payload's length on every
/// packet, the packet numbers, each packet's own length and the zero padding after it.
/// </summary>
public class Galahad2ScreenEncoderTests {
    private static byte[] Jpeg(int length) {
        var jpeg = new byte[length];
        for (int i = 0; i < length; i++) {
            jpeg[i] = (byte)(i * 7 + 3);
        }

        return jpeg;
    }

    // The packet the encoder should write, built from the register note's table.
    private static byte[] Expected(byte command, int total, int number, byte[] payload, int offset, int length) {
        var packet = new byte[1024];
        packet[0] = 0x02;
        packet[1] = command;
        packet[2] = (byte)(total >> 24);
        packet[3] = (byte)(total >> 16);
        packet[4] = (byte)(total >> 8);
        packet[5] = (byte)total;
        packet[6] = (byte)(number >> 16);
        packet[7] = (byte)(number >> 8);
        packet[8] = (byte)number;
        packet[9] = (byte)(length >> 8);
        packet[10] = (byte)length;
        Array.Copy(payload, offset, packet, 11, length);
        return packet;
    }

    [Fact]
    public void ASmallJpeg_IsOnePacket_ZeroPadded() {
        byte[] jpeg = { 0xFF, 0xD8, 0x01, 0x02, 0xFF, 0xD9 };

        byte[] packet = Assert.Single(Galahad2ScreenEncoder.EncodeJpeg(jpeg));

        var expected = new byte[1024];
        expected[0] = 0x02;
        expected[1] = 0x0E;
        expected[5] = 6;
        expected[10] = 6;
        expected[11] = 0xFF;
        expected[12] = 0xD8;
        expected[13] = 0x01;
        expected[14] = 0x02;
        expected[15] = 0xFF;
        expected[16] = 0xD9;
        Assert.Equal(expected, packet);
    }

    [Fact]
    public void AJpegOfExactlyOneChunk_IsOneFullPacket() {
        byte[] jpeg = Jpeg(1013);

        byte[] packet = Assert.Single(Galahad2ScreenEncoder.EncodeJpeg(jpeg));

        Assert.Equal(Expected(0x0E, 1013, 0, jpeg, 0, 1013), packet);
        Assert.Equal(0x03, packet[9]);
        Assert.Equal(0xF5, packet[10]);
    }

    [Fact]
    public void AJpegOneByteOverAChunk_IsTwoPackets_TheLastHoldingOneByte() {
        byte[] jpeg = Jpeg(1014);

        IReadOnlyList<byte[]> packets = Galahad2ScreenEncoder.EncodeJpeg(jpeg);

        Assert.Equal(2, packets.Count);
        Assert.Equal(Expected(0x0E, 1014, 0, jpeg, 0, 1013), packets[0]);
        Assert.Equal(Expected(0x0E, 1014, 1, jpeg, 1013, 1), packets[1]);
        Assert.Equal(new byte[] { 0x02, 0x0E, 0x00, 0x00, 0x03, 0xF6, 0x00, 0x00, 0x01, 0x00, 0x01 }, packets[1].Take(11).ToArray());
    }

    // A 56 KB frame, the largest seen on the owner's cooler: 57 packets, the size and packet number
    // fields past one byte, the last packet partly filled.
    [Fact]
    public void ALargeJpeg_IsSplitInto1013ByteChunks_NumberedFromZero() {
        byte[] jpeg = Jpeg(56 * 1024);

        IReadOnlyList<byte[]> packets = Galahad2ScreenEncoder.EncodeJpeg(jpeg);

        Assert.Equal(57, packets.Count);
        for (int n = 0; n < packets.Count; n++) {
            int offset = n * 1013;
            Assert.Equal(Expected(0x0E, jpeg.Length, n, jpeg, offset, Math.Min(1013, jpeg.Length - offset)), packets[n]);
        }

        Assert.Equal(new byte[] { 0x02, 0x0E, 0x00, 0x00, 0xE0, 0x00, 0x00, 0x00, 0x38, 0x02, 0x68 }, packets[56].Take(11).ToArray());
    }

    [Fact]
    public void TheLargestJpeg_NumbersItsPacketsPastOneByte() {
        byte[] jpeg = Jpeg(Galahad2ScreenEncoder.MaximumJpegLength);

        IReadOnlyList<byte[]> packets = Galahad2ScreenEncoder.EncodeJpeg(jpeg);

        Assert.Equal(259, packets.Count);
        Assert.Equal(new byte[] { 0x02, 0x0E, 0x00, 0x04, 0x00, 0x00, 0x00, 0x01, 0x02, 0x03, 0x16 }, packets[258].Take(11).ToArray());
        Assert.Equal(Expected(0x0E, jpeg.Length, 258, jpeg, 258 * 1013, 790), packets[258]);
    }

    [Fact]
    public void AnyCommand_IsCarriedTheSameWay() {
        byte[] payload = { 0x00, 0x01, 0x02 };

        byte[] packet = Assert.Single(Galahad2ScreenEncoder.EncodeCommand(0x14, payload));

        Assert.Equal(Expected(0x14, 3, 0, payload, 0, 3), packet);
    }

    [Fact]
    public void BadPayloads_AreRejected() {
        Assert.Throws<ArgumentNullException>(() => Galahad2ScreenEncoder.EncodeJpeg(null!));
        Assert.Throws<ArgumentException>(() => Galahad2ScreenEncoder.EncodeJpeg(Array.Empty<byte>()));
        Assert.Throws<ArgumentException>(() => Galahad2ScreenEncoder.EncodeJpeg(new byte[Galahad2ScreenEncoder.MaximumJpegLength + 1]));
        Assert.Throws<ArgumentNullException>(() => Galahad2ScreenEncoder.EncodeCommand(0x14, null!));
        Assert.Throws<ArgumentException>(() => Galahad2ScreenEncoder.EncodeCommand(0x14, Array.Empty<byte>()));
    }
}
