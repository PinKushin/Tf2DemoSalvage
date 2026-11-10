using System;
using System.Buffers.Binary;
using System.Text;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// <c>EvaluateBlendRequirements</c> with the texture in hand (`docs/findings/17-translucency.md`): <c>$translucent</c>
/// blends only over a texture whose flags say it has alpha (B713).
/// </summary>
public sealed class TextureTranslucencyConformanceTests
{
    private static VmtMaterial Material(string body) =>
        VmtMaterial.Parse(Encoding.UTF8.GetBytes("\"LightmappedGeneric\"\n{\n" + body + "\n}"));

    /// <remarks>
    /// <c>ITexture::IsTranslucent</c> is <c>( flags &amp; 0x3000 ) != 0</c> (`materialsystem.dll` <c>0x18003acd0</c>, read in
    /// disassembly): <c>ONEBITALPHA</c> or <c>EIGHTBITALPHA</c> (`vtf.h:49-50`), and no other bit.
    /// </remarks>
    [TestCase(0x1000u, true)]
    [TestCase(0x2000u, true)]
    [TestCase(0x0u, false)]
    [TestCase(0x4000u, false)]
    public void HasAlpha_AFlagsWord_IsTheTwoAlphaBits(uint flags, bool expected)
    {
        byte[] file = VtfFixture.Build(VtfFormat.Rgba8888, 2, 2, 1, new byte[16]);

        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(20), flags);

        VtfTexture.Read(file).HasAlpha.ShouldBe(expected);
    }

    /// <remarks>
    /// **The routes**: `$translucent` needs the texture's alpha; `$alpha` below 1 and `$vertexalpha` do not; `$alphatest`
    /// cancels every one. <see cref="VmtMaterial.IsTranslucent"/> is the same rule with alpha assumed.
    /// </remarks>
    [TestCase("\"$translucent\" 1", true, true)]
    [TestCase("\"$translucent\" 1", false, false)]
    [TestCase("\"$alpha\" 0.5", false, true)]
    [TestCase("\"$vertexalpha\" 1", false, true)]
    [TestCase("\"$translucent\" 1\n\"$alphatest\" 1", true, false)]
    [TestCase("", true, false)]
    public void IsTranslucentOver_ARouteAndATexture_BlendsOnlyByTheEnginesRule(string body, bool textureHasAlpha, bool expected)
    {
        VmtMaterial material = Material(body);

        material.IsTranslucentOver(textureHasAlpha).ShouldBe(expected);
        material.IsTranslucent.ShouldBe(material.IsTranslucentOver(true));
    }
}
