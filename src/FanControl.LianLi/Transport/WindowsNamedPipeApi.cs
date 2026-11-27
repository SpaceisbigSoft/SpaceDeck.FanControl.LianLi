using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace FanControl.LianLi.Transport;

/// <summary>
/// <see cref="INamedPipeApi"/> over <see cref="NamedPipeClientStream"/>, write-only for the readings and
/// read-only for the screen frames, and <c>kernel32</c>'s
/// <c>GetNamedPipeServerProcessId</c>.
/// </summary>
// Excluded from coverage: nothing here but the pipe stream and the P/Invoke call themselves. Every
// decision made on their results is SpaceDeckPublisher's or SpaceDeckScreenClient's, unit-tested through a fake.
[ExcludeFromCodeCoverage]
internal sealed class WindowsNamedPipeApi : INamedPipeApi {
    /// <summary>The single instance; it holds no state.</summary>
    public static readonly WindowsNamedPipeApi Instance = new WindowsNamedPipeApi();

    private WindowsNamedPipeApi() {
    }

    public INamedPipeConnection? TryConnect(string pipeName, int timeoutMilliseconds) {
        var connection = new Connection(pipeName);
        try {
            connection.Connect(timeoutMilliseconds);
            return connection;
        } catch (TimeoutException) {
            // No instance of the pipe took the connection in time: nothing is listening, or every
            // instance is busy. Not a failure, just nobody there yet.
            connection.Dispose();
            return null;
        } catch {
            connection.Dispose();
            throw;
        }
    }

    public INamedPipeReader? TryConnectForReading(string pipeName, int timeoutMilliseconds) {
        var reader = new Reader(pipeName);
        try {
            reader.Connect(timeoutMilliseconds);
            return reader;
        } catch (TimeoutException) {
            // As for a write-only connection: nobody there yet.
            reader.Dispose();
            return null;
        } catch {
            reader.Dispose();
            throw;
        }
    }

    private sealed class Connection : INamedPipeConnection {
        private readonly NamedPipeClientStream _stream;

        // Write-only, synchronous, and at the identification level: the server may learn who the
        // client is but never act as it.
        public Connection(string pipeName)
            => _stream = new NamedPipeClientStream(".", pipeName, PipeDirection.Out, PipeOptions.None, TokenImpersonationLevel.Identification);

        public void Connect(int timeoutMilliseconds) => _stream.Connect(timeoutMilliseconds);

        public int ReadServerProcessId() {
            if (!NativeMethods.GetNamedPipeServerProcessId(_stream.SafePipeHandle, out uint processId)) {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            return unchecked((int)processId);
        }

        // No Flush: on a pipe it waits until the server has read everything, which is exactly the
        // wait a write must not depend on.
        public void Write(byte[] buffer) => _stream.Write(buffer, 0, buffer.Length);

        public void Dispose() => _stream.Dispose();
    }

    private sealed class Reader : INamedPipeReader {
        private readonly NamedPipeClientStream _stream;

        // Read-only and at the identification level, as the write-only connection. Overlapped, so a
        // read waiting for the next frame ends when another thread closes the handle (closing a handle
        // cancels its pending overlapped I/O), which is how the client is stopped.
        public Reader(string pipeName)
            => _stream = new NamedPipeClientStream(".", pipeName, PipeDirection.In, PipeOptions.Asynchronous, TokenImpersonationLevel.Identification);

        public void Connect(int timeoutMilliseconds) => _stream.Connect(timeoutMilliseconds);

        public int ReadServerProcessId() {
            if (!NativeMethods.GetNamedPipeServerProcessId(_stream.SafePipeHandle, out uint processId)) {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            return unchecked((int)processId);
        }

        public int Read(byte[] buffer, int offset, int count) => _stream.Read(buffer, offset, count);

        public void Dispose() => _stream.Dispose();
    }

    private static class NativeMethods {
        [DllImport("kernel32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint serverProcessId);
    }
}
