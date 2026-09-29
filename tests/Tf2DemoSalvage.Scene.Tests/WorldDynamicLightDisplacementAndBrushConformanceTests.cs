using System;
using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>B425: a dlight on a displacement's lightmap and on a brush entity's, the two paths step 2 left unported.</summary>
/// <remarks>
/// **Displacements, read from `engine.dll`.** The leaf pass (`0x1800d4680`) tries the leaf's displacements FIRST: each whose
/// parent face does not already carry this light's bit this frame, and whose box (`CDispInfo` slot 0x18) meets the light's
/// sphere — `IsBoxIntersectingSphere` `0x180172c60`, squared gap strictly below `r²` — takes the bit and the frame. The
/// per-face pass (`0x1800d0ba0`) hands a `SURFDRAW_HAS_DISP` face to slot 0x38 (`0x1800c4540`): a bit survives for an active
/// light without `DLIGHT_NO_WORLD_ILLUMINATION` (`flags &amp; 1`) — no plane test, no `0xd` mask. `R_BuildLightMap`
/// (`0x1800cfca0`) then calls slot 0x30 (`0x1800c3130`): a light with `flags &amp; 0xc` goes to the alpha path
/// (`0x1800bf7d0`), otherwise one page takes `0x1800bf8d0` and a bumped face `0x1800bf9d0`. Both walk every luxel through
/// `0x1800c0600`, which rebuilds its world position from the sample-position lump, and add by the TRUE 3D distance:
/// - flat (`0x1800c0ad0`): `d² = |o − p|²`; below `r²`, `colour · min( (d² == 0 ? 1 : minlight·r²/d²)·(1 − d²/r²), 2 )`.
/// - bumped (`0x1800c0c40`): `dir = (o − p)/√(d² + 1e-10)`; with n, s, t its dots on the luxel's normal and tangents, page 0
///   takes `max(n, 0)·f` (NOT `f`, and no division by the normal's share), page 1 `max(0.8164966 s + 0.57735026 n, 0)·f`,
///   page 2 `max(−0.40824822 s + 0.70710677 t + 0.57735026 n, 0)·f`, page 3 the same with `−0.70710677 t`.
/// - The tangents: `CCoreDispInfo::GenerateDispSurfTangentSpaces` (`builddisp.cpp:1692-1727`): `T = |tAxis|`, `S = |N × T|`,
///   `T = |S × N|`, and `S` negated when `(sAxis × tAxis)·planeNormal &gt; 0`.
///
/// **Brush entities, read from `engine.dll` `0x1800e03a0`** (`R_MarkDlightsOnBrushModel`): with any light active, each dlight
/// with `time &lt;= die` and `radius &gt; 0` — no flag test — has its origin moved into the model's space by the entity's
/// render matrix (`0x18027c260`: `AngleMatrix` plus the origin; the move `Rᵀ(o − t)`, as `0x180279620` does), and walks the
/// model's head node (`0x1800d4520`) when that node's box meets the sphere (`IsBoxIntersectingSphereExtents` `0x180172d10`,
/// strict). The origin is restored after. The rebuild reads the light through the same matrix: `0x1800cfca0` and `0x1800d0ba0`
/// both move it with `0x180279620` before the plane test and the adders.
/// </remarks>
public sealed class WorldDynamicLightDisplacementAndBrushConformanceTests
{
    /// <summary>
    /// Luxel (0, 1) sits 40 units ABOVE the parent face's plane, 24 below the light: `d² = 576`, `1020 · 0.06391059 = 65.19`,
    /// halved into the byte, 32. A flat face's reading (`d² = 64²`) would give 6. Luxel (0, 0) at the origin: `d² = 16² + 64²
    /// = 4352`, `(39.0625/4352)·(1 − 0.4352) = 0.0050695`, 5.17, 2.
    /// </summary>
    [Test]
    public void Frame_ALightAboveADisplacementLuxel_LightsItByItsOwnPosition()
    {
        LightmapAtlas atlas = WorldDynamicLightConformanceTests.Atlas(size: 2);
        WorldDynamicLights world = new(Terrain());

        world.Frame(Lights((0f, 16f, 64f)), atlas, _ => 1f, []);

        WorldDynamicLightConformanceTests.Texel(atlas, 0, 0, 1, 2).ShouldBe((byte)32);
        WorldDynamicLightConformanceTests.Texel(atlas, 0, 0, 0, 2).ShouldBe((byte)2);
        world.Bits(0).ShouldBe(1u);
    }

    [Test]
    public void Frame_ALightWhoseSphereMissesTheDisplacementsBox_LeavesItBaked()
    {
        LightmapAtlas atlas = WorldDynamicLightConformanceTests.Atlas(size: 2);
        WorldDynamicLights world = new(Terrain());

        // The box tops out at z = 40: a light 100 above it touches it at exactly r, which is not strictly inside.
        world.Frame(Lights((0f, 16f, 140f)), atlas, _ => 1f, []);

        world.Bits(0).ShouldBe(0u);
        WorldDynamicLightConformanceTests.Texel(atlas, 0, 0, 1, 2).ShouldBe((byte)0);
    }

    /// <summary>
    /// Slot 0x38 masks only `flags &amp; 1`, and slot 0x30 sends `flags &amp; 0xc` to the alpha path, so a model-only light
    /// (`2`) still lights a displacement and an alpha light (`4`) keeps its bit but adds nothing to the lightmap.
    /// </summary>
    [TestCase(DynamicLights.NoModelIllumination, 32)]
    [TestCase(0x4, 0)]
    [TestCase(DynamicLights.NoWorldIllumination, 0)]
    public void Frame_ALightsFlagsOnADisplacement_FollowTheDisplacementsOwnMasks(int flags, int red)
    {
        LightmapAtlas atlas = WorldDynamicLightConformanceTests.Atlas(size: 2);
        WorldDynamicLights world = new(Terrain());
        DynamicLights lights = Lights((0f, 16f, 64f));
        lights.Dlights[0].Flags = flags;

        world.Frame(lights, atlas, _ => 1f, []);

        WorldDynamicLightConformanceTests.Texel(atlas, 0, 0, 1, 2).ShouldBe((byte)red);
    }

    /// <summary>
    /// Luxel (0, 1) at (0, 16, 40), the light at (0, 48, 64): `d² = 1600`, `f = 0.0205078`, `1020 f = 20.92`; direction
    /// (0, 0.8, 0.6). The frame: S = (1, 0, 0), T = (0, 1, 0), N = (0, 0, 1). Page 0 takes `0.6 f` = 12.55 → 6 (a flat
    /// reading's `f` would be 10); page 1 `0.34641 f` = 7.25 → 3; page 2 `(0.56569 + 0.34641) f` = 19.08 → 9; page 3 faces
    /// away.
    /// </summary>
    [Test]
    public void Frame_ABumpedDisplacementLuxelLitAtASlant_TakesTheDisplacementsOwnBasisShares()
    {
        LightmapAtlas atlas = WorldDynamicLightConformanceTests.Atlas(bumped: true, size: 2);
        WorldDynamicLights world = new(Terrain());

        world.Frame(Lights((0f, 48f, 64f)), atlas, _ => 1f, []);

        WorldDynamicLightConformanceTests.Texel(atlas, 0, 0, 1, 2).ShouldBe((byte)6);
        WorldDynamicLightConformanceTests.Texel(atlas, 1, 0, 1, 2).ShouldBe((byte)3);
        WorldDynamicLightConformanceTests.Texel(atlas, 2, 0, 1, 2).ShouldBe((byte)9);
        WorldDynamicLightConformanceTests.Texel(atlas, 3, 0, 1, 2).ShouldBe((byte)0);
    }

    /// <summary>
    /// The light leans along S: from luxel (0, 1) the direction is (0.6, 0, 0.8), `f` as above. With S = +x (the flip
    /// `(sAxis × tAxis)·N &gt; 0` applied) page 0 takes `0.8 f` = 16.73 → 8, page 1 `(0.48990 + 0.46188) f` = 19.91 → 9,
    /// pages 2 and 3 `(−0.24495 + 0.46188) f` = 4.54 → 2. Unflipped, page 1 would face away and page 2 take 7.
    /// </summary>
    [Test]
    public void Frame_ABumpedDisplacementLuxelLitAlongItsTextureS_TakesTheFlippedTangent()
    {
        LightmapAtlas atlas = WorldDynamicLightConformanceTests.Atlas(bumped: true, size: 2);
        WorldDynamicLights world = new(Terrain());

        world.Frame(Lights((24f, 16f, 72f)), atlas, _ => 1f, []);

        WorldDynamicLightConformanceTests.Texel(atlas, 0, 0, 1, 2).ShouldBe((byte)8);
        WorldDynamicLightConformanceTests.Texel(atlas, 1, 0, 1, 2).ShouldBe((byte)9);
        WorldDynamicLightConformanceTests.Texel(atlas, 2, 0, 1, 2).ShouldBe((byte)2);
        WorldDynamicLightConformanceTests.Texel(atlas, 3, 0, 1, 2).ShouldBe((byte)2);
    }

    /// <summary>
    /// The head node's box is only (0, 0, −8)–(8, 8, 8): a light at (80, 80, 0) is 72 off it on two axes, `10368 ≥ 10000`,
    /// so `0x180172d10` refuses the walk — though the face's own test (one luxel diagonal past its rectangle, inside a
    /// circle of 6.25) would have taken it.
    /// </summary>
    [Test]
    public void Frame_ALightTheHeadNodesBoxRefuses_IsNotWalkedIntoTheDoorEvenWhereTheFaceWouldTakeIt()
    {
        DecalWorld door = Door();
        DecalWorld tiny = door with
        {
            Nodes = [door.Nodes[0], door.Nodes[1] with { Maxs = new Vector3(8f, 8f, 8f) }],
        };
        WorldDynamicLights world = new(tiny);
        LitBrush still = new(1, Vector3.Zero, Vector3.Zero);

        world.Frame(Lights((80f, 80f, 0f)), WorldDynamicLightConformanceTests.Atlas(), _ => 1f, [], [still]);
        world.Bits(0).ShouldBe(0u);

        // The control: the step-2 box takes the same light.
        WorldDynamicLights control = new(door);

        control.Frame(Lights((80f, 80f, 0f)), WorldDynamicLightConformanceTests.Atlas(), _ => 1f, [], [still]);
        control.Bits(0).ShouldBe(1u);
    }

    /// <summary>
    /// The door's floor is the step-2 floor in the model's own space; the entity stands at (100, 0, 0) turned 90° in yaw.
    /// The light at world (68, 32, 24) is `Rᵀ((−32, 32, 24))` = (32, 32, 24) in the model, over luxel (2, 2) — so the
    /// step-2 floor's 32. Read in world space it would be 36 units off that luxel's column.
    /// </summary>
    [Test]
    public void Frame_ALightOverATurnedDoor_LightsItsFaceInTheModelsSpace()
    {
        LightmapAtlas atlas = WorldDynamicLightConformanceTests.Atlas();
        WorldDynamicLights world = new(Door());

        world.Frame(Lights((68f, 32f, 24f)), atlas, _ => 1f, [], [Turned]);

        WorldDynamicLightConformanceTests.Texel(atlas, 0, 2, 2).ShouldBe((byte)32);
        WorldDynamicLightConformanceTests.Texel(atlas, 0, 0, 0).ShouldBe((byte)5);
        world.Bits(0).ShouldBe(1u);
    }

    [Test]
    public void Frame_ADoorNobodyDraws_IsNotLitThroughTheWorldsWalk()
    {
        LightmapAtlas atlas = WorldDynamicLightConformanceTests.Atlas();
        WorldDynamicLights world = new(Door());

        world.Frame(Lights((68f, 32f, 24f)), atlas, _ => 1f, []);

        world.Bits(0).ShouldBe(0u);
        WorldDynamicLightConformanceTests.Texel(atlas, 0, 2, 2).ShouldBe((byte)0);
    }

    /// <summary>The head node's box is z −8 to 8: a light 108 above the door's plane misses it (a gap of exactly 100 is not inside).</summary>
    [Test]
    public void Frame_ALightOutsideTheDoorsHeadNodeBox_MarksNothing()
    {
        WorldDynamicLights world = new(Door());

        world.Frame(Lights((68f, 32f, 108f)), WorldDynamicLightConformanceTests.Atlas(), _ => 1f, [], [Turned]);

        world.Bits(0).ShouldBe(0u);
    }

    /// <summary>The light leaves the door: the face still carries its bit, so it is rebuilt baked once, and then left.</summary>
    [Test]
    public void Frame_AfterTheLightLeavesTheDoor_RebuildsItsFaceBakedOnceThenLeavesIt()
    {
        LightmapAtlas atlas = WorldDynamicLightConformanceTests.Atlas();
        WorldDynamicLights world = new(Door());
        DynamicLights lights = Lights((68f, 32f, 24f));
        List<AtlasRegion> dirty = [];

        world.Frame(lights, atlas, _ => 1f, dirty, [Turned]);
        WorldDynamicLightConformanceTests.Texel(atlas, 0, 2, 2).ShouldBe((byte)32);

        (lights.Dlights[0].X, lights.Dlights[0].Y, lights.Dlights[0].Z) = (68f, 32f, 1000f);
        dirty.Clear();
        world.Frame(lights, atlas, _ => 1f, dirty, [Turned]);

        dirty.Count.ShouldBe(1);
        WorldDynamicLightConformanceTests.Texel(atlas, 0, 2, 2).ShouldBe((byte)0);
        world.Bits(0).ShouldBe(0u);

        dirty.Clear();
        world.Frame(lights, atlas, _ => 1f, dirty, [Turned]);

        dirty.ShouldBeEmpty();
    }

    [Test]
    public void BrushesOf_ADrawnSubmodel_IsItsModelsHeadNodeWhereItStands()
    {
        SceneProp door = new(
            7, "*1", SceneModelKind.Brush, new ScenePose { X = 100f, Y = 2f, Z = 3f, Pitch = 4f, Yaw = 90f, Roll = 5f });
        SceneProp crate = new(8, "models/crate.mdl", SceneModelKind.Studio, new ScenePose());
        BspModel[] models = [default, new((0f, 0f, 0f), (0f, 0f, 0f), (0f, 0f, 0f), 12, 0, 1)];

        IReadOnlyList<LitBrush> brushes =WorldDynamicLights.BrushesOf([door, crate], models);

        brushes.ShouldHaveSingleItem().ShouldBe(new LitBrush(12, new Vector3(100f, 2f, 3f), new Vector3(4f, 90f, 5f)));
    }

    /// <summary>The door at (100, 0, 0), turned 90° in yaw.</summary>
    private static readonly LitBrush Turned = new(1, new Vector3(100f, 0f, 0f), new Vector3(0f, 90f, 0f));

    /// <summary>A white dlight of radius 100 at 2^2 in slot 0, alive until 2 at time 1.</summary>
    private static DynamicLights Lights((float X, float Y, float Z) at)
    {
        DynamicLights lights = new() { Time = 1f };
        DynamicLight light = lights.AllocDlight(1);
        (light.X, light.Y, light.Z) = at;
        light.Radius = 100f;
        (light.Red, light.Green, light.Blue, light.Exponent) = (255, 255, 255, 2);
        light.Die = 2f;

        return lights;
    }

    /// <summary>
    /// A 2×2-luxel displacement on face 0, its parent quad on z = 0 with texture axes x and y; its luxels at (0, 0, 0),
    /// (16, 0, 0), (0, 16, 40) and (16, 16, 0). The root splits on x = 1000, so every walk falls to leaf 0, which holds it.
    /// </summary>
    internal static DecalWorld Terrain() =>
        new([new DecalNode(-1, -1, Vector3.UnitX, 1000f, 0, 0)], [Array.Empty<int>()], [Parent()])
        {
            Displacements =
            [
                new DecalDisplacement(0, Vector3.Zero, new Vector3(16f, 16f, 40f), [])
                {
                    Luxels = [Vector3.Zero, new(16f, 0f, 0f), new(0f, 16f, 40f), new(16f, 16f, 0f)],
                },
            ],
        };

    /// <summary>The displacement's parent face.</summary>
    internal static DecalFace Parent() =>
        WorldDynamicLightConformanceTests.Floor(2) with
        {
            OnNode = false,
            Displacement = true,
            TextureS = new Vector4(1f, 0f, 0f, 0f),
            TextureT = new Vector4(0f, 1f, 0f, 0f),
        };

    /// <summary>
    /// Node 0 is the world, with no faces, leading to empty leaf 0 both ways; node 1 is a door's model holding the step-2
    /// floor, its box ±8 about its plane.
    /// </summary>
    internal static DecalWorld Door() =>
        new(
            [
                new DecalNode(-1, -1, Vector3.UnitZ, -10000f, 0, 0),
                new DecalNode(-1, -1, Vector3.UnitZ, 0f, 0, 1, new Vector3(0f, 0f, -8f), new Vector3(80f, 80f, 8f)),
            ],
            [Array.Empty<int>()],
            [WorldDynamicLightConformanceTests.Floor()]);
}
