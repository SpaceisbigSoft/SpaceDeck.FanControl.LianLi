using System;

namespace FanControl.LianLi.SpaceDeck;

/// <summary>
/// One reading as the SpaceDeck collector's catalog lists it: a sensor the plugin registered with
/// FanControl, under its FanControl id.
/// </summary>
internal sealed class SpaceDeckSensor {
    /// <summary>A temperature in degrees Celsius.</summary>
    public const string TemperatureKind = "temperature";

    /// <summary>A fan or pump speed in RPM.</summary>
    public const string FanKind = "fan";

    /// <summary>The duty, in percent, FanControl is applying to a control.</summary>
    public const string ControlKind = "control";

    /// <summary>Create the entry. Every string is required and already valid for the pipe.</summary>
    public SpaceDeckSensor(string id, string hardwareId, string name, string kind) {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        HardwareId = hardwareId ?? throw new ArgumentNullException(nameof(hardwareId));
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Kind = kind ?? throw new ArgumentNullException(nameof(kind));
    }

    /// <summary>The sensor's FanControl id, without the plugin's Name.</summary>
    public string Id { get; }

    /// <summary>The <see cref="SpaceDeckHardware.Id"/> of the device it belongs to.</summary>
    public string HardwareId { get; }

    /// <summary>The sensor's name.</summary>
    public string Name { get; }

    /// <summary><see cref="TemperatureKind"/>, <see cref="FanKind"/> or <see cref="ControlKind"/>.</summary>
    public string Kind { get; }
}
