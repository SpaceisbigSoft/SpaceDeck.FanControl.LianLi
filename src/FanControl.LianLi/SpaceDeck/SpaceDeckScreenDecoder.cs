using System;
using System.Text;

namespace FanControl.LianLi.SpaceDeck;

/// <summary>
/// The framing of the SpaceDeck collector's screen pipe (SpaceDeck's <c>docs/SCREEN-PIPE.md</c>), as
/// pure functions over the bytes of one message, read in three parts: the 8-byte header
/// (<c>SDSF</c>, version 1, kind 1, the id's length), then the id and the payload's length, then the
/// payload, a JPEG. All integers are little-endian. Every rule the collector keeps is checked, and a
/// message that breaks one throws a <see cref="FormatException"/> whose message names the rule, the
/// same text for every break of it, so the client logs each kind of break once.
/// </summary>
internal static class SpaceDeckScreenDecoder {
    /// <summary>The header: magic (4), version (1), kind (1), id length (2).</summary>
    public const int HeaderLength = 8;

    /// <summary>The payload's length field after the id.</summary>
    public const int PayloadLengthLength = 4;

    /// <summary>The longest id, as for a catalog id.</summary>
    public const int MaximumIdLength = SpaceDeckText.MaximumIdLength;

    /// <summary>The largest payload, 256 KiB.</summary>
    public const int MaximumPayloadLength = 256 * 1024;

    // "SDSF", the one version, and the one kind of message: a JPEG frame.
    private static readonly byte[] Magic = { 0x53, 0x44, 0x53, 0x46 };
    private const byte Version = 1;
    private const byte JpegKind = 1;

    // A JPEG starts with the start-of-image marker FF D8 and ends with the end-of-image marker FF D9.
    private const byte MarkerPrefix = 0xFF;
    private const byte StartOfImage = 0xD8;
    private const byte EndOfImage = 0xD9;

    /// <summary>The id length <paramref name="header"/> announces, 1 to 128.</summary>
    public static int ReadHeader(byte[] header) {
        if (header is null) {
            throw new ArgumentNullException(nameof(header));
        }

        if (header.Length != HeaderLength) {
            throw new ArgumentException("A header is 8 bytes.", nameof(header));
        }

        for (int i = 0; i < Magic.Length; i++) {
            if (header[i] != Magic[i]) {
                throw new FormatException("a message that does not start with SDSF");
            }
        }

        if (header[4] != Version) {
            throw new FormatException("a message of a version other than 1");
        }

        if (header[5] != JpegKind) {
            throw new FormatException("a message of an unknown kind");
        }

        int idLength = header[6] | (header[7] << 8);
        if (idLength < 1 || idLength > MaximumIdLength) {
            throw new FormatException("an id length outside 1 to 128");
        }

        return idLength;
    }

    /// <summary>
    /// The hardware id in <paramref name="idAndLength"/>, the id's bytes followed by the payload's
    /// length field: ASCII, by the catalog's id rule.
    /// </summary>
    public static string ReadId(byte[] idAndLength) {
        int idLength = IdLengthOf(idAndLength);
        var id = new StringBuilder(idLength);
        for (int i = 0; i < idLength; i++) {
            id.Append((char)idAndLength[i]);
        }

        string text = id.ToString();
        if (!SpaceDeckText.IsValidId(text)) {
            throw new FormatException("an id that breaks the id rule");
        }

        return text;
    }

    /// <summary>The payload length at the end of <paramref name="idAndLength"/>, 1 to 262144.</summary>
    public static int ReadPayloadLength(byte[] idAndLength) {
        int at = IdLengthOf(idAndLength);
        long length = idAndLength[at]
            | ((long)idAndLength[at + 1] << 8)
            | ((long)idAndLength[at + 2] << 16)
            | ((long)idAndLength[at + 3] << 24);
        if (length < 1 || length > MaximumPayloadLength) {
            throw new FormatException("a payload length outside 1 to 262144");
        }

        return (int)length;
    }

    /// <summary>Check that <paramref name="payload"/> starts <c>FF D8</c> and ends <c>FF D9</c>, as a JPEG does.</summary>
    public static void CheckJpeg(byte[] payload) {
        if (payload is null) {
            throw new ArgumentNullException(nameof(payload));
        }

        int last = payload.Length - 1;
        if (payload.Length < 4
            || payload[0] != MarkerPrefix
            || payload[1] != StartOfImage
            || payload[last - 1] != MarkerPrefix
            || payload[last] != EndOfImage) {
            throw new FormatException("a payload that is not a JPEG (FF D8 ... FF D9)");
        }
    }

    private static int IdLengthOf(byte[] idAndLength) {
        if (idAndLength is null) {
            throw new ArgumentNullException(nameof(idAndLength));
        }

        if (idAndLength.Length <= PayloadLengthLength) {
            throw new ArgumentException("An id is at least one byte, followed by the payload length.", nameof(idAndLength));
        }

        return idAndLength.Length - PayloadLengthLength;
    }
}
