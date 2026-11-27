#if ENABLE_LIGHTING
using System;

namespace FanControl.LianLi.OpenRgb;

/// <summary>
/// The OpenRGB SDK's network framing, as pure functions, and its packet ids. Every packet is a 16-byte
/// header - <c>ORGB</c>, the controller index, the packet id and the payload's size, each a 32-bit
/// little-endian integer - and the payload. The protocol is versioned: a client says the newest version
/// it speaks, the server answers with its own, and both use the lower.
/// </summary>
internal static class OpenRgbProtocol {
    /// <summary>The header in front of every payload.</summary>
    public const int HeaderLength = 16;

    /// <summary>
    /// The newest protocol version this server speaks: 6, as lian-li-linux's. Version 1 adds the vendor
    /// string, 3 a mode's brightness, 4 a zone's segments, 5 zone and controller flags and alternate
    /// names, 6 drops a mode's and an LED's value and adds a zone's own modes.
    /// </summary>
    public const long MaximumVersion = 6;

    /// <summary>
    /// The largest payload taken from a client: the commands this server acts on are tens of bytes, so
    /// anything near this is not one of them, and it bounds what a hostile client makes it hold.
    /// </summary>
    public const int MaximumPayloadLength = 64 * 1024;

    /// <summary>NET_PACKET_ID_REQUEST_CONTROLLER_COUNT.</summary>
    public const long RequestControllerCount = 0;

    /// <summary>NET_PACKET_ID_REQUEST_CONTROLLER_DATA.</summary>
    public const long RequestControllerData = 1;

    /// <summary>NET_PACKET_ID_REQUEST_PROTOCOL_VERSION.</summary>
    public const long RequestProtocolVersion = 40;

    /// <summary>NET_PACKET_ID_SET_CLIENT_NAME.</summary>
    public const long SetClientName = 50;

    /// <summary>NET_PACKET_ID_REQUEST_PROFILE_LIST.</summary>
    public const long RequestProfileList = 150;

    /// <summary>NET_PACKET_ID_REQUEST_PLUGIN_LIST.</summary>
    public const long RequestPluginList = 200;

    /// <summary>NET_PACKET_ID_RGBCONTROLLER_UPDATELEDS.</summary>
    public const long UpdateLeds = 1050;

    /// <summary>NET_PACKET_ID_RGBCONTROLLER_UPDATEZONELEDS.</summary>
    public const long UpdateZoneLeds = 1051;

    /// <summary>NET_PACKET_ID_RGBCONTROLLER_UPDATESINGLELED.</summary>
    public const long UpdateSingleLed = 1052;

    /// <summary>NET_PACKET_ID_RGBCONTROLLER_SETCUSTOMMODE.</summary>
    public const long SetCustomMode = 1100;

    /// <summary>NET_PACKET_ID_RGBCONTROLLER_UPDATEMODE.</summary>
    public const long UpdateMode = 1101;

    /// <summary>NET_PACKET_ID_RGBCONTROLLER_SAVEMODE.</summary>
    public const long SaveMode = 1102;

    // "ORGB".
    private static readonly byte[] Magic = { 0x4F, 0x52, 0x47, 0x42 };

    /// <summary>
    /// The payload size <paramref name="header"/> announces, with the controller index and packet id
    /// it names; a header without the magic, or announcing more than
    /// <see cref="MaximumPayloadLength"/>, throws a <see cref="FormatException"/>.
    /// </summary>
    public static int ReadHeader(byte[] header, out long deviceIndex, out long packetId) {
        if (header is null) {
            throw new ArgumentNullException(nameof(header));
        }

        if (header.Length != HeaderLength) {
            throw new ArgumentException("A header is 16 bytes.", nameof(header));
        }

        for (int i = 0; i < Magic.Length; i++) {
            if (header[i] != Magic[i]) {
                throw new FormatException("a packet that does not start with ORGB");
            }
        }

        var reader = new OpenRgbReader(header);
        _ = reader.UInt32();
        deviceIndex = reader.UInt32();
        packetId = reader.UInt32();
        long size = reader.UInt32();
        if (size > MaximumPayloadLength) {
            throw new FormatException("a packet larger than 64 KiB");
        }

        return (int)size;
    }

    /// <summary>The whole packet: the header for <paramref name="payload"/>, then the payload.</summary>
    public static byte[] Packet(long deviceIndex, long packetId, byte[] payload) {
        if (payload is null) {
            throw new ArgumentNullException(nameof(payload));
        }

        var packet = new byte[HeaderLength + payload.Length];
        Array.Copy(Magic, packet, Magic.Length);
        byte[] fields = new OpenRgbWriter().UInt32(deviceIndex).UInt32(packetId).UInt32(payload.Length).ToArray();
        Array.Copy(fields, 0, packet, Magic.Length, fields.Length);
        Array.Copy(payload, 0, packet, HeaderLength, payload.Length);
        return packet;
    }
}
#endif
