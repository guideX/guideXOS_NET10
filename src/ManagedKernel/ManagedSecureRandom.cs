using System;

namespace GuideXOS.Net10.ManagedKernel;

internal interface IManagedEntropyProvider
{
    bool IsAvailable { get; }

    bool TryFill(Span<byte> destination);
}

internal enum ManagedEntropyProviderKind : uint
{
    None = 0,
    Hardware = 1,
    VirtioRng = 2
}

/// <summary>
/// Narrow random-byte consumer boundary. Production code supplies the native
/// hardware provider; host tests supply an explicitly injected fixture.
/// </summary>
internal sealed class ManagedSecureRandom
{
    internal const int MaximumBytesPerFill = 1024;

    private readonly IManagedEntropyProvider _provider;

    internal ManagedSecureRandom(IManagedEntropyProvider provider)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    internal bool IsAvailable => _provider.IsAvailable;

    internal bool TryFill(Span<byte> destination)
    {
        if (destination.Length > MaximumBytesPerFill || !IsAvailable)
        {
            return false;
        }
        return _provider.TryFill(destination);
    }
}

/* Production provider order is explicit and bounded: NativeHardwareEntropy
   internally tries RDSEED before RDRAND; a healthy virtio-rng driver is the
   final provider. There is deliberately no timing or deterministic fallback. */
internal sealed class ManagedEntropyService : IManagedEntropyProvider
{
    private readonly IManagedEntropyProvider? _hardwareProvider;
    private readonly nuint _hardwareFillAddress;
    private readonly ulong _hardwareCapabilities;
    private readonly uint _hardwareMaximumBytesPerFill;
    /* There is one production entropy router. Keep the driver publication in
       a static root so the router object itself remains a scalar-only state
       holder on NativeAOT's GC path. */
    private static ManagedVirtioRngDriver? s_virtioRng;

    internal ManagedEntropyService(IManagedEntropyProvider hardware)
    {
        _hardwareProvider = hardware ?? throw new ArgumentNullException(nameof(hardware));
    }

    internal ManagedEntropyService(nuint hardwareFillAddress,
                                   ulong hardwareCapabilities,
                                   uint hardwareMaximumBytesPerFill)
    {
        _hardwareFillAddress = hardwareFillAddress;
        _hardwareCapabilities = hardwareCapabilities;
        _hardwareMaximumBytesPerFill = hardwareMaximumBytesPerFill;
    }

    internal ManagedEntropyProviderKind LastProvider { get; private set; }

    internal bool HasVirtioRng => s_virtioRng.HasValue && s_virtioRng.Value.IsAvailable;

    internal void AttachVirtioRng(ManagedVirtioRngDriver driver)
    {
        s_virtioRng = driver;
    }

    internal void DetachVirtioRng(ManagedVirtioRngDriver driver)
    {
        if (s_virtioRng.HasValue) s_virtioRng = null;
    }

    private bool IsHardwareAvailable => _hardwareProvider != null
        ? _hardwareProvider.IsAvailable
        : _hardwareFillAddress != 0 &&
          (_hardwareCapabilities & NativeHardwareEntropy.CapabilityHardwareEntropy) != 0;

    private unsafe bool TryFillHardware(Span<byte> destination)
    {
        if (_hardwareProvider != null)
            return _hardwareProvider.TryFill(destination);
        if (!IsHardwareAvailable ||
            (uint)destination.Length > _hardwareMaximumBytesPerFill)
            return false;
        if (destination.Length == 0) return true;
        delegate* unmanaged<nuint, uint, uint> fill =
            (delegate* unmanaged<nuint, uint, uint>)_hardwareFillAddress;
        fixed (byte* address = destination)
        {
            return fill((nuint)address, (uint)destination.Length) ==
                   NativeHardwareEntropy.StatusOk;
        }
    }

    public bool IsAvailable => IsHardwareAvailable || HasVirtioRng;

    public bool TryFill(Span<byte> destination)
    {
        LastProvider = ManagedEntropyProviderKind.None;
        if (IsHardwareAvailable && TryFillHardware(destination))
        {
            LastProvider = ManagedEntropyProviderKind.Hardware;
            return true;
        }
        if (s_virtioRng.HasValue && s_virtioRng.Value.IsAvailable &&
            s_virtioRng.Value.TryFill(destination))
        {
            LastProvider = ManagedEntropyProviderKind.VirtioRng;
            return true;
        }
        return false;
    }
}

internal static class ManagedCryptoComparison
{
    internal static bool FixedTimeEquals(ReadOnlySpan<byte> left,
                                         ReadOnlySpan<byte> right)
    {
        int length = Math.Min(left.Length, right.Length);
        uint difference = (uint)(left.Length ^ right.Length);
        for (int index = 0; index != length; ++index)
        {
            difference |= (uint)(left[index] ^ right[index]);
        }
        return difference == 0;
    }
}

internal unsafe sealed class NativeHardwareEntropy : IManagedEntropyProvider
{
    internal const ulong CapabilityHardwareEntropy = 1UL << 0;
    internal const ulong CapabilityRdrand = 1UL << 1;
    internal const ulong CapabilityRdseed = 1UL << 2;
    internal const uint StatusOk = 0;

    private readonly nuint _fillAddress;
    private readonly ulong _capabilities;
    private readonly uint _maximumBytesPerFill;

    internal NativeHardwareEntropy(nuint fillAddress, ulong capabilities,
                                   uint maximumBytesPerFill)
    {
        _fillAddress = fillAddress;
        _capabilities = capabilities;
        _maximumBytesPerFill = maximumBytesPerFill;
    }

    internal bool IsRdrandAvailable =>
        (_capabilities & CapabilityRdrand) != 0;

    internal bool IsRdseedAvailable =>
        (_capabilities & CapabilityRdseed) != 0;

    public bool IsAvailable =>
        _fillAddress != 0 &&
        (_capabilities & CapabilityHardwareEntropy) != 0;

    public bool TryFill(Span<byte> destination)
    {
        if (!IsAvailable || (uint)destination.Length > _maximumBytesPerFill)
        {
            return false;
        }
        if (destination.Length == 0)
        {
            return true;
        }
        delegate* unmanaged<nuint, uint, uint> fill =
            (delegate* unmanaged<nuint, uint, uint>)_fillAddress;
        fixed (byte* address = destination)
        {
            return fill((nuint)address, (uint)destination.Length) == StatusOk;
        }
    }
}
