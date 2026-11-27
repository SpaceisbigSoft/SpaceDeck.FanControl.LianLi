#if ENABLE_LIGHTING
using System;
using System.Collections.Generic;
using System.Linq;
using FanControl.LianLi.Devices;
using FanControl.LianLi.Protocol;

namespace FanControl.LianLi.OpenRgb;

/// <summary>
/// One Galahad II Vision's screen ring as OpenRGB sees it: the active mode, what every mode is set to,
/// and the twelve LEDs' colours. It starts from the look the cooler last showed through OpenRGB (its
/// handoff's last look, a saved one at a start), or Direct in white when there is none. Every change
/// that alters what the ring shows hands the new look to the cooler's worker through its
/// <see cref="CoolerHandoff"/>, newest first, and never writes to the cooler itself. Safe from every
/// client's thread at once.
/// </summary>
internal sealed class OpenRgbRing {
    private static readonly RgbColor White = new RgbColor(255, 255, 255);

    private readonly object _gate = new object();
    private readonly CoolerHandoff _handoff;

    // Under _gate.
    private readonly OpenRgbModeSetting[] _settings;
    private readonly RgbColor[] _leds;
    private int _active;

    /// <summary>The ring of the cooler <paramref name="id"/>, whose looks go through <paramref name="handoff"/>.</summary>
    public OpenRgbRing(string id, CoolerHandoff handoff) {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        _handoff = handoff ?? throw new ArgumentNullException(nameof(handoff));
        _settings = OpenRgbRingMode.All.Select(OpenRgbModeSetting.DefaultFor).ToArray();
        _leds = Enumerable.Repeat(White, Galahad2RingLook.LedCount).ToArray();
        if (handoff.LastLook is Galahad2RingLook last) {
            _active = OpenRgbRingMode.IndexOf(last.Mode);
            _settings[_active] = OpenRgbModeSetting.From(last);
            if (last.Mode == Galahad2RingMode.Direct) {
                last.Colors.ToArray().CopyTo(_leds, 0);
            }
        }
    }

    /// <summary>The cooler's id, <c>LianLi/{index}</c>, the controller's serial and the key its look is saved under.</summary>
    public string Id { get; }

    /// <summary>What the ring shows now.</summary>
    public Galahad2RingLook Look {
        get {
            lock (_gate) {
                return _settings[_active].Look(_leds);
            }
        }
    }

    /// <summary>The controller description for a client of <paramref name="protocol"/>.</summary>
    public byte[] Describe(string version, long protocol) {
        lock (_gate) {
            return OpenRgbControllerEncoder.Encode(Id, version, _active, _settings, _leds, protocol);
        }
    }

    /// <summary>Make mode <paramref name="index"/> active with <paramref name="setting"/>, and show it.</summary>
    public void SetMode(int index, OpenRgbModeSetting setting) {
        if (index < 0 || index >= OpenRgbRingMode.All.Count) {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        if (setting is null) {
            throw new ArgumentNullException(nameof(setting));
        }

        if (!ReferenceEquals(setting.Mode, OpenRgbRingMode.All[index])) {
            throw new ArgumentException("The setting is another mode's.", nameof(setting));
        }

        lock (_gate) {
            _active = index;
            _settings[index] = setting;
            Show();
        }
    }

    /// <summary>SETCUSTOMMODE: make Direct active, as a client does before it sets the LEDs one by one.</summary>
    public void SetCustomMode() {
        lock (_gate) {
            _active = OpenRgbRingMode.IndexOf(Galahad2RingMode.Direct);
            Show();
        }
    }

    /// <summary>Set every LED's colour; shown when Direct is the active mode, and kept for when it is.</summary>
    public void SetLeds(IReadOnlyList<RgbColor> colors) {
        if (colors is null) {
            throw new ArgumentNullException(nameof(colors));
        }

        if (colors.Count != Galahad2RingLook.LedCount) {
            throw new ArgumentException("One colour is needed for each LED.", nameof(colors));
        }

        lock (_gate) {
            colors.ToArray().CopyTo(_leds, 0);
            ShowIfDirect();
        }
    }

    /// <summary>Set one LED's colour; shown when Direct is the active mode.</summary>
    public void SetLed(int led, RgbColor color) {
        if (led < 0 || led >= Galahad2RingLook.LedCount) {
            throw new ArgumentOutOfRangeException(nameof(led));
        }

        lock (_gate) {
            _leds[led] = color;
            ShowIfDirect();
        }
    }

    // Caller holds _gate, so two clients' looks reach the handoff in the order they were made.
    private void ShowIfDirect() {
        if (_settings[_active].Mode.RingMode == Galahad2RingMode.Direct) {
            Show();
        }
    }

    private void Show() => _handoff.OfferLook(_settings[_active].Look(_leds));
}
#endif
