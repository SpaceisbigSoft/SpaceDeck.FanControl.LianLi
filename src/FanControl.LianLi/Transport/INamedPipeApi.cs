namespace FanControl.LianLi.Transport;

/// <summary>
/// The client end of a local named pipe, for the one pipe the plugin writes to: the SpaceDeck
/// collector's (see <c>SPACEDECK.md</c>). An interface so the publisher's connect, check and write
/// decisions are tested through a fake, without Windows or a server.
/// </summary>
internal interface INamedPipeApi {
    /// <summary>
    /// Connect to the local pipe <paramref name="pipeName"/> as a write-only client that lets the
    /// server identify it but not act as it (the identification impersonation level), waiting at
    /// most <paramref name="timeoutMilliseconds"/>. Returns null when no instance of the pipe took
    /// the connection in that time: no server, or every instance busy. Any other failure throws.
    /// </summary>
    INamedPipeConnection? TryConnect(string pipeName, int timeoutMilliseconds);
}
