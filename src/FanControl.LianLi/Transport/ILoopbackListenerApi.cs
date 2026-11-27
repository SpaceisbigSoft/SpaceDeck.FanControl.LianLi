#if ENABLE_LIGHTING
namespace FanControl.LianLi.Transport;

/// <summary>
/// A TCP listener on the loopback address only, for the Lighting build's OpenRGB SDK server. An
/// interface so the server's accept, read and write decisions are tested through a fake, without
/// sockets.
/// </summary>
internal interface ILoopbackListenerApi {
    /// <summary>
    /// Listen on <c>127.0.0.1</c>:<paramref name="port"/>, and on no other address, taking the port
    /// for this process alone. Throws when the port cannot be had (another program has it).
    /// </summary>
    ILoopbackListener Listen(int port);
}
#endif
