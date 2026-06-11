using Okojo.Hosting;
using Okojo.Runtime;

namespace Enaga.React.OkojoRuntime;

internal sealed class RenderInvalidatingHostTaskScheduler(
    Action onTaskQueued,
    TimeProvider? timeProvider = null
) : IHostTaskScheduler, IQueuedHostDelayScheduler, IHostTaskQueuePump, IDisposable
{
    private readonly object gate = new();
    private readonly Dictionary<HostTaskQueueKey, Queue<QueuedWorkItem>> queues = [];
    private readonly TimeProvider timeProvider = timeProvider ?? TimeProvider.System;
    private bool disposed;

    public IHostAgentScheduler CreateAgentScheduler(HostTaskTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return new AgentScheduler(this, target);
    }

    public IHostDelayedOperation ScheduleDelayed(
        TimeSpan delay,
        Action<object?> callback,
        object? state
    )
    {
        return ScheduleDelayed(delay, default, callback, state);
    }

    public IHostDelayedOperation ScheduleDelayed(
        TimeSpan delay,
        HostTaskQueueKey targetQueue,
        Action<object?> callback,
        object? state
    )
    {
        ArgumentNullException.ThrowIfNull(callback);
        ObjectDisposedException.ThrowIf(disposed, this);
        return DelayedOperation.Create(this, timeProvider, delay, targetQueue, callback, state);
    }

    public int PumpQueue(HostTaskQueueKey queueKey, int maxTasks = int.MaxValue)
    {
        if (maxTasks <= 0)
            return 0;

        QueuedWorkItem[]? batch = null;
        var count = 0;
        lock (gate)
        {
            if (!queues.TryGetValue(queueKey, out var queue) || queue.Count == 0)
                return 0;

            count = Math.Min(maxTasks, queue.Count);
            batch = new QueuedWorkItem[count];
            for (var index = 0; index < count; index++)
                batch[index] = queue.Dequeue();
        }

        for (var index = 0; index < count; index++)
            batch[index].Invoke();
        return count;
    }

    public bool PumpOne(params ReadOnlySpan<HostTaskQueueKey> preferredOrder)
    {
        if (!TryDequeueNextAction(preferredOrder, out var task))
            return false;

        task.Invoke();
        return true;
    }

    private bool TryDequeueNextAction(
        ReadOnlySpan<HostTaskQueueKey> preferredOrder,
        out QueuedWorkItem task
    )
    {
        lock (gate)
        {
            if (preferredOrder.Length != 0)
            {
                for (var index = 0; index < preferredOrder.Length; index++)
                {
                    if (
                        !queues.TryGetValue(preferredOrder[index], out var preferredQueue)
                        || preferredQueue.Count == 0
                    )
                        continue;

                    task = preferredQueue.Dequeue();
                    return true;
                }
            }

            foreach (var queue in queues.Values)
            {
                if (queue.Count == 0)
                    continue;

                task = queue.Dequeue();
                return true;
            }
        }

        task = default;
        return false;
    }

    public void Dispose()
    {
        disposed = true;
        lock (gate)
        {
            queues.Clear();
        }
    }

    private void EnqueueReady(HostTaskQueueKey queueKey, in QueuedWorkItem task)
    {
        if (disposed)
            return;

        lock (gate)
        {
            if (!queues.TryGetValue(queueKey, out var queue))
            {
                queue = [];
                queues[queueKey] = queue;
            }

            queue.Enqueue(task);
        }

        onTaskQueued();
    }

    private sealed class AgentScheduler(
        RenderInvalidatingHostTaskScheduler owner,
        HostTaskTarget target
    ) : IQueuedHostAgentScheduler
    {
        private static readonly Action<object?> SEnqueueAgentTask = static state =>
        {
            var work = (AgentQueuedWork)state!;
            work.Target.EnqueueTask(work.Callback, work.State);
        };

        public void EnqueueTask(Action<object?> callback, object? state)
        {
            EnqueueTask(HostingTaskQueueKeys.Default, callback, state);
        }

        public void EnqueueTask(HostTaskQueueKey queueKey, Action<object?> callback, object? state)
        {
            ArgumentNullException.ThrowIfNull(callback);
            owner.EnqueueReady(
                queueKey,
                new QueuedWorkItem(SEnqueueAgentTask, new AgentQueuedWork(target, callback, state))
            );
        }
    }

    private sealed class DelayedOperation : IHostDelayedOperation
    {
        private readonly Action<object?> callback;
        private readonly RenderInvalidatingHostTaskScheduler owner;
        private readonly HostTaskQueueKey targetQueue;
        private readonly object? state;
        private int status;
        private ITimer? timer;

        private DelayedOperation(
            RenderInvalidatingHostTaskScheduler owner,
            HostTaskQueueKey targetQueue,
            Action<object?> callback,
            object? state
        )
        {
            this.owner = owner;
            this.targetQueue = targetQueue;
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
            HostTaskQueueKey targetQueue,
            Action<object?> callback,
            object? state
        )
        {
            var operation = new DelayedOperation(owner, targetQueue, callback, state);
            var dueTime = delay <= TimeSpan.Zero ? TimeSpan.FromTicks(1) : delay;
            operation.timer = timeProvider.CreateTimer(
                static operationState =>
                {
                    ((DelayedOperation)operationState!).OnReady();
                },
                operation,
                dueTime,
                Timeout.InfiniteTimeSpan
            );
            return operation;
        }

        private void OnReady()
        {
            if (Interlocked.CompareExchange(ref status, 2, 0) != 0)
                return;

            ReleaseTimer();
            owner.EnqueueReady(targetQueue, new QueuedWorkItem(callback, state));
        }

        private void ReleaseTimer()
        {
            Interlocked.Exchange(ref timer, null)?.Dispose();
        }
    }

    private readonly record struct QueuedWorkItem(Action<object?> Callback, object? State)
    {
        public void Invoke()
        {
            Callback(State);
        }
    }

    private sealed class AgentQueuedWork(
        HostTaskTarget target,
        Action<object?> callback,
        object? state
    )
    {
        public HostTaskTarget Target { get; } = target;
        public Action<object?> Callback { get; } = callback;
        public object? State { get; } = state;
    }
}
