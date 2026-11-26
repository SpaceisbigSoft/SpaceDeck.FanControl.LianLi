using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace FanControl.LianLi.Transport;

/// <summary>
/// <see cref="IServiceControlApi"/> over <c>advapi32</c>'s service control manager calls.
/// </summary>
// Excluded from coverage: nothing here but the P/Invoke calls themselves. Every decision made on
// their result is SpaceDeckPublisher's, unit-tested through a fake.
[ExcludeFromCodeCoverage]
internal sealed class WindowsServiceControlApi : IServiceControlApi {
    // SC_MANAGER_CONNECT and SERVICE_QUERY_STATUS: the least the status query needs.
    private const uint ManagerConnect = 0x0001;
    private const uint ServiceQueryStatus = 0x0004;

    // SC_STATUS_PROCESS_INFO, which fills a SERVICE_STATUS_PROCESS: nine DWORDs, the current state
    // the second and the process id the eighth.
    private const int StatusProcessInfo = 0;
    private const int StatusWords = 9;
    private const int CurrentStateWord = 1;
    private const int ProcessIdWord = 7;

    // SERVICE_RUNNING, and ERROR_SERVICE_DOES_NOT_EXIST from OpenService.
    private const uint Running = 4;
    private const int ServiceDoesNotExist = 1060;

    /// <summary>The single instance; it holds no state.</summary>
    public static readonly WindowsServiceControlApi Instance = new WindowsServiceControlApi();

    private WindowsServiceControlApi() {
    }

    public int? QueryProcessId(string serviceName) {
        IntPtr manager = NativeMethods.OpenSCManagerW(null, null, ManagerConnect);
        if (manager == IntPtr.Zero) {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        try {
            IntPtr service = NativeMethods.OpenServiceW(manager, serviceName, ServiceQueryStatus);
            if (service == IntPtr.Zero) {
                int error = Marshal.GetLastWin32Error();
                if (error == ServiceDoesNotExist) {
                    return null;
                }

                throw new Win32Exception(error);
            }

            try {
                var status = new uint[StatusWords];
                if (!NativeMethods.QueryServiceStatusEx(service, StatusProcessInfo, status, StatusWords * sizeof(uint), out _)) {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }

                return status[CurrentStateWord] == Running ? unchecked((int)status[ProcessIdWord]) : 0;
            } finally {
                _ = NativeMethods.CloseServiceHandle(service);
            }
        } finally {
            _ = NativeMethods.CloseServiceHandle(manager);
        }
    }

    private static class NativeMethods {
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern IntPtr OpenSCManagerW(string? machineName, string? databaseName, uint desiredAccess);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern IntPtr OpenServiceW(IntPtr manager, string serviceName, uint desiredAccess);

        [DllImport("advapi32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool QueryServiceStatusEx(IntPtr service, int infoLevel, [Out] uint[] buffer, int bufferSize, out int bytesNeeded);

        [DllImport("advapi32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CloseServiceHandle(IntPtr handle);
    }
}
