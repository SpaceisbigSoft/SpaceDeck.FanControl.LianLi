#if ENABLE_LIGHTING
using System;
using System.Collections.Generic;

namespace FanControl.LianLi.Protocol;

/// <summary>
/// Pure encoder for the Galahad II Vision's screen ring, the twelve LEDs round its LCD, as the
/// OpenRGB server drives it: a direct look as 0x14 SetLEDColors on the screen path, one colour per
/// LED in B packets (<see cref="Galahad2ScreenEncoder"/>), which overrides any built-in effect until
/// the next 0x83; any other look as one 0x83 SetPumpLighting command packet, the cooler's own effect
/// with up to four colours, its speed, brightness and direction. Neither has a reply. No I/O and no
/// state.
/// </summary>
internal static class Galahad2RingEncoder {
    // 0x83 SetPumpLighting: a 19-byte payload in one command packet.
    private const byte SetPumpLightCommand = 0x83;
    private const int PumpPayloadLength = 19;

    // 0x14 SetLEDColors: byte 0 picks the path (0 the screen ring, 1 the fans on the cooler's header),
    // then eight colour slots that must be there but are not used, then R,G,B per LED.
    private const byte SetLedColorsCommand = 0x14;
    private const byte ScreenPath = 0;
    private const int UnusedColorSlots = 8;

    /// <summary>
    /// The scope byte of every 0x83 the ring is sent: outer (1). On the Vision only outer (1) or all (2)
    /// light the ring, and inner (0), which L-Connect writes for it, is taken but shows nothing
    /// (ll-control's live test, in the register note); outer names the ring and nothing else.
    /// </summary>
    public const byte RingScope = 1;

    private const byte SourceMcu = 0;            // the cooler's own MCU drives the LEDs, so these colours show
    private const byte StaticModeByte = 3;       // an Off look goes out as a dark Static one, disabled

    /// <summary>The packets that put <paramref name="look"/> on the ring, in the order they are written.</summary>
    public static IReadOnlyList<byte[]> Encode(Galahad2RingLook look) {
        if (look is null) {
            throw new ArgumentNullException(nameof(look));
        }

        return look.Mode == Galahad2RingMode.Direct
            ? Galahad2ScreenEncoder.EncodeCommand(SetLedColorsCommand, LedColors(look.Colors))
            : new[] { CommandPacket.Build(SetPumpLightCommand, PumpLight(look)) };
    }

    private static byte[] LedColors(IReadOnlyList<RgbColor> colors) {
        // Length (LEDs + 8) x 3 + 1, L-Connect's own for the Vision's ring: 61 bytes for twelve LEDs.
        var payload = new byte[1 + ((colors.Count + UnusedColorSlots) * 3)];
        payload[0] = ScreenPath;
        int at = 1 + (UnusedColorSlots * 3); // bytes 1-24 stay zero
        foreach (RgbColor color in colors) {
            payload[at] = color.R;
            payload[at + 1] = color.G;
            payload[at + 2] = color.B;
            at += 3;
        }

        return payload;
    }

    private static byte[] PumpLight(Galahad2RingLook look) {
        bool off = look.Mode == Galahad2RingMode.Off;
        var payload = new byte[PumpPayloadLength];
        payload[0] = RingScope;
        payload[1] = off ? StaticModeByte : ModeByte(look.Mode);
        payload[2] = off ? (byte)0 : (byte)look.Brightness;
        payload[3] = off ? (byte)0 : (byte)look.Speed;
        if (!off) {
            for (int i = 0; i < look.Colors.Count; i++) {
                int at = 4 + (i * 3); // payload[4..15] = four R,G,B triples
                payload[at] = look.Colors[i].R;
                payload[at + 1] = look.Colors[i].G;
                payload[at + 2] = look.Colors[i].B;
            }

            payload[16] = (byte)look.Direction;
        }

        payload[17] = off ? (byte)1 : (byte)0; // disabled: 1 turns the ring off
        payload[18] = SourceMcu;
        return payload;
    }

    // The cooler's effect bytes (its 0x82 list on fw 1.6, lian-li-linux's mode bytes).
    private static byte ModeByte(Galahad2RingMode mode) {
        switch (mode) {
            case Galahad2RingMode.Rainbow:
                return 1;
            case Galahad2RingMode.RainbowMorph:
                return 2;
            case Galahad2RingMode.Breathing:
                return 4;
            case Galahad2RingMode.Runway:
                return 5;
            case Galahad2RingMode.Meteor:
                return 6;
            default:
                return StaticModeByte;
        }
    }
}
#endif
