#if ENABLE_LIGHTING
using System;
using System.Collections.Generic;
using System.Text;
using FanControl.LianLi.Logging;
using FanControl.LianLi.Protocol;

namespace FanControl.LianLi.OpenRgb;

/// <summary>
/// One OpenRGB client's conversation with the server, with no I/O of its own: each packet the client
/// sent in, the packets to send back out. It keeps the protocol version the two agreed on (the lower of
/// the client's and <see cref="OpenRgbProtocol.MaximumVersion"/>; 0 until the client asks) and the name
/// the client gave. Requests are answered, commands go to the ring they name, SAVEMODE also keeps the
/// look in the saved looks' file, and a packet id the server does not know is ignored, as OpenRGB's own
/// server ignores one. A packet that names a controller the server does not have, or that does not
/// decode, throws a <see cref="FormatException"/>: the client is not one to keep talking to.
/// </summary>
internal sealed class OpenRgbSession {
    /// <summary>The longest client name kept.</summary>
    public const int MaximumClientNameLength = 64;

    // An empty profile or plugin list: its size (6, itself included) and a count of 0.
    private static readonly byte[] EmptyList = { 6, 0, 0, 0, 0, 0 };

    private readonly IReadOnlyList<OpenRgbRing> _rings;
    private readonly OpenRgbLookFile _looks;
    private readonly string _version;
    private readonly ILog _log;

    /// <summary>A conversation about <paramref name="rings"/>, by their index, for plugin <paramref name="version"/>.</summary>
    public OpenRgbSession(IReadOnlyList<OpenRgbRing> rings, OpenRgbLookFile looks, string version, ILog log) {
        _rings = rings ?? throw new ArgumentNullException(nameof(rings));
        _looks = looks ?? throw new ArgumentNullException(nameof(looks));
        _version = version ?? throw new ArgumentNullException(nameof(version));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <summary>The protocol version agreed with the client.</summary>
    public long Version { get; private set; }

    /// <summary>The name the client gave itself (SET_CLIENT_NAME), made printable, or null until it has.</summary>
    public string? ClientName { get; private set; }

    /// <summary>The packets to send back for packet <paramref name="packetId"/> about controller <paramref name="deviceIndex"/>.</summary>
    public IReadOnlyList<byte[]> Handle(long deviceIndex, long packetId, byte[] payload) {
        if (payload is null) {
            throw new ArgumentNullException(nameof(payload));
        }

        switch (packetId) {
            case OpenRgbProtocol.RequestProtocolVersion:
                // A client older than the version request sends none, and stays at 0.
                long requested = payload.Length >= 4 ? new OpenRgbReader(payload).UInt32() : 0;
                Version = Math.Min(requested, OpenRgbProtocol.MaximumVersion);
                return Reply(0, packetId, new OpenRgbWriter().UInt32(OpenRgbProtocol.MaximumVersion).ToArray());
            case OpenRgbProtocol.RequestControllerCount:
                var count = new OpenRgbWriter().UInt32(_rings.Count);
                if (Version >= 6) {
                    for (int i = 0; i < _rings.Count; i++) {
                        count.UInt32(i); // version 6 lists each controller's id, here its index
                    }
                }

                return Reply(0, packetId, count.ToArray());
            case OpenRgbProtocol.RequestControllerData:
                // The request carries the version the client wants the description in; a client that
                // sends none gets the one agreed.
                long protocol = payload.Length == 4 ? Math.Min(new OpenRgbReader(payload).UInt32(), OpenRgbProtocol.MaximumVersion) : Version;
                return Reply(deviceIndex, packetId, Ring(deviceIndex).Describe(_version, protocol));
            case OpenRgbProtocol.SetClientName:
                ClientName = Printable(payload);
                return Array.Empty<byte[]>();
            case OpenRgbProtocol.RequestProfileList:
            case OpenRgbProtocol.RequestPluginList:
                return Reply(0, packetId, EmptyList);
            case OpenRgbProtocol.UpdateLeds:
                Ring(deviceIndex).SetLeds(OpenRgbCommandDecoder.Leds(payload));
                return Array.Empty<byte[]>();
            case OpenRgbProtocol.UpdateZoneLeds:
                Ring(deviceIndex).SetLeds(OpenRgbCommandDecoder.ZoneLeds(payload));
                return Array.Empty<byte[]>();
            case OpenRgbProtocol.UpdateSingleLed:
                OpenRgbRing single = Ring(deviceIndex);
                int led = OpenRgbCommandDecoder.SingleLed(payload, out RgbColor color);
                single.SetLed(led, color);
                return Array.Empty<byte[]>();
            case OpenRgbProtocol.SetCustomMode:
                OpenRgbRing custom = Ring(deviceIndex);
                OpenRgbCommandDecoder.CustomMode(payload);
                custom.SetCustomMode();
                return Array.Empty<byte[]>();
            case OpenRgbProtocol.UpdateMode:
            case OpenRgbProtocol.SaveMode:
                OpenRgbRing ring = Ring(deviceIndex);
                OpenRgbModeSetting setting = OpenRgbCommandDecoder.Mode(payload, Version, out int index);
                ring.SetMode(index, setting);
                if (packetId == OpenRgbProtocol.SaveMode) {
                    _looks.Save(ring.Id, ring.Look, _log);
                }

                return Array.Empty<byte[]>();
            default:
                return Array.Empty<byte[]>();
        }
    }

    private OpenRgbRing Ring(long deviceIndex) {
        if (deviceIndex >= _rings.Count) {
            throw new FormatException("a controller the server does not have");
        }

        return _rings[(int)deviceIndex];
    }

    private static IReadOnlyList<byte[]> Reply(long deviceIndex, long packetId, byte[] payload)
        => new[] { OpenRgbProtocol.Packet(deviceIndex, packetId, payload) };

    // The name as the client sent it, up to its zero, every character that is not printable ASCII a
    // question mark, cut to the longest kept: it goes into the log.
    private static string Printable(byte[] payload) {
        string text = Encoding.UTF8.GetString(payload);
        int zero = text.IndexOf('\0');
        if (zero >= 0) {
            text = text.Substring(0, zero);
        }

        var name = new StringBuilder(Math.Min(text.Length, MaximumClientNameLength));
        for (int i = 0; i < text.Length && i < MaximumClientNameLength; i++) {
            name.Append(text[i] >= ' ' && text[i] <= '~' ? text[i] : '?');
        }

        return name.ToString();
    }
}
#endif
