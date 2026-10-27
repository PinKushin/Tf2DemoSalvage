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

        lighting.StylesChanged();

        lighting.ModelLightingAt(30f, 0f, 100f).Locals[0].X.ShouldBe(230f);
    }

    /// <remarks>
    /// The production path a model is drawn with folds a fifth lamp into its cube (B453, `LocalLights.Split`): five
    /// lamps to the east, so the fold lands on +X only, and four lamps are the control — nothing left to fold.
    /// </remarks>
    [Test]
    public void LightingAt_FiveLampsToTheEast_FoldsTheFifthIntoThePositiveXFace()
    {
        BspWorldLight[] five = [.. new[] { 100f, 200f, 300f, 400f, 500f }.Select(x => Lamp((x, 0f, 100f), 400f))];

        PointLighting withFive = Lit(five).LightingAt(0f, 0f, 100f);
        PointLighting withFour = Lit(five[..4]).LightingAt(0f, 0f, 100f);

        withFive.Locals.Count.ShouldBe(4);
        withFour.Cube.PositiveX.Red.ShouldBe(0.6f, "the control: four lamps all take slots");
        withFive.Cube.NegativeX.Red.ShouldBe(0.6f, "a lamp to the east adds nothing to the west face");

        // About 400 / 500² = 0.0016, from wherever the cache cell puts the point.
        withFive.Cube.PositiveX.Red.ShouldBeGreaterThan(0.6f + 0.001f);
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
            QuadraticAttenuation: 1f);

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
