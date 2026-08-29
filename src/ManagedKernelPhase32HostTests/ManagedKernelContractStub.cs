namespace GuideXOS.Net10.ManagedKernel;

/* The host composition suite does not boot the NativeAOT contract. The
   production implementation is supplied by ManagedKernel.cs; this isolated
   stub keeps the public-constructor dependency explicit and unavailable. */
internal static class ManagedKernelContract
{
    internal static ManagedSecureRandom? SecureRandom => null;
    internal static bool TryEnsureEntropyService() => false;
}
