#if ENABLE_LIGHTING
using System;

namespace FanControl.LianLi.Transport;

/// <summary>
/// One client an <see cref="ILoopbackListener"/> accepted. <see cref="IDisposable.Dispose"/> closes it,
/// may be called from another thread while <see cref="Read"/> or <see cref="Write"/> waits, which
/// then throws or returns 0, and never throws itself.
/// </summary>
internal interface ILoopbackConnection : IDisposable {
    /// <summary>
    /// Read up to <paramref name="count"/> bytes into <paramref name="buffer"/> at
    /// <paramref name="offset"/>, waiting at most <paramref name="timeoutMilliseconds"/> for at least
    /// one (<see cref="System.Threading.Timeout.Infinite"/> for as long as it takes). Returns how many
    /// were read, or 0 once the client has closed its end; throws <see cref="TimeoutException"/> when
    /// nothing came in time.
    /// </summary>
    int Read(byte[] buffer, int offset, int count, int timeoutMilliseconds);

    /// <summary>
    /// Write every byte of <paramref name="buffer"/>, waiting at most
    /// <paramref name="timeoutMilliseconds"/> for a client that does not read; throws when it cannot.
    /// </summary>
    void Write(byte[] buffer, int timeoutMilliseconds);
}
#endif
