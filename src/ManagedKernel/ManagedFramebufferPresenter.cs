using System;
using System.Runtime.InteropServices;

namespace GuideXOS.Net10.ManagedKernel;

/* UEFI GOP pixel formats used by the loader.  The values intentionally match
   EFI_GRAPHICS_PIXEL_FORMAT so the descriptor can be copied without a second
   translation table. */
public enum ManagedPhysicalFramebufferPixelFormat : uint
{
    RedGreenBlueReserved8 = 0,
    BlueGreenRedReserved8 = 1,
    BitMask = 2
}

public enum ManagedFramebufferPresentationState : byte
{
    Reset = 0,
    Complete = 1,
    Failed = 2,
    Cancelled = 3
}

public enum ManagedFramebufferPresentationFailureReason : byte
{
    None = 0,
    NoFramebuffer = 1,
    InvalidFramebufferDescriptor = 2,
    UnsupportedPhysicalPixelFormat = 3,
    FramebufferAddressOverflow = 4,
    FramebufferRangeOverflow = 5,
    FramebufferMappingFailure = 6,
    SourceFramebufferInvalid = 7,
    DestinationOutOfBounds = 8,
    PresentationCancelled = 9,
    PresentationVerificationFailure = 10
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct ManagedPhysicalFramebufferDescriptor
{
    public ManagedPhysicalFramebufferDescriptor(
        ulong framebufferBase, ulong framebufferSize, uint width, uint height,
        uint pixelsPerScanLine, uint bytesPerPixel,
        ManagedPhysicalFramebufferPixelFormat pixelFormat,
        uint redMask = 0, uint greenMask = 0, uint blueMask = 0,
        uint reservedMask = 0)
    {
        FramebufferBase = framebufferBase;
        FramebufferSize = framebufferSize;
        Width = width;
        Height = height;
        PixelsPerScanLine = pixelsPerScanLine;
        BytesPerPixel = bytesPerPixel;
        PixelFormat = pixelFormat;
        RedMask = redMask;
        GreenMask = greenMask;
        BlueMask = blueMask;
        ReservedMask = reservedMask;
    }

    public ulong FramebufferBase { get; }
    public ulong FramebufferSize { get; }
    public uint Width { get; }
    public uint Height { get; }
    public uint PixelsPerScanLine { get; }
    public uint BytesPerPixel { get; }
    public ManagedPhysicalFramebufferPixelFormat PixelFormat { get; }
    public uint RedMask { get; }
    public uint GreenMask { get; }
    public uint BlueMask { get; }
    public uint ReservedMask { get; }
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct ManagedFramebufferPresentationTelemetry
{
    internal ManagedFramebufferPresentationTelemetry(
        ManagedFramebufferPresentationState state,
        ManagedFramebufferPresentationFailureReason failureReason,
        long pixelsWritten, long pixelsClipped, int rowsWritten,
        bool sourceHashCaptured, bool sourceHashUnchanged,
        bool destinationHashValid)
    {
        State = state;
        FailureReason = failureReason;
        PixelsWritten = pixelsWritten;
        PixelsClipped = pixelsClipped;
        RowsWritten = rowsWritten;
        SourceHashCaptured = sourceHashCaptured;
        SourceHashUnchanged = sourceHashUnchanged;
        DestinationHashValid = destinationHashValid;
    }

    public ManagedFramebufferPresentationState State { get; }
    public ManagedFramebufferPresentationFailureReason FailureReason { get; }
    public long PixelsWritten { get; }
    public long PixelsClipped { get; }
    public int RowsWritten { get; }
    public bool SourceHashCaptured { get; }
    public bool SourceHashUnchanged { get; }
    public bool DestinationHashValid { get; }
}

/* A destination is either a host-owned byte array (used by adversarial tests)
   or a bounded identity-mapped physical address.  The presenter never asks
   the destination for a stride-sized scratch surface and never writes row
   padding. */
public unsafe sealed class ManagedFramebufferDestination
{
    private readonly byte[]? _hostStorage;
    private readonly nuint _physicalAddress;
    private readonly nuint _byteLength;

    private ManagedFramebufferDestination(byte[] hostStorage)
    {
        _hostStorage = hostStorage;
        _physicalAddress = 0;
        _byteLength = (nuint)hostStorage.Length;
    }

    private ManagedFramebufferDestination(nuint physicalAddress, nuint byteLength)
    {
        _hostStorage = null;
        _physicalAddress = physicalAddress;
        _byteLength = byteLength;
    }

    public static ManagedFramebufferDestination FromHost(byte[] storage)
    {
        if (storage == null) throw new ArgumentNullException(nameof(storage));
        return new ManagedFramebufferDestination(storage);
    }

    public static ManagedFramebufferDestination FromPhysical(nuint address,
                                                              nuint byteLength)
    {
        return new ManagedFramebufferDestination(address, byteLength);
    }

    internal nuint ByteLength => _byteLength;

    internal bool TryWrite32(nuint byteOffset, uint value)
    {
        if (byteOffset > _byteLength || _byteLength - byteOffset < 4) return false;
        if (_hostStorage != null)
        {
            int offset = checked((int)byteOffset);
            _hostStorage[offset] = (byte)value;
            _hostStorage[offset + 1] = (byte)(value >> 8);
            _hostStorage[offset + 2] = (byte)(value >> 16);
            _hostStorage[offset + 3] = (byte)(value >> 24);
            return true;
        }
        if (_physicalAddress == 0 || _physicalAddress > nuint.MaxValue - byteOffset)
            return false;
        *(uint*)(_physicalAddress + byteOffset) = value;
        return true;
    }
}

public unsafe sealed class ManagedFramebufferPresenter
{
    private static readonly byte[] SourceHashDomain =
        "GXOS-P47-FB-ARGB8888\0"u8.ToArray();
    private static readonly byte[] DestinationHashDomain =
        "GXOS-P49-DST-ARGB8888\0"u8.ToArray();
    private readonly byte[] _sourceHashBefore = new byte[ManagedSha256.DigestSize];
    private readonly byte[] _sourceHashAfter = new byte[ManagedSha256.DigestSize];
    private readonly byte[] _destinationHash = new byte[ManagedSha256.DigestSize];
    private ManagedFramebufferPresentationTelemetry _telemetry;
    private ManagedFramebufferPresentationFailureReason _failureReason;
    private ManagedFramebufferPresentationState _state;
    private long _pixelsWritten;
    private long _pixelsClipped;
    private int _rowsWritten;
    private bool _sourceHashCaptured;
    private bool _sourceHashUnchanged;
    private bool _cancelRequested;
    private int _cancelAfterRows = -1;

    public ManagedFramebufferPresenter()
    {
        Reset();
    }

    public ManagedFramebufferPresentationTelemetry Telemetry => _telemetry;

    public void Cancel() => _cancelRequested = true;

    public void CancelAfterRows(int rowCount)
    {
        _cancelAfterRows = rowCount < 0 ? -1 : rowCount;
    }

    public bool TryCopySourceHashBefore(Span<byte> destination) =>
        CopyHash(_sourceHashBefore, _sourceHashCaptured, destination);

    public bool TryCopySourceHashAfter(Span<byte> destination) =>
        CopyHash(_sourceHashAfter, _sourceHashCaptured, destination);

    public bool TryCopyDestinationHash(Span<byte> destination) =>
        CopyHash(_destinationHash, _telemetry.DestinationHashValid, destination);

    public void Reset()
    {
        _state = ManagedFramebufferPresentationState.Reset;
        _failureReason = ManagedFramebufferPresentationFailureReason.None;
        _pixelsWritten = 0;
        _pixelsClipped = 0;
        _rowsWritten = 0;
        _sourceHashCaptured = false;
        _sourceHashUnchanged = false;
        _cancelRequested = false;
        _cancelAfterRows = -1;
        _sourceHashBefore.AsSpan().Clear();
        _sourceHashAfter.AsSpan().Clear();
        _destinationHash.AsSpan().Clear();
        _lastSource = default;
        _lastSourceValid = false;
        UpdateTelemetry(false);
    }

    public bool TryPresent(in ManagedFramebuffer source,
                           in ManagedPhysicalFramebufferDescriptor descriptor,
                           ManagedFramebufferDestination destination,
                           int destinationX = 0, int destinationY = 0)
    {
        ResetAttempt();
        if (!TryValidateSource(in source))
            return Fail(ManagedFramebufferPresentationFailureReason.SourceFramebufferInvalid);
        ManagedFramebufferPresentationFailureReason descriptorFailure;
        if (!TryValidateDescriptor(in descriptor, destination, out descriptorFailure))
            return Fail(descriptorFailure);
        _lastSource = source;
        _lastSourceValid = true;
        if (!TryHashSource(in source, _sourceHashBefore))
            return Fail(ManagedFramebufferPresentationFailureReason.SourceFramebufferInvalid);
        _sourceHashCaptured = true;

        long sourceRight = (long)source.Width;
        long sourceBottom = (long)source.Height;
        long physicalRight = (long)descriptor.Width - destinationX;
        long physicalBottom = (long)descriptor.Height - destinationY;
        long sourceX0 = Math.Max(0, -((long)destinationX));
        long sourceY0 = Math.Max(0, -((long)destinationY));
        long sourceX1 = Math.Min(sourceRight, physicalRight);
        long sourceY1 = Math.Min(sourceBottom, physicalBottom);
        long sourcePixels = checked((long)source.Width * source.Height);
        long visiblePixels = sourceX1 > sourceX0 && sourceY1 > sourceY0
            ? checked((sourceX1 - sourceX0) * (sourceY1 - sourceY0)) : 0;
        _pixelsClipped = sourcePixels - visiblePixels;

        ManagedSha256 destinationHash = new();
        if (!destinationHash.Append(DestinationHashDomain) ||
            !AppendUInt32(destinationHash, (uint)Math.Max(0, sourceX1 - sourceX0)) ||
            !AppendUInt32(destinationHash, (uint)Math.Max(0, sourceY1 - sourceY0)))
            return Fail(ManagedFramebufferPresentationFailureReason.PresentationVerificationFailure);
        if (visiblePixels != 0)
        {
            for (long row = sourceY0; row < sourceY1; ++row)
            {
                if (_cancelRequested || (_cancelAfterRows >= 0 &&
                                          _rowsWritten >= _cancelAfterRows))
                    return Fail(ManagedFramebufferPresentationFailureReason.PresentationCancelled,
                                cancelled: true);
                int sourceY = checked((int)row);
                int targetY = checked(destinationY + sourceY);
                for (long column = sourceX0; column < sourceX1; ++column)
                {
                    int sourceX = checked((int)column);
                    if (!source.TryGetPixel(sourceX, sourceY, out uint argb))
                        return Fail(ManagedFramebufferPresentationFailureReason.SourceFramebufferInvalid);
                    uint opaqueArgb = argb | 0xFF000000U;
                    uint packed = PackPixel(in descriptor, opaqueArgb);
                    long targetX = (long)destinationX + column;
                    long pixelIndex = checked((long)targetY * descriptor.PixelsPerScanLine + targetX);
                    long byteOffset = checked(pixelIndex * descriptor.BytesPerPixel);
                    if (byteOffset < 0 || !destination.TryWrite32((nuint)byteOffset, packed))
                        return Fail(ManagedFramebufferPresentationFailureReason.DestinationOutOfBounds);
                    if (!AppendArgb(destinationHash, opaqueArgb))
                        return Fail(ManagedFramebufferPresentationFailureReason.PresentationVerificationFailure);
                    ++_pixelsWritten;
                }
                ++_rowsWritten;
            }
        }
        if (!destinationHash.TryFinalize(_destinationHash))
            return Fail(ManagedFramebufferPresentationFailureReason.PresentationVerificationFailure);
        _state = ManagedFramebufferPresentationState.Complete;
        if (!TryHashSource(in source, _sourceHashAfter))
            return Fail(ManagedFramebufferPresentationFailureReason.SourceFramebufferInvalid);
        _sourceHashUnchanged = Equal(_sourceHashBefore, _sourceHashAfter);
        if (!_sourceHashUnchanged)
            return Fail(ManagedFramebufferPresentationFailureReason.PresentationVerificationFailure);
        UpdateTelemetry(true);
        return true;
    }

    private void ResetAttempt()
    {
        _state = ManagedFramebufferPresentationState.Reset;
        _failureReason = ManagedFramebufferPresentationFailureReason.None;
        _pixelsWritten = 0;
        _pixelsClipped = 0;
        _rowsWritten = 0;
        _sourceHashCaptured = false;
        _sourceHashUnchanged = false;
        _sourceHashBefore.AsSpan().Clear();
        _sourceHashAfter.AsSpan().Clear();
        _destinationHash.AsSpan().Clear();
        _lastSource = default;
        _lastSourceValid = false;
    }

    private bool Fail(ManagedFramebufferPresentationFailureReason reason,
                      bool cancelled = false)
    {
        _failureReason = reason;
        _state = cancelled ? ManagedFramebufferPresentationState.Cancelled :
            ManagedFramebufferPresentationState.Failed;
        if (_sourceHashCaptured)
        {
            _sourceHashUnchanged = TryHashSourceFromLastFramebuffer(_sourceHashAfter) &&
                                   Equal(_sourceHashBefore, _sourceHashAfter);
        }
        UpdateTelemetry(false);
        return false;
    }

    /* The last source is retained only for the duration of an attempt.  This
       is set by TryPresent and avoids exposing a second framebuffer. */
    private ManagedFramebuffer _lastSource;
    private bool _lastSourceValid;

    private bool TryHashSourceFromLastFramebuffer(Span<byte> destination) =>
        _lastSourceValid && TryHashSource(in _lastSource, destination);

    private void UpdateTelemetry(bool destinationHashValid)
    {
        _telemetry = new(_state, _failureReason, _pixelsWritten, _pixelsClipped,
                         _rowsWritten, _sourceHashCaptured, _sourceHashUnchanged,
                         destinationHashValid);
    }

    private static bool CopyHash(byte[] source, bool valid, Span<byte> destination)
    {
        if (!valid || destination.Length < source.Length) return false;
        source.AsSpan().CopyTo(destination);
        return true;
    }

    private static bool TryValidateSource(in ManagedFramebuffer source)
    {
        if (source.PixelFormat != ManagedRasterPixelFormat.Argb8888 ||
            source.BackingStorage == null || source.Width <= 0 || source.Height <= 0 ||
            source.Stride < source.Width || source.Offset < 0)
            return false;
        long required = (long)source.Offset + (long)source.Stride * source.Height;
        return required > 0 && required <= source.BackingStorage.Length;
    }

    internal static bool TryValidateDescriptor(
        in ManagedPhysicalFramebufferDescriptor descriptor,
        out ManagedFramebufferPresentationFailureReason failure)
    {
        return TryValidateDescriptorShape(in descriptor, out failure);
    }

    private static bool TryValidateDescriptor(
        in ManagedPhysicalFramebufferDescriptor descriptor,
        ManagedFramebufferDestination destination,
        out ManagedFramebufferPresentationFailureReason failure)
    {
        if (!TryValidateDescriptorShape(in descriptor, out failure)) return false;
        if (destination == null || descriptor.FramebufferSize > (ulong)destination.ByteLength)
        {
            failure = ManagedFramebufferPresentationFailureReason.DestinationOutOfBounds;
            return false;
        }
        return true;
    }

    private static bool TryValidateDescriptorShape(
        in ManagedPhysicalFramebufferDescriptor descriptor,
        out ManagedFramebufferPresentationFailureReason failure)
    {
        failure = ManagedFramebufferPresentationFailureReason.None;
        if (descriptor.FramebufferBase == 0 || descriptor.FramebufferSize == 0)
        {
            failure = ManagedFramebufferPresentationFailureReason.NoFramebuffer;
            return false;
        }
        if (descriptor.BytesPerPixel != 4 || descriptor.Width == 0 ||
            descriptor.Height == 0 || descriptor.PixelsPerScanLine < descriptor.Width)
        {
            failure = ManagedFramebufferPresentationFailureReason.InvalidFramebufferDescriptor;
            return false;
        }
        if (descriptor.FramebufferBase > ulong.MaxValue - descriptor.FramebufferSize)
        {
            failure = ManagedFramebufferPresentationFailureReason.FramebufferAddressOverflow;
            return false;
        }
        ulong required = (ulong)descriptor.PixelsPerScanLine * descriptor.Height;
        if (required > ulong.MaxValue / descriptor.BytesPerPixel)
        {
            failure = ManagedFramebufferPresentationFailureReason.FramebufferRangeOverflow;
            return false;
        }
        required *= descriptor.BytesPerPixel;
        if (required == 0 || required > descriptor.FramebufferSize)
        {
            failure = ManagedFramebufferPresentationFailureReason.FramebufferRangeOverflow;
            return false;
        }
        if (descriptor.PixelFormat == ManagedPhysicalFramebufferPixelFormat.BitMask)
        {
            if (descriptor.RedMask == 0 || descriptor.GreenMask == 0 ||
                descriptor.BlueMask == 0 ||
                (descriptor.RedMask & descriptor.GreenMask) != 0 ||
                (descriptor.RedMask & descriptor.BlueMask) != 0 ||
                (descriptor.GreenMask & descriptor.BlueMask) != 0 ||
                (descriptor.RedMask & descriptor.ReservedMask) != 0 ||
                (descriptor.GreenMask & descriptor.ReservedMask) != 0 ||
                (descriptor.BlueMask & descriptor.ReservedMask) != 0 ||
                !IsContiguous(descriptor.RedMask) || !IsContiguous(descriptor.GreenMask) ||
                !IsContiguous(descriptor.BlueMask) || !IsContiguous(descriptor.ReservedMask))
            {
                failure = ManagedFramebufferPresentationFailureReason.UnsupportedPhysicalPixelFormat;
                return false;
            }
        }
        else if (descriptor.PixelFormat != ManagedPhysicalFramebufferPixelFormat.RedGreenBlueReserved8 &&
                 descriptor.PixelFormat != ManagedPhysicalFramebufferPixelFormat.BlueGreenRedReserved8)
        {
            failure = ManagedFramebufferPresentationFailureReason.UnsupportedPhysicalPixelFormat;
            return false;
        }
        if (descriptor.FramebufferBase > nuint.MaxValue ||
            descriptor.FramebufferSize > nuint.MaxValue)
        {
            failure = ManagedFramebufferPresentationFailureReason.FramebufferMappingFailure;
            return false;
        }
        return true;
    }

    private static bool IsContiguous(uint mask)
    {
        if (mask == 0) return true;
        while ((mask & 1U) == 0) mask >>= 1;
        while ((mask & 1U) != 0) mask >>= 1;
        return mask == 0;
    }

    private static uint PackPixel(in ManagedPhysicalFramebufferDescriptor descriptor,
                                  uint argb)
    {
        uint red = (argb >> 16) & 0xFFU;
        uint green = (argb >> 8) & 0xFFU;
        uint blue = argb & 0xFFU;
        if (descriptor.PixelFormat == ManagedPhysicalFramebufferPixelFormat.RedGreenBlueReserved8)
            return red | (green << 8) | (blue << 16);
        if (descriptor.PixelFormat == ManagedPhysicalFramebufferPixelFormat.BlueGreenRedReserved8)
            return blue | (green << 8) | (red << 16);
        uint packed = PackChannel(red, descriptor.RedMask) |
                      PackChannel(green, descriptor.GreenMask) |
                      PackChannel(blue, descriptor.BlueMask);
        if (descriptor.ReservedMask != 0)
            packed |= descriptor.ReservedMask;
        return packed;
    }

    private static uint PackChannel(uint value, uint mask)
    {
        if (mask == 0) return 0;
        int shift = 0;
        uint shifted = mask;
        while ((shifted & 1U) == 0) { shifted >>= 1; ++shift; }
        uint maximum = shifted;
        uint scaled = maximum == 0xFFU ? value : (value * maximum + 127U) / 255U;
        return (scaled << shift) & mask;
    }

    private static bool TryHashSource(in ManagedFramebuffer source, Span<byte> destination)
    {
        ManagedSha256 hash = new();
        if (!hash.Append(SourceHashDomain)) return false;
        Span<byte> bytes = stackalloc byte[4];
        for (int y = 0; y != source.Height; ++y)
        {
            for (int x = 0; x != source.Width; ++x)
            {
                if (!source.TryGetPixel(x, y, out uint pixel)) return false;
                bytes[0] = (byte)(pixel >> 24);
                bytes[1] = (byte)(pixel >> 16);
                bytes[2] = (byte)(pixel >> 8);
                bytes[3] = (byte)pixel;
                if (!hash.Append(bytes)) return false;
            }
        }
        return hash.TryFinalize(destination);
    }

    private static bool AppendArgb(ManagedSha256 hash, uint argb)
    {
        Span<byte> bytes = stackalloc byte[4];
        bytes[0] = (byte)(argb >> 24);
        bytes[1] = (byte)(argb >> 16);
        bytes[2] = (byte)(argb >> 8);
        bytes[3] = (byte)argb;
        return hash.Append(bytes);
    }

    private static bool AppendUInt32(ManagedSha256 hash, uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        bytes[0] = (byte)(value >> 24);
        bytes[1] = (byte)(value >> 16);
        bytes[2] = (byte)(value >> 8);
        bytes[3] = (byte)value;
        return hash.Append(bytes);
    }

    private static bool Equal(byte[] left, byte[] right)
    {
        if (left.Length != right.Length) return false;
        int difference = 0;
        for (int index = 0; index != left.Length; ++index)
            difference |= left[index] ^ right[index];
        return difference == 0;
    }
}
