using System.Runtime.InteropServices;
using Silk.NET.Windowing;
using SilkGlfw = Silk.NET.GLFW.Glfw;

namespace Enaga.Hosting;

internal sealed class NativeWindowLoop : IDisposable
{
    private const double IdleEventPollMs = 16.67;
    private readonly double activeFramesPerSecond;
    private readonly SilkGlfw? glfwApi;
    private readonly Func<bool> hasImmediateWork;
    private readonly TimeProvider timeProvider;
    private readonly ManualResetEventSlim wakeSignal;
    private readonly object scheduleGate = new();
    private readonly IWindow window;
    private double? appliedFramesPerSecond;
    private double nextFrameDueMs;
    private long startTimestamp;

    public NativeWindowLoop(
        IWindow window,
        SilkGlfw? glfwApi,
        double activeFramesPerSecond,
        Func<bool> hasImmediateWork,
        ManualResetEventSlim wakeSignal,
        TimeProvider? timeProvider = null)
    {
        this.window = window ?? throw new ArgumentNullException(nameof(window));
        this.glfwApi = glfwApi;
        this.activeFramesPerSecond = activeFramesPerSecond;
        this.hasImmediateWork = hasImmediateWork ?? throw new ArgumentNullException(nameof(hasImmediateWork));
        this.wakeSignal = wakeSignal ?? throw new ArgumentNullException(nameof(wakeSignal));
        this.timeProvider = timeProvider ?? TimeProvider.System;
        startTimestamp = this.timeProvider.GetTimestamp();
    }

    public double ElapsedMs => timeProvider.GetElapsedTime(startTimestamp).TotalMilliseconds;

    public void RequestActiveCadence()
    {
        ApplyRenderCadence(activeFramesPerSecond);
    }

    public void RequestCadence(double framesPerSecond)
    {
        ApplyRenderCadence(framesPerSecond);
    }

    public void RequestImmediateFrame()
    {
        lock (scheduleGate)
        {
            nextFrameDueMs = Math.Min(nextFrameDueMs, ElapsedMs);
        }
    }

    public void ScheduleFrameNoLaterThan(double dueMs)
    {
        lock (scheduleGate)
        {
            nextFrameDueMs = Math.Min(nextFrameDueMs, dueMs);
        }
    }

    public void Run()
    {
        if (!window.IsInitialized)
        {
            window.Initialize();
            if (window.API.API == ContextAPI.Vulkan && window.VkSurface is null)
                throw new InvalidOperationException("Windowing platform doesn't support Vulkan.");
        }

        window.FramesPerSecond = 0;
        window.UpdatesPerSecond = 0;
        startTimestamp = timeProvider.GetTimestamp();
        nextFrameDueMs = ElapsedMs;
        using var timerResolutionScope = TimerResolutionScope.TryCreate();
        while (!window.IsClosing)
        {
            var framesPerSecond = appliedFramesPerSecond ?? activeFramesPerSecond;
            var renderPaused = framesPerSecond <= 0;
            var frameIntervalMs = renderPaused ? double.PositiveInfinity : 1000d / Math.Max(1, framesPerSecond);
            var nowMs = ElapsedMs;
            var remainingMs = Math.Max(0, GetNextFrameDueMs() - nowMs);
            if (remainingMs > 0.05)
                WaitForWork(double.IsPositiveInfinity(remainingMs) ? IdleEventPollMs : remainingMs);
            else
                ProcessEvents(resetWakeSignal: true);

            if (!window.IsClosing)
            {
                nowMs = ElapsedMs;
                if (hasImmediateWork())
                {
                    ApplyRenderCadence(activeFramesPerSecond);
                    renderPaused = false;
                    ScheduleFrameNoLaterThan(nowMs);
                }

                if (nowMs + 0.05 < GetNextFrameDueMs())
                    continue;

                window.DoUpdate();
            }
            if (!window.IsClosing)
            {
                if (renderPaused)
                    continue;

                window.DoRender();
                nowMs = ElapsedMs;
                if ((appliedFramesPerSecond ?? activeFramesPerSecond) <= 0)
                    continue;

                AdvanceNextFrameDueMs(nowMs, frameIntervalMs);
            }
        }

        ProcessEvents(resetWakeSignal: true);
        window.Reset();
    }

    public void Dispose()
    {
    }

    private void ProcessEvents(bool resetWakeSignal)
    {
        if (resetWakeSignal)
            wakeSignal.Reset();

        if (glfwApi is not null)
            glfwApi.PollEvents();
        else
            window.DoEvents();
    }

    private void WaitForWork(double timeoutMs)
    {
        wakeSignal.Wait(TimeSpan.FromMilliseconds(Math.Max(0, timeoutMs)));
        ProcessEvents(resetWakeSignal: true);
    }

    private void ApplyRenderCadence(double framesPerSecond)
    {
        var targetFramesPerSecond = framesPerSecond <= 0 ? 0 : Math.Max(1, framesPerSecond);
        if (appliedFramesPerSecond is { } applied &&
            Math.Abs(applied - targetFramesPerSecond) < 0.001)
        {
            return;
        }

        appliedFramesPerSecond = targetFramesPerSecond;
        if (targetFramesPerSecond <= 0)
        {
            lock (scheduleGate)
            {
                nextFrameDueMs = double.PositiveInfinity;
            }
        }
    }

    private double GetNextFrameDueMs()
    {
        lock (scheduleGate)
        {
            return nextFrameDueMs;
        }
    }

    private void AdvanceNextFrameDueMs(double nowMs, double frameIntervalMs)
    {
        lock (scheduleGate)
        {
            if (nowMs - nextFrameDueMs > frameIntervalMs)
                nextFrameDueMs = nowMs + frameIntervalMs;
            else
                nextFrameDueMs += frameIntervalMs;
        }
    }

    private sealed class TimerResolutionScope : IDisposable
    {
        private readonly uint periodMs;
        private bool disposed;

        private TimerResolutionScope(uint periodMs)
        {
            this.periodMs = periodMs;
        }

        public static TimerResolutionScope? TryCreate()
        {
            if (!OperatingSystem.IsWindows())
                return null;

            const uint periodMs = 2;
            var result = WinMM.TimeBeginPeriod(periodMs);
            if (result != 0)
                throw new InvalidOperationException($"timeBeginPeriod({periodMs}) failed with MMRESULT={result}.");

            return new TimerResolutionScope(periodMs);
        }

        public void Dispose()
        {
            if (disposed || !OperatingSystem.IsWindows())
                return;

            disposed = true;
            var result = WinMM.TimeEndPeriod(periodMs);
            if (result != 0)
                throw new InvalidOperationException($"timeEndPeriod({periodMs}) failed with MMRESULT={result}.");
        }
    }

    private static partial class WinMM
    {
        [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
        public static extern uint TimeBeginPeriod(uint periodMs);

        [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
        public static extern uint TimeEndPeriod(uint periodMs);
    }
}
