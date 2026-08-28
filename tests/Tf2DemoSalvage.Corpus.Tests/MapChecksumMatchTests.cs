using System;
using System.IO;

using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Core.Tests;

/// <summary>
/// That a demo's map checksum matches the map file we would draw it against.
/// </summary>
/// <remarks>
/// **The whole point of the checksum, and the test that proves the implementation.** `CRC_MapFile`
/// can be transcribed correctly in isolation and still disagree with the engine over which bytes it
/// covers, in what order, or whether they are decompressed. Only a real demo and its real map can
/// settle that, because the demo carries a number computed by Valve's own code.
///
/// **`cp_process_f12` is the pair that must agree.** The `.bsp` was put in the TF2 install
/// deliberately so the real client can play these demos, so the map on disk IS the map they were
/// recorded on — which makes a mismatch here a defect in this project rather than a fact about the
/// corpus.
/// </remarks>
public sealed class MapChecksumMatchTests
{
    private static string MapPath(string name) =>
        Path.Combine(GameInstall.Require(), "maps", name + ".bsp");

    /// <summary>That the f12 demo and the f12 map agree, to the bit.</summary>
    /// <remarks>
    /// **A single exact number, and there is nothing to approximate.** Either every lump but the
    /// entities was hashed, in header order, over raw bytes — or the answer is some other number
    /// entirely. There is no partial credit in a checksum, which is what makes this a decisive test
    /// of the whole algorithm rather than of any one part.
    /// </remarks>
    [Test]
    public void MapHash_ForTheF12DemoAndTheF12Map_Agree()
    {
        string demo = Corpus.Demo("cp_process_f12");
        string map = MapPath("cp_process_f12");

        if (!File.Exists(map))
        {
            Assert.Ignore("cp_process_f12.bsp is not in this TF2 install.");
            return;
        }

        DemoTimeline timeline = TimelineCache.For(demo);

        // **A modern demo's CRC is dead, so the MD5 is what identifies its map.** Measured across
        // gcor: 2007 through 2011 carry a real `mapCRC` and 2013 onward carry `0xFFFFFFFF`, the
        // CRC32 init value, alongside a real sixteen-byte hash. Asserting the CRC here would be
        // asserting that Valve still computes a field they stopped computing.
        timeline.MapCrc.ShouldBe(
            0xFFFFFFFFu, "a demo of this era carries no map CRC, only a hash");

        byte[] recorded =
            [.. timeline.MapHash.ShouldNotBeNull("a modern demo carries a map hash")];

        recorded.Length.ShouldBe(16, "the modern map hash is an MD5");

        // **Over the same lumps the CRC covers, not over the whole file.** Measured: the MD5 of the
        // `.bsp` as a file is `B18E4159…` where the demo says `DF0D50EF…`, so the selection is the
        // lump walk rather than the bytes on disk.
        byte[] onDisk = BspMapChecksum.OfMap(File.ReadAllBytes(map)).Md5;

        Convert.ToHexString(onDisk).ShouldBe(
            Convert.ToHexString(recorded),
            "the demo was recorded on this map, so the hashes must be identical");
    }

    /// <summary>That a different map gives a different answer — the control.</summary>
    /// <remarks>
    /// **Without this, the test above passes against a checksum that always returns the same
    /// number.** Any other installed map must disagree, and that is the property the whole feature
    /// rests on: a checksum that cannot distinguish two maps cannot detect a mismatch.
    ///
    /// Uses whichever other map is present rather than naming one, so it works on any install.
    /// </remarks>
    [Test]
    public void MapCrc_ForADifferentMap_DiffersFromTheDemos()
    {
        string map = MapPath("cp_process_f12");

        if (!File.Exists(map))
        {
            Assert.Ignore("cp_process_f12.bsp is not in this TF2 install.");
            return;
        }

        uint f12 = BspMapChecksum.OfMap(File.ReadAllBytes(map)).Crc;

        foreach (string other in new[] { "cp_badlands", "koth_harvest_final", "cp_process_final" })
        {
            string path = MapPath(other);

            if (!File.Exists(path))
            {
                continue;
            }

            BspMapChecksum.OfMap(File.ReadAllBytes(path)).Crc.ShouldNotBe(
                f12, $"{other} is a different map from cp_process_f12");

            return;
        }

        Assert.Ignore("no second map is installed to compare against.");
    }

    /// <summary>That the same map read twice gives the same answer.</summary>
    /// <remarks>
    /// **Cheap, and it rules out the one way a checksum can be useless without being wrong.** A
    /// hash that varied between reads — from an uninitialised accumulator, or from hashing a
    /// pointer rather than its contents — would fail every comparison and look exactly like a
    /// mismatched map.
    /// </remarks>
    [Test]
    public void MapCrc_ForTheSameMapTwice_IsStable()
    {
        string map = MapPath("cp_process_f12");

        if (!File.Exists(map))
        {
            Assert.Ignore("cp_process_f12.bsp is not in this TF2 install.");
            return;
        }

        byte[] bytes = File.ReadAllBytes(map);

        BspMapChecksum.OfMap(bytes).Crc.ShouldBe(BspMapChecksum.OfMap(bytes).Crc);
        Convert.ToHexString(BspMapChecksum.OfMap(bytes).Md5)
            .ShouldBe(Convert.ToHexString(BspMapChecksum.OfMap(bytes).Md5));
    }
}
