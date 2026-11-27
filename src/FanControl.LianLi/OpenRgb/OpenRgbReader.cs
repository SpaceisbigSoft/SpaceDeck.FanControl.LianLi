#if ENABLE_LIGHTING
using System;
using System.Collections.Generic;
using System.Text;
using FanControl.LianLi.Protocol;

namespace FanControl.LianLi.OpenRgb;

/// <summary>
/// Reads the fields of an OpenRGB SDK payload a client sent, in the encoding <see cref="OpenRgbWriter"/>
/// writes. A client may be anything on this PC, so every read is checked against what is left: a payload
/// shorter than its fields, a string without its zero, a size field that is not the payload's, or bytes
/// left over throw a <see cref="FormatException"/> naming the fault, never an index error.
/// </summary>
internal sealed class OpenRgbReader {
    private readonly byte[] _data;
    private int _at;

    /// <summary>Read <paramref name="data"/> from its start.</summary>
    public OpenRgbReader(byte[] data) {
        _data = data ?? throw new ArgumentNullException(nameof(data));
    }

    /// <summary>A 16-bit unsigned integer.</summary>
    public int UInt16() {
        Need(2);
        int value = _data[_at] | (_data[_at + 1] << 8);
        _at += 2;
        return value;
    }

    /// <summary>A 32-bit unsigned integer.</summary>
    public long UInt32() {
        Need(4);
        long value = _data[_at] | ((long)_data[_at + 1] << 8) | ((long)_data[_at + 2] << 16) | ((long)_data[_at + 3] << 24);
        _at += 4;
        return value;
    }

    /// <summary>A string, which must end in its zero; the zero is not part of it.</summary>
    public string String() {
        int length = UInt16();
        Need(length);
        if (length == 0 || _data[_at + length - 1] != 0) {
            throw new FormatException("a string without its terminating zero");
        }

        string text = Encoding.UTF8.GetString(_data, _at, length - 1);
        _at += length;
        return text;
    }

    /// <summary>A colour: R, G, B and a byte that is not used.</summary>
    public RgbColor Color() {
        Need(4);
        var color = new RgbColor(_data[_at], _data[_at + 1], _data[_at + 2]);
        _at += 4;
        return color;
    }

    /// <summary>A list of colours: the count, then each colour.</summary>
    public IReadOnlyList<RgbColor> Colors() {
        int count = UInt16();
        Need(count * 4);
        var colors = new RgbColor[count];
        for (int i = 0; i < count; i++) {
            colors[i] = Color();
        }

        return colors;
    }

    /// <summary>The size field a payload starts with, which must be the payload's whole length.</summary>
    public void Size() {
        if (UInt32() != _data.Length) {
            throw new FormatException("a size field that is not the payload's length");
        }
    }

    /// <summary>Check that the whole payload has been read.</summary>
    public void End() {
        if (_at != _data.Length) {
            throw new FormatException("bytes past the end of its fields");
        }
    }

    private void Need(int count) {
        if (_data.Length - _at < count) {
            throw new FormatException("a payload shorter than its fields");
        }
    }
}
#endif
