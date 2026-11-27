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
    private readonly List<string> _readAttempts = new List<string>();
    private readonly List<int> _readTimeouts = new List<int>();
    private readonly List<FakeNamedPipeReader> _readers = new List<FakeNamedPipeReader>();

    public bool Available { get; set; } = true;

    public int ServerProcessId { get; set; } = FakeServiceControlApi.CollectorProcessId;

    public Exception? Fails { get; set; }

    /// <summary>Applied to each connection as it is made, to script its failures.</summary>
    public Action<FakeNamedPipeConnection>? ConfigureConnection { get; set; }

    public IReadOnlyList<string> Attempts { get { lock (_gate) { return _attempts.ToArray(); } } }

    public IReadOnlyList<int> Timeouts { get { lock (_gate) { return _timeouts.ToArray(); } } }

    public IReadOnlyList<FakeNamedPipeConnection> Connections { get { lock (_gate) { return _connections.ToArray(); } } }

    /// <summary>Whether a read-only connection is taken; separate from <see cref="Available"/>, the readings' pipe.</summary>
    public bool ReadersAvailable { get; set; } = true;

    /// <summary>Applied to each read-only connection as it is made, to script what it reads.</summary>
    public Action<FakeNamedPipeReader>? ConfigureReader { get; set; }

    public IReadOnlyList<string> ReadAttempts { get { lock (_gate) { return _readAttempts.ToArray(); } } }

    public IReadOnlyList<int> ReadTimeouts { get { lock (_gate) { return _readTimeouts.ToArray(); } } }

    public IReadOnlyList<FakeNamedPipeReader> Readers { get { lock (_gate) { return _readers.ToArray(); } } }

    public INamedPipeReader? TryConnectForReading(string pipeName, int timeoutMilliseconds) {
        lock (_gate) {
            _readAttempts.Add(pipeName);
            _readTimeouts.Add(timeoutMilliseconds);
            if (!ReadersAvailable) {
                return null;
            }

            var reader = new FakeNamedPipeReader(ServerProcessId);
            ConfigureReader?.Invoke(reader);
            _readers.Add(reader);
            return reader;
        }
    }

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
