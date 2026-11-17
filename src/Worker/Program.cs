using System;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;

namespace WinMemoryCleaner.Worker
{
    /// <summary>
    /// WinMemoryCleaner worker process. Performs exactly one native memory operation and exits.
    /// Isolated from the UI process so a hanging native call can never freeze the interface:
    /// the parent enforces a timeout and kills this process when it is exceeded.
    ///
    /// Usage: WinMemoryCleaner.Worker.exe --operation low-priority-standby|standby
    /// Exit codes: 0 = success, 1 = usage error, 2 = privilege failure, 3 = native call failure.
    /// The hidden --hang flag blocks forever; it exists to test the parent timeout path.
    /// </summary>
    internal static class Program
    {
        private const int ExitSuccess = 0;
        private const int ExitUsage = 1;
        private const int ExitPrivilege = 2;
        private const int ExitNativeFailure = 3;

        private const int SystemMemoryListInformation = 80; // 0x50
        private const int MemoryPurgeStandbyList = 4;
        private const int MemoryPurgeLowPriorityStandbyList = 5;
        private const int PrivilegeEnabled = 2; // SE_PRIVILEGE_ENABLED
        private const string SeProfileSingleProcessPrivilege = "SeProfileSingleProcessPrivilege";

        internal static int Main(string[] args)
        {
            try
            {
                var operation = string.Empty;
                var hang = false;

                for (var i = 0; i < args.Length; i++)
                {
                    if (string.Equals(args[i], "--operation", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                    {
                        i++;
                        operation = args[i];
                    }
                    else if (string.Equals(args[i], "--hang", StringComparison.OrdinalIgnoreCase))
                    {
                        hang = true;
                    }
                }

                if (hang)
                {
                    // Test hook: intentionally block so the parent must enforce its timeout
                    Console.WriteLine("HANG");
                    Thread.Sleep(Timeout.Infinite);
                    return ExitSuccess;
                }

                int command;

                if (string.Equals(operation, "low-priority-standby", StringComparison.OrdinalIgnoreCase))
                {
                    command = MemoryPurgeLowPriorityStandbyList;
                }
                else if (string.Equals(operation, "standby", StringComparison.OrdinalIgnoreCase))
                {
                    command = MemoryPurgeStandbyList;
                }
                else
                {
                    Console.WriteLine("USAGE: WinMemoryCleaner.Worker.exe --operation low-priority-standby|standby");
                    return ExitUsage;
                }

                if (!EnablePrivilege(SeProfileSingleProcessPrivilege))
                {
                    Console.WriteLine("PRIVILEGE_FAILED");
                    return ExitPrivilege;
                }

                var buffer = Marshal.AllocHGlobal(sizeof(int));

                try
                {
                    Marshal.WriteInt32(buffer, command);

                    var status = NtSetSystemInformation(SystemMemoryListInformation, buffer, sizeof(int));

                    if (status != 0)
                    {
                        Console.WriteLine("NT_STATUS_0x" + status.ToString("X8", System.Globalization.CultureInfo.InvariantCulture));
                        return ExitNativeFailure;
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }

                Console.WriteLine("OK");
                return ExitSuccess;
            }
            catch (Exception e)
            {
                Console.WriteLine("EXCEPTION " + e.GetType().Name + ": " + e.Message);
                return ExitNativeFailure;
            }
        }

        private static bool EnablePrivilege(string privilegeName)
        {
            var result = false;

            try
            {
                using (var current = WindowsIdentity.GetCurrent(TokenAccessLevels.Query | TokenAccessLevels.AdjustPrivileges))
                {
                    long luid = 0;

                    if (LookupPrivilegeValue(null, privilegeName, ref luid))
                    {
                        var newState = new TokenPrivileges
                        {
                            Count = 1,
                            Luid = luid,
                            Attr = PrivilegeEnabled
                        };

                        result = AdjustTokenPrivileges(current.Token, false, ref newState, 0, IntPtr.Zero, IntPtr.Zero);
                    }
                }
            }
            catch
            {
                result = false;
            }

            return result;
        }

        [DllImport("ntdll.dll")]
        private static extern int NtSetSystemInformation(int systemInformationClass, IntPtr systemInformation, uint systemInformationLength);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool LookupPrivilegeValue(string systemName, string name, ref long luid);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AdjustTokenPrivileges(IntPtr tokenHandle, bool disableAllPrivileges, ref TokenPrivileges newState, int bufferLength, IntPtr previousState, IntPtr returnLength);

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct TokenPrivileges
        {
            public int Count;
            public long Luid;
            public int Attr;
        }
    }
}
