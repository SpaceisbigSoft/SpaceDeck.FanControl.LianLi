using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using FanControl.LianLi.Devices;
using FanControl.LianLi.Logging;
using FanControl.LianLi.Transport;

namespace FanControl.LianLi.SpaceDeck;

/// <summary>
/// Reads the screen frames SpaceDeck's collector sends for the coolers' screens, from its screen pipe
/// (<c>\\.\pipe\SpaceDeck.Screen</c>), on a thread of its own, and hands each to its cooler's
/// <see cref="CoolerHandoff"/>, whose worker sends it. It connects as a read-only client with a 250 ms
/// timeout and, before reading anything, checks that the pipe's server is the
/// <c>SpaceDeckCollector</c> service's own process, as <see cref="SpaceDeckPublisher"/> does for the
/// readings; while the collector is absent it tries again after a gap that doubles from one second to
/// thirty, and when it is not installed it logs one line and stops. Nothing is ever written to the
/// pipe. Every message is checked (<see cref="SpaceDeckScreenDecoder"/>) and rationed - a burst of 8,
/// then 4 a second on average - and one that breaks a rule drops the connection; one for a device
/// without a screen is ignored. Each kind of failure is logged once for the life of the client, so a
/// collector that keeps misbehaving costs a line, not one per try. The only bytes from the pipe that
/// reach a cooler are a JPEG's, which the plugin wraps in packets of its own. <see cref="Step"/> does
/// one unit of work on the injected clock and says how long to wait for the next; the thread only
/// loops on it, so a test drives it without the thread.
/// </summary>
internal sealed class SpaceDeckScreenClient : IDisposable {
    /// <summary>The collector's screen pipe, <c>\\.\pipe\SpaceDeck.Screen</c>.</summary>
    public const string PipeName = "SpaceDeck.Screen";

    /// <summary>The most messages taken at once after a quiet spell: the token bucket's size.</summary>
    public const int BurstMessages = 8;

    /// <summary>The messages a second the bucket refills at: the collector forwards at most 2 a second per screen.</summary>
    public const int MessagesPerSecond = 4;

    /// <summary>What <see cref="Step"/> returns once the client has nothing more to do.</summary>
    public const int Stopped = -1;

    // How long Dispose waits for the thread: closing the connection ends a read that is waiting.
    private const int StopTimeoutMilliseconds = 1000;

    // The bucket counts in thousandths of a message, so it refills by whole numbers each millisecond.
    private const int TokenScale = 1000;

    private readonly IReadOnlyDictionary<string, CoolerHandoff> _screens;
    private readonly INamedPipeApi _pipe;
    private readonly IServiceControlApi _services;
    private readonly IDeviceCallClock _clock;
    private readonly ILog _log;
    private readonly Thread _thread;

    // Set to end the wait between connects early, by Dispose.
    private readonly AutoResetEvent _signal = new AutoResetEvent(false);

    // Guards setting and disposing _signal: the thread disposes it as its last act.
    private readonly object _signalGate = new object();

    // Guards _connection, which Dispose closes from its own thread to end a read that is waiting.
    private readonly object _connectionGate = new object();

    private volatile bool _stop;
    private int _stopSignalled;
    private bool _started;
    private bool _signalDisposed;
    private INamedPipeReader? _connection;

    // The reading thread's own state (or the test's, calling Step).
    private readonly HashSet<string> _logged = new HashSet<string>(StringComparer.Ordinal);
    private long _nextConnectAt;
    private int _retryGap = SpaceDeckPublisher.FirstRetryMilliseconds;
    private bool _off;
    private long _tokens;
    private long _tokensAt;

    /// <summary>
    /// Create a client that hands the frames for each hardware id in <paramref name="screens"/> to its
    /// handoff; <see cref="Start"/> starts its thread.
    /// </summary>
    public SpaceDeckScreenClient(
        IReadOnlyDictionary<string, CoolerHandoff> screens,
        INamedPipeApi pipe,
        IServiceControlApi services,
        IDeviceCallClock clock,
        ILog log) {
        _screens = screens ?? throw new ArgumentNullException(nameof(screens));
        _pipe = pipe ?? throw new ArgumentNullException(nameof(pipe));
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _thread = new Thread(Loop) { IsBackground = true, Name = "LianLiSpaceDeckScreen" };
    }

    /// <summary>Whether the thread has finished (or never ran).</summary>
    public bool HasStopped => !_started || !_thread.IsAlive;

    /// <summary>Start the reading thread.</summary>
    public void Start() {
        _started = true;
        _thread.Start();
    }

    /// <summary>
    /// Do one unit of work - connect, or read one message and hand its frame over - and return how
    /// long to wait before the next, or <see cref="Stopped"/> once there is nothing more to do (the
    /// collector is not installed, or the client is being disposed). A read waits for as long as the
    /// collector sends nothing. Nothing escapes it: this runs on a thread the plugin starts itself.
    /// </summary>
    public int Step() {
        if (_off || _stop) {
            return Stopped;
        }

        long now = _clock.NowMilliseconds;
        INamedPipeReader? connection = Volatile.Read(ref _connection);
        if (connection is null && now < _nextConnectAt) {
            return Wait(_nextConnectAt - now);
        }

        try {
            connection ??= Connect(now);
            if (connection is null) {
                return _off ? Stopped : Wait(_nextConnectAt - now);
            }

            ReadMessage(connection);
            return 0;
        }
#pragma warning disable CA1031 // resilience: this runs on a thread of the plugin's own, where an escaping exception ends FanControl's process, and a picture on the cooler's screen never matters more than the fans
        catch (Exception ex) {
#pragma warning restore CA1031
            Disconnect();
            if (_stop) {
                return Stopped;
            }

            string kind = ex is FormatException ? "the collector sent " + ex.Message + ", so the connection was dropped" : ex.Message;
            Failed(kind, now);
            return Wait(_nextConnectAt - now);
        }
    }

    /// <summary>
    /// Stop the thread and wait a bounded time for it, closing the connection so a read that waits for
    /// the next frame ends at once. Idempotent.
    /// </summary>
    public void Dispose() {
        if (Interlocked.Exchange(ref _stopSignalled, 1) == 1) {
            return;
        }

        _stop = true;
        Signal();
        Disconnect();
        if (!_started) {
            ReleaseSignal();
        } else if (!_thread.Join(StopTimeoutMilliseconds)) {
            _log.Write("SpaceDeck screen: the client was still reading at Close; it stops when the read returns");
        }
    }

    // The service first: its process is what the pipe's server must be, and a collector that is
    // not installed switches the client off. Returns the connection, or null.
    private INamedPipeReader? Connect(long now) {
        int? collector = _services.QueryProcessId(SpaceDeckPublisher.ServiceName);
        if (collector is null) {
            _off = true;
            _log.Write("SpaceDeck screen: the SpaceDeck collector is not installed (there is no " + SpaceDeckPublisher.ServiceName + " service), so nothing is shown on the coolers' screens");
            return null;
        }

        if (collector.Value == 0) {
            Failed("the " + SpaceDeckPublisher.ServiceName + " service is not running", now);
            return null;
        }

        INamedPipeReader? connection = _pipe.TryConnectForReading(PipeName, SpaceDeckPublisher.ConnectTimeoutMilliseconds);
        if (connection is null) {
            Failed(string.Format(CultureInfo.InvariantCulture, "its screen pipe took no connection within {0} ms", SpaceDeckPublisher.ConnectTimeoutMilliseconds), now);
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
                "the screen pipe's server is process {0}, not the {1} service's process {2}, so it was left unread",
                server,
                SpaceDeckPublisher.ServiceName,
                collector.Value), now);
            return null;
        }

        // Dispose may have run since the step began; a connection it could not see is closed here.
        lock (_connectionGate) {
            if (_stop) {
                connection.Dispose();
                return null;
            }

            _connection = connection;
        }

        _tokens = BurstMessages * TokenScale;
        _tokensAt = now;
        LogOnce(string.Format(
            CultureInfo.InvariantCulture,
            "SpaceDeck screen: connected to SpaceDeck's collector (process {0}); showing its frames on {1} cooler screen(s)",
            server,
            _screens.Count));
        return connection;
    }

    // One message: the header, then the id and the payload's length, then the payload. The message is
    // rationed once its header is in, by the time it arrived.
    private void ReadMessage(INamedPipeReader connection) {
        byte[] header = ReadExactly(connection, SpaceDeckScreenDecoder.HeaderLength);
        Ration();
        int idLength = SpaceDeckScreenDecoder.ReadHeader(header);
        byte[] idAndLength = ReadExactly(connection, idLength + SpaceDeckScreenDecoder.PayloadLengthLength);
        string id = SpaceDeckScreenDecoder.ReadId(idAndLength);
        byte[] payload = ReadExactly(connection, SpaceDeckScreenDecoder.ReadPayloadLength(idAndLength));
        SpaceDeckScreenDecoder.CheckJpeg(payload);

        // A valid message ends a run of failures, so the next one is tried again after a second.
        _retryGap = SpaceDeckPublisher.FirstRetryMilliseconds;
        if (_screens.TryGetValue(id, out CoolerHandoff? screen)) {
            screen.OfferFrame(payload);
        } else {
            LogOnce("SpaceDeck screen: frames for " + id + ", which has no screen the plugin drives, are ignored (logged once)");
        }
    }

    // Refill the bucket for the time since it was last read, then take a message's worth from it.
    private void Ration() {
        long now = _clock.NowMilliseconds;
        long elapsed = Math.Max(0, now - _tokensAt);
        _tokens = Math.Min(BurstMessages * TokenScale, _tokens + (elapsed * MessagesPerSecond));
        _tokensAt = now;
        if (_tokens < TokenScale) {
            throw new FormatException("more than 4 messages a second");
        }

        _tokens -= TokenScale;
    }

    private static byte[] ReadExactly(INamedPipeReader connection, int count) {
        var buffer = new byte[count];
        int read = 0;
        while (read < count) {
            int got = connection.Read(buffer, read, count - read);
            if (got <= 0) {
                throw new EndOfStreamException("the collector closed its screen pipe");
            }

            read += got;
        }

        return buffer;
    }

    // Each kind of failure is logged the first time; the next try waits a gap that doubles up to the longest.
    private void Failed(string reason, long now) {
        LogOnce(string.Format(
            CultureInfo.InvariantCulture,
            "SpaceDeck screen: not reading SpaceDeck's collector ({0}); trying again in the background, at most every {1} s (logged once)",
            reason,
            SpaceDeckPublisher.LongestRetryMilliseconds / 1000));
        _nextConnectAt = now + _retryGap;
        _retryGap = Math.Min(_retryGap * 2, SpaceDeckPublisher.LongestRetryMilliseconds);
    }

    private void LogOnce(string line) {
        if (_logged.Add(line)) {
            _log.Write(line);
        }
    }

    private void Disconnect() {
        INamedPipeReader? connection;
        lock (_connectionGate) {
            connection = _connection;
            _connection = null;
        }

        connection?.Dispose();
    }

    private static int Wait(long milliseconds) => (int)Math.Max(0, Math.Min(milliseconds, SpaceDeckPublisher.LongestRetryMilliseconds));

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
        // Step itself says Stopped once Dispose has run, so the loop needs no check of its own.
        try {
            for (int wait = Step(); wait != Stopped; wait = Step()) {
                _ = _signal.WaitOne(wait);
            }
        } finally {
            Disconnect();
            ReleaseSignal();
        }
    }
}
