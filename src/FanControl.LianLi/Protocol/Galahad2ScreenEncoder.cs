using System;
using System.Collections.Generic;

namespace FanControl.LianLi.Protocol;

/// <summary>
/// Pure encoder for the Galahad II Vision LCD's screen commands, which go out as "B packets": whole
/// 1024-byte output reports with report id 0x02, an 11-byte header and up to 1013 bytes of payload
/// each, a longer payload split across as many packets as it takes. 0x0E SendJpeg shows one JPEG on
/// the screen; its payload is the JPEG itself, with no header or flag of its own. The screen keeps a
/// frame for about 20 seconds and then goes back to its own display, so nothing here is persistent.
/// No I/O and no state.
/// </summary>
internal static class Galahad2ScreenEncoder {
    /// <summary>The length of every B packet: the Vision's whole output report.</summary>
    public const int PacketLength = 1024;

    /// <summary>The header in front of each packet's payload.</summary>
    public const int HeaderLength = 11;

    /// <summary>The most payload one packet carries.</summary>
    public const int MaximumChunkLength = PacketLength - HeaderLength;

    /// <summary>
    /// The largest JPEG encoded, the screen pipe's own limit (256 KiB): a 480x480 frame is 20 to 60 KB,
    /// so anything near this is not a frame for this screen.
    /// </summary>
    public const int MaximumJpegLength = 256 * 1024;

    // Report id of the B packets (L-Connect's BCommandPacket), and the SendJpeg command byte.
    private const byte ReportId = 0x02;
    private const byte SendJpegCommand = 0x0E;

    /// <summary>
    /// The B packets of 0x0E SendJpeg for <paramref name="jpeg"/>, in the order they are written: one
    /// per 1013 bytes, the last one holding what is left.
    /// </summary>
    public static IReadOnlyList<byte[]> EncodeJpeg(byte[] jpeg) {
        if (jpeg is null) {
            throw new ArgumentNullException(nameof(jpeg));
        }

        if (jpeg.Length > MaximumJpegLength) {
            throw new ArgumentException("A JPEG for the screen is at most 256 KiB.", nameof(jpeg));
        }

        return EncodeCommand(SendJpegCommand, jpeg);
    }

    /// <summary>
    /// The B packets that carry <paramref name="payload"/> for <paramref name="command"/>. Every packet
    /// repeats the payload's whole length and numbers itself from 0; a payload is at least one byte,
    /// since no command the plugin sends this way has none.
    /// </summary>
    public static IReadOnlyList<byte[]> EncodeCommand(byte command, byte[] payload) {
        if (payload is null) {
            throw new ArgumentNullException(nameof(payload));
        }

        if (payload.Length == 0) {
            throw new ArgumentException("A B command carries at least one byte.", nameof(payload));
        }

        int count = (payload.Length + MaximumChunkLength - 1) / MaximumChunkLength;
        var packets = new List<byte[]>(count);
        for (int number = 0; number < count; number++) {
            int offset = number * MaximumChunkLength;
            int length = Math.Min(MaximumChunkLength, payload.Length - offset);
            var packet = new byte[PacketLength];
            packet[0] = ReportId;
            packet[1] = command;
            packet[2] = (byte)(payload.Length >> 24); // bytes 2-5: the whole payload's length, big-endian
            packet[3] = (byte)(payload.Length >> 16);
            packet[4] = (byte)(payload.Length >> 8);
            packet[5] = (byte)payload.Length;
            packet[6] = (byte)(number >> 16);         // bytes 6-8: this packet's number from 0, big-endian
            packet[7] = (byte)(number >> 8);
            packet[8] = (byte)number;
            packet[9] = (byte)(length >> 8);          // bytes 9-10: the payload bytes in this packet, big-endian
            packet[10] = (byte)length;
            Array.Copy(payload, offset, packet, HeaderLength, length); // the rest stays zero
            packets.Add(packet);
        }

        return packets;
    }
}
