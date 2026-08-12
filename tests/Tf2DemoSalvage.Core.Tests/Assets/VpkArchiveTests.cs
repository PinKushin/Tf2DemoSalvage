using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using Tf2DemoSalvage.Core.Assets;

namespace Tf2DemoSalvage.Core.Tests.Assets;

/// <summary>
/// Reading the archives the game keeps its materials in.
/// </summary>
/// <remarks>
/// The fixtures are built here rather than taken from the game, so these run without a TF2 install
/// and in CI. Three shapes get their own test because each fails **silently** — the archive reads,
/// the lookup misses, and the caller concludes the file is simply not there:
///
/// - a folder of <c>" "</c>, which is the archive root and not a directory named space;
/// - preload bytes, which are part of the file and come before the archived part;
/// - archive index <c>0x7FFF</c>, which means the directory file itself.
/// </remarks>
public sealed class VpkArchiveTests
{
    [Test]
    public void Read_NotAVpk_IsRefused()
    {
        Should.Throw<InvalidDataException>(() => VpkArchive.Read(new byte[64]));
    }

    [Test]
    public void Read_ListsEveryFileWithItsFullPath()
    {
        byte[] vpk = Vpk(
            new VpkFile("vmt", "materials/concrete", "concretefloor007b", 1, 0u, 100u, []),
            new VpkFile("vtf", "materials/metal", "metalwall001", 2, 100u, 200u, []));

        VpkArchive archive = VpkArchive.Read(vpk);

        archive.Count.ShouldBe(2);
        archive.TryFind("materials/concrete/concretefloor007b.vmt", out _).ShouldBeTrue();
        archive.TryFind("materials/metal/metalwall001.vtf", out _).ShouldBeTrue();
    }

    [Test]
    public void Read_ARootFolderIsASpace()
    {
        // " " is the archive root. Read literally it produces a path of " /name.ext", which never
        // matches anything and looks exactly like a missing file.
        byte[] vpk = Vpk(new VpkFile("txt", " ", "readme", 1, 0u, 10u, []));

        VpkArchive archive = VpkArchive.Read(vpk);

        archive.TryFind("readme.txt", out _).ShouldBeTrue();
    }

    [Test]
    public void Find_IsCaseInsensitiveAndSlashAgnostic()
    {
        // Material names come out of a BSP compiled on someone else's machine, in whatever case
        // and separator that machine used.
        byte[] vpk = Vpk(new VpkFile("vmt", "materials/concrete", "floor", 1, 0u, 10u, []));

        VpkArchive archive = VpkArchive.Read(vpk);

        archive.TryFind("MATERIALS/CONCRETE/FLOOR.VMT", out _).ShouldBeTrue();
        archive.TryFind("materials\\concrete\\floor.vmt", out _).ShouldBeTrue();
    }

    [Test]
    public void ReadFile_APreloadOnlyEntry_ReturnsThePreloadBytes()
    {
        // A small file can live entirely in the directory with a length of zero. Reading only the
        // archived part returns an empty file - and an empty VMT parses fine and draws nothing.
        byte[] preload = [1, 2, 3, 4, 5];
        byte[] vpk = Vpk(new VpkFile("vmt", "materials", "tiny", 0x7FFF, 0u, 0u, preload));

        VpkArchive archive = VpkArchive.Read(vpk);

        archive.TryFind("materials/tiny.vmt", out VpkEntry entry).ShouldBeTrue();
        entry.Size.ShouldBe(5);
        entry.Preload.ToArray().ShouldBe(preload);
    }

    [Test]
    public void ReadFile_AMissingPath_ReturnsNull()
    {
        VpkArchive archive = VpkArchive.Read(Vpk(new VpkFile("vmt", "materials", "one", 1, 0u, 4u, [])));

        archive.ReadFile("materials/absent.vmt").ShouldBeNull();
    }

    [Test]
    public void Read_AnEntryWithoutItsTerminator_IsRefused()
    {
        // The terminator is how a reader knows it is still aligned with the tree. Without the
        // check, a misread walks off into the data and invents thousands of files.
        byte[] vpk = Vpk(new VpkFile("vmt", "materials", "one", 1, 0u, 4u, []));

        // The terminator sits at the end of the fixed 18-byte entry.
        int at = Array.LastIndexOf(vpk, (byte)0xFF);
        vpk[at] = 0x00;

        Should.Throw<InvalidDataException>(() => VpkArchive.Read(vpk));
    }

    [Test]
    public void Read_AVersionItDoesNotKnow_IsRefused()
    {
        byte[] vpk = Vpk(new VpkFile("vmt", "materials", "one", 1, 0u, 4u, []));
        BinaryPrimitives.WriteUInt32LittleEndian(vpk.AsSpan(4), 9);

        Should.Throw<InvalidDataException>(() => VpkArchive.Read(vpk));
    }

    /// <summary>One file to put in a fixture archive.</summary>
    private sealed record VpkFile(
        string Extension, string Folder, string Name, int Archive, uint Offset, uint Length,
        byte[] Preload);

    /// <summary>Builds a version 2 VPK directory holding the given entries.</summary>
    private static byte[] Vpk(params VpkFile[] files)
    {
        List<byte> tree = [];

        // Grouped the way the format nests: extension, then folder, then name.
        foreach (IGrouping<string, VpkFile> byExtension in
            files.GroupBy(file => file.Extension, StringComparer.Ordinal))
        {
            Write(tree, byExtension.Key);

            foreach (IGrouping<string, VpkFile> byFolder in
                byExtension.GroupBy(file => file.Folder, StringComparer.Ordinal))
            {
                Write(tree, byFolder.Key);

                foreach (VpkFile file in byFolder)
                {
                    Write(tree, file.Name);

                    tree.AddRange(BitConverter.GetBytes(0u));                      // CRC
                    tree.AddRange(BitConverter.GetBytes((ushort)file.Preload.Length));
                    tree.AddRange(BitConverter.GetBytes((ushort)file.Archive));
                    tree.AddRange(BitConverter.GetBytes(file.Offset));
                    tree.AddRange(BitConverter.GetBytes(file.Length));
                    tree.AddRange(BitConverter.GetBytes((ushort)0xFFFF));
                    tree.AddRange(file.Preload);
                }

                tree.Add(0);
            }

            tree.Add(0);
        }

        tree.Add(0);

        byte[] vpk = new byte[28 + tree.Count];
        BinaryPrimitives.WriteUInt32LittleEndian(vpk, 0x55AA1234);
        BinaryPrimitives.WriteUInt32LittleEndian(vpk.AsSpan(4), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(vpk.AsSpan(8), (uint)tree.Count);
        tree.CopyTo(vpk, 28);

        return vpk;
    }

    private static void Write(List<byte> tree, string text)
    {
        tree.AddRange(Encoding.UTF8.GetBytes(text));
        tree.Add(0);
    }
}
