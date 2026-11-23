using System.Buffers.Binary;

namespace HermesDesktop.Core.Rpc;

/// <summary>
/// Helpers for normalizing raw clipboard image payloads (such as Win32 CF_DIB / CF_DIBV5)
/// into standard encoded image bytes with proper file headers.
/// </summary>
public static class ClipboardImageHelper
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static bool HasEncodedImageSignature(ReadOnlySpan<byte> data)
    {
        if (data.Length >= PngSignature.Length && data[..PngSignature.Length].SequenceEqual(PngSignature))
            return true;
        if (data.Length >= 2 && data[0] == (byte)'B' && data[1] == (byte)'M')
            return true;
        if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
            return true;
        if (data.Length >= 6 && data[0] == (byte)'G' && data[1] == (byte)'I' && data[2] == (byte)'F')
            return true;
        if (data.Length >= 12 && data[0] == (byte)'R' && data[1] == (byte)'I' && data[2] == (byte)'F' && data[3] == (byte)'F'
            && data[8] == (byte)'W' && data[9] == (byte)'E' && data[10] == (byte)'B' && data[11] == (byte)'P')
            return true;
        return false;
    }

    /// <summary>
    /// If <paramref name="raw"/> is a headerless Win32 DIB (BITMAPINFOHEADER / BITMAPV4HEADER / BITMAPV5HEADER),
    /// prepends a 14-byte BITMAPFILEHEADER so standard decoders (including Avalonia Bitmap) can load it.
    /// Returns <paramref name="raw"/> unchanged if it already has an encoded image signature.
    /// </summary>
    public static byte[] WrapRawDibAsBmpIfNeeded(byte[] raw)
    {
        if (raw is not { Length: >= 40 }) return raw;
        if (HasEncodedImageSignature(raw)) return raw;

        var headerSize = BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(0, 4));
        if (headerSize is not (40 or 52 or 56 or 108 or 124) || headerSize > raw.Length)
        {
            return raw;
        }

        var bitCount = BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(14, 2));
        var compression = BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(16, 4));
        var colorsUsed = BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(32, 4));

        var paletteEntries = 0u;
        if (bitCount <= 8)
        {
            paletteEntries = colorsUsed > 0 ? colorsUsed : (1u << bitCount);
        }

        // BITMAPINFOHEADER (40 bytes) with BI_BITFIELDS (3) stores 3 DWORD color masks right after the header.
        var maskBytes = (headerSize == 40 && compression == 3) ? 12u : 0u;
        const uint fileHeaderSize = 14;
        var pixelOffset = fileHeaderSize + headerSize + maskBytes + (paletteEntries * 4u);
        var totalSize = fileHeaderSize + (uint)raw.Length;

        var bmp = new byte[totalSize];
        bmp[0] = (byte)'B';
        bmp[1] = (byte)'M';
        BinaryPrimitives.WriteUInt32LittleEndian(bmp.AsSpan(2, 4), totalSize);
        BinaryPrimitives.WriteUInt32LittleEndian(bmp.AsSpan(10, 4), pixelOffset);
        Buffer.BlockCopy(raw, 0, bmp, (int)fileHeaderSize, raw.Length);
        return bmp;
    }
}
