using System;

namespace GuideXOS.Net10.ManagedKernel;

/* Phase 25 does not exercise the production virtio transport, but the
   shared entropy router retains the provider shape so the host suite can
   compile the same secure-random boundary. */
internal struct ManagedVirtioRngDriver : IManagedEntropyProvider
{
    public bool IsAvailable => false;

    public bool TryFill(Span<byte> destination) => false;
}
