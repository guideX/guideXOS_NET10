namespace GuideXOS.Net10.ManagedKernel;

internal static class ManagedKernelContract
{
    internal static ManagedSecureRandom? SecureRandom => null;
    internal static bool TryEnsureEntropyService() => false;
}
