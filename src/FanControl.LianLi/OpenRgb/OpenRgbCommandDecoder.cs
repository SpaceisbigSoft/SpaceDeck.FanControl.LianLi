#if ENABLE_LIGHTING
using System;
using System.Collections.Generic;
using FanControl.LianLi.Protocol;

namespace FanControl.LianLi.OpenRgb;

/// <summary>
/// Pure decoders for the commands an OpenRGB client sends the ring, each checked whole: the fields
/// lian-li-linux reads (its <c>Command::decode</c>), the size field that starts them, no byte left
/// over, and what the ring can take - its one zone, its twelve LEDs, one of its modes by index and by
/// name, and that mode's colours and direction. Anything else throws a <see cref="FormatException"/>,
/// for the server to close the client.
/// </summary>
internal static class OpenRgbCommandDecoder {
    /// <summary>UPDATELEDS: a colour for every LED.</summary>
    public static IReadOnlyList<RgbColor> Leds(byte[] payload) {
        var reader = new OpenRgbReader(payload);
        reader.Size();
        IReadOnlyList<RgbColor> colors = LedColors(reader);
        reader.End();
        return colors;
    }

    /// <summary>UPDATEZONELEDS: a colour for every LED of the one zone.</summary>
    public static IReadOnlyList<RgbColor> ZoneLeds(byte[] payload) {
        var reader = new OpenRgbReader(payload);
        reader.Size();
        if (reader.UInt32() != 0) {
            throw new FormatException("a zone the ring does not have");
        }

        IReadOnlyList<RgbColor> colors = LedColors(reader);
        reader.End();
        return colors;
    }

    /// <summary>UPDATESINGLELED: one LED's colour, with no size field.</summary>
    public static int SingleLed(byte[] payload, out RgbColor color) {
        var reader = new OpenRgbReader(payload);
        long led = reader.UInt32();
        color = reader.Color();
        reader.End();
        if (led >= Galahad2RingLook.LedCount) {
            throw new FormatException("an LED the ring does not have");
        }

        return (int)led;
    }

    /// <summary>SETCUSTOMMODE, which carries nothing.</summary>
    public static void CustomMode(byte[] payload) => new OpenRgbReader(payload).End();

    /// <summary>
    /// UPDATEMODE and SAVEMODE: the index of the mode and its description, in the layout of
    /// <paramref name="protocol"/>. A speed or brightness past the mode's top is taken as its top, as
    /// lian-li-linux takes it; a mode without a brightness, or a client older than version 3, which
    /// sends none, gets the mode's own.
    /// </summary>
    public static OpenRgbModeSetting Mode(byte[] payload, long protocol, out int index) {
        var reader = new OpenRgbReader(payload);
        reader.Size();
        long modeIndex = reader.UInt32();
        if (modeIndex >= OpenRgbRingMode.All.Count) {
            throw new FormatException("a mode the ring does not have");
        }

        OpenRgbRingMode mode = OpenRgbRingMode.All[(int)modeIndex];
        if (reader.String() != mode.Name) {
            throw new FormatException("a mode whose name is not its index's");
        }

        if (protocol < 6) {
            _ = reader.UInt32(); // the value, which the index already says
        }

        _ = reader.UInt32(); // the flags, the speed range, the brightness range and the colour range are
        _ = reader.UInt32(); // the mode's own, as the ring described it; a client cannot change them
        _ = reader.UInt32();
        if (protocol >= 3) {
            _ = reader.UInt32();
            _ = reader.UInt32();
        }

        _ = reader.UInt32();
        _ = reader.UInt32();
        long speed = reader.UInt32();
        long brightness = protocol >= 3 ? reader.UInt32() : mode.BrightnessMaximum;
        long direction = reader.UInt32();
        long colorMode = reader.UInt32();
        IReadOnlyList<RgbColor> colors = reader.Colors();
        reader.End();

        if (colorMode != mode.ColorMode) {
            throw new FormatException("a mode with colours other than its own");
        }

        if (colors.Count < mode.ColorsMinimum || colors.Count > mode.ColorsMaximum) {
            throw new FormatException("a mode with more or fewer colours than it takes");
        }

        if (mode.HasDirection && direction != OpenRgbRingMode.Left && direction != OpenRgbRingMode.Right) {
            throw new FormatException("a direction the ring does not have");
        }

        index = (int)modeIndex;
        return new OpenRgbModeSetting(
            mode,
            (int)Math.Min(speed, mode.SpeedMaximum),
            (int)Math.Min(brightness, mode.BrightnessMaximum),
            mode.HasDirection ? (int)direction : 0,
            colors);
    }

    private static IReadOnlyList<RgbColor> LedColors(OpenRgbReader reader) {
        IReadOnlyList<RgbColor> colors = reader.Colors();
        if (colors.Count != Galahad2RingLook.LedCount) {
            throw new FormatException("a colour count that is not the ring's twelve LEDs");
        }

        return colors;
    }
}
#endif
