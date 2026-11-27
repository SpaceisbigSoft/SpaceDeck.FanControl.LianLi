using System.Collections.Generic;
using System.Text;

namespace FanControl.LianLi.Tests.Fakes;

/// <summary>Messages of the collector's screen pipe, built byte by byte from SpaceDeck's SCREEN-PIPE.md table.</summary>
internal static class ScreenMessage {
    /// <summary>A JPEG-shaped payload: FF D8, filler, FF D9.</summary>
    public static byte[] Jpeg(int length) {
        var jpeg = new byte[length];
        for (int i = 0; i < length; i++) {
            jpeg[i] = (byte)i;
        }

        jpeg[0] = 0xFF;
        jpeg[1] = 0xD8;
        jpeg[length - 2] = 0xFF;
        jpeg[length - 1] = 0xD9;
        return jpeg;
    }

    /// <summary>The header: SDSF, the version, the kind, the id's length.</summary>
    public static byte[] Header(int idLength, byte version = 1, byte kind = 1)
        => new byte[] { 0x53, 0x44, 0x53, 0x46, version, kind, (byte)idLength, (byte)(idLength >> 8) };

    /// <summary>The id's bytes, then the payload's length, little-endian.</summary>
    public static byte[] IdAndLength(string id, long payloadLength) {
        var bytes = new List<byte>(Encoding.ASCII.GetBytes(id)) {
            (byte)payloadLength,
            (byte)(payloadLength >> 8),
            (byte)(payloadLength >> 16),
            (byte)(payloadLength >> 24),
        };
        return bytes.ToArray();
    }

    /// <summary>A whole message for <paramref name="id"/> carrying <paramref name="payload"/>.</summary>
    public static byte[] Build(string id, byte[] payload) {
        var bytes = new List<byte>(Header(id.Length));
        bytes.AddRange(IdAndLength(id, payload.Length));
        bytes.AddRange(payload);
        return bytes.ToArray();
    }
}
