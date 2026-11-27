using System;

namespace FanControl.LianLi.SpaceDeck;

/// <summary>
/// A screen on a device in the SpaceDeck collector's catalog (the hardware entry's optional
/// <c>screen</c> field): the size of the frames SpaceDeck draws for it and their format. The
/// collector takes a width and height of 1 to 1024 and the format <c>jpeg</c>, the only one.
/// </summary>
internal sealed class SpaceDeckScreen {
    /// <summary>The Galahad II Vision LCD's round 480x480 screen, which shows JPEG frames.</summary>
    public static readonly SpaceDeckScreen Galahad2Lcd = new SpaceDeckScreen(480, 480, "jpeg");

    /// <summary>Create the entry. The size is checked against the collector's range.</summary>
    public SpaceDeckScreen(int width, int height, string format) {
        if (width < 1 || width > 1024) {
            throw new ArgumentOutOfRangeException(nameof(width), "A screen is 1 to 1024 pixels wide.");
        }

        if (height < 1 || height > 1024) {
            throw new ArgumentOutOfRangeException(nameof(height), "A screen is 1 to 1024 pixels high.");
        }

        Width = width;
        Height = height;
        Format = format ?? throw new ArgumentNullException(nameof(format));
    }

    /// <summary>The width of a frame, in pixels.</summary>
    public int Width { get; }

    /// <summary>The height of a frame, in pixels.</summary>
    public int Height { get; }

    /// <summary>The frames' format, <c>jpeg</c>.</summary>
    public string Format { get; }
}
