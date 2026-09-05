using System;
using GuideXOS.Net10.ManagedKernel;

namespace GuideXOS.Net10.ManagedKernelPhase49HostTests;

internal static class Program
{
    private static int s_cases;

    private static void Check(bool condition, string name)
    {
        ++s_cases;
        if (!condition) throw new InvalidOperationException(name);
    }

    private static ManagedFramebuffer MakeSource(uint[] storage,
                                                  int offset = 1,
                                                  int width = 3,
                                                  int height = 2,
                                                  int stride = 5)
    {
        for (int y = 0; y != height; ++y)
        {
            for (int x = 0; x != width; ++x)
                storage[offset + y * stride + x] =
                    0xFF000000U | (uint)(x * 0x4000) |
                    (uint)(y * 0x004000) | (uint)(x + y + 1);
            storage[offset + y * stride + width] = 0xDEADBEEFU;
            storage[offset + y * stride + width + 1] = 0xCAFEBABEU;
        }
        return new ManagedFramebuffer(storage, offset, width, height, stride);
    }

    private static ManagedPhysicalFramebufferDescriptor Descriptor(
        ManagedPhysicalFramebufferPixelFormat format =
            ManagedPhysicalFramebufferPixelFormat.BlueGreenRedReserved8)
    {
        return new ManagedPhysicalFramebufferDescriptor(
            0x1000, 72, 4, 3, 6, 4, format,
            format == ManagedPhysicalFramebufferPixelFormat.BitMask ? 0x00FF0000U : 0,
            format == ManagedPhysicalFramebufferPixelFormat.BitMask ? 0x0000FF00U : 0,
            format == ManagedPhysicalFramebufferPixelFormat.BitMask ? 0x000000FFU : 0,
            format == ManagedPhysicalFramebufferPixelFormat.BitMask ? 0xFF000000U : 0);
    }

    private static bool AllEqual(byte[] bytes, int start, int length, byte value)
    {
        for (int index = start; index < start + length; ++index)
            if (bytes[index] != value) return false;
        return true;
    }

    private static void TestValidBgrAndPadding()
    {
        uint[] sourceStorage = new uint[16];
        ManagedFramebuffer source = MakeSource(sourceStorage);
        uint[] sourceSnapshot = (uint[])sourceStorage.Clone();
        byte[] destinationStorage = new byte[80];
        Array.Fill(destinationStorage, (byte)0xA5);
        ManagedFramebufferPresenter presenter = new();
        ManagedPhysicalFramebufferDescriptor descriptor = Descriptor();
        bool result = presenter.TryPresent(in source, in descriptor,
            ManagedFramebufferDestination.FromHost(destinationStorage), 2, 1);
        Check(result, "valid-bgr-result");
        Check(presenter.Telemetry.State == ManagedFramebufferPresentationState.Complete,
            "valid-bgr-state");
        Check(presenter.Telemetry.PixelsWritten == 4 &&
              presenter.Telemetry.PixelsClipped == 2, "valid-bgr-clip");
        Check(presenter.Telemetry.SourceHashCaptured &&
              presenter.Telemetry.SourceHashUnchanged &&
              presenter.Telemetry.DestinationHashValid, "valid-bgr-hashes");
        for (int index = 0; index != sourceStorage.Length; ++index)
            Check(sourceStorage[index] == sourceSnapshot[index], "source-preserved-" + index);
        Check(AllEqual(destinationStorage, 72, 8, 0xA5), "destination-canary");
        for (int row = 0; row != 3; ++row)
        {
            int rowStart = row * 24;
            for (int pixel = 0; pixel != 6; ++pixel)
            {
                bool visible = row >= 1 && row <= 2 && pixel >= 2 && pixel <= 3;
                Check(visible || AllEqual(destinationStorage, rowStart + pixel * 4, 4, 0xA5),
                    "padding-or-unwritten-" + row + "-" + pixel);
            }
        }
        Span<byte> before = stackalloc byte[ManagedSha256.DigestSize];
        Span<byte> after = stackalloc byte[ManagedSha256.DigestSize];
        Check(presenter.TryCopySourceHashBefore(before) &&
              presenter.TryCopySourceHashAfter(after), "source-hash-copy");
        for (int index = 0; index != before.Length; ++index)
            Check(before[index] == after[index], "source-hash-equal-" + index);
    }

    private static void TestRgbAndBitMask()
    {
        uint[] sourceStorage = new uint[16];
        ManagedFramebuffer source = MakeSource(sourceStorage);
        ManagedFramebufferPresenter presenter = new();
        byte[] rgb = new byte[80];
        Array.Fill(rgb, (byte)0xCC);
        ManagedPhysicalFramebufferDescriptor rgbDescriptor = Descriptor(
            ManagedPhysicalFramebufferPixelFormat.RedGreenBlueReserved8);
        Check(presenter.TryPresent(in source, in rgbDescriptor,
            ManagedFramebufferDestination.FromHost(rgb)), "valid-rgb");
        Check(presenter.Telemetry.PixelsWritten == 6, "rgb-pixel-count");
        byte[] bitMask = new byte[80];
        ManagedPhysicalFramebufferDescriptor maskDescriptor = Descriptor(
            ManagedPhysicalFramebufferPixelFormat.BitMask);
        Check(presenter.TryPresent(in source, in maskDescriptor,
            ManagedFramebufferDestination.FromHost(bitMask)), "valid-bitmask");
        Check(presenter.Telemetry.State == ManagedFramebufferPresentationState.Complete,
            "bitmask-state");
        Check(presenter.TryCopyDestinationHash(stackalloc byte[ManagedSha256.DigestSize]),
            "destination-hash-copy");
    }

    private static void TestDescriptorFailures()
    {
        uint[] sourceStorage = new uint[16];
        ManagedFramebuffer source = MakeSource(sourceStorage);
        byte[] destination = new byte[80];
        ManagedFramebufferPresenter presenter = new();
        ManagedPhysicalFramebufferDescriptor[] descriptors =
        {
            new(0, 72, 4, 3, 6, 4, ManagedPhysicalFramebufferPixelFormat.BlueGreenRedReserved8),
            new(0x1000, 0, 4, 3, 6, 4, ManagedPhysicalFramebufferPixelFormat.BlueGreenRedReserved8),
            new(0x1000, 72, 4, 3, 6, 3, ManagedPhysicalFramebufferPixelFormat.BlueGreenRedReserved8),
            new(0x1000, 72, 4, 3, 3, 4, ManagedPhysicalFramebufferPixelFormat.BlueGreenRedReserved8),
            new(ulong.MaxValue - 4, 8, 4, 1, 4, 4, ManagedPhysicalFramebufferPixelFormat.BlueGreenRedReserved8),
            new(0x1000, 16, 4, 3, 6, 4, ManagedPhysicalFramebufferPixelFormat.BlueGreenRedReserved8),
            new(0x1000, 72, 4, 3, 6, 4, (ManagedPhysicalFramebufferPixelFormat)3)
        };
        ManagedFramebufferPresentationFailureReason[] reasons =
        {
            ManagedFramebufferPresentationFailureReason.NoFramebuffer,
            ManagedFramebufferPresentationFailureReason.NoFramebuffer,
            ManagedFramebufferPresentationFailureReason.InvalidFramebufferDescriptor,
            ManagedFramebufferPresentationFailureReason.InvalidFramebufferDescriptor,
            ManagedFramebufferPresentationFailureReason.FramebufferAddressOverflow,
            ManagedFramebufferPresentationFailureReason.FramebufferRangeOverflow,
            ManagedFramebufferPresentationFailureReason.UnsupportedPhysicalPixelFormat
        };
        for (int index = 0; index != descriptors.Length; ++index)
        {
            byte[] before = (byte[])destination.Clone();
            Check(!presenter.TryPresent(in source, in descriptors[index],
                ManagedFramebufferDestination.FromHost(destination)), "descriptor-fail-" + index);
            Check(presenter.Telemetry.FailureReason == reasons[index],
                "descriptor-reason-" + index);
            for (int byteIndex = 0; byteIndex != destination.Length; ++byteIndex)
                Check(before[byteIndex] == destination[byteIndex], "descriptor-no-write-" + index + "-" + byteIndex);
        }
        byte[] shortDestination = new byte[71];
        ManagedPhysicalFramebufferDescriptor valid = Descriptor();
        Check(!presenter.TryPresent(in source, in valid,
            ManagedFramebufferDestination.FromHost(shortDestination)), "short-destination");
        Check(presenter.Telemetry.FailureReason ==
              ManagedFramebufferPresentationFailureReason.DestinationOutOfBounds,
            "short-destination-reason");
    }

    private static void TestSourceFailuresAndClipping()
    {
        ManagedFramebufferPresenter presenter = new();
        byte[] destination = new byte[80];
        ManagedPhysicalFramebufferDescriptor descriptor = Descriptor();
        ManagedFramebuffer[] sources =
        {
            new ManagedFramebuffer(null!, 3, 2),
            new ManagedFramebuffer(new uint[4], 0, 3, 2, 2),
            new ManagedFramebuffer(new uint[8], -1, 3, 2, 3),
            new ManagedFramebuffer(new uint[8], 0, 3, 2, 3,
                (ManagedRasterPixelFormat)1)
        };
        for (int index = 0; index != sources.Length; ++index)
        {
            Check(!presenter.TryPresent(in sources[index], in descriptor,
                ManagedFramebufferDestination.FromHost(destination)), "source-fail-" + index);
            Check(presenter.Telemetry.FailureReason ==
                  ManagedFramebufferPresentationFailureReason.SourceFramebufferInvalid,
                "source-fail-reason-" + index);
        }
        uint[] storage = new uint[16];
        ManagedFramebuffer source = MakeSource(storage);
        Array.Fill(destination, (byte)0x91);
        Check(presenter.TryPresent(in source, in descriptor,
            ManagedFramebufferDestination.FromHost(destination), -2, -1), "negative-clip");
        Check(presenter.Telemetry.PixelsWritten == 1 &&
              presenter.Telemetry.PixelsClipped == 5, "negative-clip-count");
        Check(presenter.TryPresent(in source, in descriptor,
            ManagedFramebufferDestination.FromHost(destination), 99, 99), "fully-clipped");
        Check(presenter.Telemetry.PixelsWritten == 0 &&
              presenter.Telemetry.PixelsClipped == 6, "fully-clipped-count");
    }

    private static void TestCancellationAndReset()
    {
        uint[] storage = new uint[16];
        ManagedFramebuffer source = MakeSource(storage);
        ManagedPhysicalFramebufferDescriptor descriptor = Descriptor();
        ManagedFramebufferPresenter presenter = new();
        byte[] destination = new byte[80];
        Array.Fill(destination, (byte)0x77);
        presenter.CancelAfterRows(1);
        Check(!presenter.TryPresent(in source, in descriptor,
            ManagedFramebufferDestination.FromHost(destination)), "cancel-after-row");
        Check(presenter.Telemetry.State == ManagedFramebufferPresentationState.Cancelled &&
              presenter.Telemetry.FailureReason ==
              ManagedFramebufferPresentationFailureReason.PresentationCancelled &&
              presenter.Telemetry.RowsWritten == 1, "cancel-after-row-state");
        presenter.Reset();
        Check(presenter.Telemetry.State == ManagedFramebufferPresentationState.Reset &&
              presenter.Telemetry.PixelsWritten == 0 &&
              !presenter.Telemetry.SourceHashCaptured, "reset-state");
        presenter.Cancel();
        Check(!presenter.TryPresent(in source, in descriptor,
            ManagedFramebufferDestination.FromHost(destination)), "cancel-before-start");
        presenter.Reset();
        Check(presenter.TryPresent(in source, in descriptor,
            ManagedFramebufferDestination.FromHost(destination)), "post-reset-reuse");
        for (int repeat = 0; repeat != 12; ++repeat)
        {
            presenter.Reset();
            Check(presenter.TryPresent(in source, in descriptor,
                ManagedFramebufferDestination.FromHost(destination)),
                "bounded-repeat-" + repeat);
        }
    }

    private static void Main()
    {
        TestValidBgrAndPadding();
        TestRgbAndBitMask();
        TestDescriptorFailures();
        TestSourceFailuresAndClipping();
        TestCancellationAndReset();
        for (int repeat = 0; repeat != 12; ++repeat)
        {
            uint[] storage = new uint[16];
            ManagedFramebuffer source = MakeSource(storage);
            ManagedFramebufferPresenter presenter = new();
            ManagedPhysicalFramebufferDescriptor descriptor = Descriptor();
            Check(presenter.TryPresent(in source, in descriptor,
                ManagedFramebufferDestination.FromHost(new byte[80])),
                "independent-repeat-" + repeat);
        }
        Console.WriteLine("MANAGED_KERNEL_PHASE49_HOST_TESTS_PASS cases=" + s_cases);
    }
}
