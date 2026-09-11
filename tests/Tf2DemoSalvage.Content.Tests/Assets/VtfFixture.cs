using System;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>A minimal version 7.2 VTF: the 64-byte header, then the image data exactly as given.</summary>
/// <remarks>
/// **No resource table and no thumbnail** (`lowResImageFormat` is -1), so the images start right after
/// the header — the layout `VtfTexture` computes for a 7.2 file. The caller writes the mips smallest
/// first, as the format stores them.
/// </remarks>
internal static class VtfFixture
{
    private const int HeaderSize = 64;

    /// <summary>Builds the file.</summary>
    /// <param name="format">The pixel format the header declares.</param>
    /// <param name="width">The header's width.</param>
    /// <param name="height">The header's height.</param>
    /// <param name="mips">How many mip levels the header declares.</param>
    /// <param name="images">Every level's bytes, smallest first.</param>
    /// <returns>The file's bytes.</returns>
    public static byte[] Build(VtfFormat format, int width, int height, int mips, byte[] images)
    {
        byte[] file = new byte[HeaderSize + images.Length];

        file[0] = (byte)'V';
        file[1] = (byte)'T';
        file[2] = (byte)'F';
        file[3] = 0;

        WriteInt(file, 12, HeaderSize);
        WriteShort(file, 16, width);
        WriteShort(file, 18, height);
        WriteInt(file, 20, 0);
        WriteShort(file, 24, 1);
        WriteInt(file, 52, (int)format);
        file[56] = (byte)mips;
        WriteInt(file, 57, -1);

        images.CopyTo(file, HeaderSize);
        return file;
    }

    private static void WriteInt(byte[] into, int at, int value) =>
        BitConverter.GetBytes(value).CopyTo(into, at);

    private static void WriteShort(byte[] into, int at, int value) =>
        BitConverter.GetBytes((ushort)value).CopyTo(into, at);
}
