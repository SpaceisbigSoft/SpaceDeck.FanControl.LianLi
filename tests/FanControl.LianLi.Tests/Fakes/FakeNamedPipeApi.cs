using System;
using System.Collections.Generic;
using FanControl.LianLi.Transport;

namespace FanControl.LianLi.Tests.Fakes;

/// <summary>
/// A pipe whose server is there or not as a test says (<see cref="Available"/>), answering as the
/// process <see cref="ServerProcessId"/>. Every attempt is recorded with its pipe name and timeout,
/// and every connection made is kept, so a test reads exactly what was written on each. Locked, since
/// the publisher's thread connects while a test reads.
/// </summary>
internal sealed class FakeNamedPipeApi : INamedPipeApi {
    private readonly object _gate = new object();
    private readonly List<string> _attempts = new List<string>();
    private readonly List<int> _timeouts = new List<int>();
    private readonly List<FakeNamedPipeConnection> _connections = new List<FakeNamedPipeConnection>();

    public bool Available { get; set; } = true;

    public int ServerProcessId { get; set; } = FakeServiceControlApi.CollectorProcessId;

    public Exception? Fails { get; set; }

    /// <summary>Applied to each connection as it is made, to script its failures.</summary>
    public Action<FakeNamedPipeConnection>? ConfigureConnection { get; set; }

    public IReadOnlyList<string> Attempts { get { lock (_gate) { return _attempts.ToArray(); } } }

    public IReadOnlyList<int> Timeouts { get { lock (_gate) { return _timeouts.ToArray(); } } }

    public IReadOnlyList<FakeNamedPipeConnection> Connections { get { lock (_gate) { return _connections.ToArray(); } } }

    public INamedPipeConnection? TryConnect(string pipeName, int timeoutMilliseconds) {
        lock (_gate) {
            _attempts.Add(pipeName);
            _timeouts.Add(timeoutMilliseconds);
            if (Fails != null) {
                throw Fails;
            }

            if (!Available) {
                return null;
            }

            var connection = new FakeNamedPipeConnection(ServerProcessId);
            ConfigureConnection?.Invoke(connection);
            _connections.Add(connection);
            return connection;
        }
    }
}
