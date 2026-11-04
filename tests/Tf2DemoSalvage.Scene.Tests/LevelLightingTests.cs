using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;

using Microsoft.Extensions.Logging;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// The light reaching a world position: the leaf's bounce, the direct lights, and the sun's trace.
/// </summary>
/// <remarks>
/// **The engine answers this query, not the window** —
/// <c>IVEngineClient::ComputeLighting( const Vector&amp; pt, const Vector* pNormal, bool bClamp,
/// Vector&amp; color, Vector *pBoxColors )</c> at <c>src/public/cdll_int.h:392</c>, whose comment
/// says that when <c>pBoxColors</c> is given "(it's an array of 6), then it'll copy the light
/// contribution at each box side". Six box sides is an ambient cube. Client code asks for it as
/// <c>engine-&gt;ComputeLighting( pos, NULL, true, vecColor )</c>
/// (<c>c_impact_effects.cpp:486</c>) — a level service, reached through an interface.
///
/// Ours lived in <c>MainForm</c> as <c>LightAt</c>/<c>SunAt</c> and was handed to
/// <see cref="MapAssets"/> and <see cref="EntityModelSet"/> as delegates, so **nothing tested it**:
/// reaching it needed an STA thread, a device and a real map (B188, B184).
///
/// **Every case here is a PAIR**, because a lighting test that measures one sample cannot tell
/// "reads the leaf" from "returns a constant" — which is the failure mode this subsystem has
/// already had once, when nearest-sample lookup returned a plausible number for every query.
/// </remarks>
public sealed class LevelLightingTests
{
    [Test]
    public void ComputeLighting_WithWorldLightsButNoTree_IsUnlit()
    {
        // **The distinguishing input for the guard.** A bright point light sits exactly where the
        // query is, so an implementation that dropped the "no tree" guard and fell through to the
        // direct term would answer a very bright cube. Asserting default against a map with NO
        // lights would pass either way — that is the "wrong condition" trap, and this is the input
        // that escapes it.
        LevelLighting lighting = new(
            leaves: null,
            ambient: [],
            worldLights: [Lamp((0f, 0f, 100f), 1000f)],
            sun: null,
            new RecordingLogger());

        lighting.ComputeLighting(0f, 0f, 100f).ShouldBe(default(AmbientCube));
    }

    [Test]
    public void ComputeLighting_InTwoDifferentLeaves_TakesEachLeafsOwnSamples()
    {
        // **The control pair, and the whole point of the subsystem.** Two crates either side of a
        // doorway are lit differently without either carrying a lightmap, so the observable that
        // matters is that two positions in DIFFERENT leaves disagree. One position could not
        // distinguish "looked up the leaf" from "returned the only cube it had".
        LevelLighting lighting = Lit();

        float above = AmbientCube.Luminance(lighting.ComputeLighting(0f, 0f, 100f));
        float below = AmbientCube.Luminance(lighting.ComputeLighting(0f, 0f, -100f));

        above.ShouldBeGreaterThan(below);
        below.ShouldBeGreaterThan(0f, "leaf 1 is dim, not black");
    }

    [Test]
    public void ComputeLighting_WithALampNearby_IsBrighterThanTheBounceAlone()
    {
        // The direct term (B95, D37). Same leaf and same query point in both, so the ONLY
        // difference is whether the map carries a lamp — which is what makes this a measurement of
        // the lamp rather than of the leaf.
        float bounceOnly = AmbientCube.Luminance(Lit().ComputeLighting(0f, 0f, 100f));
        float withLamp = AmbientCube.Luminance(
            Lit([Lamp((0f, 0f, 120f), 400f)]).ComputeLighting(0f, 0f, 100f));

        withLamp.ShouldBeGreaterThan(bounceOnly);
    }

    [Test]
    public void ComputeLighting_ForALeafPastTheEndOfTheSamples_IsUnlitRatherThanThrowing()
    {
        // A map whose LEAF_AMBIENT lump is shorter than its tree — the tree answers leaf 1 and only
        // leaf 0 has samples. Reading it would be an index past the end, so the bound is load
        // bearing rather than defensive.
        LevelLighting lighting = new(
            leaves: OneSplit(above: 0, below: 1),
            ambient: [Samples(Grey(0.5f))],
            worldLights: [],
            sun: null,
            new RecordingLogger());

        Should.NotThrow(() => lighting.ComputeLighting(0f, 0f, -100f));
        lighting.ComputeLighting(0f, 0f, -100f).ShouldBe(default(AmbientCube));

        // The control: the leaf that IS in range still answers, so the test above is measuring the
        // bound rather than a lighting path that never works.
        AmbientCube.Luminance(lighting.ComputeLighting(0f, 0f, 100f)).ShouldBeGreaterThan(0f);
    }

    /// <remarks>
    /// The light cache's skylight test (`engine.dll` 0x1801b8e20): from the point, `57016.32` along the light's reversed
    /// normal, world only, and the light counts when that trace strikes `SURF_SKY`.
    /// </remarks>
    [Test]
    public void SunAt_UnderOpenSky_IsTheSunTracedAsTheEngineTracesIt()
    {
        ((float X, float Y, float Z) From, (float X, float Y, float Z) To) asked = default;

        LevelLighting lighting = new(
            leaves: OneSplit(above: 1, below: 0, solidLeaf: -1),
            ambient: [Samples(Grey(0.5f)), Samples(Grey(0.1f))],
            worldLights: [],
            sun: Sky(),
            new RecordingLogger(),
            strikesSky: (from, to) =>
            {
                asked = (from, to);
                return true;
            });

        SunLight? sun = lighting.SunAt(0f, 0f, -100f);

        sun.ShouldNotBeNull();
        sun.Value.Red.ShouldBe(0.9f);
        asked.From.ShouldBe((0f, 0f, -100f));
        asked.To.ShouldBe((0f, 0f, -100f + 57016.32f));
    }

    /// <remarks>
    /// **Every caller of the sun is counted, inside the call** (B594 follow-up): a light cache entry built, a static
    /// prop's state built once, and a direct <see cref="LevelLighting.SunAt"/> — and a cache hit or a static prop asked
    /// again calls nothing. Untimed, no ticks are taken; timed, the trace's own time is.
    /// </remarks>
    [Test]
    public void SunWork_EachCallerOnceAndAgain_CountsEveryCallWhereItRan()
    {
        LevelLighting lighting = new(
            leaves: OneSplit(above: 1, below: 0, solidLeaf: -1),
            ambient: [Samples(Grey(0.5f)), Samples(Grey(0.1f))],
            worldLights: [],
            sun: Sky(),
            new RecordingLogger(),
            strikesSky: static (_, _) =>
            {
                long from = System.Diagnostics.Stopwatch.GetTimestamp();

                while (System.Diagnostics.Stopwatch.GetTimestamp() == from)
                {
                    // A trace that lasts at least one stopwatch tick, so a timed call cannot read as zero by resolution alone.
                }

                return true;
            });

        lighting.ModelSunAt(0f, 0f, -100f);
        lighting.ModelSunAt(0f, 0f, -100f);
        lighting.StaticPropLightingAt(1f, 0f, -100f);
        lighting.StaticPropLightingAt(1f, 0f, -100f);
        lighting.SunAt(0f, 0f, -100f);

        lighting.SunWork.ShouldBe(new SunWork(Calls: 3, LightCache: 1, StaticProp: 1, Direct: 1, Ticks: 0));

        lighting.TimeSun = true;
        lighting.SunAt(0f, 0f, -100f);

        lighting.SunWork.Ticks.ShouldBeGreaterThan(0);
    }

    [Test]
    public void SunAt_WhereTheTraceMissesSky_IsNull()
    {
        // **The control, and Valve's parenthesis made real.** `bspfile.h` defines a sky light as a
        // "directional light with no falloff (surface must trace to SKY texture)" — without the
        // trace the sun lights the inside of every building. Same sun, same point; only the trace's answer differs.
        LevelLighting lighting = new(
            leaves: OneSplit(above: 1, below: 0, solidLeaf: -1),
            ambient: [Samples(Grey(0.5f)), Samples(Grey(0.1f))],
            worldLights: [],
            sun: Sky(),
            new RecordingLogger(),
            strikesSky: static (_, _) => false);

        lighting.SunAt(0f, 0f, -100f).ShouldBeNull();
    }

    /// <remarks>
    /// **The leaf is asked before the ray** (B514). *Read in disassembly*, `engine.dll` `0x1801b60a0`: an
    /// `emit_skylight` is dropped unless the leaf holding the point has `flags &amp; 5` — `LEAF_FLAGS_SKY` (1) or
    /// `LEAF_FLAGS_SKY2D` (4), `bspfile.h:791-793`; `LEAF_FLAGS_RADIAL` (2) is neither. `dleaf_t` keeps them in the top
    /// seven bits of the short at +6 (`:807-808`). The point is in leaf 0, and the ray would strike sky in every case.
    /// </remarks>
    [TestCase(0, false)]
    [TestCase(1, true)]
    [TestCase(2, false)]
    [TestCase(4, true)]
    public void SunAt_ALeafFlaggedOrNotForSkyInItsPvs_TakesTheSunOnlyWhenFlagged(int leafFlags, bool lit)
    {
        int traces = 0;
        byte[] plane = new byte[20];
        BinaryPrimitives.WriteSingleLittleEndian(plane.AsSpan(8), 1f);

        byte[] node = new byte[32];
        BinaryPrimitives.WriteInt32LittleEndian(node.AsSpan(4), -1 - 1);
        BinaryPrimitives.WriteInt32LittleEndian(node.AsSpan(8), -0 - 1);

        byte[] leaves = new byte[64];
        BinaryPrimitives.WriteInt16LittleEndian(leaves.AsSpan(6), (short)(leafFlags << 9));

        LevelLighting lighting = new(
            leaves: BspLeafTree.FromLumps(node, plane, leaves),
            ambient: [Samples(Grey(0.5f)), Samples(Grey(0.1f))],
            worldLights: [],
            sun: Sky(),
            new RecordingLogger(),
            strikesSky: (_, _) =>
            {
                traces++;
                return true;
            });

        (lighting.SunAt(0f, 0f, -100f) is not null).ShouldBe(lit);
        traces.ShouldBe(lit ? 1 : 0, "an unflagged leaf never reaches the trace");
    }

    /// <remarks>
    /// **A static prop's cube is the light cache's computed one, not its leaf's** (B514; `engine.dll`
    /// `FUN_1801bb4b0` with a static caller under `r_radiosity` 4 — <see cref="StaticPropAmbient"/>). And it is built
    /// once: the handle's static state is computed when first asked for (`FUN_1801ba590`, the `+0x174 &amp; 4` bit) and
    /// kept, so 162 rays are not cast again for every frame the prop is drawn.
    /// </remarks>
    [Test]
    public void StaticPropLightingAt_GivenTheComputedAmbient_TakesItInPlaceOfTheLeafCubeAndBuildsItOncePerPoint()
    {
        int built = 0;
        AmbientCube computed = new(
            (0.1f, 0.2f, 0.3f), (0.1f, 0.2f, 0.3f), (0.1f, 0.2f, 0.3f), (0.1f, 0.2f, 0.3f), (0.7f, 0.8f, 0.9f), (0f, 0f, 0f));

        LevelLighting lighting = new(
            leaves: OneSplit(above: 0, below: 1),
            ambient: [Samples(Grey(0.5f)), Samples(Grey(0.1f))],
            worldLights: [],
            sun: null,
            new RecordingLogger(),
            staticPropAmbient: (x, y, z) =>
            {
                built++;
                (x, y, z).ShouldBe((0f, 0f, 100f));
                return computed;
            });

        lighting.StaticPropLightingAt(0f, 0f, 100f).Lighting.Cube.ShouldBe(computed);
        lighting.StaticPropLightingAt(0f, 0f, 100f).Lighting.Cube.ShouldBe(computed);
        built.ShouldBe(1);

        // The control: a model that is not a static prop still takes the leaf's own samples.
        lighting.ComputeLighting(0f, 0f, 100f).ShouldNotBe(computed);
    }

    [Test]
    public void SunAt_OnAMapWithNoSun_IsNull()
    {
        // An indoor map compiles with no `emit_skylight` at all. The pair with the case above: same
        // open sky, no sun to find.
        Lit().SunAt(0f, 0f, -100f).ShouldBeNull();
    }

    [Test]
    public void ComputeLighting_AtManyDistinctPlaces_ReportsTheLightTermsOnlyToItsLimit()
    {
        // **The two terms are reported apart because one number cannot say which is missing** — no
        // light near enough to be chosen, or lights chosen that contribute nothing once attenuated
        // (docs/memory/logs-are-the-debugger.md#a-log-must-name-what-it-measured). It is capped because this runs for
        // every model every time one moves, and a per-frame line printed 1,280 times a second once
        // already (B163).
        RecordingLogger log = new();
        LevelLighting lighting = Lit([Lamp((0f, 0f, 120f), 400f)], log: log);

        for (int step = 0; step < LevelLighting.LightTermReportLimit * 3; step++)
        {
            lighting.ComputeLighting(step * 4f, 0f, 100f);
        }

        log.Count("light terms at").ShouldBe(LevelLighting.LightTermReportLimit);
    }

    [Test]
    public void ComputeLighting_AtTheSamePlaceTwice_ReportsItOnce()
    {
        // The question the line answers is about a PLACE, not about a frame, so repeating it every
        // frame would say nothing new at the cost of the whole log.
        RecordingLogger log = new();
        LevelLighting lighting = Lit([Lamp((0f, 0f, 120f), 400f)], log: log);

        lighting.ComputeLighting(0f, 0f, 100f);
        lighting.ComputeLighting(0f, 0f, 100f);

        log.Count("light terms at").ShouldBe(1);
    }

    [Test]
    public void ComputeLighting_WhenItReportsLightTerms_WritesAtDebugRatherThanInformation()
    {
        // **A per-frame diagnostic at Information is what B191 turned out to be**: one log line per
        // frame reaching a per-line disk flush, freezing playback for 120 ms every few seconds. The
        // level is the thing that keeps it out of a release run (`developer 0`), so it is asserted
        // rather than left to whoever edits the line next.
        RecordingLogger log = new();

        Lit([Lamp((0f, 0f, 120f), 400f)], log: log).ComputeLighting(0f, 0f, 100f);

        log.Lines
            .Where(line => line.Message.Contains("light terms at", StringComparison.Ordinal))
            .Select(line => line.Level)
            .ShouldAllBe(level => level == LogLevel.Debug);
    }

    [Test]
    public void ComputeLighting_OnAMapWithNoWorldLights_ReportsNothing()
    {
        // The line names what the direct term added, so on a map that has no direct lights it has
        // nothing to say — and forty places' worth of "0, and 0 lights on the map" is exactly the
        // noise that hides the lines that matter.
        RecordingLogger log = new();

        Lit(log: log).ComputeLighting(0f, 0f, 100f);

        log.Count("light terms at").ShouldBe(0);
    }

    [Test]
    public void Constructor_WithNoLogger_Refuses()
    {
        Should.Throw<ArgumentNullException>(() =>
            new LevelLighting(null, [], [], null, render: null!));
    }

    /// <remarks>
    /// Two lamps either side of one 32-unit cell: x 1 is nearer the west lamp, x 30 the east. The light cache
    /// (`0x1801b9cd0`) builds the cell's entry at the first point to miss and lights the second with it.
    /// </remarks>
    [Test]
    public void ModelLightingAt_TwoPointsInOneCell_BothTakeTheFirstPointsEntry()
    {
        LevelLighting lighting = Lit([Lamp((-200f, 0f, 100f), 400f), Lamp((230f, 0f, 100f), 400f)]);

        lighting.ModelLightingAt(1f, 0f, 100f).Locals[0].X.ShouldBe(-200f);
        lighting.ModelLightingAt(30f, 0f, 100f).Locals[0].X.ShouldBe(-200f, "the entry built at x 1");

        // The control: the uncached reading at x 30 ranks the east lamp first.
        lighting.LightingAt(30f, 0f, 100f).Locals[0].X.ShouldBe(230f);
    }

    /// <remarks>
    /// A light style change relights an entry where it was built, not where it is asked from (`ComputeLightStyles` on a
    /// stale hit, `0x1801b9cd0`): dimming the west lamp's style puts the east lamp first at x 1.
    /// </remarks>
    [Test]
    public void ModelLightingAt_AfterAStyleChange_RelightsTheEntryAtItsOwnPoint()
    {
        LevelLighting lighting = Lit([Lamp((-200f, 0f, 100f), 400f) with { Style = 1 }, Lamp((230f, 0f, 100f), 400f)]);

        lighting.ModelLightingAt(1f, 0f, 100f);
        lighting.StyleScale = style => style == 1 ? 0.01f : 1f;

        lighting.ModelLightingAt(30f, 0f, 100f).Locals[0].X.ShouldBe(-200f, "unchanged until told");

        lighting.StylesChanged([1]);

        lighting.ModelLightingAt(30f, 0f, 100f).Locals[0].X.ShouldBe(230f);
    }

    /// <remarks>
    /// A model draw's one `LightcacheGet` (`0x1800f1bd0`, `0x1801b9cd0`) answers both its lights and its sun: asked twice at one
    /// point in one frame, the cache is looked in once.
    /// </remarks>
    [Test]
    public void ModelSunAt_AfterModelLightingAtTheSamePointAndFrame_LooksInTheCacheOnce()
    {
        LevelLighting lighting = Sunny();

        lighting.ModelLightingAt(0f, 0f, -100f);
        lighting.ModelSunAt(0f, 0f, -100f);

        lighting.LightCacheLookups.ShouldBe(1);
    }

    /// <remarks>
    /// Another point in between is looked up again, so that point's touch of the LRU is not skipped, and it answers with its own
    /// entry: a build for each of two cells, then the first's own sun, and no third build.
    /// </remarks>
    [Test]
    public void ModelSunAt_WithAnotherPointAskedInBetween_LooksInTheCacheAgain()
    {
        LevelLighting lighting = Sunny();

        lighting.ModelLightingAt(0f, 0f, -100f);
        lighting.ModelLightingAt(100f, 0f, -100f);
        lighting.ModelSunAt(0f, 0f, -100f);

        lighting.LightCacheLookups.ShouldBe(3);
        lighting.SunWork.LightCache.ShouldBe(2);
    }

    /// <remarks>
    /// **The budget is per frame, which is why the frame is part of the remembered question.** Past `lightcache_maxmiss` a third
    /// cell takes the nearest entry and builds nothing; the same point next frame has a fresh budget and must build its own.
    /// </remarks>
    [Test]
    public void ModelSunAt_APointPastTheBudgetAskedAgainNextFrame_BuildsItsOwnEntry()
    {
        LevelLighting lighting = Sunny();

        lighting.Frame = LightCache<int>.UnthrottledFrames + 40;
        lighting.ModelSunAt(0f, 0f, -100f);
        lighting.ModelSunAt(100f, 0f, -100f);
        lighting.ModelSunAt(200f, 0f, -100f);
        lighting.ModelSunAt(200f, 0f, -100f);

        lighting.SunWork.LightCache.ShouldBe(2, "the budget of two is spent, so the third cell takes the nearest");

        lighting.Frame++;
        lighting.ModelSunAt(200f, 0f, -100f);

        lighting.SunWork.LightCache.ShouldBe(3);
    }

    /// <remarks>
    /// **`FUN_1801b7fd0` asks with flags `0xf`, so past the budget it takes the nearest entry and builds nothing** (`0x1801b9cd0`). The third point
    /// stands in the bright leaf, and the nearest entry is the dim leaf's, so the answer is the dim cube; next frame it builds its own.
    /// </remarks>
    [Test]
    public void ComputeLighting_PastTheMissBudget_TakesTheNearestEntryThenItsOwn()
    {
        LevelLighting lighting = Lit();

        lighting.Frame = LightCache<int>.UnthrottledFrames + 40;
        lighting.ComputeLighting(3200f, 0f, 100f);
        lighting.ComputeLighting(0f, 0f, -100f);

        lighting.ComputeLighting(0f, 0f, 100f).ShouldBe(Grey(0.05f));

        lighting.Frame++;
        lighting.ComputeLighting(0f, 0f, 100f).ShouldBe(Grey(0.6f));
    }

    /// <remarks>
    /// **What `FUN_1801b7fd0` gets from a zeroed pool entry is zero, and that is its answer.** `0x1801b9cd0`'s scan treats those entries as key 0 like any
    /// other, so a point near the map's far corner can be nearest one; it copies the entry's zero cube and no slots out, and the fold has nothing to do.
    /// Nothing here draws full bright: the only consumers are `RopeLight`, which averages the faces, and a log line.
    /// </remarks>
    [Test]
    public void ComputeLighting_PastTheMissBudgetNearestAZeroedEntry_IsZero()
    {
        LevelLighting lighting = Lit();

        lighting.Frame = LightCache<int>.UnthrottledFrames + 40;
        lighting.ComputeLighting(30000f, 30000f, 30000f);
        lighting.ComputeLighting(30000f, 30000f, 29000f);

        lighting.ComputeLighting(-32000f, -32000f, -32000f).ShouldBe(default(AmbientCube));

        lighting.Frame++;
        lighting.ComputeLighting(-32000f, -32000f, -32000f).PositiveX.Red.ShouldBe(0.05f, 0.0001f, "the control: its own entry, next frame");
    }

    /// <remarks>
    /// **The memo answers only the question just asked.** A `ComputeLighting` at another point between a model's two questions replaces it, so the second
    /// question looks in the cache again, where the engine's one `LightcacheGet` would not.
    /// </remarks>
    [Test]
    public void ModelSunAt_AfterComputeLightingAtAnotherPoint_LooksAgain()
    {
        LevelLighting lighting = Sunny();

        lighting.ModelLightingAt(0f, 0f, 100f);
        lighting.ModelSunAt(0f, 0f, 100f);
        lighting.LightCacheLookups.ShouldBe(1, "the control: nothing came between");

        lighting.ComputeLighting(0f, 0f, -100f);
        lighting.ModelSunAt(0f, 0f, 100f);
        lighting.LightCacheLookups.ShouldBe(3);
    }

    /// <remarks>
    /// A build that throws answered nothing, so nothing is remembered for its point. Point A's sun strikes sky and point B's does
    /// not, so A's entry read at B would hand B a sun it does not have.
    /// </remarks>
    [Test]
    public void ModelSunAt_AfterABuildThatThrew_IsNotTheLastPointsEntry()
    {
        bool throwing = false;
        LevelLighting lighting = new(
            leaves: OneSplit(above: 1, below: 0, solidLeaf: -1),
            ambient: [Samples(Grey(0.5f)), Samples(Grey(0.1f))],
            worldLights: [],
            sun: Sky(),
            new RecordingLogger(),
            strikesSky: (from, _) =>
            {
                if (from.X < 50f)
                {
                    return true;
                }

                return throwing ? throw new InvalidOperationException("the trace failed") : false;
            });

        lighting.ModelSunAt(0f, 0f, -100f).ShouldNotBeNull();

        throwing = true;
        Should.Throw<InvalidOperationException>(() => lighting.ModelSunAt(100f, 0f, -100f));
        throwing = false;

        lighting.ModelSunAt(100f, 0f, -100f).ShouldBeNull();
    }

    /// <remarks>
    /// **After a throw the last answer is not this lookup's, so it answers nothing** (B675). The cache moved its victim to the front before the build threw, so
    /// repeating point A's lookup is no longer the no-op the memo's arithmetic rests on: it moves A's entry back to the front.
    /// </remarks>
    [Test]
    public void ModelSunAt_AfterAnotherPointsBuildThrew_LooksInTheCacheAgain()
    {
        bool throwing = false;
        LevelLighting lighting = new(
            leaves: OneSplit(above: 1, below: 0, solidLeaf: -1),
            ambient: [Samples(Grey(0.5f)), Samples(Grey(0.1f))],
            worldLights: [],
            sun: Sky(),
            new RecordingLogger(),
            strikesSky: (from, _) => throwing && from.X > 50f ? throw new InvalidOperationException("the trace failed") : true);

        lighting.ModelSunAt(0f, 0f, -100f);
        lighting.ModelSunAt(0f, 0f, -100f);
        lighting.LightCacheLookups.ShouldBe(1, "the control: asked again with nothing between, it is remembered");

        throwing = true;
        Should.Throw<InvalidOperationException>(() => lighting.ModelSunAt(100f, 0f, -100f));
        throwing = false;

        lighting.ModelSunAt(0f, 0f, -100f);
        lighting.LightCacheLookups.ShouldBe(3);
    }

    /// <remarks>
    /// **The line reports the cube the answer used, carried from it, not a second reading of the asked point's leaf** (B675, B243). Past the budget the entry is the
    /// dim leaf's while the asked point stands in the bright one, so a second route says 0.6 about an answer built from 0.05.
    /// </remarks>
    [Test]
    public void ComputeLighting_PastTheMissBudget_ReportsTheEntrysCube()
    {
        RecordingLogger log = new();
        LevelLighting lighting = Lit([Lamp((100000f, 0f, 100000f), 1f)], log: log);

        lighting.Frame = LightCache<int>.UnthrottledFrames + 40;
        lighting.ComputeLighting(3200f, 0f, 100f);
        lighting.ComputeLighting(0f, 0f, -100f);
        lighting.ComputeLighting(0f, 0f, 100f);

        log.Lines.ShouldContain(line => line.Message.Contains("light terms at (0,0,100): cube 0.05,", StringComparison.Ordinal));
    }

    /// <remarks>
    /// **What the model path takes from a zeroed entry** (`0x1801b9cd0` with flags `0xf`, reached past the budget): flag 1 copies its zero cube and no slots; flag 4's
    /// styled mask is zero; flag 2 reaches `FUN_1801b7a10` with no PVS row, which looks one up from the ENTRY's origin `+0x184`, (0 0 0), so a dlight seen from the map's
    /// origin is in it. `ComputeLighting`'s zero is the same copy. What the DRAW does next, `FUN_1800f3110`, is not ported: B676.
    /// </remarks>
    [Test]
    public void ModelLightingAt_PastTheMissBudgetNearestAZeroedEntry_IsZeroWithOnlyDlightsSeenFromTheOrigin()
    {
        LevelLighting lighting = StaticPlusDynamicLightingConformanceTests.Map([]);

        lighting.Frame = LightCache<int>.UnthrottledFrames + 40;
        lighting.ModelLightingAt(30000f, 30000f, 30000f);
        lighting.ModelLightingAt(30000f, 30000f, 29000f);

        PointLighting zero = lighting.ModelLightingAt(-32000f, -32000f, -32000f);

        zero.Cube.ShouldBe(default(AmbientCube));
        zero.Locals.ShouldBeEmpty();

        // The map's origin is in cluster 1, below the plane; the asked point's own leaf is not what is asked. Cluster 0 is not in cluster 1's PVS.
        DynamicLights lights = new() { Time = 1f };
        lighting.Dynamic = lights;
        DynamicLight light = lights.AllocDlight(1);
        (light.X, light.Y, light.Z, light.Radius, light.Red) = (0f, 0f, 120f, 255f, 255);

        lighting.ModelLightingAt(-32000f, -32000f, -32000f).Locals.ShouldBeEmpty("the control: not seen from the origin");

        light.Z = -20f;

        lighting.ModelLightingAt(-32000f, -32000f, -32000f).Locals.ShouldHaveSingleItem().Z.ShouldBe(-20f);
    }

    /// <remarks>
    /// **An empty ambient lump costs a static state its leaf cube and nothing else** (B674 review): `FUN_1801b8270` calls `FUN_1801b5a50` over the world lights whatever `FUN_1801bb4b0`
    /// found. Without this a prop lit from its handle on such a map loses its sun and lamps.
    /// </remarks>
    [Test]
    public void StaticPropLightingAt_OnAMapWithNoAmbientLump_KeepsTheSunAndTheLamps()
    {
        LevelLighting lighting = new(
            leaves: OneSplit(above: 1, below: 0, solidLeaf: -1),
            ambient: [],
            worldLights: [Lamp((0f, 0f, 120f), 400f)],
            sun: Sky(),
            new RecordingLogger(),
            strikesSky: static (_, _) => true);

        (PointLighting state, SunLight? sun) = lighting.StaticPropLightingAt(0f, 0f, 100f);

        sun.ShouldNotBeNull();
        state.Locals.ShouldHaveSingleItem().Z.ShouldBe(120f);
        state.Cube.ShouldBe(default(AmbientCube));
    }

    /// <remarks>A hit is every model every frame, so it owes the collector nothing: no closure over the point.</remarks>
    [Test]
    public void ModelLightingAt_AHitInAnotherCell_AllocatesNothing()
    {
        LevelLighting lighting = Sunny();

        // Warmed by the same loop, unmeasured, so only the hits themselves are read.
        AskTwoCellsAThousandTimes(lighting);

        long before = GC.GetAllocatedBytesForCurrentThread();

        AskTwoCellsAThousandTimes(lighting);

        (GC.GetAllocatedBytesForCurrentThread() - before).ShouldBe(0);
    }

    private static void AskTwoCellsAThousandTimes(LevelLighting lighting)
    {
        for (int each = 0; each < 1000; each++)
        {
            lighting.ModelLightingAt(0f, 0f, -100f);
            lighting.ModelSunAt(100f, 0f, -100f);
        }
    }

    /// <summary>A two-leaf map with a sun that always strikes sky, so a counted sun marks each entry built.</summary>
    private static LevelLighting Sunny() =>
        new(
            leaves: OneSplit(above: 1, below: 0, solidLeaf: -1),
            ambient: [Samples(Grey(0.5f)), Samples(Grey(0.1f))],
            worldLights: [],
            sun: Sky(),
            new RecordingLogger(),
            strikesSky: static (_, _) => true);

    /// <remarks>
    /// The production path a model is drawn with folds a fifth lamp into its cube (B453, `LocalLights.Split`): five
    /// lamps to the east, so the fold lands on +X only, and four lamps are the control — nothing left to fold.
    /// </remarks>
    [Test]
    public void LightingAt_FiveLampsToTheEast_FoldsTheFifthIntoThePositiveXFace()
    {
        // Bright enough for 500 units to be inside the computed radius, about 680 (`FUN_1800cbdd0`).
        BspWorldLight[] five = [.. new[] { 100f, 200f, 300f, 400f, 500f }.Select(x => Lamp((x, 0f, 100f), 4000f))];

        PointLighting withFive = Lit(five).LightingAt(0f, 0f, 100f);
        PointLighting withFour = Lit(five[..4]).LightingAt(0f, 0f, 100f);

        withFive.Locals.Count.ShouldBe(4);
        withFour.Cube.PositiveX.Red.ShouldBe(0.6f, "the control: four lamps all take slots");
        withFive.Cube.NegativeX.Red.ShouldBe(0.6f, "a lamp to the east adds nothing to the west face");

        // About 4000 / 500² = 0.016, from wherever the cache cell puts the point.
        withFive.Cube.PositiveX.Red.ShouldBeGreaterThan(0.6f + 0.01f);
    }

    /// <summary>A map lit brightly above the z = 0 plane and dimly below it.</summary>
    internal static LevelLighting Lit(
        IReadOnlyList<BspWorldLight>? worldLights = null,
        BspWorldLight? sun = null,
        RecordingLogger? log = null) =>
        new(
            // Leaf 2 above rather than leaf 0, so an implementation that ignored the tree and read
            // `ambient[0]` would answer the dim cube for a point that is plainly in daylight.
            OneSplit(above: 2, below: 1),
            [Samples(Grey(0.2f)), Samples(Grey(0.05f)), Samples(Grey(0.6f))],
            worldLights ?? [],
            sun,
            log ?? new RecordingLogger());

    /// <summary>A leaf holding one uniform sample, filling a large box.</summary>
    private static AmbientSamples Samples(AmbientCube cube) =>
        new([new AmbientSample(cube, 0.5f, 0.5f, 0.5f)], (-512f, -512f, -512f, 512f, 512f, 512f));

    /// <summary>A cube of one brightness on all six faces.</summary>
    private static AmbientCube Grey(float level) =>
        new(
            (level, level, level), (level, level, level), (level, level, level),
            (level, level, level), (level, level, level), (level, level, level));

    /// <summary>A point light with Valve's own falloff terms.</summary>
    internal static BspWorldLight Lamp((float X, float Y, float Z) origin, float intensity) =>
        new(
            origin,
            (intensity, intensity, intensity),
            (0f, 0f, -1f),
            WorldLightKind.Point,

            // `1 / (constant + linear * dist + quadratic * dist^2)`, stated inline in `bspfile.h`.
            // A purely quadratic lamp is the ordinary case a mapper places.
            QuadraticAttenuation: 1f,

            // Past every test's reach: the load never leaves a real lamp 0 here, and 0 is refused from outside the box.
            Radius: 100000f,

            // In the PVS: a real light's cluster is read from the lump, and `FUN_1801b60a0` drops a negative one (B669).
            Cluster: 0);

    /// <summary>The sun, pointing straight down.</summary>
    private static BspWorldLight Sky() =>
        new((0f, 0f, 8192f), (0.9f, 0.8f, 0.7f), (0f, 0f, -1f), WorldLightKind.SkyLight);

    /// <summary>A tree of one node splitting on the z = 0 plane.</summary>
    /// <remarks>
    /// The same fixture as <c>BspLeafTreeTests.OneSplit</c>, which is the shape a tree test needs:
    /// a real BSP cannot say which leaf is the right answer without already trusting the walk.
    /// </remarks>
    private static BspLeafTree OneSplit(int above, int below, int solidLeaf = -1)
    {
        byte[] plane = new byte[20];

        BinaryPrimitives.WriteSingleLittleEndian(plane.AsSpan(8), 1f);

        byte[] node = new byte[32];

        BinaryPrimitives.WriteInt32LittleEndian(node.AsSpan(4), -above - 1);
        BinaryPrimitives.WriteInt32LittleEndian(node.AsSpan(8), -below - 1);

        if (solidLeaf < 0)
        {
            // No leaf contents lump: `SeesSky` answers true when there are no leaves to test, which
            // is what makes the open-sky cases above open sky.
            return BspLeafTree.FromLumps(node, plane);
        }

        byte[] leaves = new byte[128];

        BinaryPrimitives.WriteInt32LittleEndian(leaves.AsSpan(solidLeaf * 32), 1);

        return BspLeafTree.FromLumps(node, plane, leaves);
    }
}
