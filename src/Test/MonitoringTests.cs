using System;
using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;

#pragma warning disable 1591 // Missing XML comment for publicly visible type or member

namespace WinMemoryCleaner.Test
{
    /// <summary>
    /// Fakes for the monitoring pipeline
    /// </summary>
    public class FakeMonitoringService : IMonitoringService
    {
        public MetricHistory CpuHistory { get { return new MetricHistory(60); } }
        public MetricHistory RamHistory { get { return new MetricHistory(60); } }
        public MetricHistory GpuHistory { get { return new MetricHistory(60); } }
        public MetricHistory DiskHistory { get { return new MetricHistory(60); } }
        public SystemMetricsSnapshot Latest { get; private set; }

        public event Action<SystemMetricsSnapshot> OnSnapshot;

        public void Push(SystemMetricsSnapshot snapshot)
        {
            Latest = snapshot;

            var handler = OnSnapshot;

            if (handler != null)
                handler(snapshot);
        }

        public static SystemMetricsSnapshot CreateSnapshot(double availableGb, double totalGb, double commitPercent)
        {
            return new SystemMetricsSnapshot
            {
                RamTotalBytes = (long)(totalGb * 1073741824.0),
                RamAvailableBytes = (long)(availableGb * 1073741824.0),
                RamUsedBytes = (long)((totalGb - availableGb) * 1073741824.0),
                CommitTotalBytes = (long)(commitPercent * 1073741824.0),
                CommitLimitBytes = (long)(100.0 * 1073741824.0),
                CpuValid = true,
                CpuUsage = 10.0
            };
        }
    }

#pragma warning disable 0067 // The event is never raised by this fake
    public class FakeProcessMonitorService : IProcessMonitorService
    {
        public event Action<ProcessStatsSnapshot> OnSnapshot;
        public event Action<MemoryLeakSuspect> OnLeakSuspected;
#pragma warning restore 0067

        public ProcessStatsSnapshot LastSnapshot
        {
            get
            {
                return new ProcessStatsSnapshot
                {
                    Valid = true,
                    TopByCpu = new List<ProcessStats> { new ProcessStats { Name = "test.exe", CpuPercent = 12.0, PrivateBytes = 536870912 } },
                    TopByMemory = new List<ProcessStats> { new ProcessStats { Name = "test.exe", CpuPercent = 12.0, PrivateBytes = 536870912 } }
                };
            }
        }
    }

    public class FakeWorkerRunner : IWorkerProcessRunner
    {
        public event Action<string, WorkerResult> OnCompleted;

        public int RunCount;
        public TimeSpan Delay = TimeSpan.Zero;

        public WorkerResult Run(string operation)
        {
            RunCount++;

            if (Delay > TimeSpan.Zero)
                Thread.Sleep(Delay);

            var result = WorkerResult.FromSuccess(0, 1234, "OK", Delay);

            var handler = OnCompleted;

            if (handler != null)
                handler(operation, result);

            return result;
        }
    }

    /// <summary>
    /// Metric history ring buffer tests
    /// </summary>
    [TestFixture]
    public class MetricHistoryTests
    {
        [Test]
        public void Add_BeyondCapacity_KeepsMostRecent()
        {
            var history = new MetricHistory(5);

            for (var i = 1; i <= 8; i++)
                history.Add(i);

            Assert.AreEqual(5, history.Count);
            Assert.AreEqual(8f, history.Last);

            var buffer = new float[5];
            var copied = history.CopyTo(buffer, 0);

            Assert.AreEqual(5, copied);
            Assert.AreEqual(4f, buffer[0]);
            Assert.AreEqual(8f, buffer[4]);
        }

        [Test]
        public void CopyTo_ReturnsChronologicalOrder()
        {
            var history = new MetricHistory(3);

            history.Add(10);
            history.Add(20);
            history.Add(30);

            var buffer = new float[3];
            history.CopyTo(buffer, 0);

            Assert.AreEqual(10f, buffer[0]);
            Assert.AreEqual(20f, buffer[1]);
            Assert.AreEqual(30f, buffer[2]);
        }
    }

    /// <summary>
    /// Memory pressure sliding window tests
    /// </summary>
    [TestFixture]
    public class MemoryPressureServiceTests
    {
        [Test]
        public void NormalMemory_StaysNormal()
        {
            var monitoring = new FakeMonitoringService();

            using (var service = new MemoryPressureService(monitoring, 10))
            {
                for (var i = 0; i < 15; i++)
                    monitoring.Push(FakeMonitoringService.CreateSnapshot(8, 16, 50));

                Assert.AreEqual(Enums.Memory.Pressure.Normal, service.Level);
            }
        }

        [Test]
        public void ShortSpike_DoesNotTriggerWarning()
        {
            var monitoring = new FakeMonitoringService();

            using (var service = new MemoryPressureService(monitoring, 10))
            {
                for (var i = 0; i < 5; i++)
                    monitoring.Push(FakeMonitoringService.CreateSnapshot(8, 16, 50));

                // 3-second spike of commit pressure, then back to normal: must NOT warn
                for (var i = 0; i < 3; i++)
                    monitoring.Push(FakeMonitoringService.CreateSnapshot(8, 16, 90));

                for (var i = 0; i < 5; i++)
                    monitoring.Push(FakeMonitoringService.CreateSnapshot(8, 16, 50));

                Assert.AreEqual(Enums.Memory.Pressure.Normal, service.Level);
            }
        }

        [Test]
        public void SustainedLowMemory_TriggersWarning()
        {
            var monitoring = new FakeMonitoringService();

            using (var service = new MemoryPressureService(monitoring, 10))
            {
                for (var i = 0; i < 12; i++)
                    monitoring.Push(FakeMonitoringService.CreateSnapshot(1.0, 16, 50)); // ~6% available

                Assert.AreEqual(Enums.Memory.Pressure.Warning, service.Level);
            }
        }

        [Test]
        public void SustainedCriticalPressure_TriggersCritical()
        {
            var monitoring = new FakeMonitoringService();

            using (var service = new MemoryPressureService(monitoring, 10))
            {
                // Critical: available < max(1 GB, 8%) AND commit >= 90%, sustained 10 samples
                for (var i = 0; i < 12; i++)
                    monitoring.Push(FakeMonitoringService.CreateSnapshot(0.5, 16, 95));

                Assert.AreEqual(Enums.Memory.Pressure.Critical, service.Level);
            }
        }

        [Test]
        public void HighCommitWithoutLowRam_StaysWarning()
        {
            var monitoring = new FakeMonitoringService();

            using (var service = new MemoryPressureService(monitoring, 10))
            {
                // Commit >= 90% but plenty of available RAM: warning (commit), not critical
                for (var i = 0; i < 12; i++)
                    monitoring.Push(FakeMonitoringService.CreateSnapshot(6.0, 16, 92));

                Assert.AreEqual(Enums.Memory.Pressure.Warning, service.Level);
            }
        }
    }

    /// <summary>
    /// System status evaluator threshold tests
    /// </summary>
    [TestFixture]
    public class SystemStatusEvaluatorTests
    {
        private SystemStatusEvaluator CreateEvaluator()
        {
            return new SystemStatusEvaluator(new FakeMonitoringService(), new FakeProcessMonitorService(), null);
        }

        [Test]
        public void NormalLoad_ReportsNormal()
        {
            using (var evaluator = CreateEvaluator())
            {
                var status = evaluator.Evaluate(FakeMonitoringService.CreateSnapshot(8, 16, 50), null);

                Assert.AreEqual(Enums.Status.Level.Normal, status.Level);
            }
        }

        [Test]
        public void ShortCpuSpike_ReportsNormal()
        {
            using (var evaluator = CreateEvaluator())
            {
                var snapshot = FakeMonitoringService.CreateSnapshot(8, 16, 50);
                snapshot.CpuUsage = 90;

                evaluator.Evaluate(snapshot, null); // 1 second of 90% CPU is not a warning yet

                Assert.AreEqual(Enums.Status.Level.Normal, evaluator.Evaluate(snapshot, null).Level);
            }
        }

        [Test]
        public void SustainedCpuLoad_ReportsWarning()
        {
            using (var evaluator = CreateEvaluator())
            {
                var snapshot = FakeMonitoringService.CreateSnapshot(8, 16, 50);
                snapshot.CpuUsage = 90;
                snapshot.DiskValid = false;

                SystemStatus status = null;

                for (var i = 0; i < 12; i++)
                    status = evaluator.Evaluate(snapshot, null);

                Assert.AreEqual(Enums.Status.Level.Warning, status.Level);
                Assert.AreEqual(Enums.Status.Reason.CpuLoad, status.Reason);
            }
        }

        [Test]
        public void CriticalMemoryPressure_TakesPriorityOverCpuWarning()
        {
            using (var evaluator = CreateEvaluator())
            {
                var snapshot = FakeMonitoringService.CreateSnapshot(8, 16, 50);
                snapshot.CpuUsage = 90;

                for (var i = 0; i < 12; i++)
                    evaluator.Evaluate(snapshot, null);

                var status = evaluator.Evaluate(snapshot, Enums.Memory.Pressure.Critical);

                Assert.AreEqual(Enums.Status.Level.Critical, status.Level);
                Assert.AreEqual(Enums.Status.Reason.MemoryPressure, status.Reason);
            }
        }

        [Test]
        public void OptimizingOverride_Wins()
        {
            using (var evaluator = CreateEvaluator())
            {
                evaluator.IsOptimizing = true;

                var status = evaluator.Evaluate(FakeMonitoringService.CreateSnapshot(8, 16, 50), null);

                Assert.AreEqual(Enums.Status.Level.Optimizing, status.Level);
            }
        }
    }

    /// <summary>
    /// Smart optimizer atomic gate tests (rapid-click / concurrent invocation protection)
    /// </summary>
    [TestFixture]
    public class SmartMemoryOptimizerTests
    {
        [Test]
        public void ConcurrentCleanups_OnlyOneRuns()
        {
            var monitoring = new FakeMonitoringService();
            var worker = new FakeWorkerRunner { Delay = TimeSpan.FromMilliseconds(1500) };

            using (var scheduler = new SamplingScheduler())
            using (var pressure = new MemoryPressureService(monitoring, 10))
            using (var optimizer = new SmartMemoryOptimizer(worker, monitoring, pressure, new FakeProcessMonitorService(), scheduler))
            {
                optimizer.ForceCleanupRequired = true; // Deterministic: the full worker path runs

                var nonNull = 0;
                var threads = new List<Thread>();

                for (var i = 0; i < 10; i++)
                {
                    var thread = new Thread(delegate()
                    {
                        var result = optimizer.Cleanup(Enums.Memory.Optimization.Reason.Manual);

                        if (result != null)
                            Interlocked.Increment(ref nonNull);
                    });

                    threads.Add(thread);
                }

                foreach (var thread in threads)
                    thread.Start();

                foreach (var thread in threads)
                    thread.Join(20000);

                Assert.AreEqual(1, nonNull, "Exactly one concurrent cleanup must run; the rest must be ignored");
                Assert.AreEqual(1, worker.RunCount, "The worker must be invoked exactly once");
                Assert.IsFalse(optimizer.IsRunning, "The gate must be released after the cleanup");

                // After the gate is released the next cleanup can run again
                var next = optimizer.Cleanup(Enums.Memory.Optimization.Reason.Manual);
                Assert.IsNotNull(next, "The next cleanup must not be blocked by a stuck gate");
            }
        }

        [Test]
        public void HealthyMemory_CleanupNotNeeded()
        {
            var monitoring = new FakeMonitoringService();
            var worker = new FakeWorkerRunner();

            using (var scheduler = new SamplingScheduler())
            using (var pressure = new MemoryPressureService(monitoring, 10))
            using (var optimizer = new SmartMemoryOptimizer(worker, monitoring, pressure, new FakeProcessMonitorService(), scheduler))
            {
                monitoring.Push(FakeMonitoringService.CreateSnapshot(8, 16, 50));

                var result = optimizer.Cleanup(Enums.Memory.Optimization.Reason.Manual);

                Assert.IsNotNull(result);
                Assert.AreEqual(CleanupOutcome.NotNeeded, result.Outcome);
                Assert.AreEqual(0, worker.RunCount, "No native operation may run when RAM is fine");
            }
        }

        [Test]
        public void IsRunning_FalseAfterDispose()
        {
            var monitoring = new FakeMonitoringService();
            var worker = new FakeWorkerRunner();

            var scheduler = new SamplingScheduler();
            var pressure = new MemoryPressureService(monitoring, 10);
            var optimizer = new SmartMemoryOptimizer(worker, monitoring, pressure, new FakeProcessMonitorService(), scheduler);

            optimizer.Dispose();
            pressure.Dispose();
            scheduler.Dispose();

            Assert.IsFalse(optimizer.IsRunning);
        }

        [Test]
        public void HundredSequentialCleanups_GateNeverStuck()
        {
            var monitoring = new FakeMonitoringService();
            var worker = new FakeWorkerRunner();

            using (var scheduler = new SamplingScheduler())
            using (var pressure = new MemoryPressureService(monitoring, 10))
            using (var optimizer = new SmartMemoryOptimizer(worker, monitoring, pressure, new FakeProcessMonitorService(), scheduler))
            {
                optimizer.ForceCleanupRequired = true;

                for (var i = 0; i < 100; i++)
                {
                    var result = optimizer.Cleanup(Enums.Memory.Optimization.Reason.Manual);

                    Assert.IsNotNull(result, "Sequential cleanup #" + i + " must never be ignored");
                    Assert.IsFalse(optimizer.IsRunning, "Gate must be free after cleanup #" + i);
                }

                Assert.AreEqual(100, worker.RunCount);
            }
        }
    }

    /// <summary>
    /// Worker process isolation tests: the real WinMemoryCleaner.Worker.exe is launched
    /// from the test output directory
    /// </summary>
    [TestFixture]
    public class WorkerProcessRunnerTests
    {
        [Test]
        public void MissingWorker_ReportsFailure()
        {
            using (var runner = new WorkerProcessRunner())
            {
                var result = runner.Run(WorkerOperation.Standby);

                Assert.IsNotNull(result);
                Assert.IsFalse(result.Success);
                Assert.IsFalse(result.TimedOut);
            }
        }

        [Test]
        [Timeout(60000)]
        public void HangingWorker_IsKilledAfterTimeout()
        {
            // The hidden --hang flag blocks forever; the runner must enforce its 12 s timeout,
            // kill the worker and keep functioning (the UI process is never blocked).
            if (!System.IO.File.Exists(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WinMemoryCleaner.Worker.exe")))
                Assert.Ignore("WinMemoryCleaner.Worker.exe is not next to the test assembly");

            using (var runner = new WorkerProcessRunner())
            {
                var started = DateTime.UtcNow;
                var result = runner.Run("hang");

                Assert.IsTrue(result.TimedOut, "A hung worker must be reported as a timeout");

                var elapsed = DateTime.UtcNow - started;
                Assert.GreaterOrEqual(elapsed.TotalMilliseconds, WorkerProcessRunner.TimeoutMs - 500, "Timeout must actually be enforced");
                Assert.LessOrEqual(elapsed.TotalMilliseconds, WorkerProcessRunner.TimeoutMs + 8000, "Kill must happen shortly after the timeout");

                // The runner stays usable after killing a worker
                var second = runner.Run(WorkerOperation.LowPriorityStandby);

                Assert.IsNotNull(second);
                Assert.IsFalse(second.TimedOut, "The runner must recover after killing a hung worker");
            }
        }
    }
}

#pragma warning restore 1591 // Missing XML comment for publicly visible type or member
