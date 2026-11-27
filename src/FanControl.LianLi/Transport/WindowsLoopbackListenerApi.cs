#if ENABLE_LIGHTING
using System;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace FanControl.LianLi.Transport;

/// <summary>
/// <see cref="ILoopbackListenerApi"/> over <see cref="TcpListener"/> and <see cref="Socket"/>.
/// </summary>
// Excluded from coverage: nothing here but the sockets themselves. Every decision made on their
// results is OpenRgbServer's, unit-tested through a fake.
[ExcludeFromCodeCoverage]
internal sealed class WindowsLoopbackListenerApi : ILoopbackListenerApi {
    // A connection is accepted at once or refused by the server (it takes at most four), so a short
    // queue of pending ones is enough.
    private const int Backlog = 4;

    /// <summary>The single instance; it holds no state.</summary>
    public static readonly WindowsLoopbackListenerApi Instance = new WindowsLoopbackListenerApi();

    private WindowsLoopbackListenerApi() {
    }

    public ILoopbackListener Listen(int port) {
        // The loopback address only: nothing off this PC can reach the server. ExclusiveAddressUse keeps
        // another program from binding the same port beside it and taking its clients.
        var listener = new TcpListener(IPAddress.Loopback, port) { ExclusiveAddressUse = true };
        listener.Start(Backlog);
        return new Listener(listener);
    }

    private sealed class Listener : ILoopbackListener {
        private readonly TcpListener _listener;

        public Listener(TcpListener listener) => _listener = listener;

        public ILoopbackConnection Accept() => new Connection(_listener.AcceptSocket());

        public void Dispose() => _listener.Stop();
    }

    private sealed class Connection : ILoopbackConnection {
        private readonly Socket _socket;

        public Connection(Socket socket) {
            _socket = socket;
            _socket.NoDelay = true;
        }

        public int Read(byte[] buffer, int offset, int count, int timeoutMilliseconds) {
            // 0 is the socket's own "no timeout".
            _socket.ReceiveTimeout = timeoutMilliseconds == Timeout.Infinite ? 0 : timeoutMilliseconds;
            try {
                return _socket.Receive(buffer, offset, count, SocketFlags.None);
            } catch (SocketException ex) when (ex.SocketErrorCode == SocketError.TimedOut) {
                throw new TimeoutException("the client sent nothing for " + timeoutMilliseconds + " ms", ex);
            }
        }

        public void Write(byte[] buffer, int timeoutMilliseconds) {
            _socket.SendTimeout = timeoutMilliseconds;
            int sent = 0;
            while (sent < buffer.Length) {
                sent += _socket.Send(buffer, sent, buffer.Length - sent, SocketFlags.None);
            }
        }

        public void Dispose() => _socket.Close();
    }
}
#endif
