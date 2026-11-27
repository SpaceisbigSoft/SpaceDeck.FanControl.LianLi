namespace FanControl.LianLi.Transport;

/// <summary>
/// The client end of a local named pipe, for the two pipes of the SpaceDeck collector (see
/// <c>SPACEDECK.md</c>): the one the plugin writes its readings to, and the one it reads screen
/// frames from. An interface so the publisher's and the screen client's connect, check, write and
/// read decisions are tested through a fake, without Windows or a server.
/// </summary>
internal interface INamedPipeApi {
    /// <summary>
    /// Connect to the local pipe <paramref name="pipeName"/> as a write-only client that lets the
    /// server identify it but not act as it (the identification impersonation level), waiting at
    /// most <paramref name="timeoutMilliseconds"/>. Returns null when no instance of the pipe took
    /// the connection in that time: no server, or every instance busy. Any other failure throws.
    /// </summary>
    INamedPipeConnection? TryConnect(string pipeName, int timeoutMilliseconds);

    /// <summary>
    /// Connect to the local pipe <paramref name="pipeName"/> as a read-only client at the
    /// identification impersonation level, waiting at most <paramref name="timeoutMilliseconds"/>.
    /// Returns null when no instance of the pipe took the connection in that time. Any other failure
    /// throws.
    /// </summary>
    INamedPipeReader? TryConnectForReading(string pipeName, int timeoutMilliseconds);
}
