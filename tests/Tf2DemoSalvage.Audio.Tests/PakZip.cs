using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace Tf2DemoSalvage.Audio.Tests;

/// <summary>A zip of the given files, as a map's pakfile holds them.</summary>
internal static class PakZip
{
    /// <summary>Zips the files, each at its path.</summary>
    /// <param name="files">Path to bytes.</param>
    /// <returns>The zip.</returns>
    public static byte[] Of(Dictionary<string, byte[]> files)
    {
        using MemoryStream stream = new();

        using (ZipArchive archive = new(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach ((string path, byte[] bytes) in files)
            {
                using Stream entry = archive.CreateEntry(path).Open();
                entry.Write(bytes);
            }
        }

        return stream.ToArray();
    }
}
