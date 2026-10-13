using System;
using GuideXOS.Net10.ManagedKernel;

internal static class Program
{
    private static int s_checks;

    private static int Main()
    {
        var first = new ManagedE1000AsyncTxLedger();
        Check(first.StartEpisode(1), "episode-1-starts");
        Check(first.TryPrepare(3, out ulong pendingId) ==
              ManagedE1000AsyncTxSubmitResult.Accepted &&
              first.MarkHardwareOwned(3) &&
              first.Query(pendingId) == ManagedE1000AsyncTxResult.Pending,
            "pending-request-is-hardware-owned");
        first.BeginStopping();
        Check(first.TryPrepare(4, out _) ==
              ManagedE1000AsyncTxSubmitResult.Unavailable,
            "submission-rejected-as-soon-as-stop-begins");
        Check(first.Query(pendingId) == ManagedE1000AsyncTxResult.InvalidOrStale &&
              first.Consume(pendingId) == ManagedE1000AsyncTxResult.InvalidOrStale,
            "query-and-consume-rejected-during-stopping");
        Check(first.InvalidateAfterDmaQuiesced() && first.Outstanding == 0 &&
              first.Query(pendingId) == ManagedE1000AsyncTxResult.InvalidOrStale,
            "pending-ledger-invalidated-after-dma-quiescence");
        Check(!first.TryCompleteOwned(3, ManagedE1000Protocol.TxStatusDone) &&
              first.Outstanding == 0,
            "queued-txdw-after-invalidation-cannot-resurrect-request");

        ulong reusedId = 0;
        Check(first.StartEpisode(2) &&
              first.TryPrepare(3, out reusedId) ==
                  ManagedE1000AsyncTxSubmitResult.Accepted &&
              first.MarkHardwareOwned(3) &&
              reusedId != pendingId &&
              ManagedE1000Protocol.TryDecodeAsyncTxRequestId(reusedId, 16,
                  out uint reusedIndex, out uint reusedGeneration,
                  out uint reusedEpisode) &&
              reusedIndex == 3 && reusedGeneration == 1 && reusedEpisode == 2,
            "same-slot-and-generation-get-new-episode-identity");
        Check(first.Query(pendingId) == ManagedE1000AsyncTxResult.InvalidOrStale &&
              first.Consume(pendingId) == ManagedE1000AsyncTxResult.InvalidOrStale,
            "old-identity-rejected-against-new-request");
        Check(!first.TryCompleteOwned(3, 0) &&
              first.Query(reusedId) == ManagedE1000AsyncTxResult.Pending,
            "late-txdw-without-new-dd-cannot-complete-new-episode");
        Check(first.TryCompleteOwned(3, ManagedE1000Protocol.TxStatusDone) &&
              first.Query(reusedId) == ManagedE1000AsyncTxResult.Success &&
              !first.TryCompleteOwned(3, ManagedE1000Protocol.TxStatusDone),
            "dd-completes-once-and-duplicate-txdw-cannot-mutate-completion");
        Check(first.Consume(reusedId) == ManagedE1000AsyncTxResult.Success &&
              first.Consume(reusedId) == ManagedE1000AsyncTxResult.InvalidOrStale,
            "post-restart-query-and-consume");

        Check(first.TryPrepare(3, out ulong generationTwoId) ==
                  ManagedE1000AsyncTxSubmitResult.Accepted &&
              first.MarkHardwareOwned(3) &&
              generationTwoId != reusedId &&
              first.Query(reusedId) == ManagedE1000AsyncTxResult.InvalidOrStale &&
              first.Query(generationTwoId) == ManagedE1000AsyncTxResult.Pending,
            "same-episode-slot-generation-reuse-keeps-old-id-stale");
        first.BeginStopping();
        Check(first.InvalidateAfterDmaQuiesced() && first.Outstanding == 0,
            "second-stop-clears-generation-two-request");

        var second = new ManagedE1000AsyncTxLedger();
        Check(second.StartEpisode(7), "second-driver-instance-starts");
        ulong[] mixedIds = new ulong[4];
        for (uint index = 0; index < 4; ++index)
        {
            Check(second.TryPrepare(index, out mixedIds[index]) ==
                  ManagedE1000AsyncTxSubmitResult.Accepted &&
                  second.MarkHardwareOwned(index),
                "teardown-request-prepared-" + index);
        }
        Check(second.TryCompleteOwned(0, ManagedE1000Protocol.TxStatusDone) &&
              second.TryCompleteOwned(2, ManagedE1000Protocol.TxStatusDone) &&
              second.Query(mixedIds[0]) == ManagedE1000AsyncTxResult.Success &&
              second.Query(mixedIds[1]) == ManagedE1000AsyncTxResult.Pending &&
              second.Query(mixedIds[2]) == ManagedE1000AsyncTxResult.Success &&
              second.Query(mixedIds[3]) == ManagedE1000AsyncTxResult.Pending &&
              second.Outstanding == 4 && second.CompletedUnconsumed == 2 &&
              second.HardwareOwned == 2 && second.ExposedCompletions == 2 &&
              second.ConsumedCompletions == 0,
            "pending-and-completed-unconsumed-remain-api-outstanding");
        second.BeginStopping();
        for (uint index = 0; index < 4; ++index)
            Check(second.Query(mixedIds[index]) ==
                      ManagedE1000AsyncTxResult.InvalidOrStale &&
                  second.Consume(mixedIds[index]) ==
                      ManagedE1000AsyncTxResult.InvalidOrStale,
                "all-old-identities-rejected-during-stop-" + index);
        Check(second.InvalidateAfterDmaQuiesced() && second.Outstanding == 0,
            "teardown-clears-pending-and-completed-ledger");
        for (uint index = 0; index < 4; ++index)
            Check(second.Query(mixedIds[index]) ==
                      ManagedE1000AsyncTxResult.InvalidOrStale &&
                  second.Consume(mixedIds[index]) ==
                      ManagedE1000AsyncTxResult.InvalidOrStale,
                "all-old-identities-rejected-after-teardown-" + index);

        var restarted = new ManagedE1000AsyncTxLedger();
        Check(restarted.StartEpisode(8), "recreated-driver-starts");
        ulong[] ids = new ulong[ManagedE1000AsyncTxLedger.Capacity];
        for (uint index = 0; index < (uint)ids.Length; ++index)
        {
            Check(restarted.TryPrepare(index, out ids[index]) ==
                  ManagedE1000AsyncTxSubmitResult.Accepted &&
                  restarted.MarkHardwareOwned(index),
                "restarted-capacity-slot-" + index);
        }
        Check(restarted.Outstanding == 4 &&
              restarted.TryPrepare(4, out _) == ManagedE1000AsyncTxSubmitResult.Busy,
            "async-capacity-restored-to-four");
        for (uint index = 0; index < (uint)ids.Length; ++index)
            Check(restarted.TryCompleteOwned(index, ManagedE1000Protocol.TxStatusDone) &&
                  restarted.Query(ids[index]) == ManagedE1000AsyncTxResult.Success &&
                  restarted.Consume(ids[index]) == ManagedE1000AsyncTxResult.Success,
                "restarted-request-complete-and-consume-" + index);
        Check(restarted.Outstanding == 0 &&
              restarted.ExposedCompletions == 4 &&
              restarted.ConsumedCompletions == 4,
            "restarted-ledger-empty-after-consume");

        Check(!ManagedE1000Protocol.IsTxDescriptorStatusComplete(0) &&
              ManagedE1000Protocol.IsTxDescriptorStatusComplete(
                  ManagedE1000Protocol.TxStatusDone),
            "synchronous-tx-still-uses-dd-as-completion-authority");

        RunSyncTxNotificationRaceRegressions();

        var handoffRollback = new ManagedE1000AsyncTxLedger();
        Check(handoffRollback.StartEpisode(9) &&
              handoffRollback.TryPrepare(2, out _) ==
                  ManagedE1000AsyncTxSubmitResult.Accepted &&
              handoffRollback.MarkHardwareOwned(2) &&
              handoffRollback.RollbackHardwareHandoff(2) &&
              handoffRollback.Outstanding == 0 &&
              handoffRollback.IsFree(2),
            "failed-tail-handoff-rolls-back-before-hardware-ownership");

        Console.WriteLine("MANAGED_KERNEL_PHASE91_HOST_TESTS=PASS checks=" + s_checks);
        return 0;
    }

    private static void RunSyncTxNotificationRaceRegressions()
    {
        const uint txdw = ManagedE1000Protocol.InterruptCauseTxDescriptorWriteback;

        var immediate = new ManagedE1000SyncTxProof();
        Check(immediate.Arm(10) && immediate.Observe(10, txdw) &&
              immediate.IsAccepted(true, true, 0, 0),
            "sync-txdw-immediate-delivery-accepts-dd-and-fresh-notification");

        var deferred = new ManagedE1000SyncTxProof();
        Check(deferred.Arm(20), "sync-txdw-deferred-proof-arms");
        bool ddCompletedBeforeDispatch = true;
        Check(!deferred.IsAccepted(ddCompletedBeforeDispatch, false, 0, 0),
            "sync-dd-completion-does-not-imply-managed-dispatch");
        Check(deferred.Observe(20, txdw) &&
              deferred.IsAccepted(ddCompletedBeforeDispatch, true, 0, 0),
            "queued-txdw-dispatched-after-dd-completion-is-accepted");

        var coalescedLedger = new ManagedE1000AsyncTxLedger();
        Check(coalescedLedger.StartEpisode(10),
            "coalesced-completion-ledger-starts");
        ulong[] coalescedIds = new ulong[2];
        for (uint index = 0; index != (uint)coalescedIds.Length; ++index)
            Check(coalescedLedger.TryPrepare(index, out coalescedIds[index]) ==
                      ManagedE1000AsyncTxSubmitResult.Accepted &&
                  coalescedLedger.MarkHardwareOwned(index),
                "coalesced-descriptor-prepared-and-owned-" + index);
        Check(coalescedLedger.TryCompleteOwned(0,
                  ManagedE1000Protocol.TxStatusDone) &&
              coalescedLedger.TryCompleteOwned(1,
                  ManagedE1000Protocol.TxStatusDone),
            "one-txdw-may-observe-multiple-dd-completions");
        foreach (ulong id in coalescedIds)
            Check(coalescedLedger.Query(id) == ManagedE1000AsyncTxResult.Success &&
                  coalescedLedger.Consume(id) == ManagedE1000AsyncTxResult.Success,
                "coalesced-dd-completion-remains-request-specific");
        var coalescedProof = new ManagedE1000SyncTxProof();
        Check(coalescedProof.Arm(30) && coalescedProof.Observe(30, txdw) &&
              coalescedProof.TxdwNotificationCount == 1 &&
              coalescedProof.IsAccepted(true, true, 0,
                  coalescedLedger.Outstanding),
            "one-txdw-notification-does-not-require-one-event-per-frame");

        var staleBeforeRestart = new ManagedE1000SyncTxProof();
        Check(staleBeforeRestart.Arm(40) &&
              !staleBeforeRestart.Observe(39, txdw) &&
              !staleBeforeRestart.IsAccepted(true, true, 0, 0),
            "pre-restart-e1000-event-cannot-satisfy-new-sync-proof");

        var replacementRoute = new ManagedE1000SyncTxProof();
        Check(replacementRoute.Arm(51) &&
              !replacementRoute.Observe(50, txdw) &&
              replacementRoute.Observe(51, txdw) &&
              replacementRoute.IsAccepted(true, true, 0, 0),
            "fresh-e1000-event-after-replacement-subscription-is-proven");

        var com1Interleaving = new ManagedE1000SyncTxProof();
        Check(com1Interleaving.Arm(60) &&
              !com1Interleaving.Observe(60, 0) &&
              !com1Interleaving.IsSatisfied &&
              com1Interleaving.Observe(61, txdw) &&
              com1Interleaving.IsAccepted(true, true, 0, 0),
            "com1-interleaving-does-not-count-as-e1000-txdw");

        var delayedDispatcher = new ManagedE1000SyncTxProof();
        Check(delayedDispatcher.Arm(70), "delayed-dispatch-proof-arms");
        bool delayedDdComplete = true;
        Check(!delayedDispatcher.IsAccepted(delayedDdComplete, false, 0, 0) &&
              delayedDispatcher.TxdwNotificationCount == 0,
            "delayed-managed-dispatch-remains-pending-after-hardware-dd");
        Check(delayedDispatcher.Observe(70, txdw) &&
              delayedDispatcher.IsAccepted(delayedDdComplete, true, 0, 0),
            "delayed-managed-dispatch-completes-bounded-notification-proof");

        var noNotification = new ManagedE1000SyncTxProof();
        Check(noNotification.Arm(80) &&
              !noNotification.IsAccepted(true, true, 0, 0),
            "valid-dd-without-new-notification-does-not-pass-routing-proof");
        Check(!noNotification.IsAccepted(true, false, 0, 0) &&
              !noNotification.IsAccepted(true, true, 1, 0) &&
              !noNotification.IsAccepted(true, true, 0, 1),
            "sync-proof-rejects-dispatch-rejection-and-live-async-ownership");
    }

    private static void Check(bool condition, string name)
    {
        s_checks++;
        if (!condition) throw new InvalidOperationException(name);
    }
}
