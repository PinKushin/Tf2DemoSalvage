using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Content.Tests.Bsp;

/// <summary>
/// The four lightmaps a bump-lit face carries, and the one it does not.
/// </summary>
/// <remarks>
/// **The control matters more than the measurement here, and that is unusual.** Set 0 of a bumped
/// face sits at exactly the byte offset where an unbumped face's only set sits, so a four-set
/// reader whose arithmetic is completely wrong still draws every unbumped face correctly — and
/// unbumped faces are most of the map. A picture would look right. The existing lightmap tests
/// would pass. Only an assertion that the OLD read is byte-identical after the change can see it.
///
/// The offset arithmetic itself is not inferred. <c>vrad</c>'s radial.cpp states it:
///
/// <code>
/// pdata[bumpSample] = &amp;(*pdlightdata)[f->lightofs +
///     (k * bumpSampleCount + bumpSample) * fl->numluxels * 4];
/// </code>
/// </remarks>
public sealed class BumpedLightmapTests
{
    private static string? MapFile
    {
        get
        {
            foreach (string? root in new[]
            {
                Environment.GetEnvironmentVariable("TF2_FOLDER"),
                @"C:\Program Files (x86)\Steam\steamapps\common\Team Fortress 2\tf",
                @"F:\SteamLibrary\steamapps\common\Team Fortress 2\tf",
            })
            {
                if (string.IsNullOrWhiteSpace(root))
                {
                    continue;
                }

                string map = Path.Combine(root, "maps", "cp_process_final.bsp");

                if (File.Exists(map))
                {
                    return map;
                }
            }

            return null;
        }
    }

    [Test]
    public void SetOffset_IsValvesOwnArithmetic()
    {
        // Hand-worked against the line quoted above. A face at byte 1000 with 100 luxels: set 0 of
        // style 0 is the face's own offset, set 1 is one full lightmap along, and style 1 starts
        // after all four sets of style 0.
        BspLightmaps.SetOffset(1000, style: 0, set: 0, luxels: 100, sets: 4).ShouldBe(1000);
        BspLightmaps.SetOffset(1000, style: 0, set: 1, luxels: 100, sets: 4).ShouldBe(1400);
        BspLightmaps.SetOffset(1000, style: 0, set: 3, luxels: 100, sets: 4).ShouldBe(2200);
        BspLightmaps.SetOffset(1000, style: 1, set: 0, luxels: 100, sets: 4).ShouldBe(2600);
    }

    [Test]
    public void SetOffset_OnAnUnbumpedFace_LeavesEveryStyleWhereItAlwaysWas()
    {
        // **The arithmetic has to collapse.** With one set per style the formula must reduce to
        // exactly what the single-set reader has always done, or introducing bump support moves
        // every unbumped face's lighting. This is the case where a stray "* 4" hides.
        BspLightmaps.SetOffset(1000, style: 0, set: 0, luxels: 100, sets: 1).ShouldBe(1000);
        BspLightmaps.SetOffset(1000, style: 1, set: 0, luxels: 100, sets: 1).ShouldBe(1400);
        BspLightmaps.SetOffset(1000, style: 2, set: 0, luxels: 100, sets: 1).ShouldBe(1800);
    }

    [Test]
    public void ReadAll_LeavesTheFlatSetExactlyWhereReadFoundIt()
    {
        // **The control, and the only thing that can catch a wrong set stride.** Every face's flat
        // lightmap must come out byte-identical to what the single-set reader produced, bumped or
        // not. A reader that multiplied the offset by four on unbumped faces too would still draw
        // a plausible map, because the lighting it picked up belongs to some other face.
        if (MapFile is not { } path)
        {
            Assert.Ignore("Team Fortress 2 is not installed; set TF2_FOLDER to run this.");
            return;
        }

        ReadOnlyMemory<byte> map = File.ReadAllBytes(path);

        IReadOnlyList<BspLightmap> flat = BspLightmaps.Read(map);
        IReadOnlyList<BspFaceLighting> all = BspLightmaps.ReadAll(map);

        all.Count.ShouldBe(flat.Count);

        for (int face = 0; face < flat.Count; face++)
        {
            all[face].Flat.Width.ShouldBe(flat[face].Width, $"face {face} width");
            all[face].Flat.Height.ShouldBe(flat[face].Height, $"face {face} height");
            all[face].Flat.Pixels.ToArray().ShouldBe(
                flat[face].Pixels.ToArray(), $"face {face} must light exactly as it always has");
        }
    }

    [Test]
    public void ReadAll_FindsBumpLitFacesAndGivesThemThreeDirectionalSets()
    {
        // The condition has to exist before the measurement means anything: if this map had no
        // bump-lit faces, every assertion about them would hold vacuously.
        if (MapFile is not { } path)
        {
            Assert.Ignore("Team Fortress 2 is not installed; set TF2_FOLDER to run this.");
            return;
        }

        IReadOnlyList<BspFaceLighting> all = BspLightmaps.ReadAll(File.ReadAllBytes(path));

        int bumped = all.Count(face => face.IsBumped);

        TestContext.Out.WriteLine($"BUMPED {bumped} of {all.Count} faces are bump lit");

        bumped.ShouldBeGreaterThan(0, "cp_process_final must contain bump-lit faces to test them");

        foreach (BspFaceLighting face in all)
        {
            if (face.IsBumped)
            {
                face.Directional.Count.ShouldBe(3, "a bump-lit face carries three directional sets");

                foreach (BspLightmap set in face.Directional)
                {
                    set.Width.ShouldBe(face.Flat.Width);
                    set.Height.ShouldBe(face.Flat.Height);
                }
            }
            else
            {
                face.Directional.ShouldBeEmpty("an unbumped face has no directional lighting");
            }
        }
    }

    [Test]
    public void BumpedLightmaps_EveryFacesLighting_TilesTheLumpWithoutGapOrOverlap()
    {
        // **The only test here that can falsify the set count, and finding that out cost a wrong
        // one.** The obvious control - the flat set must read identically to before - is blind to
        // it: set 0 sits at lightofs + (0 * sets + 0) * luxels * 4, and the sets term cancels when
        // the style is zero, which it always is here. Forcing every face to four sets passes that
        // control and every other assertion in this file.
        //
        // Lengths cannot be fooled that way. vrad lays faces down one after another, so a face's
        // whole span - styles times sets times luxels times four - must reach exactly the next
        // face's offset. Get the set count wrong on even one face and the arithmetic stops
        // meeting. This is the same lever that settled the string table layout: a stated length
        // rules a layout out before any byte is read.
        if (MapFile is not { } path)
        {
            Assert.Ignore("Team Fortress 2 is not installed; set TF2_FOLDER to run this.");
            return;
        }

        ReadOnlyMemory<byte> map = File.ReadAllBytes(path);

        IReadOnlyList<(int Offset, long Bytes, int Styles)> spans = BspLightmaps.Spans(map);

        spans.Count.ShouldBeGreaterThan(0, "the map must have lit faces");

        // Faces can share an offset - identical lighting is deduplicated - so distinct starts are
        // what has to tile, and each one's length must be agreed by everyone using it.
        List<(int Offset, long Bytes, int Styles)> ordered = [.. spans
            .GroupBy(span => span.Offset)
            .Select(group =>
            {
                group.Select(span => span.Bytes).Distinct().Count().ShouldBe(
                    1, $"faces sharing offset {group.Key} must agree on its length");

                return (Offset: group.Key, Bytes: group.First().Bytes, Styles: group.First().Styles);
            })
            .OrderBy(span => span.Offset)];

        int touching = 0;

        for (int at = 0; at + 1 < ordered.Count; at++)
        {
            // **The gap between two faces is not padding, it is the next face's own header.**
            // vrad adds four bytes per light style to the running size BEFORE it takes the offset,
            // so every face is preceded by one average light colour per style and lightofs points
            // past them. Written without this the test found zero spans meeting their neighbour
            // and looked exactly like a broken set count - the arithmetic was right and
            // incomplete, which is the harder of the two to tell apart from wrong.
            long ends = ordered[at].Offset + ordered[at].Bytes + (ordered[at + 1].Styles * 4);

            ends.ShouldBeLessThanOrEqualTo(
                ordered[at + 1].Offset,
                $"lighting at {ordered[at].Offset} runs {ordered[at].Bytes} bytes and would " +
                $"overlap the face at {ordered[at + 1].Offset}");

            if (ends == ordered[at + 1].Offset)
            {
                touching++;
            }
        }

        TestContext.Out.WriteLine(
            $"BUMPED {touching} of {ordered.Count - 1} lighting spans end exactly where the next begins");

        // **Checked with == rather than <=, which is the whole point.** An overlap check alone
        // passes against a reader that thinks every face is half its real size. Valve packs these
        // with no padding, so nearly every span must meet its neighbour exactly; a handful will
        // not, because the last face before a deduplicated run can be followed by a gap.
        touching.ShouldBeGreaterThan(
            (ordered.Count - 1) * 9 / 10,
            "vrad packs lighting without padding, so spans must meet their neighbours");
    }

    [Test]
    public void ReadAll_TheDirectionalSetsAreNotCopiesOfTheFlatOne()
    {
        // **Three sets of the right size prove nothing about where they were read from.** An
        // implementation that returned the flat set three times would satisfy every assertion
        // above, and the shader would then combine three identical lightmaps into a flat result
        // that looks exactly like no bump mapping at all - which is the state we are leaving.
        if (MapFile is not { } path)
        {
            Assert.Ignore("Team Fortress 2 is not installed; set TF2_FOLDER to run this.");
            return;
        }

        IReadOnlyList<BspFaceLighting> all = BspLightmaps.ReadAll(File.ReadAllBytes(path));

        int compared = 0;
        int differing = 0;

        foreach (BspFaceLighting face in all)
        {
            if (!face.IsBumped || face.Flat.IsEmpty)
            {
                continue;
            }

            compared++;

            if (!face.Directional[0].Pixels.ToArray().SequenceEqual(face.Flat.Pixels.ToArray()))
            {
                differing++;
            }
        }

        TestContext.Out.WriteLine(
            $"BUMPED {differing} of {compared} bump-lit faces differ from their flat set");

        // Not "all", because a face lit evenly from every direction legitimately has identical
        // sets - a flat ceiling under uniform light does. The great majority must differ.
        differing.ShouldBeGreaterThan(
            compared / 2, "the directional sets must be read from their own data");
    }
}
