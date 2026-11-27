#if ENABLE_LIGHTING
using System;
using System.Collections.Generic;
using FanControl.LianLi.Protocol;

namespace FanControl.LianLi.OpenRgb;

/// <summary>
/// The modes the screen ring offers OpenRGB, in the order OpenRGB lists and indexes them, with what
/// each has: a speed and a brightness on the cooler's 0 to 4 scale, a direction round the ring (OpenRGB's
/// left and right), and the colours it takes - one per LED for Direct, up to four of its own for the
/// coloured effects, none for the rainbows. Every mode can be saved (OpenRGB's "Save to device"), which
/// keeps the look for the plugin to show again at the next start.
/// </summary>
internal sealed class OpenRgbRingMode {
    /// <summary>MODE_FLAG_HAS_SPEED.</summary>
    public const long HasSpeedFlag = 1 << 0;

    /// <summary>MODE_FLAG_HAS_DIRECTION_LR.</summary>
    public const long HasDirectionFlag = 1 << 1;

    /// <summary>MODE_FLAG_HAS_BRIGHTNESS.</summary>
    public const long HasBrightnessFlag = 1 << 4;

    /// <summary>MODE_FLAG_HAS_PER_LED_COLOR.</summary>
    public const long PerLedColorFlag = 1 << 5;

    /// <summary>MODE_FLAG_HAS_MODE_SPECIFIC_COLOR.</summary>
    public const long ModeSpecificColorFlag = 1 << 6;

    /// <summary>MODE_FLAG_MANUAL_SAVE: OpenRGB offers to save the mode, which sends SAVEMODE.</summary>
    public const long ManualSaveFlag = 1 << 8;

    /// <summary>MODE_COLORS_NONE.</summary>
    public const long NoColors = 0;

    /// <summary>MODE_COLORS_PER_LED.</summary>
    public const long PerLedColors = 1;

    /// <summary>MODE_COLORS_MODE_SPECIFIC.</summary>
    public const long ModeSpecificColors = 2;

    /// <summary>MODE_DIRECTION_LEFT: counter-clockwise round the ring.</summary>
    public const int Left = 0;

    /// <summary>MODE_DIRECTION_RIGHT: clockwise round the ring.</summary>
    public const int Right = 1;

    private const long EffectFlags = HasSpeedFlag | HasBrightnessFlag | ManualSaveFlag;
    private const int EffectColors = Galahad2RingLook.MaximumEffectColors;

    /// <summary>Every mode, by its OpenRGB index. The value is the cooler's own mode byte where it has one.</summary>
    public static readonly IReadOnlyList<OpenRgbRingMode> All = new[] {
        new OpenRgbRingMode("Direct", 0, Galahad2RingMode.Direct, PerLedColorFlag | ManualSaveFlag, 0, 0, PerLedColors),
        new OpenRgbRingMode("Off", 0, Galahad2RingMode.Off, ManualSaveFlag, 0, 0, NoColors),
        new OpenRgbRingMode("Static", 3, Galahad2RingMode.Static, HasBrightnessFlag | ModeSpecificColorFlag | ManualSaveFlag, 1, EffectColors, ModeSpecificColors),
        new OpenRgbRingMode("Breathing", 4, Galahad2RingMode.Breathing, EffectFlags | ModeSpecificColorFlag, 1, EffectColors, ModeSpecificColors),
        new OpenRgbRingMode("Rainbow", 1, Galahad2RingMode.Rainbow, EffectFlags | HasDirectionFlag, 0, 0, NoColors),
        new OpenRgbRingMode("Rainbow Morph", 2, Galahad2RingMode.RainbowMorph, EffectFlags, 0, 0, NoColors),
        new OpenRgbRingMode("Runway", 5, Galahad2RingMode.Runway, EffectFlags | HasDirectionFlag | ModeSpecificColorFlag, 1, EffectColors, ModeSpecificColors),
        new OpenRgbRingMode("Meteor", 6, Galahad2RingMode.Meteor, EffectFlags | HasDirectionFlag | ModeSpecificColorFlag, 1, EffectColors, ModeSpecificColors),
    };

    private OpenRgbRingMode(string name, int value, Galahad2RingMode ringMode, long flags, int colorsMinimum, int colorsMaximum, long colorMode) {
        Name = name;
        Value = value;
        RingMode = ringMode;
        Flags = flags;
        ColorsMinimum = colorsMinimum;
        ColorsMaximum = colorsMaximum;
        ColorMode = colorMode;
    }

    /// <summary>The mode's name, which identifies it in an update from a client.</summary>
    public string Name { get; }

    /// <summary>The mode's value field: the cooler's mode byte, 0 for Direct and Off.</summary>
    public int Value { get; }

    /// <summary>What the ring shows in this mode.</summary>
    public Galahad2RingMode RingMode { get; }

    /// <summary>The MODE_FLAG bits.</summary>
    public long Flags { get; }

    /// <summary>The fewest colours of its own the mode takes.</summary>
    public int ColorsMinimum { get; }

    /// <summary>The most colours of its own the mode takes.</summary>
    public int ColorsMaximum { get; }

    /// <summary>The MODE_COLORS value.</summary>
    public long ColorMode { get; }

    /// <summary>Whether the mode has a speed.</summary>
    public bool HasSpeed => (Flags & HasSpeedFlag) != 0;

    /// <summary>Whether the mode has a brightness.</summary>
    public bool HasBrightness => (Flags & HasBrightnessFlag) != 0;

    /// <summary>Whether the mode goes one way or the other round the ring.</summary>
    public bool HasDirection => (Flags & HasDirectionFlag) != 0;

    /// <summary>The top of the speed scale: 4, or 0 for a mode without one.</summary>
    public int SpeedMaximum => HasSpeed ? Galahad2RingLook.MaximumLevel : 0;

    /// <summary>The top of the brightness scale: 4, or 0 for a mode without one.</summary>
    public int BrightnessMaximum => HasBrightness ? Galahad2RingLook.MaximumLevel : 0;

    /// <summary>The index of the mode that shows <paramref name="ringMode"/>.</summary>
    public static int IndexOf(Galahad2RingMode ringMode) {
        for (int i = 0; i < All.Count; i++) {
            if (All[i].RingMode == ringMode) {
                return i;
            }
        }

        throw new ArgumentOutOfRangeException(nameof(ringMode));
    }
}
#endif
