using System;

namespace GuideXOS.Net10.ManagedKernel;

/// <summary>
/// RFC 2104 / RFC 4231 HMAC-SHA256 over <see cref="ManagedSha256"/>.
/// </summary>
internal sealed class ManagedHmacSha256
{
    internal const int BlockSize = ManagedSha256.BlockSize;
    internal const int DigestSize = ManagedSha256.DigestSize;

    private readonly ManagedSha256 _inner = new();
    private readonly ManagedSha256 _outer = new();
    private readonly byte[] _innerPad = new byte[BlockSize];
    private readonly byte[] _outerPad = new byte[BlockSize];
    private bool _initialized;
    private bool _finalized;

    private ManagedHmacSha256()
    {
    }

    internal bool Initialize(ReadOnlySpan<byte> key)
    {
        Span<byte> keyBlock = stackalloc byte[BlockSize];
        Span<byte> hashedKey = stackalloc byte[DigestSize];
        keyBlock.Clear();
        hashedKey.Clear();

        if (key.Length > BlockSize)
        {
            if (!ManagedSha256.TryHash(key, hashedKey))
            {
                keyBlock.Clear();
                hashedKey.Clear();
                return false;
            }
            hashedKey.CopyTo(keyBlock);
        }
        else
        {
            key.CopyTo(keyBlock);
        }

        for (int index = 0; index != BlockSize; ++index)
        {
            _innerPad[index] = (byte)(keyBlock[index] ^ 0x36);
            _outerPad[index] = (byte)(keyBlock[index] ^ 0x5C);
        }
        keyBlock.Clear();
        hashedKey.Clear();

        _inner.Reset();
        _outer.Reset();
        _initialized = _inner.Append(_innerPad);
        _finalized = false;
        return _initialized;
    }

    internal bool Append(ReadOnlySpan<byte> data)
    {
        return _initialized && !_finalized && _inner.Append(data);
    }

    internal bool TryFinalize(Span<byte> destination)
    {
        Span<byte> innerDigest = stackalloc byte[DigestSize];
        innerDigest.Clear();
        if (!_initialized || _finalized || destination.Length < DigestSize ||
            !_inner.TryFinalize(innerDigest))
        {
            innerDigest.Clear();
            return false;
        }

        _outer.Reset();
        bool success = _outer.Append(_outerPad) &&
            _outer.Append(innerDigest) && _outer.TryFinalize(destination);
        innerDigest.Clear();
        if (success) _finalized = true;
        return success;
    }

    internal void Reset()
    {
        if (!_initialized)
        {
            _finalized = false;
            return;
        }
        _inner.Reset();
        _outer.Reset();
        _inner.Append(_innerPad);
        _finalized = false;
    }

    internal void Clear()
    {
        _inner.Reset();
        _outer.Reset();
        _innerPad.AsSpan().Clear();
        _outerPad.AsSpan().Clear();
        _initialized = false;
        _finalized = false;
    }

    internal static bool TryCreate(ReadOnlySpan<byte> key,
                                   out ManagedHmacSha256? hmac)
    {
        hmac = new ManagedHmacSha256();
        if (!hmac.Initialize(key))
        {
            hmac.Clear();
            hmac = null;
            return false;
        }
        return true;
    }

    internal static bool TryCompute(ReadOnlySpan<byte> key,
                                    ReadOnlySpan<byte> data,
                                    Span<byte> destination)
    {
        if (destination.Length < DigestSize ||
            !TryCreate(key, out ManagedHmacSha256? hmac) || hmac == null)
        {
            return false;
        }
        bool success = hmac.Append(data) && hmac.TryFinalize(destination);
        hmac.Clear();
        return success;
    }
}
