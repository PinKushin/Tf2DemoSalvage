using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>The sun takes one of a model's four light slots, ranked with the lamps, and what happens to it when it loses (B170, B676).</summary>
/// <remarks>
/// *Read in disassembly*, `engine.dll` (TF2 x64, the build on disk 2026-10-07), `AddWorldLightToLightingState` at `0x1801b79e0`
/// (`0x1801b60a0` on the build B453 read):
/// - an `emit_skylight` (type 3) goes through the same function as every lamp, once a state (`+0x10` of the rank array is set the
///   first time one is seen, and a second is refused at the top);
/// - its ratio is `0x1801baeb0`'s sky branch: 1.0 when the trace from the point along `-normal` strikes `SURF_SKY`, with the
///   direction written as `-normal`; no style, no box, no brightest-channel minimum;
/// - its rank is luminance (`0x18047de38..40`) times that 1, against `r_worldlightmin`, then a free slot below
///   `min( r_worldlights, MaxNumLights )`, else it evicts the weakest strictly weaker slot (`0x1801ba2e0`) or is refused;
/// - **refused, it is folded** (`0x1801b76f0`) along the `-normal` the trace wrote;
/// - **evicted, it is folded along (0 0 0)**: the evicting branch zeroes the direction for a type-3 light (`0x1801b7d67`..`7d78`)
///   and the fold adds only where the direction's dot with a face is positive, so an evicted sun adds nothing.
/// The style-0 lights are added in lump order (`0x1801b7450`); a styled light is added after them (`0x1801bbbf0`).
///
/// Every lamp is 10 units from the point, quadratic, so its rank is its intensity over 100; the sun's is its luminance,
/// 0.299 · 0.9 + 0.587 · 0.8 + 0.114 · 0.7 = 0.8185 (*arithmetic*).
/// </remarks>
public sealed class ModelLightCountConformanceTests
{
    private const float X = 0f;
    private const float Y = 0f;
    private const float Z = 100f;

    private static readonly BspWorldLight Sun =
        new((0f, 0f, 8192f), (0.9f, 0.8f, 0.7f), (0f, 0f, -1f), WorldLightKind.SkyLight, LumpIndex: 9);

    /// <summary>A sun and five lamps ranked 2, 1.5, 1, 0.5 and 0.25: the sun is the fourth, the two weakest lamps fold.</summary>
    [Test]
    public void ModelLightingAt_ASunAndFiveLamps_TakesThreeLampsAndTheSunAndFoldsTheTwoWeakest()
    {
        BspWorldLight[] lights =
        [
            Lamp(0f, 10f, 0f, 200f),
            Lamp(0f, -10f, 0f, 150f),
            Lamp(0f, 0f, -10f, 100f),
            Sun,
            Lamp(10f, 0f, 0f, 50f),
            Lamp(-10f, 0f, 0f, 25f),
        ];

        LevelLighting lighting = Map(lights);
        PointLighting lit = lighting.ModelLightingAt(X, Y, Z);

        lit.Locals.Select(static light => (light.X, light.Y, light.Z)).ShouldBe(
            [(0f, 10f, 100f), (0f, -10f, 100f), (0f, 0f, 90f)],
            "three lamps beside the sun, in slot order: here the lump's");
        lighting.ModelSunAt(X, Y, Z).ShouldNotBeNull("the sun holds the fourth slot");

        // The two lamps that lost: 50 / 100 on +X and 25 / 100 on -X, nothing else.
        lit.Cube.PositiveX.Red.ShouldBe(0.6f + 0.5f, 0.00001f);
        lit.Cube.NegativeX.Red.ShouldBe(0.6f + 0.25f, 0.00001f);
        lit.Cube.PositiveZ.Red.ShouldBe(0.6f, 0.00001f, "the sun took a slot and is not folded");
        lit.Cube.PositiveY.Red.ShouldBe(0.6f, 0.00001f);
    }

    /// <summary>Four lamps that each outrank the sun, the sun listed first: it takes a slot, is evicted, and adds nothing anywhere.</summary>
    [Test]
    public void ModelLightingAt_ASunListedFirstAndEvicted_IsNeitherASlotNorFolded()
    {
        LevelLighting lighting = Map([Sun, .. FourStrongerLamps()]);
        PointLighting lit = lighting.ModelLightingAt(X, Y, Z);

        lit.Locals.Count.ShouldBe(4);
        lighting.ModelSunAt(X, Y, Z).ShouldBeNull();
        lit.Cube.ShouldBe(LevelLightingTests.Grey(0.6f), "an evicted sun is folded along (0 0 0)");
    }

    /// <summary>The same four lamps with the sun listed last: refused a slot, it is folded along `-normal`, onto +Z alone.</summary>
    [Test]
    public void ModelLightingAt_ASunListedLastAndRefused_IsFoldedOntoTheFaceItShinesOn()
    {
        LevelLighting lighting = Map([.. FourStrongerLamps(), Sun]);
        PointLighting lit = lighting.ModelLightingAt(X, Y, Z);

        lit.Locals.Count.ShouldBe(4);
        lighting.ModelSunAt(X, Y, Z).ShouldBeNull();
        lit.Cube.PositiveZ.ShouldBe((0.6f + 0.9f, 0.6f + 0.8f, 0.6f + 0.7f));
        lit.Cube.NegativeZ.ShouldBe((0.6f, 0.6f, 0.6f));
        lit.Cube.PositiveX.ShouldBe((0.6f, 0.6f, 0.6f));
    }

    /// <summary>
    /// A lamp added to a full state evicts the sun, its weakest slot, and the sun adds nothing. *Read in disassembly* on every route
    /// `Add` stands for: a static prop's listed lamp (`FUN_1801b5400` = `0x1801b6d40`, which zeroes the evicted type-3 light's direction
    /// at `0x1801b705e` from `r13`, 0 since `0x1801b6d56`), a static prop's dlight (`FUN_1801b6550` = `0x1801b7e90`, at `0x1801b8077`),
    /// and a moving model's dlight, merged through `0x1801b71d0` into `0x1801b79e0` (`0x1801b7d67`). Each then folds along that zero
    /// direction (`0x1801b76f0`), which adds nothing.
    /// </summary>
    [Test]
    public void Add_ALampThatEvictsTheSun_FoldsNothingForIt()
    {
        LightingState state = LocalLights.Build(
            [Sun, Lamp(0f, 10f, 0f, 200f), Lamp(0f, -10f, 0f, 150f), Lamp(0f, 0f, -10f, 100f)],
            X, Y, Z, LevelLightingTests.Grey(0.6f), traceKeeps: static _ => true);

        state.Count.ShouldBe(4, "the control: the sun holds a slot before the lamp arrives");

        LocalLights.Add(state, Lamp(10f, 0f, 0f, 90f), X, Y, Z);

        Enumerable.Range(0, state.Count).Select(state.SlotAt).ShouldNotContain(Sun);
        state.Cube.ShouldBe(LevelLightingTests.Grey(0.6f));
    }

    /// <summary>
    /// The trace decides: under a roof the sun is neither a slot nor folded. Listed last behind four stronger lamps, a sun wrongly seen
    /// would be refused and folded onto +Z (0.9, 0.8, 0.7), so the cube is what tells the two apart; the control is the same map in the sun.
    /// </summary>
    [Test]
    public void ModelLightingAt_ASunTheTraceMisses_IsNeitherSlottedNorFolded()
    {
        Map([.. FourStrongerLamps(), Sun]).ModelLightingAt(X, Y, Z).Cube.PositiveZ
            .ShouldBe((0.6f + 0.9f, 0.6f + 0.8f, 0.6f + 0.7f), "the control: seen, it is refused and folded");

        LevelLighting lighting = Map([.. FourStrongerLamps(), Sun], strikesSky: false);

        lighting.ModelLightingAt(X, Y, Z).Locals.Count.ShouldBe(4);
        lighting.ModelSunAt(X, Y, Z).ShouldBeNull();
        lighting.ModelLightingAt(X, Y, Z).Cube.ShouldBe(LevelLightingTests.Grey(0.6f));
    }

    /// <summary>A second sky light is refused at the top (`+0x10` set by the first): it takes no slot and is not folded.</summary>
    [Test]
    public void Build_TwoSkyLightsBothSeen_TakesTheFirstAndIgnoresTheSecond()
    {
        BspWorldLight second = Sun with { Intensity = (5f, 5f, 5f), Normal = (0f, 0f, 1f), LumpIndex = 10 };
        LightingState state = LocalLights.Build(
            [Sun, .. FourStrongerLamps()[..3], second], X, Y, Z, LevelLightingTests.Grey(0.6f), traceKeeps: static _ => true);

        Enumerable.Range(0, state.Count).Select(state.SlotAt).ShouldBe([.. FourStrongerLamps()[..3], Sun], ignoreOrder: true);
        state.Cube.ShouldBe(LevelLightingTests.Grey(0.6f), "the second is neither slotted nor folded onto -Z");
    }

    /// <summary>
    /// The engine ranks the style-0 lights into one state and the styled into another, then re-adds the styled state's slots to a copy of
    /// the first (`0x1801b7450`, `0x1801bbbf0`, merged by `0x1801b71d0`), and a newcomer evicts only a strictly weaker slot (`0x1801ba2e0`).
    /// So a styled lamp tying a style-0 lamp loses, wherever the two sit in the lump: here the styled one is listed first.
    /// </summary>
    [Test]
    public void ModelLightingAt_AStyledLampTyingAStyleZeroLampListedLater_LosesTheTie()
    {
        LevelLighting lighting = Map(
        [
            Lamp(10f, 0f, 0f, 50f) with { Style = 5 },
            Lamp(0f, 10f, 0f, 200f),
            Lamp(0f, -10f, 0f, 150f),
            Lamp(0f, 0f, -10f, 100f),
            Lamp(-10f, 0f, 0f, 50f),
        ]);
        PointLighting lit = lighting.ModelLightingAt(X, Y, Z);

        lit.Locals.Select(static light => (light.X, light.Y, light.Z)).ShouldBe(
            [(0f, 10f, 100f), (0f, -10f, 100f), (0f, 0f, 90f), (-10f, 0f, 100f)], ignoreOrder: true);
        lit.Cube.PositiveX.Red.ShouldBe(0.6f + 0.5f, 0.00001f, "the styled lamp lost and is folded");
        lit.Cube.NegativeX.Red.ShouldBe(0.6f, 0.00001f);
    }

    /// <summary>
    /// The sun is style 0, so it holds a slot in the first state; four stronger styled lamps re-added on the merge evict it, and an evicted
    /// sun adds nothing. Listed after them, a single pass in list order would have refused it and folded it onto +Z instead.
    /// </summary>
    [Test]
    public void ModelLightingAt_ASunListedAfterFourStrongerStyledLamps_IsEvictedOnTheMergeAndAddsNothing()
    {
        LevelLighting lighting = Map([.. FourStrongerLamps().Select(static lamp => lamp with { Style = 5 }), Sun]);
        PointLighting lit = lighting.ModelLightingAt(X, Y, Z);

        lit.Locals.Count.ShouldBe(4);
        lighting.ModelSunAt(X, Y, Z).ShouldBeNull();
        lit.Cube.ShouldBe(LevelLightingTests.Grey(0.6f));
    }

    /// <summary>
    /// `ComputeLighting` folds each slot at the ASKED point (`0x1801b9910`), and `0x1801baeb0`'s sky branch traces whatever its flags say, so
    /// a slotted sun is traced again from there. The entry is built at (1 1 100), which sees the sky; (10 1 100) shares its cell and does not.
    /// </summary>
    [Test]
    public void ComputeLighting_AtAPointInTheSameCellTheSkyDoesNotReach_FoldsNoSun()
    {
        LevelLighting lighting = new(
            LevelLightingTests.OneSplit(above: 2, below: 1),
            [
                LevelLightingTests.Samples(LevelLightingTests.Grey(0.2f)),
                LevelLightingTests.Samples(LevelLightingTests.Grey(0.05f)),
                LevelLightingTests.Samples(LevelLightingTests.Grey(0.6f)),
            ],
            [Sun],
            Sun,
            new RecordingLogger(),
            strikesSky: static (from, _) => from.X < 5f);

        lighting.ComputeLighting(1f, 1f, 100f).PositiveZ.ShouldBe((0.6f + 0.9f, 0.6f + 0.8f, 0.6f + 0.7f), "the control: seen where the entry is built");
        lighting.ComputeLighting(10f, 1f, 100f).ShouldBe(LevelLightingTests.Grey(0.6f));
    }

    private static BspWorldLight[] FourStrongerLamps() =>
    [
        Lamp(0f, 10f, 0f, 200f),
        Lamp(0f, -10f, 0f, 150f),
        Lamp(0f, 0f, -10f, 100f),
        Lamp(10f, 0f, 0f, 90f),
    ];

    private static BspWorldLight Lamp(float dx, float dy, float dz, float intensity) =>
        LevelLightingTests.Lamp((X + dx, Y + dy, Z + dz), intensity);

    /// <summary>Above z = 0 a uniform 0.6 cube; no leaf lump, so every leaf admits the sky and every cluster is in.</summary>
    private static LevelLighting Map(IReadOnlyList<BspWorldLight> lights, bool strikesSky = true) =>
        new(
            LevelLightingTests.OneSplit(above: 2, below: 1),
            [
                LevelLightingTests.Samples(LevelLightingTests.Grey(0.2f)),
                LevelLightingTests.Samples(LevelLightingTests.Grey(0.05f)),
                LevelLightingTests.Samples(LevelLightingTests.Grey(0.6f)),
            ],
            lights,
            Sun,
            new RecordingLogger(),
            strikesSky: (_, _) => strikesSky);
}
