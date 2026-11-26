using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace FanControl.LianLi.Transport;

/// <summary>
/// <see cref="INamedPipeApi"/> over <see cref="NamedPipeClientStream"/> and <c>kernel32</c>'s
/// <c>GetNamedPipeServerProcessId</c>.
/// </summary>
// Excluded from coverage: nothing here but the pipe stream and the P/Invoke call themselves. Every
// decision made on their results is SpaceDeckPublisher's, unit-tested through a fake.
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

    private static class NativeMethods {
        [DllImport("kernel32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint serverProcessId);
    }
}
