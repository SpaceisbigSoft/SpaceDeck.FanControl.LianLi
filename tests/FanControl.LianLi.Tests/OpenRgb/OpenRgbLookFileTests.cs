#if ENABLE_LIGHTING
using System;
using System.Collections.Generic;
using System.IO;
using FanControl.LianLi.Devices;
using FanControl.LianLi.OpenRgb;
using FanControl.LianLi.Protocol;
using FanControl.LianLi.Tests.Fakes;
using Xunit;

namespace FanControl.LianLi.Tests.OpenRgb;

/// <summary>The looks saved through OpenRGB: what it writes reads back exactly, and anything else is logged and taken as none.</summary>
public sealed class OpenRgbLookFileTests : IDisposable {
    private static readonly RgbColor Red = new RgbColor(255, 0, 0);

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "lianli-openrgb-" + Guid.NewGuid().ToString("N"));
    private readonly FakeLogger _log = new FakeLogger();

    public void Dispose() {
        if (Directory.Exists(_directory)) {
            Directory.Delete(_directory, recursive: true);
        }

        if (File.Exists(_directory)) {
            File.Delete(_directory);
        }
    }

    private OpenRgbLookFile Looks() => new OpenRgbLookFile(_directory);

    private void WriteText(string text) {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Looks().FilePath, text);
    }

    private const string NotRead = "OpenRGB: no saved screen ring look read from ";

    [Fact]
    public void NoFile_IsNoLook_AndNothingLogged() {
        Assert.Empty(Looks().Load(_log));
        Assert.Empty(_log.Messages);
    }

    [Fact]
    public void SavedLooks_ReadBack_EachUnderItsCooler() {
        var runway = new Galahad2RingLook(Galahad2RingMode.Runway, 3, 2, Galahad2RingLook.CounterClockwise, new[] { Red, new RgbColor(1, 2, 3) });
        Galahad2RingLook direct = Galahad2RingLook.DirectOf(new RgbColor(9, 8, 7));

        Looks().Save("LianLi/0", runway, _log);
        Looks().Save("LianLi/2", direct, _log);
        IReadOnlyDictionary<string, Galahad2RingLook> looks = Looks().Load(_log);

        Assert.Equal(2, looks.Count);
        Galahad2RingLook read = looks["LianLi/0"];
        Assert.Equal((Galahad2RingMode.Runway, 3, 2, 1), (read.Mode, read.Speed, read.Brightness, read.Direction));
        Assert.Equal(runway.Colors, read.Colors);
        Assert.Equal(direct.Colors, looks["LianLi/2"].Colors);
        Assert.Contains("OpenRGB: the screen ring's Runway look for LianLi/0 is saved in " + Looks().FilePath, _log.Messages);
        Assert.Equal(new[] { Looks().FilePath }, Directory.GetFiles(_directory));
    }

    [Fact]
    public void Format_IsTheFileThisWrites() {
        var looks = new[] {
            new KeyValuePair<string, Galahad2RingLook>("LianLi/0", new Galahad2RingLook(Galahad2RingMode.Static, 0, 4, 0, new[] { Red })),
            new KeyValuePair<string, Galahad2RingLook>("a\"b\\c", new Galahad2RingLook(Galahad2RingMode.Off, 0, 0, 0, Array.Empty<RgbColor>())),
        };

        Assert.Equal(
            "{\"version\":1,\"rings\":[{\"id\":\"LianLi/0\",\"mode\":\"Static\",\"speed\":0,\"brightness\":4,\"direction\":0,\"colors\":[[255,0,0]]},"
                + "{\"id\":\"a\\\"b\\\\c\",\"mode\":\"Off\",\"speed\":0,\"brightness\":0,\"direction\":0,\"colors\":[]}]}",
            OpenRgbLookFile.Format(looks));
        Assert.Equal("a\"b\\c", Assert.Single(OpenRgbLookFile.Parse(JsonValue.Parse(OpenRgbLookFile.Format(new[] { looks[1] }))).Keys));
    }

    [Fact]
    public void AFileWithNoRings_OrALookWithNoColours_IsRead() {
        WriteText("{\"version\":1}");
        Assert.Empty(Looks().Load(_log));

        WriteText("{\"version\":1,\"rings\":[{\"id\":\"LianLi/0\",\"mode\":\"Rainbow\",\"speed\":1,\"brightness\":2,\"direction\":0}]}");
        Assert.Empty(Looks().Load(_log)["LianLi/0"].Colors);
        Assert.Empty(_log.Messages);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"rings\":[]}")]
    [InlineData("{\"version\":2,\"rings\":[]}")]
    [InlineData("{\"version\":1,\"rings\":[{\"mode\":\"Static\",\"speed\":0,\"brightness\":4,\"direction\":0}]}")]
    [InlineData("{\"version\":1,\"rings\":[{\"id\":\"LianLi/0\",\"speed\":0,\"brightness\":4,\"direction\":0}]}")]
    [InlineData("{\"version\":1,\"rings\":[{\"id\":\"LianLi/0\",\"mode\":\"Spin\",\"speed\":0,\"brightness\":4,\"direction\":0}]}")]
    [InlineData("{\"version\":1,\"rings\":[{\"id\":\"LianLi/0\",\"mode\":\"42\",\"speed\":0,\"brightness\":4,\"direction\":0}]}")]
    [InlineData("{\"version\":1,\"rings\":[{\"id\":\"LianLi/0\",\"mode\":\"Static\",\"brightness\":4,\"direction\":0}]}")]
    [InlineData("{\"version\":1,\"rings\":[{\"id\":\"LianLi/0\",\"mode\":\"Static\",\"speed\":9,\"brightness\":4,\"direction\":0}]}")]
    [InlineData("{\"version\":1,\"rings\":[{\"id\":\"LianLi/0\",\"mode\":\"Static\",\"speed\":0,\"brightness\":4,\"direction\":0,\"colors\":[[1,2]]}]}")]
    [InlineData("{\"version\":1,\"rings\":[{\"id\":\"LianLi/0\",\"mode\":\"Static\",\"speed\":0,\"brightness\":4,\"direction\":0,\"colors\":[[1,2,\"x\"]]}]}")]
    [InlineData("{\"version\":1,\"rings\":[{\"id\":\"LianLi/0\",\"mode\":\"Static\",\"speed\":0,\"brightness\":4,\"direction\":0,\"colors\":[[1,-2,3]]}]}")]
    [InlineData("{\"version\":1,\"rings\":[{\"id\":\"LianLi/0\",\"mode\":\"Static\",\"speed\":0,\"brightness\":4,\"direction\":0,\"colors\":[[1,2,256]]}]}")]
    public void AFileThisDidNotWrite_IsLogged_AndHoldsNoLook(string text) {
        WriteText(text);

        Assert.Empty(Looks().Load(_log));
        Assert.Single(_log.Messages, m => m.StartsWith(NotRead, StringComparison.Ordinal) && m.EndsWith("; L-Connect's look is replayed instead", StringComparison.Ordinal));
    }

    [Fact]
    public void AnOversizedFile_IsNotRead() {
        WriteText("{\"version\":1,\"rings\":[]," + new string(' ', 64 * 1024) + "}");

        Assert.Empty(Looks().Load(_log));
        Assert.Contains(_log.Messages, m => m.Contains("larger than 65536 bytes"));
    }

    [Fact]
    public void ALockedFile_IsLogged_AndHoldsNoLook() {
        Looks().Save("LianLi/0", Galahad2RingLook.DirectOf(Red), _log);
        string path = Looks().FilePath;
        FileStream? held = null;
        if (OperatingSystem.IsWindows()) {
            held = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        } else {
            File.SetUnixFileMode(path, UnixFileMode.None);
        }

        try {
            Assert.Empty(Looks().Load(_log));
            Assert.Contains(_log.Messages, m => m.StartsWith(NotRead, StringComparison.Ordinal));
        } finally {
            held?.Dispose();
            if (!OperatingSystem.IsWindows()) {
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
        }
    }

    [Fact]
    public void ASaveThatCannotWrite_IsLogged() {
        File.WriteAllText(_directory, "not a directory");

        Looks().Save("LianLi/0", Galahad2RingLook.DirectOf(Red), _log);

        Assert.Contains(_log.Messages, m => m.StartsWith("OpenRGB: the screen ring's look for LianLi/0 could not be saved to ", StringComparison.Ordinal));
    }

    [Fact]
    public void Machine_IsBesideThePluginLog()
        => Assert.Equal(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FanControl.LianLi", "openrgb-looks.json"),
            OpenRgbLookFile.Machine.FilePath);

    [Fact]
    public void MissingArguments_AreRejected() {
        Assert.Throws<ArgumentNullException>(() => new OpenRgbLookFile(null!));
        Assert.Throws<ArgumentNullException>(() => Looks().Load(null!));
        Assert.Throws<ArgumentNullException>(() => Looks().Save(null!, Galahad2RingLook.DirectOf(Red), _log));
        Assert.Throws<ArgumentNullException>(() => Looks().Save("a", null!, _log));
        Assert.Throws<ArgumentNullException>(() => Looks().Save("a", Galahad2RingLook.DirectOf(Red), null!));
    }
}
#endif
