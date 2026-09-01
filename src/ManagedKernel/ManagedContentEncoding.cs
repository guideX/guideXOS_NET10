using System;

namespace GuideXOS.Net10.ManagedKernel;

public enum ManagedHttpContentEncodingState : byte
{
    Missing = 0,
    Identity = 1,
    Gzip = 2,
    Deflate = 3,
    Unsupported = 4,
    Malformed = 5,
    TooLong = 6
}

public enum ManagedContentDecoderState : byte
{
    Idle = 0,
    Receiving = 1,
    Paused = 2,
    Completed = 3,
    Cancelled = 4,
    Failed = 5
}

public enum ManagedContentDecoderFailureReason : byte
{
    None = 0,
    UnsupportedEncoding = 1,
    MalformedGzipHeader = 2,
    MalformedZlibHeader = 3,
    MalformedDeflateStream = 4,
    GzipCrcMismatch = 5,
    GzipIsizeMismatch = 6,
    ZlibAdlerMismatch = 7,
    TruncatedCompressedStream = 8,
    DecodedResourceLimitExceeded = 9,
    TrailingCompressedData = 10,
    GzipOptionalFieldTooLong = 11,
    GzipHeaderCrcMismatch = 12,
    DownstreamConsumerFailure = 13
}

public enum ManagedContentDecoderProcessResult : byte
{
    NeedInput = 0,
    OutputAvailable = 1,
    Complete = 2,
    Failed = 3,
    Cancelled = 4
}

public static class ManagedContentEncodingLimits
{
    public const int MaximumContentEncodingLength = 32;
    public const int InputStagingSize = 1024;
    public const int OutputWindowSize = 1024;
    public const int HistoryWindowSize = 32 * 1024;
    public const int MaximumGzipOptionalFieldLength = 1024;
    public const int MaximumDecodedResourceLength = 4 * 1024 * 1024;
}

/* A small RFC 1951 decoder owned by the managed kernel.  It intentionally
   exposes a pull/push boundary rather than a Stream implementation: input is
   copied into a fixed staging array, output remains in one fixed window until
   the downstream sink accepts it, and Pump never allocates. */
public sealed class ManagedContentEncodingDecoder
{
    private enum WrapperPhase : byte
    {
        Header = 0,
        Deflate = 1,
        Trailer = 2,
        Complete = 3
    }

    private enum GzipHeaderPhase : byte
    {
        Fixed = 0,
        ExtraLengthLow = 1,
        ExtraLengthHigh = 2,
        ExtraData = 3,
        FileName = 4,
        Comment = 5,
        HeaderCrcLow = 6,
        HeaderCrcHigh = 7
    }

    private enum DeflatePhase : byte
    {
        BlockHeader = 0,
        StoredLength = 1,
        StoredData = 2,
        HuffmanSymbol = 3,
        LengthExtra = 4,
        DistanceSymbol = 5,
        DistanceExtra = 6,
        MatchCopy = 7,
        Finished = 8,
        DynamicHeader = 9,
        DynamicCodeLengths = 10
    }

    private enum StepResult : byte
    {
        Continue = 0,
        NeedInput = 1,
        OutputFull = 2,
        Complete = 3,
        Failed = 4
    }

    private const byte GzipFlagHeaderCrc = 2;
    private const byte GzipFlagExtra = 4;
    private const byte GzipFlagName = 8;
    private const byte GzipFlagComment = 16;

    private static readonly byte[] CodeLengthOrder =
    {
        16, 17, 18, 0, 8, 7, 9, 6, 10, 5, 11, 4, 12, 3, 13, 2, 14, 1, 15
    };

    private static readonly int[] LengthBase =
    {
        3, 4, 5, 6, 7, 8, 9, 10, 11, 13, 15, 17, 19, 23, 27, 31,
        35, 43, 51, 59, 67, 83, 99, 115, 131, 163, 195, 227, 258
    };

    private static readonly int[] LengthExtra =
    {
        0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2,
        3, 3, 3, 3, 4, 4, 4, 4, 5, 5, 5, 5, 0
    };

    private static readonly int[] DistanceBase =
    {
        1, 2, 3, 4, 5, 7, 9, 13, 17, 25, 33, 49, 65, 97, 129, 193,
        257, 385, 513, 769, 1025, 1537, 2049, 3073, 4097, 6145,
        8193, 12289, 16385, 24577
    };

    private static readonly int[] DistanceExtra =
    {
        0, 0, 0, 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 6,
        7, 7, 8, 8, 9, 9, 10, 10, 11, 11, 12, 12, 13, 13
    };

    private readonly ManagedHttpContentEncodingState _encoding;
    private readonly int _maximumDecodedLength;
    private readonly byte[] _input = new byte[ManagedContentEncodingLimits.InputStagingSize];
    private readonly byte[] _output = new byte[ManagedContentEncodingLimits.OutputWindowSize];
    private readonly byte[] _history = new byte[ManagedContentEncodingLimits.HistoryWindowSize];
    private readonly byte[] _gzipHeader = new byte[10];
    private readonly byte[] _gzipTrailer = new byte[8];
    private readonly byte[] _zlibHeader = new byte[2];
    private readonly byte[] _dynamicLengths = new byte[320];
    private readonly byte[] _literalLengths = new byte[288];
    private readonly ushort[] _literalCodes = new ushort[288];
    private readonly byte[] _distanceLengths = new byte[32];
    private readonly ushort[] _distanceCodes = new ushort[32];
    private readonly byte[] _codeLengthLengths = new byte[19];
    private readonly ushort[] _codeLengthCodes = new ushort[19];
    private readonly int[] _treeCounts = new int[16];
    private readonly int[] _treeNextCodes = new int[16];

    private ManagedContentDecoderState _state;
    private ManagedContentDecoderFailureReason _failureReason;
    private WrapperPhase _wrapperPhase;
    private GzipHeaderPhase _gzipHeaderPhase;
    private DeflatePhase _deflatePhase;
    private int _inputOffset;
    private int _inputLength;
    private int _outputLength;
    private int _historyWrite;
    private int _historyLength;
    private uint _bitBuffer;
    private int _bitCount;
    private bool _gzip;
    private byte _gzipFlags;
    private int _gzipHeaderIndex;
    private int _gzipExtraLength;
    private int _gzipOptionalBytes;
    private uint _gzipHeaderCrc;
    private uint _gzipDataCrc;
    private uint _adlerA;
    private uint _adlerB;
    private int _trailerIndex;
    private int _zlibHeaderIndex;
    private int _dynamicHlit;
    private int _dynamicHdist;
    private int _dynamicHclen;
    private int _dynamicHeaderIndex;
    private int _dynamicReadCount;
    private int _dynamicRepeatValue;
    private int _dynamicRepeatCount;
    private byte _dynamicRepeatSymbol;
    private bool _blockFinal;
    private int _blockHeaderProgress;
    private int _storedLength;
    private int _storedLengthIndex;
    private int _matchLength;
    private int _matchDistance;
    private int _matchExtraBits;
    private int _symbolTree;
    private int _symbolCode;
    private int _symbolLength;
    private bool _decoderPaused;
    private int _decodedLength;
    private int _encodedBytesAccepted;
    private int _decodedBytesConsumed;
    private int _pauseCount;
    private int _resumeCount;
    private bool _crcValidated;
    private bool _isizeValidated;
    private bool _adlerValidated;
    private bool _failedByNoInput;

    public ManagedContentEncodingDecoder(
        ManagedHttpContentEncodingState encoding,
        int maximumDecodedLength = ManagedContentEncodingLimits.MaximumDecodedResourceLength)
    {
        if (encoding != ManagedHttpContentEncodingState.Gzip &&
            encoding != ManagedHttpContentEncodingState.Deflate)
            throw new ArgumentOutOfRangeException(nameof(encoding));
        if (maximumDecodedLength < 0 ||
            maximumDecodedLength > ManagedContentEncodingLimits.MaximumDecodedResourceLength)
            throw new ArgumentOutOfRangeException(nameof(maximumDecodedLength));
        _encoding = encoding;
        _maximumDecodedLength = maximumDecodedLength;
        Reset();
    }

    public ManagedContentEncodingDecoder(
        bool gzip,
        int maximumDecodedLength = ManagedContentEncodingLimits.MaximumDecodedResourceLength)
        : this(gzip ? ManagedHttpContentEncodingState.Gzip :
                      ManagedHttpContentEncodingState.Deflate,
               maximumDecodedLength)
    {
    }

    public ManagedHttpContentEncodingState Encoding => _encoding;
    public int MaximumDecodedLength => _maximumDecodedLength;
    public ManagedContentDecoderState State => _state;
    public ManagedContentDecoderFailureReason FailureReason => _failureReason;
    public int InputLength => _inputLength;
    public int InputFreeCapacity => _input.Length - _inputLength;
    public int OutputLength => _outputLength;
    public int BufferedOutputLength => _outputLength;
    public int DecodedBytesProduced => _decodedLength;
    public int DecodedBytesConsumed => _decodedBytesConsumed;
    public int EncodedBytesAccepted => _encodedBytesAccepted;
    public int HistoryWindowSize => _history.Length;
    public int HistoryBytesAvailable => _historyLength;
    public int PauseCount => _pauseCount;
    public int ResumeCount => _resumeCount;
    public bool CrcValidated => _crcValidated;
    public bool IsizeValidated => _isizeValidated;
    public bool AdlerValidated => _adlerValidated;
    public bool IsComplete => _state == ManagedContentDecoderState.Completed;
    public bool IsTerminal => _state == ManagedContentDecoderState.Completed ||
                              _state == ManagedContentDecoderState.Cancelled ||
                              _state == ManagedContentDecoderState.Failed;

    public bool AppendInput(ReadOnlySpan<byte> input)
    {
        if (input.Length > InputFreeCapacity ||
            _state == ManagedContentDecoderState.Cancelled ||
            _state == ManagedContentDecoderState.Failed)
            return false;
        if (_state == ManagedContentDecoderState.Completed)
        {
            if (input.Length != 0) Fail(ManagedContentDecoderFailureReason.TrailingCompressedData);
            return false;
        }
        if (input.Length == 0) return true;
        if (_inputOffset + _inputLength + input.Length > _input.Length)
        {
            _input.AsSpan(_inputOffset, _inputLength).CopyTo(_input);
            _inputOffset = 0;
        }
        input.CopyTo(_input.AsSpan(_inputOffset + _inputLength));
        _inputLength += input.Length;
        _encodedBytesAccepted += input.Length;
        if (_state == ManagedContentDecoderState.Idle)
            _state = ManagedContentDecoderState.Receiving;
        return true;
    }

    public ManagedContentDecoderProcessResult Pump(bool endOfInput = false)
    {
        if (_state == ManagedContentDecoderState.Cancelled)
            return ManagedContentDecoderProcessResult.Cancelled;
        if (_state == ManagedContentDecoderState.Failed)
            return ManagedContentDecoderProcessResult.Failed;
        if (_state == ManagedContentDecoderState.Completed)
            return ManagedContentDecoderProcessResult.Complete;
        _failedByNoInput = false;
        while (true)
        {
            if (_outputLength == _output.Length)
                return ManagedContentDecoderProcessResult.OutputAvailable;
            StepResult step = _wrapperPhase == WrapperPhase.Header
                ? StepHeader()
                : _wrapperPhase == WrapperPhase.Deflate
                    ? StepDeflate()
                    : _wrapperPhase == WrapperPhase.Trailer
                        ? StepTrailer()
                        : StepResult.Complete;
            if (step == StepResult.Continue) continue;
            if (step == StepResult.OutputFull)
                return ManagedContentDecoderProcessResult.OutputAvailable;
            if (step == StepResult.Complete)
            {
                _state = ManagedContentDecoderState.Completed;
                if (_inputLength != 0)
                {
                    Fail(ManagedContentDecoderFailureReason.TrailingCompressedData);
                    return ManagedContentDecoderProcessResult.Failed;
                }
                return ManagedContentDecoderProcessResult.Complete;
            }
            if (step == StepResult.Failed)
                return ManagedContentDecoderProcessResult.Failed;
            if (endOfInput)
            {
                Fail(ManagedContentDecoderFailureReason.TruncatedCompressedStream);
                return ManagedContentDecoderProcessResult.Failed;
            }
            _failedByNoInput = true;
            return ManagedContentDecoderProcessResult.NeedInput;
        }
    }

    public ManagedHttpBodyDeliveryResult ConsumeOutput(IManagedHttpBodySink sink)
    {
        if (sink == null) throw new ArgumentNullException(nameof(sink));
        if (_state == ManagedContentDecoderState.Cancelled)
            return ManagedHttpBodyDeliveryResult.Cancelled;
        if (_state == ManagedContentDecoderState.Failed)
            return ManagedHttpBodyDeliveryResult.Failed;
        if (_outputLength == 0) return ManagedHttpBodyDeliveryResult.NoData;
        ManagedHttpBodySinkResult result = sink.Consume(
            _output.AsSpan(0, _outputLength));
        if (result == ManagedHttpBodySinkResult.Pause)
        {
            if (!_decoderPaused)
            {
                _decoderPaused = true;
                _pauseCount++;
            }
            _state = ManagedContentDecoderState.Paused;
            return ManagedHttpBodyDeliveryResult.Paused;
        }
        if (result == ManagedHttpBodySinkResult.Fail)
        {
            Fail(ManagedContentDecoderFailureReason.DownstreamConsumerFailure);
            return ManagedHttpBodyDeliveryResult.Failed;
        }
        _decodedBytesConsumed += _outputLength;
        _output.AsSpan(0, _outputLength).Clear();
        _outputLength = 0;
        if (_decoderPaused)
        {
            _decoderPaused = false;
            _resumeCount++;
        }
        if (_state == ManagedContentDecoderState.Paused)
            _state = ManagedContentDecoderState.Receiving;
        return ManagedHttpBodyDeliveryResult.Delivered;
    }

    public void Cancel()
    {
        if (!IsTerminal) _state = ManagedContentDecoderState.Cancelled;
        _input.AsSpan().Clear();
        _output.AsSpan().Clear();
        _inputOffset = 0;
        _inputLength = 0;
        _outputLength = 0;
    }

    public void Reset()
    {
        _state = ManagedContentDecoderState.Idle;
        _failureReason = ManagedContentDecoderFailureReason.None;
        _wrapperPhase = WrapperPhase.Header;
        _gzipHeaderPhase = GzipHeaderPhase.Fixed;
        _deflatePhase = DeflatePhase.BlockHeader;
        _inputOffset = 0;
        _inputLength = 0;
        _outputLength = 0;
        _historyWrite = 0;
        _historyLength = 0;
        _bitBuffer = 0;
        _bitCount = 0;
        _gzip = _encoding == ManagedHttpContentEncodingState.Gzip;
        _gzipFlags = 0;
        _gzipHeaderIndex = 0;
        _gzipExtraLength = 0;
        _gzipOptionalBytes = 0;
        _gzipHeaderCrc = 0xFFFFFFFFU;
        _gzipDataCrc = 0xFFFFFFFFU;
        _adlerA = 1;
        _adlerB = 0;
        _trailerIndex = 0;
        _zlibHeaderIndex = 0;
        _dynamicHlit = 0;
        _dynamicHdist = 0;
        _dynamicHclen = 0;
        _dynamicHeaderIndex = 0;
        _dynamicReadCount = 0;
        _dynamicRepeatValue = 0;
        _dynamicRepeatCount = 0;
        _dynamicRepeatSymbol = 0;
        _blockFinal = false;
        _blockHeaderProgress = 0;
        _storedLength = 0;
        _storedLengthIndex = 0;
        _matchLength = 0;
        _matchDistance = 0;
        _matchExtraBits = 0;
        _symbolTree = 0;
        _symbolCode = 0;
        _symbolLength = 0;
        _decoderPaused = false;
        _decodedLength = 0;
        _encodedBytesAccepted = 0;
        _decodedBytesConsumed = 0;
        _pauseCount = 0;
        _resumeCount = 0;
        _crcValidated = false;
        _isizeValidated = false;
        _adlerValidated = false;
        _failedByNoInput = false;
        _input.AsSpan().Clear();
        _output.AsSpan().Clear();
        _history.AsSpan().Clear();
        _gzipHeader.AsSpan().Clear();
        _gzipTrailer.AsSpan().Clear();
        _zlibHeader.AsSpan().Clear();
        _dynamicLengths.AsSpan().Clear();
        _literalLengths.AsSpan().Clear();
        _literalCodes.AsSpan().Clear();
        _distanceLengths.AsSpan().Clear();
        _distanceCodes.AsSpan().Clear();
        _codeLengthLengths.AsSpan().Clear();
        _codeLengthCodes.AsSpan().Clear();
        BuildFixedTrees();
    }

    private StepResult StepHeader()
    {
        return _gzip ? StepGzipHeader() : StepZlibHeader();
    }

    private StepResult StepGzipHeader()
    {
        while (true)
        {
            switch (_gzipHeaderPhase)
            {
                case GzipHeaderPhase.Fixed:
                    if (!TryReadInputByte(out byte fixedByte)) return StepResult.NeedInput;
                    _gzipHeader[_gzipHeaderIndex++] = fixedByte;
                    UpdateCrc(ref _gzipHeaderCrc, fixedByte);
                    if (_gzipHeaderIndex != _gzipHeader.Length) continue;
                    if (_gzipHeader[0] != 0x1F || _gzipHeader[1] != 0x8B ||
                        _gzipHeader[2] != 8)
                    {
                        return Fail(ManagedContentDecoderFailureReason.MalformedGzipHeader);
                    }
                    _gzipFlags = _gzipHeader[3];
                    if ((_gzipFlags & 0xE0) != 0)
                    {
                        return Fail(ManagedContentDecoderFailureReason.MalformedGzipHeader);
                    }
                    if ((_gzipFlags & GzipFlagExtra) != 0)
                    {
                        _gzipHeaderPhase = GzipHeaderPhase.ExtraLengthLow;
                        continue;
                    }
                    if ((_gzipFlags & GzipFlagName) != 0)
                    {
                        _gzipHeaderPhase = GzipHeaderPhase.FileName;
                        _gzipOptionalBytes = 0;
                        continue;
                    }
                    if ((_gzipFlags & GzipFlagComment) != 0)
                    {
                        _gzipHeaderPhase = GzipHeaderPhase.Comment;
                        _gzipOptionalBytes = 0;
                        continue;
                    }
                    if ((_gzipFlags & GzipFlagHeaderCrc) != 0)
                    {
                        _gzipHeaderPhase = GzipHeaderPhase.HeaderCrcLow;
                        continue;
                    }
                    return FinishGzipHeader();

                case GzipHeaderPhase.ExtraLengthLow:
                    if (!TryReadHeaderByte(out byte extraLow)) return StepResult.NeedInput;
                    _gzipExtraLength = extraLow;
                    _gzipHeaderPhase = GzipHeaderPhase.ExtraLengthHigh;
                    continue;

                case GzipHeaderPhase.ExtraLengthHigh:
                    if (!TryReadHeaderByte(out byte extraHigh)) return StepResult.NeedInput;
                    _gzipExtraLength |= extraHigh << 8;
                    if (_gzipExtraLength > ManagedContentEncodingLimits.MaximumGzipOptionalFieldLength)
                        return Fail(ManagedContentDecoderFailureReason.GzipOptionalFieldTooLong);
                    _gzipOptionalBytes = 0;
                    _gzipHeaderPhase = _gzipExtraLength == 0
                        ? NextGzipOptionalPhase() : GzipHeaderPhase.ExtraData;
                    continue;

                case GzipHeaderPhase.ExtraData:
                    if (_gzipOptionalBytes == _gzipExtraLength)
                    {
                        _gzipHeaderPhase = NextGzipOptionalPhase();
                        continue;
                    }
                    if (!TryReadHeaderByte(out _)) return StepResult.NeedInput;
                    _gzipOptionalBytes++;
                    continue;

                case GzipHeaderPhase.FileName:
                    if (_gzipOptionalBytes == ManagedContentEncodingLimits.MaximumGzipOptionalFieldLength)
                        return Fail(ManagedContentDecoderFailureReason.GzipOptionalFieldTooLong);
                    if (!TryReadHeaderByte(out byte nameByte)) return StepResult.NeedInput;
                    _gzipOptionalBytes++;
                    if (nameByte == 0)
                        _gzipHeaderPhase = NextGzipOptionalPhase();
                    continue;

                case GzipHeaderPhase.Comment:
                    if (_gzipOptionalBytes == ManagedContentEncodingLimits.MaximumGzipOptionalFieldLength)
                        return Fail(ManagedContentDecoderFailureReason.GzipOptionalFieldTooLong);
                    if (!TryReadHeaderByte(out byte commentByte)) return StepResult.NeedInput;
                    _gzipOptionalBytes++;
                    if (commentByte == 0)
                        _gzipHeaderPhase = (_gzipFlags & GzipFlagHeaderCrc) != 0
                            ? GzipHeaderPhase.HeaderCrcLow : GzipHeaderPhase.Fixed;
                    if (_gzipHeaderPhase == GzipHeaderPhase.Fixed)
                        return FinishGzipHeader();
                    continue;

                case GzipHeaderPhase.HeaderCrcLow:
                    if (!TryReadInputByte(out byte crcLow)) return StepResult.NeedInput;
                    _gzipTrailer[0] = crcLow;
                    _gzipHeaderPhase = GzipHeaderPhase.HeaderCrcHigh;
                    continue;

                case GzipHeaderPhase.HeaderCrcHigh:
                    if (!TryReadInputByte(out byte crcHigh)) return StepResult.NeedInput;
                    ushort expected = (ushort)(_gzipTrailer[0] | (crcHigh << 8));
                    if ((ushort)(~_gzipHeaderCrc) != expected)
                        return Fail(ManagedContentDecoderFailureReason.GzipHeaderCrcMismatch);
                    return FinishGzipHeader();
            }
        }
    }

    private GzipHeaderPhase NextGzipOptionalPhase()
    {
        if ((_gzipFlags & GzipFlagName) != 0 && _gzipHeaderPhase != GzipHeaderPhase.FileName)
        {
            _gzipOptionalBytes = 0;
            return GzipHeaderPhase.FileName;
        }
        if ((_gzipFlags & GzipFlagComment) != 0 && _gzipHeaderPhase != GzipHeaderPhase.Comment)
        {
            _gzipOptionalBytes = 0;
            return GzipHeaderPhase.Comment;
        }
        return (_gzipFlags & GzipFlagHeaderCrc) != 0
            ? GzipHeaderPhase.HeaderCrcLow : GzipHeaderPhase.Fixed;
    }

    private StepResult FinishGzipHeader()
    {
        _wrapperPhase = WrapperPhase.Deflate;
        _deflatePhase = DeflatePhase.BlockHeader;
        return StepResult.Continue;
    }

    private StepResult StepZlibHeader()
    {
        while (_zlibHeaderIndex != 2)
        {
            if (!TryReadInputByte(out byte value)) return StepResult.NeedInput;
            _zlibHeader[_zlibHeaderIndex++] = value;
        }
        byte cmf = _zlibHeader[0];
        byte flg = _zlibHeader[1];
        if ((cmf & 0x0F) != 8 || (cmf >> 4) > 7 ||
            (((cmf << 8) | flg) % 31) != 0)
            return Fail(ManagedContentDecoderFailureReason.MalformedZlibHeader);
        if ((flg & 0x20) != 0)
            return Fail(ManagedContentDecoderFailureReason.MalformedZlibHeader);
        _wrapperPhase = WrapperPhase.Deflate;
        _deflatePhase = DeflatePhase.BlockHeader;
        return StepResult.Continue;
    }

    private StepResult StepDeflate()
    {
        while (true)
        {
            if (_outputLength == _output.Length) return StepResult.OutputFull;
            switch (_deflatePhase)
            {
                case DeflatePhase.BlockHeader:
                    if (_blockHeaderProgress == 0)
                    {
                        if (!TryReadBits(1, out uint final)) return StepResult.NeedInput;
                        _blockFinal = final != 0;
                        _blockHeaderProgress = 1;
                    }
                    if (!TryReadBits(2, out uint kind)) return StepResult.NeedInput;
                    _blockHeaderProgress = 0;
                    if (kind == 0)
                    {
                        AlignToByte();
                        _storedLength = 0;
                        _storedLengthIndex = 0;
                        _deflatePhase = DeflatePhase.StoredLength;
                        continue;
                    }
                    if (kind == 1)
                    {
                        BuildFixedTrees();
                        ResetSymbolDecoder();
                        _deflatePhase = DeflatePhase.HuffmanSymbol;
                        continue;
                    }
                    if (kind == 2)
                    {
                        _dynamicHeaderIndex = 0;
                        _dynamicReadCount = 0;
                        _codeLengthLengths.AsSpan().Clear();
                        _deflatePhase = DeflatePhase.DynamicHeader;
                        continue;
                    }
                    return Fail(ManagedContentDecoderFailureReason.MalformedDeflateStream);

                case DeflatePhase.StoredLength:
                    if (_storedLengthIndex == 0)
                    {
                        if (!TryReadBits(16, out uint length)) return StepResult.NeedInput;
                        _storedLength = (int)length;
                        _storedLengthIndex = 1;
                    }
                    if (!TryReadBits(16, out uint inverse)) return StepResult.NeedInput;
                    _storedLengthIndex = 0;
                    if ((((uint)_storedLength) ^ inverse) != 0xFFFFU)
                        return Fail(ManagedContentDecoderFailureReason.MalformedDeflateStream);
                    _deflatePhase = _storedLength == 0 && _blockFinal
                        ? DeflatePhase.Finished : DeflatePhase.StoredData;
                    if (_deflatePhase == DeflatePhase.Finished)
                    {
                        AlignToByte();
                        _wrapperPhase = WrapperPhase.Trailer;
                        _trailerIndex = 0;
                        continue;
                    }
                    continue;

                case DeflatePhase.StoredData:
                    if (_storedLength == 0)
                    {
                        _deflatePhase = _blockFinal
                            ? DeflatePhase.Finished : DeflatePhase.BlockHeader;
                        continue;
                    }
                    if (!TryReadBits(8, out uint storedByte)) return StepResult.NeedInput;
                    if (!TryAppendOutput((byte)storedByte))
                        return _state == ManagedContentDecoderState.Failed
                            ? StepResult.Failed : StepResult.OutputFull;
                    _storedLength--;
                    continue;

                case DeflatePhase.DynamicHeader:
                    if (_dynamicHeaderIndex == 0)
                    {
                        if (!TryReadBits(5, out uint hlit)) return StepResult.NeedInput;
                        _dynamicHlit = (int)hlit + 257;
                        _dynamicHeaderIndex = 1;
                    }
                    if (_dynamicHeaderIndex == 1)
                    {
                        if (!TryReadBits(5, out uint hdist)) return StepResult.NeedInput;
                        _dynamicHdist = (int)hdist + 1;
                        _dynamicHeaderIndex = 2;
                    }
                    if (_dynamicHeaderIndex == 2)
                    {
                        if (!TryReadBits(4, out uint hclen)) return StepResult.NeedInput;
                        _dynamicHclen = (int)hclen + 4;
                        _dynamicHeaderIndex = 3;
                    }
                    while (_dynamicReadCount < _dynamicHclen)
                    {
                        if (!TryReadBits(3, out uint codeLength)) return StepResult.NeedInput;
                        _codeLengthLengths[CodeLengthOrder[_dynamicReadCount]] = (byte)codeLength;
                        _dynamicReadCount++;
                    }
                    if (!BuildTree(_codeLengthLengths, _codeLengthCodes, 19, true))
                        return StepResult.Failed;
                    _dynamicReadCount = 0;
                    _dynamicHeaderIndex = 0;
                    ResetSymbolDecoder();
                    _deflatePhase = DeflatePhase.DynamicCodeLengths;
                    continue;

                case DeflatePhase.DynamicCodeLengths:
                    if (_dynamicReadCount == _dynamicHlit + _dynamicHdist)
                    {
                        _literalLengths.AsSpan().Clear();
                        _distanceLengths.AsSpan().Clear();
                        _dynamicLengths.AsSpan(0, _dynamicHlit).CopyTo(_literalLengths);
                        _dynamicLengths.AsSpan(_dynamicHlit, _dynamicHdist).CopyTo(_distanceLengths);
                        if (!BuildTree(_literalLengths, _literalCodes, 288, false) ||
                            !BuildTree(_distanceLengths, _distanceCodes, 32, false))
                            return StepResult.Failed;
                        ResetSymbolDecoder();
                        _deflatePhase = DeflatePhase.HuffmanSymbol;
                        continue;
                    }
                    int total = _dynamicHlit + _dynamicHdist;
                    if (_dynamicRepeatSymbol == 0)
                    {
                        if (!TryDecodeSymbol(_codeLengthLengths, _codeLengthCodes, 19,
                                             3, out int codeLengthSymbol))
                            return _failedByNoInput ? StepResult.NeedInput : StepResult.Failed;
                        if (codeLengthSymbol <= 15)
                        {
                            _dynamicLengths[_dynamicReadCount++] = (byte)codeLengthSymbol;
                            continue;
                        }
                        if (codeLengthSymbol != 16 && codeLengthSymbol != 17 &&
                            codeLengthSymbol != 18)
                            return Fail(ManagedContentDecoderFailureReason.MalformedDeflateStream);
                        _dynamicRepeatSymbol = (byte)codeLengthSymbol;
                    }
                    if (_dynamicRepeatSymbol == 16)
                    {
                        if (_dynamicReadCount == 0)
                            return Fail(ManagedContentDecoderFailureReason.MalformedDeflateStream);
                        if (!TryReadBits(2, out uint extra16)) return StepResult.NeedInput;
                        _dynamicRepeatCount = (int)extra16 + 3;
                        _dynamicRepeatValue = _dynamicLengths[_dynamicReadCount - 1];
                    }
                    else if (_dynamicRepeatSymbol == 17)
                    {
                        if (!TryReadBits(3, out uint extra17)) return StepResult.NeedInput;
                        _dynamicRepeatCount = (int)extra17 + 3;
                        _dynamicRepeatValue = 0;
                    }
                    else if (_dynamicRepeatSymbol == 18)
                    {
                        if (!TryReadBits(7, out uint extra18)) return StepResult.NeedInput;
                        _dynamicRepeatCount = (int)extra18 + 11;
                        _dynamicRepeatValue = 0;
                    }
                    if (_dynamicReadCount + _dynamicRepeatCount > total)
                        return Fail(ManagedContentDecoderFailureReason.MalformedDeflateStream);
                    while (_dynamicRepeatCount != 0)
                    {
                        _dynamicLengths[_dynamicReadCount++] = (byte)_dynamicRepeatValue;
                        _dynamicRepeatCount--;
                    }
                    _dynamicRepeatSymbol = 0;
                    continue;

                case DeflatePhase.HuffmanSymbol:
                    if (!TryDecodeSymbol(_literalLengths, _literalCodes, 288,
                                         1, out int literal))
                        return _failedByNoInput ? StepResult.NeedInput : StepResult.Failed;
                    if (literal < 256)
                    {
                        if (!TryAppendOutput((byte)literal))
                            return _state == ManagedContentDecoderState.Failed
                                ? StepResult.Failed : StepResult.OutputFull;
                        continue;
                    }
                    if (literal == 256)
                    {
                        AlignToByte();
                        if (_blockFinal)
                        {
                            _deflatePhase = DeflatePhase.Finished;
                            _wrapperPhase = WrapperPhase.Trailer;
                            _trailerIndex = 0;
                            return StepResult.Continue;
                        }
                        else
                        {
                            _deflatePhase = DeflatePhase.BlockHeader;
                        }
                        continue;
                    }
                    if (literal > 285)
                        return Fail(ManagedContentDecoderFailureReason.MalformedDeflateStream);
                    int lengthIndex = literal - 257;
                    _matchLength = LengthBase[lengthIndex];
                    _matchExtraBits = LengthExtra[lengthIndex];
                    _deflatePhase = DeflatePhase.LengthExtra;
                    continue;

                case DeflatePhase.LengthExtra:
                    if (_matchExtraBits != 0)
                    {
                        if (!TryReadBits(_matchExtraBits, out uint lengthExtra))
                            return StepResult.NeedInput;
                        _matchLength += (int)lengthExtra;
                        _matchExtraBits = 0;
                    }
                    ResetSymbolDecoder();
                    _deflatePhase = DeflatePhase.DistanceSymbol;
                    continue;

                case DeflatePhase.DistanceSymbol:
                    if (!TryDecodeSymbol(_distanceLengths, _distanceCodes, 32,
                                         2, out int distanceSymbol))
                        return _failedByNoInput ? StepResult.NeedInput : StepResult.Failed;
                    if (distanceSymbol > 29)
                        return Fail(ManagedContentDecoderFailureReason.MalformedDeflateStream);
                    _matchDistance = DistanceBase[distanceSymbol];
                    _matchExtraBits = DistanceExtra[distanceSymbol];
                    _deflatePhase = DeflatePhase.DistanceExtra;
                    continue;

                case DeflatePhase.DistanceExtra:
                    if (_matchExtraBits != 0)
                    {
                        if (!TryReadBits(_matchExtraBits, out uint distanceExtra))
                            return StepResult.NeedInput;
                        _matchDistance += (int)distanceExtra;
                        _matchExtraBits = 0;
                    }
                    if (_matchDistance > ManagedContentEncodingLimits.HistoryWindowSize ||
                        _matchDistance > _historyLength)
                        return Fail(ManagedContentDecoderFailureReason.MalformedDeflateStream);
                    _deflatePhase = DeflatePhase.MatchCopy;
                    continue;

                case DeflatePhase.MatchCopy:
                    while (_matchLength != 0)
                    {
                        if (_outputLength == _output.Length) return StepResult.OutputFull;
                        int source = _historyWrite - _matchDistance;
                        if (source < 0) source += _history.Length;
                        byte copy = _history[source];
                        if (!TryAppendOutput(copy))
                            return _state == ManagedContentDecoderState.Failed
                                ? StepResult.Failed : StepResult.OutputFull;
                        _matchLength--;
                    }
                    _deflatePhase = DeflatePhase.HuffmanSymbol;
                    continue;

                case DeflatePhase.Finished:
                    AlignToByte();
                    _wrapperPhase = WrapperPhase.Trailer;
                    _trailerIndex = 0;
                    return StepResult.Continue;
            }
        }
    }

    private StepResult StepTrailer()
    {
        if (_gzip)
        {
            while (_trailerIndex != 8)
            {
                if (!TryReadInputByte(out byte value)) return StepResult.NeedInput;
                _gzipTrailer[_trailerIndex++] = value;
            }
            uint expectedCrc = (uint)(_gzipTrailer[0] | (_gzipTrailer[1] << 8) |
                                      (_gzipTrailer[2] << 16) | (_gzipTrailer[3] << 24));
            uint expectedSize = (uint)(_gzipTrailer[4] | (_gzipTrailer[5] << 8) |
                                       (_gzipTrailer[6] << 16) | (_gzipTrailer[7] << 24));
            if ((~_gzipDataCrc) != expectedCrc)
                return Fail(ManagedContentDecoderFailureReason.GzipCrcMismatch);
            _crcValidated = true;
            if (((uint)_decodedLength) != expectedSize)
                return Fail(ManagedContentDecoderFailureReason.GzipIsizeMismatch);
            _isizeValidated = true;
        }
        else
        {
            while (_trailerIndex != 4)
            {
                if (!TryReadInputByte(out byte value)) return StepResult.NeedInput;
                _gzipTrailer[_trailerIndex++] = value;
            }
            uint expectedAdler = (uint)((_gzipTrailer[0] << 24) |
                                        (_gzipTrailer[1] << 16) |
                                        (_gzipTrailer[2] << 8) | _gzipTrailer[3]);
            uint actualAdler = (_adlerB << 16) | _adlerA;
            if (actualAdler != expectedAdler)
                return Fail(ManagedContentDecoderFailureReason.ZlibAdlerMismatch);
            _adlerValidated = true;
        }
        _wrapperPhase = WrapperPhase.Complete;
        return StepResult.Complete;
    }

    private bool TryAppendOutput(byte value)
    {
        if (_outputLength == _output.Length) return false;
        if (_decodedLength == _maximumDecodedLength)
        {
            Fail(ManagedContentDecoderFailureReason.DecodedResourceLimitExceeded);
            return false;
        }
        _output[_outputLength++] = value;
        _decodedLength++;
        _history[_historyWrite] = value;
        _historyWrite++;
        if (_historyWrite == _history.Length) _historyWrite = 0;
        if (_historyLength != _history.Length) _historyLength++;
        UpdateCrc(ref _gzipDataCrc, value);
        uint nextA = _adlerA + value;
        if (nextA >= 65521) nextA -= 65521;
        _adlerA = nextA;
        uint nextB = _adlerB + _adlerA;
        if (nextB >= 65521) nextB %= 65521;
        _adlerB = nextB;
        return true;
    }

    private bool TryReadInputByte(out byte value)
    {
        if (_inputLength == 0)
        {
            value = 0;
            _failedByNoInput = true;
            return false;
        }
        value = _input[_inputOffset++];
        _inputLength--;
        if (_inputLength == 0) _inputOffset = 0;
        return true;
    }

    private bool TryReadHeaderByte(out byte value)
    {
        if (!TryReadInputByte(out value)) return false;
        UpdateCrc(ref _gzipHeaderCrc, value);
        return true;
    }

    private bool TryReadBits(int count, out uint value)
    {
        while (_bitCount < count && _inputLength != 0)
        {
            _bitBuffer |= (uint)_input[_inputOffset++] << _bitCount;
            _inputLength--;
            if (_inputLength == 0) _inputOffset = 0;
            _bitCount += 8;
        }
        if (_bitCount < count)
        {
            value = 0;
            _failedByNoInput = true;
            return false;
        }
        uint mask = (1U << count) - 1U;
        value = _bitBuffer & mask;
        _bitBuffer >>= count;
        _bitCount -= count;
        return true;
    }

    private void AlignToByte()
    {
        int discard = _bitCount & 7;
        _bitBuffer >>= discard;
        _bitCount -= discard;
    }

    private void ResetSymbolDecoder()
    {
        _symbolTree = 0;
        _symbolCode = 0;
        _symbolLength = 0;
    }

    private bool TryDecodeSymbol(byte[] lengths, ushort[] codes, int symbolCount,
                                 int treeKind, out int symbol)
    {
        if (_symbolTree != treeKind)
        {
            _symbolTree = treeKind;
            _symbolCode = 0;
            _symbolLength = 0;
        }
        while (_symbolLength < 15)
        {
            if (!TryReadBits(1, out uint bit))
            {
                symbol = 0;
                return false;
            }
            _symbolCode |= (int)bit << _symbolLength;
            _symbolLength++;
            for (int index = 0; index != symbolCount; ++index)
            {
                if (lengths[index] == _symbolLength &&
                    codes[index] == _symbolCode)
                {
                    symbol = index;
                    ResetSymbolDecoder();
                    return true;
                }
            }
        }
        symbol = 0;
        Fail(ManagedContentDecoderFailureReason.MalformedDeflateStream);
        return false;
    }

    private bool BuildTree(byte[] lengths, ushort[] codes, int symbolCount,
                           bool codeLengthTree)
    {
        _treeCounts.AsSpan().Clear();
        int symbols = 0;
        for (int index = 0; index != symbolCount; ++index)
        {
            byte length = lengths[index];
            if (length > 15) return FailState(ManagedContentDecoderFailureReason.MalformedDeflateStream);
            if (length != 0)
            {
                _treeCounts[length]++;
                symbols++;
            }
            codes[index] = 0;
        }
        if (symbols == 0) return FailState(ManagedContentDecoderFailureReason.MalformedDeflateStream);
        int left = 1;
        for (int bits = 1; bits != 16; ++bits)
        {
            left = (left << 1) - _treeCounts[bits];
            if (left < 0)
                return FailState(ManagedContentDecoderFailureReason.MalformedDeflateStream);
        }
        if (left != 0 && symbols != 1)
            return FailState(ManagedContentDecoderFailureReason.MalformedDeflateStream);
        _treeNextCodes.AsSpan().Clear();
        int code = 0;
        for (int bits = 1; bits != 16; ++bits)
        {
            code = (code + _treeCounts[bits - 1]) << 1;
            _treeNextCodes[bits] = code;
        }
        for (int index = 0; index != symbolCount; ++index)
        {
            int length = lengths[index];
            if (length == 0) continue;
            int canonical = _treeNextCodes[length]++;
            codes[index] = (ushort)ReverseBits((uint)canonical, length);
        }
        return true;
    }

    private void BuildFixedTrees()
    {
        for (int index = 0; index != _literalLengths.Length; ++index)
            _literalLengths[index] = index <= 143 ? (byte)8 :
                index <= 255 ? (byte)9 : index <= 279 ? (byte)7 : (byte)8;
        for (int index = 0; index != _distanceLengths.Length; ++index)
            _distanceLengths[index] = 5;
        BuildTree(_literalLengths, _literalCodes, 288, false);
        BuildTree(_distanceLengths, _distanceCodes, 32, false);
    }

    private static uint ReverseBits(uint value, int count)
    {
        uint result = 0;
        for (int index = 0; index != count; ++index)
        {
            result = (result << 1) | (value & 1U);
            value >>= 1;
        }
        return result;
    }

    private static void UpdateCrc(ref uint crc, byte value)
    {
        crc ^= value;
        for (int bit = 0; bit != 8; ++bit)
            crc = (crc & 1U) != 0 ? (crc >> 1) ^ 0xEDB88320U : crc >> 1;
    }

    private StepResult Fail(ManagedContentDecoderFailureReason reason)
    {
        _failureReason = reason;
        _state = ManagedContentDecoderState.Failed;
        return StepResult.Failed;
    }

    private bool FailState(ManagedContentDecoderFailureReason reason)
    {
        _failureReason = reason;
        _state = ManagedContentDecoderState.Failed;
        return false;
    }
}
