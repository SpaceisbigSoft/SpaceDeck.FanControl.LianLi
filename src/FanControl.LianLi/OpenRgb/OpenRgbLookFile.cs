#if ENABLE_LIGHTING
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using FanControl.LianLi.Devices;
using FanControl.LianLi.Logging;
using FanControl.LianLi.Protocol;

namespace FanControl.LianLi.OpenRgb;

/// <summary>
/// Keeps the screen ring looks saved through OpenRGB (SAVEMODE, its "Save to device"), one per cooler
/// by its <c>LianLi/{index}</c> id, in a small JSON file beside <c>remembered-controllers.json</c>, so
/// the plugin shows them again at the next start instead of L-Connect's. A file that is missing, locked,
/// corrupt or not what this writes is logged and taken as holding no look, and L-Connect's replay then
/// sets the ring as it would without OpenRGB. Written whole to a temporary file and then swapped in,
/// as <c>RememberedControllerFile</c> is, so a power cut mid-write leaves the previous file.
/// </summary>
internal sealed class OpenRgbLookFile {
    /// <summary>The file's name, in the plugin's own folder.</summary>
    public const string FileName = "openrgb-looks.json";

    // Bumped when the layout changes; a file of another version is not read.
    private const int FormatVersion = 1;

    // A handful of coolers' looks are a few hundred bytes; anything near this is not this file.
    private const long MaximumBytes = 64 * 1024;

    private readonly object _gate = new object();
    private readonly string _directory;
    private readonly string _path;

    /// <summary>Keep the looks in a file in <paramref name="directory"/>.</summary>
    public OpenRgbLookFile(string directory) {
        _directory = directory ?? throw new ArgumentNullException(nameof(directory));
        _path = Path.Combine(directory, FileName);
    }

    /// <summary>The file beside the plugin's log, under the local application data of the account that runs FanControl.</summary>
    public static OpenRgbLookFile Machine => new OpenRgbLookFile(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FanControl.LianLi"));

    /// <summary>The file the looks are kept in.</summary>
    public string FilePath => _path;

    /// <summary>Every saved look, by cooler id; empty when there is none or the file cannot be used (logged).</summary>
    public IReadOnlyDictionary<string, Galahad2RingLook> Load(ILog log) {
        if (log is null) {
            throw new ArgumentNullException(nameof(log));
        }

        lock (_gate) {
            return Read(log);
        }
    }

    /// <summary>Keep <paramref name="look"/> as cooler <paramref name="id"/>'s, beside the others; a failure is logged.</summary>
    public void Save(string id, Galahad2RingLook look, ILog log) {
        if (id is null) {
            throw new ArgumentNullException(nameof(id));
        }

        if (look is null) {
            throw new ArgumentNullException(nameof(look));
        }

        if (log is null) {
            throw new ArgumentNullException(nameof(log));
        }

        lock (_gate) {
            var looks = new SortedDictionary<string, Galahad2RingLook>(Read(log).ToDictionary(e => e.Key, e => e.Value), StringComparer.Ordinal) {
                [id] = look,
            };
            Write(looks, log, "the screen ring's " + look.Mode + " look for " + id + " is saved in ", "the screen ring's look for " + id + " could not be saved to ");
        }
    }

    /// <summary>
    /// Forget cooler <paramref name="id"/>'s look, keeping the others, so L-Connect's is replayed for it
    /// at the next start; nothing is written when none is saved for it, and a failure is logged.
    /// </summary>
    public void Remove(string id, ILog log) {
        if (id is null) {
            throw new ArgumentNullException(nameof(id));
        }

        if (log is null) {
            throw new ArgumentNullException(nameof(log));
        }

        lock (_gate) {
            var looks = new SortedDictionary<string, Galahad2RingLook>(Read(log).ToDictionary(e => e.Key, e => e.Value), StringComparer.Ordinal);
            if (!looks.Remove(id)) {
                log.Write("OpenRGB: no screen ring look was saved for " + id + "; L-Connect's look is replayed at the next start");
                return;
            }

            Write(looks, log, "the screen ring's look for " + id + " is forgotten, so L-Connect's is replayed at the next start: ", "the screen ring's look for " + id + " could not be forgotten in ");
        }
    }

    /// <summary>The text this writes for <paramref name="looks"/>.</summary>
    internal static string Format(IEnumerable<KeyValuePair<string, Galahad2RingLook>> looks) {
        var json = new StringBuilder();
        json.Append("{\"version\":").Append(FormatVersion.ToString(CultureInfo.InvariantCulture)).Append(",\"rings\":[");
        bool first = true;
        foreach (KeyValuePair<string, Galahad2RingLook> entry in looks) {
            json.Append(first ? string.Empty : ",");
            first = false;
            Galahad2RingLook look = entry.Value;
            json.Append("{\"id\":").Append(Quote(entry.Key))
                .Append(",\"mode\":").Append(Quote(look.Mode.ToString()))
                .Append(",\"speed\":").Append(look.Speed.ToString(CultureInfo.InvariantCulture))
                .Append(",\"brightness\":").Append(look.Brightness.ToString(CultureInfo.InvariantCulture))
                .Append(",\"direction\":").Append(look.Direction.ToString(CultureInfo.InvariantCulture))
                .Append(",\"colors\":[")
                .Append(string.Join(",", look.Colors.Select(c => string.Format(CultureInfo.InvariantCulture, "[{0},{1},{2}]", c.R, c.G, c.B))))
                .Append("]}");
        }

        return json.Append("]}").ToString();
    }

    /// <summary>The looks in a parsed file; anything this does not write throws <see cref="FormatException"/>.</summary>
    internal static IReadOnlyDictionary<string, Galahad2RingLook> Parse(JsonValue root) {
        if (root.Member("version")?.AsInt() != FormatVersion) {
            throw new FormatException("not a version " + FormatVersion + " file");
        }

        var looks = new Dictionary<string, Galahad2RingLook>(StringComparer.Ordinal);
        foreach (JsonValue entry in root.Member("rings")?.Elements ?? Array.Empty<JsonValue>()) {
            string id = entry.Member("id")?.AsString() ?? throw new FormatException("a look without its cooler's id");
            if (!Enum.TryParse(entry.Member("mode")?.AsString(), out Galahad2RingMode mode) || !Enum.IsDefined(typeof(Galahad2RingMode), mode)) {
                throw new FormatException("a look of a mode the ring does not have");
            }

            var colors = new List<RgbColor>();
            foreach (JsonValue color in entry.Member("colors")?.Elements ?? Array.Empty<JsonValue>()) {
                IReadOnlyList<JsonValue> rgb = color.Elements;
                if (rgb.Count != 3) {
                    throw new FormatException("a colour that is not R, G, B");
                }

                colors.Add(new RgbColor(Channel(rgb[0]), Channel(rgb[1]), Channel(rgb[2])));
            }

            try {
                looks[id] = new Galahad2RingLook(mode, Level(entry, "speed"), Level(entry, "brightness"), Level(entry, "direction"), colors);
            } catch (ArgumentException ex) {
                throw new FormatException("a look the ring cannot show: " + ex.Message, ex);
            }
        }

        return looks;
    }

    // Caller holds _gate. The whole file to a temporary one, then swapped in; each message ends with the path.
    private void Write(SortedDictionary<string, Galahad2RingLook> looks, ILog log, string written, string failed) {
        string temporary = _path + ".tmp";
        try {
            Directory.CreateDirectory(_directory);
            File.WriteAllText(temporary, Format(looks), new UTF8Encoding(false));
            if (File.Exists(_path)) {
                File.Replace(temporary, _path, null);
            } else {
                File.Move(temporary, _path);
            }

            log.Write("OpenRGB: " + written + _path);
        } catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) {
            log.Write("OpenRGB: " + failed + _path + ": " + ex.Message);
        }
    }

    // Caller holds _gate.
    private IReadOnlyDictionary<string, Galahad2RingLook> Read(ILog log) {
        try {
            if (!File.Exists(_path)) {
                return new Dictionary<string, Galahad2RingLook>();
            }

            if (new FileInfo(_path).Length > MaximumBytes) {
                throw new FormatException("the file is larger than " + MaximumBytes + " bytes");
            }

            return Parse(JsonValue.Parse(File.ReadAllText(_path, Encoding.UTF8)));
        } catch (Exception ex) when (ex is FormatException || ex is IOException || ex is UnauthorizedAccessException) {
            log.Write("OpenRGB: no saved screen ring look read from " + _path + " (" + ex.Message + "); L-Connect's look is replayed instead");
            return new Dictionary<string, Galahad2RingLook>();
        }
    }

    private static int Level(JsonValue entry, string name)
        => entry.Member(name)?.AsInt() ?? throw new FormatException("a look without its " + name);

    private static byte Channel(JsonValue value) {
        int? channel = value.AsInt();
        if (channel is null || channel < 0 || channel > 255) {
            throw new FormatException("a colour channel outside 0 to 255");
        }

        return (byte)channel.Value;
    }

    // A JSON string; the ids and mode names are plain ASCII, so only quotes and backslashes are escaped.
    private static string Quote(string text) => "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
}
#endif
