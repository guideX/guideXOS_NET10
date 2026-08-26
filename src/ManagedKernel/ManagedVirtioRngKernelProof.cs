using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace GuideXOS.Net10.ManagedKernel;

internal static unsafe class ManagedVirtioRngKernelProof
{
    private static int s_run;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void CollectWithRoots(ManagedEntropyService entropy,
                                         ManagedSecureRandom random)
    {
        GC.KeepAlive(entropy);
        GC.KeepAlive(random);
        GC.Collect();
        GC.KeepAlive(entropy);
        GC.KeepAlive(random);
    }

    [UnmanagedCallersOnly(EntryPoint = "GxManagedKernelRunPhase26")]
    internal static uint Run()
    {
        if (!ManagedKernelContract.IsStarted || s_run != 0 ||
            !ManagedKernelContract.DeviceResourcesInstalled ||
            !ManagedKernelContract.DmaServicesInstalled)
            return ManagedKernelContract.InvalidState;

        ManagedEntropyService? entropy = ManagedKernelContract.EntropyService;
        ManagedSecureRandom? random = ManagedKernelContract.SecureRandom;
        ManagedVirtioRngDriver? driverCandidate = ManagedVirtioRngDriver.TryCreate();
        if (!driverCandidate.HasValue)
        {
            if (ManagedVirtioRngDriver.LastProbeFoundDevice)
                return ManagedKernelContract.InvalidState;
            Span<byte> unavailable = stackalloc byte[32];
            unavailable.Clear();
            bool fabricated = random != null && random.TryFill(unavailable);
            if (fabricated || (random != null && random.IsAvailable) ||
                !KernelLog.Write("GXOS_NET10:MANAGED_ENTROPY_RDSEED_UNAVAILABLE=1\r\n"u8) ||
                !KernelLog.Write("GXOS_NET10:MANAGED_ENTROPY_RDRAND_UNAVAILABLE=1\r\n"u8) ||
                !KernelLog.Write("GXOS_NET10:MANAGED_ENTROPY_VIRTIO_RNG_UNAVAILABLE=1\r\n"u8) ||
                !KernelLog.Write("GXOS_NET10:MANAGED_SECURE_RANDOM_NO_PROVIDER_FAIL_CLOSED_PASS\r\n"u8) ||
                !KernelLog.Write("GXOS_NET10:MANAGED_SECURE_RANDOM_NO_TIMING_FALLBACK=1\r\n"u8) ||
                unavailable[0] != 0 || unavailable[31] != 0)
                return ManagedKernelContract.InvalidState;
            s_run = 1;
            return KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_PHASE26_PASS\r\n"u8)
                ? ManagedKernelContract.ManagedOk : ManagedKernelContract.InvalidState;
        }

        ManagedVirtioRngDriver driver = driverCandidate.Value;
        if (entropy == null || random == null)
        {
            entropy = new ManagedEntropyService(
                ManagedKernelContract.EntropyFillAddress,
                ManagedKernelContract.EntropyCapabilities,
                ManagedKernelContract.EntropyMaxBytesPerFill);
            random = new ManagedSecureRandom(entropy);
        }
        if (random == null) return ManagedKernelContract.InvalidState;
        if (!driver.TryStart()) return ManagedKernelContract.InvalidState;
        entropy.AttachVirtioRng(driver);
        Span<byte> first = stackalloc byte[64];
        Span<byte> second = stackalloc byte[64];
        if (!random.IsAvailable || !random.TryFill(first) ||
            entropy.LastProvider != ManagedEntropyProviderKind.VirtioRng ||
            !KernelLog.Write("GXOS_NET10:MANAGED_SECURE_RANDOM_PROVIDER=VIRTIO_RNG\r\n"u8) ||
            !KernelLog.Write("GXOS_NET10:MANAGED_SECURE_RANDOM_FILL_PASS\r\n"u8))
        {
            entropy.DetachVirtioRng(driver);
            driver.TryStop();
            return ManagedKernelContract.InvalidState;
        }
        if (!KernelLog.Write("GXOS_NET10:MANAGED_SECURE_RANDOM_GC_BEGIN\r\n"u8))
        {
            entropy.DetachVirtioRng(driver);
            driver.TryStop();
            return ManagedKernelContract.InvalidState;
        }
        CollectWithRoots(entropy, random);
        if (!KernelLog.Write("GXOS_NET10:MANAGED_SECURE_RANDOM_GC_COMPLETE\r\n"u8))
        {
            entropy.DetachVirtioRng(driver);
            driver.TryStop();
            return ManagedKernelContract.InvalidState;
        }
        if (!random.TryFill(second) ||
            entropy.LastProvider != ManagedEntropyProviderKind.VirtioRng ||
            !KernelLog.Write("GXOS_NET10:MANAGED_SECURE_RANDOM_GC_SURVIVAL_PASS\r\n"u8) ||
            !KernelLog.Write("GXOS_NET10:MANAGED_SECURE_RANDOM_SECOND_FILL_PASS\r\n"u8))
        {
            entropy.DetachVirtioRng(driver);
            driver.TryStop();
            return ManagedKernelContract.InvalidState;
        }
        first.Clear();
        second.Clear();
        if (!driver.TryStop() ||
            ManagedDeviceResourceRuntimeCatalog.ActiveClaimCountForDriver(
                ManagedVirtioRngProtocol.DriverId) != 0 ||
            !KernelLog.Write("GXOS_NET10:MANAGED_VIRTIO_RNG_QUEUE_RELEASED\r\n"u8))
        {
            entropy.DetachVirtioRng(driver);
            return ManagedKernelContract.InvalidState;
        }
        entropy.DetachVirtioRng(driver);

        ManagedVirtioRngDriver? reusedCandidate = ManagedVirtioRngDriver.TryCreate();
        if (!reusedCandidate.HasValue)
            return ManagedKernelContract.InvalidState;
        ManagedVirtioRngDriver reused = reusedCandidate.Value;
        if (!reused.TryStart()) return ManagedKernelContract.InvalidState;
        entropy.AttachVirtioRng(reused);
        Span<byte> reuseBytes = stackalloc byte[16];
        if (!random.TryFill(reuseBytes) ||
            entropy.LastProvider != ManagedEntropyProviderKind.VirtioRng ||
            !reused.TryStop() ||
            ManagedDeviceResourceRuntimeCatalog.ActiveClaimCountForDriver(
                ManagedVirtioRngProtocol.DriverId) != 0)
        {
            entropy.DetachVirtioRng(reused);
            reused.TryStop();
            return ManagedKernelContract.InvalidState;
        }
        entropy.DetachVirtioRng(reused);
        reuseBytes.Clear();
        s_run = 1;
        return KernelLog.Write("GXOS_NET10:MANAGED_VIRTIO_RNG_REINITIALIZE_REUSE_PASS\r\n"u8) &&
               KernelLog.Write("GXOS_NET10:MANAGED_VIRTIO_RNG_TEARDOWN_PASS\r\n"u8) &&
               KernelLog.Write("GXOS_NET10:MANAGED_SECURE_RANDOM_REPORTS_SUCCESS\r\n"u8) &&
               KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_PHASE26_PASS\r\n"u8)
            ? ManagedKernelContract.ManagedOk : ManagedKernelContract.InvalidState;
    }
}
