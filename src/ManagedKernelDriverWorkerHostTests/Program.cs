using System;
using System.Collections.Generic;
using GuideXOS.Net10.ManagedKernel;

internal static class Program
{
    private sealed class Consumer : IManagedDriverWorkerConsumer
    {
        private readonly uint _deviceId;
        private readonly ulong _subscriptionId;
        private ulong _lastSequence;
        internal readonly List<byte> Payloads = new List<byte>();

        internal Consumer(uint deviceId, ulong subscriptionId)
        {
            _deviceId = deviceId;
            _subscriptionId = subscriptionId;
        }

        public bool TryHandle(in ManagedDriverWorkItem item)
        {
            if (item.DeviceId != _deviceId ||
                item.SubscriptionId != _subscriptionId || item.Sequence == 0 ||
                (_lastSequence != 0 && item.Sequence != _lastSequence + 1))
            {
                return false;
            }
            _lastSequence = item.Sequence;
            Payloads.Add(item.Payload);
            return true;
        }
    }

    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private static ManagedDriverWorkItem Item(uint device, ulong subscription,
                                               ulong sequence, byte payload)
    {
        return new ManagedDriverWorkItem(device, subscription, sequence, payload);
    }

    private static void Main()
    {
        ManagedDriverWorkerModel worker = new ManagedDriverWorkerModel();
        Consumer first = new Consumer(1, 101);
        Consumer second = new Consumer(2, 202);

        Check(worker.RegisterConsumer(1, 101, first), "first route");
        Check(worker.RegisterConsumer(2, 202, second), "second route");
        Check(!worker.RegisterConsumer(3, 303, first), "route table bounded");
        Check(worker.Start(), "start");
        Check(!worker.Start(), "duplicate start rejected");

        for (byte index = 0; index != 6; ++index)
        {
            Check(worker.Enqueue(Item(1, 101, (ulong)index + 1,
                                       (byte)('A' + index))), "first enqueue");
        }
        Check(worker.Enqueue(Item(2, 202, 1, (byte)'x')), "second enqueue");
        Check(worker.WakeRequests == 1, "wake coalescing");
        Check(worker.QueueHighWater == 7, "high water");
        Check(worker.RunActivation(), "first bounded activation");
        Check(worker.DeliveredCount == 4 && worker.YieldCount == 1 &&
              worker.RescheduleCount == 1, "bounded yield");
        Check(worker.RunActivation(), "second activation");
        Check(worker.QueueDepth == 0 && worker.WakeRequests == 1,
              "pending cleared without duplicate wake");
        Check(first.Payloads.Count == 6 && second.Payloads.Count == 1 &&
              first.Payloads[0] == (byte)'A' && first.Payloads[5] == (byte)'F' &&
              second.Payloads[0] == (byte)'x', "two-consumer routing");

        Check(worker.Enqueue(Item(1, 101, 3, (byte)'s')), "stale enqueue");
        Check(worker.Enqueue(Item(99, 999, 1, (byte)'z')), "unknown route enqueue");
        Check(worker.RunActivation(), "invalid activation");
        Check(worker.RejectedCount == 2 && worker.DeliveredCount == 7,
              "stale and unknown records rejected");

        for (ulong sequence = 10; sequence != 18; ++sequence)
        {
            Check(worker.Enqueue(Item(2, 202, sequence, (byte)sequence)),
                  "queue fill");
        }
        Check(!worker.Enqueue(Item(2, 202, 18, 18)), "queue full drop");
        Check(worker.DropCount == 1 && worker.QueueHighWater == 8,
              "bounded queue drop and high water");
        Check(worker.RunActivation() && worker.RunActivation(),
              "drain filled queue in bounded batches");
        Check(worker.QueueDepth == 0 && worker.YieldCount == 2,
              "filled queue rescheduled");

        /* A malformed keyboard-side record must not poison the shared worker:
           the serial route remains usable on the next activation. */
        Check(worker.Enqueue(Item(1, 101, 7, (byte)'g')),
              "serial recovery enqueue");
        Check(worker.RunActivation() && worker.DeliveredCount == 8 &&
              first.Payloads.Count == 7 && first.Payloads[6] == (byte)'g',
              "serial route survives rejected input");

        /* A worker instance must be reusable after each auto-reset wake. This
           is intentionally separate from burst/coalescing coverage: every
           cycle returns to the sleeping state before the next signal. */
        ManagedDriverWorkerModel repeated = new ManagedDriverWorkerModel();
        Consumer repeatedConsumer = new Consumer(7, 707);
        Check(repeated.RegisterConsumer(7, 707, repeatedConsumer),
              "repeated-wake route");
        Check(repeated.Start(), "repeated-wake start");
        for (ulong sequence = 1; sequence != 4; ++sequence)
        {
            Check(repeated.Enqueue(Item(7, 707, sequence,
                                        (byte)('p' + sequence - 1))),
                  "repeated-wake enqueue");
            Check(repeated.WakeRequests == (uint)sequence,
                  "repeated-wake signal count");
            Check(repeated.RunActivation(), "repeated-wake dispatch");
            Check(repeated.IsSleeping && repeated.QueueDepth == 0,
                  "repeated-wake sleep re-arm");
        }
        Check(repeated.DispatchBatches == 3 &&
              repeated.DeliveredCount == 3 &&
              repeatedConsumer.Payloads.Count == 3,
              "repeated-wake same worker instance");

        Check(worker.BeginStop(), "begin stop");
        Check(!worker.BeginStop(), "duplicate stop rejected");
        Check(worker.CompleteStop(), "complete stop");
        Check(!worker.Enqueue(Item(1, 101, 7, 7)), "enqueue after stop");
        Check(!worker.RunActivation(), "activation after stop");
        Check(worker.Destroy(), "destroy");
        Check(!worker.Destroy() && !worker.CompleteStop(),
              "duplicate destroy/complete rejected");

        Console.WriteLine("MANAGED_KERNEL_DRIVER_WORKER_HOST_TESTS=PASSED");
        Console.WriteLine($"WAKE_REQUESTS={worker.WakeRequests}");
        Console.WriteLine($"DISPATCH_BATCHES={worker.DispatchBatches}");
        Console.WriteLine($"DELIVERED={worker.DeliveredCount}");
        Console.WriteLine($"REJECTED={worker.RejectedCount}");
        Console.WriteLine($"DROPPED={worker.DropCount}");
        Console.WriteLine($"YIELDS={worker.YieldCount}");
        Console.WriteLine($"REPEATED_WAKE_CYCLES={repeated.DispatchBatches}");
    }
}
