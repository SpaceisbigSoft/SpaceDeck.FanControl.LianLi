#if ENABLE_LIGHTING
using System;

namespace FanControl.LianLi.Transport;

/// <summary>
/// One listening socket <see cref="ILoopbackListenerApi.Listen"/> opened.
/// <see cref="IDisposable.Dispose"/> stops it, may be called from another thread while
/// <see cref="Accept"/> waits, which then throws, and never throws itself.
/// </summary>
internal interface ILoopbackListener : IDisposable {
    /// <summary>Wait for the next client and return its connection; throws once the listener is stopped.</summary>
    ILoopbackConnection Accept();
}
#endif
