using System;

namespace GuideXOS.Net10.ManagedKernel;

internal enum ManagedE1000AsyncTxOwnership : byte
{
    Free, Prepared, HardwareOwned, Completed
}
internal enum ManagedE1000AsyncTxResult : byte
{
    Pending, Success, HardwareError, InvalidOrStale, AlreadyConsumed
}
internal enum ManagedE1000AsyncTxSubmitResult : byte
{
    Accepted, Busy, InvalidArgument, Unavailable
}

/* Fixed-capacity software ownership ledger for the E1000 TX ring. Hardware DD
   is supplied by the driver and remains the only completion authority. */
internal sealed class ManagedE1000AsyncTxLedger
{
    internal const uint Capacity = 4;
    private readonly ManagedE1000AsyncTxOwnership[] _ownership =
        new ManagedE1000AsyncTxOwnership[(int)ManagedE1000Protocol.RingCount];
    private readonly uint[] _generation =
        new uint[(int)ManagedE1000Protocol.RingCount];
    private readonly ManagedE1000AsyncTxResult[] _result =
        new ManagedE1000AsyncTxResult[(int)ManagedE1000Protocol.RingCount];
    private readonly bool[] _completionExposed =
        new bool[(int)ManagedE1000Protocol.RingCount];
    private uint _episode;
    private uint _outstanding;
    private uint _exposedCompletions;
    private uint _consumedCompletions;
    private bool _admitting;

    internal uint Outstanding => _outstanding;
    internal uint Episode => _episode;
    internal bool IsAdmitting => _admitting;
    internal uint ExposedCompletions => _exposedCompletions;
    internal uint ConsumedCompletions => _consumedCompletions;
    internal uint CompletedUnconsumed =>
        CountOwnership(ManagedE1000AsyncTxOwnership.Completed);
    internal uint HardwareOwned =>
        CountOwnership(ManagedE1000AsyncTxOwnership.HardwareOwned);

    internal bool StartEpisode(uint episode)
    {
        if (episode == 0 || _outstanding != 0) return false;
        Array.Clear(_ownership);
        Array.Clear(_generation);
        Array.Clear(_result);
        Array.Clear(_completionExposed);
        _exposedCompletions = 0;
        _consumedCompletions = 0;
        _episode = episode;
        _admitting = true;
        return true;
    }

    internal ManagedE1000AsyncTxSubmitResult TryPrepare(uint index,
        out ulong requestId)
    {
        requestId = 0;
        if (!_admitting) return ManagedE1000AsyncTxSubmitResult.Unavailable;
        if (index >= ManagedE1000Protocol.RingCount)
            return ManagedE1000AsyncTxSubmitResult.InvalidArgument;
        if (_outstanding >= Capacity ||
            _ownership[index] != ManagedE1000AsyncTxOwnership.Free)
            return ManagedE1000AsyncTxSubmitResult.Busy;
        uint generation = _generation[index];
        if (generation >= ManagedE1000Protocol.AsyncTxGenerationMax)
            return ManagedE1000AsyncTxSubmitResult.Busy;
        generation++;
        if (!ManagedE1000Protocol.TryBuildAsyncTxRequestId(index, generation,
                _episode, ManagedE1000Protocol.RingCount, out requestId))
            return ManagedE1000AsyncTxSubmitResult.Unavailable;
        _generation[index] = generation;
        _completionExposed[index] = false;
        _ownership[index] = ManagedE1000AsyncTxOwnership.Prepared;
        _outstanding++;
        return ManagedE1000AsyncTxSubmitResult.Accepted;
    }

    internal bool MarkHardwareOwned(uint index)
    {
        if (!_admitting || index >= ManagedE1000Protocol.RingCount ||
            _ownership[index] != ManagedE1000AsyncTxOwnership.Prepared)
            return false;
        _ownership[index] = ManagedE1000AsyncTxOwnership.HardwareOwned;
        return true;
    }

    internal bool RollbackPrepared(uint index)
    {
        if (index >= ManagedE1000Protocol.RingCount ||
            _ownership[index] != ManagedE1000AsyncTxOwnership.Prepared ||
            _outstanding == 0) return false;
        _ownership[index] = ManagedE1000AsyncTxOwnership.Free;
        _outstanding--;
        return true;
    }

    internal bool RollbackHardwareHandoff(uint index)
    {
        if (index >= ManagedE1000Protocol.RingCount ||
            _ownership[index] != ManagedE1000AsyncTxOwnership.HardwareOwned ||
            _outstanding == 0) return false;
        _ownership[index] = ManagedE1000AsyncTxOwnership.Free;
        _outstanding--;
        return true;
    }

    internal bool CompleteFromDescriptor(uint index, byte status)
    {
        if (!_admitting || index >= ManagedE1000Protocol.RingCount ||
            _ownership[index] != ManagedE1000AsyncTxOwnership.HardwareOwned ||
            (status & ManagedE1000Protocol.TxStatusDone) == 0) return false;
        _ownership[index] = ManagedE1000AsyncTxOwnership.Completed;
        _result[index] = (status & ManagedE1000Protocol.TxStatusErrorMask) != 0
            ? ManagedE1000AsyncTxResult.HardwareError
            : ManagedE1000AsyncTxResult.Success;
        return true;
    }

    internal ManagedE1000AsyncTxResult Query(ulong requestId)
    {
        if (!_admitting || !TryDecodeCurrent(requestId, out uint index,
                out uint generation) ||
            _generation[index] != generation ||
            _ownership[index] == ManagedE1000AsyncTxOwnership.Free)
            return ManagedE1000AsyncTxResult.InvalidOrStale;
        if (_ownership[index] != ManagedE1000AsyncTxOwnership.Completed)
            return ManagedE1000AsyncTxResult.Pending;
        if (!_completionExposed[index])
        {
            _completionExposed[index] = true;
            _exposedCompletions++;
        }
        return _result[index];
    }

    internal ManagedE1000AsyncTxResult Consume(ulong requestId)
    {
        ManagedE1000AsyncTxResult result = Query(requestId);
        if (result != ManagedE1000AsyncTxResult.Success &&
            result != ManagedE1000AsyncTxResult.HardwareError) return result;
        ManagedE1000Protocol.TryDecodeAsyncTxRequestId(requestId,
            ManagedE1000Protocol.RingCount, out uint index, out _, out _);
        _ownership[index] = ManagedE1000AsyncTxOwnership.Free;
        _outstanding--;
        _consumedCompletions++;
        return result;
    }

    internal void BeginStopping() => _admitting = false;

    /* Call only after E1000 reset completed and PCI bus mastering is clear. */
    internal bool InvalidateAfterDmaQuiesced()
    {
        if (_admitting) return false;
        Array.Clear(_ownership);
        Array.Clear(_result);
        Array.Clear(_completionExposed);
        _outstanding = 0;
        return true;
    }

    internal bool TryCompleteOwned(uint index, byte status) =>
        CompleteFromDescriptor(index, status);

    internal bool IsFree(uint index) =>
        index < ManagedE1000Protocol.RingCount &&
        _ownership[index] == ManagedE1000AsyncTxOwnership.Free;

    internal ManagedE1000AsyncTxOwnership OwnershipAt(uint index) =>
        index < ManagedE1000Protocol.RingCount
            ? _ownership[index]
            : ManagedE1000AsyncTxOwnership.Free;

    private uint CountOwnership(ManagedE1000AsyncTxOwnership ownership)
    {
        uint count = 0;
        for (uint index = 0; index != ManagedE1000Protocol.RingCount; ++index)
            if (_ownership[index] == ownership) count++;
        return count;
    }

    private bool TryDecodeCurrent(ulong requestId, out uint index,
        out uint generation)
    {
        index = 0;
        generation = 0;
        return ManagedE1000Protocol.TryDecodeAsyncTxRequestId(requestId,
                   ManagedE1000Protocol.RingCount, out index, out generation,
                   out uint episode) && episode == _episode;
    }
}
