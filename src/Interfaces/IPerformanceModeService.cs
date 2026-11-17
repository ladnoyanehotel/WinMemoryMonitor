using System;

#pragma warning disable 1591 // Missing XML comment for publicly visible type or member

namespace WinMemoryCleaner
{
    /// <summary>
    /// Performance Mode service interface
    /// </summary>
    public interface IPerformanceModeService
    {
        event Action<Enums.PerformanceMode.State> OnChanged;

        Enums.PerformanceMode.State State { get; }

        void Enable();
        void Disable();
        void ShutdownRestore();
    }
}

#pragma warning restore 1591 // Missing XML comment for publicly visible type or member
