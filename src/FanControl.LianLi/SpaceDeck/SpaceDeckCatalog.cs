using System;
using System.Collections.Generic;
using System.Linq;

namespace FanControl.LianLi.SpaceDeck;

/// <summary>
/// The devices and readings the plugin sends the SpaceDeck collector, in the order its
/// <c>values</c> lines follow, with the <c>catalog</c> line that describes them encoded once. Two
/// catalogs describe the same set exactly when their lines are equal, which is how the publisher
/// tells that the set has changed.
/// </summary>
internal sealed class SpaceDeckCatalog {
    /// <summary>Create the catalog from entries already valid for the pipe.</summary>
    public SpaceDeckCatalog(IEnumerable<SpaceDeckHardware> hardware, IEnumerable<SpaceDeckSensor> sensors) {
        Hardware = (hardware ?? throw new ArgumentNullException(nameof(hardware))).ToArray();
        Sensors = (sensors ?? throw new ArgumentNullException(nameof(sensors))).ToArray();
        Line = SpaceDeckPipeEncoder.Catalog(Hardware, Sensors);
    }

    /// <summary>Every device with at least one reading, in order of its first reading.</summary>
    public IReadOnlyList<SpaceDeckHardware> Hardware { get; }

    /// <summary>Every reading, grouped by device.</summary>
    public IReadOnlyList<SpaceDeckSensor> Sensors { get; }

    /// <summary>The <c>catalog</c> line, ending in its newline.</summary>
    public string Line { get; }
}
