#if ENABLE_LIGHTING
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FanControl.LianLi.Devices;
using FanControl.LianLi.OpenRgb;
using FanControl.LianLi.Protocol;
using FanControl.LianLi.Tests.Fakes;
using Xunit;

namespace FanControl.LianLi.Tests.OpenRgb;

/// <summary>One client's conversation: the version agreed, each request answered, each command applied, and what is refused.</summary>
public sealed class OpenRgbSessionTests : IDisposable {
    private static readonly RgbColor Red = new RgbColor(255, 0, 0);

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "LianLiOpenRgbSession" + Guid.NewGuid().ToString("N"));
    private readonly FakeLogger _log = new FakeLogger();
    private readonly CoolerHandoff _first = new CoolerHandoff(new FakeClock());
    private readonly CoolerHandoff _second = new CoolerHandoff(new FakeClock());
    private readonly OpenRgbLookFile _looks;
    private readonly OpenRgbSession _session;

    public OpenRgbSessionTests() {
        _looks = new OpenRgbLookFile(_directory);
        _session = new OpenRgbSession(
            new[] { new OpenRgbRing("LianLi/0", _first), new OpenRgbRing("LianLi/3", _second) }, _looks, "2.0.9", _log);
    }

    public void Dispose() {
        if (Directory.Exists(_directory)) {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static byte[] U32(long value) => new OpenRgbWriter().UInt32(value).ToArray();

    private static void AssertPacket(byte[] packet, long device, long id, byte[] payload)
        => Assert.Equal(OpenRgbProtocol.Packet(device, id, payload), packet);

    [Fact]
    public void TheVersion_IsTheLowerOfTheClientsAndSix_AndTheReplyIsSix() {
        Assert.Equal(0, _session.Version);

        AssertPacket(Assert.Single(_session.Handle(0, 40, U32(3))), 0, 40, U32(6));
        Assert.Equal(3, _session.Version);

        _session.Handle(0, 40, U32(99));
        Assert.Equal(6, _session.Version);

        _session.Handle(0, 40, Array.Empty<byte>());
        Assert.Equal(0, _session.Version);
    }

    [Fact]
    public void TheControllerCount_ListsTheIdsFromVersionSix() {
        AssertPacket(Assert.Single(_session.Handle(0, 0, Array.Empty<byte>())), 0, 0, U32(2));

        _session.Handle(0, 40, U32(6));
        AssertPacket(Assert.Single(_session.Handle(0, 0, Array.Empty<byte>())), 0, 0, new OpenRgbWriter().UInt32(2).UInt32(0).UInt32(1).ToArray());
    }

    [Fact]
    public void ControllerData_IsInTheVersionTheRequestAsksFor_OrTheOneAgreed() {
        _session.Handle(0, 40, U32(4));
        var ring = new OpenRgbRing("LianLi/3", _second);

        AssertPacket(Assert.Single(_session.Handle(1, 1, U32(2))), 1, 1, ring.Describe("2.0.9", 2));
        AssertPacket(Assert.Single(_session.Handle(1, 1, U32(9))), 1, 1, ring.Describe("2.0.9", 6));
        AssertPacket(Assert.Single(_session.Handle(1, 1, Array.Empty<byte>())), 1, 1, ring.Describe("2.0.9", 4));
    }

    [Fact]
    public void ProfileAndPluginLists_AreEmpty() {
        AssertPacket(Assert.Single(_session.Handle(0, 150, Array.Empty<byte>())), 0, 150, new byte[] { 6, 0, 0, 0, 0, 0 });
        AssertPacket(Assert.Single(_session.Handle(0, 200, Array.Empty<byte>())), 0, 200, new byte[] { 6, 0, 0, 0, 0, 0 });
    }

    [Fact]
    public void TheClientsName_IsKeptPrintable_UpToItsZero_AndCut() {
        Assert.Null(_session.ClientName);

        Assert.Empty(_session.Handle(0, 50, System.Text.Encoding.UTF8.GetBytes("OpenRGBé\n\0junk")));
        Assert.Equal("OpenRGB??", _session.ClientName);

        _session.Handle(0, 50, System.Text.Encoding.ASCII.GetBytes(new string('n', 80)));
        Assert.Equal(new string('n', 64), _session.ClientName);
    }

    [Fact]
    public void LedCommands_GoToTheRingTheyName() {
        RgbColor[] leds = Enumerable.Repeat(Red, 12).ToArray();
        var colors = new OpenRgbWriter().UInt32(0).Colors(leds);
        Assert.Empty(_session.Handle(1, 1050, colors.Patch(0, colors.Length).ToArray()));
        Assert.Equal(leds, _second.PeekLook()!.Colors);
        Assert.Null(_first.PeekLook());

        var zone = new OpenRgbWriter().UInt32(0).UInt32(0).Colors(Enumerable.Repeat(new RgbColor(1, 1, 1), 12).ToArray());
        _session.Handle(0, 1051, zone.Patch(0, zone.Length).ToArray());
        Assert.Equal(new RgbColor(1, 1, 1), _first.PeekLook()!.Colors[0]);

        _session.Handle(0, 1052, new OpenRgbWriter().UInt32(5).Color(Red).ToArray());
        Assert.Equal(Red, _first.PeekLook()!.Colors[5]);
    }

    [Fact]
    public void ModeCommands_ChangeTheRing_AndSaveModeKeepsTheLook() {
        _session.Handle(0, 40, U32(5));
        var runway = new OpenRgbModeSetting(OpenRgbRingMode.All[6], 4, 2, OpenRgbRingMode.Left, new[] { Red });

        Assert.Empty(_session.Handle(0, 1101, OpenRgbCommandDecoderTests.ModePayload(6, runway, 5)));
        Assert.Equal(Galahad2RingMode.Runway, _first.PeekLook()!.Mode);
        Assert.False(File.Exists(_looks.FilePath));

        Assert.Empty(_session.Handle(0, 1100, Array.Empty<byte>()));
        Assert.Equal(Galahad2RingMode.Direct, _first.PeekLook()!.Mode);

        Assert.Empty(_session.Handle(1, 1102, OpenRgbCommandDecoderTests.ModePayload(6, runway, 5)));
        Galahad2RingLook saved = _looks.Load(_log)["LianLi/3"];
        Assert.Equal((Galahad2RingMode.Runway, 4, 2, 1), (saved.Mode, saved.Speed, saved.Brightness, saved.Direction));
    }

    [Fact]
    public void AnUnknownPacket_IsIgnored()
        => Assert.Empty(_session.Handle(7, 9999, new byte[] { 1, 2, 3 }));

    [Theory]
    [InlineData(1L)]
    [InlineData(1050L)]
    [InlineData(1051L)]
    [InlineData(1052L)]
    [InlineData(1100L)]
    [InlineData(1101L)]
    [InlineData(1102L)]
    public void APacketForAControllerTheServerDoesNotHave_IsAFormatFault(long packetId)
        => Assert.Equal(
            "a controller the server does not have",
            Assert.Throws<FormatException>(() => _session.Handle(2, packetId, Array.Empty<byte>())).Message);

    [Fact]
    public void BadArguments_AreRejected() {
        var rings = new List<OpenRgbRing>();
        Assert.Throws<ArgumentNullException>(() => _session.Handle(0, 0, null!));
        Assert.Throws<ArgumentNullException>(() => new OpenRgbSession(null!, _looks, "v", _log));
        Assert.Throws<ArgumentNullException>(() => new OpenRgbSession(rings, null!, "v", _log));
        Assert.Throws<ArgumentNullException>(() => new OpenRgbSession(rings, _looks, null!, _log));
        Assert.Throws<ArgumentNullException>(() => new OpenRgbSession(rings, _looks, "v", null!));
    }
}
#endif
