using System;
using System.Buffers.Binary;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// A static prop with a colour mesh on DX9 hardware: its colours PLUS the handle's styled and dynamic lights (B424, B425).
/// </summary>
/// <remarks>
/// **Read from `engine.dll`, the model draw `0x1800f1bd0`, lighting handle `param_4`, colour mesh `*param_7`.** When hardware
/// config slot `+0x150` (`SupportsStaticPlusDynamicLighting`, true on DX9) answers true the colour mesh is KEPT,
/// `param_8 + 0x40` is set to 1 (the studio render's static-plus-dynamic flag), and the draw's lighting state is
/// `FUN_1801ba590(handle, …, 6 + FUN_1801bb8a0(handle))` — `FUN_1801bb8a0` is `handle + 0x1a4 != 0`, the dlight bits.
///
/// `FUN_1801ba590` (`0x1801ba590`): without bit 1 the state starts at ZERO — no leaf cube, no style-0 light, since those
/// are what the colours already hold. Bit 4 accumulates `FUN_1801b5400`: every light of the handle's list (`+0x1b0`,
/// count `+0x1c0`, filled by `FUN_1801b6bf0` with the style-nonzero lights in PVS and reach) at its CURRENT style
/// value (`FUN_1800efbf0`), ranked into at most four local-light slots, a loser folded into the cube (`FUN_1801b5db0`).
/// Bit 2 adds each dlight whose bit is set in `+0x1a4` (masked by the live set `DAT_1806996c4`, cleared when the stamp
/// `+0x1a8` is stale), converted by `FUN_1801bb940` and ranked by `FUN_1801b6550` the same way. `FUN_1801b5260` sets
/// the bit when the dlight's PVS holds the prop.
///
/// **The non-DX9 branch** (slot `+0x150` false: `FUN_1801bb8a0` or `FUN_1801bb830` clears `*param_7` and takes full
/// lighting, flags 7) is what B424 first ported. TF2 on DX9 hardware never takes it, so it is not here.
///
/// **The shader adds the parts**: `PixelShaderDoLightingLinear` (`common_vertexlitgeneric_dx9.h:259-319`) is
/// `GammaToLinear( staticLightingColor * cOverbright ) + AmbientLight( cube ) + Σ light`, with `cOverbright` 2.0 (`:31`).
/// </remarks>
public sealed class StaticPlusDynamicLightingConformanceTests
{
    private const int Style = 5;

    /// <summary>A flicker pattern: more than one letter.</summary>
    internal const string Flicker = "mmamammmmammamamaaamammma";

    [Test]
    public void StyledLights_AStyledLampInPvsAndReach_IsListed()
    {
        LevelLighting lighting = Map([Lamp(Style, cluster: 0), Lamp(0, cluster: 0), Lamp(Style, cluster: 1), Lamp(Style, cluster: 0, radius: 10f)]);

        lighting.StyledLights(0f, 0f, 100f).ShouldBe([0], "style 0 is baked, cluster 1 is out of PVS, a radius of 10 does not reach");
    }

    /// <summary>Flags 6: no static state — the style-0 lamp beside the styled one is what the colours already hold.</summary>
    [Test]
    public void StaticPlusDynamicLights_FlagsSix_HoldTheStyledLampAndNoStaticState()
    {
        LevelLighting lighting = Map([Lamp(0, cluster: 0) with { Origin = (0f, 30f, 120f) }, Lamp(Style, cluster: 0)]);

        lighting.LightingAt(0f, 0f, 100f).Locals.Count.ShouldBe(2, "the control: both lamps reach the point");
        lighting.StaticPlusDynamicLights(1000, 0f, 0f, 100f).ShouldHaveSingleItem().Y.ShouldBe(0f, "only the styled lamp");
    }

    /// <summary>`FUN_1801b5400` takes each listed light at its current style value: `'g'` is 132 of 264, half.</summary>
    [Test]
    public void StaticPlusDynamicLights_AStyledLampAtHalfValue_IsHalfItsFullStrength()
    {
        LightStyleValues values = new();
        values.Set(Style, "g");
        values.Advance(0d);

        LevelLighting lighting = Map([Lamp(Style, cluster: 0)]);
        LocalLight full = lighting.StaticPlusDynamicLights(1000, 0f, 0f, 100f).ShouldHaveSingleItem();

        lighting.StyleScale = values.Scale;
        LocalLight half = lighting.StaticPlusDynamicLights(1000, 0f, 0f, 100f).ShouldHaveSingleItem();

        values.Scale(Style).ShouldBe(0.5f);
        half.ShouldBe(full with { Red = full.Red * 0.5f, Green = full.Green * 0.5f, Blue = full.Blue * 0.5f });
    }

    /// <summary>A switchable style at zero contributes nothing, so the colours stand alone.</summary>
    [Test]
    public void StaticPlusDynamicLights_AStyleSwitchedOff_HoldNoLight()
    {
        LevelLighting lighting = Map([Lamp(Style, cluster: 0)]);
        lighting.StyleScale = _ => 0f;

        lighting.StaticPlusDynamicLights(1000, 0f, 0f, 100f).ShouldBeEmpty();
    }

    /// <summary>Bit 2: a live dlight whose PVS holds the prop is ranked in; out of PVS or dead, it is not.</summary>
    [TestCase(120f, 1.0f, 1)]
    [TestCase(-20f, 1.0f, 0)]
    [TestCase(120f, 1.1f, 0)]
    public void StaticPlusDynamicLights_ADlight_IsRankedInOnlyWhileLiveAndInPvs(float z, float now, int expected)
    {
        LevelLighting lighting = Map([]);
        DynamicLights lights = new() { Time = 1f };
        lighting.Dynamic = lights;
        DynamicLight light = lights.AllocDlight(1);
        (light.X, light.Y, light.Z, light.Radius, light.Red, light.Die) = (0f, 0f, z, 255f, 255, 1.05f);

        lights.Time = now;
        lights.Decay(0.05f);

        lighting.StaticPlusDynamicLights(1000, 0f, 0f, 100f).Count.ShouldBe(expected);
    }

    /// <summary>
    /// A live dlight flagged `DLIGHT_NO_MODEL_ILLUMINATION` (0x2) is not ranked. *Interpolated:* the model draw's
    /// `0x1801b7a10` test, since the enumerator calling `FUN_1801b5260` is unread.
    /// </summary>
    [Test]
    public void StaticPlusDynamicLights_ADlightThatLightsNoModel_IsNotRanked()
    {
        LevelLighting lighting = Map([]);
        DynamicLights lights = new() { Time = 1f };
        lighting.Dynamic = lights;
        DynamicLight light = lights.AllocDlight(1);
        (light.X, light.Y, light.Z, light.Radius, light.Red, light.Die) = (0f, 0f, 120f, 255f, 255, 1.05f);

        lighting.StaticPlusDynamicLights(1000, 0f, 0f, 100f).Count.ShouldBe(1, "the control: unflagged, it lights");

        light.Flags = DynamicLights.NoModelIllumination;

        lighting.StaticPlusDynamicLights(1000, 0f, 0f, 100f).ShouldBeEmpty();
    }

    /// <summary>A lamp 20 units above the prop's lighting origin.</summary>
    internal static BspWorldLight Lamp(int style, int cluster, float radius = 0f) =>
        LevelLightingTests.Lamp((0f, 0f, 120f), 400f) with { Style = style, Cluster = cluster, Radius = radius };

    /// <summary>Two leaves split at z = 0: leaf 2 above in cluster 0, leaf 1 below in cluster 1, each seeing only itself.</summary>
    internal static LevelLighting Map(IReadOnlyList<BspWorldLight> lights)
    {
        byte[] plane = new byte[20];
        BinaryPrimitives.WriteSingleLittleEndian(plane.AsSpan(8), 1f);

        byte[] node = new byte[32];
        BinaryPrimitives.WriteInt32LittleEndian(node.AsSpan(4), -2 - 1);
        BinaryPrimitives.WriteInt32LittleEndian(node.AsSpan(8), -1 - 1);

        byte[] leaves = new byte[96];
        BinaryPrimitives.WriteInt32LittleEndian(leaves.AsSpan(0), 1);
        BinaryPrimitives.WriteInt16LittleEndian(leaves.AsSpan(4), -1);
        BinaryPrimitives.WriteInt16LittleEndian(leaves.AsSpan(32 + 4), 1);
        BinaryPrimitives.WriteInt16LittleEndian(leaves.AsSpan(64 + 4), 0);

        // numclusters, then (pvs, pas) offsets per cluster, then one literal row byte each.
        byte[] vis = new byte[22];
        BinaryPrimitives.WriteInt32LittleEndian(vis.AsSpan(0), 2);
        BinaryPrimitives.WriteInt32LittleEndian(vis.AsSpan(4), 20);
        BinaryPrimitives.WriteInt32LittleEndian(vis.AsSpan(8), 20);
        BinaryPrimitives.WriteInt32LittleEndian(vis.AsSpan(12), 21);
        BinaryPrimitives.WriteInt32LittleEndian(vis.AsSpan(16), 21);
        vis[20] = 0b01;
        vis[21] = 0b10;

        AmbientSamples samples = new(
            [new AmbientSample(default, 0.5f, 0.5f, 0.5f)], (-512f, -512f, -512f, 512f, 512f, 512f));

        return new LevelLighting(
            BspLeafTree.FromLumps(node, plane, leaves),
            [samples, samples, samples],
            lights,
            null,
            new RecordingLogger(),
            visibility: BspVisibility.FromLump(vis));
    }
}
