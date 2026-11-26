using System;
using FanControl.Plugins;

namespace FanControl.LianLi.Tests.Fakes;

/// <summary>
/// A sensor or control as FanControl holds it, with an id, name and value a test sets; reading the
/// id or the value throws when a test says, for the guards around the SpaceDeck readings.
/// </summary>
internal sealed class FakePluginSensor : IPluginControlSensor {
    private readonly string? _id;
    private float? _value;

    public FakePluginSensor(string? id, string? name, float? value = null) {
        _id = id;
        Name = name!; // a host sensor may report no name; the readings must cope
        _value = value;
    }

    public bool IdFails { get; set; }

    public bool ValueFails { get; set; }

    public string Id => IdFails ? throw new InvalidOperationException("the id cannot be read") : _id!;

    public string Name { get; }

    public float? Value {
        get => ValueFails ? throw new InvalidOperationException("the value cannot be read") : _value;
        set => _value = value;
    }

    public void Update() {
    }

    public void Set(float val) => _value = val;

    public void Reset() => _value = null;
}
