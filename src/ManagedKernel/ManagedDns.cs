using System;

namespace GuideXOS.Net10.ManagedKernel;

internal enum ManagedDnsResult : byte
{
    None = 0,
    Resolved = 1,
    NxDomain = 2,
    Malformed = 3,
    TransactionMismatch = 4,
    NotResponse = 5,
    UnsupportedOpcode = 6,
    Truncated = 7,
    UnsupportedRcode = 8,
    NoAddress = 9,
    PortMismatch = 10,
    OutstandingQuery = 11,
    RetryExhausted = 12
}

internal static class ManagedDnsProtocol
{
    internal const ushort TypeA = 1;
    internal const ushort ClassIn = 1;
    internal const ushort ServerPort = 53;
    internal const int HeaderLength = 12;
    internal const int MaximumNameCharacters = 253;
    internal const int MaximumEncodedNameLength = 255;
    internal const int MaximumDecodedNameLength = 255;
    internal const int MaximumCompressionHops = 16;
    internal const int MaximumAnswerRecords = 8;
    internal const int MaximumMessageLength = ManagedUdpProtocol.MaximumPayloadLength;
    internal const ushort QueryFlags = 0x0100;

    internal static bool TryEncodeName(ReadOnlySpan<byte> asciiName,
                                       Span<byte> encoded, out ushort length)
    {
        length = 0;
        if (asciiName.Length == 0 ||
            asciiName.Length > MaximumNameCharacters ||
            encoded.Length < MaximumEncodedNameLength)
            return false;

        int labelStart = 0;
        int output = 0;
        for (int index = 0; index <= asciiName.Length; ++index)
        {
            if (index != asciiName.Length && asciiName[index] != (byte)'.')
                continue;

            int labelLength = index - labelStart;
            if (labelLength == 0 || labelLength > 63 ||
                output > encoded.Length - labelLength - 2)
                return false;
            encoded[output++] = (byte)labelLength;
            for (int labelIndex = 0; labelIndex != labelLength; ++labelIndex)
            {
                byte value = asciiName[labelStart + labelIndex];
                if (value < 0x21 || value > 0x7E || value == (byte)'.')
                    return false;
                encoded[output++] = value;
            }

            if (index == asciiName.Length)
            {
                encoded[output++] = 0;
                break;
            }
            labelStart = index + 1;
        }

        if (output > MaximumEncodedNameLength) return false;
        length = (ushort)output;
        return true;
    }

    internal static bool TryBuildQuery(Span<byte> message, ushort transactionId,
                                       ReadOnlySpan<byte> asciiName,
                                       out ushort length)
    {
        length = 0;
        Span<byte> encoded = stackalloc byte[MaximumEncodedNameLength];
        if (transactionId == 0 || message.Length < HeaderLength + 5 ||
            message.Length > MaximumMessageLength ||
            !TryEncodeName(asciiName, encoded, out ushort nameLength))
            return false;
        return TryBuildQueryFromEncodedName(message, transactionId,
                                            encoded.Slice(0, nameLength),
                                            out length);
    }

    internal static bool TryBuildQueryFromEncodedName(
        Span<byte> message, ushort transactionId, ReadOnlySpan<byte> encodedName,
        out ushort length)
    {
        length = 0;
        if (transactionId == 0 || message.Length < HeaderLength + 5 ||
            message.Length > MaximumMessageLength ||
            encodedName.Length == 0 || encodedName.Length > MaximumEncodedNameLength ||
            encodedName[encodedName.Length - 1] != 0 ||
            encodedName.Length + HeaderLength + 4 > message.Length)
            return false;

        message.Clear();
        ManagedEthernetProtocol.WriteUInt16Network(message, 0, transactionId);
        ManagedEthernetProtocol.WriteUInt16Network(message, 2, QueryFlags);
        ManagedEthernetProtocol.WriteUInt16Network(message, 4, 1);
        encodedName.CopyTo(message.Slice(HeaderLength));
        int offset = HeaderLength + encodedName.Length;
        ManagedEthernetProtocol.WriteUInt16Network(message, offset, TypeA);
        ManagedEthernetProtocol.WriteUInt16Network(message, offset + 2, ClassIn);
        length = (ushort)(offset + 4);
        return true;
    }

    internal static ManagedDnsResult TryParseResponse(
        ReadOnlySpan<byte> message, ushort expectedTransactionId,
        ReadOnlySpan<byte> expectedEncodedName, out uint address, out uint ttl)
    {
        address = 0;
        ttl = 0;
        if (message.Length < HeaderLength ||
            expectedTransactionId == 0 ||
            expectedEncodedName.Length == 0 ||
            expectedEncodedName.Length > MaximumEncodedNameLength ||
            expectedEncodedName[expectedEncodedName.Length - 1] != 0)
            return ManagedDnsResult.Malformed;

        ushort transactionId = ManagedEthernetProtocol.ReadUInt16Network(message, 0);
        if (transactionId != expectedTransactionId)
            return ManagedDnsResult.TransactionMismatch;
        ushort flags = ManagedEthernetProtocol.ReadUInt16Network(message, 2);
        if ((flags & 0x8000) == 0) return ManagedDnsResult.NotResponse;
        if (((flags >> 11) & 0x0F) != 0) return ManagedDnsResult.UnsupportedOpcode;
        if ((flags & 0x0070) != 0) return ManagedDnsResult.Malformed;
        if ((flags & 0x0200) != 0) return ManagedDnsResult.Truncated;

        ushort questionCount = ManagedEthernetProtocol.ReadUInt16Network(message, 4);
        ushort answerCount = ManagedEthernetProtocol.ReadUInt16Network(message, 6);
        ushort authorityCount = ManagedEthernetProtocol.ReadUInt16Network(message, 8);
        ushort additionalCount = ManagedEthernetProtocol.ReadUInt16Network(message, 10);
        if (questionCount != 1 || answerCount > MaximumAnswerRecords ||
            authorityCount != 0 || additionalCount != 0)
            return ManagedDnsResult.Malformed;

        Span<byte> decodedName = stackalloc byte[MaximumDecodedNameLength];
        if (!TryDecodeName(message, HeaderLength, decodedName,
                           out int consumed, out int decodedLength) ||
            !NamesEqual(decodedName.Slice(0, decodedLength), expectedEncodedName))
            return ManagedDnsResult.Malformed;
        int offset = HeaderLength + consumed;
        if (offset > message.Length - 4)
            return ManagedDnsResult.Malformed;
        ushort questionType = ManagedEthernetProtocol.ReadUInt16Network(message, offset);
        ushort questionClass = ManagedEthernetProtocol.ReadUInt16Network(message, offset + 2);
        if (questionType != TypeA || questionClass != ClassIn)
            return ManagedDnsResult.Malformed;
        offset += 4;

        byte rcode = (byte)(flags & 0x0F);
        if (rcode == 3)
        {
            return answerCount == 0 && offset == message.Length
                ? ManagedDnsResult.NxDomain
                : ManagedDnsResult.Malformed;
        }
        if (rcode != 0) return ManagedDnsResult.UnsupportedRcode;

        bool found = false;
        for (int record = 0; record != answerCount; ++record)
        {
            if (!TryDecodeName(message, offset, decodedName,
                               out consumed, out decodedLength))
                return ManagedDnsResult.Malformed;
            offset += consumed;
            if (offset > message.Length - 10)
                return ManagedDnsResult.Malformed;
            ushort type = ManagedEthernetProtocol.ReadUInt16Network(message, offset);
            ushort recordClass = ManagedEthernetProtocol.ReadUInt16Network(
                message, offset + 2);
            uint recordTtl = ManagedEthernetProtocol.ReadUInt32Network(
                message, offset + 4);
            ushort dataLength = ManagedEthernetProtocol.ReadUInt16Network(
                message, offset + 8);
            offset += 10;
            if (dataLength > message.Length - offset)
                return ManagedDnsResult.Malformed;

            if (type == TypeA && recordClass == ClassIn)
            {
                if (dataLength != 4) return ManagedDnsResult.Malformed;
                if (!found && NamesEqual(decodedName.Slice(0, decodedLength),
                                         expectedEncodedName))
                {
                    address = ManagedEthernetProtocol.ReadUInt32Network(message, offset);
                    ttl = recordTtl;
                    found = true;
                }
            }
            /* CNAME and every other RR type are structurally skipped.  Phase
               20 deliberately does not follow aliases or retain RR lists. */
            offset += dataLength;
        }

        if (offset != message.Length) return ManagedDnsResult.Malformed;
        return found ? ManagedDnsResult.Resolved : ManagedDnsResult.NoAddress;
    }

    internal static bool TryDecodeName(ReadOnlySpan<byte> message, int start,
                                       Span<byte> decoded, out int consumed,
                                       out int decodedLength)
    {
        consumed = 0;
        decodedLength = 0;
        if (start < 0 || start >= message.Length ||
            decoded.Length < MaximumDecodedNameLength)
            return false;

        Span<int> visited = stackalloc int[MaximumCompressionHops];
        int cursor = start;
        int output = 0;
        int hops = 0;
        bool jumped = false;
        while (true)
        {
            if (cursor < 0 || cursor >= message.Length) return false;
            byte length = message[cursor];
            if (length == 0)
            {
                if (output >= decoded.Length) return false;
                decoded[output++] = 0;
                if (!jumped) consumed = cursor - start + 1;
                decodedLength = output;
                return consumed != 0;
            }
            if ((length & 0xC0) == 0xC0)
            {
                if (cursor >= message.Length - 1) return false;
                int target = ((length & 0x3F) << 8) | message[cursor + 1];
                if (target >= message.Length || target == cursor || hops >= visited.Length)
                    return false;
                for (int index = 0; index != hops; ++index)
                    if (visited[index] == target) return false;
                visited[hops++] = target;
                if (!jumped) consumed = cursor - start + 2;
                jumped = true;
                cursor = target;
                continue;
            }
            if ((length & 0xC0) != 0 || length > 63 ||
                cursor >= message.Length - 1 ||
                length > message.Length - cursor - 1 ||
                output > decoded.Length - length - 2)
                return false;
            decoded[output++] = length;
            message.Slice(cursor + 1, length).CopyTo(decoded.Slice(output));
            output += length;
            cursor += length + 1;
        }
    }

    internal static bool NamesEqual(ReadOnlySpan<byte> left,
                                    ReadOnlySpan<byte> right)
    {
        if (left.Length != right.Length) return false;
        for (int index = 0; index != left.Length; ++index)
        {
            byte leftValue = left[index];
            byte rightValue = right[index];
            if (leftValue >= (byte)'A' && leftValue <= (byte)'Z')
                leftValue = (byte)(leftValue + ((byte)'a' - (byte)'A'));
            if (rightValue >= (byte)'A' && rightValue <= (byte)'Z')
                rightValue = (byte)(rightValue + ((byte)'a' - (byte)'A'));
            if (leftValue != rightValue) return false;
        }
        return true;
    }
}

internal sealed class ManagedDnsResolver
{
    internal const ushort ClientPort = 15200;
    internal const ushort ServerPort = ManagedDnsProtocol.ServerPort;
    internal const int MaximumAttempts = 3;
    private static ushort s_nextTransactionId = 0x2001;

    private readonly byte[] _serverIpv4 = new byte[4];
    private readonly byte[] _queryName = new byte[ManagedDnsProtocol.MaximumEncodedNameLength];
    private readonly byte[] _resolvedIpv4 = new byte[4];
    private ushort _queryNameLength;
    private ushort _transactionId;
    private int _attempts;
    private bool _hasServer;
    private bool _active;

    internal ManagedDnsResult Result { get; private set; } = ManagedDnsResult.None;
    internal bool HasServer => _hasServer;
    internal bool IsActive => _active;
    internal bool HasResolvedAddress => Result == ManagedDnsResult.Resolved;
    internal ushort TransactionId => _transactionId;
    internal int Attempts => _attempts;
    internal uint Ttl { get; private set; }
    internal ReadOnlySpan<byte> ServerIpv4 => _serverIpv4;
    internal ReadOnlySpan<byte> ResolvedIpv4 => _resolvedIpv4;
    internal ReadOnlySpan<byte> QueryName => _queryName.AsSpan(0, _queryNameLength);

    internal bool TryInstallServer(ReadOnlySpan<byte> serverIpv4)
    {
        if (!IsUsableIpv4(serverIpv4)) return false;
        serverIpv4.CopyTo(_serverIpv4);
        _hasServer = true;
        return true;
    }

    internal bool TryStartQuery(ReadOnlySpan<byte> asciiName)
    {
        if (!_hasServer || _active ||
            !ManagedDnsProtocol.TryEncodeName(asciiName, _queryName,
                                               out ushort encodedLength))
        {
            if (_active) Result = ManagedDnsResult.OutstandingQuery;
            return false;
        }
        _queryNameLength = encodedLength;
        _transactionId = NextTransactionId();
        _attempts = 1;
        _active = true;
        Result = ManagedDnsResult.None;
        _resolvedIpv4.AsSpan().Clear();
        Ttl = 0;
        return true;
    }

    internal bool TryBuildQuery(Span<byte> message, out ushort length)
    {
        length = 0;
        return _active && ManagedDnsProtocol.TryBuildQueryFromEncodedName(
            message, _transactionId, QueryName, out length);
    }

    internal ManagedDnsResult TryProcessResponse(ushort sourcePort,
                                                  ushort destinationPort,
                                                  ReadOnlySpan<byte> message)
    {
        if (!_active) return ManagedDnsResult.None;
        if (sourcePort != ServerPort || destinationPort != ClientPort)
        {
            Result = ManagedDnsResult.PortMismatch;
            return Result;
        }
        ManagedDnsResult result = ManagedDnsProtocol.TryParseResponse(
            message, _transactionId, QueryName, out uint address, out uint ttl);
        Result = result;
        if (result == ManagedDnsResult.Resolved)
        {
            ManagedEthernetProtocol.WriteUInt32Network(_resolvedIpv4, 0, address);
            Ttl = ttl;
            _active = false;
        }
        else if (result == ManagedDnsResult.NxDomain)
        {
            _resolvedIpv4.AsSpan().Clear();
            Ttl = 0;
            _active = false;
        }
        return result;
    }

    internal bool TryRetry()
    {
        if (!_active || _attempts >= MaximumAttempts)
        {
            if (_active) Result = ManagedDnsResult.RetryExhausted;
            return false;
        }
        _transactionId = NextTransactionId();
        _attempts++;
        Result = ManagedDnsResult.None;
        return true;
    }

    internal void ResetForDhcp()
    {
        ClearState(clearServer: true);
    }

    internal void ResetForTeardown()
    {
        ClearState(clearServer: true);
    }

    private static ushort NextTransactionId()
    {
        ushort next = s_nextTransactionId;
        if (next == 0) next = 1;
        s_nextTransactionId = (ushort)(next + 1);
        if (s_nextTransactionId == 0) s_nextTransactionId = 1;
        return next;
    }

    private void ClearState(bool clearServer)
    {
        _active = false;
        _queryNameLength = 0;
        _transactionId = 0;
        _attempts = 0;
        Result = ManagedDnsResult.None;
        Ttl = 0;
        _resolvedIpv4.AsSpan().Clear();
        if (clearServer)
        {
            _hasServer = false;
            _serverIpv4.AsSpan().Clear();
        }
        _queryName.AsSpan().Clear();
    }

    private static bool IsUsableIpv4(ReadOnlySpan<byte> address)
    {
        if (address.Length != 4) return false;
        uint value = ManagedEthernetProtocol.ReadUInt32Network(address, 0);
        return value != 0 && value != 0xFFFFFFFFU;
    }
}
