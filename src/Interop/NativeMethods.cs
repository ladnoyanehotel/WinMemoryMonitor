using Microsoft.Win32.SafeHandles;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;

namespace WinMemoryCleaner
{
    /// <summary>
    /// Windows API
    /// </summary>
    internal static class NativeMethods
    {
        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool AdjustTokenPrivileges(IntPtr tokenHandle, [MarshalAs(UnmanagedType.Bool)] bool disableAllPrivileges, ref Structs.Windows.TokenPrivileges newState, int bufferLength, IntPtr previousState, IntPtr returnLength);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool AllowSetForegroundWindow(int dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool AttachConsole(int dwProcessId);

        [SuppressUnmanagedCodeSecurity]
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        internal static extern SafeFileHandle CreateFile([MarshalAs(UnmanagedType.LPWStr)] string lpFileName, FileAccess dwDesiredAccess, FileShare dwShareMode, IntPtr lpSecurityAttributes, FileMode dwCreationDisposition, int dwFlagsAndAttributes, IntPtr hTemplateFile);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool DestroyIcon(IntPtr hIcon);

        [SuppressUnmanagedCodeSecurity]
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool DeviceIoControl(SafeFileHandle hDevice, int dwIoControlCode, IntPtr lpInBuffer, int nInBufferSize, IntPtr lpOutBuffer, int nOutBufferSize, out int lpBytesReturned, IntPtr lpOverlapped);

        [DllImport("dwmapi.dll", SetLastError = true)]
        internal static extern void DwmSetWindowAttribute(IntPtr hWnd, int attribute, ref int value, int size);

        [SuppressUnmanagedCodeSecurity]
        [DllImport("psapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool EmptyWorkingSet(IntPtr hProcess);

        [DllImport("user32", SetLastError = true, CharSet = CharSet.Unicode)]
        internal static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [SuppressUnmanagedCodeSecurity]
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool FlushFileBuffers(SafeFileHandle hFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern IntPtr GetStdHandle(int nStdHandle);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern IntPtr LocalFree(IntPtr hMem);

        [SuppressUnmanagedCodeSecurity]
        [DllImport("psapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetPerformanceInfo([In, Out] Structs.Windows.PerformanceInformation performanceInformation, int size);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out int lpdwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GlobalMemoryStatusEx([In, Out] Structs.Windows.MemoryStatusEx lpBuffer);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool LookupPrivilegeValue(string lpSystemName, string lpName, ref long lpLuid);

        [SuppressUnmanagedCodeSecurity]
        [DllImport("ntdll.dll", SetLastError = true)]
        internal static extern int NtSetSystemInformation(int SystemInformationClass, IntPtr SystemInformation, uint SystemInformationLength);

        [SuppressUnmanagedCodeSecurity]
        [DllImport("ntdll.dll", SetLastError = true)]
        internal static extern int NtQuerySystemInformation(int systemInformationClass, IntPtr systemInformation, uint systemInformationLength, out uint returnLength);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [SuppressUnmanagedCodeSecurity]
        [DllImport("powrprof.dll", SetLastError = true)]
        internal static extern int PowerGetActiveScheme(IntPtr rootPowerKey, out IntPtr activePolicyGuid);

        [SuppressUnmanagedCodeSecurity]
        [DllImport("powrprof.dll", SetLastError = true)]
        internal static extern int PowerSetActiveScheme(IntPtr rootPowerKey, ref Guid policyGuid);

        [SuppressUnmanagedCodeSecurity]
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetSystemPowerStatus([In, Out] Structs.Windows.SystemPowerStatus powerStatus);

        [SuppressUnmanagedCodeSecurity]
        [DllImport("pdh.dll", SetLastError = true)]
        internal static extern int PdhOpenQuery(IntPtr dataSource, IntPtr userData, out IntPtr queryHandle);

        [SuppressUnmanagedCodeSecurity]
        [DllImport("pdh.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        internal static extern int PdhAddEnglishCounterW(IntPtr queryHandle, string counterPath, IntPtr userData, out IntPtr counterHandle);

        [SuppressUnmanagedCodeSecurity]
        [DllImport("pdh.dll", SetLastError = true)]
        internal static extern int PdhCollectQueryData(IntPtr queryHandle);

        [SuppressUnmanagedCodeSecurity]
        [DllImport("pdh.dll", SetLastError = true)]
        internal static extern int PdhGetFormattedCounterValue(IntPtr counterHandle, uint format, out IntPtr counterType, ref Structs.Windows.PdhCounterValue value);

        [SuppressUnmanagedCodeSecurity]
        [DllImport("pdh.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        internal static extern int PdhGetFormattedCounterArrayW(IntPtr counterHandle, uint format, ref uint bufferSize, out uint itemCount, IntPtr itemBuffer);

        [SuppressUnmanagedCodeSecurity]
        [DllImport("pdh.dll", SetLastError = true)]
        internal static extern int PdhRemoveCounter(IntPtr counterHandle);

        [SuppressUnmanagedCodeSecurity]
        [DllImport("pdh.dll", SetLastError = true)]
        internal static extern int PdhCloseQuery(IntPtr queryHandle);

        [SuppressUnmanagedCodeSecurity]
        [DllImport("pdh.dll", SetLastError = true)]
        internal static extern int PdhFreeMemory(IntPtr buffer);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetSystemFileCacheSize(IntPtr minimumFileCacheSize, IntPtr maximumFileCacheSize, int flags);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    }
}
