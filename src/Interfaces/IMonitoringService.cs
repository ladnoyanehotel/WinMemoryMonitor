using System;

#pragma warning disable 1591 // Missing XML comment for publicly visible type or member

namespace WinMemoryCleaner
{
    /// <summary>
    /// Performance monitoring service interface
    /// </summary>
    public interface IMonitoringService
    {
        event Action<SystemMetricsSnapshot> OnSnapshot;

        MetricHistory CpuHistory { get; }
        MetricHistory RamHistory { get; }
        MetricHistory GpuHistory { get; }
        MetricHistory DiskHistory { get; }
        SystemMetricsSnapshot Latest { get; }
    }

    /// <summary>
    /// Memory pressure service interface
    /// </summary>
    public interface IMemoryPressureService
    {
        event Action<Enums.Memory.Pressure> OnLevelChanged;

        Enums.Memory.Pressure Level { get; }
        bool IsHardFaultsElevated { get; }
        int CriticalConditionSeconds { get; }
    }

    /// <summary>
    /// Process monitor service interface
    /// </summary>
    public interface IProcessMonitorService
    {
        event Action<ProcessStatsSnapshot> OnSnapshot;
        event Action<MemoryLeakSuspect> OnLeakSuspected;

        ProcessStatsSnapshot LastSnapshot { get; }
    }

    /// <summary>
    /// System status evaluator interface
    /// </summary>
    public interface ISystemStatusEvaluator
    {
        SystemStatus Current { get; }
        bool IsOptimizing { set; }
        SystemStatus Evaluate(SystemMetricsSnapshot snapshot, Enums.Memory.Pressure? pressureLevel);
    }
}

#pragma warning restore 1591 // Missing XML comment for publicly visible type or member
