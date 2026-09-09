using System;

namespace GuideXOS.Net10.ManagedKernel;

public static class ManagedImageLimits
{
    public const int DefaultImageCapacity = 4;
    public const int MaximumImageCapacity = 16;
    public const int DefaultMaximumWidth = 256;
    public const int DefaultMaximumHeight = 256;
    public const int DefaultPixelBudget = 65_536;
    public const int MaximumChunkLength = ManagedHttpLimits.MaximumStreamedBodyLength;
    public const int MaximumSourceAliasesPerImage = 8;
}

public readonly struct ManagedImageHandle : IEquatable<ManagedImageHandle>
{
    internal ManagedImageHandle(int slot, uint generation)
    {
        Slot = slot;
        Generation = generation;
    }

    public static ManagedImageHandle Invalid => default;
    public int Slot { get; }
    public uint Generation { get; }
    public bool IsValid => Slot >= 0 && Generation != 0;
    public bool Equals(ManagedImageHandle other) => Slot == other.Slot &&
        Generation == other.Generation;
    public override bool Equals(object? obj) => obj is ManagedImageHandle other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Slot, Generation);
    public static bool operator ==(ManagedImageHandle left, ManagedImageHandle right) => left.Equals(right);
    public static bool operator !=(ManagedImageHandle left, ManagedImageHandle right) => !left.Equals(right);
}

public enum ManagedImageState : byte
{
    Empty = 0,
    Reserved = 1,
    Complete = 2
}

public readonly struct ManagedPageImageDescriptor
{
    internal ManagedPageImageDescriptor(ManagedImageHandle handle, int sourceNodeIndex,
                                        int width, int height, int pixelStart,
                                        int pixelCount, ManagedImageState state)
    {
        Handle = handle;
        SourceNodeIndex = sourceNodeIndex;
        Width = width;
        Height = height;
        PixelStart = pixelStart;
        PixelCount = pixelCount;
        State = state;
    }

    public ManagedImageHandle Handle { get; }
    public int SourceNodeIndex { get; }
    public int Width { get; }
    public int Height { get; }
    public int PixelStart { get; }
    public int PixelCount { get; }
    public ManagedImageState State { get; }
}

internal struct ManagedPageImageRecord
{
    internal int SourceNodeIndex;
    internal int Width;
    internal int Height;
    internal int PixelStart;
    internal int PixelCount;
    internal ManagedImageState State;
}

/// <summary>
/// One fixed cumulative ARGB8888 arena.  Image handles carry the store
/// generation, so a Reset makes every prior handle invalid without relying on
/// object identity or a per-image allocation.
/// </summary>
public sealed class ManagedPageImageStore
{
    private readonly ManagedPageImageRecord[] _records;
    private readonly uint[] _pixels;
    private readonly byte[] _decodedHashes;
    private readonly int[] _sourceAliases;
    private readonly byte[] _sourceAliasCounts;
    private readonly int _maximumWidth;
    private readonly int _maximumHeight;
    private uint _generation;
    private int _count;
    private int _pixelUsed;

    public ManagedPageImageStore(int imageCapacity = ManagedImageLimits.DefaultImageCapacity,
                                 int pixelBudget = ManagedImageLimits.DefaultPixelBudget,
                                 int maximumWidth = ManagedImageLimits.DefaultMaximumWidth,
                                 int maximumHeight = ManagedImageLimits.DefaultMaximumHeight)
    {
        if (imageCapacity <= 0 || imageCapacity > ManagedImageLimits.MaximumImageCapacity)
            throw new ArgumentOutOfRangeException(nameof(imageCapacity));
        if (pixelBudget <= 0 || pixelBudget > ManagedImageLimits.DefaultPixelBudget)
            throw new ArgumentOutOfRangeException(nameof(pixelBudget));
        if (maximumWidth <= 0 || maximumWidth > ManagedImageLimits.DefaultMaximumWidth)
            throw new ArgumentOutOfRangeException(nameof(maximumWidth));
        if (maximumHeight <= 0 || maximumHeight > ManagedImageLimits.DefaultMaximumHeight)
            throw new ArgumentOutOfRangeException(nameof(maximumHeight));
        _records = new ManagedPageImageRecord[imageCapacity];
        _pixels = new uint[pixelBudget];
        _decodedHashes = new byte[imageCapacity * ManagedSha256.DigestSize];
        _sourceAliases = new int[checked(imageCapacity * ManagedImageLimits.MaximumSourceAliasesPerImage)];
        _sourceAliasCounts = new byte[imageCapacity];
        PixelBudget = pixelBudget;
        _maximumWidth = maximumWidth;
        _maximumHeight = maximumHeight;
        Reset();
    }

    public int Capacity => _records.Length;
    public int PixelBudget { get; }
    public int PixelArenaBytes => checked(_pixels.Length * sizeof(uint));
    public int MaximumWidth => _maximumWidth;
    public int MaximumHeight => _maximumHeight;
    public int Count => _count;
    public int PixelUsed => _pixelUsed;
    public uint Generation => _generation;
    public uint[] PixelArena => _pixels;

    public void Reset()
    {
        _records.AsSpan().Clear();
        _pixels.AsSpan().Clear();
        _decodedHashes.AsSpan().Clear();
        _sourceAliases.AsSpan().Fill(-1);
        _sourceAliasCounts.AsSpan().Clear();
        ++_generation;
        if (_generation == 0) _generation = 1;
        _count = 0;
        _pixelUsed = 0;
    }

    public bool TryReserve(int sourceNodeIndex, int width, int height,
                           out ManagedImageHandle handle)
    {
        handle = ManagedImageHandle.Invalid;
        if (sourceNodeIndex < 0 || width <= 0 || height <= 0 ||
            width > _maximumWidth || height > _maximumHeight || _count == _records.Length)
            return false;
        long count = (long)width * height;
        if (count > int.MaxValue || count > _pixels.Length - _pixelUsed)
            return false;
        int slot = _count++;
        _records[slot] = new ManagedPageImageRecord
        {
            SourceNodeIndex = sourceNodeIndex,
            Width = width,
            Height = height,
            PixelStart = _pixelUsed,
            PixelCount = (int)count,
            State = ManagedImageState.Reserved
        };
        _pixelUsed += (int)count;
        handle = new ManagedImageHandle(slot, _generation);
        return true;
    }

    public bool TryComplete(ManagedImageHandle handle, ReadOnlySpan<byte> decodedHash)
    {
        if (!TryGetRecord(handle, out int slot, out ManagedPageImageRecord record) ||
            record.State != ManagedImageState.Reserved ||
            decodedHash.Length < ManagedSha256.DigestSize)
            return false;
        decodedHash[..ManagedSha256.DigestSize].CopyTo(
            _decodedHashes.AsSpan(slot * ManagedSha256.DigestSize, ManagedSha256.DigestSize));
        record.State = ManagedImageState.Complete;
        _records[slot] = record;
        return true;
    }

    public bool TryAbort(ManagedImageHandle handle)
    {
        if (!TryGetRecord(handle, out int slot, out ManagedPageImageRecord record)) return false;
        if (slot == _count - 1)
        {
            _pixelUsed -= record.PixelCount;
            _records[slot] = default;
            --_count;
        }
        else
        {
            record.State = ManagedImageState.Empty;
            _records[slot] = record;
        }
        return true;
    }

    public bool TryGetDescriptor(ManagedImageHandle handle, out ManagedPageImageDescriptor descriptor)
    {
        descriptor = default;
        if (!TryGetRecord(handle, out _, out ManagedPageImageRecord record) ||
            record.State != ManagedImageState.Complete)
            return false;
        descriptor = new(handle, record.SourceNodeIndex, record.Width, record.Height,
                          record.PixelStart, record.PixelCount, record.State);
        return true;
    }

    public bool TryGetForSourceNode(int sourceNodeIndex, out ManagedImageHandle handle)
    {
        handle = ManagedImageHandle.Invalid;
        if (sourceNodeIndex < 0) return false;
        for (int slot = 0; slot != _count; ++slot)
        {
            ManagedPageImageRecord record = _records[slot];
            if (record.State == ManagedImageState.Complete &&
                (record.SourceNodeIndex == sourceNodeIndex || HasSourceAlias(slot, sourceNodeIndex)))
            {
                handle = new ManagedImageHandle(slot, _generation);
                return true;
            }
        }
        return false;
    }

    public bool TryAssociateSourceNode(ManagedImageHandle handle, int sourceNodeIndex)
    {
        if (!TryGetRecord(handle, out int slot, out ManagedPageImageRecord record) ||
            record.State != ManagedImageState.Complete || sourceNodeIndex < 0)
            return false;
        if (record.SourceNodeIndex == sourceNodeIndex || HasSourceAlias(slot, sourceNodeIndex))
            return true;
        int count = _sourceAliasCounts[slot];
        if (count == ManagedImageLimits.MaximumSourceAliasesPerImage) return false;
        _sourceAliases[slot * ManagedImageLimits.MaximumSourceAliasesPerImage + count] = sourceNodeIndex;
        _sourceAliasCounts[slot] = (byte)(count + 1);
        return true;
    }

    public bool TryReadPixel(ManagedImageHandle handle, int x, int y, out uint pixel)
    {
        pixel = 0;
        if (!TryGetRecord(handle, out _, out ManagedPageImageRecord record) ||
            record.State != ManagedImageState.Complete || x < 0 || y < 0 ||
            x >= record.Width || y >= record.Height)
            return false;
        pixel = _pixels[record.PixelStart + y * record.Width + x];
        return true;
    }

    public bool TryWritePixel(ManagedImageHandle handle, int index, uint pixel)
    {
        if (!TryGetRecord(handle, out _, out ManagedPageImageRecord record) ||
            record.State != ManagedImageState.Reserved || index < 0 || index >= record.PixelCount)
            return false;
        _pixels[record.PixelStart + index] = pixel;
        return true;
    }

    public bool TryCopyDecodedHash(ManagedImageHandle handle, Span<byte> destination)
    {
        if (!TryGetRecord(handle, out int slot, out ManagedPageImageRecord record) ||
            record.State != ManagedImageState.Complete ||
            destination.Length < ManagedSha256.DigestSize)
            return false;
        _decodedHashes.AsSpan(slot * ManagedSha256.DigestSize,
                              ManagedSha256.DigestSize).CopyTo(destination);
        return true;
    }

    private bool TryGetRecord(ManagedImageHandle handle, out int slot,
                              out ManagedPageImageRecord record)
    {
        slot = handle.Slot;
        record = default;
        if (!handle.IsValid || handle.Generation != _generation ||
            slot < 0 || slot >= _count)
            return false;
        record = _records[slot];
        return true;
    }

    private bool HasSourceAlias(int slot, int sourceNodeIndex)
    {
        int count = _sourceAliasCounts[slot];
        int offset = slot * ManagedImageLimits.MaximumSourceAliasesPerImage;
        for (int index = 0; index != count; ++index)
            if (_sourceAliases[offset + index] == sourceNodeIndex) return true;
        return false;
    }
}

public enum ManagedPngFailureReason : byte
{
    None = 0,
    SignatureMismatch = 1,
    Truncated = 2,
    ChunkLengthExceeded = 3,
    InvalidChunkOrder = 4,
    InvalidChunkCrc = 5,
    InvalidIhdr = 6,
    UnsupportedBitDepth = 7,
    UnsupportedColorType = 8,
    UnsupportedCompression = 9,
    UnsupportedFilterMethod = 10,
    UnsupportedInterlace = 11,
    InvalidDimensions = 12,
    PixelBudgetExceeded = 13,
    MissingIdat = 14,
    InvalidIdatSequence = 15,
    InvalidIend = 16,
    InvalidFilter = 17,
    DecodedSizeMismatch = 18,
    InflateFailure = 19,
    UnsupportedTransparency = 20,
    UnknownCriticalChunk = 21,
    TrailingData = 22,
    StoreFailure = 23
}

public readonly struct ManagedPngTelemetry
{
    internal ManagedPngTelemetry(ManagedPngDecoder decoder)
    {
        SourceNodeIndex = decoder.SourceNodeIndex;
        Width = decoder.Width;
        Height = decoder.Height;
        BitDepth = decoder.BitDepth;
        ColorType = decoder.ColorType;
        IdatChunkCount = decoder.IdatChunkCount;
        IdatCompressedBytes = decoder.IdatCompressedBytes;
        InflatedBytes = decoder.InflatedBytes;
        DecodedPixels = decoder.DecodedPixels;
        ResourceBytes = decoder.BytesProcessed;
        FilterNone = decoder.FilterNone;
        FilterSub = decoder.FilterSub;
        FilterUp = decoder.FilterUp;
        FilterAverage = decoder.FilterAverage;
        FilterPaeth = decoder.FilterPaeth;
        FailureReason = decoder.PngFailureReason;
        State = decoder.State;
    }

    public int SourceNodeIndex { get; }
    public int Width { get; }
    public int Height { get; }
    public int BitDepth { get; }
    public int ColorType { get; }
    public int IdatChunkCount { get; }
    public int IdatCompressedBytes { get; }
    public int InflatedBytes { get; }
    public int DecodedPixels { get; }
    public int ResourceBytes { get; }
    public int FilterNone { get; }
    public int FilterSub { get; }
    public int FilterUp { get; }
    public int FilterAverage { get; }
    public int FilterPaeth { get; }
    public ManagedPngFailureReason FailureReason { get; }
    public ManagedResourceConsumerState State { get; }
}

/// <summary>
/// Incremental PNG parser and scanline decoder.  It consumes the entity after
/// HTTP Content-Encoding has been removed and uses the existing bounded zlib
/// decoder for the PNG IDAT stream.
/// </summary>
public sealed class ManagedPngDecoder : IManagedResourceConsumer
{
    private enum ParseState : byte { Signature, ChunkHeader, ChunkData, ChunkCrc, Done, Failed }

    private static readonly byte[] Signature = { 137, 80, 78, 71, 13, 10, 26, 10 };
    private readonly ManagedPageImageStore _store;
    private readonly byte[] _chunkHeader = new byte[8];
    private readonly byte[] _ihdr = new byte[13];
    private readonly byte[] _chunkType = new byte[4];
    private readonly byte[] _chunkCrcBytes = new byte[4];
    private readonly byte[] _previousRow = new byte[ManagedImageLimits.DefaultMaximumWidth * 4];
    private readonly byte[] _currentRow = new byte[ManagedImageLimits.DefaultMaximumWidth * 4];
    private readonly ManagedPngInflatedSink _inflatedSink;
    private readonly ManagedSha256 _resourceHash = new();
    private readonly ManagedSha256 _decodedHash = new();
    private readonly byte[] _resourceDigest = new byte[ManagedSha256.DigestSize];
    private readonly byte[] _decodedDigest = new byte[ManagedSha256.DigestSize];
    private readonly byte[] _canonicalPixel = new byte[4];
    private ManagedContentEncodingDecoder? _zlib;
    private ManagedImageHandle _handle;
    private ParseState _parseState;
    private ManagedResourceConsumerState _state;
    private ManagedPngFailureReason _failureReason;
    private int _sourceNodeIndex;
    private int _signatureIndex;
    private int _chunkHeaderIndex;
    private int _chunkCrcIndex;
    private int _chunkRemaining;
    private uint _chunkLength;
    private uint _chunkCrc;
    private uint _expectedCrc;
    private int _ihdrLength;
    private bool _ihdrSeen;
    private bool _idatSeen;
    private bool _idatClosed;
    private bool _iendSeen;
    private int _width;
    private int _height;
    private int _bitDepth;
    private int _colorType;
    private int _bytesPerPixel;
    private int _rowBytes;
    private int _expectedInflated;
    private int _rowIndex;
    private int _rowPosition;
    private byte _filter;
    private int _inflatedBytes;
    private int _decodedPixels;
    private int _idatChunkCount;
    private int _idatCompressedBytes;
    private int _resourceBytes;
    private int _filterNone;
    private int _filterSub;
    private int _filterUp;
    private int _filterAverage;
    private int _filterPaeth;

    public ManagedPngDecoder(ManagedPageImageStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _inflatedSink = new ManagedPngInflatedSink(this);
        Reset();
    }

    public ManagedResourceConsumerState State => _state;
    public ManagedResourceConsumerFailureReason FailureReason =>
        _failureReason == ManagedPngFailureReason.None
            ? ManagedResourceConsumerFailureReason.None
            : ManagedResourceConsumerFailureReason.ConsumerFailure;
    public ManagedPngFailureReason PngFailureReason => _failureReason;
    public ManagedPngFailureReason FailureReasonCode => _failureReason;
    public int BytesProcessed => _resourceBytes;
    public int SourceNodeIndex => _sourceNodeIndex;
    public int Width => _width;
    public int Height => _height;
    public int BitDepth => _bitDepth;
    public int ColorType => _colorType;
    public int IdatChunkCount => _idatChunkCount;
    public int IdatCompressedBytes => _idatCompressedBytes;
    public int InflatedBytes => _inflatedBytes;
    public int DecodedPixels => _decodedPixels;
    public int FilterNone => _filterNone;
    public int FilterSub => _filterSub;
    public int FilterUp => _filterUp;
    public int FilterAverage => _filterAverage;
    public int FilterPaeth => _filterPaeth;
    public ManagedPngTelemetry Telemetry => new(this);
    public bool IsComplete => _state == ManagedResourceConsumerState.Completed;

    public bool TryCopyResourceDigest(Span<byte> destination)
    {
        if (_state != ManagedResourceConsumerState.Completed ||
            destination.Length < ManagedSha256.DigestSize) return false;
        _resourceDigest.AsSpan().CopyTo(destination);
        return true;
    }

    public bool TryCopyDecodedPixelDigest(Span<byte> destination)
    {
        if (_state != ManagedResourceConsumerState.Completed ||
            destination.Length < ManagedSha256.DigestSize) return false;
        _decodedDigest.AsSpan().CopyTo(destination);
        return true;
    }

    public bool TryGetImageHandle(out ManagedImageHandle handle)
    {
        handle = _handle;
        return IsComplete && _handle.IsValid;
    }

    public void Start(int sourceNodeIndex)
    {
        Reset();
        _sourceNodeIndex = sourceNodeIndex;
        _state = ManagedResourceConsumerState.Receiving;
    }

    public ManagedHttpBodySinkResult Consume(ReadOnlySpan<byte> segment)
    {
        if (_state != ManagedResourceConsumerState.Receiving ||
            !_resourceHash.Append(segment) || segment.Length > int.MaxValue - _resourceBytes)
            return FailConsumer();
        _resourceBytes += segment.Length;
        for (int index = 0; index != segment.Length; ++index)
            if (!ProcessByte(segment[index])) return FailConsumer();
        return ManagedHttpBodySinkResult.Continue;
    }

    public bool Complete()
    {
        if (_state == ManagedResourceConsumerState.Completed) return true;
        if (_state != ManagedResourceConsumerState.Receiving) return false;
        if (!_iendSeen || !_idatSeen || _rowIndex != _height ||
            _rowPosition != 0 || !_zlibComplete())
            return Fail(ManagedPngFailureReason.Truncated);
        if (!_resourceHash.TryFinalize(_resourceDigest) ||
            !_decodedHash.TryFinalize(_decodedDigest) ||
            !_store.TryComplete(_handle, _decodedDigest))
            return Fail(ManagedPngFailureReason.StoreFailure);
        _state = ManagedResourceConsumerState.Completed;
        return true;
    }

    public void Cancel()
    {
        if (_state == ManagedResourceConsumerState.Completed) return;
        if (_handle.IsValid) _store.TryAbort(_handle);
        _state = ManagedResourceConsumerState.Cancelled;
    }

    public void Reset()
    {
        if (_handle.IsValid && _state == ManagedResourceConsumerState.Receiving)
            _store.TryAbort(_handle);
        _handle = ManagedImageHandle.Invalid;
        _parseState = ParseState.Signature;
        _state = ManagedResourceConsumerState.Idle;
        _failureReason = ManagedPngFailureReason.None;
        _sourceNodeIndex = -1;
        _signatureIndex = 0;
        _chunkHeaderIndex = 0;
        _chunkCrcIndex = 0;
        _chunkRemaining = 0;
        _chunkLength = 0;
        _chunkCrc = 0;
        _expectedCrc = 0;
        _ihdrLength = 0;
        _ihdrSeen = false;
        _idatSeen = false;
        _idatClosed = false;
        _iendSeen = false;
        _width = 0;
        _height = 0;
        _bitDepth = 0;
        _colorType = 0;
        _bytesPerPixel = 0;
        _rowBytes = 0;
        _expectedInflated = 0;
        _rowIndex = 0;
        _rowPosition = 0;
        _filter = 0;
        _inflatedBytes = 0;
        _decodedPixels = 0;
        _idatChunkCount = 0;
        _idatCompressedBytes = 0;
        _resourceBytes = 0;
        _filterNone = 0;
        _filterSub = 0;
        _filterUp = 0;
        _filterAverage = 0;
        _filterPaeth = 0;
        _zlib?.Reset();
        _zlib = null;
        _resourceHash.Reset();
        _decodedHash.Reset();
        _resourceDigest.AsSpan().Clear();
        _decodedDigest.AsSpan().Clear();
        _previousRow.AsSpan().Clear();
        _currentRow.AsSpan().Clear();
        _chunkHeader.AsSpan().Clear();
        _ihdr.AsSpan().Clear();
        _chunkType.AsSpan().Clear();
        _chunkCrcBytes.AsSpan().Clear();
    }

    private bool ProcessByte(byte value)
    {
        if (_parseState == ParseState.Done)
            return Fail(ManagedPngFailureReason.TrailingData);
        switch (_parseState)
        {
            case ParseState.Signature:
                if (value != Signature[_signatureIndex++])
                    return Fail(ManagedPngFailureReason.SignatureMismatch);
                if (_signatureIndex == Signature.Length) _parseState = ParseState.ChunkHeader;
                return true;
            case ParseState.ChunkHeader:
                _chunkHeader[_chunkHeaderIndex++] = value;
                if (_chunkHeaderIndex != _chunkHeader.Length) return true;
                _chunkHeaderIndex = 0;
                _chunkLength = ((uint)_chunkHeader[0] << 24) |
                    ((uint)_chunkHeader[1] << 16) | ((uint)_chunkHeader[2] << 8) | _chunkHeader[3];
                if (_chunkLength > ManagedImageLimits.MaximumChunkLength || _chunkLength > int.MaxValue)
                    return Fail(ManagedPngFailureReason.ChunkLengthExceeded);
                for (int index = 0; index != 4; ++index) _chunkType[index] = _chunkHeader[index + 4];
                if (!BeginChunk((int)_chunkLength)) return false;
                return _chunkRemaining == 0 ? BeginChunkCrc() : true;
            case ParseState.ChunkData:
                if (!ConsumeChunkDataByte(value)) return false;
                --_chunkRemaining;
                if (_chunkRemaining == 0) return BeginChunkCrc();
                return true;
            case ParseState.ChunkCrc:
                _chunkCrcBytes[_chunkCrcIndex++] = value;
                if (_chunkCrcIndex != 4) return true;
                _chunkCrcIndex = 0;
                _expectedCrc = ((uint)_chunkCrcBytes[0] << 24) |
                    ((uint)_chunkCrcBytes[1] << 16) | ((uint)_chunkCrcBytes[2] << 8) | _chunkCrcBytes[3];
                if (~_chunkCrc != _expectedCrc)
                    return Fail(ManagedPngFailureReason.InvalidChunkCrc);
                if (IsType("IHDR"u8))
                {
                    if (!ParseIhdr()) return false;
                    _parseState = ParseState.ChunkHeader;
                }
                else if (IsType("IEND"u8))
                {
                    if (_chunkLength != 0 || !_idatSeen || !_zlibComplete() ||
                        _rowIndex != _height || _rowPosition != 0)
                        return Fail(ManagedPngFailureReason.InvalidIend);
                    _iendSeen = true;
                    _parseState = ParseState.Done;
                }
                else _parseState = ParseState.ChunkHeader;
                return true;
            default:
                return false;
        }
    }

    private bool BeginChunk(int length)
    {
        bool ihdr = IsType("IHDR"u8);
        bool idat = IsType("IDAT"u8);
        bool iend = IsType("IEND"u8);
        if (!_ihdrSeen && !ihdr) return Fail(ManagedPngFailureReason.InvalidChunkOrder);
        if (ihdr && _ihdrSeen) return Fail(ManagedPngFailureReason.InvalidChunkOrder);
        if (idat && _idatClosed) return Fail(ManagedPngFailureReason.InvalidIdatSequence);
        if (idat)
        {
            if (!_ihdrSeen || _zlib == null) return Fail(ManagedPngFailureReason.MissingIdat);
            _idatSeen = true;
            ++_idatChunkCount;
            _idatCompressedBytes = checked(_idatCompressedBytes + length);
        }
        else if (_idatSeen) _idatClosed = true;
        if (iend && !_idatSeen) return Fail(ManagedPngFailureReason.InvalidIend);
        if (IsType("tRNS"u8)) return Fail(ManagedPngFailureReason.UnsupportedTransparency);
        if (!ihdr && !idat && !iend && !IsKnownNonCritical(_chunkType) &&
            !IsAncillary(_chunkType))
            return Fail(ManagedPngFailureReason.UnknownCriticalChunk);
        _chunkRemaining = length;
        _chunkCrc = 0xFFFFFFFFU;
        for (int index = 0; index != 4; ++index) UpdateCrc(_chunkType[index]);
        _ihdrLength = 0;
        _parseState = length == 0 ? ParseState.ChunkCrc : ParseState.ChunkData;
        return true;
    }

    private bool BeginChunkCrc()
    {
        _chunkCrcIndex = 0;
        _parseState = ParseState.ChunkCrc;
        return true;
    }

    private bool ConsumeChunkDataByte(byte value)
    {
        UpdateCrc(value);
        if (IsType("IHDR"u8))
        {
            if (_ihdrLength == _ihdr.Length) return Fail(ManagedPngFailureReason.InvalidIhdr);
            _ihdr[_ihdrLength++] = value;
            return true;
        }
        if (IsType("IDAT"u8)) return FeedInflate(value);
        return true;
    }

    private bool ParseIhdr()
    {
        if (_ihdrLength != 13) return Fail(ManagedPngFailureReason.InvalidIhdr);
        uint width = BigEndian(_ihdr, 0);
        uint height = BigEndian(_ihdr, 4);
        _bitDepth = _ihdr[8];
        _colorType = _ihdr[9];
        if (width == 0 || height == 0 || width > (uint)_store.MaximumWidth ||
            height > (uint)_store.MaximumHeight)
            return Fail(ManagedPngFailureReason.InvalidDimensions);
        if (_bitDepth != 8) return Fail(ManagedPngFailureReason.UnsupportedBitDepth);
        _bytesPerPixel = _colorType switch { 2 => 3, 6 => 4, 0 => 1, 4 => 2, _ => 0 };
        if (_bytesPerPixel == 0) return Fail(ManagedPngFailureReason.UnsupportedColorType);
        if (_ihdr[10] != 0) return Fail(ManagedPngFailureReason.UnsupportedCompression);
        if (_ihdr[11] != 0) return Fail(ManagedPngFailureReason.UnsupportedFilterMethod);
        if (_ihdr[12] != 0) return Fail(ManagedPngFailureReason.UnsupportedInterlace);
        long rowBytes = (long)width * _bytesPerPixel;
        long expected = (long)height * (rowBytes + 1);
        if (rowBytes > _currentRow.Length || expected > int.MaxValue)
            return Fail(ManagedPngFailureReason.DecodedSizeMismatch);
        _width = (int)width;
        _height = (int)height;
        _rowBytes = (int)rowBytes;
        _expectedInflated = (int)expected;
        if (!_store.TryReserve(_sourceNodeIndex, _width, _height, out _handle))
            return Fail(ManagedPngFailureReason.PixelBudgetExceeded);
        _zlib = new ManagedContentEncodingDecoder(ManagedHttpContentEncodingState.Deflate,
                                                   _expectedInflated);
        _ihdrSeen = true;
        return true;
    }

    private bool FeedInflate(byte value)
    {
        Span<byte> one = stackalloc byte[1];
        one[0] = value;
        while (true)
        {
            if (_zlib == null) return false;
            if (_zlib.InputFreeCapacity == 0)
            {
                if (!PumpInflate(false)) return false;
                if (_zlib.InputFreeCapacity == 0) return Fail(ManagedPngFailureReason.InflateFailure);
            }
            if (!_zlib.AppendInput(one)) return Fail(ManagedPngFailureReason.InflateFailure);
            return PumpInflate(false);
        }
    }

    private bool PumpInflate(bool endOfInput)
    {
        if (_zlib == null) return false;
        while (true)
        {
            ManagedContentDecoderProcessResult result = _zlib.Pump(endOfInput);
            if (result == ManagedContentDecoderProcessResult.OutputAvailable)
            {
                if (_zlib.ConsumeOutput(_inflatedSink) != ManagedHttpBodyDeliveryResult.Delivered)
                    return _failureReason != ManagedPngFailureReason.None
                        ? false : Fail(ManagedPngFailureReason.InflateFailure);
                continue;
            }
            if (result == ManagedContentDecoderProcessResult.Complete) return true;
            if (result == ManagedContentDecoderProcessResult.NeedInput) return true;
            return FailInflate();
        }
    }

    private bool FinishInflate()
    {
        if (_zlib == null) return false;
        while (!_zlib.IsComplete)
        {
            ManagedContentDecoderProcessResult result = _zlib.Pump(true);
            if (result == ManagedContentDecoderProcessResult.OutputAvailable)
            {
                if (_zlib.ConsumeOutput(_inflatedSink) != ManagedHttpBodyDeliveryResult.Delivered)
                    return _failureReason != ManagedPngFailureReason.None
                        ? false : Fail(ManagedPngFailureReason.InflateFailure);
                continue;
            }
            if (result != ManagedContentDecoderProcessResult.Complete)
                return FailInflate();
        }
        return _inflatedBytes == _expectedInflated;
    }

    private bool _zlibComplete()
    {
        if (_zlib == null) return false;
        return _zlib.IsComplete || FinishInflate();
    }

    private bool FailInflate()
    {
        if (_failureReason != ManagedPngFailureReason.None) return false;
        return Fail(_zlib?.FailureReason == ManagedContentDecoderFailureReason.DecodedResourceLimitExceeded
            ? ManagedPngFailureReason.DecodedSizeMismatch
            : ManagedPngFailureReason.InflateFailure);
    }

    private bool ProcessInflatedByte(byte value)
    {
        if (_inflatedBytes == _expectedInflated)
            return Fail(ManagedPngFailureReason.DecodedSizeMismatch);
        ++_inflatedBytes;
        if (_rowPosition == 0)
        {
            if (value > 4) return Fail(ManagedPngFailureReason.InvalidFilter);
            _filter = value;
            switch (_filter)
            {
                case 0: ++_filterNone; break;
                case 1: ++_filterSub; break;
                case 2: ++_filterUp; break;
                case 3: ++_filterAverage; break;
                case 4: ++_filterPaeth; break;
            }
            _rowPosition = 1;
            return true;
        }
        int index = _rowPosition - 1;
        int left = index >= _bytesPerPixel ? _currentRow[index - _bytesPerPixel] : 0;
        int up = _rowIndex == 0 ? 0 : _previousRow[index];
        int upLeft = _rowIndex == 0 || index < _bytesPerPixel
            ? 0 : _previousRow[index - _bytesPerPixel];
        int predictor = _filter switch
        {
            0 => 0,
            1 => left,
            2 => up,
            3 => (left + up) / 2,
            4 => Paeth(left, up, upLeft),
            _ => 0
        };
        _currentRow[index] = (byte)(value + predictor);
        ++_rowPosition;
        if (_rowPosition == _rowBytes + 1)
        {
            if (!FinishRow()) return false;
            _rowPosition = 0;
        }
        return true;
    }

    private bool FinishRow()
    {
        for (int x = 0; x != _width; ++x)
        {
            int offset = x * _bytesPerPixel;
            byte r, g, b, a;
            if (_colorType == 2)
            {
                r = _currentRow[offset]; g = _currentRow[offset + 1];
                b = _currentRow[offset + 2]; a = 255;
            }
            else if (_colorType == 6)
            {
                r = _currentRow[offset]; g = _currentRow[offset + 1];
                b = _currentRow[offset + 2]; a = _currentRow[offset + 3];
            }
            else if (_colorType == 0)
            {
                r = g = b = _currentRow[offset]; a = 255;
            }
            else
            {
                r = g = b = _currentRow[offset]; a = _currentRow[offset + 1];
            }
            uint pixel = ((uint)a << 24) | ((uint)r << 16) | ((uint)g << 8) | b;
            if (!_store.TryWritePixel(_handle, _decodedPixels++, pixel))
                return Fail(ManagedPngFailureReason.StoreFailure);
            _canonicalPixel[0] = a; _canonicalPixel[1] = r;
            _canonicalPixel[2] = g; _canonicalPixel[3] = b;
            if (!_decodedHash.Append(_canonicalPixel))
                return Fail(ManagedPngFailureReason.StoreFailure);
        }
        _currentRow.AsSpan(0, _rowBytes).CopyTo(_previousRow);
        ++_rowIndex;
        return true;
    }

    private ManagedHttpBodySinkResult FailConsumer()
    {
        if (_failureReason == ManagedPngFailureReason.None)
            _failureReason = ManagedPngFailureReason.Truncated;
        _state = ManagedResourceConsumerState.Failed;
        _parseState = ParseState.Failed;
        if (_handle.IsValid) _store.TryAbort(_handle);
        return ManagedHttpBodySinkResult.Fail;
    }

    private bool Fail(ManagedPngFailureReason reason)
    {
        _failureReason = reason;
        _state = ManagedResourceConsumerState.Failed;
        _parseState = ParseState.Failed;
        if (_handle.IsValid) _store.TryAbort(_handle);
        return false;
    }

    private bool IsType(ReadOnlySpan<byte> type) => _chunkType.AsSpan().SequenceEqual(type);

    private static bool IsAncillary(ReadOnlySpan<byte> type) =>
        type.Length == 4 && (type[0] & 0x20) != 0;

    private static bool IsKnownNonCritical(ReadOnlySpan<byte> type) =>
        type.SequenceEqual("PLTE"u8);

    private void UpdateCrc(byte value)
    {
        _chunkCrc ^= value;
        for (int bit = 0; bit != 8; ++bit)
            _chunkCrc = (_chunkCrc & 1) != 0 ? (_chunkCrc >> 1) ^ 0xEDB88320U : _chunkCrc >> 1;
    }

    private static uint BigEndian(byte[] value, int offset) =>
        ((uint)value[offset] << 24) | ((uint)value[offset + 1] << 16) |
        ((uint)value[offset + 2] << 8) | value[offset + 3];

    private static int Paeth(int left, int up, int upLeft)
    {
        int p = left + up - upLeft;
        int pa = Math.Abs(p - left);
        int pb = Math.Abs(p - up);
        int pc = Math.Abs(p - upLeft);
        return pa <= pb && pa <= pc ? left : pb <= pc ? up : upLeft;
    }

    private sealed class ManagedPngInflatedSink : IManagedHttpBodySink
    {
        private readonly ManagedPngDecoder _owner;
        internal ManagedPngInflatedSink(ManagedPngDecoder owner) => _owner = owner;
        public ManagedHttpBodySinkResult Consume(ReadOnlySpan<byte> segment)
        {
            for (int index = 0; index != segment.Length; ++index)
                if (!_owner.ProcessInflatedByte(segment[index])) return ManagedHttpBodySinkResult.Fail;
            return ManagedHttpBodySinkResult.Continue;
        }
    }
}
