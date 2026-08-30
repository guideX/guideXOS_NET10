using System;

namespace GuideXOS.Net10.ManagedKernel;

public enum ManagedHttpsUrlParseFailureReason : byte
{
    None = 0,
    Empty = 1,
    TooLong = 2,
    UnsupportedScheme = 3,
    MalformedScheme = 4,
    MalformedAuthority = 5,
    EmptyHostname = 6,
    InvalidHostname = 7,
    UserinfoNotSupported = 8,
    Ipv6NotSupported = 9,
    InvalidPort = 10,
    PortOverflow = 11,
    InvalidPath = 12,
    PathTooLong = 13,
    InvalidCharacter = 14,
    EmptyReference = 15,
    HttpsDowngrade = 16,
    UnsupportedReference = 17
}

/* A deliberately small HTTPS-only URL value.  The two arrays contain only
   the canonical hostname and request-target for this URL.  No URI framework,
   original-string retention, or unbounded visited-URL collection is used. */
public readonly struct ManagedHttpsUrl : IEquatable<ManagedHttpsUrl>
{
    public const int MaximumUrlLength = 512;
    public const int MaximumLocationLength = 128;
    public const int MaximumHostnameLength = ManagedHttpLimits.MaximumHostnameLength;
    public const int MaximumPathLength = ManagedHttpLimits.MaximumPathLength;
    public const ushort DefaultPort = ManagedHttpLimits.DefaultHttpsPort;

    private readonly byte[]? _hostname;
    private readonly byte[]? _requestTarget;
    private readonly ushort _port;

    private ManagedHttpsUrl(byte[] hostname, byte[] requestTarget, ushort port)
    {
        _hostname = hostname;
        _requestTarget = requestTarget;
        _port = port;
    }

    public bool IsValid => _hostname != null && _requestTarget != null;
    public ReadOnlySpan<byte> Hostname => _hostname ?? ReadOnlySpan<byte>.Empty;
    public ushort Port => _port;
    public ReadOnlySpan<byte> RequestTarget =>
        _requestTarget ?? ReadOnlySpan<byte>.Empty;

    public static bool TryParse(ReadOnlySpan<byte> value,
                                out ManagedHttpsUrl url) =>
        TryParse(value, out url, out _);

    public static bool TryParse(ReadOnlySpan<byte> value,
                                out ManagedHttpsUrl url,
                                out ManagedHttpsUrlParseFailureReason failure)
    {
        url = default;
        failure = ManagedHttpsUrlParseFailureReason.None;
        if (value.Length == 0)
            return SetFailure(out failure, ManagedHttpsUrlParseFailureReason.Empty);
        if (value.Length > MaximumUrlLength)
            return SetFailure(out failure, ManagedHttpsUrlParseFailureReason.TooLong);
        if (!ValidateCharacters(value))
            return SetFailure(out failure,
                              ManagedHttpsUrlParseFailureReason.InvalidCharacter);

        int end = value.IndexOf((byte)'#');
        if (end < 0) end = value.Length;
        int schemeEnd = value[..end].IndexOf((byte)':');
        if (schemeEnd < 0)
            return SetFailure(out failure,
                              ManagedHttpsUrlParseFailureReason.MalformedScheme);
        if (schemeEnd != 5 ||
            !EqualsAsciiIgnoreCase(value[..schemeEnd], "https"u8))
            return SetFailure(out failure,
                              ManagedHttpsUrlParseFailureReason.UnsupportedScheme);
        if (schemeEnd + 3 > end || value[schemeEnd + 1] != (byte)'/' ||
            value[schemeEnd + 2] != (byte)'/')
            return SetFailure(out failure,
                              ManagedHttpsUrlParseFailureReason.MalformedScheme);

        int authorityStart = schemeEnd + 3;
        int authorityEnd = authorityStart;
        while (authorityEnd < end && value[authorityEnd] != (byte)'/' &&
               value[authorityEnd] != (byte)'?')
            authorityEnd++;
        if (authorityStart == authorityEnd)
            return SetFailure(out failure,
                              ManagedHttpsUrlParseFailureReason.EmptyHostname);
        ReadOnlySpan<byte> authority = value[authorityStart..authorityEnd];
        if (authority.IndexOf((byte)'@') >= 0)
            return SetFailure(out failure,
                              ManagedHttpsUrlParseFailureReason.UserinfoNotSupported);
        if (authority[0] == (byte)'[' || authority.IndexOf((byte)'[') >= 0 ||
            authority.IndexOf((byte)']') >= 0)
            return SetFailure(out failure,
                              ManagedHttpsUrlParseFailureReason.Ipv6NotSupported);

        int portSeparator = authority.IndexOf((byte)':');
        if (portSeparator >= 0 &&
            authority[(portSeparator + 1)..].IndexOf((byte)':') >= 0)
            return SetFailure(out failure,
                              ManagedHttpsUrlParseFailureReason.InvalidPort);
        ReadOnlySpan<byte> hostname = portSeparator < 0
            ? authority : authority[..portSeparator];
        if (hostname.Length == 0)
            return SetFailure(out failure,
                              ManagedHttpsUrlParseFailureReason.EmptyHostname);
        if (!ManagedHttpRequestBuilder.IsValidHostname(hostname))
            return SetFailure(out failure,
                              ManagedHttpsUrlParseFailureReason.InvalidHostname);

        ushort port = DefaultPort;
        if (portSeparator >= 0)
        {
            ReadOnlySpan<byte> portText = authority[(portSeparator + 1)..];
            if (!TryParsePort(portText, out port, out failure)) return false;
        }

        ReadOnlySpan<byte> target = value[authorityEnd..end];
        Span<byte> normalizedTarget = stackalloc byte[MaximumPathLength];
        if (target.Length == 0)
        {
            normalizedTarget[0] = (byte)'/';
            return Create(hostname, normalizedTarget[..1], port,
                          out url, out failure);
        }
        if (target[0] == (byte)'?')
        {
            if (target.Length + 1 > MaximumPathLength)
                return SetFailure(out failure,
                                  ManagedHttpsUrlParseFailureReason.PathTooLong);
            normalizedTarget[0] = (byte)'/';
            target.CopyTo(normalizedTarget[1..]);
            return Create(hostname, normalizedTarget[..(target.Length + 1)], port,
                          out url, out failure);
        }
        if (target[0] != (byte)'/')
        {
            return SetFailure(out failure,
                              ManagedHttpsUrlParseFailureReason.InvalidPath);
        }

        if (!TryCopyTarget(target, normalizedTarget, out int targetLength,
                           out failure))
            return false;
        return Create(hostname, normalizedTarget[..targetLength], port,
                      out url, out failure);
    }

    public static bool TryParse(ReadOnlySpan<char> value,
                                out ManagedHttpsUrl url) =>
        TryParse(value, out url, out _);

    public static bool TryParse(string value, out ManagedHttpsUrl url) =>
        TryParse(value.AsSpan(), out url, out _);

    public static bool TryParse(ReadOnlySpan<char> value,
                                out ManagedHttpsUrl url,
                                out ManagedHttpsUrlParseFailureReason failure)
    {
        url = default;
        failure = ManagedHttpsUrlParseFailureReason.None;
        if (value.Length == 0)
            return SetFailure(out failure, ManagedHttpsUrlParseFailureReason.Empty);
        if (value.Length > MaximumUrlLength)
            return SetFailure(out failure, ManagedHttpsUrlParseFailureReason.TooLong);
        Span<byte> ascii = stackalloc byte[MaximumUrlLength];
        for (int index = 0; index != value.Length; ++index)
        {
            if (value[index] > 0x7F)
                return SetFailure(out failure,
                                  ManagedHttpsUrlParseFailureReason.InvalidCharacter);
            ascii[index] = (byte)value[index];
        }
        return TryParse(ascii[..value.Length], out url, out failure);
    }

    public static bool TryCreate(ReadOnlySpan<byte> hostname,
                                 ReadOnlySpan<byte> requestTarget,
                                 out ManagedHttpsUrl url) =>
        TryCreate(hostname, DefaultPort, requestTarget, out url);

    public static bool TryCreate(ReadOnlySpan<byte> hostname, ushort port,
                                 ReadOnlySpan<byte> requestTarget,
                                 out ManagedHttpsUrl url) =>
        Create(hostname, requestTarget, port, out url, out _);

    public static bool TryResolve(in ManagedHttpsUrl current,
                                  ReadOnlySpan<byte> reference,
                                  out ManagedHttpsUrl resolved) =>
        TryResolve(current, reference, out resolved, out _);

    public static bool TryResolve(in ManagedHttpsUrl current,
                                  string reference,
                                  out ManagedHttpsUrl resolved) =>
        TryResolve(current, reference.AsSpan(), out resolved, out _);

    public static bool TryResolve(in ManagedHttpsUrl current,
                                  ReadOnlySpan<char> reference,
                                  out ManagedHttpsUrl resolved,
                                  out ManagedHttpsUrlParseFailureReason failure)
    {
        resolved = default;
        failure = ManagedHttpsUrlParseFailureReason.None;
        if (reference.Length > MaximumLocationLength)
            return SetFailure(out failure,
                              ManagedHttpsUrlParseFailureReason.TooLong);
        Span<byte> ascii = stackalloc byte[MaximumLocationLength];
        for (int index = 0; index != reference.Length; ++index)
        {
            char value = reference[index];
            if (value > 0x7F)
                return SetFailure(out failure,
                                  ManagedHttpsUrlParseFailureReason.InvalidCharacter);
            ascii[index] = (byte)value;
        }
        return TryResolve(current, ascii[..reference.Length], out resolved,
                          out failure);
    }

    public static bool TryResolve(in ManagedHttpsUrl current,
                                  ReadOnlySpan<byte> reference,
                                  out ManagedHttpsUrl resolved,
                                  out ManagedHttpsUrlParseFailureReason failure)
    {
        resolved = default;
        failure = ManagedHttpsUrlParseFailureReason.None;
        if (!current.IsValid)
            return SetFailure(out failure,
                              ManagedHttpsUrlParseFailureReason.MalformedAuthority);
        if (reference.Length == 0)
            return SetFailure(out failure,
                              ManagedHttpsUrlParseFailureReason.EmptyReference);
        if (reference.Length > MaximumLocationLength)
            return SetFailure(out failure,
                              ManagedHttpsUrlParseFailureReason.TooLong);
        if (!ValidateCharacters(reference))
            return SetFailure(out failure,
                              ManagedHttpsUrlParseFailureReason.InvalidCharacter);

        int end = reference.IndexOf((byte)'#');
        if (end < 0) end = reference.Length;
        if (end == 0)
            return Create(current.Hostname, current.RequestTarget, current.Port,
                          out resolved, out failure);
        ReadOnlySpan<byte> withoutFragment = reference[..end];

        if (StartsWithAsciiIgnoreCase(withoutFragment, "https:"u8))
            return TryParse(withoutFragment, out resolved, out failure);
        if (StartsWithAsciiIgnoreCase(withoutFragment, "http:"u8))
            return SetFailure(out failure,
                              ManagedHttpsUrlParseFailureReason.HttpsDowngrade);
        if (withoutFragment.IndexOf((byte)':') >= 0 &&
            !withoutFragment.StartsWith("/"u8))
            return SetFailure(out failure,
                              ManagedHttpsUrlParseFailureReason.UnsupportedReference);

        Span<byte> absolute = stackalloc byte[MaximumUrlLength];
        int offset = 0;
        if (withoutFragment.StartsWith("//"u8))
        {
            if (!Append(absolute, ref offset, "https:"u8))
                return SetFailure(out failure,
                                  ManagedHttpsUrlParseFailureReason.TooLong);
            if (!Append(absolute, ref offset, withoutFragment))
                return SetFailure(out failure,
                                  ManagedHttpsUrlParseFailureReason.TooLong);
        }
        else if (withoutFragment[0] == (byte)'/' ||
                 withoutFragment[0] == (byte)'?')
        {
            if (!AppendOrigin(current, absolute, ref offset))
                return SetFailure(out failure,
                                  ManagedHttpsUrlParseFailureReason.TooLong);
            ReadOnlySpan<byte> path = withoutFragment;
            if (withoutFragment[0] == (byte)'?')
            {
                ReadOnlySpan<byte> currentPath = current.RequestTarget;
                int query = currentPath.IndexOf((byte)'?');
                if (query >= 0) currentPath = currentPath[..query];
                if (!Append(absolute, ref offset, currentPath) ||
                    !Append(absolute, ref offset, withoutFragment))
                    return SetFailure(out failure,
                                      ManagedHttpsUrlParseFailureReason.TooLong);
            }
            else
            {
                if (!AppendNormalizedPath(path, absolute, ref offset))
                    return SetFailure(out failure,
                                      offset >= MaximumUrlLength
                                          ? ManagedHttpsUrlParseFailureReason.TooLong
                                          : ManagedHttpsUrlParseFailureReason.InvalidPath);
            }
        }
        else
        {
            if (!AppendOrigin(current, absolute, ref offset))
                return SetFailure(out failure,
                                  ManagedHttpsUrlParseFailureReason.TooLong);
            ReadOnlySpan<byte> currentPath = current.RequestTarget;
            int query = currentPath.IndexOf((byte)'?');
            if (query >= 0) currentPath = currentPath[..query];
            int slash = currentPath.LastIndexOf((byte)'/');
            if (slash < 0) slash = 0;
            int pathStart = offset;
            if (!Append(absolute, ref offset, currentPath[..(slash + 1)]) ||
                !Append(absolute, ref offset, withoutFragment))
                return SetFailure(out failure,
                                  ManagedHttpsUrlParseFailureReason.TooLong);
            if (!NormalizeAbsolutePath(absolute, pathStart, offset,
                                       out int normalizedLength))
                return SetFailure(out failure,
                                  ManagedHttpsUrlParseFailureReason.InvalidPath);
            offset = normalizedLength;
        }

        return TryParse(absolute[..offset], out resolved, out failure);
    }

    public bool TryCopyAbsoluteUrl(Span<byte> destination, out int length)
    {
        length = 0;
        if (!IsValid) return false;
        Span<byte> scratch = stackalloc byte[MaximumUrlLength];
        int offset = 0;
        if (!AppendOrigin(this, scratch, ref offset) ||
            !Append(scratch, ref offset, RequestTarget) ||
            destination.Length < offset)
            return false;
        scratch[..offset].CopyTo(destination);
        length = offset;
        return true;
    }

    public bool Equals(ManagedHttpsUrl other) =>
        Port == other.Port && Hostname.SequenceEqual(other.Hostname) &&
        RequestTarget.SequenceEqual(other.RequestTarget);

    public override bool Equals(object? obj) =>
        obj is ManagedHttpsUrl other && Equals(other);

    public override int GetHashCode() =>
        (int)((uint)Port * 397U + (uint)Hostname.Length * 17U +
              (uint)RequestTarget.Length);

    internal void Clear()
    {
        _hostname?.AsSpan().Clear();
        _requestTarget?.AsSpan().Clear();
    }

    private static bool Create(ReadOnlySpan<byte> hostname,
                               ReadOnlySpan<byte> requestTarget,
                               ushort port,
                               out ManagedHttpsUrl url,
                               out ManagedHttpsUrlParseFailureReason failure)
    {
        url = default;
        failure = ManagedHttpsUrlParseFailureReason.None;
        if (port == 0)
            return SetFailure(out failure,
                              ManagedHttpsUrlParseFailureReason.InvalidPort);
        if (!ManagedHttpRequestBuilder.IsValidHostname(hostname))
            return SetFailure(out failure,
                              ManagedHttpsUrlParseFailureReason.InvalidHostname);
        Span<byte> target = stackalloc byte[MaximumPathLength];
        if (!TryCopyTarget(requestTarget, target, out int length,
                           out failure))
            return false;
        byte[] canonicalHostname = new byte[hostname.Length];
        for (int index = 0; index != hostname.Length; ++index)
            canonicalHostname[index] = ToLowerAscii(hostname[index]);
        url = new ManagedHttpsUrl(canonicalHostname, target[..length].ToArray(),
                                  port);
        return true;
    }

    private static bool TryCopyTarget(ReadOnlySpan<byte> value,
                                      Span<byte> destination,
                                      out int length,
                                      out ManagedHttpsUrlParseFailureReason failure)
    {
        length = 0;
        failure = ManagedHttpsUrlParseFailureReason.None;
        if (value.Length == 0 || value[0] != (byte)'/')
            return SetFailure(out failure,
                              ManagedHttpsUrlParseFailureReason.InvalidPath);
        int end = value.IndexOf((byte)'#');
        if (end < 0) end = value.Length;
        ReadOnlySpan<byte> path = value[..end];
        if (path.Length > MaximumPathLength)
            return SetFailure(out failure,
                              ManagedHttpsUrlParseFailureReason.PathTooLong);
        for (int index = 0; index != path.Length; ++index)
        {
            byte current = path[index];
            if (current < 0x21 || current > 0x7E || current == (byte)'\\' ||
                current == (byte)'#')
                return SetFailure(out failure,
                                  ManagedHttpsUrlParseFailureReason.InvalidPath);
        }
        path.CopyTo(destination);
        length = path.Length;
        return true;
    }

    private static bool AppendNormalizedPath(ReadOnlySpan<byte> path,
                                             Span<byte> destination,
                                             ref int offset)
    {
        int query = path.IndexOf((byte)'?');
        ReadOnlySpan<byte> pathOnly = query < 0 ? path : path[..query];
        if (pathOnly.Length == 0 || pathOnly[0] != (byte)'/') return false;
        Span<byte> normalized = stackalloc byte[MaximumPathLength];
        if (!NormalizePath(pathOnly, normalized, out int pathLength)) return false;
        if (!Append(destination, ref offset, normalized[..pathLength])) return false;
        return query < 0 || Append(destination, ref offset, path[query..]);
    }

    private static bool NormalizePath(ReadOnlySpan<byte> path,
                                      Span<byte> destination,
                                      out int length)
    {
        length = 1;
        destination[0] = (byte)'/';
        int index = 1;
        while (index <= path.Length)
        {
            int start = index;
            while (index < path.Length && path[index] != (byte)'/') index++;
            ReadOnlySpan<byte> segment = path[start..index];
            if (segment.Length != 0 && !segment.SequenceEqual("."u8))
            {
                if (segment.SequenceEqual(".."u8))
                {
                    while (length > 1 && destination[length - 1] == (byte)'/')
                        length--;
                    while (length > 1 && destination[length - 1] != (byte)'/')
                        length--;
                }
                else
                {
                    if (length > 1 && destination[length - 1] != (byte)'/')
                        destination[length++] = (byte)'/';
                    if (segment.Length > destination.Length - length)
                        return false;
                    segment.CopyTo(destination[length..]);
                    length += segment.Length;
                }
            }
            if (index == path.Length) break;
            index++;
        }
        if (path.Length > 1 && path[^1] == (byte)'/' &&
            destination[length - 1] != (byte)'/')
            destination[length++] = (byte)'/';
        return length <= MaximumPathLength;
    }

    private static bool NormalizeAbsolutePath(Span<byte> value,
                                              int pathStart,
                                              int end,
                                              out int normalizedEnd)
    {
        normalizedEnd = end;
        if (pathStart < 0 || pathStart >= end || value[pathStart] != (byte)'/')
            return false;
        int query = -1;
        for (int index = pathStart; index != end; ++index)
            if (value[index] == (byte)'?') { query = index; break; }
        int pathEnd = query < 0 ? end : query;
        Span<byte> normalized = stackalloc byte[MaximumPathLength];
        if (!NormalizePath(value[pathStart..pathEnd], normalized,
                           out int pathLength)) return false;
        int tailLength = end - pathEnd;
        if (pathLength + tailLength > MaximumPathLength) return false;
        normalized[..pathLength].CopyTo(value[pathStart..]);
        if (tailLength != 0)
            value[pathEnd..end].CopyTo(value[(pathStart + pathLength)..]);
        normalizedEnd = pathStart + pathLength + tailLength;
        return true;
    }

    private static bool AppendOrigin(in ManagedHttpsUrl value,
                                     Span<byte> destination, ref int offset)
    {
        if (!Append(destination, ref offset, "https://"u8) ||
            !Append(destination, ref offset, value.Hostname)) return false;
        if (value.Port != DefaultPort)
        {
            if (!Append(destination, ref offset, ":"u8) ||
                !AppendPort(destination, ref offset, value.Port)) return false;
        }
        return true;
    }

    private static bool AppendPort(Span<byte> destination, ref int offset,
                                   ushort port)
    {
        Span<byte> digits = stackalloc byte[5];
        int count = 0;
        do
        {
            digits[count++] = (byte)('0' + port % 10);
            port /= 10;
        } while (port != 0);
        if (count > destination.Length - offset) return false;
        for (int index = 0; index != count; ++index)
            destination[offset + index] = digits[count - index - 1];
        offset += count;
        return true;
    }

    private static bool TryParsePort(ReadOnlySpan<byte> value,
                                     out ushort port,
                                     out ManagedHttpsUrlParseFailureReason failure)
    {
        port = 0;
        failure = ManagedHttpsUrlParseFailureReason.None;
        if (value.Length == 0)
            return SetFailure(out failure,
                              ManagedHttpsUrlParseFailureReason.InvalidPort);
        uint parsed = 0;
        for (int index = 0; index != value.Length; ++index)
        {
            if (value[index] < (byte)'0' || value[index] > (byte)'9')
                return SetFailure(out failure,
                                  ManagedHttpsUrlParseFailureReason.InvalidPort);
            parsed = parsed * 10U + (uint)(value[index] - (byte)'0');
            if (parsed > ushort.MaxValue)
                return SetFailure(out failure,
                                  ManagedHttpsUrlParseFailureReason.PortOverflow);
        }
        if (parsed == 0)
            return SetFailure(out failure,
                              ManagedHttpsUrlParseFailureReason.InvalidPort);
        port = (ushort)parsed;
        return true;
    }

    private static bool ValidateCharacters(ReadOnlySpan<byte> value)
    {
        for (int index = 0; index != value.Length; ++index)
        {
            byte current = value[index];
            if (current < 0x21 || current > 0x7E || current == (byte)'\\')
                return false;
        }
        return true;
    }

    private static bool Append(Span<byte> destination, ref int offset,
                               ReadOnlySpan<byte> value)
    {
        if (value.Length > destination.Length - offset) return false;
        value.CopyTo(destination[offset..]);
        offset += value.Length;
        return true;
    }

    private static bool Append(Span<byte> destination, ref int offset,
                               ReadOnlySpan<char> value)
    {
        if (value.Length > destination.Length - offset) return false;
        for (int index = 0; index != value.Length; ++index)
            destination[offset++] = (byte)value[index];
        return true;
    }

    private static bool StartsWithAsciiIgnoreCase(ReadOnlySpan<byte> value,
                                                  ReadOnlySpan<byte> prefix)
    {
        return value.Length >= prefix.Length &&
               EqualsAsciiIgnoreCase(value[..prefix.Length], prefix);
    }

    private static bool EqualsAsciiIgnoreCase(ReadOnlySpan<byte> left,
                                              ReadOnlySpan<byte> right)
    {
        if (left.Length != right.Length) return false;
        for (int index = 0; index != left.Length; ++index)
            if (ToLowerAscii(left[index]) != ToLowerAscii(right[index]))
                return false;
        return true;
    }

    private static byte ToLowerAscii(byte value) =>
        value >= (byte)'A' && value <= (byte)'Z'
            ? (byte)(value + ((byte)'a' - (byte)'A')) : value;

    private static bool SetFailure(out ManagedHttpsUrlParseFailureReason failure,
                                   ManagedHttpsUrlParseFailureReason value)
    {
        failure = value;
        return false;
    }
}
