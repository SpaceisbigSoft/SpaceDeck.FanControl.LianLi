#if ENABLE_LIGHTING
using System;
using System.Collections.Generic;
using System.Linq;
using FanControl.LianLi.Protocol;

namespace FanControl.LianLi.OpenRgb;

/// <summary>
/// What one of the ring's modes is set to, as OpenRGB shows it: its speed, brightness and direction
/// (OpenRGB's <see cref="OpenRgbRingMode.Left"/> or <see cref="OpenRgbRingMode.Right"/>) and its own
/// colours. Never changed once made; a client's update makes a new one.
/// </summary>
internal sealed class OpenRgbModeSetting {
    // A colour the mode needs before anyone has picked one: white, as lian-li-linux's server starts.
    private static readonly RgbColor White = new RgbColor(255, 255, 255);

    /// <summary>Create the setting; every value is checked against what <paramref name="mode"/> takes.</summary>
    public OpenRgbModeSetting(OpenRgbRingMode mode, int speed, int brightness, int direction, IReadOnlyList<RgbColor> colors) {
        Mode = mode ?? throw new ArgumentNullException(nameof(mode));
        if (speed < 0 || speed > mode.SpeedMaximum) {
            throw new ArgumentOutOfRangeException(nameof(speed));
        }

        if (brightness < 0 || brightness > mode.BrightnessMaximum) {
            throw new ArgumentOutOfRangeException(nameof(brightness));
        }

        if (mode.HasDirection ? direction != OpenRgbRingMode.Left && direction != OpenRgbRingMode.Right : direction != 0) {
            throw new ArgumentOutOfRangeException(nameof(direction));
        }

        if (colors is null) {
            throw new ArgumentNullException(nameof(colors));
        }

        if (colors.Count < mode.ColorsMinimum || colors.Count > mode.ColorsMaximum) {
            throw new ArgumentException("The mode takes " + mode.ColorsMinimum + " to " + mode.ColorsMaximum + " colours.", nameof(colors));
        }

        Speed = speed;
        Brightness = brightness;
        Direction = direction;
        Colors = colors.ToArray();
    }

    /// <summary>The mode this is a setting of.</summary>
    public OpenRgbRingMode Mode { get; }

    /// <summary>The speed, 0 to the mode's maximum.</summary>
    public int Speed { get; }

    /// <summary>The brightness, 0 to the mode's maximum.</summary>
    public int Brightness { get; }

    /// <summary>OpenRGB's direction, or 0 for a mode without one.</summary>
    public int Direction { get; }

    /// <summary>The mode's own colours.</summary>
    public IReadOnlyList<RgbColor> Colors { get; }

    /// <summary>The setting a mode has before anyone has set it: middle speed, full brightness, clockwise, white.</summary>
    public static OpenRgbModeSetting DefaultFor(OpenRgbRingMode mode) {
        if (mode is null) {
            throw new ArgumentNullException(nameof(mode));
        }

        return new OpenRgbModeSetting(
            mode,
            mode.SpeedMaximum / 2,
            mode.BrightnessMaximum,
            mode.HasDirection ? OpenRgbRingMode.Right : 0,
            Enumerable.Repeat(White, mode.ColorsMinimum).ToArray());
    }

    /// <summary>
    /// The setting of the mode that shows <paramref name="look"/>, as close to it as the mode has: a
    /// value the mode does not have is left at its default, and a missing colour is white.
    /// </summary>
    public static OpenRgbModeSetting From(Galahad2RingLook look) {
        if (look is null) {
            throw new ArgumentNullException(nameof(look));
        }

        OpenRgbRingMode mode = OpenRgbRingMode.All[OpenRgbRingMode.IndexOf(look.Mode)];
        OpenRgbModeSetting defaults = DefaultFor(mode);
        var colors = look.Colors.Take(mode.ColorsMaximum).ToList();
        while (colors.Count < mode.ColorsMinimum) {
            colors.Add(White);
        }

        return new OpenRgbModeSetting(
            mode,
            mode.HasSpeed ? look.Speed : defaults.Speed,
            mode.HasBrightness ? look.Brightness : defaults.Brightness,
            mode.HasDirection ? (look.Direction == Galahad2RingLook.CounterClockwise ? OpenRgbRingMode.Left : OpenRgbRingMode.Right) : 0,
            colors);
    }

    /// <summary>
    /// The look this setting puts on the ring; a direct one takes <paramref name="leds"/>, one colour
    /// per LED. A mode without a brightness shows at full brightness.
    /// </summary>
    public Galahad2RingLook Look(IReadOnlyList<RgbColor> leds) {
        if (leds is null) {
            throw new ArgumentNullException(nameof(leds));
        }

        return new Galahad2RingLook(
            Mode.RingMode,
            Speed,
            Mode.HasBrightness ? Brightness : Galahad2RingLook.MaximumLevel,
            Direction == OpenRgbRingMode.Left && Mode.HasDirection ? Galahad2RingLook.CounterClockwise : Galahad2RingLook.Clockwise,
            Mode.RingMode == Galahad2RingMode.Direct ? leds : Colors);
    }
}
#endif
