using System;
using System.Runtime.InteropServices;

#pragma warning disable 1591 // Missing XML comment for publicly visible type or member

namespace WinMemoryCleaner
{
    /// <summary>  
    /// Structs  
    /// </summary>  
    public static class Structs
    {
        public static class Windows
        {
            /// <summary>  
            /// Memory Combine Information Ex  
            /// </summary>
            [StructLayout(LayoutKind.Sequential, Pack = 1)]
            public struct MemoryCombineInformationEx
            {
                public IntPtr Handle;
                public IntPtr PagesCombined;
                public long Flags;
            }

            /// <summary>
            /// Memory Status EX structure contains information about the current state of both physical and virtual memory, including extended memory
            /// </summary>
            [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
            public class MemoryStatusEx
            {
                public readonly int Length;          // The size of the structure, in bytes.
                public int MemoryLoad;               // A number between 0 and 100 that specifies the approximate percentage of physical memory that is in use.
                public long TotalPhys;               // The amount of actual physical memory, in bytes.
                public long AvailPhys;               // The amount of physical memory currently available, in bytes.
                public long TotalPageFile;           // The current committed memory limit for the system or the current process, whichever is smaller, in bytes.
                public long AvailPageFile;           // The maximum of memory the current process can commit, in bytes.
                public long TotalVirtual;            // The size of the user-mode portion of the virtual address space of the calling process, in bytes.
                public long AvailVirtual;            // The amount of unreserved and uncommitted memory currently available in the user-mode portion of the virtual address space of the calling process, in bytes.
                public long AvailExtendedVirtual;    // Reserved. This value is always 0.

                /// <summary>
                /// Initializes a new instance of the <see cref="MemoryStatusEx" /> class.
                /// </summary>
                public MemoryStatusEx()
                {
                    Length = Marshal.SizeOf(typeof(MemoryStatusEx));
                    MemoryLoad = 0;
                    TotalPhys = 0;
                    AvailPhys = 0;
                    TotalPageFile = 0;
                    AvailPageFile = 0;
                    TotalVirtual = 0;
                    AvailVirtual = 0;
                    AvailExtendedVirtual = 0;
                }
            }

            /// <summary>
            /// Performance Information structure returned by GetPerformanceInfo
            /// </summary>
            [StructLayout(LayoutKind.Sequential)]
            public class PerformanceInformation
            {
                public int Length;                   // The size of the structure, in bytes
                public IntPtr CommitTotal;           // Number of pages currently committed by the system
                public IntPtr CommitLimit;           // Current maximum number of pages that can be committed by the system without extending the paging file(s)
                public IntPtr CommitPeak;            // Maximum number of pages that were simultaneously committed by the system since the last system reboot
                public IntPtr PhysicalTotal;         // Number of physical pages on the system
                public IntPtr PhysicalAvailable;     // Number of physical pages available in the system
                public IntPtr SystemCache;           // Number of physical pages used by the system cache
                public IntPtr KernelTotal;           // Number of physical pages used by the kernel
                public IntPtr KernelPaged;           // Number of physical kernel pages that can be paged to the disk
                public IntPtr KernelNonpaged;        // Number of physical kernel pages that cannot be paged to the disk
                public IntPtr PageSize;              // Size of a page, in bytes
                public int HandleCount;              // Current number of open handles
                public int ProcessCount;             // Current number of processes
                public int ThreadCount;              // Current number of threads

                /// <summary>
                /// Initializes a new instance of the <see cref="PerformanceInformation" /> class.
                /// </summary>
                public PerformanceInformation()
                {
                    Length = Marshal.SizeOf(typeof(PerformanceInformation));
                }
            }

            /// <summary>
            /// System Processor Performance Information, one per logical processor (NtQuerySystemInformation, class 8)
            /// </summary>
            [StructLayout(LayoutKind.Sequential)]
            public struct SystemProcessorPerformanceInformation
            {
                public long IdleTime;                // Amount of time that the system has been idle
                public long KernelTime;              // Amount of time that the system has spent executing kernel code (includes idle)
                public long UserTime;                // Amount of time that the system has spent executing user code
                public long Reserved1a;
                public long Reserved1b;
                public long DpcTime;                 // Amount of time that the system has spent executing deferred procedure calls (DPC)
                public long InterruptTime;           // Amount of time that the system has spent servicing interrupts
                public long Reserved2;
            }

            /// <summary>
            /// PDH formatted counter value (union of large integer / double, followed by the value type)
            /// </summary>
            [StructLayout(LayoutKind.Sequential)]
            public struct PdhCounterValue
            {
                public double DoubleValue;           // The union overlays LARGE_INTEGER and double; reading as double is valid for PDH_FMT_DOUBLE
                public int Type;                     // PDH counter type
            }

            /// <summary>
            /// PDH formatted counter value item, one per instance (PdhGetFormattedCounterArrayW)
            /// </summary>
            [StructLayout(LayoutKind.Sequential)]
            public struct PdhCounterValueItem
            {
                public IntPtr Name;                  // Pointer to the instance name
                public PdhCounterValue Value;        // The formatted value
            }

            /// <summary>
            /// System Power Status structure (GetSystemPowerStatus)
            /// </summary>
            [StructLayout(LayoutKind.Sequential)]
            public class SystemPowerStatus
            {
                public byte AcLineStatus;            // 0 = offline (battery), 1 = online (AC), 255 = unknown
                public byte BatteryFlag;             // Battery status flag
                public byte BatteryLifePercent;      // 0-100 or 255 if unknown
                public byte Reserved1;
                public int BatteryLifeTime;          // Seconds of battery life remaining or -1 if unknown
                public int BatteryFullLifeTime;      // Seconds of battery life when at full charge or -1 if unknown
            }

            /// <summary>  
            /// System File Cache Information structure for x86 working set  
            /// </summary>
            [StructLayout(LayoutKind.Sequential, Pack = 1)]
            public struct SystemFileCacheInformation32
            {
                public int CurrentSize;
                public int PeakSize;
                public int PageFaultCount;
                public int MinimumWorkingSet;
                public int MaximumWorkingSet;
                public int CurrentSizeIncludingTransitionInPages;
                public int PeakSizeIncludingTransitionInPages;
                public int TransitionRePurposeCount;
                public int Flags;
            }

            /// <summary>  
            /// System File Cache Information structure for x64 working set  
            /// </summary>  
            [StructLayout(LayoutKind.Sequential, Pack = 1)]
            public struct SystemFileCacheInformation64
            {
                public long CurrentSize;
                public long PeakSize;
                public long PageFaultCount;
                public long MinimumWorkingSet;
                public long MaximumWorkingSet;
                public long CurrentSizeIncludingTransitionInPages;
                public long PeakSizeIncludingTransitionInPages;
                public long TransitionRePurposeCount;
                public long Flags;
            }

            /// <summary>  
            /// Token Privileges structure, used for adjusting token privileges  
            /// </summary>  
            [StructLayout(LayoutKind.Sequential, Pack = 1)]
            public struct TokenPrivileges
            {
                public int Count;
                public long Luid;
                public int Attr;
            }
        }
    }
}

#pragma warning restore 1591 // Missing XML comment for publicly visible type or member