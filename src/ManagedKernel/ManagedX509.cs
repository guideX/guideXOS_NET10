using System;

namespace GuideXOS.Net10.ManagedKernel;

/*
 * Phase 30 deliberately implements a certificate-shaped subset of DER rather
 * than a general ASN.1 object model.  All offsets in ManagedX509Certificate
 * refer to the immutable certificate supplied to TryParseCertificate.  The
 * caller owns that buffer and must keep it alive while using the result.
 */
internal enum ManagedX509ValidationStatus : byte
{
    Success = 0,
    MalformedDer,
    UnsupportedAlgorithm,
    InvalidCertificate,
    InvalidPublicKey,
    InvalidTime,
    TimeUnavailable,
    NotYetValid,
    Expired,
    UnknownCriticalExtension,
    BadSignature,
    IssuerSubjectMismatch,
    InvalidCa,
    InvalidKeyUsage,
    InvalidExtendedKeyUsage,
    PathLengthExceeded,
    HostnameMismatch,
    UntrustedRoot
}

internal readonly struct ManagedX509UtcTime
{
    internal readonly int Year;
    internal readonly int Month;
    internal readonly int Day;
    internal readonly int Hour;
    internal readonly int Minute;
    internal readonly int Second;
    internal readonly bool IsValid;

    internal ManagedX509UtcTime(int year, int month, int day, int hour,
                                int minute, int second)
    {
        Year = year;
        Month = month;
        Day = day;
        Hour = hour;
        Minute = minute;
        Second = second;
        IsValid = IsCalendarDateValid(year, month, day) &&
                  hour >= 0 && hour <= 23 &&
                  minute >= 0 && minute <= 59 &&
                  second >= 0 && second <= 59;
    }

    internal static ManagedX509UtcTime Unavailable => default;

    internal static bool TryCreate(int year, int month, int day, int hour,
                                   int minute, int second,
                                   out ManagedX509UtcTime value)
    {
        value = new ManagedX509UtcTime(year, month, day, hour, minute,
                                        second);
        return value.IsValid;
    }

    internal static int Compare(in ManagedX509UtcTime left,
                                in ManagedX509UtcTime right)
    {
        if (left.Year != right.Year) return left.Year < right.Year ? -1 : 1;
        if (left.Month != right.Month) return left.Month < right.Month ? -1 : 1;
        if (left.Day != right.Day) return left.Day < right.Day ? -1 : 1;
        if (left.Hour != right.Hour) return left.Hour < right.Hour ? -1 : 1;
        if (left.Minute != right.Minute) return left.Minute < right.Minute ? -1 : 1;
        if (left.Second != right.Second) return left.Second < right.Second ? -1 : 1;
        return 0;
    }

    private static bool IsCalendarDateValid(int year, int month, int day)
    {
        if (year < 0 || year > 9999 || month < 1 || month > 12 || day < 1)
            return false;
        int days = month switch
        {
            2 => IsLeapYear(year) ? 29 : 28,
            4 or 6 or 9 or 11 => 30,
            _ => 31
        };
        return day <= days;
    }

    private static bool IsLeapYear(int year)
    {
        return (year % 4 == 0 && year % 100 != 0) || year % 400 == 0;
    }
}

internal readonly struct ManagedX509Certificate
{
    internal readonly int RawLength;
    internal readonly int TbsOffset;
    internal readonly int TbsLength;
    internal readonly int SerialOffset;
    internal readonly int SerialLength;
    internal readonly int IssuerOffset;
    internal readonly int IssuerLength;
    internal readonly int SubjectOffset;
    internal readonly int SubjectLength;
    internal readonly int PublicKeyOffset;
    internal readonly int PublicKeyLength;
    internal readonly int SignatureOffset;
    internal readonly int SignatureLength;
    internal readonly int SubjectAltNameOffset;
    internal readonly int SubjectAltNameLength;
    internal readonly int CommonNameOffset;
    internal readonly int CommonNameLength;
    internal readonly int DnsNameCount;
    internal readonly ManagedX509UtcTime NotBefore;
    internal readonly ManagedX509UtcTime NotAfter;
    internal readonly bool HasBasicConstraints;
    internal readonly bool IsCertificateAuthority;
    internal readonly bool HasPathLengthConstraint;
    internal readonly int PathLengthConstraint;
    internal readonly bool HasKeyUsage;
    internal readonly ushort KeyUsage;
    internal readonly bool HasExtendedKeyUsage;
    internal readonly bool HasServerAuth;
    internal readonly bool HasSubjectAltName;
    internal readonly bool HasUnknownCriticalExtension;

    internal ManagedX509Certificate(
        int rawLength, int tbsOffset, int tbsLength, int serialOffset,
        int serialLength, int issuerOffset, int issuerLength,
        int subjectOffset, int subjectLength, int publicKeyOffset,
        int publicKeyLength, int signatureOffset, int signatureLength,
        int subjectAltNameOffset, int subjectAltNameLength,
        int commonNameOffset, int commonNameLength, int dnsNameCount,
        ManagedX509UtcTime notBefore, ManagedX509UtcTime notAfter,
        bool hasBasicConstraints, bool isCertificateAuthority,
        bool hasPathLengthConstraint, int pathLengthConstraint,
        bool hasKeyUsage, ushort keyUsage, bool hasExtendedKeyUsage,
        bool hasServerAuth, bool hasSubjectAltName,
        bool hasUnknownCriticalExtension)
    {
        RawLength = rawLength;
        TbsOffset = tbsOffset;
        TbsLength = tbsLength;
        SerialOffset = serialOffset;
        SerialLength = serialLength;
        IssuerOffset = issuerOffset;
        IssuerLength = issuerLength;
        SubjectOffset = subjectOffset;
        SubjectLength = subjectLength;
        PublicKeyOffset = publicKeyOffset;
        PublicKeyLength = publicKeyLength;
        SignatureOffset = signatureOffset;
        SignatureLength = signatureLength;
        SubjectAltNameOffset = subjectAltNameOffset;
        SubjectAltNameLength = subjectAltNameLength;
        CommonNameOffset = commonNameOffset;
        CommonNameLength = commonNameLength;
        DnsNameCount = dnsNameCount;
        NotBefore = notBefore;
        NotAfter = notAfter;
        HasBasicConstraints = hasBasicConstraints;
        IsCertificateAuthority = isCertificateAuthority;
        HasPathLengthConstraint = hasPathLengthConstraint;
        PathLengthConstraint = pathLengthConstraint;
        HasKeyUsage = hasKeyUsage;
        KeyUsage = keyUsage;
        HasExtendedKeyUsage = hasExtendedKeyUsage;
        HasServerAuth = hasServerAuth;
        HasSubjectAltName = hasSubjectAltName;
        HasUnknownCriticalExtension = hasUnknownCriticalExtension;
    }

    internal bool HasDigitalSignature => (KeyUsage & (1 << 0)) != 0;
    internal bool HasKeyCertSign => (KeyUsage & (1 << 5)) != 0;
}

internal ref struct ManagedDerReader
{
    internal const int MaximumDepth = 12;
    internal const int MaximumElementsPerReader = 256;

    private readonly ReadOnlySpan<byte> _data;
    private readonly int _end;
    private readonly int _depth;
    private int _offset;
    private int _elementCount;

    internal ManagedDerReader(ReadOnlySpan<byte> data)
        : this(data, 0, data.Length, 0)
    {
    }

    private ManagedDerReader(ReadOnlySpan<byte> data, int offset, int end,
                             int depth)
    {
        _data = data;
        _offset = offset;
        _end = end;
        _depth = depth;
        _elementCount = 0;
    }

    internal bool AtEnd => _offset == _end;
    internal bool HasData => _offset != _end;

    internal bool TryPeekTag(out byte tag)
    {
        if (_offset >= _end)
        {
            tag = 0;
            return false;
        }
        tag = _data[_offset];
        return true;
    }

    internal bool TryRead(byte expectedTag, out int elementOffset,
                          out int elementLength, out int contentOffset,
                          out int contentLength)
    {
        return TryReadCore(expectedTag, false, out elementOffset,
                           out elementLength, out contentOffset,
                           out contentLength, out _);
    }

    internal bool TryReadAny(out byte tag, out int elementOffset,
                             out int elementLength, out int contentOffset,
                             out int contentLength)
    {
        return TryReadCore(0, true, out elementOffset, out elementLength,
                           out contentOffset, out contentLength, out tag);
    }

    internal bool TryEnter(byte expectedTag, out ManagedDerReader child,
                           out int elementOffset, out int elementLength)
    {
        child = default;
        if (!TryRead(expectedTag, out elementOffset, out elementLength,
                     out int contentOffset, out int contentLength) ||
            _depth >= MaximumDepth)
        {
            return false;
        }
        child = new ManagedDerReader(_data, contentOffset,
                                     contentOffset + contentLength,
                                     _depth + 1);
        return true;
    }

    private bool TryReadCore(byte expectedTag, bool anyTag,
                             out int elementOffset, out int elementLength,
                             out int contentOffset, out int contentLength,
                             out byte tag)
    {
        elementOffset = 0;
        elementLength = 0;
        contentOffset = 0;
        contentLength = 0;
        tag = 0;
        if (_elementCount >= MaximumElementsPerReader ||
            _offset >= _end)
        {
            return false;
        }
        int start = _offset;
        tag = _data[_offset++];
        if ((tag & 0x1F) == 0x1F || (!anyTag && tag != expectedTag) ||
            !TryReadLength(out int length) || length > _end - _offset)
        {
            return false;
        }
        contentOffset = _offset;
        contentLength = length;
        _offset += length;
        _elementCount++;
        elementOffset = start;
        elementLength = _offset - start;
        return true;
    }

    private bool TryReadLength(out int length)
    {
        length = 0;
        if (_offset >= _end) return false;
        byte first = _data[_offset++];
        if ((first & 0x80) == 0)
        {
            length = first;
            return true;
        }

        int octets = first & 0x7F;
        if (octets == 0 || octets > 2 || octets > _end - _offset)
            return false;
        if (_data[_offset] == 0) return false;
        int value = 0;
        for (int index = 0; index != octets; ++index)
        {
            value = (value << 8) | _data[_offset++];
        }
        if (value < 128) return false;
        length = value;
        return true;
    }
}

internal static class ManagedX509
{
    internal const int MaximumCertificateLength = 16 * 1024;
    internal const int MaximumChainLength = 4;
    internal const int MaximumRdnAttributes = 32;
    internal const int MaximumSanDnsNames = 32;
    internal const int MaximumDnsNameLength = 253;
    internal const int MaximumExtensionCount = 32;

    private const byte Sequence = 0x30;
    private const byte Set = 0x31;
    private const byte Integer = 0x02;
    private const byte ObjectIdentifier = 0x06;
    private const byte Boolean = 0x01;
    private const byte OctetString = 0x04;
    private const byte BitString = 0x03;
    private const byte Utf8String = 0x0C;
    private const byte PrintableString = 0x13;
    private const byte Ia5String = 0x16;
    private const byte UtcTime = 0x17;
    private const byte GeneralizedTime = 0x18;

    private static readonly byte[] EcdsaWithSha256Oid =
        { 0x2A, 0x86, 0x48, 0xCE, 0x3D, 0x04, 0x03, 0x02 };
    private static readonly byte[] EcPublicKeyOid =
        { 0x2A, 0x86, 0x48, 0xCE, 0x3D, 0x02, 0x01 };
    private static readonly byte[] Prime256v1Oid =
        { 0x2A, 0x86, 0x48, 0xCE, 0x3D, 0x03, 0x01, 0x07 };
    private static readonly byte[] CommonNameOid = { 0x55, 0x04, 0x03 };
    private static readonly byte[] SubjectAltNameOid = { 0x55, 0x1D, 0x11 };
    private static readonly byte[] BasicConstraintsOid = { 0x55, 0x1D, 0x13 };
    private static readonly byte[] KeyUsageOid = { 0x55, 0x1D, 0x0F };
    private static readonly byte[] ExtendedKeyUsageOid = { 0x55, 0x1D, 0x25 };
    private static readonly byte[] ServerAuthOid =
        { 0x2B, 0x06, 0x01, 0x05, 0x05, 0x07, 0x03, 0x01 };

    internal static bool TryParseCertificate(
        ReadOnlySpan<byte> der, out ManagedX509Certificate certificate)
    {
        return TryParseCertificate(der, out certificate, out _) ==
               ManagedX509ValidationStatus.Success;
    }

    internal static ManagedX509ValidationStatus TryParseCertificate(
        ReadOnlySpan<byte> der, out ManagedX509Certificate certificate,
        out ManagedX509ValidationStatus parseStatus)
    {
        certificate = default;
        parseStatus = ManagedX509ValidationStatus.MalformedDer;
        if (der.Length == 0 || der.Length > MaximumCertificateLength)
            return parseStatus;

        ManagedDerReader root = new(der);
        if (!root.TryEnter(Sequence, out ManagedDerReader certificateReader,
                           out int certificateOffset,
                           out int certificateLength) ||
            certificateOffset != 0 || certificateLength != der.Length)
            return parseStatus;

        if (!certificateReader.TryEnter(Sequence, out ManagedDerReader tbs,
                                        out int tbsOffset,
                                        out int tbsLength))
            return parseStatus;
        if (ParseTbs(der, ref tbs, out int serialOffset,
                     out int serialLength, out int issuerOffset,
                     out int issuerLength, out int subjectOffset,
                     out int subjectLength, out int publicKeyOffset,
                     out int publicKeyLength, out int sanOffset,
                     out int sanLength, out int commonNameOffset,
                     out int commonNameLength, out int dnsNameCount,
                     out ManagedX509UtcTime notBefore,
                     out ManagedX509UtcTime notAfter,
                     out bool hasBasicConstraints,
                     out bool isCertificateAuthority,
                     out bool hasPathLengthConstraint,
                     out int pathLengthConstraint, out bool hasKeyUsage,
                     out ushort keyUsage, out bool hasExtendedKeyUsage,
                     out bool hasServerAuth, out bool hasSubjectAltName,
                     out bool hasUnknownCriticalExtension,
                     out parseStatus) != ManagedX509ValidationStatus.Success)
            return parseStatus;

        ManagedX509ValidationStatus status = parseStatus;
        if (!certificateReader.TryEnter(Sequence,
                                        out ManagedDerReader outerAlgorithm,
                                        out _, out _))
        {
            parseStatus = ManagedX509ValidationStatus.MalformedDer;
            return parseStatus;
        }
        ManagedX509ValidationStatus algorithmStatus =
            ParseSignatureAlgorithm(der, ref outerAlgorithm);
        if (algorithmStatus != ManagedX509ValidationStatus.Success)
        {
            parseStatus = algorithmStatus;
            return parseStatus;
        }
        if (!outerAlgorithm.AtEnd ||
            !certificateReader.TryRead(BitString, out _, out _,
                                       out int signatureValueOffset,
                                       out int signatureValueLength) ||
            !TryGetBitStringPayload(der, signatureValueOffset,
                                    signatureValueLength, true,
                                    out int signatureOffset,
                                    out int signatureLength) ||
            !certificateReader.AtEnd || !root.AtEnd)
        {
            parseStatus = ManagedX509ValidationStatus.MalformedDer;
            return parseStatus;
        }

        certificate = new ManagedX509Certificate(
            der.Length, tbsOffset, tbsLength, serialOffset, serialLength,
            issuerOffset, issuerLength, subjectOffset, subjectLength,
            publicKeyOffset, publicKeyLength, signatureOffset,
            signatureLength, sanOffset, sanLength, commonNameOffset,
            commonNameLength, dnsNameCount, notBefore, notAfter,
            hasBasicConstraints, isCertificateAuthority,
            hasPathLengthConstraint, pathLengthConstraint, hasKeyUsage,
            keyUsage, hasExtendedKeyUsage, hasServerAuth,
            hasSubjectAltName, hasUnknownCriticalExtension);
        parseStatus = status;
        return status;
    }

    internal static bool TryValidateCertificateSignature(
        ReadOnlySpan<byte> certificate, in ManagedX509Certificate parsed,
        ReadOnlySpan<byte> issuerPublicKey,
        out ManagedX509ValidationStatus status)
    {
        status = ManagedX509ValidationStatus.MalformedDer;
        if (!HasRange(certificate, parsed.TbsOffset, parsed.TbsLength) ||
            !HasRange(certificate, parsed.SignatureOffset,
                      parsed.SignatureLength) ||
            parsed.RawLength != certificate.Length ||
            parsed.PublicKeyLength != ManagedP256.PublicKeySize ||
            issuerPublicKey.Length != ManagedP256.PublicKeySize)
            return false;
        if (parsed.HasUnknownCriticalExtension)
        {
            status = ManagedX509ValidationStatus.UnknownCriticalExtension;
            return false;
        }

        Span<byte> digest = stackalloc byte[ManagedP256.DigestSize];
        try
        {
            if (!ManagedSha256.TryHash(
                    certificate.Slice(parsed.TbsOffset, parsed.TbsLength),
                    digest))
            {
                status = ManagedX509ValidationStatus.MalformedDer;
                return false;
            }
            bool valid = ManagedP256.TryVerifyDerSignature(
                digest, issuerPublicKey,
                certificate.Slice(parsed.SignatureOffset,
                                  parsed.SignatureLength));
            status = valid ? ManagedX509ValidationStatus.Success :
                ManagedX509ValidationStatus.BadSignature;
            return valid;
        }
        finally
        {
            digest.Clear();
        }
    }

    internal static bool TryMatchHostname(
        ReadOnlySpan<byte> certificate, in ManagedX509Certificate parsed,
        ReadOnlySpan<byte> hostname)
    {
        if (!HasRange(certificate, parsed.SubjectAltNameOffset,
                      parsed.SubjectAltNameLength) ||
            !IsValidDnsName(hostname, false))
            return false;

        if (parsed.DnsNameCount != 0)
        {
            ManagedDerReader names = new(certificate.Slice(
                parsed.SubjectAltNameOffset, parsed.SubjectAltNameLength));
            if (!names.TryEnter(Sequence, out ManagedDerReader nameReader,
                                out _, out _))
                return false;
            while (!nameReader.AtEnd)
            {
                if (!nameReader.TryReadAny(out byte tag, out _, out _,
                                           out int valueOffset,
                                           out int valueLength))
                    return false;
                if (tag == 0x82 &&
                    TryMatchDnsName(certificate.Slice(
                        parsed.SubjectAltNameOffset,
                        parsed.SubjectAltNameLength), valueOffset,
                        valueLength, hostname))
                    return true;
            }
            return false;
        }

        if (parsed.CommonNameLength == 0 ||
            !HasRange(certificate, parsed.CommonNameOffset,
                      parsed.CommonNameLength))
            return false;
        return TryMatchDnsPattern(
            certificate.Slice(parsed.CommonNameOffset,
                              parsed.CommonNameLength), hostname);
    }

    internal static bool TryValidateServerChain(
        ReadOnlySpan<byte> leaf, ReadOnlySpan<byte> intermediate1,
        ReadOnlySpan<byte> intermediate2, ReadOnlySpan<byte> candidateRoot,
        ReadOnlySpan<byte> trustedRoot, in ManagedX509UtcTime currentTime,
        ReadOnlySpan<byte> hostname,
        out ManagedX509ValidationStatus status)
    {
        status = ManagedX509ValidationStatus.MalformedDer;
        bool hasIntermediate1 = !intermediate1.IsEmpty;
        bool hasIntermediate2 = !intermediate2.IsEmpty;
        if (!hasIntermediate1 && hasIntermediate2)
        {
            status = ManagedX509ValidationStatus.InvalidCertificate;
            return false;
        }
        if (!currentTime.IsValid)
        {
            status = ManagedX509ValidationStatus.TimeUnavailable;
            return false;
        }
        if (candidateRoot.IsEmpty || trustedRoot.IsEmpty ||
            candidateRoot.Length != trustedRoot.Length ||
            !ManagedCryptoComparison.FixedTimeEquals(candidateRoot,
                                                     trustedRoot))
        {
            status = ManagedX509ValidationStatus.UntrustedRoot;
            return false;
        }

        ManagedX509ValidationStatus parseStatus = TryParseCertificate(
            leaf, out ManagedX509Certificate leafCertificate, out status);
        if (parseStatus != ManagedX509ValidationStatus.Success) return false;
        parseStatus = TryParseCertificate(intermediate1,
            out ManagedX509Certificate firstIntermediate, out status);
        if (hasIntermediate1 &&
            parseStatus != ManagedX509ValidationStatus.Success) return false;
        if (!hasIntermediate1) firstIntermediate = default;
        parseStatus = TryParseCertificate(intermediate2,
            out ManagedX509Certificate secondIntermediate, out status);
        if (hasIntermediate2 &&
            parseStatus != ManagedX509ValidationStatus.Success) return false;
        if (!hasIntermediate2) secondIntermediate = default;
        parseStatus = TryParseCertificate(candidateRoot,
            out ManagedX509Certificate rootCertificate, out status);
        if (parseStatus != ManagedX509ValidationStatus.Success) return false;

        if (leafCertificate.HasUnknownCriticalExtension ||
            (hasIntermediate1 && firstIntermediate.HasUnknownCriticalExtension) ||
            (hasIntermediate2 && secondIntermediate.HasUnknownCriticalExtension) ||
            rootCertificate.HasUnknownCriticalExtension)
        {
            status = ManagedX509ValidationStatus.UnknownCriticalExtension;
            return false;
        }

        if (!ValidateTime(in leafCertificate, in currentTime, out status) ||
            !ValidateLeaf(in leafCertificate, out status) ||
            !TryMatchHostname(leaf, in leafCertificate, hostname))
        {
            if (status == ManagedX509ValidationStatus.Success)
                status = ManagedX509ValidationStatus.HostnameMismatch;
            return false;
        }
        if (hasIntermediate1 &&
            (!ValidateTime(in firstIntermediate, in currentTime, out status) ||
             firstIntermediate.HasUnknownCriticalExtension))
        {
            if (firstIntermediate.HasUnknownCriticalExtension)
                status = ManagedX509ValidationStatus.UnknownCriticalExtension;
            return false;
        }
        if (hasIntermediate2 &&
            (!ValidateTime(in secondIntermediate, in currentTime, out status) ||
             secondIntermediate.HasUnknownCriticalExtension))
        {
            if (secondIntermediate.HasUnknownCriticalExtension)
                status = ManagedX509ValidationStatus.UnknownCriticalExtension;
            return false;
        }
        if (rootCertificate.HasUnknownCriticalExtension)
        {
            status = ManagedX509ValidationStatus.UnknownCriticalExtension;
            return false;
        }

        if (hasIntermediate2)
        {
            if (!ValidateIssuer(in secondIntermediate, 1, out status) ||
                !ValidateIssuer(in rootCertificate, 2, out status) ||
                !VerifyLink(intermediate1, in firstIntermediate,
                            intermediate2, in secondIntermediate, out status) ||
                !VerifyLink(intermediate2, in secondIntermediate,
                            candidateRoot, in rootCertificate, out status))
                return false;
            if (!VerifyLink(leaf, in leafCertificate, intermediate1,
                            in firstIntermediate, out status)) return false;
        }
        else if (hasIntermediate1)
        {
            if (!ValidateIssuer(in firstIntermediate, 0, out status) ||
                !ValidateIssuer(in rootCertificate, 1, out status) ||
                !VerifyLink(leaf, in leafCertificate, intermediate1,
                            in firstIntermediate, out status) ||
                !VerifyLink(intermediate1, in firstIntermediate,
                            candidateRoot, in rootCertificate, out status))
                return false;
        }
        else
        {
            if (!ValidateIssuer(in rootCertificate, 0, out status) ||
                !VerifyLink(leaf, in leafCertificate, candidateRoot,
                            in rootCertificate, out status))
                return false;
        }

        status = ManagedX509ValidationStatus.Success;
        return true;
    }

    internal static bool TryParseTimeForTest(byte tag, ReadOnlySpan<byte> value,
                                             out ManagedX509UtcTime time)
    {
        return TryParseTime(tag, value, out time);
    }

    internal static bool IsValidDnsNameForTest(ReadOnlySpan<byte> name,
                                               bool wildcard)
    {
        return IsValidDnsName(name, wildcard);
    }

    private static ManagedX509ValidationStatus ParseTbs(
        ReadOnlySpan<byte> der, ref ManagedDerReader tbs,
        out int serialOffset, out int serialLength, out int issuerOffset,
        out int issuerLength, out int subjectOffset, out int subjectLength,
        out int publicKeyOffset, out int publicKeyLength, out int sanOffset,
        out int sanLength, out int commonNameOffset, out int commonNameLength,
        out int dnsNameCount, out ManagedX509UtcTime notBefore,
        out ManagedX509UtcTime notAfter, out bool hasBasicConstraints,
        out bool isCertificateAuthority, out bool hasPathLengthConstraint,
        out int pathLengthConstraint, out bool hasKeyUsage,
        out ushort keyUsage, out bool hasExtendedKeyUsage,
        out bool hasServerAuth, out bool hasSubjectAltName,
        out bool hasUnknownCriticalExtension,
        out ManagedX509ValidationStatus status)
    {
        serialOffset = serialLength = issuerOffset = issuerLength = 0;
        subjectOffset = subjectLength = publicKeyOffset = publicKeyLength = 0;
        sanOffset = sanLength = commonNameOffset = commonNameLength = 0;
        dnsNameCount = 0;
        notBefore = notAfter = default;
        hasBasicConstraints = isCertificateAuthority = false;
        hasPathLengthConstraint = false;
        pathLengthConstraint = 0;
        hasKeyUsage = false;
        keyUsage = 0;
        hasExtendedKeyUsage = hasServerAuth = hasSubjectAltName = false;
        hasUnknownCriticalExtension = false;
        status = ManagedX509ValidationStatus.MalformedDer;

        int version = 0;
        if (tbs.TryPeekTag(out byte firstTag) && firstTag == 0xA0)
        {
            if (!tbs.TryEnter(0xA0, out ManagedDerReader versionWrapper,
                              out _, out _) ||
                !versionWrapper.TryRead(Integer, out _, out _,
                                        out int versionOffset,
                                        out int versionLength) ||
                !TryReadSmallInteger(der, versionOffset, versionLength,
                                     2, out version) ||
                !versionWrapper.AtEnd)
                return status;
        }
        if (version < 0 || version > 2 ||
            !tbs.TryRead(Integer, out _, out _, out serialOffset,
                        out serialLength) ||
            !TryValidatePositiveInteger(der, serialOffset, serialLength,
                                         20) ||
            (serialLength == 1 && der[serialOffset] == 0))
            return status;

        if (!tbs.TryEnter(Sequence, out ManagedDerReader signature,
                          out _, out _))
        {
            return status;
        }
        status = ParseSignatureAlgorithm(der, ref signature);
        if (status != ManagedX509ValidationStatus.Success)
            return status;

        if (!tbs.TryEnter(Sequence, out ManagedDerReader issuer,
                          out issuerOffset, out issuerLength) ||
            !ParseName(der, ref issuer, false, out _, out _, out _) ||
            !issuer.AtEnd)
        {
            status = ManagedX509ValidationStatus.MalformedDer;
            return status;
        }
        if (!tbs.TryEnter(Sequence, out ManagedDerReader validity,
                          out _, out _) ||
            !TryReadValidity(der, ref validity, out notBefore,
                             out notAfter))
        {
            status = ManagedX509ValidationStatus.MalformedDer;
            return status;
        }
        if (!tbs.TryEnter(Sequence, out ManagedDerReader subject,
                          out subjectOffset, out subjectLength) ||
            !ParseName(der, ref subject, true, out commonNameOffset,
                       out commonNameLength, out _) || !subject.AtEnd)
        {
            status = ManagedX509ValidationStatus.MalformedDer;
            return status;
        }

        if (!tbs.TryEnter(Sequence, out ManagedDerReader spki,
                          out _, out _))
        {
            status = ManagedX509ValidationStatus.MalformedDer;
            return status;
        }
        ManagedX509ValidationStatus publicKeyStatus =
            ParseSubjectPublicKeyInfo(der, ref spki, out publicKeyOffset,
                                      out publicKeyLength);
        if (publicKeyStatus != ManagedX509ValidationStatus.Success)
        {
            status = publicKeyStatus;
            return status;
        }

        bool extensionsSeen = false;
        bool issuerUniqueIdSeen = false;
        bool subjectUniqueIdSeen = false;
        int extensionCount = 0;
        while (!tbs.AtEnd)
        {
            if (!tbs.TryPeekTag(out byte tag))
            {
                status = ManagedX509ValidationStatus.MalformedDer;
                return status;
            }
            if (tag == 0x81 || tag == 0x82)
            {
                if (tag == 0x81 ? issuerUniqueIdSeen : subjectUniqueIdSeen)
                {
                    status = ManagedX509ValidationStatus.MalformedDer;
                    return status;
                }
                if (tag == 0x81) issuerUniqueIdSeen = true;
                else subjectUniqueIdSeen = true;
                if (!tbs.TryReadAny(out _, out _, out _, out int idOffset,
                                    out int idLength) ||
                    !TryGetBitStringPayload(der, idOffset, idLength, false,
                                            out _, out _))
                {
                    status = ManagedX509ValidationStatus.MalformedDer;
                    return status;
                }
                continue;
            }
            if (tag != 0xA3 || extensionsSeen || version != 2)
            {
                status = ManagedX509ValidationStatus.MalformedDer;
                return status;
            }
            extensionsSeen = true;
            if (!tbs.TryEnter(0xA3, out ManagedDerReader extensionWrapper,
                              out _, out _) ||
                !extensionWrapper.TryEnter(Sequence,
                    out ManagedDerReader extensions, out _, out _))
            {
                status = ManagedX509ValidationStatus.MalformedDer;
                return status;
            }
            ManagedX509ValidationStatus extensionStatus = ParseExtensions(
                der, ref extensions, ref extensionCount, ref sanOffset,
                ref sanLength, ref dnsNameCount, ref hasBasicConstraints,
                ref isCertificateAuthority, ref hasPathLengthConstraint,
                ref pathLengthConstraint, ref hasKeyUsage, ref keyUsage,
                ref hasExtendedKeyUsage, ref hasServerAuth,
                ref hasSubjectAltName, ref hasUnknownCriticalExtension);
            if (extensionStatus != ManagedX509ValidationStatus.Success ||
                !extensions.AtEnd || !extensionWrapper.AtEnd)
            {
                status = extensionStatus == ManagedX509ValidationStatus.Success
                    ? ManagedX509ValidationStatus.MalformedDer
                    : extensionStatus;
                return status;
            }
        }
        status = ManagedX509ValidationStatus.Success;
        return status;
    }

    private static ManagedX509ValidationStatus ParseSignatureAlgorithm(
        ReadOnlySpan<byte> der, ref ManagedDerReader reader)
    {
        if (!reader.TryRead(ObjectIdentifier, out _, out _,
                            out int oidOffset, out int oidLength) ||
            !TryValidateOid(der, oidOffset, oidLength))
            return ManagedX509ValidationStatus.MalformedDer;
        if (!IsOid(der, oidOffset, oidLength, EcdsaWithSha256Oid) ||
            !reader.AtEnd)
            return ManagedX509ValidationStatus.UnsupportedAlgorithm;
        return ManagedX509ValidationStatus.Success;
    }

    private static ManagedX509ValidationStatus ParseSubjectPublicKeyInfo(
        ReadOnlySpan<byte> der, ref ManagedDerReader reader,
        out int keyOffset, out int keyLength)
    {
        keyOffset = keyLength = 0;
        if (!reader.TryEnter(Sequence, out ManagedDerReader algorithm,
                             out _, out _))
            return ManagedX509ValidationStatus.MalformedDer;
        if (!algorithm.TryRead(ObjectIdentifier, out _, out _,
                               out int algorithmOidOffset,
                               out int algorithmOidLength) ||
            !IsOid(der, algorithmOidOffset, algorithmOidLength, EcPublicKeyOid) ||
            !algorithm.TryRead(ObjectIdentifier, out _, out _,
                               out int curveOidOffset, out int curveOidLength) ||
            !IsOid(der, curveOidOffset, curveOidLength, Prime256v1Oid) ||
            !algorithm.AtEnd)
        {
            return ManagedX509ValidationStatus.UnsupportedAlgorithm;
        }
        if (!reader.TryRead(BitString, out _, out _, out int bitOffset,
                            out int bitLength) ||
            !TryGetBitStringPayload(der, bitOffset, bitLength, true,
                                    out keyOffset, out keyLength) ||
            keyLength != ManagedP256.PublicKeySize ||
            !ManagedP256.TryValidatePublicKey(
                der.Slice(keyOffset, keyLength)) || !reader.AtEnd)
        {
            return ManagedX509ValidationStatus.InvalidPublicKey;
        }
        return ManagedX509ValidationStatus.Success;
    }

    private static bool ParseName(ReadOnlySpan<byte> der,
                                  ref ManagedDerReader name, bool captureCn,
                                  out int commonNameOffset,
                                  out int commonNameLength,
                                  out int attributeCount)
    {
        commonNameOffset = commonNameLength = attributeCount = 0;
        bool foundCn = false;
        while (!name.AtEnd)
        {
            if (!name.TryEnter(Set, out ManagedDerReader rdn,
                               out _, out _) || rdn.AtEnd)
                return false;
            while (!rdn.AtEnd)
            {
                if (++attributeCount > MaximumRdnAttributes ||
                    !rdn.TryEnter(Sequence, out ManagedDerReader atv,
                                  out _, out _) ||
                    !atv.TryRead(ObjectIdentifier, out _, out _,
                                 out int oidOffset, out int oidLength) ||
                    !TryValidateOid(der, oidOffset, oidLength))
                    return false;
                bool isCn = IsOid(der, oidOffset, oidLength, CommonNameOid);
                if (!atv.TryReadAny(out byte valueTag, out _, out _,
                                    out int valueOffset, out int valueLength) ||
                    !IsSupportedNameString(valueTag, der, valueOffset,
                                           valueLength) || !atv.AtEnd)
                    return false;
                if (captureCn && isCn)
                {
                    if (foundCn) return false;
                    foundCn = true;
                    commonNameOffset = valueOffset;
                    commonNameLength = valueLength;
                }
            }
            if (!rdn.AtEnd) return false;
        }
        return true;
    }

    private static bool TryReadValidity(ReadOnlySpan<byte> der,
                                        ref ManagedDerReader validity,
                                        out ManagedX509UtcTime notBefore,
                                        out ManagedX509UtcTime notAfter)
    {
        notBefore = notAfter = default;
        if (!validity.TryReadAny(out byte firstTag, out _, out _,
                                 out int firstOffset, out int firstLength) ||
            !TryParseTime(firstTag, der.Slice(firstOffset, firstLength),
                          out notBefore) ||
            !validity.TryReadAny(out byte secondTag, out _, out _,
                                 out int secondOffset, out int secondLength) ||
            !TryParseTime(secondTag, der.Slice(secondOffset, secondLength),
                          out notAfter) || !validity.AtEnd ||
            ManagedX509UtcTime.Compare(in notBefore, in notAfter) > 0)
            return false;
        return true;
    }

    private static bool TryParseTime(byte tag, ReadOnlySpan<byte> value,
                                     out ManagedX509UtcTime time)
    {
        time = default;
        int digits = tag == UtcTime ? 12 : tag == GeneralizedTime ? 14 : 0;
        if (digits == 0 || value.Length != digits + 1 ||
            value[^1] != (byte)'Z') return false;
        Span<int> parts = stackalloc int[6];
        int offset = 0;
        for (int part = 0; part != 6; ++part)
        {
            int width = part == 0 && tag == GeneralizedTime ? 4 : 2;
            if (offset + width > digits) return false;
            int number = 0;
            for (int index = 0; index != width; ++index)
            {
                byte digit = value[offset++];
                if (digit < (byte)'0' || digit > (byte)'9') return false;
                number = number * 10 + digit - (byte)'0';
            }
            parts[part] = number;
        }
        int year = tag == UtcTime ?
            (parts[0] >= 50 ? 1900 + parts[0] : 2000 + parts[0]) : parts[0];
        return ManagedX509UtcTime.TryCreate(year, parts[1], parts[2],
                                            parts[3], parts[4], parts[5],
                                            out time);
    }

    private static ManagedX509ValidationStatus ParseExtensions(
        ReadOnlySpan<byte> der, ref ManagedDerReader extensions,
        ref int extensionCount, ref int sanOffset, ref int sanLength,
        ref int dnsNameCount, ref bool hasBasicConstraints,
        ref bool isCertificateAuthority, ref bool hasPathLengthConstraint,
        ref int pathLengthConstraint, ref bool hasKeyUsage,
        ref ushort keyUsage, ref bool hasExtendedKeyUsage,
        ref bool hasServerAuth, ref bool hasSubjectAltName,
        ref bool hasUnknownCriticalExtension)
    {
        while (!extensions.AtEnd)
        {
            if (++extensionCount > MaximumExtensionCount ||
                !extensions.TryEnter(Sequence, out ManagedDerReader extension,
                                      out _, out _))
                return ManagedX509ValidationStatus.MalformedDer;
            if (!extension.TryRead(ObjectIdentifier, out _, out _,
                                   out int oidOffset, out int oidLength) ||
                !TryValidateOid(der, oidOffset, oidLength))
                return ManagedX509ValidationStatus.MalformedDer;
            bool critical = false;
            if (extension.TryPeekTag(out byte nextTag) && nextTag == Boolean)
            {
                if (!extension.TryRead(Boolean, out _, out _,
                                       out int boolOffset, out int boolLength) ||
                    boolLength != 1 ||
                    (der[boolOffset] != 0 && der[boolOffset] != 0xFF))
                    return ManagedX509ValidationStatus.MalformedDer;
                critical = der[boolOffset] != 0;
            }
            if (!extension.TryRead(OctetString, out _, out _,
                                   out int valueOffset, out int valueLength) ||
                valueLength == 0 || !extension.AtEnd)
                return ManagedX509ValidationStatus.MalformedDer;

            bool known = false;
            ManagedX509ValidationStatus status =
                ManagedX509ValidationStatus.Success;
            if (IsOid(der, oidOffset, oidLength, BasicConstraintsOid))
            {
                known = true;
                if (hasBasicConstraints || !ParseBasicConstraints(
                        der.Slice(valueOffset, valueLength),
                        out bool ca, out bool pathPresent, out int path))
                    return ManagedX509ValidationStatus.MalformedDer;
                hasBasicConstraints = true;
                isCertificateAuthority = ca;
                hasPathLengthConstraint = pathPresent;
                pathLengthConstraint = path;
            }
            else if (IsOid(der, oidOffset, oidLength, KeyUsageOid))
            {
                known = true;
                if (hasKeyUsage || !ParseKeyUsage(
                        der.Slice(valueOffset, valueLength), out keyUsage))
                    return ManagedX509ValidationStatus.MalformedDer;
                hasKeyUsage = true;
            }
            else if (IsOid(der, oidOffset, oidLength, ExtendedKeyUsageOid))
            {
                known = true;
                if (hasExtendedKeyUsage || !ParseExtendedKeyUsage(
                        der.Slice(valueOffset, valueLength), out hasServerAuth))
                    return ManagedX509ValidationStatus.MalformedDer;
                hasExtendedKeyUsage = true;
            }
            else if (IsOid(der, oidOffset, oidLength, SubjectAltNameOid))
            {
                known = true;
                if (hasSubjectAltName || !ParseSubjectAltName(
                        der, valueOffset, valueLength, critical,
                        out dnsNameCount))
                    return ManagedX509ValidationStatus.MalformedDer;
                hasSubjectAltName = true;
                sanOffset = valueOffset;
                sanLength = valueLength;
            }
            if (!known && critical) hasUnknownCriticalExtension = true;
            if (status != ManagedX509ValidationStatus.Success) return status;
        }
        return ManagedX509ValidationStatus.Success;
    }

    private static bool ParseBasicConstraints(ReadOnlySpan<byte> value,
                                              out bool ca,
                                              out bool pathPresent,
                                              out int pathLength)
    {
        ca = false;
        pathPresent = false;
        pathLength = 0;
        ManagedDerReader reader = new(value);
        if (!reader.TryEnter(Sequence, out ManagedDerReader body,
                             out _, out _)) return false;
        if (body.TryPeekTag(out byte tag) && tag == Boolean)
        {
            if (!body.TryRead(Boolean, out _, out _, out int boolOffset,
                              out int boolLength) || boolLength != 1 ||
                (value[boolOffset] != 0 && value[boolOffset] != 0xFF))
                return false;
            ca = value[boolOffset] == 0xFF;
        }
        if (body.HasData)
        {
            if (!body.TryRead(Integer, out _, out _, out int intOffset,
                              out int intLength) ||
                !TryReadSmallInteger(value, intOffset, intLength,
                                     int.MaxValue, out pathLength))
                return false;
            pathPresent = true;
        }
        return body.AtEnd && reader.AtEnd && (!pathPresent || ca);
    }

    private static bool ParseKeyUsage(ReadOnlySpan<byte> value,
                                      out ushort keyUsage)
    {
        keyUsage = 0;
        ManagedDerReader reader = new(value);
        if (!reader.TryRead(BitString, out _, out _, out int bitOffset,
                            out int bitLength) || !reader.AtEnd ||
            !TryGetBitStringPayload(value, bitOffset, bitLength, true,
                                    out int payloadOffset,
                                    out int payloadLength) ||
            (payloadLength != 1 && payloadLength != 2) ||
            (payloadLength == 2 && value[bitOffset] != 7))
            return false;
        for (int index = 0; index != payloadLength; ++index)
        {
            byte bits = value[payloadOffset + index];
            for (int bit = 0; bit != 8; ++bit)
            {
                if ((bits & (1 << (7 - bit))) != 0)
                    keyUsage |= (ushort)(1 << (index * 8 + bit));
            }
        }
        return true;
    }

    private static bool ParseExtendedKeyUsage(ReadOnlySpan<byte> value,
                                              out bool serverAuth)
    {
        serverAuth = false;
        ManagedDerReader reader = new(value);
        if (!reader.TryEnter(Sequence, out ManagedDerReader body,
                             out _, out _)) return false;
        int count = 0;
        while (!body.AtEnd)
        {
            if (++count > MaximumExtensionCount ||
                !body.TryRead(ObjectIdentifier, out _, out _,
                              out int oidOffset, out int oidLength) ||
                !TryValidateOid(value, oidOffset, oidLength))
                return false;
            if (IsOid(value, oidOffset, oidLength, ServerAuthOid))
                serverAuth = true;
        }
        return count != 0 && body.AtEnd && reader.AtEnd;
    }

    private static bool ParseSubjectAltName(ReadOnlySpan<byte> der,
                                            int offset, int length,
                                            bool critical, out int dnsNameCount)
    {
        dnsNameCount = 0;
        ReadOnlySpan<byte> value = der.Slice(offset, length);
        ManagedDerReader reader = new(value);
        if (!reader.TryEnter(Sequence, out ManagedDerReader names,
                             out _, out _)) return false;
        while (!names.AtEnd)
        {
            if (!names.TryReadAny(out byte tag, out _, out _,
                                  out int nameOffset, out int nameLength))
                return false;
            if (tag == 0x82)
            {
                if (++dnsNameCount > MaximumSanDnsNames ||
                    !IsValidDnsName(value.Slice(nameOffset, nameLength), true))
                    return false;
            }
            else if (critical)
            {
                return false;
            }
        }
        return reader.AtEnd && names.AtEnd;
    }

    private static bool ValidateTime(in ManagedX509Certificate certificate,
                                     in ManagedX509UtcTime current,
                                     out ManagedX509ValidationStatus status)
    {
        status = ManagedX509ValidationStatus.Success;
        if (!certificate.NotBefore.IsValid || !certificate.NotAfter.IsValid ||
            !current.IsValid)
        {
            status = ManagedX509ValidationStatus.TimeUnavailable;
            return false;
        }
        if (ManagedX509UtcTime.Compare(in current, in certificate.NotBefore) < 0)
        {
            status = ManagedX509ValidationStatus.NotYetValid;
            return false;
        }
        if (ManagedX509UtcTime.Compare(in current, in certificate.NotAfter) > 0)
        {
            status = ManagedX509ValidationStatus.Expired;
            return false;
        }
        return true;
    }

    private static bool ValidateLeaf(in ManagedX509Certificate certificate,
                                     out ManagedX509ValidationStatus status)
    {
        status = ManagedX509ValidationStatus.Success;
        if (certificate.HasBasicConstraints &&
            certificate.IsCertificateAuthority)
        {
            status = ManagedX509ValidationStatus.InvalidCa;
            return false;
        }
        if (certificate.HasKeyUsage && !certificate.HasDigitalSignature)
        {
            status = ManagedX509ValidationStatus.InvalidKeyUsage;
            return false;
        }
        if (certificate.HasExtendedKeyUsage && !certificate.HasServerAuth)
        {
            status = ManagedX509ValidationStatus.InvalidExtendedKeyUsage;
            return false;
        }
        return true;
    }

    private static bool ValidateIssuer(in ManagedX509Certificate certificate,
                                       int caBelow,
                                       out ManagedX509ValidationStatus status)
    {
        status = ManagedX509ValidationStatus.Success;
        if (!certificate.HasBasicConstraints ||
            !certificate.IsCertificateAuthority)
        {
            status = ManagedX509ValidationStatus.InvalidCa;
            return false;
        }
        if (!certificate.HasKeyUsage || !certificate.HasKeyCertSign)
        {
            status = ManagedX509ValidationStatus.InvalidKeyUsage;
            return false;
        }
        if (certificate.HasPathLengthConstraint &&
            caBelow > certificate.PathLengthConstraint)
        {
            status = ManagedX509ValidationStatus.PathLengthExceeded;
            return false;
        }
        return true;
    }

    private static bool VerifyLink(ReadOnlySpan<byte> childDer,
                                   in ManagedX509Certificate child,
                                   ReadOnlySpan<byte> issuerDer,
                                   in ManagedX509Certificate issuer,
                                   out ManagedX509ValidationStatus status)
    {
        status = ManagedX509ValidationStatus.IssuerSubjectMismatch;
        if (!HasRange(childDer, child.IssuerOffset, child.IssuerLength) ||
            !HasRange(issuerDer, issuer.SubjectOffset, issuer.SubjectLength) ||
            !RangesEqual(childDer, child.IssuerOffset, child.IssuerLength,
                         issuerDer, issuer.SubjectOffset, issuer.SubjectLength))
            return false;
        return TryValidateCertificateSignature(childDer, in child,
                                               issuerDer.Slice(
                                                   issuer.PublicKeyOffset,
                                                   issuer.PublicKeyLength),
                                               out status);
    }

    private static bool TryGetBitStringPayload(ReadOnlySpan<byte> der,
                                               int offset, int length,
                                               bool requireNonEmpty,
                                               out int payloadOffset,
                                               out int payloadLength)
    {
        payloadOffset = payloadLength = 0;
        if (!HasRange(der, offset, length) || length < 1) return false;
        byte unused = der[offset];
        if (unused > 7 || (requireNonEmpty && length == 1)) return false;
        payloadOffset = offset + 1;
        payloadLength = length - 1;
        if (payloadLength != 0 &&
            (der[offset + length - 1] & ((1 << unused) - 1)) != 0)
            return false;
        return true;
    }

    private static bool TryValidatePositiveInteger(ReadOnlySpan<byte> der,
                                                    int offset, int length,
                                                    int maximumLength)
    {
        if (!HasRange(der, offset, length) || length == 0 ||
            length > maximumLength || (der[offset] & 0x80) != 0)
            return false;
        return length == 1 || der[offset] != 0 ||
               (der[offset + 1] & 0x80) != 0;
    }

    private static bool TryReadSmallInteger(ReadOnlySpan<byte> der,
                                            int offset, int length,
                                            int maximum,
                                            out int value)
    {
        value = 0;
        if (!TryValidatePositiveInteger(der, offset, length, 4)) return false;
        int start = der[offset] == 0 ? offset + 1 : offset;
        for (int index = start; index != offset + length; ++index)
        {
            if (value > (maximum - der[index]) / 256) return false;
            value = value * 256 + der[index];
        }
        return value <= maximum;
    }

    private static bool TryValidateOid(ReadOnlySpan<byte> der, int offset,
                                       int length)
    {
        if (!HasRange(der, offset, length) || length == 0) return false;
        int end = offset + length;
        int position = offset;
        while (position < end)
        {
            bool first = true;
            int octets = 0;
            while (true)
            {
                if (position >= end || ++octets > 5) return false;
                byte current = der[position++];
                if (first && current == 0x80) return false;
                first = false;
                if ((current & 0x80) == 0) break;
            }
        }
        return true;
    }

    private static bool IsOid(ReadOnlySpan<byte> der, int offset, int length,
                              ReadOnlySpan<byte> expected)
    {
        return length == expected.Length && HasRange(der, offset, length) &&
               ManagedCryptoComparison.FixedTimeEquals(
                   der.Slice(offset, length), expected);
    }

    private static bool IsSupportedNameString(byte tag, ReadOnlySpan<byte> der,
                                              int offset, int length)
    {
        if (tag != Utf8String && tag != PrintableString && tag != Ia5String)
            return false;
        if (!HasRange(der, offset, length) || length > MaximumDnsNameLength)
            return false;
        if (tag == Ia5String)
        {
            for (int index = 0; index != length; ++index)
                if (der[offset + index] > 0x7F) return false;
        }
        return true;
    }

    private static bool IsValidDnsName(ReadOnlySpan<byte> name,
                                       bool wildcard)
    {
        if (name.Length == 0 || name.Length > MaximumDnsNameLength)
            return false;
        int labelStart = 0;
        int labelCount = 0;
        while (labelStart < name.Length)
        {
            int labelEnd = labelStart;
            while (labelEnd < name.Length && name[labelEnd] != (byte)'.')
                ++labelEnd;
            int labelLength = labelEnd - labelStart;
            if (labelLength == 0 || labelLength > 63 ||
                name[labelStart] == (byte)'-' ||
                name[labelEnd - 1] == (byte)'-') return false;
            bool isWildcard = labelStart == 0 && labelLength == 1 &&
                              name[0] == (byte)'*';
            if (isWildcard)
            {
                if (!wildcard || labelEnd == name.Length ||
                    name.Slice(labelEnd + 1).IndexOf((byte)'.') < 0)
                    return false;
            }
            else
            {
                for (int index = labelStart; index < labelEnd; ++index)
                {
                    byte current = name[index];
                    bool alpha = (current >= (byte)'A' && current <= (byte)'Z') ||
                                 (current >= (byte)'a' && current <= (byte)'z');
                    bool digit = current >= (byte)'0' && current <= (byte)'9';
                    if (!alpha && !digit && current != (byte)'-') return false;
                }
            }
            ++labelCount;
            if (labelEnd == name.Length) break;
            labelStart = labelEnd + 1;
        }
        return labelCount != 0;
    }

    private static bool TryMatchDnsName(ReadOnlySpan<byte> value, int offset,
                                        int length, ReadOnlySpan<byte> hostname)
    {
        if (offset < 0 || length < 0 || length > value.Length - offset)
            return false;
        return TryMatchDnsPattern(value.Slice(offset, length), hostname);
    }

    private static bool TryMatchDnsPattern(ReadOnlySpan<byte> pattern,
                                           ReadOnlySpan<byte> hostname)
    {
        if (!IsValidDnsName(pattern, true) ||
            !IsValidDnsName(hostname, false)) return false;
        if (pattern[0] != (byte)'*')
            return AsciiEqualsIgnoreCase(pattern, hostname);
        ReadOnlySpan<byte> suffix = pattern[2..];
        if (hostname.Length <= suffix.Length + 1 ||
            hostname[hostname.Length - suffix.Length - 1] != (byte)'.' ||
            hostname[..(hostname.Length - suffix.Length - 1)].IndexOf(
                (byte)'.') >= 0)
            return false;
        return AsciiEqualsIgnoreCase(hostname[^suffix.Length..], suffix);
    }

    private static bool AsciiEqualsIgnoreCase(ReadOnlySpan<byte> left,
                                              ReadOnlySpan<byte> right)
    {
        if (left.Length != right.Length) return false;
        for (int index = 0; index != left.Length; ++index)
        {
            byte a = left[index];
            byte b = right[index];
            if (a >= (byte)'A' && a <= (byte)'Z') a += 32;
            if (b >= (byte)'A' && b <= (byte)'Z') b += 32;
            if (a != b) return false;
        }
        return true;
    }

    private static bool HasRange(ReadOnlySpan<byte> data, int offset, int length)
    {
        return offset >= 0 && length >= 0 && offset <= data.Length &&
               length <= data.Length - offset;
    }

    private static bool RangesEqual(ReadOnlySpan<byte> left, int leftOffset,
                                    int leftLength, ReadOnlySpan<byte> right,
                                    int rightOffset, int rightLength)
    {
        return leftLength == rightLength && HasRange(left, leftOffset,
               leftLength) && HasRange(right, rightOffset, rightLength) &&
               ManagedCryptoComparison.FixedTimeEquals(
                   left.Slice(leftOffset, leftLength),
                   right.Slice(rightOffset, rightLength));
    }
}
