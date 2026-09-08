using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using GuideXOS.Net10.ManagedKernel;

internal static class Program
{
    private static int s_cases;

    private static int Main()
    {
        try
        {
            SignatureAndCrc();
            FiltersAndPixels();
            BoundsAndFormats();
            LongGcmAgainstPlatform();
            StoreHandles();
            LayoutPaintRaster();
            Console.WriteLine($"MANAGED_KERNEL_PHASE52_HOST_TESTS_PASS cases={s_cases}");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"MANAGED_KERNEL_PHASE52_HOST_TESTS_FAIL cases={s_cases} error={error}");
            return 1;
        }
    }

    private static void SignatureAndCrc()
    {
        byte[] png = BuildPng(3, 5, 6, includeAllFilters: true, idatSplits: 3);
        ManagedPageImageStore store = new();
        ManagedPngDecoder decoder = Decode(png, store, 7, 1);
        Check(decoder.IsComplete && decoder.Width == 3 && decoder.Height == 5,
              "valid-fragmented-png");
        Check(decoder.IdatChunkCount == 3 && decoder.FilterNone == 1 && decoder.FilterSub == 1 &&
              decoder.FilterUp == 1 && decoder.FilterAverage == 1 && decoder.FilterPaeth == 1,
              "idat-and-filter-telemetry");
        byte[] expectedHash = SHA256.HashData(ExpectedPixels(3, 5, 6));
        byte[] actualHash = new byte[32];
        Check(decoder.TryCopyDecodedPixelDigest(actualHash) &&
              CryptographicOperations.FixedTimeEquals(actualHash, expectedHash),
              "decoded-pixel-hash");

        byte[] badSignature = (byte[])png.Clone();
        badSignature[0] ^= 1;
        Check(DecodeFailure(badSignature, ManagedPngFailureReason.SignatureMismatch),
              "signature-rejection");
        byte[] badCrc = (byte[])png.Clone();
        badCrc[29] ^= 1;
        Check(DecodeFailure(badCrc, ManagedPngFailureReason.InvalidChunkCrc),
              "crc-rejection");
        Check(DecodeFailure(png.AsSpan(0, png.Length - 2).ToArray(),
                            ManagedPngFailureReason.Truncated), "truncated-rejection");
    }

    private static void FiltersAndPixels()
    {
        foreach (int colorType in new[] { 2, 6, 0, 4 })
        {
            byte[] png = BuildPng(3, 5, colorType, includeAllFilters: true, idatSplits: 2);
            ManagedPageImageStore store = new();
            ManagedPngDecoder decoder = Decode(png, store, 1, 1);
            Check(decoder.IsComplete && decoder.ColorType == colorType,
                  "supported-color-type-" + colorType);
            Check(decoder.DecodedPixels == 15, "decoded-pixel-count-" + colorType);
            uint first = 0;
            bool found = store.TryGetForSourceNode(1, out ManagedImageHandle handle);
            Check(found, "canonical-alpha-handle-" + colorType +
                  " count=" + store.Count + " generation=" + store.Generation);
            bool read = found && store.TryReadPixel(handle, 0, 0, out first);
            Check(read, "canonical-alpha-read-" + colorType +
                  " value=" + first.ToString("X8") +
                  " arena=" + store.PixelArena[0].ToString("X8"));
            Check((first >> 24) == (colorType == 6 || colorType == 4 ? 128 : 255),
                  "canonical-alpha-" + colorType + " value=" + first.ToString("X8") +
                  " arena=" + store.PixelArena[0].ToString("X8"));
        }

        byte[] invalidFilter = BuildPng(1, 1, 6, false, 1, filterOverride: 5);
        ManagedPngFailureReason invalidFilterReason = DecodeReason(invalidFilter);
        Check(invalidFilterReason == ManagedPngFailureReason.InvalidFilter,
              "invalid-filter actual=" + invalidFilterReason);
    }

    private static void BoundsAndFormats()
    {
        byte[] oversized = BuildPng(257, 1, 6, false, 1);
        Check(DecodeFailure(oversized, ManagedPngFailureReason.InvalidDimensions),
              "maximum-dimension-rejection");
        byte[] indexed = BuildPng(1, 1, 3, false, 1);
        Check(DecodeFailure(indexed, ManagedPngFailureReason.UnsupportedColorType),
              "indexed-rejection");
        byte[] sixteen = BuildPng(1, 1, 6, false, 1, bitDepth: 16);
        Check(DecodeFailure(sixteen, ManagedPngFailureReason.UnsupportedBitDepth),
              "bit-depth-rejection");

        byte[] tooMuch = BuildPng(1, 1, 6, false, 1, extraInflatedByte: true);
        ManagedPngFailureReason tooMuchReason = DecodeReason(tooMuch);
        Check(tooMuchReason == ManagedPngFailureReason.DecodedSizeMismatch,
              "decompression-bomb-rejection actual=" + tooMuchReason);
    }

    private static void StoreHandles()
    {
        ManagedPageImageStore store = new(2, 4, 2, 2);
        Check(store.TryReserve(3, 2, 2, out ManagedImageHandle first), "store-reserve");
        Check(store.TryWritePixel(first, 0, 0xFFFF0000U), "store-write");
        Check(store.TryComplete(first, new byte[32]), "store-complete");
        Check(store.TryGetForSourceNode(3, out ManagedImageHandle found) && found == first,
              "source-node-association");
        store.Reset();
        Check(!store.TryReadPixel(first, 0, 0, out _), "stale-generation-rejection");
        Check(store.TryReserve(4, 1, 1, out _), "store-reuse");
        Check(!store.TryReserve(5, 2, 2, out _) || store.PixelUsed <= store.PixelBudget,
              "pixel-budget-bound");
    }

    private static void LongGcmAgainstPlatform()
    {
        byte[] key = new byte[16];
        byte[] nonce = new byte[12];
        byte[] aad = new byte[] { 0, 0, 0, 0, 0, 0, 0, 1, 23, 3, 3, 1, 163 };
        byte[] plaintext = new byte[419];
        for (int index = 0; index != plaintext.Length; ++index)
            plaintext[index] = (byte)(index * 37 + 11);
        byte[] ciphertext = new byte[plaintext.Length];
        byte[] tag = new byte[16];
        Check(ManagedAesGcm.TryEncrypt(key, nonce, aad, plaintext, ciphertext, tag),
              "long-gcm-managed-encrypt");
        byte[] expectedCiphertext = new byte[plaintext.Length];
        byte[] expectedTag = new byte[16];
        using (AesGcm aes = new(key, 16))
            aes.Encrypt(nonce, plaintext, expectedCiphertext, expectedTag, aad);
        Check(CryptographicOperations.FixedTimeEquals(ciphertext, expectedCiphertext) &&
              CryptographicOperations.FixedTimeEquals(tag, expectedTag),
              "long-gcm-platform-match");
        byte[] recovered = new byte[plaintext.Length];
        Check(ManagedAesGcm.TryDecrypt(key, nonce, aad, ciphertext, tag, recovered) &&
              CryptographicOperations.FixedTimeEquals(recovered, plaintext),
              "long-gcm-managed-decrypt");
    }

    private static void LayoutPaintRaster()
    {
        ManagedHtmlTreeBuilder builder = Parse("<html><body><img id='proof' src='/image.png'></body></html>");
        ManagedHtmlNodeHandle imageNode = FindTag(builder.Document.DocumentNode, builder.Document,
                                                   ManagedHtmlTag.Img);
        Check(imageNode != ManagedHtmlNodeHandle.Invalid, "image-node-discovered");
        ManagedPageImageStore store = new();
        ManagedPngDecoder decoder = Decode(BuildPng(3, 5, 6, true, 3), store, imageNode.Index);
        Check(decoder.IsComplete, "image-decoder-integration");
        ManagedCssEngine css = new(builder.Document);
        Check(css.TryStyle(), "image-style");
        ManagedLayoutEngine layout = new(builder.Document, css,
                                         ManagedLayoutArenaOptions.Default, null, store);
        Check(layout.TryLayout(40, 20) && layout.TryGetBoxForNode(imageNode, out int boxIndex) &&
              layout.TryGetBox(boxIndex, out ManagedLayoutBox box) &&
              box.BorderBox.Width == 3 && box.BorderBox.Height == 5,
              "intrinsic-layout");
        ManagedPaintEngine paint = new(layout, ManagedPaintArenaOptions.Default, null, store);
        Check(paint.TryGenerate(40, 20) && paint.ImageCommands == 1 &&
              paint.ImagePlaceholderCommands == 0, "resolved-image-command");
        uint[] framebufferStorage = new uint[40 * 20];
        ManagedSoftwareRasterizer rasterizer = new();
        Check(rasterizer.TryRender(paint, new ManagedFramebuffer(framebufferStorage, 40, 20)),
              "resolved-image-raster");
        bool hasImagePixel = false;
        for (int index = 0; index != framebufferStorage.Length; ++index)
            if (framebufferStorage[index] == 0x80800000U || framebufferStorage[index] == 0xFF000000U)
            {
                hasImagePixel = true;
                break;
            }
        Check(framebufferStorage[0] == 0xFF000080U || hasImagePixel, "image-pixels-present");

        ManagedLayoutEngine placeholderLayout = new(builder.Document, css);
        Check(placeholderLayout.TryLayout(40, 20), "placeholder-layout");
        ManagedPaintEngine placeholderPaint = new(placeholderLayout);
        Check(placeholderPaint.TryGenerate(40, 20) && placeholderPaint.ImagePlaceholderCommands == 1,
              "placeholder-preserved");
    }

    private static ManagedPngDecoder Decode(byte[] bytes, ManagedPageImageStore store,
                                            int sourceNode, int segmentation = 17)
    {
        ManagedPngDecoder decoder = new(store);
        decoder.Start(sourceNode);
        for (int offset = 0; offset != bytes.Length;)
        {
            int length = Math.Min(segmentation, bytes.Length - offset);
            ManagedHttpBodySinkResult consumed = decoder.Consume(bytes.AsSpan(offset, length));
            Check(consumed == ManagedHttpBodySinkResult.Continue,
                  $"png-consume offset={offset} failure={decoder.PngFailureReason}");
            offset += length;
        }
        Check(decoder.Complete(), "png-complete");
        return decoder;
    }

    private static bool DecodeFailure(byte[] bytes, ManagedPngFailureReason expected)
    {
        return DecodeReason(bytes) == expected;
    }

    private static ManagedPngFailureReason DecodeReason(byte[] bytes)
    {
        ManagedPageImageStore store = new();
        ManagedPngDecoder decoder = new(store);
        decoder.Start(1);
        ManagedHttpBodySinkResult result = decoder.Consume(bytes);
        bool consumedFailure = result == ManagedHttpBodySinkResult.Fail;
        if (!consumedFailure) decoder.Complete();
        return store.Count == 0 ? decoder.PngFailureReason : ManagedPngFailureReason.StoreFailure;
    }

    private static byte[] BuildPng(int width, int height, int colorType,
                                   bool includeAllFilters, int idatSplits,
                                   int filterOverride = -1, int bitDepth = 8,
                                   bool extraInflatedByte = false)
    {
        int bpp = colorType switch { 2 => 3, 6 => 4, 0 => 1, 4 => 2, _ => 1 };
        int rowBytes = checked(width * bpp);
        List<byte> inflated = new();
        for (int y = 0; y != height; ++y)
        {
            int filter = filterOverride >= 0 ? filterOverride :
                (includeAllFilters ? y % 5 : 0);
            inflated.Add((byte)filter);
            byte[] reconstructed = ReconstructedRow(width, y, colorType);
            byte[] previous = y == 0 ? new byte[rowBytes] : ReconstructedRow(width, y - 1, colorType);
            for (int index = 0; index != rowBytes; ++index)
            {
                int left = index >= bpp ? reconstructed[index - bpp] : 0;
                int up = previous[index];
                int upLeft = index >= bpp ? previous[index - bpp] : 0;
                int predictor = filter switch
                {
                    0 => 0,
                    1 => left,
                    2 => up,
                    3 => (left + up) / 2,
                    4 => Paeth(left, up, upLeft),
                    _ => 0
                };
                inflated.Add((byte)(reconstructed[index] - predictor));
            }
        }
        if (extraInflatedByte) inflated.Add(0);
        byte[] zlib = Zlib(inflated.ToArray());
        using MemoryStream output = new();
        output.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        byte[] ihdr = new byte[13];
        Put32(ihdr, 0, (uint)width); Put32(ihdr, 4, (uint)height);
        ihdr[8] = (byte)bitDepth; ihdr[9] = (byte)colorType;
        WriteChunk(output, "IHDR"u8, ihdr);
        int splitLength = Math.Max(1, (zlib.Length + idatSplits - 1) / idatSplits);
        for (int offset = 0; offset != zlib.Length;)
        {
            int length = Math.Min(splitLength, zlib.Length - offset);
            WriteChunk(output, "IDAT"u8, zlib.AsSpan(offset, length));
            offset += length;
        }
        WriteChunk(output, "IEND"u8, ReadOnlySpan<byte>.Empty);
        return output.ToArray();
    }

    private static byte[] ReconstructedRow(int width, int y, int colorType)
    {
        int bpp = colorType switch { 2 => 3, 6 => 4, 0 => 1, 4 => 2, _ => 1 };
        byte[] row = new byte[width * bpp];
        for (int x = 0; x != width; ++x)
        {
            int offset = x * bpp;
            byte red = (byte)(x == 0 && y == 0 ? 0x80 : 0x20 + x * 17 + y * 9);
            if (colorType == 2 || colorType == 6)
            {
                row[offset] = red; row[offset + 1] = (byte)(0x40 + y * 13);
                row[offset + 2] = (byte)(0x60 + x * 11);
                if (colorType == 6) row[offset + 3] = (byte)(x == 0 && y == 0 ? 0x80 : 0xFF);
            }
            else
            {
                row[offset] = red;
                if (colorType == 4) row[offset + 1] = (byte)(x == 0 && y == 0 ? 0x80 : 0xFF);
            }
        }
        return row;
    }

    private static byte[] ExpectedPixels(int width, int height, int colorType)
    {
        using MemoryStream output = new();
        for (int y = 0; y != height; ++y)
        {
            byte[] row = ReconstructedRow(width, y, colorType);
            int bpp = colorType switch { 2 => 3, 6 => 4, 0 => 1, 4 => 2, _ => 1 };
            for (int x = 0; x != width; ++x)
            {
                int offset = x * bpp;
                byte r = row[offset];
                byte g = colorType == 0 || colorType == 4 ? r : row[offset + 1];
                byte b = colorType == 0 || colorType == 4 ? r : row[offset + 2];
                byte a = colorType == 6 || colorType == 4 ? row[offset + bpp - 1] : (byte)255;
                output.WriteByte(a); output.WriteByte(r); output.WriteByte(g); output.WriteByte(b);
            }
        }
        return output.ToArray();
    }

    private static byte[] Zlib(byte[] plain)
    {
        using MemoryStream compressed = new();
        compressed.WriteByte(0x78); compressed.WriteByte(0x9C);
        using (DeflateStream deflate = new(compressed, CompressionLevel.Optimal, true))
            deflate.Write(plain);
        uint a = 1, b = 0;
        foreach (byte value in plain) { a = (a + value) % 65521; b = (b + a) % 65521; }
        uint adler = (b << 16) | a;
        compressed.WriteByte((byte)(adler >> 24)); compressed.WriteByte((byte)(adler >> 16));
        compressed.WriteByte((byte)(adler >> 8)); compressed.WriteByte((byte)adler);
        return compressed.ToArray();
    }

    private static void WriteChunk(Stream stream, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Write32(stream, (uint)data.Length);
        stream.Write(type);
        stream.Write(data);
        uint crc = 0xFFFFFFFFU;
        foreach (byte value in type) crc = Crc(crc, value);
        foreach (byte value in data) crc = Crc(crc, value);
        Write32(stream, ~crc);
    }

    private static uint Crc(uint crc, byte value)
    {
        crc ^= value;
        for (int bit = 0; bit != 8; ++bit)
            crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320U : crc >> 1;
        return crc;
    }

    private static void Write32(Stream stream, uint value)
    {
        stream.WriteByte((byte)(value >> 24)); stream.WriteByte((byte)(value >> 16));
        stream.WriteByte((byte)(value >> 8)); stream.WriteByte((byte)value);
    }

    private static void Put32(byte[] value, int offset, uint item)
    {
        value[offset] = (byte)(item >> 24); value[offset + 1] = (byte)(item >> 16);
        value[offset + 2] = (byte)(item >> 8); value[offset + 3] = (byte)item;
    }

    private static ManagedHtmlTreeBuilder Parse(string html)
    {
        ManagedHtmlTreeBuilder builder = new();
        ManagedHtmlTokenizer tokenizer = new();
        List<uint> scalars = new();
        foreach (char value in html) scalars.Add(value);
        for (int offset = 0; offset != scalars.Count;)
        {
            int length = Math.Min(19, scalars.Count - offset);
            uint[] window = new uint[length];
            for (int index = 0; index != length; ++index) window[index] = scalars[offset + index];
            Check(tokenizer.AppendInput(window), "html-input");
            Check(tokenizer.Pump(builder) != ManagedHtmlTokenizerProcessResult.Failed, "html-pump");
            offset += length;
        }
        Check(tokenizer.Pump(builder, true) == ManagedHtmlTokenizerProcessResult.Complete &&
              builder.Complete(), "html-complete");
        return builder;
    }

    private static ManagedHtmlNodeHandle FindTag(ManagedHtmlNodeHandle node,
                                                  ManagedHtmlDocument document,
                                                  ManagedHtmlTag tag)
    {
        if (document.GetElementTag(node) == tag) return node;
        ManagedHtmlNodeHandle child = document.GetFirstChild(node);
        while (!child.IsInvalid)
        {
            ManagedHtmlNodeHandle result = FindTag(child, document, tag);
            if (!result.IsInvalid) return result;
            child = document.GetNextSibling(child);
        }
        return ManagedHtmlNodeHandle.Invalid;
    }

    private static int Paeth(int left, int up, int upLeft)
    {
        int p = left + up - upLeft;
        int pa = Math.Abs(p - left), pb = Math.Abs(p - up), pc = Math.Abs(p - upLeft);
        return pa <= pb && pa <= pc ? left : pb <= pc ? up : upLeft;
    }

    private static void Check(bool condition, string name)
    {
        ++s_cases;
        if (!condition) throw new InvalidOperationException(name);
    }
}
