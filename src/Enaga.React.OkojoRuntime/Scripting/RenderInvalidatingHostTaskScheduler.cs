using System.Collections.Concurrent;
using Okojo.Hosting;
using Okojo.Runtime;

namespace Enaga.React.OkojoRuntime;

internal sealed class RenderInvalidatingHostTaskScheduler(Action onTaskQueued, TimeProvider? timeProvider = null) : IHostTaskScheduler, IQueuedHostDelayScheduler, IDisposable
{
    private readonly ConcurrentQueue<Action> queuedDelayedTasks = new();
    private readonly TimeProvider timeProvider = timeProvider ?? TimeProvider.System;
    private bool disposed;

    public IHostAgentScheduler CreateAgentScheduler(HostTaskTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return new AgentScheduler(target, onTaskQueued);
    }

    public IHostDelayedOperation ScheduleDelayed(TimeSpan delay, Action<object?> callback, object? state)
    {
        return ScheduleDelayed(delay, default, callback, state);
    }

    public IHostDelayedOperation ScheduleDelayed(
        TimeSpan delay,
        HostTaskQueueKey targetQueue,
        Action<object?> callback,
        object? state)
    {
        ArgumentNullException.ThrowIfNull(callback);
        ObjectDisposedException.ThrowIf(disposed, this);
        return DelayedOperation.Create(this, timeProvider, delay, callback, state);
    }

    public void PumpQueuedDelayedTasks()
    {
        while (queuedDelayedTasks.TryDequeue(out var task))
            task();
    }

    public void Dispose()
    {
        disposed = true;
        while (queuedDelayedTasks.TryDequeue(out _))
        {
        }
    }

    private void EnqueueDelayedTask(Action task)
    {
        if (disposed)
            return;

        queuedDelayedTasks.Enqueue(task);
        onTaskQueued();
    }

    private sealed class AgentScheduler(HostTaskTarget target, Action onTaskQueued) : IHostAgentScheduler
    {
        public void EnqueueTask(Action<object?> callback, object? state)
        {
            ArgumentNullException.ThrowIfNull(callback);
            target.EnqueueTask(callback, state);
            onTaskQueued();
        }
    }

    private sealed class DelayedOperation : IHostDelayedOperation
    {
        private readonly Action<object?> callback;
        private readonly RenderInvalidatingHostTaskScheduler owner;
        private readonly object? state;
        private int status;
        private ITimer? timer;

        private DelayedOperation(RenderInvalidatingHostTaskScheduler owner, Action<object?> callback, object? state)
        {
            this.owner = owner;
            this.callback = callback;
            this.state = state;
        }

        public bool Cancel()
        {
            if (Interlocked.CompareExchange(ref status, 1, 0) != 0)
                return false;

            ReleaseTimer();
            return true;
        }

        public void Dispose()
        {
            _ = Cancel();
        }

        public static DelayedOperation Create(
            RenderInvalidatingHostTaskScheduler owner,
            TimeProvider timeProvider,
            TimeSpan delay,
            Action<object?> callback,
            object? state)
        {
            var operation = new DelayedOperation(owner, callback, state);
            var dueTime = delay <= TimeSpan.Zero ? TimeSpan.FromTicks(1) : delay;
            operation.timer = timeProvider.CreateTimer(static operationState =>
            {
                ((DelayedOperation)operationState!).OnReady();
            }, operation, dueTime, Timeout.InfiniteTimeSpan);
            return operation;
        }

        private void OnReady()
        {
            if (Interlocked.CompareExchange(ref status, 2, 0) != 0)
                return;

            ReleaseTimer();
            owner.EnqueueDelayedTask(() => callback(state));
        }

        private void ReleaseTimer()
        {
            Interlocked.Exchange(ref timer, null)?.Dispose();
        }
    }
}
