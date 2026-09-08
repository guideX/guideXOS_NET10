using System;

namespace GuideXOS.Net10.ManagedKernel;

// The Phase 47-50 host projects compile the pre-image layout/paint slice
// directly instead of referencing the complete ManagedKernel project.  Keep
// that historical slice buildable while the production project supplies the
// full image store and PNG decoder.
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
    public override bool Equals(object? obj) => obj is ManagedImageHandle other &&
        Equals(other);
    public override int GetHashCode() => HashCode.Combine(Slot, Generation);
}

public readonly struct ManagedPageImageDescriptor
{
    public int SourceNodeIndex { get; }
    public int Width { get; }
    public int Height { get; }
    public int PixelCount { get; }

    internal ManagedPageImageDescriptor(int sourceNodeIndex, int width, int height,
                                        int pixelCount)
    {
        SourceNodeIndex = sourceNodeIndex;
        Width = width;
        Height = height;
        PixelCount = pixelCount;
    }
}

public sealed class ManagedPageImageStore
{
    public bool TryGetForSourceNode(int sourceNodeIndex, out ManagedImageHandle handle)
    {
        handle = ManagedImageHandle.Invalid;
        return false;
    }

    public bool TryGetDescriptor(ManagedImageHandle handle,
                                 out ManagedPageImageDescriptor descriptor)
    {
        descriptor = default;
        return false;
    }

    public bool TryReadPixel(ManagedImageHandle handle, int x, int y, out uint pixel)
    {
        pixel = 0;
        return false;
    }
}
