#if ENABLE_LIGHTING
using System;
using System.Collections.Generic;
using System.Linq;

namespace FanControl.LianLi.Protocol;

/// <summary>
/// One look of the Galahad II Vision's screen ring, as <see cref="Galahad2RingEncoder"/> sends it:
/// the mode, its speed and brightness on the cooler's own 0 to 4 scale, its direction as the cooler's
/// byte (0 clockwise, 1 counter-clockwise), and its colours - twelve, one per LED, for
/// <see cref="Galahad2RingMode.Direct"/>, up to four for an effect. Never changed once made, so one
/// thread can hand it to another as it is.
/// </summary>
internal sealed class Galahad2RingLook {
    /// <summary>The LEDs round the screen.</summary>
    public const int LedCount = 12;

    /// <summary>The most colours an effect takes.</summary>
    public const int MaximumEffectColors = 4;

    /// <summary>The top of the cooler's speed and brightness scales.</summary>
    public const int MaximumLevel = 4;

    /// <summary>The ring going clockwise, the cooler's direction byte 0.</summary>
    public const int Clockwise = 0;

    /// <summary>The ring going counter-clockwise, the cooler's direction byte 1.</summary>
    public const int CounterClockwise = 1;

    /// <summary>Create the look; every value is checked against what the cooler takes.</summary>
    public Galahad2RingLook(Galahad2RingMode mode, int speed, int brightness, int direction, IReadOnlyList<RgbColor> colors) {
        if (!Enum.IsDefined(typeof(Galahad2RingMode), mode)) {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        if (speed < 0 || speed > MaximumLevel) {
            throw new ArgumentOutOfRangeException(nameof(speed), "The speed is 0 to 4.");
        }

        if (brightness < 0 || brightness > MaximumLevel) {
            throw new ArgumentOutOfRangeException(nameof(brightness), "The brightness is 0 to 4.");
        }

        if (direction != Clockwise && direction != CounterClockwise) {
            throw new ArgumentOutOfRangeException(nameof(direction), "The direction is clockwise or counter-clockwise.");
        }

        if (colors is null) {
            throw new ArgumentNullException(nameof(colors));
        }

        if (mode == Galahad2RingMode.Direct ? colors.Count != LedCount : colors.Count > MaximumEffectColors) {
            throw new ArgumentException("A direct look has a colour per LED, an effect up to four.", nameof(colors));
        }

        Mode = mode;
        Speed = speed;
        Brightness = brightness;
        Direction = direction;
        Colors = colors.ToArray();
    }

    /// <summary>What the ring shows.</summary>
    public Galahad2RingMode Mode { get; }

    /// <summary>The effect's speed, 0 to 4.</summary>
    public int Speed { get; }

    /// <summary>The effect's brightness, 0 (off) to 4.</summary>
    public int Brightness { get; }

    /// <summary>The cooler's direction byte, <see cref="Clockwise"/> or <see cref="CounterClockwise"/>.</summary>
    public int Direction { get; }

    /// <summary>One colour per LED for a direct look, up to four for an effect.</summary>
    public IReadOnlyList<RgbColor> Colors { get; }

    /// <summary>A direct look of twelve LEDs all <paramref name="color"/>.</summary>
    public static Galahad2RingLook DirectOf(RgbColor color)
        => new Galahad2RingLook(Galahad2RingMode.Direct, 0, MaximumLevel, Clockwise, Enumerable.Repeat(color, LedCount).ToArray());
}
#endif
