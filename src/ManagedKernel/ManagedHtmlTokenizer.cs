using System;

namespace GuideXOS.Net10.ManagedKernel;

public enum ManagedHtmlTokenKind : byte
{
    Text = 0,
    StartTag = 1,
    EndTag = 2,
    Comment = 3,
    Doctype = 4,
    EndOfFile = 5
}

public enum ManagedHtmlTokenizerState : byte
{
    Idle = 0,
    Data = 1,
    TagOpen = 2,
    EndTagOpen = 3,
    TagName = 4,
    BeforeAttributeName = 5,
    AttributeName = 6,
    AfterAttributeName = 7,
    BeforeAttributeValue = 8,
    AttributeValueDoubleQuoted = 9,
    AttributeValueSingleQuoted = 10,
    AttributeValueUnquoted = 11,
    AfterAttributeValueQuoted = 12,
    SelfClosingStartTag = 13,
    MarkupDeclarationOpen = 14,
    CommentSecondDash = 15,
    Comment = 16,
    DoctypeKeyword = 17,
    DoctypeBeforeName = 18,
    DoctypeName = 19,
    DoctypeAfterName = 20,
    CharacterReference = 21,
    RawText = 22,
    RcData = 23,
    ScriptData = 24,
    RawTextCandidate = 25,
    RawTextCandidateFlush = 26,
    RawTextCloseAfterText = 27,
    Paused = 28,
    Completed = 29,
    Cancelled = 30,
    Failed = 31,
    EndTagAfterName = 32
}

public enum ManagedHtmlTokenizerFailureReason : byte
{
    None = 0,
    InvalidMarkup = 1,
    TruncatedMarkup = 2,
    TagNameTooLong = 3,
    AttributeNameTooLong = 4,
    AttributeValueTooLong = 5,
    TooManyAttributes = 6,
    EntityNameTooLong = 7,
    InvalidNumericEntity = 8,
    CommentStateError = 9,
    TruncatedComment = 10,
    DoctypeTooLong = 11,
    UnsupportedMarkupDeclaration = 12,
    TokenConsumerFailure = 13,
    Cancelled = 14,
    NoProgress = 15
}

public enum ManagedHtmlTokenizerProcessResult : byte
{
    NeedInput = 0,
    Progress = 1,
    Paused = 2,
    Complete = 3,
    Failed = 4,
    Cancelled = 5
}

public enum ManagedHtmlTokenConsumerState : byte
{
    Idle = 0,
    Receiving = 1,
    Paused = 2,
    Completed = 3,
    Cancelled = 4,
    Failed = 5
}

public enum ManagedHtmlTokenConsumerFailureReason : byte
{
    None = 0,
    ConsumerFailure = 1,
    FinalizationFailure = 2
}

internal readonly struct ManagedHtmlAttributeSlot
{
    internal ManagedHtmlAttributeSlot(byte[] name, int nameLength,
                                      uint[] value, int valueLength,
                                      bool hasValue)
    {
        Name = name;
        NameLength = nameLength;
        Value = value;
        ValueLength = valueLength;
        HasValue = hasValue;
    }

    internal readonly byte[] Name;
    internal readonly int NameLength;
    internal readonly uint[] Value;
    internal readonly int ValueLength;
    internal readonly bool HasValue;
}

/* Token data is a synchronous snapshot view.  Copy methods are deliberately
   the only public access to token storage: a consumer may inspect a token
   during Consume, but cannot retain a span into storage that the tokenizer
   will reuse after the callback returns. */
public readonly struct ManagedHtmlToken
{
    private readonly byte[]? _tagName;
    private readonly int _tagNameLength;
    private readonly uint[]? _text;
    private readonly int _textLength;
    private readonly ManagedHtmlAttributeSlot[]? _attributes;
    private readonly int _attributeCount;
    private readonly uint[]? _comment;
    private readonly int _commentLength;
    private readonly byte[]? _doctypeName;
    private readonly int _doctypeNameLength;

    internal ManagedHtmlToken(ManagedHtmlTokenKind kind, byte[]? tagName,
                              int tagNameLength, uint[]? text, int textLength,
                              ManagedHtmlAttributeSlot[]? attributes,
                              int attributeCount, bool selfClosing,
                              uint[]? comment, int commentLength,
                              bool commentFragment, bool commentFinal,
                              byte[]? doctypeName, int doctypeNameLength)
    {
        Kind = kind;
        _tagName = tagName;
        _tagNameLength = tagNameLength;
        _text = text;
        _textLength = textLength;
        _attributes = attributes;
        _attributeCount = attributeCount;
        IsSelfClosing = selfClosing;
        _comment = comment;
        _commentLength = commentLength;
        IsCommentFragment = commentFragment;
        IsCommentFinalFragment = commentFinal;
        _doctypeName = doctypeName;
        _doctypeNameLength = doctypeNameLength;
    }

    public ManagedHtmlTokenKind Kind { get; }
    public bool IsSelfClosing { get; }
    public bool IsCommentFragment { get; }
    public bool IsCommentFinalFragment { get; }
    public int TextLength => Kind == ManagedHtmlTokenKind.Text ? _textLength : 0;
    public int TagNameLength => (Kind == ManagedHtmlTokenKind.StartTag ||
                                 Kind == ManagedHtmlTokenKind.EndTag)
        ? _tagNameLength : 0;
    public int AttributeCount => Kind == ManagedHtmlTokenKind.StartTag
        ? _attributeCount : 0;
    public int CommentLength => Kind == ManagedHtmlTokenKind.Comment
        ? _commentLength : 0;
    public int DoctypeNameLength => Kind == ManagedHtmlTokenKind.Doctype
        ? _doctypeNameLength : 0;

    public bool TryCopyText(Span<uint> destination, out int length)
    {
        length = TextLength;
        if (destination.Length < length || _text == null) return false;
        _text.AsSpan(0, length).CopyTo(destination);
        return true;
    }

    public bool TryCopyTagName(Span<byte> destination, out int length)
    {
        length = TagNameLength;
        if (destination.Length < length || _tagName == null) return false;
        _tagName.AsSpan(0, length).CopyTo(destination);
        return true;
    }

    public bool TryCopyComment(Span<uint> destination, out int length)
    {
        length = CommentLength;
        if (destination.Length < length || _comment == null) return false;
        _comment.AsSpan(0, length).CopyTo(destination);
        return true;
    }

    public bool TryCopyDoctypeName(Span<byte> destination, out int length)
    {
        length = DoctypeNameLength;
        if (destination.Length < length || _doctypeName == null) return false;
        _doctypeName.AsSpan(0, length).CopyTo(destination);
        return true;
    }

    public bool TryCopyAttributeName(int index, Span<byte> destination,
                                     out int length)
    {
        length = 0;
        if (_attributes == null || index < 0 || index >= _attributeCount)
            return false;
        ManagedHtmlAttributeSlot slot = _attributes[index];
        length = slot.NameLength;
        if (destination.Length < length) return false;
        slot.Name.AsSpan(0, length).CopyTo(destination);
        return true;
    }

    public bool TryCopyAttributeValue(int index, Span<uint> destination,
                                      out int length, out bool hasValue)
    {
        length = 0;
        hasValue = false;
        if (_attributes == null || index < 0 || index >= _attributeCount)
            return false;
        ManagedHtmlAttributeSlot slot = _attributes[index];
        length = slot.ValueLength;
        hasValue = slot.HasValue;
        if (destination.Length < length) return false;
        slot.Value.AsSpan(0, length).CopyTo(destination);
        return true;
    }
}

public interface IManagedHtmlTokenConsumer
{
    ManagedHtmlTokenConsumerState State { get; }
    ManagedHtmlTokenConsumerFailureReason FailureReason { get; }
    int TokensProcessed { get; }
    ManagedHttpBodySinkResult Consume(in ManagedHtmlToken token);
    bool Complete();
    void Cancel();
    void Reset();
}

public readonly struct ManagedHtmlTokenizerProgressSnapshot
{
    internal ManagedHtmlTokenizerProgressSnapshot(
        ManagedHtmlTokenizerState state,
        ManagedHtmlTokenizerFailureReason failureReason,
        int scalarsReceived, int scalarsConsumed, int tokensEmitted,
        int textTokens, int startTagTokens, int endTagTokens,
        int commentTokens, int doctypeTokens, int attributesEmitted,
        int characterReferencesDecoded, int bufferedTextScalars,
        int currentTagNameLength, int currentAttributeCount,
        int pauseCount, int resumeCount, bool tokenPending,
        int peakTextScalars)
    {
        State = state;
        FailureReason = failureReason;
        ScalarsReceived = scalarsReceived;
        ScalarsConsumed = scalarsConsumed;
        TokensEmitted = tokensEmitted;
        TextTokens = textTokens;
        StartTagTokens = startTagTokens;
        EndTagTokens = endTagTokens;
        CommentTokens = commentTokens;
        DoctypeTokens = doctypeTokens;
        AttributesEmitted = attributesEmitted;
        CharacterReferencesDecoded = characterReferencesDecoded;
        BufferedTextScalars = bufferedTextScalars;
        CurrentTagNameLength = currentTagNameLength;
        CurrentAttributeCount = currentAttributeCount;
        PauseCount = pauseCount;
        ResumeCount = resumeCount;
        TokenPending = tokenPending;
        PeakTextScalars = peakTextScalars;
    }

    public ManagedHtmlTokenizerState State { get; }
    public ManagedHtmlTokenizerFailureReason FailureReason { get; }
    public int ScalarsReceived { get; }
    public int ScalarsConsumed { get; }
    public int TokensEmitted { get; }
    public int TextTokens { get; }
    public int StartTagTokens { get; }
    public int EndTagTokens { get; }
    public int CommentTokens { get; }
    public int DoctypeTokens { get; }
    public int AttributesEmitted { get; }
    public int CharacterReferencesDecoded { get; }
    public int BufferedTextScalars { get; }
    public int CurrentTagNameLength { get; }
    public int CurrentAttributeCount { get; }
    public int PauseCount { get; }
    public int ResumeCount { get; }
    public bool TokenPending { get; }
    public int PeakTextScalars { get; }
    public bool IsComplete => State == ManagedHtmlTokenizerState.Completed;
    public bool IsCancelled => State == ManagedHtmlTokenizerState.Cancelled;
    public bool IsTerminal => State == ManagedHtmlTokenizerState.Completed ||
                              State == ManagedHtmlTokenizerState.Cancelled ||
                              State == ManagedHtmlTokenizerState.Failed;
}

public static class ManagedHtmlTokenizerLimits
{
    public const int InputWindowCapacity = 256;
    public const int TextTokenCapacity = 128;
    public const int MaximumTagNameLength = 64;
    public const int MaximumAttributeNameLength = 32;
    public const int MaximumAttributeValueLength = 256;
    public const int MaximumAttributesPerTag = 16;
    public const int MaximumCommentFragmentLength = 256;
    public const int MaximumDoctypeNameLength = 32;
    public const int MaximumEntityNameLength = 32;
    public const int MaximumRawCandidateLength = MaximumTagNameLength + 8;
}

/* A bounded, resumable HTML lexical tokenizer.  It deliberately stops at a
   token boundary and has no tree, token queue, document string, recursion, or
   storage that scales with the input document. */
public sealed class ManagedHtmlTokenizer
{
    private enum HtmlMode : byte
    {
        Data = 0,
        RawText = 1,
        RcData = 2,
        ScriptData = 3
    }

    private enum EntityDestination : byte
    {
        Text = 0,
        Attribute = 1
    }

    private enum InputStep : byte
    {
        Consumed = 0,
        Blocked = 1,
        Failed = 2
    }

    private readonly uint[] _input =
        new uint[ManagedHtmlTokenizerLimits.InputWindowCapacity];
    private readonly uint[] _text =
        new uint[ManagedHtmlTokenizerLimits.TextTokenCapacity];
    private readonly byte[] _tagName =
        new byte[ManagedHtmlTokenizerLimits.MaximumTagNameLength];
    private readonly byte[] _rawTagName =
        new byte[ManagedHtmlTokenizerLimits.MaximumTagNameLength];
    private readonly byte[] _currentAttributeName =
        new byte[ManagedHtmlTokenizerLimits.MaximumAttributeNameLength];
    private readonly uint[] _currentAttributeValue =
        new uint[ManagedHtmlTokenizerLimits.MaximumAttributeValueLength];
    private readonly byte[][] _attributeNames =
        new byte[ManagedHtmlTokenizerLimits.MaximumAttributesPerTag][];
    private readonly uint[][] _attributeValues =
        new uint[ManagedHtmlTokenizerLimits.MaximumAttributesPerTag][];
    private readonly ManagedHtmlAttributeSlot[] _attributeSlots =
        new ManagedHtmlAttributeSlot[ManagedHtmlTokenizerLimits.MaximumAttributesPerTag];
    private readonly uint[] _comment =
        new uint[ManagedHtmlTokenizerLimits.MaximumCommentFragmentLength];
    private readonly byte[] _doctypeName =
        new byte[ManagedHtmlTokenizerLimits.MaximumDoctypeNameLength];
    private readonly uint[] _entityOutput =
        new uint[ManagedHtmlTokenizerLimits.MaximumEntityNameLength + 2];
    private readonly uint[] _entityName =
        new uint[ManagedHtmlTokenizerLimits.MaximumEntityNameLength];
    private readonly uint[] _rawCandidate =
        new uint[ManagedHtmlTokenizerLimits.MaximumRawCandidateLength];

    private ManagedHtmlTokenizerState _state;
    private ManagedHtmlTokenizerFailureReason _failureReason;
    private HtmlMode _mode;
    private EntityDestination _entityDestination;
    private ManagedHtmlTokenizerState _entityReturnState;
    private ManagedHtmlTokenizerState _pausedState;
    private ManagedHtmlToken _pendingToken;
    private ManagedHtmlTokenKind _pendingKind;
    private bool _tokenPending;
    private bool _pendingCommentFinal;
    private bool _selfClosing;
    private bool _currentAttributeActive;
    private bool _currentAttributeHasValue;
    private int _inputOffset;
    private int _inputLength;
    private int _textLength;
    private int _tagNameLength;
    private int _rawTagNameLength;
    private int _currentAttributeNameLength;
    private int _currentAttributeValueLength;
    private int _attributeCount;
    private int _commentLength;
    private int _commentPendingDashes;
    private int _doctypeNameLength;
    private int _doctypeKeywordIndex;
    private int _entityLength;
    private int _entityOutputLength;
    private int _entityOutputOffset;
    private int _rawCandidateLength;
    private int _rawFlushOffset;
    private int _scalarsReceived;
    private int _scalarsConsumed;
    private int _tokensEmitted;
    private int _textTokens;
    private int _startTagTokens;
    private int _endTagTokens;
    private int _commentTokens;
    private int _doctypeTokens;
    private int _attributesEmitted;
    private int _characterReferencesDecoded;
    private int _pauseCount;
    private int _resumeCount;
    private int _peakTextScalars;
    private bool _paused;

    public ManagedHtmlTokenizer()
    {
        for (int index = 0; index != _attributeNames.Length; ++index)
        {
            _attributeNames[index] =
                new byte[ManagedHtmlTokenizerLimits.MaximumAttributeNameLength];
            _attributeValues[index] =
                new uint[ManagedHtmlTokenizerLimits.MaximumAttributeValueLength];
        }
        Reset();
    }

    public ManagedHtmlTokenizerState State => _state;
    public ManagedHtmlTokenizerFailureReason FailureReason => _failureReason;
    public int InputLength => _inputLength;
    public int InputFreeCapacity => _input.Length - _inputLength;
    public int ScalarsReceived => _scalarsReceived;
    public int ScalarsConsumed => _scalarsConsumed;
    public int TokensEmitted => _tokensEmitted;
    public int TextTokenCount => _textTokens;
    public int StartTagCount => _startTagTokens;
    public int EndTagCount => _endTagTokens;
    public int CommentCount => _commentTokens;
    public int DoctypeCount => _doctypeTokens;
    public int AttributesEmitted => _attributesEmitted;
    public int CharacterReferencesDecoded => _characterReferencesDecoded;
    public int BufferedTextScalars => _textLength;
    public int CurrentTagNameLength => _tagNameLength;
    public int CurrentAttributeCount => _attributeCount;
    public int PauseCount => _pauseCount;
    public int ResumeCount => _resumeCount;
    public int PeakTextScalars => _peakTextScalars;
    public bool IsComplete => _state == ManagedHtmlTokenizerState.Completed;
    public bool IsCancelled => _state == ManagedHtmlTokenizerState.Cancelled;
    public bool IsTerminal => _state == ManagedHtmlTokenizerState.Completed ||
                              _state == ManagedHtmlTokenizerState.Cancelled ||
                              _state == ManagedHtmlTokenizerState.Failed;
    public ManagedHtmlTokenizerProgressSnapshot Progress => new(
        _state, _failureReason, _scalarsReceived, _scalarsConsumed,
        _tokensEmitted, _textTokens, _startTagTokens, _endTagTokens,
        _commentTokens, _doctypeTokens, _attributesEmitted,
        _characterReferencesDecoded, _textLength, _tagNameLength,
        _attributeCount, _pauseCount, _resumeCount, _tokenPending,
        _peakTextScalars);

    public bool AppendInput(ReadOnlySpan<uint> input)
    {
        if (_state == ManagedHtmlTokenizerState.Cancelled ||
            _state == ManagedHtmlTokenizerState.Failed ||
            _state == ManagedHtmlTokenizerState.Completed || _paused ||
            input.Length > InputFreeCapacity)
            return false;
        if (input.Length == 0) return true;
        if (_inputOffset + _inputLength + input.Length > _input.Length)
        {
            _input.AsSpan(_inputOffset, _inputLength).CopyTo(_input);
            _inputOffset = 0;
        }
        input.CopyTo(_input.AsSpan(_inputOffset + _inputLength));
        _inputLength += input.Length;
        _scalarsReceived += input.Length;
        if (_state == ManagedHtmlTokenizerState.Idle)
            _state = ManagedHtmlTokenizerState.Data;
        return true;
    }

    public ManagedHtmlTokenizerProcessResult Pump(
        IManagedHtmlTokenConsumer consumer, bool endOfInput = false)
    {
        if (consumer == null) throw new ArgumentNullException(nameof(consumer));
        if (_state == ManagedHtmlTokenizerState.Cancelled)
            return ManagedHtmlTokenizerProcessResult.Cancelled;
        if (_state == ManagedHtmlTokenizerState.Failed)
            return ManagedHtmlTokenizerProcessResult.Failed;
        if (_state == ManagedHtmlTokenizerState.Completed)
            return ManagedHtmlTokenizerProcessResult.Complete;
        if (_paused) return ManagedHtmlTokenizerProcessResult.Paused;

        bool madeProgress = false;
        while (true)
        {
            if (_state == ManagedHtmlTokenizerState.Completed)
                return ManagedHtmlTokenizerProcessResult.Complete;
            if (_tokenPending)
            {
                ManagedHttpBodySinkResult delivery =
                    consumer.Consume(in _pendingToken);
                if (delivery == ManagedHttpBodySinkResult.Pause)
                {
                    if (!_paused) { _paused = true; ++_pauseCount; }
                    _pausedState = _state;
                    _state = ManagedHtmlTokenizerState.Paused;
                    return ManagedHtmlTokenizerProcessResult.Paused;
                }
                if (delivery == ManagedHttpBodySinkResult.Fail)
                {
                    Fail(ManagedHtmlTokenizerFailureReason.TokenConsumerFailure);
                    return ManagedHtmlTokenizerProcessResult.Failed;
                }
                CommitPendingToken();
                madeProgress = true;
                continue;
            }

            if (_state == ManagedHtmlTokenizerState.Paused)
                return ManagedHtmlTokenizerProcessResult.Paused;

            if (_entityOutputOffset < _entityOutputLength)
            {
                InputStep entityStep = FlushEntityOutput();
                if (entityStep == InputStep.Failed)
                    return ManagedHtmlTokenizerProcessResult.Failed;
                if (entityStep == InputStep.Blocked)
                    continue;
                madeProgress = true;
                continue;
            }

            if (_inputLength != 0)
            {
                uint scalar = _input[_inputOffset];
                InputStep step = ProcessScalar(scalar);
                if (step == InputStep.Failed)
                    return ManagedHtmlTokenizerProcessResult.Failed;
                if (step == InputStep.Blocked)
                    continue;
                _inputOffset = (_inputOffset + 1) % _input.Length;
                --_inputLength;
                ++_scalarsConsumed;
                madeProgress = true;
                continue;
            }

            if (!endOfInput)
                return madeProgress ? ManagedHtmlTokenizerProcessResult.Progress :
                    ManagedHtmlTokenizerProcessResult.NeedInput;

            InputStep eofStep = FinishAtEof();
            if (eofStep == InputStep.Failed)
                return ManagedHtmlTokenizerProcessResult.Failed;
            if (eofStep == InputStep.Blocked)
                continue;
            madeProgress = true;
            if (_tokenPending)
            {
                continue;
            }
            if (_state == ManagedHtmlTokenizerState.Completed)
                return ManagedHtmlTokenizerProcessResult.Complete;
            if (!madeProgress)
            {
                Fail(ManagedHtmlTokenizerFailureReason.NoProgress);
                return ManagedHtmlTokenizerProcessResult.Failed;
            }
        }
    }

    public void Resume()
    {
        if (_state == ManagedHtmlTokenizerState.Paused)
        {
            _paused = false;
            _state = StateAfterPause();
            ++_resumeCount;
        }
    }

    public void Cancel()
    {
        if (IsTerminal && _state != ManagedHtmlTokenizerState.Paused) return;
        ClearStorage();
        _failureReason = ManagedHtmlTokenizerFailureReason.Cancelled;
        _state = ManagedHtmlTokenizerState.Cancelled;
        _paused = false;
    }

    public void Reset()
    {
        ClearStorage();
        _state = ManagedHtmlTokenizerState.Idle;
        _failureReason = ManagedHtmlTokenizerFailureReason.None;
        _mode = HtmlMode.Data;
        _entityDestination = EntityDestination.Text;
        _entityReturnState = ManagedHtmlTokenizerState.Data;
        _pendingKind = ManagedHtmlTokenKind.Text;
        _pendingCommentFinal = false;
        _selfClosing = false;
        _currentAttributeActive = false;
        _currentAttributeHasValue = false;
        _scalarsReceived = 0;
        _scalarsConsumed = 0;
        _tokensEmitted = 0;
        _textTokens = 0;
        _startTagTokens = 0;
        _endTagTokens = 0;
        _commentTokens = 0;
        _doctypeTokens = 0;
        _attributesEmitted = 0;
        _characterReferencesDecoded = 0;
        _pauseCount = 0;
        _resumeCount = 0;
        _peakTextScalars = 0;
        _paused = false;
    }

    private ManagedHtmlTokenizerState StateAfterPause() => _pausedState;

    private InputStep ProcessScalar(uint scalar)
    {
        switch (_state)
        {
            case ManagedHtmlTokenizerState.Idle:
            case ManagedHtmlTokenizerState.Data:
                return ProcessData(scalar);
            case ManagedHtmlTokenizerState.TagOpen:
                return ProcessTagOpen(scalar);
            case ManagedHtmlTokenizerState.EndTagOpen:
                return ProcessEndTagOpen(scalar);
            case ManagedHtmlTokenizerState.TagName:
                return ProcessTagName(scalar);
            case ManagedHtmlTokenizerState.BeforeAttributeName:
                return ProcessBeforeAttributeName(scalar);
            case ManagedHtmlTokenizerState.AttributeName:
                return ProcessAttributeName(scalar);
            case ManagedHtmlTokenizerState.AfterAttributeName:
                return ProcessAfterAttributeName(scalar);
            case ManagedHtmlTokenizerState.BeforeAttributeValue:
                return ProcessBeforeAttributeValue(scalar);
            case ManagedHtmlTokenizerState.AttributeValueDoubleQuoted:
                return ProcessQuotedAttributeValue(scalar, (uint)'"');
            case ManagedHtmlTokenizerState.AttributeValueSingleQuoted:
                return ProcessQuotedAttributeValue(scalar, (uint)'\'');
            case ManagedHtmlTokenizerState.AttributeValueUnquoted:
                return ProcessUnquotedAttributeValue(scalar);
            case ManagedHtmlTokenizerState.AfterAttributeValueQuoted:
                return ProcessAfterAttributeValueQuoted(scalar);
            case ManagedHtmlTokenizerState.SelfClosingStartTag:
                return ProcessSelfClosingStartTag(scalar);
            case ManagedHtmlTokenizerState.MarkupDeclarationOpen:
                return ProcessMarkupDeclarationOpen(scalar);
            case ManagedHtmlTokenizerState.CommentSecondDash:
                return ProcessCommentSecondDash(scalar);
            case ManagedHtmlTokenizerState.Comment:
                return ProcessComment(scalar);
            case ManagedHtmlTokenizerState.DoctypeKeyword:
                return ProcessDoctypeKeyword(scalar);
            case ManagedHtmlTokenizerState.DoctypeBeforeName:
                return ProcessDoctypeBeforeName(scalar);
            case ManagedHtmlTokenizerState.DoctypeName:
                return ProcessDoctypeName(scalar);
            case ManagedHtmlTokenizerState.DoctypeAfterName:
                return ProcessDoctypeAfterName(scalar);
            case ManagedHtmlTokenizerState.EndTagAfterName:
                return ProcessEndTagAfterName(scalar);
            case ManagedHtmlTokenizerState.CharacterReference:
                return ProcessCharacterReference(scalar);
            case ManagedHtmlTokenizerState.RawText:
            case ManagedHtmlTokenizerState.RcData:
            case ManagedHtmlTokenizerState.ScriptData:
                return ProcessRawData(scalar);
            case ManagedHtmlTokenizerState.RawTextCandidate:
                return ProcessRawCandidate(scalar);
            case ManagedHtmlTokenizerState.RawTextCandidateFlush:
            {
                InputStep flush = FlushRawCandidate();
                return flush == InputStep.Consumed ? InputStep.Blocked : flush;
            }
            case ManagedHtmlTokenizerState.RawTextCloseAfterText:
                PrepareRawEndTagToken();
                return InputStep.Blocked;
            default:
                Fail(ManagedHtmlTokenizerFailureReason.NoProgress);
                return InputStep.Failed;
        }
    }

    private InputStep ProcessData(uint scalar)
    {
        if (_textLength == _text.Length)
        {
            PrepareTextToken();
            return InputStep.Blocked;
        }
        if (scalar == (uint)'<')
        {
            _state = ManagedHtmlTokenizerState.TagOpen;
            if (_textLength != 0) PrepareTextToken();
            return InputStep.Consumed;
        }
        if (scalar == (uint)'&')
        {
            BeginEntity(EntityDestination.Text, ManagedHtmlTokenizerState.Data);
            return InputStep.Consumed;
        }
        return AppendTextScalar(scalar);
    }

    private InputStep ProcessTagOpen(uint scalar)
    {
        if (IsAsciiLetter(scalar))
        {
            ResetTagName();
            AppendTagNameUnchecked(scalar);
            _state = ManagedHtmlTokenizerState.TagName;
            return InputStep.Consumed;
        }
        if (scalar == (uint)'/')
        {
            ResetTagName();
            _state = ManagedHtmlTokenizerState.EndTagOpen;
            return InputStep.Consumed;
        }
        if (scalar == (uint)'!')
        {
            _state = ManagedHtmlTokenizerState.MarkupDeclarationOpen;
            return InputStep.Consumed;
        }
        Fail(ManagedHtmlTokenizerFailureReason.InvalidMarkup);
        return InputStep.Failed;
    }

    private InputStep ProcessEndTagOpen(uint scalar)
    {
        if (!IsAsciiLetter(scalar))
        {
            Fail(ManagedHtmlTokenizerFailureReason.InvalidMarkup);
            return InputStep.Failed;
        }
        ResetTagName();
        AppendTagNameUnchecked(scalar);
        _state = ManagedHtmlTokenizerState.TagName;
        _pendingKind = ManagedHtmlTokenKind.EndTag;
        return InputStep.Consumed;
    }

    private InputStep ProcessTagName(uint scalar)
    {
        if (IsTagNameCharacter(scalar))
            return AppendTagName(scalar);
        if (IsHtmlWhitespace(scalar))
        {
            _state = _pendingKind == ManagedHtmlTokenKind.EndTag
                ? ManagedHtmlTokenizerState.EndTagAfterName
                : ManagedHtmlTokenizerState.BeforeAttributeName;
            return InputStep.Consumed;
        }
        if (scalar == (uint)'>')
            return _pendingKind == ManagedHtmlTokenKind.EndTag
                ? FinishEndTag() : FinishStartTag();
        if (scalar == (uint)'/' && _pendingKind != ManagedHtmlTokenKind.EndTag)
        {
            _selfClosing = true;
            _state = ManagedHtmlTokenizerState.SelfClosingStartTag;
            return InputStep.Consumed;
        }
        Fail(ManagedHtmlTokenizerFailureReason.InvalidMarkup);
        return InputStep.Failed;
    }

    private InputStep ProcessBeforeAttributeName(uint scalar)
    {
        if (IsHtmlWhitespace(scalar)) return InputStep.Consumed;
        if (scalar == (uint)'>') return FinishStartTag();
        if (scalar == (uint)'/')
        {
            _selfClosing = true;
            _state = ManagedHtmlTokenizerState.SelfClosingStartTag;
            return InputStep.Consumed;
        }
        if (!IsAttributeNameCharacter(scalar))
        {
            Fail(ManagedHtmlTokenizerFailureReason.InvalidMarkup);
            return InputStep.Failed;
        }
        if (!TryBeginAttribute(scalar)) return InputStep.Failed;
        _state = ManagedHtmlTokenizerState.AttributeName;
        return InputStep.Consumed;
    }

    private InputStep ProcessAttributeName(uint scalar)
    {
        if (IsAttributeNameCharacter(scalar))
            return AppendAttributeName(scalar);
        if (IsHtmlWhitespace(scalar))
        {
            _state = ManagedHtmlTokenizerState.AfterAttributeName;
            return InputStep.Consumed;
        }
        if (scalar == (uint)'=')
        {
            _state = ManagedHtmlTokenizerState.BeforeAttributeValue;
            return InputStep.Consumed;
        }
        if (scalar == (uint)'>')
        {
            CommitCurrentAttribute(false);
            return FinishStartTag();
        }
        if (scalar == (uint)'/')
        {
            CommitCurrentAttribute(false);
            _selfClosing = true;
            _state = ManagedHtmlTokenizerState.SelfClosingStartTag;
            return InputStep.Consumed;
        }
        Fail(ManagedHtmlTokenizerFailureReason.InvalidMarkup);
        return InputStep.Failed;
    }

    private InputStep ProcessAfterAttributeName(uint scalar)
    {
        if (IsHtmlWhitespace(scalar)) return InputStep.Consumed;
        if (scalar == (uint)'=')
        {
            _state = ManagedHtmlTokenizerState.BeforeAttributeValue;
            return InputStep.Consumed;
        }
        if (scalar == (uint)'>')
        {
            CommitCurrentAttribute(false);
            return FinishStartTag();
        }
        if (scalar == (uint)'/')
        {
            CommitCurrentAttribute(false);
            _selfClosing = true;
            _state = ManagedHtmlTokenizerState.SelfClosingStartTag;
            return InputStep.Consumed;
        }
        CommitCurrentAttribute(false);
        if (!IsAttributeNameCharacter(scalar))
        {
            Fail(ManagedHtmlTokenizerFailureReason.InvalidMarkup);
            return InputStep.Failed;
        }
        if (!TryBeginAttribute(scalar)) return InputStep.Failed;
        _state = ManagedHtmlTokenizerState.AttributeName;
        return InputStep.Consumed;
    }

    private InputStep ProcessBeforeAttributeValue(uint scalar)
    {
        if (IsHtmlWhitespace(scalar)) return InputStep.Consumed;
        if (scalar == (uint)'"')
        {
            _currentAttributeHasValue = true;
            _state = ManagedHtmlTokenizerState.AttributeValueDoubleQuoted;
            return InputStep.Consumed;
        }
        if (scalar == (uint)'\'')
        {
            _currentAttributeHasValue = true;
            _state = ManagedHtmlTokenizerState.AttributeValueSingleQuoted;
            return InputStep.Consumed;
        }
        if (scalar == (uint)'>')
        {
            _currentAttributeHasValue = true;
            CommitCurrentAttribute(false);
            return FinishStartTag();
        }
        if (scalar == (uint)'/')
        {
            _currentAttributeHasValue = true;
            CommitCurrentAttribute(false);
            _selfClosing = true;
            _state = ManagedHtmlTokenizerState.SelfClosingStartTag;
            return InputStep.Consumed;
        }
        _currentAttributeHasValue = true;
        if (scalar == (uint)'&')
        {
            BeginEntity(EntityDestination.Attribute,
                        ManagedHtmlTokenizerState.AttributeValueUnquoted);
            return InputStep.Consumed;
        }
        _state = ManagedHtmlTokenizerState.AttributeValueUnquoted;
        return AppendAttributeValue(scalar);
    }

    private InputStep ProcessQuotedAttributeValue(uint scalar, uint quote)
    {
        if (scalar == quote)
        {
            CommitCurrentAttribute(true);
            _state = ManagedHtmlTokenizerState.AfterAttributeValueQuoted;
            return InputStep.Consumed;
        }
        if (scalar == (uint)'&')
        {
            BeginEntity(EntityDestination.Attribute, _state);
            return InputStep.Consumed;
        }
        return AppendAttributeValue(scalar);
    }

    private InputStep ProcessUnquotedAttributeValue(uint scalar)
    {
        if (IsHtmlWhitespace(scalar))
        {
            CommitCurrentAttribute(true);
            _state = ManagedHtmlTokenizerState.BeforeAttributeName;
            return InputStep.Consumed;
        }
        if (scalar == (uint)'>')
        {
            CommitCurrentAttribute(true);
            return FinishStartTag();
        }
        if (scalar == (uint)'&')
        {
            BeginEntity(EntityDestination.Attribute,
                        ManagedHtmlTokenizerState.AttributeValueUnquoted);
            return InputStep.Consumed;
        }
        if (scalar == (uint)'"' || scalar == (uint)'\'')
        {
            Fail(ManagedHtmlTokenizerFailureReason.InvalidMarkup);
            return InputStep.Failed;
        }
        return AppendAttributeValue(scalar);
    }

    private InputStep ProcessAfterAttributeValueQuoted(uint scalar)
    {
        if (IsHtmlWhitespace(scalar))
        {
            _state = ManagedHtmlTokenizerState.BeforeAttributeName;
            return InputStep.Consumed;
        }
        if (scalar == (uint)'>') return FinishStartTag();
        if (scalar == (uint)'/')
        {
            _selfClosing = true;
            _state = ManagedHtmlTokenizerState.SelfClosingStartTag;
            return InputStep.Consumed;
        }
        Fail(ManagedHtmlTokenizerFailureReason.InvalidMarkup);
        return InputStep.Failed;
    }

    private InputStep ProcessSelfClosingStartTag(uint scalar)
    {
        if (scalar != (uint)'>')
        {
            Fail(ManagedHtmlTokenizerFailureReason.InvalidMarkup);
            return InputStep.Failed;
        }
        return FinishStartTag();
    }

    private InputStep ProcessMarkupDeclarationOpen(uint scalar)
    {
        if (scalar == (uint)'-')
        {
            _state = ManagedHtmlTokenizerState.CommentSecondDash;
            return InputStep.Consumed;
        }
        if (ToLowerAscii(scalar) == (uint)'d')
        {
            _doctypeKeywordIndex = 1;
            _state = ManagedHtmlTokenizerState.DoctypeKeyword;
            return InputStep.Consumed;
        }
        Fail(ManagedHtmlTokenizerFailureReason.UnsupportedMarkupDeclaration);
        return InputStep.Failed;
    }

    private InputStep ProcessCommentSecondDash(uint scalar)
    {
        if (scalar != (uint)'-')
        {
            Fail(ManagedHtmlTokenizerFailureReason.CommentStateError);
            return InputStep.Failed;
        }
        _commentLength = 0;
        _commentPendingDashes = 0;
        _state = ManagedHtmlTokenizerState.Comment;
        return InputStep.Consumed;
    }

    private InputStep ProcessComment(uint scalar)
    {
        if (scalar == (uint)'-')
        {
            if (_commentPendingDashes < 2)
            {
                ++_commentPendingDashes;
                return InputStep.Consumed;
            }
            InputStep extraDash = AppendCommentScalar((uint)'-');
            if (extraDash != InputStep.Consumed) return extraDash;
            return InputStep.Consumed;
        }
        if (_commentPendingDashes != 0)
        {
            if (scalar == (uint)'>' && _commentPendingDashes == 2)
            {
                _commentPendingDashes = 0;
                PrepareCommentToken(true);
                _state = ManagedHtmlTokenizerState.Data;
                return InputStep.Consumed;
            }
            while (_commentPendingDashes != 0)
            {
                InputStep dash = AppendCommentScalar((uint)'-');
                if (dash != InputStep.Consumed) return dash;
                --_commentPendingDashes;
            }
        }
        return AppendCommentScalar(scalar);
    }

    private InputStep ProcessDoctypeKeyword(uint scalar)
    {
        ReadOnlySpan<byte> keyword = "doctype"u8;
        if (_doctypeKeywordIndex >= keyword.Length ||
            ToLowerAscii(scalar) != keyword[_doctypeKeywordIndex])
        {
            Fail(ManagedHtmlTokenizerFailureReason.UnsupportedMarkupDeclaration);
            return InputStep.Failed;
        }
        ++_doctypeKeywordIndex;
        if (_doctypeKeywordIndex == keyword.Length)
            _state = ManagedHtmlTokenizerState.DoctypeBeforeName;
        return InputStep.Consumed;
    }

    private InputStep ProcessDoctypeBeforeName(uint scalar)
    {
        if (IsHtmlWhitespace(scalar)) return InputStep.Consumed;
        if (scalar == (uint)'>')
        {
            Fail(ManagedHtmlTokenizerFailureReason.InvalidMarkup);
            return InputStep.Failed;
        }
        if (!IsAsciiLetter(scalar))
        {
            Fail(ManagedHtmlTokenizerFailureReason.InvalidMarkup);
            return InputStep.Failed;
        }
        _doctypeNameLength = 0;
        _doctypeName[_doctypeNameLength++] = ToLowerByte(scalar);
        _state = ManagedHtmlTokenizerState.DoctypeName;
        return InputStep.Consumed;
    }

    private InputStep ProcessDoctypeName(uint scalar)
    {
        if (IsHtmlWhitespace(scalar))
        {
            _state = ManagedHtmlTokenizerState.DoctypeAfterName;
            return InputStep.Consumed;
        }
        if (scalar == (uint)'>') return FinishDoctype();
        if (!IsAsciiNameCharacter(scalar))
        {
            Fail(ManagedHtmlTokenizerFailureReason.InvalidMarkup);
            return InputStep.Failed;
        }
        if (_doctypeNameLength == _doctypeName.Length)
        {
            Fail(ManagedHtmlTokenizerFailureReason.DoctypeTooLong);
            return InputStep.Failed;
        }
        _doctypeName[_doctypeNameLength++] = ToLowerByte(scalar);
        return InputStep.Consumed;
    }

    private InputStep ProcessDoctypeAfterName(uint scalar)
    {
        if (IsHtmlWhitespace(scalar)) return InputStep.Consumed;
        if (scalar == (uint)'>') return FinishDoctype();
        Fail(ManagedHtmlTokenizerFailureReason.UnsupportedMarkupDeclaration);
        return InputStep.Failed;
    }

    private InputStep ProcessEndTagAfterName(uint scalar)
    {
        if (IsHtmlWhitespace(scalar)) return InputStep.Consumed;
        if (scalar == (uint)'>') return FinishEndTag();
        Fail(ManagedHtmlTokenizerFailureReason.InvalidMarkup);
        return InputStep.Failed;
    }

    private InputStep ProcessCharacterReference(uint scalar)
    {
        if (scalar == (uint)';')
        {
            ResolveEntity(true);
            return _state == ManagedHtmlTokenizerState.Failed
                ? InputStep.Failed : InputStep.Consumed;
        }
        if (IsEntityCharacter(scalar))
        {
            if (_entityLength == _entityName.Length)
            {
                Fail(ManagedHtmlTokenizerFailureReason.EntityNameTooLong);
                return InputStep.Failed;
            }
            _entityName[_entityLength++] = scalar;
            return InputStep.Consumed;
        }
        ResolveEntity(false);
        return _state == ManagedHtmlTokenizerState.Failed
            ? InputStep.Failed : InputStep.Blocked;
    }

    private InputStep ProcessRawData(uint scalar)
    {
        if (_textLength == _text.Length)
        {
            PrepareTextToken();
            return InputStep.Blocked;
        }
        if (scalar == (uint)'<')
        {
            _rawCandidateLength = 1;
            _rawCandidate[0] = scalar;
            _state = ManagedHtmlTokenizerState.RawTextCandidate;
            return InputStep.Consumed;
        }
        if (_mode == HtmlMode.RcData && scalar == (uint)'&')
        {
            BeginEntity(EntityDestination.Text,
                        ManagedHtmlTokenizerState.RcData);
            return InputStep.Consumed;
        }
        return AppendTextScalar(scalar);
    }

    private InputStep ProcessRawCandidate(uint scalar)
    {
        if (_rawCandidateLength == _rawCandidate.Length)
        {
            _rawFlushOffset = 0;
            _state = ManagedHtmlTokenizerState.RawTextCandidateFlush;
            return InputStep.Blocked;
        }
        _rawCandidate[_rawCandidateLength++] = scalar;
        int status = RawCandidateStatus();
        if (status == 0) return InputStep.Consumed;
        if (status < 0)
        {
            _rawFlushOffset = 0;
            _state = ManagedHtmlTokenizerState.RawTextCandidateFlush;
            return InputStep.Consumed;
        }
        if (_textLength != 0)
        {
            _state = ManagedHtmlTokenizerState.RawTextCloseAfterText;
            PrepareTextToken();
            return InputStep.Consumed;
        }
        PrepareRawEndTagToken();
        return InputStep.Consumed;
    }

    private InputStep FlushRawCandidate()
    {
        while (_rawFlushOffset != _rawCandidateLength)
        {
            InputStep result = AppendTextScalar(_rawCandidate[_rawFlushOffset]);
            if (result != InputStep.Consumed) return result;
            ++_rawFlushOffset;
        }
        _rawCandidateLength = 0;
        _rawFlushOffset = 0;
        _state = StateForMode();
        return InputStep.Consumed;
    }

    private int RawCandidateStatus()
    {
        if (_rawCandidateLength >= 1 && _rawCandidate[0] != (uint)'<') return -1;
        if (_rawCandidateLength >= 2 && _rawCandidate[1] != (uint)'/') return -1;
        int nameEnd = 2 + _rawTagNameLength;
        for (int index = 2; index < _rawCandidateLength && index < nameEnd; ++index)
        {
            if (ToLowerAscii(_rawCandidate[index]) != _rawTagName[index - 2])
                return -1;
        }
        if (_rawCandidateLength < nameEnd) return 0;
        if (_rawCandidateLength == nameEnd) return 0;
        for (int index = nameEnd; index != _rawCandidateLength; ++index)
        {
            uint scalar = _rawCandidate[index];
            if (scalar == (uint)'>')
                return index == _rawCandidateLength - 1 ? 1 : -1;
            if (!IsHtmlWhitespace(scalar)) return -1;
        }
        return 0;
    }

    private InputStep AppendTextScalar(uint scalar)
    {
        if (_textLength == _text.Length)
        {
            PrepareTextToken();
            return InputStep.Blocked;
        }
        _text[_textLength++] = scalar;
        if (_textLength > _peakTextScalars) _peakTextScalars = _textLength;
        return InputStep.Consumed;
    }

    private InputStep AppendCommentScalar(uint scalar)
    {
        if (_commentLength == _comment.Length)
        {
            PrepareCommentToken(false);
            return InputStep.Blocked;
        }
        _comment[_commentLength++] = scalar;
        return InputStep.Consumed;
    }

    private InputStep AppendTagName(uint scalar)
    {
        if (_tagNameLength == _tagName.Length)
        {
            Fail(ManagedHtmlTokenizerFailureReason.TagNameTooLong);
            return InputStep.Failed;
        }
        AppendTagNameUnchecked(scalar);
        return InputStep.Consumed;
    }

    private InputStep AppendAttributeName(uint scalar)
    {
        if (_currentAttributeNameLength == _currentAttributeName.Length)
        {
            Fail(ManagedHtmlTokenizerFailureReason.AttributeNameTooLong);
            return InputStep.Failed;
        }
        _currentAttributeName[_currentAttributeNameLength++] = ToLowerByte(scalar);
        return InputStep.Consumed;
    }

    private InputStep AppendAttributeValue(uint scalar)
    {
        if (_currentAttributeValueLength == _currentAttributeValue.Length)
        {
            Fail(ManagedHtmlTokenizerFailureReason.AttributeValueTooLong);
            return InputStep.Failed;
        }
        _currentAttributeValue[_currentAttributeValueLength++] = scalar;
        return InputStep.Consumed;
    }

    private void BeginEntity(EntityDestination destination,
                             ManagedHtmlTokenizerState returnState)
    {
        _entityDestination = destination;
        _entityReturnState = returnState;
        _entityLength = 0;
        _entityOutputLength = 0;
        _entityOutputOffset = 0;
        _state = ManagedHtmlTokenizerState.CharacterReference;
    }

    private void ResolveEntity(bool semicolon)
    {
        _entityOutputLength = 0;
        _entityOutputOffset = 0;
        bool resolved = false;
        if (semicolon && TryResolveEntity(out uint scalar))
        {
            _entityOutput[0] = scalar;
            _entityOutputLength = 1;
            resolved = true;
            ++_characterReferencesDecoded;
        }
        else if (semicolon && IsNumericEntity())
        {
            Fail(ManagedHtmlTokenizerFailureReason.InvalidNumericEntity);
            return;
        }
        if (!resolved)
        {
            int offset = 0;
            _entityOutput[offset++] = (uint)'&';
            for (int index = 0; index != _entityLength; ++index)
                _entityOutput[offset++] = _entityName[index];
            if (semicolon) _entityOutput[offset++] = (uint)';';
            _entityOutputLength = offset;
        }
        _entityLength = 0;
    }

    private InputStep FlushEntityOutput()
    {
        while (_entityOutputOffset != _entityOutputLength)
        {
            InputStep result = _entityDestination == EntityDestination.Text
                ? AppendTextScalar(_entityOutput[_entityOutputOffset])
                : AppendAttributeValue(_entityOutput[_entityOutputOffset]);
            if (result != InputStep.Consumed) return result;
            ++_entityOutputOffset;
        }
        _entityOutputLength = 0;
        _entityOutputOffset = 0;
        _state = _entityReturnState;
        return InputStep.Consumed;
    }

    private bool TryResolveEntity(out uint scalar)
    {
        scalar = 0;
        if (_entityLength == 3 && MatchesEntity("amp"u8)) { scalar = '&'; return true; }
        if (_entityLength == 2 && MatchesEntity("lt"u8)) { scalar = '<'; return true; }
        if (_entityLength == 2 && MatchesEntity("gt"u8)) { scalar = '>'; return true; }
        if (_entityLength == 4 && MatchesEntity("quot"u8)) { scalar = '"'; return true; }
        if (_entityLength == 4 && MatchesEntity("apos"u8)) { scalar = '\''; return true; }
        if (_entityLength == 0 || _entityName[0] != (uint)'#') return false;
        int offset = 1;
        int numberBase = 10;
        if (offset < _entityLength && (_entityName[offset] == (uint)'x' ||
                                       _entityName[offset] == (uint)'X'))
        {
            numberBase = 16;
            ++offset;
        }
        if (offset == _entityLength) return false;
        uint value = 0;
        for (; offset != _entityLength; ++offset)
        {
            int digit = Digit(_entityName[offset], numberBase);
            if (digit < 0 || value > (0x10FFFFU - (uint)digit) /
                (uint)numberBase) return false;
            value = value * (uint)numberBase + (uint)digit;
        }
        if (value == 0 || value > 0x10FFFFU ||
            (value >= 0xD800U && value <= 0xDFFFU)) return false;
        scalar = value;
        return true;
    }

    private bool IsNumericEntity() => _entityLength != 0 &&
                                      _entityName[0] == (uint)'#';

    private bool MatchesEntity(ReadOnlySpan<byte> name)
    {
        if (_entityLength != name.Length) return false;
        for (int index = 0; index != name.Length; ++index)
            if (_entityName[index] != name[index]) return false;
        return true;
    }

    private static int Digit(uint scalar, int numberBase)
    {
        int digit;
        if (scalar >= (uint)'0' && scalar <= (uint)'9') digit = (int)(scalar - '0');
        else if (scalar >= (uint)'a' && scalar <= (uint)'f') digit = (int)(scalar - 'a') + 10;
        else if (scalar >= (uint)'A' && scalar <= (uint)'F') digit = (int)(scalar - 'A') + 10;
        else return -1;
        return digit < numberBase ? digit : -1;
    }

    private InputStep FinishStartTag()
    {
        CommitCurrentAttribute(false);
        PrepareStartTagToken();
        _mode = DetermineMode();
        _state = _selfClosing ? ManagedHtmlTokenizerState.Data : StateForMode();
        return InputStep.Consumed;
    }

    private InputStep FinishEndTag()
    {
        PrepareEndTagToken();
        _state = ManagedHtmlTokenizerState.Data;
        return InputStep.Consumed;
    }

    private InputStep FinishDoctype()
    {
        if (_doctypeNameLength == 0)
        {
            Fail(ManagedHtmlTokenizerFailureReason.InvalidMarkup);
            return InputStep.Failed;
        }
        PrepareDoctypeToken();
        _state = ManagedHtmlTokenizerState.Data;
        return InputStep.Consumed;
    }

    private void PrepareTextToken()
    {
        if (_textLength == 0) return;
        _pendingKind = ManagedHtmlTokenKind.Text;
        _pendingToken = new ManagedHtmlToken(
            ManagedHtmlTokenKind.Text, null, 0, _text, _textLength,
            null, 0, false, null, 0, false, false, null, 0);
        _tokenPending = true;
    }

    private void PrepareStartTagToken()
    {
        _pendingKind = ManagedHtmlTokenKind.StartTag;
        _pendingToken = new ManagedHtmlToken(
            ManagedHtmlTokenKind.StartTag, _tagName, _tagNameLength, null, 0,
            _attributeSlots, _attributeCount, _selfClosing, null, 0,
            false, false, null, 0);
        _tokenPending = true;
    }

    private void PrepareEndTagToken()
    {
        _pendingKind = ManagedHtmlTokenKind.EndTag;
        _pendingToken = new ManagedHtmlToken(
            ManagedHtmlTokenKind.EndTag, _tagName, _tagNameLength, null, 0,
            null, 0, false, null, 0, false, false, null, 0);
        _tokenPending = true;
    }

    private void PrepareRawEndTagToken()
    {
        _rawCandidateLength = 0;
        _tagNameLength = _rawTagNameLength;
        _rawTagName.AsSpan(0, _rawTagNameLength).CopyTo(_tagName);
        PrepareEndTagToken();
        _state = ManagedHtmlTokenizerState.Data;
    }

    private void PrepareCommentToken(bool final)
    {
        _pendingKind = ManagedHtmlTokenKind.Comment;
        _pendingCommentFinal = final;
        _pendingToken = new ManagedHtmlToken(
            ManagedHtmlTokenKind.Comment, null, 0, null, 0, null, 0, false,
            _comment, _commentLength, true, final, null, 0);
        _tokenPending = true;
    }

    private void PrepareDoctypeToken()
    {
        _pendingKind = ManagedHtmlTokenKind.Doctype;
        _pendingToken = new ManagedHtmlToken(
            ManagedHtmlTokenKind.Doctype, null, 0, null, 0, null, 0, false,
            null, 0, false, false, _doctypeName, _doctypeNameLength);
        _tokenPending = true;
    }

    private void PrepareEofToken()
    {
        _pendingKind = ManagedHtmlTokenKind.EndOfFile;
        _pendingToken = new ManagedHtmlToken(
            ManagedHtmlTokenKind.EndOfFile, null, 0, null, 0, null, 0, false,
            null, 0, false, false, null, 0);
        _tokenPending = true;
    }

    private void PrepareRawEndTagAfterText()
    {
        if (!_tokenPending) PrepareRawEndTagToken();
    }

    private void CommitPendingToken()
    {
        _tokenPending = false;
        ++_tokensEmitted;
        switch (_pendingKind)
        {
            case ManagedHtmlTokenKind.Text:
                ++_textTokens;
                _text.AsSpan().Clear();
                _textLength = 0;
                if (_state == ManagedHtmlTokenizerState.RawTextCloseAfterText)
                    PrepareRawEndTagAfterText();
                break;
            case ManagedHtmlTokenKind.StartTag:
                ++_startTagTokens;
                _attributesEmitted += _attributeCount;
                ClearCurrentTag();
                break;
            case ManagedHtmlTokenKind.EndTag:
                ++_endTagTokens;
                ClearCurrentTag();
                break;
            case ManagedHtmlTokenKind.Comment:
                ++_commentTokens;
                _comment.AsSpan().Clear();
                _commentLength = 0;
                if (_pendingCommentFinal)
                    _commentPendingDashes = 0;
                break;
            case ManagedHtmlTokenKind.Doctype:
                ++_doctypeTokens;
                _doctypeName.AsSpan().Clear();
                _doctypeNameLength = 0;
                break;
            case ManagedHtmlTokenKind.EndOfFile:
                _state = ManagedHtmlTokenizerState.Completed;
                break;
        }
    }

    private InputStep FinishAtEof()
    {
        if (_tokenPending) return InputStep.Blocked;
        if (_state == ManagedHtmlTokenizerState.CharacterReference)
        {
            ResolveEntity(false);
            if (_state == ManagedHtmlTokenizerState.Failed)
                return InputStep.Failed;
            return InputStep.Blocked;
        }
        if (_state == ManagedHtmlTokenizerState.Comment ||
            _state == ManagedHtmlTokenizerState.CommentSecondDash)
        {
            Fail(ManagedHtmlTokenizerFailureReason.TruncatedComment);
            return InputStep.Failed;
        }
        if (_state == ManagedHtmlTokenizerState.RawTextCandidate ||
            _state == ManagedHtmlTokenizerState.RawTextCandidateFlush)
        {
            _rawFlushOffset = _state == ManagedHtmlTokenizerState.RawTextCandidate
                ? 0 : _rawFlushOffset;
            _state = ManagedHtmlTokenizerState.RawTextCandidateFlush;
            return FlushRawCandidate();
        }
        if (_state == ManagedHtmlTokenizerState.RawTextCloseAfterText)
        {
            PrepareRawEndTagAfterText();
            return InputStep.Blocked;
        }
        if (_state != ManagedHtmlTokenizerState.Data &&
            _state != ManagedHtmlTokenizerState.RawText &&
            _state != ManagedHtmlTokenizerState.RcData &&
            _state != ManagedHtmlTokenizerState.ScriptData &&
            _state != ManagedHtmlTokenizerState.Idle)
        {
            Fail(ManagedHtmlTokenizerFailureReason.TruncatedMarkup);
            return InputStep.Failed;
        }
        if (_textLength != 0)
        {
            PrepareTextToken();
            return InputStep.Blocked;
        }
        PrepareEofToken();
        return InputStep.Blocked;
    }

    private void CommitCurrentAttribute(bool hasValue)
    {
        if (!_currentAttributeActive) return;
        _currentAttributeHasValue |= hasValue;
        bool duplicate = false;
        for (int index = 0; index != _attributeCount; ++index)
        {
            if (_attributeNames[index].AsSpan(0, _attributeSlots[index].NameLength)
                .SequenceEqual(_currentAttributeName.AsSpan(0, _currentAttributeNameLength)))
            {
                duplicate = true;
                break;
            }
        }
        if (!duplicate)
        {
            int slot = _attributeCount++;
            _currentAttributeName.AsSpan(0, _currentAttributeNameLength)
                .CopyTo(_attributeNames[slot]);
            _currentAttributeValue.AsSpan(0, _currentAttributeValueLength)
                .CopyTo(_attributeValues[slot]);
            _attributeSlots[slot] = new ManagedHtmlAttributeSlot(
                _attributeNames[slot], _currentAttributeNameLength,
                _attributeValues[slot], _currentAttributeValueLength,
                _currentAttributeHasValue);
        }
        _currentAttributeActive = false;
        _currentAttributeNameLength = 0;
        _currentAttributeValueLength = 0;
        _currentAttributeHasValue = false;
    }

    private bool TryBeginAttribute(uint scalar)
    {
        if (_attributeCount == ManagedHtmlTokenizerLimits.MaximumAttributesPerTag)
        {
            Fail(ManagedHtmlTokenizerFailureReason.TooManyAttributes);
            return false;
        }
        _currentAttributeActive = true;
        _currentAttributeNameLength = 0;
        _currentAttributeValueLength = 0;
        _currentAttributeHasValue = false;
        _currentAttributeName[_currentAttributeNameLength++] = ToLowerByte(scalar);
        return true;
    }

    private void ResetTagName()
    {
        _tagNameLength = 0;
        _pendingKind = ManagedHtmlTokenKind.StartTag;
        _selfClosing = false;
        _attributeCount = 0;
        _currentAttributeActive = false;
        _currentAttributeNameLength = 0;
        _currentAttributeValueLength = 0;
        _currentAttributeHasValue = false;
    }

    private void AppendTagNameUnchecked(uint scalar) =>
        _tagName[_tagNameLength++] = ToLowerByte(scalar);

    private void ClearCurrentTag()
    {
        _tagName.AsSpan().Clear();
        _tagNameLength = 0;
        for (int index = 0; index != _attributeSlots.Length; ++index)
            _attributeSlots[index] = default;
        _attributeCount = 0;
        _currentAttributeActive = false;
    }

    private HtmlMode DetermineMode()
    {
        if (_tagNameLength == 5 && EqualsAscii(_tagName, _tagNameLength, "style"u8))
        {
            CopyRawName();
            return HtmlMode.RawText;
        }
        if (_tagNameLength == 6 && EqualsAscii(_tagName, _tagNameLength, "script"u8))
        {
            CopyRawName();
            return HtmlMode.ScriptData;
        }
        if ((_tagNameLength == 7 && EqualsAscii(_tagName, _tagNameLength, "textarea"u8)) ||
            (_tagNameLength == 5 && EqualsAscii(_tagName, _tagNameLength, "title"u8)))
        {
            CopyRawName();
            return HtmlMode.RcData;
        }
        _rawTagNameLength = 0;
        return HtmlMode.Data;
    }

    private void CopyRawName()
    {
        _rawTagNameLength = _tagNameLength;
        _tagName.AsSpan(0, _tagNameLength).CopyTo(_rawTagName);
    }

    private ManagedHtmlTokenizerState StateForMode() => _mode switch
    {
        HtmlMode.RawText => ManagedHtmlTokenizerState.RawText,
        HtmlMode.RcData => ManagedHtmlTokenizerState.RcData,
        HtmlMode.ScriptData => ManagedHtmlTokenizerState.ScriptData,
        _ => ManagedHtmlTokenizerState.Data
    };

    private void Fail(ManagedHtmlTokenizerFailureReason reason)
    {
        _failureReason = reason;
        _state = ManagedHtmlTokenizerState.Failed;
        _paused = false;
    }

    private void ClearStorage()
    {
        _input.AsSpan().Clear();
        _text.AsSpan().Clear();
        _tagName.AsSpan().Clear();
        _rawTagName.AsSpan().Clear();
        _currentAttributeName.AsSpan().Clear();
        _currentAttributeValue.AsSpan().Clear();
        _comment.AsSpan().Clear();
        _doctypeName.AsSpan().Clear();
        _entityName.AsSpan().Clear();
        _entityOutput.AsSpan().Clear();
        _rawCandidate.AsSpan().Clear();
        for (int index = 0; index != _attributeNames.Length; ++index)
        {
            _attributeNames[index].AsSpan().Clear();
            _attributeValues[index].AsSpan().Clear();
            _attributeSlots[index] = default;
        }
        _inputOffset = 0;
        _inputLength = 0;
        _textLength = 0;
        _tagNameLength = 0;
        _rawTagNameLength = 0;
        _currentAttributeNameLength = 0;
        _currentAttributeValueLength = 0;
        _attributeCount = 0;
        _commentLength = 0;
        _commentPendingDashes = 0;
        _doctypeNameLength = 0;
        _doctypeKeywordIndex = 0;
        _entityLength = 0;
        _entityOutputLength = 0;
        _entityOutputOffset = 0;
        _rawCandidateLength = 0;
        _rawFlushOffset = 0;
        _pausedState = ManagedHtmlTokenizerState.Data;
        _tokenPending = false;
        _pendingToken = default;
    }

    private static bool IsAsciiLetter(uint scalar) =>
        (scalar >= (uint)'A' && scalar <= (uint)'Z') ||
        (scalar >= (uint)'a' && scalar <= (uint)'z');

    private static bool IsAsciiNameCharacter(uint scalar) =>
        IsAsciiLetter(scalar) || (scalar >= (uint)'0' && scalar <= (uint)'9') ||
        scalar == (uint)'-' || scalar == (uint)'_' || scalar == (uint)':';

    private static bool IsTagNameCharacter(uint scalar) => IsAsciiNameCharacter(scalar);

    private static bool IsAttributeNameCharacter(uint scalar) =>
        scalar >= 0x21 && scalar <= 0x7E && scalar != (uint)'=' &&
        scalar != (uint)'>' && scalar != (uint)'/' && scalar != (uint)'"' &&
        scalar != (uint)'\'';

    private static bool IsHtmlWhitespace(uint scalar) => scalar == (uint)' ' ||
        scalar == 9 || scalar == 10 || scalar == 12 || scalar == 13;

    private static bool IsEntityCharacter(uint scalar) => IsAsciiLetter(scalar) ||
        (scalar >= (uint)'0' && scalar <= (uint)'9') || scalar == (uint)'#' ||
        scalar == (uint)'x' || scalar == (uint)'X';

    private static uint ToLowerAscii(uint scalar) => scalar >= (uint)'A' &&
        scalar <= (uint)'Z' ? scalar + 32 : scalar;

    private static byte ToLowerByte(uint scalar) => (byte)ToLowerAscii(scalar);

    private static bool EqualsAscii(byte[] value, int length, ReadOnlySpan<byte> expected)
    {
        if (length != expected.Length) return false;
        return value.AsSpan(0, length).SequenceEqual(expected);
    }
}

/* A small bounded consumer useful to callers and tests that need to count
   tokens without retaining the stream. */
public sealed class ManagedHtmlTokenCountConsumer : IManagedHtmlTokenConsumer
{
    private ManagedHtmlTokenConsumerState _state;
    private ManagedHtmlTokenConsumerFailureReason _failureReason;
    private int _tokens;
    private int _textScalars;
    private int _attributes;

    public ManagedHtmlTokenConsumerState State => _state;
    public ManagedHtmlTokenConsumerFailureReason FailureReason => _failureReason;
    public int TokensProcessed => _tokens;
    public int TextScalars => _textScalars;
    public int Attributes => _attributes;

    public ManagedHttpBodySinkResult Consume(in ManagedHtmlToken token)
    {
        if (_state == ManagedHtmlTokenConsumerState.Cancelled ||
            _state == ManagedHtmlTokenConsumerState.Failed ||
            _state == ManagedHtmlTokenConsumerState.Completed)
            return ManagedHttpBodySinkResult.Fail;
        _tokens++;
        _textScalars += token.TextLength;
        _attributes += token.AttributeCount;
        _state = ManagedHtmlTokenConsumerState.Receiving;
        return ManagedHttpBodySinkResult.Continue;
    }

    public bool Complete()
    {
        if (_state == ManagedHtmlTokenConsumerState.Cancelled ||
            _state == ManagedHtmlTokenConsumerState.Failed) return false;
        _state = ManagedHtmlTokenConsumerState.Completed;
        return true;
    }

    public void Cancel() => _state = ManagedHtmlTokenConsumerState.Cancelled;

    public void Reset()
    {
        _state = ManagedHtmlTokenConsumerState.Idle;
        _failureReason = ManagedHtmlTokenConsumerFailureReason.None;
        _tokens = 0;
        _textScalars = 0;
        _attributes = 0;
    }
}

/* Canonical token telemetry for bounded guest proofs.  The hash input is a
   binary, length-delimited representation of the token stream; it never
   retains the document or allocates per-token storage. */
public sealed class ManagedHtmlTokenHashConsumer : IManagedHtmlTokenConsumer
{
    private const int MaxNameLength = ManagedHtmlTokenizerLimits.MaximumTagNameLength;
    private const int MaxValueLength = ManagedHtmlTokenizerLimits.MaximumAttributeValueLength;
    private const int MaxCommentLength = ManagedHtmlTokenizerLimits.MaximumCommentFragmentLength;
    private readonly ManagedSha256 _hash = new();
    private readonly byte[] _digest = new byte[ManagedSha256.DigestSize];
    private ManagedHtmlTokenConsumerState _state;
    private ManagedHtmlTokenConsumerFailureReason _failureReason;
    private bool _finalized;
    private int _tokens;
    private int _textTokens;
    private int _startTags;
    private int _endTags;
    private int _comments;
    private int _doctypes;
    private int _attributes;
    private int _textScalars;

    public ManagedHtmlTokenConsumerState State => _state;
    public ManagedHtmlTokenConsumerFailureReason FailureReason => _failureReason;
    public int TokensProcessed => _tokens;
    public int TextTokenCount => _textTokens;
    public int StartTagCount => _startTags;
    public int EndTagCount => _endTags;
    public int CommentCount => _comments;
    public int DoctypeCount => _doctypes;
    public int Attributes => _attributes;
    public int TextScalars => _textScalars;

    public ManagedHttpBodySinkResult Consume(in ManagedHtmlToken token)
    {
        if (_state == ManagedHtmlTokenConsumerState.Cancelled ||
            _state == ManagedHtmlTokenConsumerState.Failed ||
            _state == ManagedHtmlTokenConsumerState.Completed ||
            _finalized || !AppendToken(token))
        {
            _failureReason = _state == ManagedHtmlTokenConsumerState.Completed
                ? ManagedHtmlTokenConsumerFailureReason.FinalizationFailure
                : ManagedHtmlTokenConsumerFailureReason.ConsumerFailure;
            _state = ManagedHtmlTokenConsumerState.Failed;
            return ManagedHttpBodySinkResult.Fail;
        }
        ++_tokens;
        _state = ManagedHtmlTokenConsumerState.Receiving;
        return ManagedHttpBodySinkResult.Continue;
    }

    public bool Complete()
    {
        if (_state == ManagedHtmlTokenConsumerState.Cancelled ||
            _state == ManagedHtmlTokenConsumerState.Failed || _finalized)
            return false;
        if (!_hash.TryFinalize(_digest))
        {
            _failureReason = ManagedHtmlTokenConsumerFailureReason.FinalizationFailure;
            _state = ManagedHtmlTokenConsumerState.Failed;
            return false;
        }
        _finalized = true;
        _state = ManagedHtmlTokenConsumerState.Completed;
        return true;
    }

    public bool TryCopyDigest(Span<byte> destination)
    {
        if (!_finalized || destination.Length < _digest.Length) return false;
        _digest.AsSpan().CopyTo(destination);
        return true;
    }

    public void Cancel() => _state = ManagedHtmlTokenConsumerState.Cancelled;

    public void Reset()
    {
        _hash.Reset();
        _digest.AsSpan().Clear();
        _state = ManagedHtmlTokenConsumerState.Idle;
        _failureReason = ManagedHtmlTokenConsumerFailureReason.None;
        _finalized = false;
        _tokens = 0;
        _textTokens = 0;
        _startTags = 0;
        _endTags = 0;
        _comments = 0;
        _doctypes = 0;
        _attributes = 0;
        _textScalars = 0;
    }

    private bool AppendToken(in ManagedHtmlToken token)
    {
        if (!AppendByte((byte)token.Kind)) return false;
        switch (token.Kind)
        {
            case ManagedHtmlTokenKind.Text:
                ++_textTokens;
                _textScalars += token.TextLength;
                return AppendScalars(token, token.TextLength, TokenPart.Text);
            case ManagedHtmlTokenKind.StartTag:
                ++_startTags;
                _attributes += token.AttributeCount;
                return AppendTag(token, true);
            case ManagedHtmlTokenKind.EndTag:
                ++_endTags;
                return AppendTag(token, false);
            case ManagedHtmlTokenKind.Comment:
                ++_comments;
                return AppendByte(token.IsCommentFragment ? (byte)1 : (byte)0) &&
                    AppendByte(token.IsCommentFinalFragment ? (byte)1 : (byte)0) &&
                    AppendLength(token.CommentLength) &&
                    AppendScalars(token, token.CommentLength, TokenPart.Comment);
            case ManagedHtmlTokenKind.Doctype:
                ++_doctypes;
                return AppendLength(token.DoctypeNameLength) &&
                    AppendName(token, TokenPart.Doctype);
            case ManagedHtmlTokenKind.EndOfFile:
                return true;
            default:
                return false;
        }
    }

    private bool AppendTag(in ManagedHtmlToken token, bool start)
    {
        if (!AppendLength(token.TagNameLength) || !AppendName(token, TokenPart.TagName))
            return false;
        if (!start) return true;
        if (!AppendByte(token.IsSelfClosing ? (byte)1 : (byte)0) ||
            !AppendLength(token.AttributeCount)) return false;
        Span<byte> name = stackalloc byte[ManagedHtmlTokenizerLimits.MaximumAttributeNameLength];
        Span<uint> value = stackalloc uint[MaxValueLength];
        for (int index = 0; index != token.AttributeCount; ++index)
        {
            if (!token.TryCopyAttributeName(index, name, out int nameLength) ||
                !AppendLength(nameLength) || !_hash.Append(name[..nameLength])) return false;
            if (!token.TryCopyAttributeValue(index, value, out int valueLength,
                                             out bool hasValue) ||
                !AppendByte(hasValue ? (byte)1 : (byte)0) ||
                !AppendLength(valueLength) || !AppendScalars(value, valueLength)) return false;
        }
        return true;
    }

    private enum TokenPart : byte { Text, TagName, Comment, Doctype }

    private bool AppendName(in ManagedHtmlToken token, TokenPart part)
    {
        Span<byte> name = stackalloc byte[MaxNameLength];
        int length;
        bool copied = part == TokenPart.Doctype
            ? token.TryCopyDoctypeName(name, out length)
            : token.TryCopyTagName(name, out length);
        return copied && _hash.Append(name[..length]);
    }

    private bool AppendScalars(in ManagedHtmlToken token, int length, TokenPart part)
    {
        Span<uint> scalars = stackalloc uint[MaxCommentLength];
        bool copied = part == TokenPart.Text
            ? token.TryCopyText(scalars, out int copiedLength)
            : token.TryCopyComment(scalars, out copiedLength);
        return copied && copiedLength == length && AppendScalars(scalars, length);
    }

    private bool AppendScalars(ReadOnlySpan<uint> scalars, int length)
    {
        for (int index = 0; index != length; ++index)
            if (!AppendUInt32(scalars[index])) return false;
        return true;
    }

    private bool AppendLength(int length) => AppendUInt32((uint)length);

    private bool AppendUInt32(uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        bytes[0] = (byte)(value >> 24);
        bytes[1] = (byte)(value >> 16);
        bytes[2] = (byte)(value >> 8);
        bytes[3] = (byte)value;
        return _hash.Append(bytes);
    }

    private bool AppendByte(byte value)
    {
        Span<byte> bytes = stackalloc byte[1];
        bytes[0] = value;
        return _hash.Append(bytes);
    }
}
