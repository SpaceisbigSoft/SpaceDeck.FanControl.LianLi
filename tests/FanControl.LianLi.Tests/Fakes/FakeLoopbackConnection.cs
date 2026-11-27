#if ENABLE_LIGHTING
using System;
using System.Collections.Generic;
using System.Threading;
using FanControl.LianLi.Transport;

namespace FanControl.LianLi.Tests.Fakes;

/// <summary>
/// One client of a <see cref="FakeLoopbackListener"/>: hands out the bytes a test queued, in the pieces
/// the test cut them into, then reports the client closed (0) - or, with <see cref="BlockWhenEmpty"/>,
/// waits as an idle client does until it is disposed. Every write is kept, and every read's timeout.
/// A read can be held at <see cref="HoldGate"/>, which disposing does not release.
/// </summary>
internal sealed class FakeLoopbackConnection : ILoopbackConnection {
    private readonly object _gate = new object();
    private readonly List<byte[]> _chunks = new List<byte[]>();
    private readonly List<byte[]> _writes = new List<byte[]>();
    private readonly List<int> _timeouts = new List<int>();
    private bool _disposed;

    public bool BlockWhenEmpty { get; set; }

    public ManualResetEventSlim? HoldGate { get; set; }

    /// <summary>Set when a read has started, for a test waiting on a client thread.</summary>
    public ManualResetEventSlim Reading { get; } = new ManualResetEventSlim(false);

    public Exception? WriteFails { get; set; }

    public bool Disposed {
        get {
            lock (_gate) {
                return _disposed;
            }
        }
    }

    public IReadOnlyList<byte[]> Writes { get { lock (_gate) { return _writes.ToArray(); } } }

    public IReadOnlyList<int> Timeouts { get { lock (_gate) { return _timeouts.ToArray(); } } }

    public void Send(params byte[] bytes) {
        lock (_gate) {
            _chunks.Add(bytes);
            Monitor.PulseAll(_gate);
        }
    }

    public int Read(byte[] buffer, int offset, int count, int timeoutMilliseconds) {
        Reading.Set();
        HoldGate?.Wait();
        lock (_gate) {
            _timeouts.Add(timeoutMilliseconds);
            while (true) {
                if (_chunks.Count > 0) {
                    byte[] chunk = _chunks[0];
                    int taken = Math.Min(count, chunk.Length);
                    Array.Copy(chunk, 0, buffer, offset, taken);
                    if (taken < chunk.Length) {
                        var rest = new byte[chunk.Length - taken];
                        Array.Copy(chunk, taken, rest, 0, rest.Length);
                        _chunks[0] = rest;
                    } else {
                        _chunks.RemoveAt(0);
                    }

                    return taken;
                }

                ObjectDisposedException.ThrowIf(_disposed, this);

                if (!BlockWhenEmpty) {
                    return 0;
                }

                _ = Monitor.Wait(_gate);
            }
        }
    }

    public void Write(byte[] buffer, int timeoutMilliseconds) {
        lock (_gate) {
            if (WriteFails != null) {
                throw WriteFails;
            }

            _writes.Add((byte[])buffer.Clone());
        }
    }

    public void Dispose() {
        lock (_gate) {
            _disposed = true;
            Monitor.PulseAll(_gate);
        }
    }
}
#endif
