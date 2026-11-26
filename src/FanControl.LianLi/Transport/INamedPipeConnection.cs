using System;

namespace FanControl.LianLi.Transport;

/// <summary>
/// One connection <see cref="INamedPipeApi.TryConnect"/> made. <see cref="IDisposable.Dispose"/>
/// closes it, may be called from another thread while <see cref="Write"/> blocks, and never throws.
/// </summary>
internal interface INamedPipeConnection : IDisposable {
    /// <summary>
    /// The process id of the pipe's server, as Windows records it for the connected pipe, so the
    /// client can tell which process it reached before it writes anything. Throws when Windows
    /// cannot say.
    /// </summary>
    int ReadServerProcessId();

    /// <summary>
    /// Write every byte of <paramref name="buffer"/>. Blocks while the pipe is full; throws once
    /// the server has closed its end.
    /// </summary>
    void Write(byte[] buffer);
}
