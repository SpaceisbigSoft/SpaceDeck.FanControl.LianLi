#if ENABLE_LIGHTING
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using FanControl.LianLi.Logging;
using FanControl.LianLi.Transport;

namespace FanControl.LianLi.OpenRgb;

/// <summary>
/// The OpenRGB SDK server for the coolers' screen rings: a TCP listener on <c>127.0.0.1:6743</c> (not
/// OpenRGB's own 6742, so OpenRGB can connect to it as a client), on a thread of its own, and a thread
/// per client, at most <see cref="MaximumClients"/>. It may run as LocalSystem, inside FanControl's
/// service, and any program on this PC can connect, so a client is taken for hostile: a packet is at
/// most 64 KiB, must arrive whole within <see cref="PacketTimeoutMilliseconds"/> of its first byte, and
/// must decode exactly (<see cref="OpenRgbSession"/>); anything else closes that client, and each kind
/// of failure is logged once for the life of the server. A client that sends nothing is left connected,
/// as OpenRGB's clients stay connected without a word. No packet ever reaches a cooler: a command only
/// changes an <see cref="OpenRgbRing"/>, which hands the look to the cooler's own worker.
/// <see cref="Dispose"/> closes the listener and every client and waits a bounded time.
/// </summary>
internal sealed class OpenRgbServer : IDisposable {
    /// <summary>The port, beside OpenRGB's own 6742.</summary>
    public const int Port = 6743;

    /// <summary>The most clients at once; another is closed as soon as it connects.</summary>
    public const int MaximumClients = 4;

    /// <summary>How long a packet may take to arrive once its first byte has.</summary>
    public const int PacketTimeoutMilliseconds = 10000;

    /// <summary>How long a reply may wait for a client that does not read.</summary>
    public const int WriteTimeoutMilliseconds = 5000;

    // How long Dispose waits for the threads; closing their sockets ends what they wait on.
    private const int StopTimeoutMilliseconds = 1000;

    private readonly IReadOnlyList<OpenRgbRing> _rings;
    private readonly OpenRgbLookFile _looks;
    private readonly string _version;
    private readonly ILoopbackListenerApi _api;
    private readonly IDeviceCallClock _clock;
    private readonly ILog _log;
    private readonly Thread _thread;

    // Guards the listener, the clients and the log's record of what it has said.
    private readonly object _gate = new object();
    private readonly List<ILoopbackConnection> _clients = new List<ILoopbackConnection>();
    private readonly List<Thread> _clientThreads = new List<Thread>();
    private readonly HashSet<string> _logged = new HashSet<string>(StringComparer.Ordinal);
    private ILoopbackListener? _listener;
    private volatile bool _stop;
    private int _stopSignalled;
    private bool _started;

    /// <summary>A server for <paramref name="rings"/>, by their OpenRGB index; <see cref="Start"/> starts it.</summary>
    public OpenRgbServer(
        IReadOnlyList<OpenRgbRing> rings, OpenRgbLookFile looks, string version, ILoopbackListenerApi api, IDeviceCallClock clock, ILog log) {
        _rings = rings ?? throw new ArgumentNullException(nameof(rings));
        _looks = looks ?? throw new ArgumentNullException(nameof(looks));
        _version = version ?? throw new ArgumentNullException(nameof(version));
        _api = api ?? throw new ArgumentNullException(nameof(api));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _thread = new Thread(Listen) { IsBackground = true, Name = "LianLiOpenRgb" };
    }

    /// <summary>Whether the listening thread has finished (or never ran).</summary>
    public bool HasStopped => !_started || !_thread.IsAlive;

    /// <summary>How many clients are connected now.</summary>
    public int ClientCount {
        get {
            lock (_gate) {
                return _clients.Count;
            }
        }
    }

    /// <summary>Start listening, on the server's own thread.</summary>
    public void Start() {
        _started = true;
        _thread.Start();
    }

    /// <summary>
    /// Talk to one client until it leaves, breaks a rule or the server stops, then close it. Runs on
    /// the client's own thread; nothing escapes it.
    /// </summary>
    public void Serve(ILoopbackConnection connection) {
        if (connection is null) {
            throw new ArgumentNullException(nameof(connection));
        }

        var session = new OpenRgbSession(_rings, _looks, _version, _log);
        try {
            var header = new byte[OpenRgbProtocol.HeaderLength];
            while (ReadPacketStart(connection, header)) {
                long deadline = _clock.NowMilliseconds + PacketTimeoutMilliseconds;
                ReadExactly(connection, header, 1, header.Length - 1, deadline);
                int size = OpenRgbProtocol.ReadHeader(header, out long deviceIndex, out long packetId);
                var payload = new byte[size];
                ReadExactly(connection, payload, 0, size, deadline);
                foreach (byte[] reply in session.Handle(deviceIndex, packetId, payload)) {
                    connection.Write(reply, WriteTimeoutMilliseconds);
                }

                if (session.ClientName is string name) {
                    LogOnce("OpenRGB: client \"" + name + "\" connected");
                }
            }
        }
#pragma warning disable CA1031 // resilience: this runs on a thread of the plugin's own, where an escaping exception ends FanControl's process; a client that misbehaves costs only its connection
        catch (Exception ex) {
#pragma warning restore CA1031
            if (!_stop) {
                LogOnce(ex is FormatException
                    ? "OpenRGB: a client sent " + ex.Message + ", so it was disconnected (logged once)"
                    : "OpenRGB: a client was disconnected: " + ex.Message + " (logged once)");
            }
        } finally {
            lock (_gate) {
                _ = _clients.Remove(connection);
            }

            connection.Dispose();
        }
    }

    /// <summary>Close the listener and every client, and wait a bounded time for their threads. Idempotent.</summary>
    public void Dispose() {
        if (Interlocked.Exchange(ref _stopSignalled, 1) == 1) {
            return;
        }

        Thread[] threads;
        lock (_gate) {
            _stop = true;
            _listener?.Dispose();
            foreach (ILoopbackConnection client in _clients) {
                client.Dispose();
            }

            threads = _clientThreads.ToArray();
        }

        bool stopped = !_started || _thread.Join(StopTimeoutMilliseconds);
        foreach (Thread thread in threads) {
            stopped &= thread.Join(StopTimeoutMilliseconds);
        }

        if (!stopped) {
            _log.Write("OpenRGB: a thread of the SDK server was still running at Close; it stops when its socket call returns");
        }
    }

    // The first byte of a packet, waited for as long as the client likes; false when the client has
    // closed its end, which ends the conversation without a word.
    private static bool ReadPacketStart(ILoopbackConnection connection, byte[] header)
        => connection.Read(header, 0, 1, Timeout.Infinite) > 0;

    private void ReadExactly(ILoopbackConnection connection, byte[] buffer, int offset, int count, long deadline) {
        int read = 0;
        while (read < count) {
            long left = deadline - _clock.NowMilliseconds;
            if (left <= 0) {
                throw new TimeoutException(string.Format(
                    CultureInfo.InvariantCulture, "a packet did not arrive whole within {0} ms", PacketTimeoutMilliseconds));
            }

            int got = connection.Read(buffer, offset + read, count - read, (int)left);
            if (got <= 0) {
                throw new EndOfStreamException("the client closed its end in the middle of a packet");
            }

            read += got;
        }
    }

    private void Listen() {
        try {
            ILoopbackListener listener = _api.Listen(Port);
            lock (_gate) {
                if (_stop) {
                    listener.Dispose();
                    return;
                }

                _listener = listener;
            }

            _log.Write(string.Format(
                CultureInfo.InvariantCulture,
                "OpenRGB: SDK server listening on 127.0.0.1:{0} for {1} screen ring(s); in OpenRGB, add 127.0.0.1:{0} under Settings, SDK Client",
                Port,
                _rings.Count));
            while (true) {
                Accept(listener.Accept());
            }
        }
#pragma warning disable CA1031 // resilience: this runs on a thread of the plugin's own, where an escaping exception ends FanControl's process; without the server only the ring's OpenRGB control is lost
        catch (Exception ex) {
#pragma warning restore CA1031
            if (!_stop) {
                _log.Write("OpenRGB: the SDK server on 127.0.0.1:" + Port.ToString(CultureInfo.InvariantCulture) + " stopped: " + ex.Message);
            }
        }
    }

    /// <summary>
    /// Take <paramref name="connection"/>, just accepted, as a client on a thread of its own, or close it
    /// at once when the server is stopping or has as many clients as it takes.
    /// </summary>
    internal void Accept(ILoopbackConnection connection) {
        if (connection is null) {
            throw new ArgumentNullException(nameof(connection));
        }

        lock (_gate) {
            if (_stop || _clients.Count >= MaximumClients) {
                connection.Dispose();
                if (!_stop) {
                    LogOnce(string.Format(
                        CultureInfo.InvariantCulture, "OpenRGB: a client was turned away: {0} are connected already, the most taken (logged once)", MaximumClients));
                }

                return;
            }

            _clients.Add(connection);
            var thread = new Thread(() => Serve(connection)) { IsBackground = true, Name = "LianLiOpenRgbClient" };
            _ = _clientThreads.RemoveAll(t => !t.IsAlive);
            _clientThreads.Add(thread);
            thread.Start();
        }
    }

    private void LogOnce(string line) {
        lock (_gate) {
            if (!_logged.Add(line)) {
                return;
            }
        }

        _log.Write(line);
    }
}
#endif
