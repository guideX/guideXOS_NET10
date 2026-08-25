using System;

namespace GuideXOS.Net10.ManagedKernel;

internal interface IManagedEntropyProvider
{
    bool IsAvailable { get; }

    bool TryFill(Span<byte> destination);
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
