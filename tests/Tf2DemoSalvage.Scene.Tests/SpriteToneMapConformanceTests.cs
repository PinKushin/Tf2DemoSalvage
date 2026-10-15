using System.Collections.Generic;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>Which shaders' linear path ends in <c>TONEMAP_SCALE_LINEAR</c> (B378), keyed on the material's shader.</summary>
/// <remarks>
/// <code>
/// spritecard_ps2x.fxc:192                       FinalOutput( …, TONEMAP_SCALE_LINEAR )
/// vertexlit_and_unlit_generic_ps2x.fxc:498,504  FinalOutput( …, TONEMAP_SCALE_LINEAR )    // UnlitGeneric
/// cable_ps2x.fxc:54                             FinalOutput( …, TONEMAP_SCALE_LINEAR, … )
/// sprite_ps2x.fxc:49                            FinalOutput( …, TONEMAP_SCALE_LINEAR )    // the SRGB path
/// unlittwotexture_ps2x.fxc:73                   FinalOutput( …, TONEMAP_SCALE_LINEAR, … )
/// refract_ps2x.fxc:207,249                      FinalOutput( …, TONEMAP_SCALE_NONE … )
/// </code>
/// </remarks>
public sealed class SpriteToneMapConformanceTests
{
    [TestCase("SpriteCard", true)]
    [TestCase("UnlitGeneric", true)]
    [TestCase("Cable", true)]
    [TestCase("Sprite", true)]
    [TestCase("UnlitTwoTexture", true)]
    [TestCase("Refract", false)]
    [TestCase("refract", false)]
    public void ScalesByToneMapOf_AShader_IsItsFinalOutputMode(string shader, bool expected) =>
        ParticleMaterial.ScalesByToneMapOf(shader).ShouldBe(expected);

    [Test]
    public void ScalesByToneMap_AMaterialOfTheRefractShader_TakesNoScale() =>
        (new ParticleMaterial(null, new List<SheetSequence>(), SpriteBlend.Translucent) { Shader = "Refract" })
            .ScalesByToneMap.ShouldBeFalse();

    [Test]
    public void ScalesByToneMap_ASpriteCardMaterial_TakesTheScale() =>
        (new ParticleMaterial(null, new List<SheetSequence>(), SpriteBlend.Translucent) { Shader = "SpriteCard" })
            .ScalesByToneMap.ShouldBeTrue();
}
