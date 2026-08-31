using System;

namespace GuideXOS.Net10.ManagedKernel;

internal static class Program
{
    private const uint Local = 0x0A00020FU;
    private const uint Mask = 0xFFFFFF00U;
    private const uint Gateway = 0x0A000202U;

    private static int s_cases;

    private static int Main()
    {
        try
        {
            TestDirectRoute();
            TestGatewayRoute();
            TestInvalidRoutes();
            Console.WriteLine(
                $"MANAGED_KERNEL_PHASE35_HOST_TESTS_PASS cases={s_cases}");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(
                $"MANAGED_KERNEL_PHASE35_HOST_TESTS_FAIL cases={s_cases} {exception}");
            return 1;
        }
    }

    private static void TestDirectRoute()
    {
        Check(ManagedIpv4Protocol.TrySelectNextHop(
                  Local, Mask, Gateway, 0x0A000203U, out uint nextHop) &&
              nextHop == 0x0A000203U,
              "same-subnet-destination-is-direct");
    }

    private static void TestGatewayRoute()
    {
        Check(ManagedIpv4Protocol.TrySelectNextHop(
                  Local, Mask, Gateway, 0x01010101U, out uint nextHop) &&
              nextHop == Gateway,
              "off-subnet-destination-uses-gateway");
        Check(ManagedIpv4Protocol.TrySelectNextHop(
                  Local, Mask, Gateway, 0x08080808U, out nextHop) &&
              nextHop == Gateway,
              "public-destination-uses-gateway");
    }

    private static void TestInvalidRoutes()
    {
        Check(!ManagedIpv4Protocol.TrySelectNextHop(
                  Local, Mask, 0, 0x08080808U, out _),
              "off-subnet-without-gateway-rejected");
        Check(!ManagedIpv4Protocol.TrySelectNextHop(
                  Local, Mask, 0x0A010202U, 0x08080808U, out _),
              "off-link-gateway-rejected");
        Check(!ManagedIpv4Protocol.TrySelectNextHop(
                  Local, Mask, Gateway, 0xFFFFFFFFU, out _),
              "limited-broadcast-rejected");
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException(name);
        s_cases++;
    }
}
