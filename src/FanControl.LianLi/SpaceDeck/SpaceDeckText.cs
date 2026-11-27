using System;
using System.Globalization;
using System.Text;

namespace FanControl.LianLi.SpaceDeck;

/// <summary>
/// Makes ids and names valid for the SpaceDeck collector's pipe, the one place that does: the
/// collector disconnects on the first catalog that breaks a rule, so every id and name goes through
/// here first. An id is 1 to 128 characters of <c>A-Z a-z 0-9 / . _ : -</c>; a name is 1 to 64
/// printable characters, counted as Unicode characters (a surrogate pair counts once), not all blank.
/// </summary>
internal static class SpaceDeckText {
    /// <summary>The longest id the collector takes.</summary>
    public const int MaximumIdLength = 128;

    /// <summary>The longest name the collector takes, in Unicode characters.</summary>
    public const int MaximumNameLength = 64;

    /// <summary>
    /// <paramref name="id"/> made valid: every character outside the id set replaced with <c>_</c>,
    /// and cut to <see cref="MaximumIdLength"/>. Null when nothing is left (an empty id), which
    /// cannot be made valid.
    /// </summary>
    public static string? Id(string id) {
        if (id is null) {
            throw new ArgumentNullException(nameof(id));
        }

        if (id.Length == 0) {
            return null;
        }

        int length = Math.Min(id.Length, MaximumIdLength);
        var builder = new StringBuilder(length);
        for (int i = 0; i < length; i++) {
            builder.Append(IsIdCharacter(id[i]) ? id[i] : '_');
        }

        return builder.ToString();
    }

    /// <summary>
    /// <paramref name="name"/> made valid: every character the collector refuses replaced with
    /// <c>?</c>, and cut to <see cref="MaximumNameLength"/> Unicode characters. When that leaves it
    /// empty or blank, <paramref name="fallback"/> (a valid id) made valid the same way instead.
    /// </summary>
    public static string Name(string name, string fallback) {
        if (name is null) {
            throw new ArgumentNullException(nameof(name));
        }

        if (fallback is null) {
            throw new ArgumentNullException(nameof(fallback));
        }

        string cleaned = Clean(name);
        return IsBlank(cleaned) ? Clean(fallback) : cleaned;
    }

    /// <summary>
    /// Whether <paramref name="id"/> is already a valid id, 1 to <see cref="MaximumIdLength"/>
    /// characters of the id set, as the collector checks the ids it sends back on its screen pipe.
    /// </summary>
    public static bool IsValidId(string id) {
        if (id is null) {
            throw new ArgumentNullException(nameof(id));
        }

        if (id.Length == 0 || id.Length > MaximumIdLength) {
            return false;
        }

        foreach (char c in id) {
            if (!IsIdCharacter(c)) {
                return false;
            }
        }

        return true;
    }

    private static bool IsIdCharacter(char c)
        => (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9')
            || c == '/' || c == '.' || c == '_' || c == ':' || c == '-';

    private static string Clean(string text) {
        var builder = new StringBuilder(Math.Min(text.Length, MaximumNameLength * 2));
        int count = 0;
        for (int i = 0; i < text.Length && count < MaximumNameLength; i++) {
            char c = text[i];
            if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1])) {
                if (IsPrintable(CharUnicodeInfo.GetUnicodeCategory(text, i))) {
                    builder.Append(c).Append(text[i + 1]);
                } else {
                    builder.Append('?');
                }

                i++;
            } else {
                // A lone surrogate is categorised Surrogate, so it is replaced here too.
                builder.Append(IsPrintable(CharUnicodeInfo.GetUnicodeCategory(c)) ? c : '?');
            }

            count++;
        }

        return builder.ToString();
    }

    // The categories the collector refuses (control and format characters, which include the
    // bidirectional overrides and zero-width characters that could disguise a name, line and
    // paragraph separators, lone surrogates), and unassigned characters as well: the host's Unicode
    // tables may be older than the collector's, which could know one of them as a format character.
    private static bool IsPrintable(UnicodeCategory category)
        => category != UnicodeCategory.Control
            && category != UnicodeCategory.Format
            && category != UnicodeCategory.LineSeparator
            && category != UnicodeCategory.ParagraphSeparator
            && category != UnicodeCategory.Surrogate
            && category != UnicodeCategory.OtherNotAssigned;

    private static bool IsBlank(string text) {
        foreach (char c in text) {
            if (!char.IsWhiteSpace(c)) {
                return false;
            }
        }

        return true;
    }
}
