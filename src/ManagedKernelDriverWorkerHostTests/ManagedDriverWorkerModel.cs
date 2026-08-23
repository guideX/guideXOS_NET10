namespace GuideXOS.Net10.ManagedKernel;

internal enum ManagedDriverWorkerModelState : uint
{
    Created = 0,
    Running = 1,
    Stopping = 2,
    Stopped = 3,
    Destroyed = 4
}

internal readonly struct ManagedDriverWorkItem
{
    internal ManagedDriverWorkItem(uint deviceId, ulong subscriptionId,
                                   ulong sequence, byte payload)
    {
        DeviceId = deviceId;
        SubscriptionId = subscriptionId;
        Sequence = sequence;
        Payload = payload;
    }

    internal uint DeviceId { get; }
    internal ulong SubscriptionId { get; }
    internal ulong Sequence { get; }
    internal byte Payload { get; }
}

internal interface IManagedDriverWorkerConsumer
{
    bool TryHandle(in ManagedDriverWorkItem item);
}

/* Host-only executable model for the native worker contract. It intentionally
   has no scheduler, threading, or ABI dependencies: those are exercised by
   the native/managed boot path, while this model makes queue, routing,
   coalescing, bounded activation, and lifecycle invariants deterministic. */
internal sealed class ManagedDriverWorkerModel
{
    internal const uint QueueCapacity = 8;
    internal const uint MaxEventsPerActivation = 4;

    private readonly ManagedDriverWorkItem[] _queue =
        new ManagedDriverWorkItem[(int)QueueCapacity];
    private readonly uint[] _deviceIds = new uint[2];
    private readonly ulong[] _subscriptionIds = new ulong[2];
    private readonly IManagedDriverWorkerConsumer?[] _consumers =
        new IManagedDriverWorkerConsumer?[2];
    private uint _read;
    private uint _write;
    private bool _workPending;

    internal ManagedDriverWorkerModelState State { get; private set; } =
        ManagedDriverWorkerModelState.Created;
    internal uint QueueDepth => _write - _read;
    internal uint QueueHighWater { get; private set; }
    internal uint WakeRequests { get; private set; }
    internal uint DropCount { get; private set; }
    internal uint DispatchBatches { get; private set; }
    internal uint DeliveredCount { get; private set; }
    internal uint RejectedCount { get; private set; }
    internal uint YieldCount { get; private set; }
    internal uint RescheduleCount { get; private set; }
    internal bool IsSleeping => State == ManagedDriverWorkerModelState.Running &&
                                 QueueDepth == 0 && !_workPending;

    internal bool RegisterConsumer(uint deviceId, ulong subscriptionId,
                                   IManagedDriverWorkerConsumer consumer)
    {
        if (State != ManagedDriverWorkerModelState.Created ||
            consumer == null || deviceId == 0 || subscriptionId == 0)
        {
            return false;
        }
        for (int index = 0; index != _consumers.Length; ++index)
        {
            if (_consumers[index] != null) continue;
            _deviceIds[index] = deviceId;
            _subscriptionIds[index] = subscriptionId;
            _consumers[index] = consumer;
            return true;
        }
        return false;
    }

    internal bool Start()
    {
        if (State != ManagedDriverWorkerModelState.Created) return false;
        State = ManagedDriverWorkerModelState.Running;
        return true;
    }

    internal bool Enqueue(in ManagedDriverWorkItem item)
    {
        if (State != ManagedDriverWorkerModelState.Running ||
            item.DeviceId == 0 || item.SubscriptionId == 0 ||
            item.Sequence == 0) return false;
        if (QueueDepth >= QueueCapacity)
        {
            DropCount++;
            return false;
        }
        _queue[_write % QueueCapacity] = item;
        _write++;
        if (QueueDepth > QueueHighWater) QueueHighWater = QueueDepth;
        if (!_workPending)
        {
            _workPending = true;
            WakeRequests++;
        }
        return true;
    }

    internal bool RunActivation()
    {
        if (State != ManagedDriverWorkerModelState.Running) return false;
        DispatchBatches++;
        uint processed = 0;
        while (processed++ != MaxEventsPerActivation && _read != _write)
        {
            ManagedDriverWorkItem item = _queue[_read % QueueCapacity];
            _read++;
            int route = -1;
            for (int index = 0; index != _consumers.Length; ++index)
            {
                if (_consumers[index] != null &&
                    _deviceIds[index] == item.DeviceId &&
                    _subscriptionIds[index] == item.SubscriptionId)
                {
                    route = index;
                    break;
                }
            }
            if (route < 0 || !_consumers[route]!.TryHandle(in item))
            {
                RejectedCount++;
            }
            else
            {
                DeliveredCount++;
            }
        }
        if (_read == _write)
        {
            _workPending = false;
        }
        else
        {
            YieldCount++;
            RescheduleCount++;
        }
        return true;
    }

    internal bool BeginStop()
    {
        if (State != ManagedDriverWorkerModelState.Running) return false;
        State = ManagedDriverWorkerModelState.Stopping;
        return true;
    }

    internal bool CompleteStop()
    {
        if (State != ManagedDriverWorkerModelState.Stopping ||
            QueueDepth != 0) return false;
        _workPending = false;
        State = ManagedDriverWorkerModelState.Stopped;
        return true;
    }

    internal bool Destroy()
    {
        if (State != ManagedDriverWorkerModelState.Stopped) return false;
        State = ManagedDriverWorkerModelState.Destroyed;
        return true;
    }
}
