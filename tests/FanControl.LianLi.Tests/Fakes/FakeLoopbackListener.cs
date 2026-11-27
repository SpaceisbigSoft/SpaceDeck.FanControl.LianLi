#if ENABLE_LIGHTING
using System;
using System.Collections.Generic;
using System.Threading;
using FanControl.LianLi.Transport;

namespace FanControl.LianLi.Tests.Fakes;

/// <summary>The listener a <see cref="FakeLoopbackListenerApi"/> makes.</summary>
internal sealed class FakeLoopbackListener : ILoopbackListener {
    private readonly object _gate = new object();
    private readonly Queue<FakeLoopbackConnection> _pending = new Queue<FakeLoopbackConnection>();
    private bool _disposed;

    public bool Disposed {
        get {
            lock (_gate) {
                return _disposed;
            }
        }
    }

    /// <summary>Thrown by the next Accept instead of a connection, when set.</summary>
    public Exception? AcceptFails { get; set; }

    public void Connect(FakeLoopbackConnection connection) {
        lock (_gate) {
            _pending.Enqueue(connection);
            Monitor.PulseAll(_gate);
        }
    }

    public ILoopbackConnection Accept() {
        lock (_gate) {
            while (true) {
                if (AcceptFails != null) {
                    throw AcceptFails;
                }

                ObjectDisposedException.ThrowIf(_disposed, this);

                if (_pending.Count > 0) {
                    return _pending.Dequeue();
                }

                _ = Monitor.Wait(_gate);
            }
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
