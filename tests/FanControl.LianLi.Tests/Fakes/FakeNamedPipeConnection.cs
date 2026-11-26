using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using FanControl.LianLi.Transport;

namespace FanControl.LianLi.Tests.Fakes;

/// <summary>
/// One connection a <see cref="FakeNamedPipeApi"/> made: records each write as the text it carried,
/// and fails a read of the server's process id, or a write, when a test says. A write can be held at
/// <see cref="WriteGate"/>, a server that has stopped reading.
/// </summary>
internal sealed class FakeNamedPipeConnection : INamedPipeConnection {
    private readonly object _gate = new object();
    private readonly List<string> _lines = new List<string>();
    private readonly int _serverProcessId;

    public FakeNamedPipeConnection(int serverProcessId) => _serverProcessId = serverProcessId;

    public Exception? ReadFails { get; set; }

    /// <summary>Thrown by the write with this zero-based number, counting every write on the connection.</summary>
    public int? FailWriteNumber { get; set; }

    public Exception WriteFailure { get; set; } = new System.IO.IOException("Pipe is broken.");

    public ManualResetEventSlim? WriteGate { get; set; }

    public bool Disposed { get; private set; }

    public IReadOnlyList<string> Lines { get { lock (_gate) { return _lines.ToArray(); } } }

    public int ReadServerProcessId() {
        if (ReadFails != null) {
            throw ReadFails;
        }

        return _serverProcessId;
    }

    public void Write(byte[] buffer) {
        WriteGate?.Wait();
        lock (_gate) {
            if (FailWriteNumber == _lines.Count) {
                throw WriteFailure;
            }

            _lines.Add(Encoding.UTF8.GetString(buffer));
        }
    }

    public void Dispose() => Disposed = true;
}
