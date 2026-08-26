using System;

namespace GuideXOS.Net10.ManagedKernel;

/* Host-only provider fixture.  It has the production driver's provider shape
   but no PCI or DMA authority; transport behavior is tested by the bounded
   queue model in Program.cs. */
internal struct ManagedVirtioRngDriver : IManagedEntropyProvider
{
    private sealed class MutableState
    {
        internal bool Healthy;
        internal int FillCount;
    }

    private readonly MutableState? _state;

    private ManagedVirtioRngDriver(MutableState state)
    {
        _state = state;
    }

    internal static ManagedVirtioRngDriver Create() =>
        new(new MutableState());

    internal bool Healthy
    {
        get => _state != null && _state.Healthy;
        set
        {
            if (_state != null) _state.Healthy = value;
        }
    }

    internal int FillCount => _state?.FillCount ?? 0;

    public bool IsAvailable => Healthy;

    public bool TryFill(Span<byte> destination)
    {
        if (_state == null || !Healthy || destination.Length >
                ManagedVirtioRngProtocol.MaximumRequestBytes)
            return false;
        for (int index = 0; index != destination.Length; ++index)
            destination[index] = (byte)(0xA0 + ((_state.FillCount + index) & 0x1F));
        _state.FillCount++;
        return true;
    }
}
