#if ENABLE_LIGHTING
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using FanControl.LianLi.Devices;
using FanControl.LianLi.OpenRgb;
using FanControl.LianLi.Plugin;
using FanControl.LianLi.Protocol;
using FanControl.LianLi.SpaceDeck;
using FanControl.LianLi.Tests.Fakes;
using FanControl.LianLi.Tests.OpenRgb;
using FanControl.LianLi.Transport;
using Xunit;

namespace FanControl.LianLi.Tests.Plugin;

/// <summary>
/// SpaceDeck's Lighting build: Load serves a Vision LCD's screen ring to OpenRGB, a client's command
/// reaches the ring through the cooler's own worker, Close stops the server, and a look saved through
/// OpenRGB is shown instead of L-Connect's at a start and after a reconnect.
/// </summary>
public sealed class LianLiPluginOpenRgbTests : IDisposable {
    private const string VisionPath = @"\\?\hid#vid_0416&pid_7391&mi_01#7&9c2f7a3&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}";

    private static readonly RgbColor Red = new RgbColor(255, 0, 0);

    private readonly LConnectDirectory _lConnect = new LConnectDirectory();
    private readonly string _plugin = Path.Combine(Path.GetTempPath(), "lianli-openrgb-plugin-" + Guid.NewGuid().ToString("N"));
    private readonly FakeLoopbackListenerApi _api = new FakeLoopbackListenerApi();
    private readonly FakeLogger _log = new FakeLogger();
    private readonly FakeClock _clock = new FakeClock();
    private readonly FakeEnumerator _enumerator = new FakeEnumerator(new LocatedDevice(0x0416, 0x7391, VisionPath, null));

    public void Dispose() {
        _lConnect.Dispose();
        if (Directory.Exists(_plugin)) {
            Directory.Delete(_plugin, recursive: true);
        }
    }

    private OpenRgbLookFile Looks => new OpenRgbLookFile(_plugin);

    private LianLiPlugin NewPlugin()
        => new LianLiPlugin(
            _enumerator,
            new DeviceCatalog(),
            _clock,
            new FakeDelay(),
            _log,
            _lConnect.Locations,
            new PluginRuntime(new FakeClock(), new FakeRememberedControllerStore()),
            CopyGuardFixture.Alone(),
            new SpaceDeckPublication(new FakeNamedPipeApi(), new FakeServiceControlApi { ProcessId = null }, new FakeDeviceCallRunner(), new FakeDeviceCallClock(), _log),
            new OpenRgbHosting(_api, Looks, new FakeDeviceCallClock(), _log));

    // A 0x83 the ring was sent through OpenRGB: on the outer scope, where L-Connect's replay writes inner.
    private static bool IsOpenRgbEffect(byte[] write, byte modeByte) => write[1] == 0x83 && write[6] == 0x01 && write[7] == modeByte;

    private bool Sent(byte modeByte, int atLeast = 1)
        => SpinWait.SpinUntil(
            () => _enumerator.TransportFor(VisionPath).SnapshotTransfers().Count(t => IsOpenRgbEffect(t.Value, modeByte)) >= atLeast,
            TimeSpan.FromSeconds(10));

    private static byte[] Packet(long id, byte[] payload) => OpenRgbProtocol.Packet(0, id, payload);

    [Fact]
    public void Load_ServesTheRing_AClientsModeReachesItThroughTheWorker_AndCloseStopsTheServer() {
        using LianLiPlugin plugin = NewPlugin();
        plugin.Initialize();
        plugin.Load(new FakeSensorsContainer());
        Assert.True(SpinWait.SpinUntil(() => _api.Ports.Count == 1, TimeSpan.FromSeconds(10)));
        var client = new FakeLoopbackConnection { BlockWhenEmpty = true };
        client.Send(Packet(40, new OpenRgbWriter().UInt32(5).ToArray()));
        client.Send(Packet(1101, OpenRgbCommandDecoderTests.ModePayload(2, new OpenRgbModeSetting(OpenRgbRingMode.All[2], 0, 3, 0, new[] { Red }), 5)));

        _api.Listener.Connect(client);

        Assert.True(Sent(0x03), "the client's Static look did not reach the ring");
        byte[] effect = _enumerator.TransportFor(VisionPath).SnapshotTransfers().Select(t => t.Value).First(w => IsOpenRgbEffect(w, 0x03));
        Assert.Equal(new byte[] { 0x01, 0x03, 0x03, 0x00, 255, 0, 0 }, effect.Skip(6).Take(7).ToArray());

        // The cooler comes back from a wake: the look set through OpenRGB is shown again.
        _clock.Advance(TimeSpan.FromSeconds(2));
        _enumerator.TransportFor(VisionPath).Generation = 1;
        Assert.True(Sent(0x03, atLeast: 2), "the OpenRGB look was not shown again after the reconnect");

        plugin.Close();

        Assert.True(_api.Listener.Disposed);
        Assert.True(client.Disposed);
    }

    [Fact]
    public void ALookSavedThroughOpenRgb_IsShownAtAStart_InsteadOfLConnectsRing_ButLConnectsFanLightStays() {
        string folder = Path.Combine(_lConnect.Locations.DeviceDirectory, "vision");
        Directory.CreateDirectory(folder);
        WriteGzip(folder, "fan", SettingFor("FanLEDLighting", "{\"Mode\":5,\"Brightness\":3,\"Speed\":2,\"Color1\":{\"R\":1,\"G\":2,\"B\":3},\"Color2\":{\"R\":0,\"G\":0,\"B\":0},\"Color3\":{\"R\":0,\"G\":0,\"B\":0},\"Color4\":{\"R\":0,\"G\":0,\"B\":0},\"Direction\":1,\"SyncToPump\":false,\"NumberOfLED\":24}"));
        WriteGzip(folder, "screen", SettingFor("ScreenLEDLighting", "{\"Mode\":12,\"IsDynamicMode\":false,\"Static\":{\"Colors\":[{\"R\":255,\"G\":0,\"B\":0}],\"Speed\":50,\"Brightness\":100,\"Direction\":1}}"));
        Looks.Save("LianLi/0", new Galahad2RingLook(Galahad2RingMode.Meteor, 1, 2, Galahad2RingLook.Clockwise, new[] { Red }), _log);
        using LianLiPlugin plugin = NewPlugin();

        plugin.Initialize();

        Assert.True(Sent(0x06), "the saved Meteor look was not shown");
        FakeDeviceTransport transport = _enumerator.TransportFor(VisionPath);
        Assert.Contains(transport.SnapshotTransfers(), t => t.Value[1] == 0x85);
        Assert.DoesNotContain(transport.SnapshotTransfers(), t => t.Value[1] == 0x83 && t.Value[6] == 0x00);
        Assert.Contains(_log.Messages, m => m == "  screen ring of " + VisionPath + ": the look set through OpenRGB is shown, not L-Connect's");
        plugin.Close();
    }

    [Fact]
    public void ACorruptSavedLooksFile_IsLogged_AndLConnectsRingIsReplayed() {
        Directory.CreateDirectory(_plugin);
        File.WriteAllText(Looks.FilePath, "not json");
        using LianLiPlugin plugin = NewPlugin();

        plugin.Initialize();
        plugin.Close();

        Assert.Contains(_log.Messages, m => m.StartsWith("OpenRGB: no saved screen ring look read from ", StringComparison.Ordinal));
        Assert.DoesNotContain(_log.Messages, m => m.Contains("the look set through OpenRGB is shown"));
    }

    [Fact]
    public void ASecondLoad_ReplacesTheServer() {
        using LianLiPlugin plugin = NewPlugin();
        plugin.Initialize();
        plugin.Load(new FakeSensorsContainer());
        Assert.True(SpinWait.SpinUntil(() => _api.Ports.Count == 1, TimeSpan.FromSeconds(10)));

        plugin.Load(new FakeSensorsContainer());

        Assert.True(SpinWait.SpinUntil(() => _api.Ports.Count == 2, TimeSpan.FromSeconds(10)));
        plugin.Close();
    }

    [Fact]
    public void ANullHosting_IsRejected()
        => Assert.Throws<ArgumentNullException>(() => new LianLiPlugin(
            _enumerator,
            new DeviceCatalog(),
            _clock,
            new FakeDelay(),
            _log,
            _lConnect.Locations,
            new PluginRuntime(new FakeClock(), new FakeRememberedControllerStore()),
            CopyGuardFixture.Alone(),
            new SpaceDeckPublication(new FakeNamedPipeApi(), new FakeServiceControlApi(), new FakeDeviceCallRunner(), new FakeDeviceCallClock(), _log),
            null!));

    private static string SettingFor(string type, string dataJson)
        => "{\"DeviceID\":\"" + VisionPath.Replace("\\", "\\\\") + "\",\"Type\":\"" + type + "\",\"Data\":" + dataJson + "}";

    private static void WriteGzip(string folder, string name, string json) {
        using FileStream file = File.Create(Path.Combine(folder, name + ".0"));
        using var gzip = new GZipStream(file, CompressionMode.Compress);
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        gzip.Write(bytes, 0, bytes.Length);
    }
}
#endif
