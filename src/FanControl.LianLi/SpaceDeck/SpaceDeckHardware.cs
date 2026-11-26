using System;

namespace FanControl.LianLi.SpaceDeck;

/// <summary>
/// One device as the SpaceDeck collector's catalog lists it: a controller, a cooler or a wireless
/// fan group, under the id its sensors share (see <see cref="SpaceDeckDeviceGrouping"/>).
/// </summary>
internal sealed class SpaceDeckHardware {
    /// <summary>The kind for a Galahad or HydroShift cooler.</summary>
    public const string CoolerKind = "cooler";

    /// <summary>The kind for every other device: a fan controller or a fan group.</summary>
    public const string FansKind = "fans";

    /// <summary>Create the entry. Every string is required and already valid for the pipe.</summary>
    public SpaceDeckHardware(string id, string name, string kind) {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Kind = kind ?? throw new ArgumentNullException(nameof(kind));
    }

    /// <summary>The id the device's sensors share, such as <c>LianLi/1</c>.</summary>
    public string Id { get; }

    /// <summary>The device's name, such as <c>Lian Li Galahad #2</c>.</summary>
    public string Name { get; }

    /// <summary><see cref="CoolerKind"/> or <see cref="FansKind"/>.</summary>
    public string Kind { get; }
}
