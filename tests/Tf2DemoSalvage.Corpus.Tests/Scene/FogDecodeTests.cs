using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// The atmosphere every corpus demo carries, read end to end.
/// </summary>
/// <remarks>
/// **This class was written asserting the opposite and that is why B132 was found.** Its first
/// version measured a fog controller reaching the entity table with zero properties on every demo
/// in the corpus, said so as an assertion, and told a future reader in its own failure message to
/// replace it rather than relax it once the number changed. The cause was upstream and had nothing
/// to do with fog: <c>EntityStateTable.Apply</c> read <c>DecodedEntity.Properties</c>, which is what
/// the wire carried, where it wanted the entity's state — and an entering entity is a delta against
/// its class's instance baseline, so an entity whose whole state equals its baseline arrives
/// carrying nothing at all. Nineteen of one demo's 195 entities were empty that way.
///
/// **The values are pinned exactly, and they were confirmed from outside this project.** Each map's
/// own <c>env_fog_controller</c> keyvalues — the numbers a mapper typed into Hammer, read straight
/// out of the BSP entity lump by <c>MapFogProbe</c> in Content.Tests — match what the demo networks:
/// granary 225/225/225 to 14000, viaduct 213/174/221 to 6500, foundry 131/121/134 from 1707 to
/// 4634. Nothing this project wrote connects those two paths, so the agreement is evidence about
/// the decode rather than evidence that a fixture agrees with the code that produced it.
///
/// **Viaduct is the specimen that fixes the colour byte order.** A <c>color32</c> arrives as one
/// 32-bit int, and reading it reversed is the plausible failure; a grey map cannot tell the two
/// readings apart and viaduct's 213/174/221 can.
/// </remarks>
public sealed class FogDecodeTests
{
    [Test]
    public void Fog_AcrossTheCorpus_IsDecodedFromEveryDemo()
    {
        List<string> paths = [.. Corpus.FilesWithSchema()];
        List<DemoTimeline> timelines = [.. paths.Select(TimelineCache.For)];

        foreach ((string path, DemoTimeline timeline) in paths.Zip(timelines))
        {
            TestContext.Out.WriteLine(
                $"{Path.GetFileName(path)}: {timeline.FogControllersSeen} sightings, " +
                $"{timeline.FogControllerProperties} properties, " +
                $"{timeline.FogSamples.Count} fog samples");
        }

        // **A controller holds fifteen properties, not "some".** That is the count its send table
        // declares and the count a trace of any corpus demo prints, so a merge that dropped one
        // would show here rather than as a value quietly taking its default.
        timelines.Select(timeline => timeline.FogControllerProperties).Distinct()
            .ShouldBe([15]);

        // Every demo, every era: protocols 11 through 24 all carry a fog controller and all decode.
        timelines.Count(timeline => timeline.FogSamples.Count > 0).ShouldBe(timelines.Count);
    }

    [Test]
    public void Fog_OnTheViaductRecordings_MatchesTheMapsOwnKeyvalues()
    {
        // **The map is the independent source and viaduct is the discriminating one.** Hammer's
        // fogcolor for koth_viaduct is "213 174 221" — three distinct bytes, so red-in-the-low-byte
        // is proved by this rather than assumed. 14528213 is 0xDDAED5.
        SceneFog fog = OnlyFog("tf2-2011-build4604-stv-koth_viaduct.dem");

        fog.Start.ShouldBe(0f);
        fog.End.ShouldBe(6500f);
        fog.MaxDensity.ShouldBe(1f);

        fog.Red.ShouldBe(213f / 255f);
        fog.Green.ShouldBe(174f / 255f);
        fog.Blue.ShouldBe(221f / 255f);
    }

    [Test]
    public void Fog_OnAPovAndStvOfOneSession_IsIdentical()
    {
        // **Two recordings of one match, made by different clients**, which is the control this
        // corpus was built to provide. A POV demo is written by a player's client and a SourceTV
        // demo by the relay; they share a server and nothing else. Fog that decoded from an
        // artefact of one writer could not survive being read out of both.
        SceneFog pov = OnlyFog("tf2-2008-build3420-pov-cp_granary.dem");
        SceneFog stv = OnlyFog("tf2-2008-build3420-stv-cp_granary.dem");

        pov.ShouldBe(stv);

        // And what cp_granary's own env_fog_controller says: 225 grey, 0 to 14000, density 0.8.
        stv.End.ShouldBe(14000f);
        stv.MaxDensity.ShouldBe(0.8f);
        stv.Red.ShouldBe(225f / 255f);
    }

    [Test]
    public void Fog_OnFoundry_KeepsTheMapsNonRoundStartAndEnd()
    {
        // **A range that starts well away from the camera, which the other specimens do not test.**
        // granary and viaduct both start at 0, so a reader that ignored m_fog.start entirely would
        // pass on either. cp_foundry starts at 1707 and ends at 4634 — numbers no default produces.
        SceneFog fog = OnlyFog("tf2-2013-build1729296-stv-cp_foundry.dem");

        fog.Start.ShouldBe(1707f);
        fog.End.ShouldBe(4634f);
        fog.MaxDensity.ShouldBe(0.7f);

        fog.Red.ShouldBe(131f / 255f);
        fog.Green.ShouldBe(121f / 255f);
        fog.Blue.ShouldBe(134f / 255f);
    }

    /// <summary>The single fog state a demo settles on, or a skip when the demo is absent.</summary>
    /// <remarks>
    /// Through <c>Corpus.Demo</c> so a missing file skips with a reason rather than throwing out of
    /// <c>First</c>. Every corpus demo records exactly one fog sample: a map's controller sends its
    /// whole state on entry and never speaks again, so a keyframe list of length one is the correct
    /// answer rather than a truncation — and asserting the count is what would catch a sampler that
    /// recorded a duplicate per tick.
    /// </remarks>
    private static SceneFog OnlyFog(string name)
    {
        DemoTimeline timeline = TimelineCache.For(Corpus.Demo(name));

        timeline.FogSamples.Count.ShouldBe(1);

        return timeline.FogSamples[0].Fog;
    }
}
