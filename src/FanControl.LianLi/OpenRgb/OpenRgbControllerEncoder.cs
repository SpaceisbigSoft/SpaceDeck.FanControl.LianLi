#if ENABLE_LIGHTING
using System;
using System.Collections.Generic;
using System.Globalization;
using FanControl.LianLi.Protocol;

namespace FanControl.LianLi.OpenRgb;

/// <summary>
/// The controller description the OpenRGB SDK sends for REQUEST_CONTROLLER_DATA, for one Galahad II
/// Vision's screen ring, as a pure function of the ring's state and the protocol version: a cooler
/// named "Lian Li GA II LCD" with every mode of <see cref="OpenRgbRingMode.All"/>, one linear zone
/// "Screen ring" of twelve LEDs, and their colours. The layout, field by field and version by version,
/// is lian-li-linux's (its <c>build_controller_zones</c>, <c>write_zone</c> and <c>ModeData::write</c>),
/// checked against its tests: the description starts with its own size, that field included.
/// </summary>
internal static class OpenRgbControllerEncoder {
    /// <summary>The controller's name, as SpaceDeck and the plugin name the cooler.</summary>
    public const string Name = "Lian Li GA II LCD";

    /// <summary>The ring's one zone.</summary>
    public const string ZoneName = "Screen ring";

    // DEVICE_TYPE_COOLER, and ZONE_TYPE_LINEAR: the ring is a line of LEDs that closes on itself.
    private const long CoolerType = 3;
    private const long LinearZone = 1;

    private const string Vendor = "Lian Li";
    private const string Description = "The ring round the screen of a Lian Li GA II LCD, through FanControl's Lian Li plugin";

    /// <summary>
    /// The description of the ring <paramref name="serial"/> (the cooler's <c>LianLi/{index}</c>), built
    /// by plugin <paramref name="version"/>, showing mode <paramref name="active"/> with
    /// <paramref name="settings"/>, one per mode, and <paramref name="leds"/>, for a client of
    /// <paramref name="protocol"/>.
    /// </summary>
    public static byte[] Encode(
        string serial, string version, int active, IReadOnlyList<OpenRgbModeSetting> settings, IReadOnlyList<RgbColor> leds, long protocol) {
        if (serial is null) {
            throw new ArgumentNullException(nameof(serial));
        }

        if (version is null) {
            throw new ArgumentNullException(nameof(version));
        }

        if (settings is null) {
            throw new ArgumentNullException(nameof(settings));
        }

        if (leds is null) {
            throw new ArgumentNullException(nameof(leds));
        }

        if (settings.Count != OpenRgbRingMode.All.Count) {
            throw new ArgumentException("One setting is needed for each mode.", nameof(settings));
        }

        if (leds.Count != Galahad2RingLook.LedCount) {
            throw new ArgumentException("One colour is needed for each LED.", nameof(leds));
        }

        var data = new OpenRgbWriter();
        data.UInt32(0); // the size, patched in at the end
        data.UInt32(CoolerType);
        data.String(Name);
        if (protocol >= 1) {
            data.String(Vendor);
        }

        data.String(Description);
        data.String(version);
        data.String(serial);
        data.String("FanControl Lian Li plugin: " + serial);

        data.UInt16(settings.Count);
        data.UInt32(active);
        foreach (OpenRgbModeSetting setting in settings) {
            WriteMode(data, setting, protocol);
        }

        data.UInt16(1); // one zone
        data.String(ZoneName);
        data.UInt32(LinearZone);
        data.UInt32(leds.Count); // the fewest LEDs, the most, and how many: the ring has twelve, fixed
        data.UInt32(leds.Count);
        data.UInt32(leds.Count);
        data.UInt16(0); // no matrix map
        if (protocol >= 4) {
            data.UInt16(0); // no segments
        }

        if (protocol >= 5) {
            data.UInt32(0); // zone flags
        }

        if (protocol >= 6) {
            data.UInt32(-1); // no mode of the zone's own is active
            data.UInt16(0);  // and it has none
            data.String(string.Empty); // the zone's alternate name
        }

        data.UInt16(leds.Count);
        for (int i = 0; i < leds.Count; i++) {
            data.String(ZoneName + " LED " + (i + 1).ToString(CultureInfo.InvariantCulture));
            if (protocol < 6) {
                data.UInt32(i); // the LED's value
            }
        }

        data.Colors(leds);
        if (protocol >= 5) {
            data.UInt16(0); // no alternate LED names
            data.UInt32(0); // controller flags
        }

        if (protocol >= 6) {
            data.String(string.Empty); // the controller's alternate name
            data.UInt32(1);            // an empty device-specific configuration: its length with the zero
            data.Byte(0);
        }

        return data.Patch(0, data.Length).ToArray();
    }

    /// <summary>One mode's description, as in the controller description and in an UPDATEMODE.</summary>
    public static void WriteMode(OpenRgbWriter data, OpenRgbModeSetting setting, long protocol) {
        if (data is null) {
            throw new ArgumentNullException(nameof(data));
        }

        if (setting is null) {
            throw new ArgumentNullException(nameof(setting));
        }

        OpenRgbRingMode mode = setting.Mode;
        data.String(mode.Name);
        if (protocol < 6) {
            data.UInt32(mode.Value);
        }

        data.UInt32(mode.Flags);
        data.UInt32(0); // the slowest speed
        data.UInt32(mode.SpeedMaximum);
        if (protocol >= 3) {
            data.UInt32(0); // the dimmest brightness
            data.UInt32(mode.BrightnessMaximum);
        }

        data.UInt32(mode.ColorsMinimum);
        data.UInt32(mode.ColorsMaximum);
        data.UInt32(setting.Speed);
        if (protocol >= 3) {
            data.UInt32(setting.Brightness);
        }

        data.UInt32(setting.Direction);
        data.UInt32(mode.ColorMode);
        data.Colors(setting.Colors);
    }
}
#endif
