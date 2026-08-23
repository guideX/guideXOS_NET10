using System;

namespace GuideXOS.Net10.ManagedKernel;

internal enum ManagedDriverWorkerState : uint
{
    Created = 0,
    Starting = 1,
    Running = 2,
    Stopping = 3,
    Stopped = 4,
    Destroyed = 5
}

/* This object is deliberately narrower than Thread/Task.  Native scheduler
   state owns wakeability and stack lifetime; this managed object owns only
   bounded dispatch policy and driver routing. */
internal unsafe sealed class ManagedDriverWorker
{
    internal const uint MaxEventsPerActivation = 4;

    private readonly ManagedInterruptDispatcher _dispatcher;
    private readonly ManagedSerialDriver _serialDriver;
    private ManagedKeyboardDriver? _keyboardDriver;
    private ManagedDriverWorkerState _state;
    private uint _dispatchBatches;
    private uint _managedDispatches;
    private uint _delivered;
    private uint _rejected;

    internal ManagedDriverWorker(ManagedInterruptDispatcher dispatcher,
                                 ManagedSerialDriver serialDriver,
                                 ManagedKeyboardDriver? keyboardDriver = null)
    {
        _dispatcher = dispatcher;
        _serialDriver = serialDriver;
        _keyboardDriver = keyboardDriver;
        _state = ManagedDriverWorkerState.Created;
    }

    internal ManagedDriverWorkerState State => _state;
    internal uint DispatchBatches => _dispatchBatches;
    internal uint ManagedDispatches => _managedDispatches;
    internal uint Delivered => _delivered;
    internal uint Rejected => _rejected;

    internal bool AttachKeyboard(ManagedKeyboardDriver keyboardDriver)
    {
        if (_state != ManagedDriverWorkerState.Running ||
            keyboardDriver == null || _keyboardDriver != null) return false;
        _keyboardDriver = keyboardDriver;
        return true;
    }

    internal bool DetachKeyboard(ManagedKeyboardDriver keyboardDriver)
    {
        if (_state != ManagedDriverWorkerState.Running ||
            keyboardDriver == null || _keyboardDriver != keyboardDriver) return false;
        _keyboardDriver = null;
        return true;
    }

    internal bool Start()
    {
        if (_state != ManagedDriverWorkerState.Created) return false;
        _state = ManagedDriverWorkerState.Starting;
        _state = ManagedDriverWorkerState.Running;
        return true;
    }

    internal bool Dispatch(out uint delivered, out uint rejected)
    {
        delivered = 0;
        rejected = 0;
        if (_state != ManagedDriverWorkerState.Running) return false;
        if (!_serialDriver.TryReconcileOperationalReceive(_dispatcher))
        {
            return false;
        }
        GC.KeepAlive(_serialDriver);
        _dispatchBatches++;
        _managedDispatches++;
        if (!_dispatcher.TryDispatchBatch(_serialDriver, _keyboardDriver,
                                           out delivered, out rejected)) return false;
        _delivered += delivered;
        _rejected += rejected;
        return true;
    }

    internal bool BeginStop()
    {
        if (_state != ManagedDriverWorkerState.Running) return false;
        _state = ManagedDriverWorkerState.Stopping;
        return true;
    }

    internal bool CompleteStop()
    {
        if (_state != ManagedDriverWorkerState.Stopping) return false;
        _state = ManagedDriverWorkerState.Stopped;
        return true;
    }

    internal bool Destroy()
    {
        if (_state != ManagedDriverWorkerState.Stopped) return false;
        _state = ManagedDriverWorkerState.Destroyed;
        return true;
    }
}
