using System;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using FanControl.LianLi.SpaceDeck;
using FanControl.LianLi.Tests.Fakes;
using Xunit;

namespace FanControl.LianLi.Tests.SpaceDeck;

/// <summary>
/// The publisher on a fake clock, pipe and service control manager: connect, check the server,
/// hello, catalog, values once a second, the catalog again when the set changes, the backoff while
/// the collector is away, the write watchdog, the off switch, and the thread stopping.
/// </summary>
public class SpaceDeckPublisherTests {
    private static readonly Guid Instance = new Guid("9f1c0000-0000-0000-0000-000000000001");

    private readonly FakeDeviceCallClock _clock = new FakeDeviceCallClock();
    private readonly FakeNamedPipeApi _pipe = new FakeNamedPipeApi();
    private readonly FakeServiceControlApi _services = new FakeServiceControlApi();
    private readonly FakeDeviceCallRunner _runner = new FakeDeviceCallRunner();
    private readonly FakeLogger _log = new FakeLogger();
    private readonly FakePluginSensor _coolant = new FakePluginSensor("LianLi/0/coolant/temp", "Lian Li GA II LCD Coolant", 31.5f);
    private readonly FakePluginSensor _pump = new FakePluginSensor("LianLi/0/ch1/fan", "Lian Li GA II LCD Pump RPM", 2800f);

    private const string Hello = "{\"t\":\"hello\",\"v\":1,\"plugin\":\"Lian Li Uni (Lighting)\",\"version\":\"2.0.7\",\"instance\":\"9f1c0000-0000-0000-0000-000000000001\"}\n";

    private SpaceDeckReadings Readings() {
        var container = new FakeSensorsContainer();
        container.TempSensors.Add(_coolant);
        container.FanSensors.Add(_pump);
        return SpaceDeckReadings.From(container, _log);
    }

    private SpaceDeckPublisher NewPublisher(SpaceDeckReadings? readings = null)
        => new SpaceDeckPublisher(readings ?? Readings(), _pipe, _services, _runner, _clock, _log, "Lian Li Uni (Lighting)", "2.0.7", Instance);

    private static SpaceDeckCatalog CatalogOf(params string[] fanIds)
        => new SpaceDeckCatalog(
            new[] { new SpaceDeckHardware("LianLi/0", "Cooler", SpaceDeckHardware.CoolerKind) },
            fanIds.Select(id => new SpaceDeckSensor(id, "LianLi/0", "Fan", SpaceDeckSensor.FanKind)));

    private string[] Lines(int connection = 0) => _pipe.Connections[connection].Lines.ToArray();

    // Steps until the publisher waits, advancing the clock by each wait, and returns when each
    // connect was tried, relative to the first.
    private long[] AttemptTimes(SpaceDeckPublisher publisher, int attempts) {
        var times = new long[attempts];
        long start = _clock.NowMilliseconds;
        int seen = 0;
        while (seen < attempts) {
            int before = _services.Queries.Count;
            int wait = publisher.Step();
            if (_services.Queries.Count > before) {
                times[seen++] = _clock.NowMilliseconds - start;
            }

            _clock.NowMilliseconds += wait;
        }

        return times;
    }

    [Fact]
    public void Connecting_SendsHelloThenTheCatalogThenValuesOnceASecond() {
        using SpaceDeckPublisher publisher = NewPublisher();
        publisher.Capture();

        Assert.Equal(SpaceDeckPublisher.ValuesIntervalMilliseconds, publisher.Step());

        Assert.Equal(new[] { SpaceDeckPublisher.ServiceName }, _services.Queries);
        Assert.Equal(new[] { "SpaceDeck.Plugins" }, _pipe.Attempts);
        Assert.Equal(new[] { 250 }, _pipe.Timeouts);
        Assert.Equal(
            new[] {
                Hello,
                "{\"t\":\"catalog\",\"hardware\":[{\"id\":\"LianLi/0\",\"name\":\"Lian Li GA II LCD\",\"kind\":\"cooler\"}],\"sensors\":["
                    + "{\"id\":\"LianLi/0/coolant/temp\",\"hardware\":\"LianLi/0\",\"name\":\"Lian Li GA II LCD Coolant\",\"kind\":\"temperature\"},"
                    + "{\"id\":\"LianLi/0/ch1/fan\",\"hardware\":\"LianLi/0\",\"name\":\"Lian Li GA II LCD Pump RPM\",\"kind\":\"fan\"}]}\n",
                "{\"t\":\"values\",\"values\":[31.5,2800]}\n",
            },
            Lines());
        Assert.All(_runner.Operations, operation => Assert.Equal(SpaceDeckPublisher.WriteOperation, operation));
        Assert.All(_runner.Timeouts, timeout => Assert.Equal(SpaceDeckPublisher.WriteTimeoutMilliseconds, timeout));
        Assert.Equal(
            "SpaceDeck: connected to SpaceDeck's collector (process 4242); sending 2 reading(s) of 1 device(s) once a second",
            Assert.Single(_log.Messages));

        // Nothing more until the second is up, then the values as they are then.
        _clock.NowMilliseconds = 999;
        _pump.Value = 2850f;
        publisher.Capture();
        Assert.Equal(1, publisher.Step());
        Assert.Equal(3, Lines().Length);

        _clock.NowMilliseconds = 1000;
        Assert.Equal(1000, publisher.Step());
        Assert.Equal("{\"t\":\"values\",\"values\":[31.5,2850]}\n", Lines()[3]);
        Assert.Single(_pipe.Attempts);
    }

    [Fact]
    public void BeforeAnyValues_OnlyHelloIsSent_AndTheCatalogFollowsTheFirstValues() {
        using SpaceDeckPublisher publisher = NewPublisher();

        Assert.Equal(SpaceDeckPublisher.ValuesIntervalMilliseconds, publisher.Step());
        Assert.Equal(new[] { Hello }, Lines());

        publisher.Capture();
        publisher.Step();

        Assert.Equal(3, Lines().Length);
        Assert.StartsWith("{\"t\":\"catalog\"", Lines()[1], StringComparison.Ordinal);
    }

    [Fact]
    public void TheCatalog_IsSentAgainWhenTheSetOfReadingsChanges_AndOnlyThen() {
        using SpaceDeckPublisher publisher = NewPublisher();
        publisher.Offer(CatalogOf("LianLi/0/ch0/fan"), new float?[] { 900f });
        publisher.Step();

        // The same set in a new catalog is not a change.
        _clock.NowMilliseconds = 1000;
        publisher.Offer(CatalogOf("LianLi/0/ch0/fan"), new float?[] { 910f });
        publisher.Step();
        Assert.Equal(4, Lines().Length);

        _clock.NowMilliseconds = 1500;
        publisher.Offer(CatalogOf("LianLi/0/ch0/fan", "LianLi/0/ch1/fan"), new float?[] { 920f, 1500f });
        Assert.Equal(500, publisher.Step());
        Assert.Equal(CatalogOf("LianLi/0/ch0/fan", "LianLi/0/ch1/fan").Line, Lines()[4]);

        _clock.NowMilliseconds = 2000;
        publisher.Step();
        Assert.Equal("{\"t\":\"values\",\"values\":[920,1500]}\n", Lines()[5]);
    }

    [Fact]
    public void ValuesNotCapturedForMoreThanFiveSeconds_AreSentAsNulls() {
        using SpaceDeckPublisher publisher = NewPublisher();
        publisher.Capture();
        publisher.Step();

        _clock.NowMilliseconds = SpaceDeckPublisher.StaleAfterMilliseconds;
        publisher.Step();
        _clock.NowMilliseconds = SpaceDeckPublisher.StaleAfterMilliseconds + SpaceDeckPublisher.ValuesIntervalMilliseconds;
        publisher.Step();

        Assert.Equal("{\"t\":\"values\",\"values\":[31.5,2800]}\n", Lines()[3]);
        Assert.Equal("{\"t\":\"values\",\"values\":[null,null]}\n", Lines()[4]);
    }

    [Fact]
    public void ACollectorThatIsNotInstalled_IsLoggedOnce_AndThePublisherStops() {
        _services.ProcessId = null;
        using SpaceDeckPublisher publisher = NewPublisher();

        Assert.Equal(SpaceDeckPublisher.Stopped, publisher.Step());
        Assert.Equal(SpaceDeckPublisher.Stopped, publisher.Step());

        Assert.Single(_services.Queries);
        Assert.Empty(_pipe.Attempts);
        Assert.Equal(
            "SpaceDeck: the SpaceDeck collector is not installed (there is no SpaceDeckCollector service), so no readings are sent to it",
            Assert.Single(_log.Messages));
    }

    [Fact]
    public void ACollectorThatIsNotRunning_IsTriedAgainAfterGapsDoublingFromOneSecondToThirty() {
        _services.ProcessId = 0;
        using SpaceDeckPublisher publisher = NewPublisher();

        Assert.Equal(new long[] { 0, 1000, 3000, 7000, 15000, 31000, 61000, 91000 }, AttemptTimes(publisher, 8));

        Assert.Empty(_pipe.Attempts);
        Assert.Equal(
            "SpaceDeck: not connected to SpaceDeck's collector (the SpaceDeckCollector service is not running); trying again in the background, at most every 30 s",
            Assert.Single(_log.Messages));
    }

    [Fact]
    public void AStepBeforeTheNextTry_TriesNothing_AndSaysHowLongToWait() {
        _pipe.Available = false;
        using SpaceDeckPublisher publisher = NewPublisher();
        Assert.Equal(1000, publisher.Step());

        _clock.NowMilliseconds = 400;
        Assert.Equal(600, publisher.Step());

        Assert.Single(_pipe.Attempts);
    }

    [Fact]
    public void APipeThatIsNotThereYet_LogsTheFirstFailureAndTheConnectionOnly() {
        _pipe.Available = false;
        using SpaceDeckPublisher publisher = NewPublisher();
        AttemptTimes(publisher, 3);

        _pipe.Available = true;
        _clock.NowMilliseconds = 100000;
        publisher.Step();

        Assert.Equal(4, _pipe.Attempts.Count);
        Assert.Equal(
            new[] {
                "SpaceDeck: not connected to SpaceDeck's collector (its pipe took no connection within 250 ms); trying again in the background, at most every 30 s",
                "SpaceDeck: connected to SpaceDeck's collector (process 4242); sending 2 reading(s) of 1 device(s) once a second",
            },
            _log.Messages);
    }

    [Fact]
    public void AServerThatIsNotTheCollectorsProcess_IsLeftWithoutAWord_AndTriedAgainLater() {
        _pipe.ServerProcessId = 999;
        using SpaceDeckPublisher publisher = NewPublisher();
        publisher.Capture();

        Assert.Equal(1000, publisher.Step());

        FakeNamedPipeConnection rejected = Assert.Single(_pipe.Connections);
        Assert.Empty(rejected.Lines);
        Assert.True(rejected.Disposed);
        Assert.Equal(
            "SpaceDeck: not connected to SpaceDeck's collector (the pipe's server is process 999, not the SpaceDeckCollector service's process 4242, so it was left without a word); trying again in the background, at most every 30 s",
            Assert.Single(_log.Messages));

        _pipe.ServerProcessId = FakeServiceControlApi.CollectorProcessId;
        _clock.NowMilliseconds = 1000;
        publisher.Step();
        Assert.Equal(3, Lines(1).Length);
    }

    [Fact]
    public void AServerWhoseProcessCannotBeRead_IsDisconnected() {
        _pipe.ConfigureConnection = connection => connection.ReadFails = new Win32Exception(5, "Access is denied");
        using SpaceDeckPublisher publisher = NewPublisher();

        Assert.Equal(1000, publisher.Step());

        Assert.True(_pipe.Connections[0].Disposed);
        Assert.Empty(_pipe.Connections[0].Lines);
        Assert.Contains("(Access is denied)", Assert.Single(_log.Messages), StringComparison.Ordinal);
    }

    [Fact]
    public void AConnectOrServiceQueryThatFails_IsAFailureLikeAnyOther() {
        _pipe.Fails = new UnauthorizedAccessException("Access to the path is denied.");
        using SpaceDeckPublisher publisher = NewPublisher();
        Assert.Equal(1000, publisher.Step());

        _pipe.Fails = null;
        _services.Fails = new Win32Exception(5, "Access is denied");
        _clock.NowMilliseconds = 1000;
        Assert.Equal(2000, publisher.Step());

        Assert.Empty(_pipe.Connections);
        Assert.Contains("(Access to the path is denied.)", Assert.Single(_log.Messages), StringComparison.Ordinal);
    }

    [Fact]
    public void AWriteThatBlocksForFiveSeconds_DropsTheConnection_AndTheNextTryReconnects() {
        _runner.TimesOut = _ => true;
        using SpaceDeckPublisher publisher = NewPublisher();
        publisher.Capture();

        Assert.Equal(1000, publisher.Step());

        Assert.True(_pipe.Connections[0].Disposed);
        Assert.Equal(SpaceDeckPublisher.WriteTimeoutMilliseconds, _runner.TimeoutOf(SpaceDeckPublisher.WriteOperation));
        Assert.Equal(
            "SpaceDeck: not connected to SpaceDeck's collector (a write blocked for more than 5000 ms, so the connection was dropped); trying again in the background, at most every 30 s",
            _log.Messages[1]);

        _runner.TimesOut = _ => false;
        _clock.NowMilliseconds = 1000;
        publisher.Step();
        Assert.Equal(Hello, Lines(1)[0]);
        Assert.Equal(3, Lines(1).Length);
    }

    [Fact]
    public void AConnectionTheCollectorDrops_IsReplaced_WithHelloAndTheCatalogAgain() {
        _pipe.ConfigureConnection = connection => connection.FailWriteNumber = 3;
        using SpaceDeckPublisher publisher = NewPublisher();
        publisher.Capture();
        publisher.Step();

        _clock.NowMilliseconds = 1000;
        publisher.Capture();
        Assert.Equal(1000, publisher.Step());
        Assert.True(_pipe.Connections[0].Disposed);

        _pipe.ConfigureConnection = null;
        _clock.NowMilliseconds = 2000;
        publisher.Step();

        Assert.Equal(new[] { Hello, Lines(0)[1], "{\"t\":\"values\",\"values\":[31.5,2800]}\n" }, Lines(1));
        Assert.Equal(
            new[] {
                "SpaceDeck: connected to SpaceDeck's collector (process 4242); sending 2 reading(s) of 1 device(s) once a second",
                "SpaceDeck: not connected to SpaceDeck's collector (Pipe is broken.); trying again in the background, at most every 30 s",
                "SpaceDeck: connected to SpaceDeck's collector (process 4242); sending 2 reading(s) of 1 device(s) once a second",
            },
            _log.Messages);
    }

    [Fact]
    public void Offer_NeedsOneValuePerReading() {
        using SpaceDeckPublisher publisher = NewPublisher();

        Assert.Throws<ArgumentException>(() => publisher.Offer(CatalogOf("LianLi/0/ch0/fan"), new float?[] { 1f, 2f }));
        Assert.Throws<ArgumentNullException>(() => publisher.Offer(null!, new float?[] { 1f }));
        Assert.Throws<ArgumentNullException>(() => publisher.Offer(CatalogOf("LianLi/0/ch0/fan"), null!));
    }

    [Fact]
    public void TheThread_StopsByItself_WhenTheCollectorIsNotInstalled() {
        _services.ProcessId = null;
        using SpaceDeckPublisher publisher = NewPublisher();

        publisher.Start();

        Assert.True(SpinWait.SpinUntil(() => publisher.HasStopped, TimeSpan.FromSeconds(10)));
        publisher.Capture(); // a wake for a thread that has gone is ignored
        publisher.Dispose();
        Assert.Single(_log.Messages);
    }

    [Fact]
    public void Dispose_StopsTheThread_AndClosesTheConnection() {
        SpaceDeckPublisher publisher = NewPublisher();
        publisher.Start();
        Assert.True(SpinWait.SpinUntil(() => _pipe.Connections.Count == 1 && Lines().Length == 1, TimeSpan.FromSeconds(10)));

        // The first values wake the thread at once, rather than at the end of its wait.
        publisher.Capture();
        Assert.True(SpinWait.SpinUntil(() => Lines().Length == 3, TimeSpan.FromSeconds(10)));

        publisher.Dispose();

        Assert.True(publisher.HasStopped);
        Assert.True(_pipe.Connections[0].Disposed);
        publisher.Dispose();
    }

    [Fact]
    public void Dispose_WhileAWriteIsBlocked_WaitsABoundedTime_AndTheThreadEndsWhenTheWriteReturns() {
        using var gate = new ManualResetEventSlim(false);
        _pipe.ConfigureConnection = connection => connection.WriteGate = gate;
        SpaceDeckPublisher publisher = NewPublisher();
        publisher.Start();
        Assert.True(SpinWait.SpinUntil(() => _pipe.Connections.Count == 1, TimeSpan.FromSeconds(10)));

        publisher.Dispose();

        Assert.False(publisher.HasStopped);
        Assert.Contains("SpaceDeck: the publisher was still inside a write at Close; it stops when the write returns", _log.Messages);
        gate.Set();
        Assert.True(SpinWait.SpinUntil(() => publisher.HasStopped, TimeSpan.FromSeconds(10)));
        Assert.True(_pipe.Connections[0].Disposed);
    }

    [Fact]
    public void Dispose_WithoutStart_ReleasesEverything() {
        SpaceDeckPublisher publisher = NewPublisher();
        publisher.Capture();
        publisher.Step();

        publisher.Dispose();
        publisher.Dispose();

        Assert.True(publisher.HasStopped);
        Assert.True(_pipe.Connections[0].Disposed);
    }

    [Fact]
    public void NullArguments_AreRejected() {
        SpaceDeckReadings readings = Readings();
        Assert.Throws<ArgumentNullException>(() => new SpaceDeckPublisher(null!, _pipe, _services, _runner, _clock, _log, "p", "v", Instance));
        Assert.Throws<ArgumentNullException>(() => new SpaceDeckPublisher(readings, null!, _services, _runner, _clock, _log, "p", "v", Instance));
        Assert.Throws<ArgumentNullException>(() => new SpaceDeckPublisher(readings, _pipe, null!, _runner, _clock, _log, "p", "v", Instance));
        Assert.Throws<ArgumentNullException>(() => new SpaceDeckPublisher(readings, _pipe, _services, null!, _clock, _log, "p", "v", Instance));
        Assert.Throws<ArgumentNullException>(() => new SpaceDeckPublisher(readings, _pipe, _services, _runner, null!, _log, "p", "v", Instance));
        Assert.Throws<ArgumentNullException>(() => new SpaceDeckPublisher(readings, _pipe, _services, _runner, _clock, null!, "p", "v", Instance));
    }
}
