using System.Text;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// <see cref="ParticleMaterial.IsTranslucentOf"/>: <c>IMaterial::IsTranslucent()</c> per shader, which decides whether a
/// particle definition's effect is a translucent renderable (B734).
/// </summary>
/// <remarks>
/// <c>CMaterial::IsTranslucentInternal</c> (materialsystem.dll <c>0x180008440</c>): the render state's translucency, alpha
/// modulation below one, or <c>MATERIAL_VAR_TRANSLUCENT</c>. <c>SpriteCard</c> blends in every branch
/// (`spritecard.cpp:255-267`); <c>Refract</c> sets the flag on every material (`refract_dx9_helper.cpp:22`); <c>Sprite</c>
/// blends in every mode but <c>kRenderNormal</c>; the rest take <c>EvaluateBlendRequirements</c> (`BaseVSShader.cpp:1580`).
/// </remarks>
public sealed class ParticleMaterialTranslucencyConformanceTests
{
    [TestCase("\"SpriteCard\" { \"$basetexture\" \"a\" }", false, true)]
    [TestCase("\"Refract\" { \"$normalmap\" \"a\" }", false, true)]
    [TestCase("\"Sprite\" { \"$basetexture\" \"a\" }", true, false)]
    [TestCase("\"Sprite\" { \"$spriterendermode\" \"5\" }", false, true)]
    [TestCase("\"UnlitGeneric\" { \"$basetexture\" \"a\" }", false, false)]
    [TestCase("\"UnlitGeneric\" { \"$vertexalpha\" \"1\" }", false, true)]
    [TestCase("\"UnlitGeneric\" { \"$basetexture\" \"a\" }", true, true)]
    [TestCase("\"UnlitGeneric\" { \"$alphatest\" \"1\" }", true, false)]
    [TestCase("\"UnlitGeneric\" { \"$translucent\" \"1\" }", false, true)]
    [TestCase("\"UnlitGeneric\" { \"$additive\" \"1\" }", false, true)]
    [TestCase("\"UnlitGeneric\" { \"$alpha\" \".5\" }", false, true)]
    [TestCase("\"UnlitGeneric\" { \"$vertexalpha\" \"0\" }", false, false)]
    public void IsTranslucentOf_AShaderAndItsFlags_IsTheMaterialSystemsAnswer(string vmt, bool textureHasAlpha, bool expected) =>
        ParticleMaterial.IsTranslucentOf(VmtMaterial.Parse(Encoding.ASCII.GetBytes(vmt)), textureHasAlpha).ShouldBe(expected);

    /// <remarks>A material that did not resolve keeps the translucent default every material had before B734.</remarks>
    [Test]
    public void IsTranslucentOf_NoMaterial_IsTranslucent() => ParticleMaterial.IsTranslucentOf(null, false).ShouldBeTrue();
}
