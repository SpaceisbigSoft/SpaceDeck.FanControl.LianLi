using System;
using System.Collections.Generic;

namespace FanControl.LianLi.SpaceDeck;

/// <summary>
/// How the readings are grouped into devices for the SpaceDeck collector, the one place that
/// decides it. A device is every reading whose id shares the part before its second slash: the
/// plugin numbers each wired controller and cooler (<c>LianLi/1/coolant/temp</c>,
/// <c>LianLi/1/ch0/fan</c>) and keys each wireless device on its RF address
/// (<c>LianLi/wa1b2c3d4e5f6/f0/fan</c>), so that part is exactly one controller, cooler or wireless
/// fan group. The device's name is what its readings' names share, which every family starts with
/// its own description of the device; its kind is a cooler for a Galahad or HydroShift (whatever it
/// is called) and fans otherwise.
/// </summary>
internal static class SpaceDeckDeviceGrouping {
    // How the coolers' names start: the Galahad II Trinity and HydroShift LCD as "Lian Li Galahad",
    // the Galahad II Vision LCD as "Lian Li GA II LCD", the HydroShift II OLED Curve and a wireless
    // HydroShift II water block with "HydroShift".
    private static readonly string[] CoolerNames = { "Galahad", "GA II", "HydroShift" };

    /// <summary>
    /// The id of the device the reading <paramref name="sensorId"/> belongs to: the id up to its
    /// second slash. Null for an id with fewer than three parts or an empty first or second part,
    /// which names no device.
    /// </summary>
    public static string? HardwareIdOf(string sensorId) {
        if (sensorId is null) {
            throw new ArgumentNullException(nameof(sensorId));
        }

        int first = sensorId.IndexOf('/');
        if (first <= 0) {
            return null;
        }

        int second = sensorId.IndexOf('/', first + 1);
        if (second <= first + 1 || second == sensorId.Length - 1) {
            return null;
        }

        return sensorId.Substring(0, second);
    }

    /// <summary>
    /// The device's name: the words every one of its readings' names starts with, up to and
    /// including a <c>#</c> number if there is one (a controller with a single channel would
    /// otherwise be named for that channel). <c>Lian Li Galahad #2 Fan</c> and
    /// <c>Lian Li Galahad #2 Coolant</c> give <c>Lian Li Galahad #2</c>; a wireless group's control,
    /// named as the group, and its fans give the group's name. Empty when the names share no word.
    /// </summary>
    public static string HardwareNameOf(IReadOnlyList<string> sensorNames) {
        if (sensorNames is null) {
            throw new ArgumentNullException(nameof(sensorNames));
        }

        if (sensorNames.Count == 0) {
            return string.Empty;
        }

        string[] common = sensorNames[0].Split(' ');
        int length = common.Length;
        for (int n = 1; n < sensorNames.Count; n++) {
            string[] words = sensorNames[n].Split(' ');
            int shared = 0;
            while (shared < length && shared < words.Length && string.Equals(common[shared], words[shared], StringComparison.Ordinal)) {
                shared++;
            }

            length = shared;
        }

        for (int w = 0; w < length; w++) {
            if (common[w].StartsWith("#", StringComparison.Ordinal)) {
                length = w + 1;
                break;
            }
        }

        return string.Join(" ", common, 0, length);
    }

    /// <summary>
    /// <see cref="SpaceDeckHardware.CoolerKind"/> for a cooler - a device named as one, or one that
    /// reports a temperature, which only the coolers' coolant sensors do - and
    /// <see cref="SpaceDeckHardware.FansKind"/> for everything else.
    /// </summary>
    public static string KindOf(string hardwareName, bool reportsTemperature) {
        if (hardwareName is null) {
            throw new ArgumentNullException(nameof(hardwareName));
        }

        if (reportsTemperature) {
            return SpaceDeckHardware.CoolerKind;
        }

        foreach (string cooler in CoolerNames) {
            if (hardwareName.IndexOf(cooler, StringComparison.OrdinalIgnoreCase) >= 0) {
                return SpaceDeckHardware.CoolerKind;
            }
        }

        return SpaceDeckHardware.FansKind;
    }
}
