using System.Buffers.Binary;
using HermesDesktop.Core.Rpc;
using HermesDesktop.Core.Storage;
using Xunit;

namespace HermesDesktop.Core.Tests;

public class ClipboardImageHelperTests
{
    [Fact]
    public void WrapRawDibAsBmpIfNeeded_PrependsFileHeaderForBitmapInfoHeader()
    {
        // Minimal 40-byte BITMAPINFOHEADER + 4 bytes of 32bpp pixel data
        var dib = new byte[44];
        BinaryPrimitives.WriteUInt32LittleEndian(dib.AsSpan(0, 4), 40);
        BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(4, 4), 1);
        BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(8, 4), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(dib.AsSpan(12, 2), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(dib.AsSpan(14, 2), 32);

        var bmp = ClipboardImageHelper.WrapRawDibAsBmpIfNeeded(dib);

        Assert.Equal(58, bmp.Length);
        Assert.Equal((byte)'B', bmp[0]);
        Assert.Equal((byte)'M', bmp[1]);
        Assert.Equal(58u, BinaryPrimitives.ReadUInt32LittleEndian(bmp.AsSpan(2, 4)));
        Assert.Equal(54u, BinaryPrimitives.ReadUInt32LittleEndian(bmp.AsSpan(10, 4)));
    }

    [Fact]
    public void WrapRawDibAsBmpIfNeeded_PreservesPngAndBmpSignatures()
    {
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00];
        Assert.Same(png, ClipboardImageHelper.WrapRawDibAsBmpIfNeeded(png));

        byte[] bmp = new byte[54];
        bmp[0] = (byte)'B';
        bmp[1] = (byte)'M';
        Assert.Same(bmp, ClipboardImageHelper.WrapRawDibAsBmpIfNeeded(bmp));
    }

    [Fact]
    public void LocalCacheStore_PersistsLastAttachmentDirectory()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"hermes-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var store = new LocalCacheStore(
                Path.Combine(tempDir, "cache.db"),
                Path.Combine(tempDir, "settings.json"));

            var settings = new AppSettingsData { LastAttachmentDirectory = "/tmp/uploads" };
            store.SaveSettings(settings);

            var loaded = store.LoadSettings();
            Assert.Equal("/tmp/uploads", loaded.LastAttachmentDirectory);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
