using System;

namespace FanControl.LianLi.Transport;

/// <summary>
/// One read-only connection <see cref="INamedPipeApi.TryConnectForReading"/> made.
/// <see cref="IDisposable.Dispose"/> closes it, may be called from another thread while
/// <see cref="Read"/> waits, which then ends (by throwing or returning 0), and never throws.
/// </summary>
internal interface INamedPipeReader : IDisposable {
    /// <summary>
    /// The process id of the pipe's server, as Windows records it for the connected pipe, so the
    /// client can tell which process it reached before it reads anything. Throws when Windows
    /// cannot say.
    /// </summary>
    int ReadServerProcessId();

    /// <summary>
    /// Read up to <paramref name="count"/> bytes into <paramref name="buffer"/> at
    /// <paramref name="offset"/>, waiting for at least one for as long as it takes; returns how many
    /// were read, or 0 once the server has closed its end.
    /// </summary>
    int Read(byte[] buffer, int offset, int count);
}
