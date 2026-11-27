#if ENABLE_LIGHTING
using System;
using System.Collections.Generic;
using System.Threading;
using FanControl.LianLi.Transport;

namespace FanControl.LianLi.Tests.Fakes;

/// <summary>
/// A loopback listener a test drives: <see cref="Listen"/> fails when a test says, or waits at
/// <see cref="ListenGate"/>, and the listener it makes hands out the connections a test queues, waiting
/// for the next until it is disposed.
/// </summary>
internal sealed class FakeLoopbackListenerApi : ILoopbackListenerApi {
    private readonly object _gate = new object();
    private readonly List<int> _ports = new List<int>();

    public Exception? ListenFails { get; set; }

    public ManualResetEventSlim? ListenGate { get; set; }

    public FakeLoopbackListener Listener { get; } = new FakeLoopbackListener();

    public IReadOnlyList<int> Ports { get { lock (_gate) { return _ports.ToArray(); } } }

    public ILoopbackListener Listen(int port) {
        lock (_gate) {
            _ports.Add(port);
        }

        ListenGate?.Wait();
        if (ListenFails != null) {
            throw ListenFails;
        }

        return Listener;
    }
}
#endif
