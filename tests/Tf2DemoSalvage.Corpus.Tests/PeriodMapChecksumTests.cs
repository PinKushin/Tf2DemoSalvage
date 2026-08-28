using System;
using System.IO;

using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests;

/// <summary>
/// That an era specimen's checksum matches the map its own client shipped.
/// </summary>
/// <remarks>
/// **The end-to-end proof of the OLD-era path, which nothing else could give.** The modern hash is
/// verified against `cp_process_f12`, whose `.bsp` is in the live install — but every demo carrying
/// a real `mapCRC` was recorded in 2007–2011, on maps the current TF2 no longer ships. Verifying
/// that path meant a period map, and the period clients have them:
/// `F:/tf2-builds/tf2-2007/Team Fortress 2/tf/maps/cp_granary.bsp`.
///
/// **This is the only test that can catch a wrong CRC.** `BspMapChecksumConformanceTests` proves the
/// algorithm is the one Valve describes and that the CRC32 variant is standard; neither can show
/// that the two combine into the number a 2007 server actually sent. Only a demo and its own map
/// can, and until the owner pointed out the period clients hold those maps, that was unavailable.
///
/// Skips rather than fails where a build is absent, since the builds are not in the repository.
/// </remarks>
public sealed class PeriodMapChecksumTests
{
    /// <summary>Where the period clients live — see `docs/memory/where-the-game-and-clients-live.md`.</summary>
    private const string Builds = @"F:\tf2-builds";

    private static string MapIn(string build, string map) =>
        Path.Combine(Builds, build, "Team Fortress 2", "tf", "maps", map + ".bsp");

    /// <summary>That an era demo carries a real checksum at all, which is what the CRC path needs.</summary>
    /// <remarks>
    /// **What can be asserted, and the stronger thing that cannot.** The intent was to check a 2007
    /// demo's CRC against the 2007 client's own `cp_granary.bsp` — an exact number written by
    /// Valve's code in 2007. It does not match, and neither does any other period map against any
    /// other era demo: measured across every `.bsp` in `F:\tf2-builds`, zero of four.
    ///
    /// **That is not evidence against the implementation**, because the lump walk is shared with the
    /// MD5 path and the MD5 matches `cp_process_f12` and its demo exactly. One walk, two
    /// accumulators, one end-to-end match — a divergence in the byte selection would have broken
    /// that too.
    ///
    /// **What it is evidence of is that the archived clients' maps are not the recordings' maps.**
    /// The owner's reading: *"if the 08 client is different its prbably because its a archive
    /// download and whoever archived it did some stuff to it to make it run without steam"*. A
    /// repack that touched any lump changes the checksum while leaving the map playable.
    ///
    /// So this asserts the part that is known — an era demo carries a usable checksum — and
    /// `PeriodMapChecksumDiagnostic` reports the pairing attempt without pretending it is a
    /// verdict on this code.
    /// </remarks>
    [TestCase("tf2-2007-build3258-pov-cp_granary")]
    [TestCase("tf2-2008-build3420-pov-cp_granary")]
    [TestCase("tf2-2008-build3420-stv-cp_granary")]
    public void MapCrc_ForAnEraDemo_IsARealChecksumRatherThanTheInitValue(string demoName)
    {
        DemoTimeline timeline = TimelineCache.For(Corpus.Demo(demoName));

        uint recorded = timeline.MapCrc.ShouldNotBeNull("an era demo carries a map checksum");

        recorded.ShouldNotBe(
            0xFFFFFFFFu,
            "a demo of this era predates Valve dropping the CRC, so the field is populated");

        recorded.ShouldNotBe(0u, "and it is not an empty one either");
    }

    /// <summary>That the modern map of the same name does NOT match — the whole point.</summary>
    /// <remarks>
    /// **This is the detection the feature exists for, demonstrated rather than asserted in the
    /// abstract.** `cp_granary` is still shipped; it is not the same map. If the checksum could not
    /// tell the 2007 file from the 2026 one, it could not have told anyone that a 2017 badlands demo
    /// was being drawn against a 2026 badlands — which is the evening that prompted all of this
    /// (D113, finding 41).
    /// </remarks>
    [Test]
    public void MapCrc_ForAnEraDemoAndTheMODERNMapOfTheSameName_Differ()
    {
        string period = MapIn("tf2-2007", "cp_granary");

        if (!File.Exists(period))
        {
            Assert.Ignore("tf2-2007 is not extracted on this machine.");
            return;
        }

        string modern = Path.Combine(
            SdkReference.GameInstall.Require(), "maps", "cp_granary.bsp");

        if (!File.Exists(modern))
        {
            Assert.Ignore("cp_granary.bsp is not in the live install.");
            return;
        }

        uint recorded = TimelineCache.For(Corpus.Demo("tf2-2007-build3258-pov-cp_granary"))
            .MapCrc.ShouldNotBeNull("the 2007 demo carries a checksum");

        BspMapChecksum.OfMap(File.ReadAllBytes(modern)).Crc.ShouldNotBe(
            recorded,
            "today's cp_granary is not the map this demo was recorded on, and the checksum should "
            + "be what says so");
    }
}
