#if ENABLE_LIGHTING
using System;
using System.Collections.Generic;
using System.Text;
using FanControl.LianLi.Protocol;

namespace FanControl.LianLi.OpenRgb;

/// <summary>
/// Builds the bytes of an OpenRGB SDK payload, in the SDK's own encoding: integers little-endian, a
/// string as its length (with its terminating zero) in 16 bits, its UTF-8 bytes and a zero, a colour as
/// R, G, B and a zero byte, a list of colours as their count in 16 bits and the colours.
/// </summary>
internal sealed class OpenRgbWriter {
    private readonly List<byte> _bytes = new List<byte>();

    /// <summary>How many bytes have been written.</summary>
    public int Length => _bytes.Count;

    /// <summary>A 16-bit unsigned integer.</summary>
    public OpenRgbWriter UInt16(int value) {
        if (value < 0 || value > ushort.MaxValue) {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        _bytes.Add((byte)value);
        _bytes.Add((byte)(value >> 8));
        return this;
    }

    /// <summary>A 32-bit integer, signed or not: the same four bytes.</summary>
    public OpenRgbWriter UInt32(long value) {
        _bytes.Add((byte)value);
        _bytes.Add((byte)(value >> 8));
        _bytes.Add((byte)(value >> 16));
        _bytes.Add((byte)(value >> 24));
        return this;
    }

    /// <summary>One byte.</summary>
    public OpenRgbWriter Byte(byte value) {
        _bytes.Add(value);
        return this;
    }

    /// <summary>A string: its length with the terminating zero, its UTF-8 bytes, the zero.</summary>
    public OpenRgbWriter String(string text) {
        if (text is null) {
            throw new ArgumentNullException(nameof(text));
        }

        byte[] bytes = Encoding.UTF8.GetBytes(text);
        UInt16(bytes.Length + 1);
        _bytes.AddRange(bytes);
        _bytes.Add(0);
        return this;
    }

    /// <summary>A colour: R, G, B, and a zero byte.</summary>
    public OpenRgbWriter Color(RgbColor color) {
        _bytes.Add(color.R);
        _bytes.Add(color.G);
        _bytes.Add(color.B);
        _bytes.Add(0);
        return this;
    }

    /// <summary>A list of colours: the count, then each colour.</summary>
    public OpenRgbWriter Colors(IReadOnlyList<RgbColor> colors) {
        if (colors is null) {
            throw new ArgumentNullException(nameof(colors));
        }

        UInt16(colors.Count);
        foreach (RgbColor color in colors) {
            Color(color);
        }

        return this;
    }

    /// <summary>Put <paramref name="value"/> in the four bytes at <paramref name="offset"/>, already written: a size known only at the end.</summary>
    public OpenRgbWriter Patch(int offset, long value) {
        if (offset < 0 || offset + 4 > _bytes.Count) {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }

        _bytes[offset] = (byte)value;
        _bytes[offset + 1] = (byte)(value >> 8);
        _bytes[offset + 2] = (byte)(value >> 16);
        _bytes[offset + 3] = (byte)(value >> 24);
        return this;
    }

    /// <summary>The bytes written.</summary>
    public byte[] ToArray() => _bytes.ToArray();
}
#endif
