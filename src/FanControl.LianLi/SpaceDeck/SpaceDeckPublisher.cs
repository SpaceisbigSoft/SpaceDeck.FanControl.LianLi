using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using FanControl.LianLi.Logging;
using FanControl.LianLi.Transport;

namespace FanControl.LianLi.SpaceDeck;

/// <summary>
/// Sends the plugin's readings to the SpaceDeck collector over its plugin pipe, from a thread of its
/// own, never from FanControl's calls. It connects as a write-only client with a 250 ms timeout and,
/// before writing anything, checks that the pipe's server is the <c>SpaceDeckCollector</c> service's
/// own process; then it sends <c>hello</c>, the catalog, and the values once a second, and the
/// catalog again whenever the set of readings changes. While the collector is absent it tries again
/// after a gap that doubles from one second to thirty. A write that blocks for more than five
/// seconds drops the connection (the bound cancels it), and the next try reconnects. When the
/// collector is not installed at all it logs one line and stops. Readings only: nothing it does
/// reaches a device, and nothing that fails here leaves the thread, so fan control is the same with
/// or without it. Each run of failures is logged once, at its first failure, and each connection
/// once, as it is made, so a collector that stays away costs one line, not one every thirty seconds.
/// <see cref="Step"/> does one unit of work on the injected clock and says how long to wait for the
/// next; the thread only loops on it, so a test drives it without the thread.
/// </summary>
internal sealed class SpaceDeckPublisher : IDisposable {
    /// <summary>The collector's pipe, <c>\\.\pipe\SpaceDeck.Plugins</c>.</summary>
    public const string PipeName = "SpaceDeck.Plugins";

    /// <summary>The collector's Windows service, whose process must be the pipe's server.</summary>
    public const string ServiceName = "SpaceDeckCollector";

    /// <summary>How long a connect may wait for the pipe: the collector answers at once or is not there.</summary>
    public const int ConnectTimeoutMilliseconds = 250;

    /// <summary>The first gap between tries at a collector that is not there; it doubles from here.</summary>
    public const int FirstRetryMilliseconds = 1000;

    /// <summary>The longest gap between tries.</summary>
    public const int LongestRetryMilliseconds = 30000;

    /// <summary>How often the values are sent: about once a second, as the collector expects.</summary>
    public const int ValuesIntervalMilliseconds = 1000;

    /// <summary>How long one write may block before the connection is dropped.</summary>
    public const int WriteTimeoutMilliseconds = 5000;

    /// <summary>
    /// How old the last captured values may be before they are sent as nulls: FanControl stops
    /// updating the sensors while it refreshes, and a frozen value must not look like a reading.
    /// </summary>
    public const int StaleAfterMilliseconds = 5000;

    /// <summary>What <see cref="Step"/> returns once the publisher has nothing more to do.</summary>
    public const int Stopped = -1;

    /// <summary>The name a write is bounded under, in the bound's own log line for a late failure.</summary>
    public const string WriteOperation = "SpaceDeck pipe write";

    // How long Dispose waits for the thread. FanControl waits for Close, and the thread is almost
    // always waiting between steps; one inside a write ends when its bound gives up on it.
    private const int StopTimeoutMilliseconds = 1000;

    private readonly SpaceDeckReadings _readings;
    private readonly INamedPipeApi _pipe;
    private readonly IServiceControlApi _services;
    private readonly IDeviceCallRunner _runner;
    private readonly IDeviceCallClock _clock;
    private readonly ILog _log;
    private readonly string _hello;
    private readonly Thread _thread;

    // Set to end the wait between steps early: by Dispose to stop, by the first Offer to send at once.
    private readonly AutoResetEvent _signal = new AutoResetEvent(false);

    // Guards setting and disposing _signal: the thread disposes it as its last act.
    private readonly object _signalGate = new object();

    // Written by FanControl's thread (Offer), read by the publishing thread: a reference swap of an
    // immutable snapshot, so neither ever waits for the other.
    private volatile Snapshot? _latest;
    private volatile bool _stop;
    private int _stopSignalled;
    private bool _started;
    private bool _signalDisposed;

    // The publishing thread's own state (or the test's, calling Step).
    private INamedPipeConnection? _connection;
    private string? _sentCatalog;
    private long _nextValuesAt;
    private long _nextConnectAt;
    private int _retryGap = FirstRetryMilliseconds;
    private bool _failing;
    private bool _off;

    /// <summary>Create a publisher of <paramref name="readings"/>; <see cref="Start"/> starts its thread.</summary>
    public SpaceDeckPublisher(
        SpaceDeckReadings readings,
        INamedPipeApi pipe,
        IServiceControlApi services,
        IDeviceCallRunner runner,
        IDeviceCallClock clock,
        ILog log,
        string plugin,
        string version,
        Guid instance) {
        _readings = readings ?? throw new ArgumentNullException(nameof(readings));
        _pipe = pipe ?? throw new ArgumentNullException(nameof(pipe));
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _hello = SpaceDeckPipeEncoder.Hello(plugin, version, instance.ToString("D"));
        _thread = new Thread(Loop) { IsBackground = true, Name = "LianLiSpaceDeck" };
    }

    /// <summary>Whether the thread has finished (or never ran).</summary>
    public bool HasStopped => !_started || !_thread.IsAlive;

    /// <summary>Start the publishing thread.</summary>
    public void Start() {
        _started = true;
        _thread.Start();
    }

    /// <summary>
    /// Capture the readings' values now, for the thread to send. Called on FanControl's own thread
    /// from the plugin's <c>Update</c>: a property read per sensor and a reference swap, nothing that
    /// waits.
    /// </summary>
    public void Capture() => Offer(_readings.Catalog, _readings.Capture());

    /// <summary>
    /// Hand the thread <paramref name="values"/> for <paramref name="catalog"/>, one per reading. The
    /// first values handed over wake the thread, so the catalog goes out as soon as there are values
    /// to follow it.
    /// </summary>
    public void Offer(SpaceDeckCatalog catalog, IReadOnlyList<float?> values) {
        if (catalog is null) {
            throw new ArgumentNullException(nameof(catalog));
        }

        if (values is null) {
            throw new ArgumentNullException(nameof(values));
        }

        if (values.Count != catalog.Sensors.Count) {
            throw new ArgumentException("One value is needed for each reading in the catalog.", nameof(values));
        }

        bool first = _latest is null;
        _latest = new Snapshot(catalog, values, _clock.NowMilliseconds);
        if (first) {
            Signal();
        }
    }

    /// <summary>
    /// Do one unit of work - connect, or send what is due - and return how long to wait before the
    /// next, or <see cref="Stopped"/> once there is nothing more to do (the collector is not
    /// installed). Nothing escapes it: this runs on a thread the plugin starts itself.
    /// </summary>
    public int Step() {
        if (_off) {
            return Stopped;
        }

        long now = _clock.NowMilliseconds;
        if (_connection is null && now < _nextConnectAt) {
            return Wait(_nextConnectAt - now);
        }

        try {
            INamedPipeConnection? connection = _connection ?? Connect(now);
            if (connection is null) {
                return _off ? Stopped : Wait(_nextConnectAt - now);
            }

            return Publish(now, connection);
        }
#pragma warning disable CA1031 // resilience: this runs on a thread of the plugin's own, where an escaping exception ends FanControl's process, and readings for SpaceDeck never matter more than the fans
        catch (Exception ex) {
#pragma warning restore CA1031
            Disconnect();
            Failed(ex.Message, now);
            return Wait(_nextConnectAt - now);
        }
    }

    /// <summary>
    /// Stop the thread and wait a bounded time for it; one still inside a write ends when the write's
    /// bound gives up on it, and closes the connection itself. Idempotent.
    /// </summary>
    public void Dispose() {
        if (Interlocked.Exchange(ref _stopSignalled, 1) == 1) {
            return;
        }

        _stop = true;
        Signal();
        if (!_started) {
            Disconnect();
            ReleaseSignal();
        } else if (!_thread.Join(StopTimeoutMilliseconds)) {
            _log.Write("SpaceDeck: the publisher was still inside a write at Close; it stops when the write returns");
        }
    }

    // The service first: its process is what the pipe's server must be, and a collector that is
    // not installed switches the publisher off. Returns the connection, hello sent, or null.
    private INamedPipeConnection? Connect(long now) {
        int? collector = _services.QueryProcessId(ServiceName);
        if (collector is null) {
            _off = true;
            _log.Write("SpaceDeck: the SpaceDeck collector is not installed (there is no " + ServiceName + " service), so no readings are sent to it");
            return null;
        }

        if (collector.Value == 0) {
            Failed("the " + ServiceName + " service is not running", now);
            return null;
        }

        INamedPipeConnection? connection = _pipe.TryConnect(PipeName, ConnectTimeoutMilliseconds);
        if (connection is null) {
            Failed(string.Format(CultureInfo.InvariantCulture, "its pipe took no connection within {0} ms", ConnectTimeoutMilliseconds), now);
            return null;
        }

        int server;
        try {
            server = connection.ReadServerProcessId();
        } catch {
            connection.Dispose();
            throw;
        }

        if (server != collector.Value) {
            connection.Dispose();
            Failed(string.Format(
                CultureInfo.InvariantCulture,
                "the pipe's server is process {0}, not the {1} service's process {2}, so it was left without a word",
                server,
                ServiceName,
                collector.Value), now);
            return null;
        }

        _connection = connection;
        _sentCatalog = null;
        _nextValuesAt = now;
        _retryGap = FirstRetryMilliseconds;
        _failing = false;
        _log.Write(string.Format(
            CultureInfo.InvariantCulture,
            "SpaceDeck: connected to SpaceDeck's collector (process {0}); sending {1} reading(s) of {2} device(s) once a second",
            server,
            _readings.Catalog.Sensors.Count,
            _readings.Catalog.Hardware.Count));
        Write(connection, _hello);
        return connection;
    }

    // The catalog goes before any values for it, and again whenever the set of readings changes;
    // the values go once a second.
    private int Publish(long now, INamedPipeConnection connection) {
        Snapshot? latest = _latest;
        if (latest is null) {
            return ValuesIntervalMilliseconds;
        }

        if (!string.Equals(latest.Catalog.Line, _sentCatalog, StringComparison.Ordinal)) {
            Write(connection, latest.Catalog.Line);
            _sentCatalog = latest.Catalog.Line;
        }

        if (now >= _nextValuesAt) {
            IReadOnlyList<float?> values = now - latest.CapturedAt > StaleAfterMilliseconds
                ? new float?[latest.Values.Count]
                : latest.Values;
            Write(connection, SpaceDeckPipeEncoder.Values(values));
            _nextValuesAt = now + ValuesIntervalMilliseconds;
        }

        return Wait(_nextValuesAt - now);
    }

    // One line, bounded: a collector that stops reading fills the pipe and the write blocks, so after
    // the bound the connection is closed and the write cancelled, and the failure reconnects.
    private void Write(INamedPipeConnection connection, string line) {
        byte[] bytes = Encoding.UTF8.GetBytes(line);
        if (!_runner.TryRun(WriteOperation, _ => connection.Write(bytes), WriteTimeoutMilliseconds, connection.Dispose, NothingToRelease)) {
            _connection = null;
            throw new TimeoutException(string.Format(
                CultureInfo.InvariantCulture, "a write blocked for more than {0} ms, so the connection was dropped", WriteTimeoutMilliseconds));
        }
    }

    private static void NothingToRelease() {
    }

    // The first failure of a run is logged; the next try waits a gap that doubles up to the longest.
    private void Failed(string reason, long now) {
        if (!_failing) {
            _failing = true;
            _log.Write(string.Format(
                CultureInfo.InvariantCulture,
                "SpaceDeck: not connected to SpaceDeck's collector ({0}); trying again in the background, at most every {1} s",
                reason,
                LongestRetryMilliseconds / 1000));
        }

        _nextConnectAt = now + _retryGap;
        _retryGap = Math.Min(_retryGap * 2, LongestRetryMilliseconds);
    }

    private void Disconnect() {
        _connection?.Dispose();
        _connection = null;
    }

    private static int Wait(long milliseconds) => (int)Math.Max(0, Math.Min(milliseconds, LongestRetryMilliseconds));

    private void Signal() {
        lock (_signalGate) {
            if (!_signalDisposed) {
                _ = _signal.Set();
            }
        }
    }

    private void ReleaseSignal() {
        lock (_signalGate) {
            _signalDisposed = true;
            _signal.Dispose();
        }
    }

    private void Loop() {
        try {
            while (!_stop) {
                int wait = Step();
                if (wait == Stopped) {
                    break;
                }

                _ = _signal.WaitOne(wait);
            }
        } finally {
            Disconnect();
            ReleaseSignal();
        }
    }

    // What FanControl's thread hands the publishing thread: never changed once made.
    private sealed class Snapshot {
        public Snapshot(SpaceDeckCatalog catalog, IReadOnlyList<float?> values, long capturedAt) {
            Catalog = catalog;
            Values = values;
            CapturedAt = capturedAt;
        }

        public SpaceDeckCatalog Catalog { get; }

        public IReadOnlyList<float?> Values { get; }

        public long CapturedAt { get; }
    }
}
