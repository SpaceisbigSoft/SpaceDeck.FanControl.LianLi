using System;
using System.Linq;
using System.Threading;
using FanControl.LianLi.Devices;
using FanControl.LianLi.Plugin;
using FanControl.LianLi.SpaceDeck;
using FanControl.LianLi.Tests.Fakes;
using FanControl.LianLi.Transport;
using Xunit;

namespace FanControl.LianLi.Tests.SpaceDeck;

/// <summary>
/// SpaceDeck's build of the plugin: Load starts publishing what it registered, Update captures the
/// values on FanControl's thread, Initialize and Close stop it, and nothing that fails on the way
/// touches the sensors FanControl was given.
/// </summary>
public class LianLiPluginSpaceDeckTests {
    private readonly FakeNamedPipeApi _pipe = new FakeNamedPipeApi();
    private readonly FakeServiceControlApi _services = new FakeServiceControlApi();
    private readonly FakeLogger _log = new FakeLogger();

    private static LocatedDevice Sli(int index) => new LocatedDevice(0x0CF2, 0xA102, "fake/" + index, null);

    private LianLiPlugin NewPlugin(FakeEnumerator enumerator)
        => new LianLiPlugin(
            enumerator,
            new DeviceCatalog(),
            new FakeClock(),
            new FakeDelay(),
            _log,
            LConnectDirectory.Absent,
            new PluginRuntime(new FakeClock(), new FakeRememberedControllerStore()),
            CopyGuardFixture.Alone(),
            new SpaceDeckPublication(_pipe, _services, new FakeDeviceCallRunner(), new FakeDeviceCallClock(), _log));

    private FakeNamedPipeConnection AwaitConnection(int index) {
        Assert.True(SpinWait.SpinUntil(() => _pipe.Connections.Count > index && _pipe.Connections[index].Lines.Count > 0, TimeSpan.FromSeconds(10)));
        return _pipe.Connections[index];
    }

    [Fact]
    public void Load_PublishesWhatItRegistered_UpdateCapturesTheValues_AndCloseStops() {
        using LianLiPlugin plugin = NewPlugin(new FakeEnumerator(Sli(0)));
        plugin.Initialize();
        var container = new FakeSensorsContainer();
        plugin.Load(container);
        FakeNamedPipeConnection connection = AwaitConnection(0);

        plugin.Update();

        Assert.True(SpinWait.SpinUntil(() => connection.Lines.Count >= 3, TimeSpan.FromSeconds(10)));
        Assert.StartsWith("{\"t\":\"hello\",\"v\":1,\"plugin\":\"" + plugin.Name + "\"", connection.Lines[0], StringComparison.Ordinal);
        string catalog = connection.Lines[1];
        Assert.StartsWith("{\"t\":\"catalog\",\"hardware\":[{\"id\":\"LianLi/0\",\"name\":\"Lian Li Uni #1\",\"kind\":\"fans\"}]", catalog, StringComparison.Ordinal);
        Assert.All(
            container.FanSensors.Select(s => s.Id).Concat(container.ControlSensors.Select(s => s.Id)),
            id => Assert.Contains("{\"id\":\"" + id + "\",\"hardware\":\"LianLi/0\"", catalog, StringComparison.Ordinal));
        Assert.StartsWith("{\"t\":\"values\",\"values\":[", connection.Lines[2], StringComparison.Ordinal);

        plugin.Close();

        Assert.True(connection.Disposed);
        Assert.Equal(4, container.ControlSensors.Count);
    }

    // A Galahad II Vision LCD: its catalog entry has the screen, the screen pipe is read, and a frame
    // the collector sends wakes the cooler's loop, which sends it as SendJpeg packets.
    [Fact]
    public void AVisionLcd_IsListedWithItsScreen_AndAFrameFromTheScreenPipeReachesIt() {
        byte[] jpeg = ScreenMessage.Jpeg(1500);
        _pipe.ConfigureReader = reader => {
            reader.Send(ScreenMessage.Build("LianLi/0", jpeg));
            reader.BlockWhenEmpty = true;
        };
        var vision = new LocatedDevice(0x0416, 0x7395, "fake/0-vision", null);
        var trinity = new LocatedDevice(0x0416, 0x7371, "fake/1-trinity", null);
        var enumerator = new FakeEnumerator(vision, trinity);
        using LianLiPlugin plugin = NewPlugin(enumerator);
        plugin.Initialize();
        plugin.Load(new FakeSensorsContainer());
        plugin.Update();
        FakeNamedPipeConnection connection = AwaitConnection(0);
        FakeDeviceTransport cooler = enumerator.TransportFor("fake/0-vision");

        Assert.True(
            SpinWait.SpinUntil(() => cooler.SnapshotTransfers().Any(t => t.Value[0] == 0x02 && t.Value[1] == 0x0E), TimeSpan.FromSeconds(10)),
            "the frame was not sent to the cooler");
        Assert.True(SpinWait.SpinUntil(() => connection.Lines.Count >= 2, TimeSpan.FromSeconds(10)));
        Assert.Contains(
            "{\"id\":\"LianLi/0\",\"name\":\"Lian Li GA II LCD\",\"kind\":\"cooler\",\"screen\":{\"width\":480,\"height\":480,\"format\":\"jpeg\"}}",
            connection.Lines[1],
            StringComparison.Ordinal);
        Assert.Contains("{\"id\":\"LianLi/1\",\"name\":\"Lian Li Galahad #2\",\"kind\":\"cooler\"}", connection.Lines[1], StringComparison.Ordinal);
        Assert.Equal(new[] { "SpaceDeck.Screen" }, _pipe.ReadAttempts);
        Assert.DoesNotContain(enumerator.TransportFor("fake/1-trinity").SnapshotTransfers(), t => t.Value[0] == 0x02);

        plugin.Close();

        Assert.True(Assert.Single(_pipe.Readers).Disposed);
    }

    [Fact]
    public void NothingRegistered_PublishesNothing() {
        using LianLiPlugin plugin = NewPlugin(new FakeEnumerator());
        plugin.Initialize();
        plugin.Load(new FakeSensorsContainer());
        plugin.Update();
        plugin.Close();

        Assert.Empty(_services.Queries);
    }

    [Fact]
    public void ReadingsThatCannotBePrepared_AreLogged_AndTheSensorsStillRegister() {
        using LianLiPlugin plugin = NewPlugin(new FakeEnumerator(Sli(0)));
        plugin.Initialize();
        var container = new FakeSensorsContainer();
        container.TempSensors.Add(new FakePluginSensor("Other/0/temp", "Other") { IdFails = true });

        plugin.Load(container);
        plugin.Update();

        Assert.Equal(4, container.ControlSensors.Count);
        Assert.Contains(
            "SpaceDeck: the readings could not be prepared, so none are sent to SpaceDeck; fan control is unaffected: the id cannot be read",
            _log.Messages);
        Assert.Empty(_services.Queries);
        plugin.Close();
    }

    [Fact]
    public void ReadingsThatCannotBeCaptured_StopThePublishing_AndAreLoggedOnce() {
        using LianLiPlugin plugin = NewPlugin(new FakeEnumerator(Sli(0)));
        plugin.Initialize();
        var container = new FakeSensorsContainer();
        container.TempSensors.Add(new FakePluginSensor("Other/0/temp", "Other") { ValueFails = true });
        plugin.Load(container);
        FakeNamedPipeConnection connection = AwaitConnection(0);

        plugin.Update();
        plugin.Update();

        Assert.Single(_log.Messages, m => m.StartsWith("SpaceDeck: the readings could not be captured", StringComparison.Ordinal));
        Assert.True(SpinWait.SpinUntil(() => connection.Disposed, TimeSpan.FromSeconds(10)));
        plugin.Close();
    }

    [Fact]
    public void ASecondLoad_ReplacesThePublisher() {
        using LianLiPlugin plugin = NewPlugin(new FakeEnumerator(Sli(0)));
        plugin.Initialize();
        plugin.Load(new FakeSensorsContainer());
        FakeNamedPipeConnection first = AwaitConnection(0);

        plugin.Load(new FakeSensorsContainer());

        Assert.True(first.Disposed);
        Assert.False(AwaitConnection(1).Disposed);
        plugin.Close();
    }

    [Fact]
    public void Initialize_StopsThePublisherOfTheLastLoad() {
        using LianLiPlugin plugin = NewPlugin(new FakeEnumerator(Sli(0)));
        plugin.Initialize();
        plugin.Load(new FakeSensorsContainer());
        FakeNamedPipeConnection first = AwaitConnection(0);

        plugin.Initialize();

        Assert.True(first.Disposed);
        plugin.Close();
    }

    [Fact]
    public void ANullPublication_IsRejected()
        => Assert.Throws<ArgumentNullException>(() => new LianLiPlugin(
            new FakeEnumerator(),
            new DeviceCatalog(),
            new FakeClock(),
            new FakeDelay(),
            _log,
            LConnectDirectory.Absent,
            new PluginRuntime(new FakeClock(), new FakeRememberedControllerStore()),
            CopyGuardFixture.Alone(),
            null!));
}
