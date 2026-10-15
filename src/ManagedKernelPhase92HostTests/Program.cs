using System;
using System.Collections.Generic;
using GuideXOS.Net10.ManagedKernel;

internal static class Program
{
    private const uint Episode = 0x92;
    private const int LifecycleTarget = 10000;
    private const uint Seed = 0x92C0FFEE;
    private static int s_checks;

    private static int Main()
    {
        RunEndurance();
        RunCompletionOrderingChecks();
        RunEpisodeBoundaryChecks();
        Console.WriteLine("MANAGED_KERNEL_PHASE92_HOST_TESTS=PASS checks=" + s_checks);
        Console.WriteLine("PHASE92_HOST_SEED=0x" + Seed.ToString("X8"));
        Console.WriteLine("PHASE92_HOST_ASYNC_LIFECYCLES=" + LifecycleTarget);
        Console.WriteLine("PHASE92_HOST_REJECTED_CAPACITY_PROBES=2500");
        return 0;
    }

    private static void RunCompletionOrderingChecks()
    {
        var ledger = new ManagedE1000AsyncTxLedger();
        Check(ledger.StartEpisode(0x9204), "ordering-episode-starts");
        var descriptors = new byte[ManagedE1000Protocol.RingCount][];
        for (int index = 0; index != descriptors.Length; ++index)
            descriptors[index] = new byte[ManagedE1000Protocol.DescriptorSize];

        ulong first = PrepareHostedRequest(ledger, descriptors, 0);
        ulong second = PrepareHostedRequest(ledger, descriptors, 1);
        ulong third = PrepareHostedRequest(ledger, descriptors, 2);

        descriptors[2][12] = ManagedE1000Protocol.TxStatusDone;
        Check(ledger.Query(third) == ManagedE1000AsyncTxResult.Pending &&
              ledger.Outstanding == 3 && ledger.CompletedUnconsumed == 0,
            "dd-before-deferred-notification-remains-pending");
        int deferredScan = ScanDdCompleted(ledger, descriptors);
        Check(deferredScan == 1 &&
              ledger.Query(third) == ManagedE1000AsyncTxResult.Success &&
              ledger.OwnershipAt(2) == ManagedE1000AsyncTxOwnership.Completed,
            "deferred-notification-publishes-dd-authoritative-completion");
        Check(ledger.Consume(third) == ManagedE1000AsyncTxResult.Success &&
              ledger.Outstanding == 2 &&
              ledger.OwnershipAt(0) == ManagedE1000AsyncTxOwnership.HardwareOwned &&
              ledger.OwnershipAt(1) == ManagedE1000AsyncTxOwnership.HardwareOwned &&
              ledger.Query(first) == ManagedE1000AsyncTxResult.Pending &&
              ledger.Query(second) == ManagedE1000AsyncTxResult.Pending,
            "completion-consumed-while-other-requests-remain-hardware-owned");

        ulong fourth = PrepareHostedRequest(ledger, descriptors, 3);
        descriptors[1][12] = ManagedE1000Protocol.TxStatusDone;
        descriptors[3][12] = ManagedE1000Protocol.TxStatusDone;
        uint txdwEvents = 1;
        int coalescedScan = ScanDdCompleted(ledger, descriptors);
        Check(txdwEvents == 1 && coalescedScan == 2 &&
              ledger.Query(first) == ManagedE1000AsyncTxResult.Pending &&
              ledger.Query(second) == ManagedE1000AsyncTxResult.Success &&
              ledger.Query(fourth) == ManagedE1000AsyncTxResult.Success &&
              ledger.HardwareOwned == 1 && ledger.CompletedUnconsumed == 2,
            "one-coalesced-txdw-scan-processes-multiple-dd-descriptors");
        Check(ledger.Consume(fourth) == ManagedE1000AsyncTxResult.Success &&
              ledger.Consume(second) == ManagedE1000AsyncTxResult.Success &&
              ledger.Outstanding == 1 &&
              ledger.OwnershipAt(0) == ManagedE1000AsyncTxOwnership.HardwareOwned,
            "out-of-order-completion-consumption-preserves-pending-request");

        descriptors[0][12] = ManagedE1000Protocol.TxStatusDone;
        ++txdwEvents;
        Check(ScanDdCompleted(ledger, descriptors) == 1 && txdwEvents == 2 &&
              ledger.Query(first) == ManagedE1000AsyncTxResult.Success &&
              ledger.Consume(first) == ManagedE1000AsyncTxResult.Success &&
              ledger.Outstanding == 0 && ledger.CompletedUnconsumed == 0,
            "later-dd-completion-drains-after-earlier-consumption");
    }

    private static ulong PrepareHostedRequest(ManagedE1000AsyncTxLedger ledger,
        byte[][] descriptors, uint index)
    {
        Check(ledger.IsFree(index), "ordering-descriptor-slot-free-" + index);
        Check(ledger.TryPrepare(index, out ulong requestId) ==
              ManagedE1000AsyncTxSubmitResult.Accepted,
            "ordering-request-prepared-" + index);
        Span<byte> descriptor = stackalloc byte[
            (int)ManagedE1000Protocol.DescriptorSize];
        Check(ManagedE1000Protocol.TryBuildTxDescriptor(descriptor,
                  0x200000UL + (ulong)index * ManagedE1000Protocol.PacketBufferSize,
                  (ushort)ManagedE1000Protocol.MinimumEthernetFrameLength),
            "ordering-production-descriptor-built-" + index);
        descriptor.CopyTo(descriptors[index]);
        Check(ledger.MarkHardwareOwned(index),
            "ordering-descriptor-marked-hardware-owned-" + index);
        return requestId;
    }

    private static void RunEndurance()
    {
        var ledger = new ManagedE1000AsyncTxLedger();
        Check(ledger.StartEpisode(Episode), "endurance-episode-starts");
        var descriptorRing = new byte[ManagedE1000Protocol.RingCount][];
        for (int index = 0; index != descriptorRing.Length; ++index)
            descriptorRing[index] = new byte[ManagedE1000Protocol.DescriptorSize];

        var random = new FixedRandom(Seed);
        var uniqueIds = new HashSet<ulong>();
        var previousIdBySlot = new ulong[ManagedE1000Protocol.RingCount];
        uint txIndex = 0;
        int submitted = 0;
        int completed = 0;
        int consumed = 0;
        int descriptorWraps = 0;
        uint previousIndex = 0;

        while (submitted < LifecycleTarget)
        {
            int occupancyTarget = 1 + (submitted / (int)ManagedE1000AsyncTxLedger.Capacity % 4);
            ulong[] ids = new ulong[ManagedE1000AsyncTxLedger.Capacity];
            uint[] indices = new uint[ManagedE1000AsyncTxLedger.Capacity];
            int acceptedThisCycle = 0;

            for (int request = 0; request < occupancyTarget; ++request)
            {
                Submit(ledger, descriptorRing, ref txIndex, ids, indices,
                    ref acceptedThisCycle, uniqueIds, previousIdBySlot,
                    ref submitted, ref descriptorWraps, ref previousIndex);
            }

            Check(ledger.Outstanding == (uint)occupancyTarget,
                "partial-occupancy-accounting-" + submitted);
            Check(ledger.Outstanding <= ManagedE1000AsyncTxLedger.Capacity,
                "partial-occupancy-within-capacity-" + submitted);

            int earlyComplete = random.Next(acceptedThisCycle + 1);
            for (int n = 0; n < earlyComplete; ++n)
            {
                int request = random.Next(acceptedThisCycle);
                if (indices[request] == uint.MaxValue) continue;
                uint index = indices[request];
                if (ledger.OwnershipAt(index) != ManagedE1000AsyncTxOwnership.HardwareOwned)
                    continue;
                descriptorRing[index][12] = ManagedE1000Protocol.TxStatusDone;
            }

            Check(ledger.Outstanding == (uint)acceptedThisCycle &&
                  ledger.CompletedUnconsumed == 0,
                "dd-before-dispatch-remains-hardware-owned-" + submitted);
            int firstScan = ScanDdCompleted(ledger, descriptorRing);
            Check(firstScan <= earlyComplete,
                "bounded-scan-does-not-complete-non-dd-descriptors-" + submitted);
            completed += firstScan;

            for (int request = 0; request < acceptedThisCycle; ++request)
            {
                if (indices[request] == uint.MaxValue) continue;
                uint index = indices[request];
                ManagedE1000AsyncTxResult result = ledger.Query(ids[request]);
                if (ledger.OwnershipAt(index) == ManagedE1000AsyncTxOwnership.Completed)
                    Check(result == ManagedE1000AsyncTxResult.Success,
                        "dd-published-as-success-" + submitted + "-" + request);
                else
                    Check(result == ManagedE1000AsyncTxResult.Pending,
                        "not-yet-dd-remains-pending-" + submitted + "-" + request);
            }

            /* A completed request remains admitted until its completion is
               consumed. Fill free logical capacity without replacing any of
               the original four identities. */
            while (acceptedThisCycle < ManagedE1000AsyncTxLedger.Capacity &&
                   submitted < LifecycleTarget)
            {
                Submit(ledger, descriptorRing, ref txIndex, ids, indices,
                    ref acceptedThisCycle, uniqueIds, previousIdBySlot,
                    ref submitted, ref descriptorWraps, ref previousIndex);
            }

            Check(ledger.Outstanding == (uint)acceptedThisCycle,
                "completed-unconsumed-counts-as-outstanding-" + submitted);
            if (acceptedThisCycle == ManagedE1000AsyncTxLedger.Capacity)
            {
                uint savedTxIndex = txIndex;
                uint savedOutstanding = ledger.Outstanding;
                uint savedCompleted = ledger.CompletedUnconsumed;
                uint savedHardwareOwned = ledger.HardwareOwned;
                var savedOwnership = SnapshotOwnership(ledger);
                var savedDescriptors = SnapshotDescriptorStatus(descriptorRing);
                Check(ledger.TryPrepare(txIndex, out ulong rejectedId) ==
                      ManagedE1000AsyncTxSubmitResult.Busy && rejectedId == 0,
                    "fifth-request-is-busy-" + submitted);
                Check(txIndex == savedTxIndex && ledger.Outstanding == savedOutstanding &&
                      ledger.CompletedUnconsumed == savedCompleted &&
                      ledger.HardwareOwned == savedHardwareOwned &&
                      Same(savedOwnership, SnapshotOwnership(ledger)) &&
                      Same(savedDescriptors, SnapshotDescriptorStatus(descriptorRing)),
                    "busy-probe-does-not-mutate-ledger-descriptors-or-tail-" + submitted);
            }

            for (int request = 0; request < acceptedThisCycle; ++request)
            {
                if (indices[request] == uint.MaxValue) continue;
                uint index = indices[request];
                if (ledger.OwnershipAt(index) == ManagedE1000AsyncTxOwnership.HardwareOwned)
                    descriptorRing[index][12] = ManagedE1000Protocol.TxStatusDone;
            }
            int secondScan = ScanDdCompleted(ledger, descriptorRing);
            completed += secondScan;
            Check(ledger.CompletedUnconsumed == (uint)acceptedThisCycle,
                "all-dd-complete-before-consumption-" + submitted);

            for (int n = acceptedThisCycle; n > 0; --n)
            {
                int selected = random.Next(n);
                int request = selected;
                ulong selectedId = ids[request];
                uint selectedIndex = indices[request];
                Check(ledger.OwnershipAt(selectedIndex) ==
                      ManagedE1000AsyncTxOwnership.Completed,
                    "completion-is-not-reused-before-consume-" + submitted + "-" + request);
                Check(ledger.Query(selectedId) == ManagedE1000AsyncTxResult.Success &&
                      ledger.Query(selectedId) == ManagedE1000AsyncTxResult.Success,
                    "repeat-query-is-stable-and-exposed-once-" + submitted + "-" + request);
                Check(ledger.Consume(selectedId) == ManagedE1000AsyncTxResult.Success,
                    "completion-consumed-once-" + submitted + "-" + request);
                consumed++;
                Check(ledger.Consume(selectedId) == ManagedE1000AsyncTxResult.InvalidOrStale,
                    "second-consume-is-stale-" + submitted + "-" + request);
                Check(ledger.Outstanding == (uint)(n - 1),
                    "capacity-reclaimed-only-after-consume-" + submitted + "-" + request);
                Check(ledger.IsFree(selectedIndex),
                    "consumed-descriptor-ledger-slot-is-reusable-" + submitted + "-" + request);

                ids[request] = ids[n - 1];
                indices[request] = indices[n - 1];
                ids[n - 1] = 0;
                indices[n - 1] = uint.MaxValue;
            }

            Check(ledger.Outstanding == 0 && ledger.HardwareOwned == 0 &&
                  ledger.CompletedUnconsumed == 0,
                "cycle-drains-to-zero-" + submitted);
        }

        Check(submitted == LifecycleTarget && completed == LifecycleTarget &&
              consumed == LifecycleTarget && uniqueIds.Count == LifecycleTarget,
            "10k-submit-complete-consume-reconciled");
        Check(ledger.Outstanding == 0 && ledger.ExposedCompletions == LifecycleTarget &&
              ledger.ConsumedCompletions == LifecycleTarget,
            "10k-ledger-exposed-and-consumed-counts-reconcile");
        Check(descriptorWraps >= 600,
            "physical-ring-wraparound-repeatedly-exercised");
        for (uint index = 0; index != ManagedE1000Protocol.RingCount; ++index)
            Check(ledger.OwnershipAt(index) == ManagedE1000AsyncTxOwnership.Free,
                "final-ledger-slot-free-" + index);
    }

    private static void Submit(ManagedE1000AsyncTxLedger ledger,
        byte[][] descriptors, ref uint txIndex, ulong[] ids, uint[] indices,
        ref int acceptedThisCycle, HashSet<ulong> uniqueIds,
        ulong[] previousIdBySlot, ref int submitted, ref int descriptorWraps,
        ref uint previousIndex)
    {
        uint index = txIndex;
        Check(ledger.IsFree(index), "next-descriptor-ledger-slot-is-free-" + submitted);
        Check(ledger.TryPrepare(index, out ulong requestId) ==
              ManagedE1000AsyncTxSubmitResult.Accepted,
            "submit-reserves-production-ledger-slot-" + submitted);
        Span<byte> descriptor = stackalloc byte[(int)ManagedE1000Protocol.DescriptorSize];
        Check(ManagedE1000Protocol.TryBuildTxDescriptor(descriptor,
                  0x100000UL + (ulong)index * ManagedE1000Protocol.PacketBufferSize,
                  (ushort)ManagedE1000Protocol.MinimumEthernetFrameLength),
            "production-descriptor-prepared-" + submitted);
        descriptor.CopyTo(descriptors[index]);
        Check(descriptors[index][12] == 0,
            "new-descriptor-clears-prior-dd-status-" + submitted);
        Check(ledger.MarkHardwareOwned(index),
            "request-owned-before-tail-advance-" + submitted);
        if (previousIdBySlot[index] != 0)
        {
            uint beforeOutstanding = ledger.Outstanding;
            uint beforeConsumed = ledger.ConsumedCompletions;
            byte beforeStatus = descriptors[index][12];
            Check(ledger.Query(previousIdBySlot[index]) ==
                  ManagedE1000AsyncTxResult.InvalidOrStale &&
                  ledger.Consume(previousIdBySlot[index]) ==
                  ManagedE1000AsyncTxResult.InvalidOrStale,
                "same-slot-old-id-rejected-after-generation-reuse-" + submitted);
            Check(ledger.Outstanding == beforeOutstanding &&
                  ledger.ConsumedCompletions == beforeConsumed &&
                  descriptors[index][12] == beforeStatus &&
                  ledger.OwnershipAt(index) == ManagedE1000AsyncTxOwnership.HardwareOwned,
                "stale-id-query-consume-cannot-mutate-active-request-" + submitted);
        }
        Check(uniqueIds.Add(requestId), "request-id-unique-" + submitted);
        previousIdBySlot[index] = requestId;
        ids[acceptedThisCycle] = requestId;
        indices[acceptedThisCycle] = index;
        acceptedThisCycle++;
        submitted++;
        if (index < previousIndex) descriptorWraps++;
        previousIndex = index;
        Check(ManagedE1000Protocol.TryAdvanceRing(txIndex,
                  ManagedE1000Protocol.RingCount, out uint nextIndex),
            "ring-cursor-advance-valid-" + submitted);
        txIndex = nextIndex;
    }

    private static int ScanDdCompleted(ManagedE1000AsyncTxLedger ledger,
        byte[][] descriptors)
    {
        int completed = 0;
        for (uint index = 0; index < ManagedE1000Protocol.RingCount; ++index)
        {
            if (ledger.OwnershipAt(index) != ManagedE1000AsyncTxOwnership.HardwareOwned)
                continue;
            if (ledger.CompleteFromDescriptor(index, descriptors[index][12]))
                completed++;
        }
        return completed;
    }

    private static void RunEpisodeBoundaryChecks()
    {
        var ledger = new ManagedE1000AsyncTxLedger();
        Check(ledger.StartEpisode(0x9201), "episode-boundary-first-episode");
        Check(ledger.TryPrepare(4, out ulong oldId) ==
              ManagedE1000AsyncTxSubmitResult.Accepted && ledger.MarkHardwareOwned(4),
            "episode-boundary-old-request-hardware-owned");
        Check(!ledger.CompleteFromDescriptor(4, 0) &&
              ledger.Query(oldId) == ManagedE1000AsyncTxResult.Pending,
            "missing-dd-cannot-complete-request");
        ledger.BeginStopping();
        Check(ledger.TryPrepare(5, out _) == ManagedE1000AsyncTxSubmitResult.Unavailable,
            "stopping-closes-admission");
        Check(ledger.InvalidateAfterDmaQuiesced() && ledger.Outstanding == 0,
            "episode-boundary-invalidates-only-after-quiescence");
        Check(ledger.StartEpisode(0x9202), "episode-boundary-second-episode");
        Check(ledger.TryPrepare(4, out ulong newId) ==
              ManagedE1000AsyncTxSubmitResult.Accepted && ledger.MarkHardwareOwned(4) &&
              newId != oldId,
            "same-descriptor-new-episode-has-distinct-identity");
        uint outstandingBefore = ledger.Outstanding;
        uint exposedBefore = ledger.ExposedCompletions;
        uint consumedBefore = ledger.ConsumedCompletions;
        Check(ledger.Query(oldId) == ManagedE1000AsyncTxResult.InvalidOrStale &&
              ledger.Consume(oldId) == ManagedE1000AsyncTxResult.InvalidOrStale,
            "old-episode-id-rejected");
        Check(ledger.Outstanding == outstandingBefore &&
              ledger.ExposedCompletions == exposedBefore &&
              ledger.ConsumedCompletions == consumedBefore &&
              ledger.OwnershipAt(4) == ManagedE1000AsyncTxOwnership.HardwareOwned &&
              !ledger.CompleteFromDescriptor(4, 0),
            "old-episode-identity-cannot-mutate-new-ownership");
        Check(ledger.CompleteFromDescriptor(4, ManagedE1000Protocol.TxStatusDone) &&
              ledger.Query(newId) == ManagedE1000AsyncTxResult.Success &&
              ledger.Consume(newId) == ManagedE1000AsyncTxResult.Success &&
              ledger.Outstanding == 0,
            "new-episode-dd-query-consume-drains");

        Check(ledger.TryPrepare(ManagedE1000Protocol.RingCount, out _) ==
              ManagedE1000AsyncTxSubmitResult.InvalidArgument,
            "out-of-ring-descriptor-rejected");
        ledger.BeginStopping();
        Check(ledger.TryPrepare(6, out _) == ManagedE1000AsyncTxSubmitResult.Unavailable,
            "submission-unavailable-while-stopped");

        var hardwareError = new ManagedE1000AsyncTxLedger();
        Check(hardwareError.StartEpisode(0x9203) &&
              hardwareError.TryPrepare(0, out ulong errorId) ==
                  ManagedE1000AsyncTxSubmitResult.Accepted &&
              hardwareError.MarkHardwareOwned(0) &&
              hardwareError.CompleteFromDescriptor(0,
                  (byte)(ManagedE1000Protocol.TxStatusDone | 0x02)) &&
              hardwareError.Query(errorId) == ManagedE1000AsyncTxResult.HardwareError &&
              hardwareError.Consume(errorId) == ManagedE1000AsyncTxResult.HardwareError &&
              hardwareError.Outstanding == 0,
            "dd-error-result-is-consumable-without-leaking-slot");
    }

    private static uint[] SnapshotOwnership(ManagedE1000AsyncTxLedger ledger)
    {
        var snapshot = new uint[ManagedE1000Protocol.RingCount];
        for (uint index = 0; index != ManagedE1000Protocol.RingCount; ++index)
            snapshot[index] = (uint)ledger.OwnershipAt(index);
        return snapshot;
    }

    private static byte[] SnapshotDescriptorStatus(byte[][] descriptors)
    {
        var snapshot = new byte[descriptors.Length];
        for (int index = 0; index != descriptors.Length; ++index)
            snapshot[index] = descriptors[index][12];
        return snapshot;
    }

    private static bool Same(uint[] left, uint[] right)
    {
        if (left.Length != right.Length) return false;
        for (int index = 0; index != left.Length; ++index)
            if (left[index] != right[index]) return false;
        return true;
    }

    private static bool Same(byte[] left, byte[] right)
    {
        if (left.Length != right.Length) return false;
        for (int index = 0; index != left.Length; ++index)
            if (left[index] != right[index]) return false;
        return true;
    }

    private static void Check(bool condition, string name)
    {
        s_checks++;
        if (!condition) throw new InvalidOperationException(name);
    }

    private struct FixedRandom
    {
        private uint _state;

        internal FixedRandom(uint seed) => _state = seed;

        internal int Next(int exclusiveMaximum)
        {
            if (exclusiveMaximum <= 0) return 0;
            uint value = _state;
            value ^= value << 13;
            value ^= value >> 17;
            value ^= value << 5;
            _state = value == 0 ? 0x9E3779B9U : value;
            return (int)(_state % (uint)exclusiveMaximum);
        }
    }
}
