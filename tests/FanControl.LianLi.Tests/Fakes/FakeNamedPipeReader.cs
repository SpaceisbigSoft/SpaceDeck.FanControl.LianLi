using System;
using System.Collections.Generic;
using System.Threading;
using FanControl.LianLi.Transport;

namespace FanControl.LianLi.Tests.Fakes;

/// <summary>
/// One read-only connection a <see cref="FakeNamedPipeApi"/> made: hands out the bytes a test queued,
/// in as many reads as the test cut them into, then reports the server's end closed (0) - or, with
/// <see cref="BlockWhenEmpty"/>, waits as a quiet collector does until it is disposed. A read can
/// also be held at <see cref="HoldGate"/>, which disposing does not release.
/// </summary>
internal sealed class FakeNamedPipeReader : INamedPipeReader {
    private readonly object _gate = new object();
    private readonly List<byte[]> _chunks = new List<byte[]>();
    private readonly ManualResetEventSlim _disposed = new ManualResetEventSlim(false);
    private readonly int _serverProcessId;

    public FakeNamedPipeReader(int serverProcessId) => _serverProcessId = serverProcessId;

    public Exception? ReadFails { get; set; }

    public Exception? ServerProcessIdFails { get; set; }

    /// <summary>Run at the start of every read, before anything is handed out.</summary>
    public Action? OnRead { get; set; }

    /// <summary>Run as the server's process id is read.</summary>
    public Action? OnReadServerProcessId { get; set; }

    public bool BlockWhenEmpty { get; set; }

    public ManualResetEventSlim? HoldGate { get; set; }

    public bool Disposed => _disposed.IsSet;

    public int Reads { get; private set; }

    /// <summary>Queue <paramref name="bytes"/> to be read in one piece (or fewer, if a read asks for less).</summary>
    public void Send(params byte[] bytes) {
        lock (_gate) {
            _chunks.Add(bytes);
        }
    }

    public int ReadServerProcessId() {
        OnReadServerProcessId?.Invoke();
        if (ServerProcessIdFails != null) {
            throw ServerProcessIdFails;
        }

        return _serverProcessId;
    }

    public int Read(byte[] buffer, int offset, int count) {
        Reads++;
        OnRead?.Invoke();
        HoldGate?.Wait();
        if (ReadFails != null) {
            throw ReadFails;
        }

        lock (_gate) {
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
        }

        if (BlockWhenEmpty) {
            _disposed.Wait();
            throw new ObjectDisposedException(nameof(FakeNamedPipeReader));
        }

        return 0;
    }

    public void Dispose() => _disposed.Set();
}
