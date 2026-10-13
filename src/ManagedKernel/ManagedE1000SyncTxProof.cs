namespace GuideXOS.Net10.ManagedKernel;

/* A TX descriptor's DD bit proves hardware completion. This separate proof
   records that a TXDW notification assigned a route sequence after the
   synchronous submission boundary; it deliberately does not assume one
   notification per descriptor or frame. */
internal sealed class ManagedE1000SyncTxProof
{
    private ulong _firstSequence;
    private uint _txdwNotificationCount;
    private bool _armed;

    internal uint TxdwNotificationCount => _txdwNotificationCount;
    internal bool IsSatisfied => _txdwNotificationCount != 0;
    internal bool IsArmed => _armed;

    internal bool Arm(ulong firstSequence)
    {
        if (firstSequence == 0) return false;
        _firstSequence = firstSequence;
        _txdwNotificationCount = 0;
        _armed = true;
        return true;
    }

    internal bool Observe(ulong sequence, uint cause)
    {
        if (!_armed || sequence < _firstSequence ||
            (cause & ManagedE1000Protocol.InterruptCauseTxDescriptorWriteback) == 0)
            return false;
        if (_txdwNotificationCount != uint.MaxValue)
            _txdwNotificationCount++;
        return true;
    }

    internal void Seal() => _armed = false;

    internal bool IsAccepted(bool synchronousDdComplete,
        bool dispatchSucceeded, uint rejectedEvents, uint asyncOutstanding) =>
        IsSatisfied && synchronousDdComplete && dispatchSucceeded &&
        rejectedEvents == 0 && asyncOutstanding == 0;
}
