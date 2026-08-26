using System;
using GuideXOS.Net10.ManagedKernel;

internal static class Program
{
    private static int s_cases;

    private static int Main()
    {
        try
        {
            IdentityTests();
            CapabilityChainTests();
            FeatureAndStatusTests();
            VirtqueueTests();
            ProviderTests();
            LifecycleAndNegativeTests();
            Console.WriteLine($"MANAGED_KERNEL_PHASE26_HOST_TESTS_PASS cases={s_cases}");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"MANAGED_KERNEL_PHASE26_HOST_TESTS_FAIL {exception.Message}");
            return 1;
        }
    }

    private static void IdentityTests()
    {
        Check(IsModern(ManagedVirtioRngProtocol.VirtioVendorId,
                        ManagedVirtioRngProtocol.ModernRngDeviceId),
            "modern-identity-accepted");
        Check(!IsModern(ManagedVirtioRngProtocol.VirtioVendorId,
                        ManagedVirtioRngProtocol.TransitionalRngDeviceId),
            "transitional-identity-rejected");
        Check(!IsModern(0x1234, ManagedVirtioRngProtocol.ModernRngDeviceId),
            "foreign-vendor-rejected");
        Check(ManagedVirtioRngProtocol.PciOwnerId == 0x1AF41044,
            "modern-owner-id-stable");
        Check(ManagedVirtioRngProtocol.DriverId != 0,
            "driver-id-nonzero");
        Check(ManagedVirtioRngProtocol.QueueSize == 8 &&
              ManagedVirtioRngProtocol.MaximumRequestBytes == 1024,
            "bounded-queue-and-request");
    }

    private static void CapabilityChainTests()
    {
        byte[] valid = BuildConfiguration();
        Check(ManagedVirtioRngProtocol.TryParseCapabilities(valid,
            out ManagedVirtioPciCapabilities capabilities),
            "capabilities-valid");
        Check(capabilities.CommonBar == 1 && capabilities.CommonOffset == 0x1000 &&
              capabilities.CommonLength == 0x100 && capabilities.NotifyBar == 4 &&
              capabilities.NotifyOffset == 0x2000 && capabilities.NotifyMultiplier == 4,
            "capabilities-fields");

        byte[] missingNotify = BuildConfiguration();
        missingNotify[0x41] = 0;
        Check(!ManagedVirtioRngProtocol.TryParseCapabilities(missingNotify, out _),
            "missing-notify-rejected");

        byte[] duplicateCommon = BuildConfiguration();
        AddCapability(duplicateCommon, 0x60, 0, 0x3A, 1, 1, 0x3000, 0x100, 0);
        duplicateCommon[0x50] = 0x60;
        Check(!ManagedVirtioRngProtocol.TryParseCapabilities(duplicateCommon, out _),
            "duplicate-common-rejected");

        byte[] malformedLength = BuildConfiguration();
        malformedLength[0x42] = 15;
        Check(!ManagedVirtioRngProtocol.TryParseCapabilities(malformedLength, out _),
            "short-common-rejected");

        byte[] badBar = BuildConfiguration();
        badBar[0x44] = 6;
        Check(!ManagedVirtioRngProtocol.TryParseCapabilities(badBar, out _),
            "bar-out-of-range-rejected");

        byte[] badNotifyMultiplier = BuildConfiguration();
        Write32(badNotifyMultiplier, 0x60, 0);
        Check(!ManagedVirtioRngProtocol.TryParseCapabilities(
                badNotifyMultiplier, out _), "notify-multiplier-zero-rejected");

        byte[] unknownType = BuildConfiguration();
        unknownType[0x53] = 0x7F;
        Check(!ManagedVirtioRngProtocol.TryParseCapabilities(unknownType, out _),
            "unknown-virtio-capability-rejected");

        byte[] cycle = BuildConfiguration();
        cycle[0x51] = 0x40;
        Check(!ManagedVirtioRngProtocol.TryParseCapabilities(cycle, out _),
            "capability-cycle-rejected");

        Check(!ManagedVirtioRngProtocol.TryParseCapabilities(new byte[63], out _),
            "short-config-rejected");
    }

    private static void FeatureAndStatusTests()
    {
        VirtioLifecycle lifecycle = new(0);
        Check(lifecycle.Reset() && lifecycle.Status == 0,
            "status-reset");
        Check(lifecycle.Set(1) && lifecycle.Set(2) &&
              (lifecycle.Status & 3) == 3, "ack-driver-sequence");
        Check(lifecycle.NegotiateZeroFeatures() &&
              (lifecycle.Status & 8) != 0, "zero-feature-negotiation");
        Check(lifecycle.EnableQueue(8) && lifecycle.Set(4) && lifecycle.QueueEnabled &&
              (lifecycle.Status & 4) != 0, "driver-ok-after-queue");
        Check(!lifecycle.EnableQueue(4), "undersized-queue-rejected");

        VirtioLifecycle unsupported = new(0x0000_0002);
        Check(!unsupported.NegotiateZeroFeatures(),
            "unsupported-feature-rejected");
        Check((unsupported.Status & 128) != 0, "feature-failure-status");
        Check(!unsupported.Set(4), "failed-device-stays-failed");

        VirtioLifecycle repeat = new(0);
        Check(repeat.Reset() && repeat.Set(1) && repeat.Set(2) &&
              repeat.NegotiateZeroFeatures() && repeat.EnableQueue(8) &&
              repeat.Set(4), "first-lifecycle");
        Check(repeat.Reset() && repeat.Status == 0 && !repeat.QueueEnabled,
            "reset-clears-queue");
        Check(repeat.Set(1) && repeat.Set(2) && repeat.NegotiateZeroFeatures() &&
              repeat.EnableQueue(8) && repeat.Set(4), "reinit-lifecycle");
    }

    private static void VirtqueueTests()
    {
        VirtqueueModel queue = new(8);
        Check(queue.Submit(64) && queue.AvailableIndex == 1,
            "submit-advances-available");
        Check(queue.Complete(0, 64, out byte[] first) && first.Length == 64,
            "used-completion-exact");
        Check(queue.Submit(1024) && queue.Complete(0, 512, out byte[] partial) &&
              partial.Length == 512, "short-used-completion-accepted");
        Check(!queue.Submit(0) && !queue.Submit(1025),
            "zero-and-oversize-rejected");
        Check(!queue.Complete(1, 10, out _), "wrong-descriptor-rejected");
        Check(!queue.Complete(0, 2048, out _), "impossible-length-rejected");
        Check(!queue.Complete(0, 0, out _), "zero-length-rejected");
        VirtqueueModel timeout = new(8);
        Check(timeout.Submit(32) && timeout.Timeout() && !timeout.Healthy,
            "poll-timeout-fails-closed");
        Check(queue.Reset() && queue.Healthy && queue.AvailableIndex == 0 &&
              queue.UsedIndex == 0, "queue-reset-recovery");

        VirtqueueModel wrap = new(8);
        for (int index = 0; index != 8; ++index)
        {
            Check(wrap.Submit(16), "ring-submit-" + index);
            Check(wrap.Complete(0, 16, out _), "ring-complete-" + index);
        }
        Check(wrap.AvailableIndex == 8 && wrap.UsedIndex == 8,
            "ring-indexes-wrap-boundary");
        Check(wrap.Submit(32) && wrap.Complete(0, 32, out _),
            "ring-reuse-after-wrap");
    }

    private static void ProviderTests()
    {
        FixtureEntropy hardware = new(true, true, 0x11);
        ManagedEntropyService service = new(hardware);
        ManagedSecureRandom random = new(service);
        byte[] sample = new byte[64];
        Check(random.TryFill(sample) && service.LastProvider ==
              ManagedEntropyProviderKind.Hardware && sample[0] == 0x11,
            "hardware-provider-first");

        hardware.Succeed = false;
        ManagedVirtioRngDriver virtio = ManagedVirtioRngDriver.Create();
        virtio.Healthy = true;
        service.AttachVirtioRng(virtio);
        Array.Clear(sample);
        Check(random.TryFill(sample) && service.LastProvider ==
              ManagedEntropyProviderKind.VirtioRng && sample[0] == 0xA0,
            "virtio-fallback-provider");
        Check(service.IsAvailable && service.HasVirtioRng,
            "virtio-availability");

        virtio.Healthy = false;
        Array.Clear(sample);
        Check(!random.TryFill(sample) && service.LastProvider ==
              ManagedEntropyProviderKind.None && AllZero(sample),
            "all-provider-failure-zero-output");
        Check(!random.TryFill(new byte[1025]), "maximum-fill-enforced");
        Check(random.TryFill(Span<byte>.Empty) == false,
            "empty-fill-requires-provider");

        FixtureEntropy partial = new(true, true, 0x22) { MaxBytes = 32 };
        ManagedSecureRandom partialRandom = new(partial);
        byte[] bounded = new byte[32];
        Check(partialRandom.TryFill(bounded) && partial.LastRequest == 32,
            "provider-contract-full-request");
        Check(partial.Requests == 1, "provider-request-count-bounded");
    }

    private static void LifecycleAndNegativeTests()
    {
        ManagedVirtioRngDriver driver = ManagedVirtioRngDriver.Create();
        driver.Healthy = true;
        ManagedEntropyService service = new(new FixtureEntropy(false, false, 0));
        service.AttachVirtioRng(driver);
        ManagedSecureRandom random = new(service);
        byte[] buffer = new byte[16];
        Check(random.TryFill(buffer), "reinit-provider-fill");
        GC.Collect();
        Check(random.TryFill(buffer) && driver.FillCount == 2,
            "gc-provider-survival");
        service.DetachVirtioRng(driver);
        Array.Clear(buffer);
        Check(!random.TryFill(buffer) && AllZero(buffer),
            "detach-fails-closed");

        ManagedVirtioRngDriver failed = ManagedVirtioRngDriver.Create();
        failed.Healthy = false;
        service.AttachVirtioRng(failed);
        Check(!service.IsAvailable && !random.TryFill(buffer),
            "failed-driver-not-published");
        failed.Healthy = true;
        Check(random.TryFill(buffer), "recovery-after-failed-start");
        service.DetachVirtioRng(failed);
        Check(!service.HasVirtioRng, "teardown-releases-provider");
        Check(ManagedVirtioRngProtocol.EntropyBufferBytes >=
              ManagedVirtioRngProtocol.MaximumRequestBytes,
            "dma-buffer-covers-request-bound");
        Check(ManagedVirtioRngProtocol.PollLimit >= 1000,
            "poll-bound-is-finite-and-nontrivial");
    }

    private static byte[] BuildConfiguration()
    {
        byte[] configuration = new byte[256];
        configuration[0x34] = 0x40;
        AddCapability(configuration, 0x40, 0x50, 0x3A, 1, 1,
                      0x1000, 0x100, 0);
        AddCapability(configuration, 0x50, 0, 20, 2, 4,
                      0x2000, 0x100, 4);
        return configuration;
    }

    private static void AddCapability(byte[] bytes, int offset, byte next,
                                      byte length, byte type, byte bar,
                                      uint capabilityOffset, uint capabilityLength,
                                      uint multiplier)
    {
        bytes[offset] = ManagedVirtioRngProtocol.PciCapabilityVendorSpecific;
        bytes[offset + 1] = next;
        bytes[offset + 2] = length;
        bytes[offset + 3] = type;
        bytes[offset + 4] = bar;
        Write32(bytes, offset + 8, capabilityOffset);
        Write32(bytes, offset + 12, capabilityLength);
        if (length >= 20) Write32(bytes, offset + 16, multiplier);
    }

    private static void Write32(byte[] bytes, int offset, uint value)
    {
        bytes[offset] = (byte)value;
        bytes[offset + 1] = (byte)(value >> 8);
        bytes[offset + 2] = (byte)(value >> 16);
        bytes[offset + 3] = (byte)(value >> 24);
    }

    private static bool IsModern(ushort vendor, ushort device) =>
        vendor == ManagedVirtioRngProtocol.VirtioVendorId &&
        device == ManagedVirtioRngProtocol.ModernRngDeviceId;

    private static bool AllZero(ReadOnlySpan<byte> bytes)
    {
        foreach (byte value in bytes) if (value != 0) return false;
        return true;
    }

    private static void Check(bool condition, string name)
    {
        s_cases++;
        if (!condition) throw new InvalidOperationException(name);
    }

    private sealed class FixtureEntropy : IManagedEntropyProvider
    {
        private readonly byte _seed;
        internal bool Succeed { get; set; }
        internal int MaxBytes { get; set; } = int.MaxValue;
        internal int Requests { get; private set; }
        internal int LastRequest { get; private set; }

        internal FixtureEntropy(bool available, bool succeed, byte seed)
        {
            IsAvailable = available;
            Succeed = succeed;
            _seed = seed;
        }

        public bool IsAvailable { get; private set; }

        public bool TryFill(Span<byte> destination)
        {
            Requests++;
            LastRequest = destination.Length;
            if (!IsAvailable || !Succeed || destination.Length > MaxBytes)
                return false;
            for (int index = 0; index != destination.Length; ++index)
                destination[index] = (byte)(_seed + index);
            return true;
        }
    }

    private sealed class VirtioLifecycle
    {
        private readonly uint _deviceFeatures;
        internal byte Status { get; private set; }
        internal bool QueueEnabled { get; private set; }

        internal VirtioLifecycle(uint deviceFeatures) => _deviceFeatures = deviceFeatures;

        internal bool Reset()
        {
            Status = 0;
            QueueEnabled = false;
            return true;
        }

        internal bool Set(byte bits)
        {
            if ((Status & 128) != 0 || (bits == 4 && !QueueEnabled)) return false;
            Status |= bits;
            return true;
        }

        internal bool NegotiateZeroFeatures()
        {
            if ((Status & 3) != 3 || _deviceFeatures != 0)
            {
                Status |= 128;
                return false;
            }
            Status |= 8;
            return true;
        }

        internal bool EnableQueue(int size)
        {
            if ((Status & 8) == 0 || size < 8) return false;
            QueueEnabled = true;
            return true;
        }
    }

    private sealed class VirtqueueModel
    {
        private readonly int _queueSize;
        private int _pending;
        internal ushort AvailableIndex { get; private set; }
        internal ushort UsedIndex { get; private set; }
        internal bool Healthy { get; private set; } = true;

        internal VirtqueueModel(int queueSize) => _queueSize = queueSize;

        internal bool Submit(int length)
        {
            if (!Healthy || length <= 0 || length >
                    ManagedVirtioRngProtocol.MaximumRequestBytes || _pending != 0)
                return false;
            _pending = length;
            AvailableIndex++;
            return true;
        }

        internal bool Complete(int descriptorId, int length, out byte[] result)
        {
            result = Array.Empty<byte>();
            if (!Healthy || _pending == 0 || descriptorId != 0 ||
                length <= 0 || length > _pending) return Fail();
            _pending = 0;
            UsedIndex++;
            result = new byte[length];
            return true;
        }

        internal bool Timeout()
        {
            if (_pending == 0) return false;
            _pending = 0;
            Healthy = false;
            return true;
        }

        internal bool Reset()
        {
            _pending = 0;
            AvailableIndex = 0;
            UsedIndex = 0;
            Healthy = true;
            return true;
        }

        private bool Fail()
        {
            Healthy = false;
            return false;
        }
    }
}
