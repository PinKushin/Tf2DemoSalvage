using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// A static prop whose model draws a phong or bump-mapped material is NOT lit by its baked vertex colours (B514).
/// </summary>
/// <remarks>
/// Three readings, one chain.
///
/// *Read in disassembly*, `studiorender.dll FUN_180057da0` (the model's materials loading), per texture: a `$bumpmap`
/// that is defined on a material needing a tangent space, OR a `$phong` that is defined with a nonzero integer value,
/// sets `studiohdr_t.flags |= 0x80` — `STUDIOHDR_FLAGS_USES_BUMPMAPPING` (`studio.h:2051`, "set at loadtime").
///
/// *Read in disassembly*, `engine.dll FUN_1800eb1a0` (the model draw's caller): static lighting needs draw flag 8, the
/// model's `STUDIOHDR_FLAGS_STATIC_PROP`, an instance, `SupportsColorOnSecondStream`, and NOT
/// (`GetDXSupportLevel() >= 80` and `model->flags &amp; 0x200`, `MODELFLAG_STUDIOHDR_USES_BUMPMAPPING`). Refused, no
/// colour mesh is handed to the studio render and the model draw `0x1800f1bd0` takes `FUN_1801ba590(handle, …, 7)`:
/// the handle's static state, its styled lights and its dlights.
///
/// *Read from published source*, `skin_ps20b.fxc:220-222`: the phong shader's diffuse is
/// `PixelShaderDoLighting( …, float3( 0, 0, 0 ), false, true, …, true, … )` — a zero static colour, no static light,
/// the ambient cube, and half-Lambert always. It could not draw a baked colour if it were handed one.
/// </remarks>
public sealed class BumpMappedStaticPropLightingConformanceTests
{
    private const int Style = 5;

    [TestCase("\"$phong\" \"1\"", true)]
    [TestCase("\"$phong\" \"0\"", false)]
    [TestCase("\"$bumpmap\" \"models/x_normal\"", true)]
    [TestCase("\"$phongexponent\" \"10\"", false)]
    public void MarksModelBumpMapped_EachOfTheTwoVariablesStudioRenderTests_FlagsTheModel(string line, bool expected) =>
        Material(line).MarksModelBumpMapped.ShouldBe(expected);

    /// <remarks>
    /// `WantsSkinShader`, `vertexlitgeneric_dx9_helper.cpp:38-59`: phong on, then a light warp, or
    /// `$basemapalphaphongmask 1`, or a bump map. Phong with none of the three stays on the generic shader.
    /// </remarks>
    [TestCase("\"$phong\" \"1\"\n\"$basemapalphaphongmask\" \"1\"", true)]
    [TestCase("\"$phong\" \"1\"\n\"$bumpmap\" \"models/x_normal\"", true)]
    [TestCase("\"$phong\" \"1\"\n\"$lightwarptexture\" \"models/x_warp\"", true)]
    [TestCase("\"$phong\" \"1\"", false)]
    [TestCase("\"$phong\" \"0\"\n\"$basemapalphaphongmask\" \"1\"", false)]
    [TestCase("\"$basemapalphaphongmask\" \"1\"\n\"$bumpmap\" \"models/x_normal\"", false)]
    public void UsesSkinShader_EachBranchOfWantsSkinShader_IsItsAnswer(string lines, bool expected) =>
        Material(lines).UsesSkinShader.ShouldBe(expected);

    /// <remarks>`0x10` is `STUDIOHDR_FLAGS_STATIC_PROP`, `0x80` `STUDIOHDR_FLAGS_USES_BUMPMAPPING`.</remarks>
    [TestCase(0x10, false)]
    [TestCase(0x90, true)]
    [TestCase(0x80, true)]
    [TestCase(0x2010, false)]
    public void RefusesColourMesh_AModelFlaggedBumpMapped_IsRefusedItsStaticLighting(int studioFlags, bool expected) =>
        StaticPropVertexLighting.RefusesColourMesh(studioFlags).ShouldBe(expected);

    /// <summary>Flags 7 with nothing styled or dynamic in reach: the static state itself, cube and style-0 lamp.</summary>
    [Test]
    public void StaticPropFullLightingAt_OnlyAStyleZeroLamp_IsTheHandlesStaticState()
    {
        LevelLighting lighting = StaticPlusDynamicLightingConformanceTests.Map(
            [StaticPlusDynamicLightingConformanceTests.Lamp(0, cluster: 0)]);

        (PointLighting full, SunLight? sun) = lighting.StaticPropFullLightingAt(1000, 0f, 0f, 100f);
        (PointLighting fixedState, _) = lighting.StaticPropLightingAt(0f, 0f, 100f);

        full.Cube.ShouldBe(fixedState.Cube);
        full.Locals.ShouldBe(fixedState.Locals);
        full.Locals.ShouldHaveSingleItem();
        sun.ShouldBeNull("this map has no sun");
    }

    /// <summary>
    /// Flags 7 beside a style-0 lamp and a styled one: both, where flags 6 (<see cref="LevelLighting.StaticPlusDynamicLights"/>)
    /// hold the styled lamp alone and flags 1 (<see cref="LevelLighting.StaticPropLightingAt"/>) the style-0 one alone.
    /// </summary>
    [Test]
    public void StaticPropFullLightingAt_AStyleZeroLampAndAStyledLamp_HoldsBothAndTheCube()
    {
        LevelLighting lighting = StaticPlusDynamicLightingConformanceTests.Map(
        [
            StaticPlusDynamicLightingConformanceTests.Lamp(0, cluster: 0) with { Origin = (0f, 30f, 120f) },
            StaticPlusDynamicLightingConformanceTests.Lamp(Style, cluster: 0),
        ]);

        lighting.StaticPropLightingAt(0f, 0f, 100f).Lighting.Locals.ShouldHaveSingleItem().Y.ShouldBe(30f);
        lighting.StaticPlusDynamicLights(1000, 0f, 0f, 100f).ShouldHaveSingleItem().Y.ShouldBe(0f);

        (PointLighting full, _) = lighting.StaticPropFullLightingAt(1000, 0f, 0f, 100f);

        full.Locals.Select(light => light.Y).Order().ShouldBe([0f, 30f]);
        full.Cube.ShouldBe(lighting.StaticPropLightingAt(0f, 0f, 100f).Lighting.Cube);
    }

    /// <summary>The styled lamp is taken at its current value: switched off (`'a'`, 0), flags 7 hold the style-0 lamp alone.</summary>
    [Test]
    public void StaticPropFullLightingAt_AStyledLampSwitchedOff_HoldsOnlyTheStyleZeroLamp()
    {
        LevelLighting lighting = StaticPlusDynamicLightingConformanceTests.Map(
        [
            StaticPlusDynamicLightingConformanceTests.Lamp(0, cluster: 0) with { Origin = (0f, 30f, 120f) },
            StaticPlusDynamicLightingConformanceTests.Lamp(Style, cluster: 0),
        ]);

        lighting.StyleScale = static style => style == Style ? 0f : 1f;

        lighting.StaticPropFullLightingAt(1000, 0f, 0f, 100f).Lighting.Locals.ShouldHaveSingleItem().Y.ShouldBe(30f);
    }

    private static VmtMaterial Material(string lines) =>
        VmtMaterial.Parse(System.Text.Encoding.UTF8.GetBytes($"\"VertexLitGeneric\"\n{{\n\"$basetexture\" \"models/x\"\n{lines}\n}}\n"));
}
