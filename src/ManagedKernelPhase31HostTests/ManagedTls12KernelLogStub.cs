using System;

namespace GuideXOS.Net10.ManagedKernel;

/* The host TLS suite links the protocol implementation without the kernel's
   serial logger.  Keep the production logging call sites compilable while
   making host tests independent of the firmware logger. */
internal static class KernelLog
{
    internal static bool Write(ReadOnlySpan<byte> value) => true;

    internal static bool WriteHexLine(ReadOnlySpan<byte> prefix, ulong value) =>
        true;
}
