using System;

namespace GuideXOS.Net10.ManagedKernel;

public enum ManagedMimeClassification : byte
{
    Unknown = 0,
    TextPlain = 1,
    Html = 2,
    Css = 3,
    Json = 4,
    JavaScript = 5,
    Xml = 6,
    Textual = 7,
    Binary = 8
}

public enum ManagedContentTypeMetadataState : byte
{
    Available = 0,
    Malformed = 1
}

public enum ManagedCharsetDeclarationState : byte
{
    None = 0,
    Utf8 = 1,
    UsAscii = 2,
    Iso88591 = 3,
    Unsupported = 4,
    Empty = 5,
    Malformed = 6,
    TooLong = 7
}

public enum ManagedTextCharset : byte
{
    None = 0,
    Utf8 = 1,
    UsAscii = 2,
    Iso88591 = 3
}

public enum ManagedTextCharsetSource : byte
{
    None = 0,
    Explicit = 1,
    Bom = 2,
    Default = 3,
    Unsupported = 4
}

public readonly struct ManagedContentTypeMetadata
{
    internal ManagedContentTypeMetadata(
        ManagedContentTypeMetadataState state,
        ManagedMimeClassification classification,
        ManagedCharsetDeclarationState charsetState,
        ManagedTextCharset charset,
        int charsetLength)
    {
        State = state;
        Classification = classification;
        CharsetState = charsetState;
        Charset = charset;
        CharsetLength = charsetLength;
    }

    public ManagedContentTypeMetadataState State { get; }
    public ManagedMimeClassification Classification { get; }
    public ManagedCharsetDeclarationState CharsetState { get; }
    public ManagedTextCharset Charset { get; }
    public int CharsetLength { get; }
    public bool IsMalformed => State == ManagedContentTypeMetadataState.Malformed;
}

/* This parser deliberately handles only the bounded subset needed by the
   resource layer.  The media type is a type/subtype token.  Parameters are
   semicolon-separated name=value pairs; values are either tokens or quoted
   strings without escapes.  Only charset is retained semantically. */
public static class ManagedContentTypeParser
{
    public const int MaximumCharsetLength = 32;

    public static ManagedContentTypeMetadata Parse(ReadOnlySpan<byte> value)
    {
        int mediaEnd = value.Length;
        for (int index = 0; index != value.Length; ++index)
        {
            if (value[index] == (byte)';')
            {
                mediaEnd = index;
                break;
            }
        }
        int parameterStart = mediaEnd;
        Trim(value, 0, mediaEnd, out int mediaStart, out int trimmedMediaEnd);
        if (mediaStart == trimmedMediaEnd)
            return Malformed(ManagedMimeClassification.Unknown,
                             ManagedCharsetDeclarationState.None, 0);

        int slash = -1;
        for (int index = mediaStart; index != trimmedMediaEnd; ++index)
        {
            if (value[index] == (byte)'/' && slash < 0) slash = index;
            else if (value[index] == (byte)'/' && slash >= 0)
                return Malformed(ManagedMimeClassification.Unknown,
                                 ManagedCharsetDeclarationState.None, 0);
        }
        if (slash <= mediaStart || slash + 1 >= trimmedMediaEnd ||
            !IsToken(value.Slice(mediaStart, slash - mediaStart)) ||
            !IsToken(value.Slice(slash + 1, trimmedMediaEnd - slash - 1)))
            return Malformed(ManagedMimeClassification.Unknown,
                             ManagedCharsetDeclarationState.None, 0);

        ManagedMimeClassification classification = Classify(
            value.Slice(mediaStart, slash - mediaStart),
            value.Slice(slash + 1, trimmedMediaEnd - slash - 1));
        ManagedCharsetDeclarationState charsetState =
            ManagedCharsetDeclarationState.None;
        ManagedTextCharset charset = ManagedTextCharset.None;
        int charsetLength = 0;
        bool malformed = false;
        bool sawCharset = false;
        Span<byte> firstCharset = stackalloc byte[MaximumCharsetLength];
        int firstCharsetLength = 0;

        int offset = parameterStart;
        while (offset < value.Length)
        {
            if (value[offset] != (byte)';')
            {
                malformed = true;
                break;
            }
            ++offset;
            while (offset < value.Length && IsWhitespace(value[offset])) ++offset;
            if (offset == value.Length)
            {
                malformed = true;
                break;
            }
            int nameStart = offset;
            while (offset < value.Length && value[offset] != (byte)'=' &&
                   value[offset] != (byte)';' && !IsWhitespace(value[offset]))
                ++offset;
            int nameEnd = offset;
            while (nameEnd > nameStart && IsWhitespace(value[nameEnd - 1])) --nameEnd;
            if (nameStart == nameEnd || !IsToken(value.Slice(nameStart, nameEnd - nameStart)))
            {
                malformed = true;
                break;
            }
            while (offset < value.Length && IsWhitespace(value[offset])) ++offset;
            if (offset == value.Length || value[offset] != (byte)'=')
            {
                malformed = true;
                break;
            }
            ++offset;
            while (offset < value.Length && IsWhitespace(value[offset])) ++offset;

            bool quoted = offset < value.Length && value[offset] == (byte)'"';
            int valueStart;
            int valueEnd;
            if (quoted)
            {
                ++offset;
                valueStart = offset;
                while (offset < value.Length && value[offset] != (byte)'"')
                {
                    if (value[offset] < 0x20 || value[offset] > 0x7E ||
                        value[offset] == (byte)'\\')
                    {
                        malformed = true;
                        break;
                    }
                    ++offset;
                }
                if (malformed || offset == value.Length)
                {
                    malformed = true;
                    break;
                }
                valueEnd = offset++;
                while (offset < value.Length && IsWhitespace(value[offset])) ++offset;
                if (offset < value.Length && value[offset] != (byte)';')
                {
                    malformed = true;
                    break;
                }
            }
            else
            {
                valueStart = offset;
                while (offset < value.Length && value[offset] != (byte)';') ++offset;
                valueEnd = offset;
                while (valueEnd > valueStart && IsWhitespace(value[valueEnd - 1])) --valueEnd;
                if (valueStart == valueEnd ||
                    !IsToken(value.Slice(valueStart, valueEnd - valueStart)))
                {
                    if (EqualsAsciiIgnoreCase(value.Slice(nameStart, nameEnd - nameStart),
                                              "charset"u8))
                    {
                        charsetState = ManagedCharsetDeclarationState.Empty;
                    }
                    malformed = true;
                    break;
                }
            }

            if (EqualsAsciiIgnoreCase(value.Slice(nameStart, nameEnd - nameStart),
                                      "charset"u8))
            {
                int length = valueEnd - valueStart;
                charsetLength = length;
                if (length != 0 && !IsCharsetToken(value.Slice(valueStart, length)))
                {
                    charsetState = ManagedCharsetDeclarationState.Malformed;
                    malformed = true;
                    break;
                }
                ManagedCharsetDeclarationState candidateState;
                ManagedTextCharset candidate;
                if (length == 0)
                {
                    candidateState = ManagedCharsetDeclarationState.Empty;
                    candidate = ManagedTextCharset.None;
                }
                else if (length > MaximumCharsetLength)
                {
                    candidateState = ManagedCharsetDeclarationState.TooLong;
                    candidate = ManagedTextCharset.None;
                }
                else
                {
                    ReadOnlySpan<byte> token = value.Slice(valueStart, length);
                    candidate = ParseCharset(token, out candidateState);
                }

                if (!sawCharset)
                {
                    sawCharset = true;
                    charsetState = candidateState;
                    charset = candidate;
                    if (length <= MaximumCharsetLength)
                    {
                        tokenCopy(value.Slice(valueStart, length), firstCharset,
                                  out firstCharsetLength);
                    }
                }
                else if (length != firstCharsetLength ||
                         !EqualsAsciiIgnoreCase(value.Slice(valueStart, length),
                                                firstCharset[..firstCharsetLength]))
                {
                    malformed = true;
                    break;
                }
            }
        }

        if (malformed)
            return Malformed(classification, charsetState, charsetLength);
        return new ManagedContentTypeMetadata(
            ManagedContentTypeMetadataState.Available, classification,
            charsetState, charset, charsetLength);
    }

    private static ManagedContentTypeMetadata Malformed(
        ManagedMimeClassification classification,
        ManagedCharsetDeclarationState charsetState, int charsetLength) =>
        new(ManagedContentTypeMetadataState.Malformed, classification,
            charsetState, ManagedTextCharset.None, charsetLength);

    private static ManagedTextCharset ParseCharset(
        ReadOnlySpan<byte> token,
        out ManagedCharsetDeclarationState state)
    {
        if (EqualsAsciiIgnoreCase(token, "utf-8"u8) ||
            EqualsAsciiIgnoreCase(token, "utf8"u8))
        {
            state = ManagedCharsetDeclarationState.Utf8;
            return ManagedTextCharset.Utf8;
        }
        if (EqualsAsciiIgnoreCase(token, "us-ascii"u8) ||
            EqualsAsciiIgnoreCase(token, "ascii"u8))
        {
            state = ManagedCharsetDeclarationState.UsAscii;
            return ManagedTextCharset.UsAscii;
        }
        if (EqualsAsciiIgnoreCase(token, "iso-8859-1"u8) ||
            EqualsAsciiIgnoreCase(token, "latin-1"u8) ||
            EqualsAsciiIgnoreCase(token, "latin1"u8))
        {
            state = ManagedCharsetDeclarationState.Iso88591;
            return ManagedTextCharset.Iso88591;
        }
        state = ManagedCharsetDeclarationState.Unsupported;
        return ManagedTextCharset.None;
    }

    private static ManagedMimeClassification Classify(
        ReadOnlySpan<byte> type, ReadOnlySpan<byte> subtype)
    {
        if (EqualsAsciiIgnoreCase(type, "text"u8))
        {
            if (EqualsAsciiIgnoreCase(subtype, "plain"u8)) return ManagedMimeClassification.TextPlain;
            if (EqualsAsciiIgnoreCase(subtype, "html"u8)) return ManagedMimeClassification.Html;
            if (EqualsAsciiIgnoreCase(subtype, "css"u8)) return ManagedMimeClassification.Css;
            if (EqualsAsciiIgnoreCase(subtype, "javascript"u8)) return ManagedMimeClassification.JavaScript;
            if (EqualsAsciiIgnoreCase(subtype, "xml"u8)) return ManagedMimeClassification.Xml;
            return ManagedMimeClassification.Textual;
        }
        if (EqualsAsciiIgnoreCase(type, "application"u8))
        {
            if (EqualsAsciiIgnoreCase(subtype, "json"u8)) return ManagedMimeClassification.Json;
            if (EqualsAsciiIgnoreCase(subtype, "javascript"u8)) return ManagedMimeClassification.JavaScript;
            if (EqualsAsciiIgnoreCase(subtype, "xhtml+xml"u8)) return ManagedMimeClassification.Xml;
            if (EqualsAsciiIgnoreCase(subtype, "xml"u8)) return ManagedMimeClassification.Xml;
            if (EqualsAsciiIgnoreCase(subtype, "octet-stream"u8)) return ManagedMimeClassification.Binary;
            return ManagedMimeClassification.Unknown;
        }
        if (EqualsAsciiIgnoreCase(type, "image"u8) ||
            EqualsAsciiIgnoreCase(type, "audio"u8) ||
            EqualsAsciiIgnoreCase(type, "video"u8) ||
            EqualsAsciiIgnoreCase(type, "font"u8) ||
            EqualsAsciiIgnoreCase(type, "multipart"u8))
            return ManagedMimeClassification.Binary;
        return ManagedMimeClassification.Unknown;
    }

    private static void tokenCopy(ReadOnlySpan<byte> source, Span<byte> destination,
                                  out int length)
    {
        length = source.Length;
        source.CopyTo(destination);
    }

    private static void Trim(ReadOnlySpan<byte> value, int start, int end,
                             out int trimmedStart, out int trimmedEnd)
    {
        while (start < end && IsWhitespace(value[start])) ++start;
        while (end > start && IsWhitespace(value[end - 1])) --end;
        trimmedStart = start;
        trimmedEnd = end;
    }

    private static bool IsToken(ReadOnlySpan<byte> value)
    {
        if (value.Length == 0) return false;
        for (int index = 0; index != value.Length; ++index)
            if (!IsToken(value[index])) return false;
        return true;
    }

    private static bool IsCharsetToken(ReadOnlySpan<byte> value)
    {
        if (value.Length == 0) return false;
        for (int index = 0; index != value.Length; ++index)
        {
            byte item = value[index];
            if (!((item >= (byte)'A' && item <= (byte)'Z') ||
                  (item >= (byte)'a' && item <= (byte)'z') ||
                  (item >= (byte)'0' && item <= (byte)'9') ||
                  item == (byte)'-' || item == (byte)'_' ||
                  item == (byte)'+' || item == (byte)'.'))
                return false;
        }
        return true;
    }

    private static bool IsToken(byte value) =>
        (value >= (byte)'A' && value <= (byte)'Z') ||
        (value >= (byte)'a' && value <= (byte)'z') ||
        (value >= (byte)'0' && value <= (byte)'9') ||
        value == (byte)'!' || value == (byte)'#' || value == (byte)'$' ||
        value == (byte)'%' || value == (byte)'&' || value == (byte)'\'' ||
        value == (byte)'*' || value == (byte)'+' || value == (byte)'-' ||
        value == (byte)'.' || value == (byte)'^' || value == (byte)'_' ||
        value == (byte)'`' || value == (byte)'|' || value == (byte)'~';

    private static bool IsWhitespace(byte value) => value == (byte)' ' || value == 9;

    internal static bool EqualsAsciiIgnoreCase(ReadOnlySpan<byte> left,
                                               ReadOnlySpan<byte> right)
    {
        if (left.Length != right.Length) return false;
        for (int index = 0; index != left.Length; ++index)
        {
            byte a = left[index], b = right[index];
            if (a >= (byte)'A' && a <= (byte)'Z') a = (byte)(a + 32);
            if (b >= (byte)'A' && b <= (byte)'Z') b = (byte)(b + 32);
            if (a != b) return false;
        }
        return true;
    }
}

public enum ManagedTextConsumerFailureReason : byte
{
    None = 0,
    DestinationFull = 1,
    ConsumerFailure = 2,
    FinalizationFailure = 3
}

public interface IManagedTextConsumer
{
    ManagedResourceConsumerState State { get; }
    ManagedTextConsumerFailureReason FailureReason { get; }
    int ScalarsProcessed { get; }
    ManagedHttpBodySinkResult Consume(ReadOnlySpan<uint> segment);
    bool Complete();
    void Cancel();
    void Reset();
}

public enum ManagedTextDecoderState : byte
{
    Idle = 0,
    Receiving = 1,
    Paused = 2,
    Completed = 3,
    Cancelled = 4,
    Failed = 5
}

public enum ManagedTextDecoderFailureReason : byte
{
    None = 0,
    InvalidUtf8 = 1,
    TruncatedUtf8 = 2,
    InvalidAscii = 3,
    DownstreamConsumerFailure = 4,
    Cancelled = 5
}

public enum ManagedTextDecoderProcessResult : byte
{
    NeedInput = 0,
    OutputAvailable = 1,
    Complete = 2,
    Paused = 3,
    Failed = 4,
    Cancelled = 5
}

public static class ManagedTextDecodingLimits
{
    public const int InputWindowSize = ManagedHttpLimits.MaximumBodyDeliveryWindow;
    public const int OutputWindowCapacity = 256;
    public const int OutputWindowBytes = OutputWindowCapacity * sizeof(uint);
}

/* A strict, allocation-free Unicode scalar decoder.  Input is retained in a
   fixed byte queue, output is retained in a fixed scalar window, and a
   sequence can be split at every byte boundary. */
public sealed class ManagedTextDecoder
{
    private readonly ManagedTextCharset _charset;
    private readonly byte[] _input = new byte[ManagedTextDecodingLimits.InputWindowSize];
    private readonly uint[] _output = new uint[ManagedTextDecodingLimits.OutputWindowCapacity];
    private readonly byte[] _bom = new byte[3];
    private ManagedTextDecoderState _state;
    private ManagedTextDecoderFailureReason _failureReason;
    private int _inputOffset;
    private int _inputLength;
    private int _outputLength;
    private int _bomLength;
    private int _bomFlushIndex;
    private bool _bomDecided;
    private uint _value;
    private int _expected;
    private int _seen;
    private byte _firstMinimum;
    private byte _firstMaximum;
    private int _bytesAccepted;
    private int _bytesConsumed;
    private int _scalarsProduced;
    private int _scalarsDelivered;
    private int _segmentCount;
    private int _peakOutputLength;
    private int _pauseCount;
    private int _resumeCount;
    private bool _bomConsumed;
    private bool _paused;

    public ManagedTextDecoder(ManagedTextCharset charset)
    {
        if (charset == ManagedTextCharset.None) throw new ArgumentOutOfRangeException(nameof(charset));
        _charset = charset;
        Reset();
    }

    public ManagedTextCharset Charset => _charset;
    public ManagedTextDecoderState State => _state;
    public ManagedTextDecoderFailureReason FailureReason => _failureReason;
    public int InputLength => _inputLength;
    public int InputFreeCapacity => _input.Length - _inputLength;
    public int OutputLength => _outputLength;
    public int BufferedOutputLength => _outputLength;
    public int BytesAccepted => _bytesAccepted;
    public int BytesConsumed => _bytesConsumed;
    public int ScalarsProduced => _scalarsProduced;
    public int ScalarsDelivered => _scalarsDelivered;
    public int SegmentCount => _segmentCount;
    public int PeakOutputLength => _peakOutputLength;
    public int PauseCount => _pauseCount;
    public int ResumeCount => _resumeCount;
    public bool BomConsumed => _bomConsumed;
    public bool IsComplete => _state == ManagedTextDecoderState.Completed;
    public bool IsTerminal => _state == ManagedTextDecoderState.Completed ||
                              _state == ManagedTextDecoderState.Cancelled ||
                              _state == ManagedTextDecoderState.Failed;

    public bool AppendInput(ReadOnlySpan<byte> input)
    {
        if (input.Length > InputFreeCapacity || IsTerminal || _paused) return false;
        if (input.Length == 0) return true;
        if (_inputOffset + _inputLength + input.Length > _input.Length)
        {
            _input.AsSpan(_inputOffset, _inputLength).CopyTo(_input);
            _inputOffset = 0;
        }
        input.CopyTo(_input.AsSpan(_inputOffset + _inputLength));
        _inputLength += input.Length;
        _bytesAccepted += input.Length;
        if (_state == ManagedTextDecoderState.Idle) _state = ManagedTextDecoderState.Receiving;
        return true;
    }

    public ManagedTextDecoderProcessResult Pump(IManagedTextConsumer consumer,
                                                bool endOfInput = false)
    {
        if (consumer == null) throw new ArgumentNullException(nameof(consumer));
        if (_state == ManagedTextDecoderState.Cancelled)
            return ManagedTextDecoderProcessResult.Cancelled;
        if (_state == ManagedTextDecoderState.Failed)
            return ManagedTextDecoderProcessResult.Failed;
        if (_state == ManagedTextDecoderState.Completed)
            return ManagedTextDecoderProcessResult.Complete;
        while (true)
        {
            if (_outputLength != 0)
            {
                ManagedHttpBodySinkResult delivery = consumer.Consume(_output.AsSpan(0, _outputLength));
                if (delivery == ManagedHttpBodySinkResult.Pause)
                {
                    if (!_paused) { _paused = true; ++_pauseCount; }
                    _state = ManagedTextDecoderState.Paused;
                    return ManagedTextDecoderProcessResult.Paused;
                }
                if (delivery == ManagedHttpBodySinkResult.Fail)
                {
                    Fail(ManagedTextDecoderFailureReason.DownstreamConsumerFailure);
                    return ManagedTextDecoderProcessResult.Failed;
                }
                _scalarsDelivered += _outputLength;
                _output.AsSpan(0, _outputLength).Clear();
                _outputLength = 0;
                ++_segmentCount;
                if (_paused)
                {
                    _paused = false;
                    ++_resumeCount;
                }
                if (_state == ManagedTextDecoderState.Paused)
                    _state = ManagedTextDecoderState.Receiving;
                continue;
            }

            if (_bomDecided && _bomFlushIndex < _bomLength)
            {
                if (_outputLength == _output.Length)
                    return ManagedTextDecoderProcessResult.OutputAvailable;
                if (!ProcessScalarByte(_bom[_bomFlushIndex++]))
                    return ManagedTextDecoderProcessResult.Failed;
                continue;
            }
            if (_bomDecided && _bomLength != 0 && _bomFlushIndex == _bomLength)
            {
                _bomLength = 0;
                _bomFlushIndex = 0;
            }
            if (_inputLength == 0)
            {
                if (!endOfInput) return ManagedTextDecoderProcessResult.NeedInput;
                if (!_bomDecided && _bomLength != 0)
                {
                    _bomDecided = true;
                    continue;
                }
                if (_expected != 0)
                {
                    Fail(ManagedTextDecoderFailureReason.TruncatedUtf8);
                    return ManagedTextDecoderProcessResult.Failed;
                }
                _state = ManagedTextDecoderState.Completed;
                return ManagedTextDecoderProcessResult.Complete;
            }
            if (_outputLength == _output.Length)
                return ManagedTextDecoderProcessResult.OutputAvailable;
            byte value = _input[_inputOffset];
            _inputOffset = (_inputOffset + 1) % _input.Length;
            --_inputLength;
            ++_bytesConsumed;
            if (!_bomDecided && _charset == ManagedTextCharset.Utf8)
            {
                _bom[_bomLength++] = value;
                if (_bomLength != _bom.Length) continue;
                _bomDecided = true;
                if (_bom[0] == 0xEF && _bom[1] == 0xBB && _bom[2] == 0xBF)
                {
                    _bomConsumed = true;
                    _bomLength = 0;
                    continue;
                }
                _bomFlushIndex = 0;
                continue;
            }
            if (!ProcessScalarByte(value))
                return ManagedTextDecoderProcessResult.Failed;
        }
    }

    public void Resume()
    {
        if (_state == ManagedTextDecoderState.Paused)
        {
            _paused = false;
            _state = ManagedTextDecoderState.Receiving;
            ++_resumeCount;
        }
    }

    public void Cancel()
    {
        if (!IsTerminal) _state = ManagedTextDecoderState.Cancelled;
        _input.AsSpan().Clear();
        _output.AsSpan().Clear();
        _inputOffset = 0;
        _inputLength = 0;
        _outputLength = 0;
        _failureReason = ManagedTextDecoderFailureReason.Cancelled;
    }

    public void Reset()
    {
        _state = ManagedTextDecoderState.Idle;
        _failureReason = ManagedTextDecoderFailureReason.None;
        _inputOffset = 0;
        _inputLength = 0;
        _outputLength = 0;
        _bomLength = 0;
        _bomFlushIndex = 0;
        _bomDecided = _charset != ManagedTextCharset.Utf8;
        _value = 0;
        _expected = 0;
        _seen = 0;
        _firstMinimum = 0x80;
        _firstMaximum = 0xBF;
        _bytesAccepted = 0;
        _bytesConsumed = 0;
        _scalarsProduced = 0;
        _scalarsDelivered = 0;
        _segmentCount = 0;
        _peakOutputLength = 0;
        _pauseCount = 0;
        _resumeCount = 0;
        _bomConsumed = false;
        _paused = false;
        _input.AsSpan().Clear();
        _output.AsSpan().Clear();
        _bom.AsSpan().Clear();
    }

    private bool ProcessScalarByte(byte value)
    {
        if (_charset == ManagedTextCharset.UsAscii)
        {
            if (value > 0x7F)
            {
                Fail(ManagedTextDecoderFailureReason.InvalidAscii);
                return false;
            }
            Emit(value);
            return true;
        }
        if (_charset == ManagedTextCharset.Iso88591)
        {
            Emit(value);
            return true;
        }
        if (_expected == 0)
        {
            if (value <= 0x7F)
            {
                Emit(value);
                return true;
            }
            if (value >= 0xC2 && value <= 0xDF)
            {
                _value = (uint)(value & 0x1F);
                _expected = 2;
                _seen = 1;
                _firstMinimum = 0x80;
                _firstMaximum = 0xBF;
                return true;
            }
            if (value >= 0xE0 && value <= 0xEF)
            {
                _value = (uint)(value & 0x0F);
                _expected = 3;
                _seen = 1;
                _firstMinimum = value == 0xE0 ? (byte)0xA0 : (byte)0x80;
                _firstMaximum = value == 0xED ? (byte)0x9F : (byte)0xBF;
                return true;
            }
            if (value >= 0xF0 && value <= 0xF4)
            {
                _value = (uint)(value & 0x07);
                _expected = 4;
                _seen = 1;
                _firstMinimum = value == 0xF0 ? (byte)0x90 : (byte)0x80;
                _firstMaximum = value == 0xF4 ? (byte)0x8F : (byte)0xBF;
                return true;
            }
            Fail(ManagedTextDecoderFailureReason.InvalidUtf8);
            return false;
        }
        if (value < _firstMinimum || value > _firstMaximum)
        {
            Fail(ManagedTextDecoderFailureReason.InvalidUtf8);
            return false;
        }
        _value = (_value << 6) | (uint)(value & 0x3F);
        ++_seen;
        _firstMinimum = 0x80;
        _firstMaximum = 0xBF;
        if (_seen == _expected)
        {
            if (_value > 0x10FFFF || (_value >= 0xD800 && _value <= 0xDFFF))
            {
                Fail(ManagedTextDecoderFailureReason.InvalidUtf8);
                return false;
            }
            Emit(_value);
            _value = 0;
            _expected = 0;
            _seen = 0;
        }
        return true;
    }

    private void Emit(uint scalar)
    {
        _output[_outputLength++] = scalar;
        if (_outputLength > _peakOutputLength) _peakOutputLength = _outputLength;
        ++_scalarsProduced;
    }

    private void Fail(ManagedTextDecoderFailureReason reason)
    {
        _failureReason = reason;
        _state = ManagedTextDecoderState.Failed;
    }
}

public sealed class ManagedTextCountConsumer : IManagedTextConsumer
{
    private readonly bool _countLines;
    private int _count;
    private int _lineCount;
    private bool _previousWasCarriageReturn;
    private ManagedResourceConsumerState _state;
    private ManagedTextConsumerFailureReason _failureReason;

    public ManagedTextCountConsumer(bool countLines = false) { _countLines = countLines; Reset(); }
    public ManagedResourceConsumerState State => _state;
    public ManagedTextConsumerFailureReason FailureReason => _failureReason;
    public int ScalarsProcessed => _count;
    public int Count => _count;
    public int LineCount => _lineCount;

    public ManagedHttpBodySinkResult Consume(ReadOnlySpan<uint> segment)
    {
        if (_state == ManagedResourceConsumerState.Cancelled ||
            _state == ManagedResourceConsumerState.Failed ||
            _state == ManagedResourceConsumerState.Completed)
            return ManagedHttpBodySinkResult.Fail;
        if (segment.Length > int.MaxValue - _count)
        {
            _failureReason = ManagedTextConsumerFailureReason.ConsumerFailure;
            _state = ManagedResourceConsumerState.Failed;
            return ManagedHttpBodySinkResult.Fail;
        }
        for (int index = 0; index != segment.Length; ++index)
        {
            uint scalar = segment[index];
            if (_countLines)
            {
                if (scalar == '\n')
                {
                    if (!_previousWasCarriageReturn) ++_lineCount;
                    _previousWasCarriageReturn = false;
                }
                else if (scalar == '\r')
                {
                    ++_lineCount;
                    _previousWasCarriageReturn = true;
                }
                else _previousWasCarriageReturn = false;
            }
        }
        _count += segment.Length;
        _state = ManagedResourceConsumerState.Receiving;
        return ManagedHttpBodySinkResult.Continue;
    }

    public bool Complete()
    {
        if (_state == ManagedResourceConsumerState.Completed) return true;
        if (_state == ManagedResourceConsumerState.Cancelled || _state == ManagedResourceConsumerState.Failed) return false;
        _state = ManagedResourceConsumerState.Completed;
        return true;
    }
    public void Cancel() => _state = ManagedResourceConsumerState.Cancelled;
    public void Reset()
    {
        _count = 0; _lineCount = 0; _previousWasCarriageReturn = false;
        _failureReason = ManagedTextConsumerFailureReason.None;
        _state = ManagedResourceConsumerState.Idle;
    }
}

public sealed class ManagedTextPrefixConsumer : IManagedTextConsumer
{
    private readonly uint[] _prefix;
    private int _captured;
    private int _processed;
    private ManagedResourceConsumerState _state;

    public ManagedTextPrefixConsumer(int capacity)
    {
        if (capacity < 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _prefix = new uint[capacity];
        Reset();
    }
    public ManagedTextPrefixConsumer(uint[] destination)
    {
        _prefix = destination ?? throw new ArgumentNullException(nameof(destination));
        Reset();
    }
    public ManagedResourceConsumerState State => _state;
    public ManagedTextConsumerFailureReason FailureReason => ManagedTextConsumerFailureReason.None;
    public int ScalarsProcessed => _processed;
    public int Capacity => _prefix.Length;
    public int CapturedLength => _captured;
    public bool IsFull => _captured == _prefix.Length;
    public ManagedHttpBodySinkResult Consume(ReadOnlySpan<uint> segment)
    {
        if (_state == ManagedResourceConsumerState.Cancelled || _state == ManagedResourceConsumerState.Failed || _state == ManagedResourceConsumerState.Completed)
            return ManagedHttpBodySinkResult.Fail;
        int copy = Math.Min(segment.Length, _prefix.Length - _captured);
        if (copy != 0) segment[..copy].CopyTo(_prefix.AsSpan(_captured));
        _captured += copy;
        _processed += segment.Length;
        _state = ManagedResourceConsumerState.Receiving;
        return ManagedHttpBodySinkResult.Continue;
    }
    public bool TryCopyPrefix(Span<uint> destination, out int length)
    {
        length = _captured;
        if (destination.Length < length) return false;
        _prefix.AsSpan(0, length).CopyTo(destination);
        return true;
    }
    public bool Complete()
    {
        if (_state == ManagedResourceConsumerState.Completed) return true;
        if (_state == ManagedResourceConsumerState.Cancelled || _state == ManagedResourceConsumerState.Failed) return false;
        _state = ManagedResourceConsumerState.Completed; return true;
    }
    public void Cancel() => _state = ManagedResourceConsumerState.Cancelled;
    public void Reset() { _prefix.AsSpan().Clear(); _captured = 0; _processed = 0; _state = ManagedResourceConsumerState.Idle; }
}

public sealed class ManagedTextDestinationConsumer : IManagedTextConsumer
{
    private readonly uint[] _destination;
    private readonly int _offset;
    private readonly int _capacity;
    private int _written;
    private ManagedResourceConsumerState _state;
    private ManagedTextConsumerFailureReason _failureReason;

    public ManagedTextDestinationConsumer(uint[] destination) : this(destination, 0, destination?.Length ?? 0) { }
    public ManagedTextDestinationConsumer(uint[] destination, int offset, int length)
    {
        if (destination == null) throw new ArgumentNullException(nameof(destination));
        if (offset < 0 || length < 0 || offset > destination.Length - length) throw new ArgumentOutOfRangeException(nameof(offset));
        _destination = destination; _offset = offset; _capacity = length; Reset();
    }
    public ManagedResourceConsumerState State => _state;
    public ManagedTextConsumerFailureReason FailureReason => _failureReason;
    public int ScalarsProcessed => _written;
    public int UnitsWritten => _written;
    public int Capacity => _capacity;
    public bool IsFull => _written == _capacity;
    public ManagedHttpBodySinkResult Consume(ReadOnlySpan<uint> segment)
    {
        if (_state == ManagedResourceConsumerState.Cancelled || _state == ManagedResourceConsumerState.Failed || _state == ManagedResourceConsumerState.Completed)
            return ManagedHttpBodySinkResult.Fail;
        if (segment.Length > _capacity - _written)
        {
            _failureReason = ManagedTextConsumerFailureReason.DestinationFull;
            _state = ManagedResourceConsumerState.Failed;
            return ManagedHttpBodySinkResult.Fail;
        }
        segment.CopyTo(_destination.AsSpan(_offset + _written));
        _written += segment.Length; _state = ManagedResourceConsumerState.Receiving;
        return ManagedHttpBodySinkResult.Continue;
    }
    public bool Complete()
    {
        if (_state == ManagedResourceConsumerState.Completed) return true;
        if (_state == ManagedResourceConsumerState.Cancelled || _state == ManagedResourceConsumerState.Failed) return false;
        _state = ManagedResourceConsumerState.Completed; return true;
    }
    public void Cancel() => _state = ManagedResourceConsumerState.Cancelled;
    public void Reset() { _destination.AsSpan(_offset, _capacity).Clear(); _written = 0; _failureReason = ManagedTextConsumerFailureReason.None; _state = ManagedResourceConsumerState.Idle; }
}

public sealed class ManagedTextCompositeConsumer : IManagedTextConsumer
{
    private readonly IManagedTextConsumer _first;
    private readonly IManagedTextConsumer _second;
    private readonly IManagedTextConsumer? _third;
    private readonly IManagedTextConsumer? _fourth;
    private int _processed;
    private ManagedResourceConsumerState _state;
    private ManagedTextConsumerFailureReason _failureReason;

    public ManagedTextCompositeConsumer(IManagedTextConsumer first, IManagedTextConsumer second,
                                        IManagedTextConsumer? third = null, IManagedTextConsumer? fourth = null)
    {
        _first = first ?? throw new ArgumentNullException(nameof(first));
        _second = second ?? throw new ArgumentNullException(nameof(second));
        _third = third; _fourth = fourth; Reset();
    }
    public ManagedResourceConsumerState State => _state;
    public ManagedTextConsumerFailureReason FailureReason => _failureReason;
    public int ScalarsProcessed => _processed;
    public ManagedHttpBodySinkResult Consume(ReadOnlySpan<uint> segment)
    {
        if (_state == ManagedResourceConsumerState.Cancelled || _state == ManagedResourceConsumerState.Failed || _state == ManagedResourceConsumerState.Completed)
            return ManagedHttpBodySinkResult.Fail;
        ManagedHttpBodySinkResult result = _first.Consume(segment);
        if (result == ManagedHttpBodySinkResult.Pause) { _state = ManagedResourceConsumerState.Paused; return result; }
        if (result == ManagedHttpBodySinkResult.Fail) return FailFrom(_first);
        result = _second.Consume(segment);
        if (result == ManagedHttpBodySinkResult.Pause) return FailPauseAfterAcceptance();
        if (result == ManagedHttpBodySinkResult.Fail) return FailFrom(_second);
        if (_third != null)
        {
            result = _third.Consume(segment);
            if (result == ManagedHttpBodySinkResult.Pause) return FailPauseAfterAcceptance();
            if (result == ManagedHttpBodySinkResult.Fail) return FailFrom(_third);
        }
        if (_fourth != null)
        {
            result = _fourth.Consume(segment);
            if (result == ManagedHttpBodySinkResult.Pause) return FailPauseAfterAcceptance();
            if (result == ManagedHttpBodySinkResult.Fail) return FailFrom(_fourth);
        }
        _processed += segment.Length; _state = ManagedResourceConsumerState.Receiving;
        return ManagedHttpBodySinkResult.Continue;
    }
    public bool Complete()
    {
        if (_state == ManagedResourceConsumerState.Completed) return true;
        if (_state == ManagedResourceConsumerState.Cancelled || _state == ManagedResourceConsumerState.Failed) return false;
        if (!_first.Complete() || !_second.Complete() || (_third != null && !_third.Complete()) || (_fourth != null && !_fourth.Complete()))
        { _failureReason = ManagedTextConsumerFailureReason.FinalizationFailure; _state = ManagedResourceConsumerState.Failed; return false; }
        _state = ManagedResourceConsumerState.Completed; return true;
    }
    public void Cancel() { _first.Cancel(); _second.Cancel(); _third?.Cancel(); _fourth?.Cancel(); _state = ManagedResourceConsumerState.Cancelled; }
    public void Reset() { _first.Reset(); _second.Reset(); _third?.Reset(); _fourth?.Reset(); _processed = 0; _failureReason = ManagedTextConsumerFailureReason.None; _state = ManagedResourceConsumerState.Idle; }
    private ManagedHttpBodySinkResult FailFrom(IManagedTextConsumer component)
    { _failureReason = component.FailureReason == ManagedTextConsumerFailureReason.None ? ManagedTextConsumerFailureReason.ConsumerFailure : component.FailureReason; _state = ManagedResourceConsumerState.Failed; return ManagedHttpBodySinkResult.Fail; }
    private ManagedHttpBodySinkResult FailPauseAfterAcceptance()
    { _failureReason = ManagedTextConsumerFailureReason.ConsumerFailure; _state = ManagedResourceConsumerState.Failed; return ManagedHttpBodySinkResult.Fail; }
}

public enum ManagedTextFailureReason : byte
{
    None = 0,
    Cancelled = 1,
    UnsupportedMime = 2,
    ContentTypeTooLong = 3,
    MalformedContentType = 4,
    UnsupportedCharset = 5,
    MalformedCharset = 6,
    EmptyCharset = 7,
    InvalidUtf8 = 8,
    TruncatedUtf8 = 9,
    InvalidAscii = 10,
    TextDestinationFull = 11,
    TextConsumerFailure = 12,
    ContentEncodingFailure = 13,
    DecodedResourceLimit = 14,
    EncodedEntityLimit = 15,
    HttpParserFailure = 16,
    TransportFailure = 17,
    TlsFailure = 18,
    TeardownFailure = 19,
    RequestFailure = 20
}

public readonly struct ManagedTextProgressSnapshot
{
    internal ManagedTextProgressSnapshot(
        ManagedResourceState state, ManagedResourceProtocol protocol,
        ManagedResourceConsumerState consumerState,
        ManagedTextFailureReason failureReason,
        ManagedResourceFailureReason resourceFailureReason,
        int statusCode, ManagedMimeClassification mime,
        ManagedHttpContentTypeState contentTypeState,
        ManagedTextCharset charset, ManagedTextCharsetSource charsetSource,
        ManagedHttpContentEncodingState contentEncoding,
        int encodedBytesReceived, int decompressedBytesProduced,
        int textInputBytesConsumed, int scalarsProduced, int scalarsDelivered,
        int bufferedScalars, int textSegments, int pauseCount, int resumeCount,
        int peakHttpBuffer, int peakDecompressionBuffer, int peakTextBuffer,
        ManagedTextDecoderState decoderState,
        ManagedTextDecoderFailureReason decoderFailureReason)
    {
        State = state; Protocol = protocol; ConsumerState = consumerState;
        FailureReason = failureReason; ResourceFailureReason = resourceFailureReason;
        StatusCode = statusCode; MimeClassification = mime; ContentTypeState = contentTypeState;
        Charset = charset; CharsetSource = charsetSource; ContentEncodingState = contentEncoding;
        EncodedHttpBytesReceived = encodedBytesReceived; DecompressedResourceBytesProduced = decompressedBytesProduced;
        TextInputBytesConsumed = textInputBytesConsumed; ScalarsProduced = scalarsProduced;
        ScalarsDelivered = scalarsDelivered; BufferedDecodedTextCount = bufferedScalars;
        TextSegmentCount = textSegments; PauseCount = pauseCount; ResumeCount = resumeCount;
        PeakHttpBuffer = peakHttpBuffer; PeakDecompressionBuffer = peakDecompressionBuffer;
        PeakTextBuffer = peakTextBuffer; DecoderState = decoderState; DecoderFailureReason = decoderFailureReason;
    }
    public ManagedResourceState State { get; }
    public ManagedResourceProtocol Protocol { get; }
    public ManagedResourceConsumerState ConsumerState { get; }
    public ManagedTextFailureReason FailureReason { get; }
    public ManagedResourceFailureReason ResourceFailureReason { get; }
    public int StatusCode { get; }
    public ManagedMimeClassification MimeClassification { get; }
    public ManagedHttpContentTypeState ContentTypeState { get; }
    public ManagedTextCharset Charset { get; }
    public ManagedTextCharsetSource CharsetSource { get; }
    public ManagedHttpContentEncodingState ContentEncodingState { get; }
    public int EncodedHttpBytesReceived { get; }
    public int DecompressedResourceBytesProduced { get; }
    public int TextInputBytesConsumed { get; }
    public int ScalarsProduced { get; }
    public int ScalarsDelivered { get; }
    public int BufferedDecodedTextCount { get; }
    public int TextSegmentCount { get; }
    public int PauseCount { get; }
    public int ResumeCount { get; }
    public int PeakHttpBuffer { get; }
    public int PeakDecompressionBuffer { get; }
    public int PeakTextBuffer { get; }
    public ManagedTextDecoderState DecoderState { get; }
    public ManagedTextDecoderFailureReason DecoderFailureReason { get; }
    public bool IsComplete => State == ManagedResourceState.Completed;
    public bool IsCancelled => State == ManagedResourceState.Cancelled;
    public bool IsTerminal => State == ManagedResourceState.Completed || State == ManagedResourceState.Cancelled || State == ManagedResourceState.Failed;
}

/* Text is opt-in.  This wrapper consumes ResourceRequest's resource-byte
   stream, so Content-Encoding is completely finished before Unicode parsing.
   The adapter accepts at most one resource delivery window into the decoder's
   fixed input queue before propagating downstream pause. */
public sealed class ManagedTextResourceRequest
{
    private readonly ManagedResourceRequest _resource;
    private readonly bool _allowUnknownMime;
    private readonly bool _allowBinaryMime;
    private readonly ManagedTextResourceAdapter _adapter;
    private readonly ManagedResourceSha256Consumer _resourceHash = new();
    private readonly ManagedResourceCompositeConsumer _resourceConsumer;
    private readonly byte[] _contentType = new byte[ManagedHttpLimits.MaximumContentTypeLength];
    private ManagedTextDecoder? _decoder;
    private IManagedTextConsumer? _consumer;
    private ManagedTextFailureReason _failureReason;
    private ManagedResourceState _state;
    private ManagedMimeClassification _mime;
    private ManagedTextCharset _charset;
    private ManagedTextCharsetSource _charsetSource;
    private bool _metadataEvaluated;
    private bool _pauseRequested;
    private int _pauseCount;
    private int _resumeCount;
    private int _peakTextBuffer;

    public ManagedTextResourceRequest(ManagedNetworkService service,
                                      int maximumEntityLength = ManagedHttpLimits.MaximumStreamedBodyLength,
                                      int maximumDecodedResourceLength = ManagedContentEncodingLimits.MaximumDecodedResourceLength,
                                      bool allowUnknownMime = false, bool allowBinaryMime = false)
    {
        _resource = new(service, maximumEntityLength, maximumDecodedResourceLength);
        _allowUnknownMime = allowUnknownMime; _allowBinaryMime = allowBinaryMime;
        _adapter = new(this); _resourceConsumer = new(_adapter, _resourceHash); _state = ManagedResourceState.Idle;
    }

    public ManagedTextResourceRequest(ManagedNetworkService service,
                                      ReadOnlySpan<byte> trustedRoot,
                                      ManagedHttpsValidationTime validationTime,
                                      int maximumEntityLength = ManagedHttpLimits.MaximumStreamedBodyLength,
                                      int maximumDecodedResourceLength = ManagedContentEncodingLimits.MaximumDecodedResourceLength,
                                      bool allowUnknownMime = false, bool allowBinaryMime = false)
    {
        _resource = new(service, trustedRoot, validationTime, maximumEntityLength, maximumDecodedResourceLength);
        _allowUnknownMime = allowUnknownMime; _allowBinaryMime = allowBinaryMime;
        _adapter = new(this); _resourceConsumer = new(_adapter, _resourceHash); _state = ManagedResourceState.Idle;
    }

    internal ManagedTextResourceRequest(ManagedNetworkService service,
                                        ReadOnlySpan<byte> trustedRoot,
                                        in ManagedX509UtcTime validationTime,
                                        ManagedSecureRandom random,
                                        int maximumEntityLength,
                                        bool compactTlsProfile,
                                        int maximumDecodedResourceLength,
                                        bool allowUnknownMime = false,
                                        bool allowBinaryMime = false)
    {
        _resource = new(service, trustedRoot, in validationTime, random,
                         maximumEntityLength, compactTlsProfile, maximumDecodedResourceLength);
        _allowUnknownMime = allowUnknownMime; _allowBinaryMime = allowBinaryMime;
        _adapter = new(this); _resourceConsumer = new(_adapter, _resourceHash); _state = ManagedResourceState.Idle;
    }

    public ManagedResourceProtocol Protocol => _resource.Protocol;
    public ManagedResourceState State => _state;
    public ManagedTextFailureReason FailureReason => _failureReason;
    public ManagedResourceFailureReason ResourceFailureReason => _resource.FailureReason;
    public ManagedTextCharset Charset => _charset;
    public ManagedTextCharsetSource CharsetSource => _charsetSource;
    public ManagedMimeClassification MimeClassification => _mime;
    public ManagedTextProgressSnapshot Progress => CreateProgress();
    public NetworkOperationResult BeginGet(ReadOnlySpan<byte> hostname,
                                           ReadOnlySpan<byte> path,
                                           IManagedTextConsumer consumer)
    {
        if (!CanBegin()) return NetworkOperationResult.Busy;
        if (consumer == null) throw new ArgumentNullException(nameof(consumer));
        consumer.Reset(); ResetTextState(consumer);
        NetworkOperationResult result = _resource.BeginGet(hostname, path, _resourceConsumer);
        if (result != NetworkOperationResult.Started) { MapResourceFailure(); return result; }
        _state = ManagedResourceState.Receiving;
        return result;
    }
    public NetworkOperationResult BeginGetUrl(ReadOnlySpan<byte> url,
                                              IManagedTextConsumer consumer)
    {
        if (Protocol != ManagedResourceProtocol.Https) return NetworkOperationResult.InvalidArgument;
        if (!CanBegin()) return NetworkOperationResult.Busy;
        if (consumer == null) throw new ArgumentNullException(nameof(consumer));
        consumer.Reset(); ResetTextState(consumer);
        NetworkOperationResult result = _resource.BeginGetUrl(url, _resourceConsumer);
        if (result != NetworkOperationResult.Started) { MapResourceFailure(); return result; }
        _state = ManagedResourceState.Receiving;
        return result;
    }
    public NetworkOperationResult Pause()
    {
        if (_state == ManagedResourceState.Paused) return NetworkOperationResult.Success;
        if (_state != ManagedResourceState.Receiving) return _state == ManagedResourceState.Idle ? NetworkOperationResult.InvalidArgument : NetworkOperationResult.Success;
        _pauseRequested = true; _state = ManagedResourceState.Paused; ++_pauseCount;
        return _resource.Pause();
    }
    public NetworkOperationResult Resume()
    {
        if (_state != ManagedResourceState.Paused) return _state == ManagedResourceState.Idle ? NetworkOperationResult.InvalidArgument : NetworkOperationResult.Success;
        _pauseRequested = false; _adapter.Resume();
        NetworkOperationResult result = _resource.Resume();
        ++_resumeCount;
        if (result != NetworkOperationResult.Success) return result;
        if (_resource.State == ManagedResourceState.Completed)
        {
            if (!_adapter.DrainAfterResume()) { MapAdapterFailure(); return NetworkOperationResult.Failed; }
            if (_adapter.IsPaused) { _state = ManagedResourceState.Paused; _pauseRequested = true; return NetworkOperationResult.Success; }
            _state = ManagedResourceState.Completed;
        }
        else _state = ManagedResourceState.Receiving;
        return NetworkOperationResult.Success;
    }
    public NetworkOperationResult Poll()
    {
        if (_state == ManagedResourceState.Completed || _state == ManagedResourceState.Cancelled) return NetworkOperationResult.Success;
        if (_state == ManagedResourceState.Failed) return NetworkOperationResult.Failed;
        if (_pauseRequested) return NetworkOperationResult.Success;
        EnsureMetadataIfAvailable();
        if (_state == ManagedResourceState.Failed) return NetworkOperationResult.Failed;
        NetworkOperationResult result = _resource.Poll();
        EnsureMetadataIfAvailable();
        if (_resource.State == ManagedResourceState.Failed) { MapResourceFailure(); return NetworkOperationResult.Failed; }
        if (_resource.State == ManagedResourceState.Cancelled) { _state = ManagedResourceState.Cancelled; _failureReason = ManagedTextFailureReason.Cancelled; return NetworkOperationResult.Success; }
        if (_adapter.IsPaused)
        {
            if (_resource.State == ManagedResourceState.Receiving) _resource.Pause();
            _state = ManagedResourceState.Paused; _pauseRequested = true; ++_pauseCount;
            UpdatePeak(); return NetworkOperationResult.Success;
        }
        if (result == NetworkOperationResult.Failed) { MapResourceFailure(); return result; }
        UpdatePeak();
        if (_resource.State == ManagedResourceState.Completed)
        {
            if (!_adapter.IsComplete) { if (!_adapter.DrainAfterResume()) { MapAdapterFailure(); return NetworkOperationResult.Failed; } }
            if (_adapter.IsPaused) { _state = ManagedResourceState.Paused; _pauseRequested = true; ++_pauseCount; return NetworkOperationResult.Success; }
            if (!_adapter.ConsumerCompleted) { MapAdapterFailure(); return NetworkOperationResult.Failed; }
            _state = ManagedResourceState.Completed;
        }
        return result;
    }
    public NetworkOperationResult Cancel()
    {
        if (_state == ManagedResourceState.Completed || _state == ManagedResourceState.Cancelled || _state == ManagedResourceState.Failed) return NetworkOperationResult.Success;
        NetworkOperationResult result = _resource.Cancel(); _adapter.Cancel(); _consumer?.Cancel();
        _pauseRequested = false; _state = ManagedResourceState.Cancelled; _failureReason = ManagedTextFailureReason.Cancelled;
        return result;
    }
    public NetworkOperationResult Reset()
    {
        if (_state == ManagedResourceState.Receiving || _state == ManagedResourceState.Paused) return NetworkOperationResult.Busy;
        NetworkOperationResult result = _resource.Reset(); if (result != NetworkOperationResult.Success) return result;
        _adapter.Reset(); _resourceHash.Reset(); _resourceConsumer.Reset(); _consumer?.Reset(); _consumer = null; _decoder = null;
        _failureReason = ManagedTextFailureReason.None; _state = ManagedResourceState.Idle; _mime = ManagedMimeClassification.Unknown;
        _charset = ManagedTextCharset.None; _charsetSource = ManagedTextCharsetSource.None; _metadataEvaluated = false;
        _pauseRequested = false; _pauseCount = 0; _resumeCount = 0; _peakTextBuffer = 0;
        return NetworkOperationResult.Success;
    }
    public bool TryCopyContentType(Span<byte> destination, out int length) => _resource.TryCopyContentType(destination, out length);
    public bool TryCopyResourceDigest(Span<byte> destination) => _resourceHash.TryCopyDigest(destination);

    private bool CanBegin() => _state == ManagedResourceState.Idle || _state == ManagedResourceState.Completed || _state == ManagedResourceState.Cancelled || _state == ManagedResourceState.Failed;
    private void ResetTextState(IManagedTextConsumer consumer)
    {
        _consumer = consumer; _decoder = null; _adapter.Reset(); _failureReason = ManagedTextFailureReason.None; _state = ManagedResourceState.Idle;
        _mime = ManagedMimeClassification.Unknown; _charset = ManagedTextCharset.None; _charsetSource = ManagedTextCharsetSource.None; _metadataEvaluated = false;
        _pauseRequested = false; _pauseCount = 0; _resumeCount = 0; _peakTextBuffer = 0;
    }
    private void EnsureMetadataIfAvailable()
    {
        ManagedResourceProgressSnapshot available = _resource.Progress;
        if (_metadataEvaluated || available.StatusCode == 0 ||
            available.TransferMode == ManagedHttpFramingMode.None) return;
        ManagedResourceProgressSnapshot raw = _resource.Progress;
        if (raw.ContentTypeState == ManagedHttpContentTypeState.TooLong) { Fail(ManagedTextFailureReason.ContentTypeTooLong); return; }
        ManagedContentTypeMetadata metadata;
        if (raw.ContentTypeState == ManagedHttpContentTypeState.Missing)
            metadata = new(ManagedContentTypeMetadataState.Available, ManagedMimeClassification.Unknown, ManagedCharsetDeclarationState.None, ManagedTextCharset.None, 0);
        else if (!_resource.TryCopyContentType(_contentType, out int length)) { Fail(ManagedTextFailureReason.MalformedContentType); return; }
        else metadata = ManagedContentTypeParser.Parse(_contentType.AsSpan(0, length));
        _mime = metadata.Classification;
        if (metadata.IsMalformed) { Fail(ManagedTextFailureReason.MalformedContentType); return; }
        if ((_mime == ManagedMimeClassification.Binary && !_allowBinaryMime) ||
            (_mime == ManagedMimeClassification.Unknown && !_allowUnknownMime)) { Fail(ManagedTextFailureReason.UnsupportedMime); return; }
        if (metadata.CharsetState == ManagedCharsetDeclarationState.Unsupported) { _charsetSource = ManagedTextCharsetSource.Unsupported; Fail(ManagedTextFailureReason.UnsupportedCharset); return; }
        if (metadata.CharsetState == ManagedCharsetDeclarationState.Malformed) { Fail(ManagedTextFailureReason.MalformedCharset); return; }
        if (metadata.CharsetState == ManagedCharsetDeclarationState.Empty) { Fail(ManagedTextFailureReason.EmptyCharset); return; }
        if (metadata.CharsetState == ManagedCharsetDeclarationState.TooLong) { _charsetSource = ManagedTextCharsetSource.Unsupported; Fail(ManagedTextFailureReason.UnsupportedCharset); return; }
        if (metadata.CharsetState == ManagedCharsetDeclarationState.None)
        {
            _charset = ManagedTextCharset.Utf8; _charsetSource = ManagedTextCharsetSource.Default;
        }
        else { _charset = metadata.Charset; _charsetSource = ManagedTextCharsetSource.Explicit; }
        _decoder = new ManagedTextDecoder(_charset); _adapter.Configure(_decoder); _metadataEvaluated = true;
    }
    internal bool EnsureForAdapter() { EnsureMetadataIfAvailable(); return _state != ManagedResourceState.Failed && _decoder != null; }
    internal IManagedTextConsumer Consumer => _consumer!;
    internal void FailFromAdapter(ManagedTextFailureReason reason) => Fail(reason);
    private void Fail(ManagedTextFailureReason reason) { _failureReason = reason; _state = ManagedResourceState.Failed; }
    private void MapAdapterFailure()
    {
        ManagedTextDecoderFailureReason decoder = _decoder?.FailureReason ?? ManagedTextDecoderFailureReason.None;
        if (decoder == ManagedTextDecoderFailureReason.InvalidUtf8) Fail(ManagedTextFailureReason.InvalidUtf8);
        else if (decoder == ManagedTextDecoderFailureReason.TruncatedUtf8) Fail(ManagedTextFailureReason.TruncatedUtf8);
        else if (decoder == ManagedTextDecoderFailureReason.InvalidAscii) Fail(ManagedTextFailureReason.InvalidAscii);
        else if (_consumer?.FailureReason == ManagedTextConsumerFailureReason.DestinationFull) Fail(ManagedTextFailureReason.TextDestinationFull);
        else Fail(ManagedTextFailureReason.TextConsumerFailure);
    }
    private void MapResourceFailure()
    {
        if (_failureReason != ManagedTextFailureReason.None && _failureReason != ManagedTextFailureReason.TextConsumerFailure) return;
        switch (_resource.FailureReason)
        {
            case ManagedResourceFailureReason.Cancelled: Fail(ManagedTextFailureReason.Cancelled); break;
            case ManagedResourceFailureReason.UnsupportedContentEncoding:
            case ManagedResourceFailureReason.MalformedContentEncoding:
            case ManagedResourceFailureReason.ContentEncodingHeaderTooLong:
            case ManagedResourceFailureReason.MalformedGzipHeader:
            case ManagedResourceFailureReason.MalformedZlibHeader:
            case ManagedResourceFailureReason.MalformedDeflateStream:
            case ManagedResourceFailureReason.GzipCrcMismatch:
            case ManagedResourceFailureReason.GzipIsizeMismatch:
            case ManagedResourceFailureReason.ZlibAdlerMismatch:
            case ManagedResourceFailureReason.TruncatedCompressedStream:
            case ManagedResourceFailureReason.TrailingCompressedData: Fail(ManagedTextFailureReason.ContentEncodingFailure); break;
            case ManagedResourceFailureReason.DecodedResourceTooLarge: Fail(ManagedTextFailureReason.DecodedResourceLimit); break;
            case ManagedResourceFailureReason.BodyTooLarge: Fail(ManagedTextFailureReason.EncodedEntityLimit); break;
            case ManagedResourceFailureReason.TlsFailure: Fail(ManagedTextFailureReason.TlsFailure); break;
            case ManagedResourceFailureReason.TransportFailure: Fail(ManagedTextFailureReason.TransportFailure); break;
            case ManagedResourceFailureReason.TeardownFailure: Fail(ManagedTextFailureReason.TeardownFailure); break;
            case ManagedResourceFailureReason.RequestFailure: Fail(ManagedTextFailureReason.RequestFailure); break;
            case ManagedResourceFailureReason.ConsumerFailure: MapAdapterFailure(); break;
            default: Fail(ManagedTextFailureReason.HttpParserFailure); break;
        }
    }
    private void UpdatePeak() { if (_decoder != null && _decoder.PeakOutputLength > _peakTextBuffer) _peakTextBuffer = _decoder.PeakOutputLength; }
    private ManagedTextProgressSnapshot CreateProgress()
    {
        ManagedResourceProgressSnapshot raw = _resource.Progress;
        ManagedResourceConsumerState consumerState = _consumer?.State ?? ManagedResourceConsumerState.Idle;
        if (_state == ManagedResourceState.Paused) consumerState = ManagedResourceConsumerState.Paused;
        if (_state == ManagedResourceState.Completed) consumerState = ManagedResourceConsumerState.Completed;
        if (_state == ManagedResourceState.Cancelled) consumerState = ManagedResourceConsumerState.Cancelled;
        if (_state == ManagedResourceState.Failed) consumerState = ManagedResourceConsumerState.Failed;
        return new(_state, Protocol, consumerState, _failureReason, _resource.FailureReason,
                   raw.StatusCode, _mime, raw.ContentTypeState, _charset, EffectiveCharsetSource(),
                   raw.ContentEncodingState, raw.EncodedBytesReceived, raw.DecodedBytesProduced,
                   _decoder?.BytesConsumed ?? 0, _decoder?.ScalarsProduced ?? 0,
                   _decoder?.ScalarsDelivered ?? 0, _decoder?.BufferedOutputLength ?? 0,
                   _decoder?.SegmentCount ?? 0, _pauseCount, _resumeCount,
                   raw.PeakBufferedBytes, raw.PeakDecodedOutputBytes, _peakTextBuffer,
                   _decoder?.State ?? ManagedTextDecoderState.Idle,
                   _decoder?.FailureReason ?? ManagedTextDecoderFailureReason.None);
    }
    private ManagedTextCharsetSource EffectiveCharsetSource() =>
        _charsetSource == ManagedTextCharsetSource.Default && _decoder?.BomConsumed == true
            ? ManagedTextCharsetSource.Bom : _charsetSource;

    private sealed class ManagedTextResourceAdapter : IManagedResourceConsumer
    {
        private readonly ManagedTextResourceRequest _owner;
        private ManagedTextDecoder? _decoder;
        private ManagedResourceConsumerState _state;
        private ManagedResourceConsumerFailureReason _resourceFailure;
        private int _processed;
        private bool _paused;
        private bool _complete;
        private bool _consumerCompleted;
        internal ManagedTextResourceAdapter(ManagedTextResourceRequest owner) { _owner = owner; Reset(); }
        public ManagedResourceConsumerState State => _state;
        public ManagedResourceConsumerFailureReason FailureReason => _resourceFailure;
        public int BytesProcessed => _processed;
        internal bool IsPaused => _paused;
        internal bool IsComplete => _complete;
        internal bool ConsumerCompleted => _consumerCompleted;
        internal void Configure(ManagedTextDecoder decoder) { _decoder = decoder; }
        public ManagedHttpBodySinkResult Consume(ReadOnlySpan<byte> segment)
        {
            if (_state == ManagedResourceConsumerState.Cancelled || _state == ManagedResourceConsumerState.Failed || _state == ManagedResourceConsumerState.Completed) return ManagedHttpBodySinkResult.Fail;
            if (_paused) return ManagedHttpBodySinkResult.Pause;
            if (!_owner.EnsureForAdapter()) return ManagedHttpBodySinkResult.Fail;
            _decoder = _owner._decoder;
            if (segment.Length > _decoder!.InputFreeCapacity)
            {
                ManagedTextDecoderProcessResult madeRoom = _decoder.Pump(_owner.Consumer);
                if (madeRoom == ManagedTextDecoderProcessResult.Failed) { _owner.MapAdapterFailure(); return ManagedHttpBodySinkResult.Fail; }
                if (segment.Length > _decoder.InputFreeCapacity) { _paused = true; _state = ManagedResourceConsumerState.Paused; return ManagedHttpBodySinkResult.Pause; }
            }
            if (!_decoder.AppendInput(segment)) { _paused = true; _state = ManagedResourceConsumerState.Paused; return ManagedHttpBodySinkResult.Pause; }
            _processed += segment.Length;
            ManagedTextDecoderProcessResult result = _decoder.Pump(_owner.Consumer);
            if (result == ManagedTextDecoderProcessResult.Failed) { _owner.MapAdapterFailure(); _resourceFailure = ManagedResourceConsumerFailureReason.ConsumerFailure; _state = ManagedResourceConsumerState.Failed; return ManagedHttpBodySinkResult.Fail; }
            if (result == ManagedTextDecoderProcessResult.Paused || _decoder.InputLength != 0)
            {
                _paused = result == ManagedTextDecoderProcessResult.Paused || _decoder.InputFreeCapacity == 0;
                if (_paused) _state = ManagedResourceConsumerState.Paused;
            }
            else _state = ManagedResourceConsumerState.Receiving;
            return ManagedHttpBodySinkResult.Continue;
        }
        public bool Complete()
        {
            if (_complete) return true;
            if (_state == ManagedResourceConsumerState.Cancelled || _state == ManagedResourceConsumerState.Failed) return false;
            if (!_owner.EnsureForAdapter()) return false;
            _decoder = _owner._decoder;
            ManagedTextDecoderProcessResult result = _decoder!.Pump(_owner.Consumer, true);
            if (result == ManagedTextDecoderProcessResult.Failed) { _owner.MapAdapterFailure(); _resourceFailure = ManagedResourceConsumerFailureReason.ConsumerFailure; _state = ManagedResourceConsumerState.Failed; return false; }
            if (result == ManagedTextDecoderProcessResult.Paused)
            {
                _paused = true; _state = ManagedResourceConsumerState.Paused; _complete = true; return true;
            }
            if (result != ManagedTextDecoderProcessResult.Complete) return false;
            if (!_owner.Consumer.Complete()) { _owner.MapAdapterFailure(); _resourceFailure = ManagedResourceConsumerFailureReason.ConsumerFailure; _state = ManagedResourceConsumerState.Failed; return false; }
            _consumerCompleted = true; _complete = true; _state = ManagedResourceConsumerState.Completed; return true;
        }
        internal bool DrainAfterResume()
        {
            if (!_owner.EnsureForAdapter()) return false;
            _decoder = _owner._decoder;
            _decoder!.Resume(); _paused = false;
            ManagedTextDecoderProcessResult result = _decoder.Pump(_owner.Consumer, _complete);
            if (result == ManagedTextDecoderProcessResult.Failed) { _owner.MapAdapterFailure(); return false; }
            if (result == ManagedTextDecoderProcessResult.Paused) { _paused = true; _state = ManagedResourceConsumerState.Paused; return true; }
            if (_complete && result == ManagedTextDecoderProcessResult.Complete && !_consumerCompleted)
            {
                if (!_owner.Consumer.Complete()) { _owner.MapAdapterFailure(); return false; }
                _consumerCompleted = true; _state = ManagedResourceConsumerState.Completed;
            }
            else _state = ManagedResourceConsumerState.Receiving;
            return true;
        }
        internal void Resume() { _paused = false; _decoder?.Resume(); if (_state == ManagedResourceConsumerState.Paused) _state = ManagedResourceConsumerState.Receiving; }
        public void Cancel() { _decoder?.Cancel(); _owner.Consumer?.Cancel(); _state = ManagedResourceConsumerState.Cancelled; }
        public void Reset() { _decoder = null; _processed = 0; _resourceFailure = ManagedResourceConsumerFailureReason.None; _state = ManagedResourceConsumerState.Idle; _paused = false; _complete = false; _consumerCompleted = false; }
    }
}
