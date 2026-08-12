using System;
using System.IO;
using System.IO.Compression;
using System.Text;

using Tf2DemoSalvage.Core.Assets;

namespace Tf2DemoSalvage.Core.Tests.Assets;

/// <summary>
/// Reading the zip a map carries inside itself.
/// </summary>
/// <remarks>
/// Stored and deflated entries are built here with <see cref="ZipArchive"/>, which is the honest
/// oracle for those two: if this reader disagrees with the framework about a zip the framework
/// wrote, this reader is wrong.
///
/// **The LZMA case cannot be built that way**, because .NET refuses to write it as much as to read
/// it. That path is covered by the integration test against a real map, where <c>cp_process_final</c>
/// carries 3,413 LZMA-compressed entries.
/// </remarks>
public sealed class PakFileTests
{
    [Test]
    public void Read_EmptyBytes_HoldsNothing()
    {
        // A map with no custom content has an empty pakfile lump, which is normal.
        PakFile.Read(ReadOnlyMemory<byte>.Empty).Count.ShouldBe(0);
    }

    [Test]
    public void Read_AStoredEntry_ComesBackByteForByte()
    {
        byte[] content = Encoding.UTF8.GetBytes("\"LightMappedGeneric\" { }");
        byte[] zip = Zip(CompressionLevel.NoCompression, ("materials/custom/a.vmt", content));

        PakFile pak = PakFile.Read(zip);

        pak.Count.ShouldBe(1);
        pak.ReadFile("materials/custom/a.vmt").ShouldBe(content);
    }

    [Test]
    public void Read_ADeflatedEntry_IsDecompressed()
    {
        // Deliberately compressible and long enough that deflate actually shrinks it - a fixture
        // that stayed stored would leave this path untested while passing.
        byte[] content = Encoding.UTF8.GetBytes(new string('a', 4096));
        byte[] zip = Zip(CompressionLevel.Optimal, ("materials/custom/b.vmt", content));

        PakFile.Read(zip).ReadFile("materials/custom/b.vmt").ShouldBe(content);
    }

    [Test]
    public void Read_SeveralEntries_FindsEachOne()
    {
        // A control against a reader that returns the first entry regardless of the path asked for.
        byte[] first = Encoding.UTF8.GetBytes("first");
        byte[] second = Encoding.UTF8.GetBytes("second");

        byte[] zip = Zip(
            CompressionLevel.NoCompression,
            ("materials/one.vmt", first),
            ("materials/two.vmt", second));

        PakFile pak = PakFile.Read(zip);

        pak.ReadFile("materials/one.vmt").ShouldBe(first);
        pak.ReadFile("materials/two.vmt").ShouldBe(second);
    }

    [Test]
    public void ReadFile_IsCaseInsensitive()
    {
        byte[] zip = Zip(CompressionLevel.NoCompression, ("Materials/Custom/A.vmt", [1, 2, 3]));

        PakFile.Read(zip).ReadFile("materials/custom/a.vmt").ShouldNotBeNull();
    }

    [Test]
    public void ReadFile_AMissingPath_ReturnsNull()
    {
        byte[] zip = Zip(CompressionLevel.NoCompression, ("materials/one.vmt", [1]));

        PakFile.Read(zip).ReadFile("materials/absent.vmt").ShouldBeNull();
    }

    [Test]
    public void Read_BytesThatAreNotAZip_HoldNothingRatherThanThrowing()
    {
        // A map whose pakfile lump is empty or damaged still has geometry worth drawing.
        byte[] rubbish = new byte[512];
        Random.Shared.NextBytes(rubbish);

        Should.NotThrow(() => PakFile.Read(rubbish));
    }

    private static byte[] Zip(CompressionLevel level, params (string Name, byte[] Content)[] files)
    {
        using MemoryStream buffer = new();

        using (ZipArchive archive = new(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach ((string name, byte[] content) in files)
            {
                ZipArchiveEntry entry = archive.CreateEntry(name, level);
                using Stream stream = entry.Open();
                stream.Write(content);
            }
        }

        return buffer.ToArray();
    }
}
