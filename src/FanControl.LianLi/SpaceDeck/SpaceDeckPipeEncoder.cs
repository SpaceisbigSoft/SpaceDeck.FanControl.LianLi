using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace FanControl.LianLi.SpaceDeck;

/// <summary>
/// The lines of the SpaceDeck collector's plugin pipe (SpaceDeck's <c>docs/PLUGIN-PIPE.md</c>), as
/// pure functions: one JSON object per line, ending in <c>\n</c>. Every character outside printable
/// ASCII is written as a <c>\u</c> escape (a surrogate pair as its two halves; a lone surrogate, which
/// the collector refuses, as U+FFFD), so a line is plain ASCII and its length in characters is its
/// length in UTF-8 bytes.
/// </summary>
internal static class SpaceDeckPipeEncoder {
    /// <summary>The pipe protocol version these lines are.</summary>
    public const int ProtocolVersion = 1;

    /// <summary>The longest line the collector takes, in bytes, without its newline.</summary>
    public const int MaximumLineBytes = 64 * 1024;

    /// <summary>The <c>hello</c> line, the first after connecting.</summary>
    public static string Hello(string plugin, string version, string instance) {
        var builder = new StringBuilder("{\"t\":\"hello\",\"v\":");
        builder.Append(ProtocolVersion.ToString(CultureInfo.InvariantCulture));
        builder.Append(",\"plugin\":");
        AppendString(builder, plugin ?? throw new ArgumentNullException(nameof(plugin)));
        builder.Append(",\"version\":");
        AppendString(builder, version ?? throw new ArgumentNullException(nameof(version)));
        builder.Append(",\"instance\":");
        AppendString(builder, instance ?? throw new ArgumentNullException(nameof(instance)));
        return builder.Append("}\n").ToString();
    }

    /// <summary>The <c>catalog</c> line for these devices and readings.</summary>
    public static string Catalog(IReadOnlyList<SpaceDeckHardware> hardware, IReadOnlyList<SpaceDeckSensor> sensors) {
        if (hardware is null) {
            throw new ArgumentNullException(nameof(hardware));
        }

        if (sensors is null) {
            throw new ArgumentNullException(nameof(sensors));
        }

        var builder = new StringBuilder("{\"t\":\"catalog\",\"hardware\":[");
        for (int i = 0; i < hardware.Count; i++) {
            if (i > 0) {
                builder.Append(',');
            }

            builder.Append("{\"id\":");
            AppendString(builder, hardware[i].Id);
            builder.Append(",\"name\":");
            AppendString(builder, hardware[i].Name);
            builder.Append(",\"kind\":");
            AppendString(builder, hardware[i].Kind);
            builder.Append('}');
        }

        builder.Append("],\"sensors\":[");
        for (int i = 0; i < sensors.Count; i++) {
            if (i > 0) {
                builder.Append(',');
            }

            builder.Append("{\"id\":");
            AppendString(builder, sensors[i].Id);
            builder.Append(",\"hardware\":");
            AppendString(builder, sensors[i].HardwareId);
            builder.Append(",\"name\":");
            AppendString(builder, sensors[i].Name);
            builder.Append(",\"kind\":");
            AppendString(builder, sensors[i].Kind);
            builder.Append('}');
        }

        return builder.Append("]}\n").ToString();
    }

    /// <summary>
    /// The <c>values</c> line: one entry per reading, in catalog order, <c>null</c> for a reading
    /// with no value and for one that is not a finite number, which JSON cannot carry.
    /// </summary>
    public static string Values(IReadOnlyList<float?> values) {
        if (values is null) {
            throw new ArgumentNullException(nameof(values));
        }

        var builder = new StringBuilder("{\"t\":\"values\",\"values\":[");
        for (int i = 0; i < values.Count; i++) {
            if (i > 0) {
                builder.Append(',');
            }

            if (values[i] is float value && !float.IsNaN(value) && !float.IsInfinity(value)) {
                // The shortest form that reads back as the same float: 21.6, not 21.600000381469727.
                builder.Append(value.ToString("R", CultureInfo.InvariantCulture));
            } else {
                builder.Append("null");
            }
        }

        return builder.Append("]}\n").ToString();
    }

    private static void AppendString(StringBuilder builder, string text) {
        builder.Append('"');
        for (int i = 0; i < text.Length; i++) {
            char c = text[i];
            if (c == '"' || c == '\\') {
                builder.Append('\\').Append(c);
            } else if (c >= ' ' && c <= '~') {
                builder.Append(c);
            } else if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1])) {
                AppendEscape(builder, c);
                AppendEscape(builder, text[i + 1]);
                i++;
            } else if (char.IsSurrogate(c)) {
                AppendEscape(builder, '\ufffd');
            } else {
                AppendEscape(builder, c);
            }
        }

        builder.Append('"');
    }

    private static void AppendEscape(StringBuilder builder, char c)
        => builder.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
}
