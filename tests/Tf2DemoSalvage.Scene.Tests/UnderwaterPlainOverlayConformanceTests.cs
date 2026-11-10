using System;
using System.Text;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Content.Tests.Assets;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// What <c>DrawUnderwaterOverlay</c>'s plain rectangle draws for a material (`viewrender.cpp:1332-1338`, B660): its base
/// texture under <c>EvaluateBlendRequirements</c>'s blend (`docs/findings/17-translucency.md`).
/// </summary>
public sealed class UnderwaterPlainOverlayConformanceTests
{
    private const uint EightBitAlpha = 0x2000;

    private static VmtMaterial Material(string shader, string body) =>
        VmtMaterial.Parse(Encoding.UTF8.GetBytes($"\"{shader}\"\n{{\n\"$basetexture\" \"effects/plain\"\n{body}\n}}"));

    /// <summary>A 2 × 2 RGBA VTF whose header carries <paramref name="flags"/> (the 32-bit word at byte 20).</summary>
    private static VtfTexture Texture(uint flags)
    {
        byte[] file = VtfFixture.Build(VtfFormat.Rgba8888, 2, 2, 1, new byte[16]);

        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(20), flags);

        VtfTexture texture = VtfTexture.Read(file);

        texture.Flags.ShouldBe(flags, "the fixture's flags reached the decoded texture");

        return texture;
    }

    /// <remarks>
    /// **`$translucent` blends only over a texture with alpha, and never with `$alphatest`**; `$alpha` below 1 or
    /// `$vertexalpha` blend whatever the texture. Each row is one of `EvaluateBlendRequirements`' routes, or its absence.
    /// </remarks>
    [TestCase("\"$translucent\" 1", EightBitAlpha, true)]
    [TestCase("\"$translucent\" 1", 0u, false)]
    [TestCase("\"$translucent\" 1\n\"$alphatest\" 1", EightBitAlpha, false)]
    [TestCase("\"$alpha\" 0.5", 0u, true)]
    [TestCase("\"$vertexalpha\" 1", 0u, true)]
    [TestCase("", EightBitAlpha, false)]
    public void PlainOf_ARoute_IsTranslucentOnlyByIt(string body, uint flags, bool translucent)
    {
        MapTexture plain = MapAssets.PlainOf(Material("UnlitGeneric", body), _ => Texture(flags)).ShouldNotBeNull();

        plain.IsTranslucent.ShouldBe(translucent);
    }

    /// <remarks>`$additive` is `BT_ADD`, or `BT_BLENDADD` when also translucent.</remarks>
    [Test]
    public void PlainOf_AnAdditiveMaterial_IsAdditive()
    {
        MapAssets.PlainOf(Material("UnlitGeneric", "\"$additive\" 1"), _ => Texture(0u)).ShouldNotBeNull().IsAdditive
            .ShouldBeTrue();
    }

    /// <remarks>
    /// **A base texture that does not load is the error texture**, whose procedural flags carry alpha
    /// (`InitProceduralTexture`, `materialsystem.dll` <c>0x18003a700</c>, sets <c>EIGHTBITALPHA</c> for BGRA8888): so
    /// `$translucent` blends it and a material without it does not. The texture is the 32-texel checkerboard.
    /// </remarks>
    [TestCase("\"$translucent\" 1", true)]
    [TestCase("", false)]
    public void PlainOf_ABaseTextureThatDoesNotLoad_IsTheErrorTextureWithAlpha(string body, bool translucent)
    {
        MapTexture plain = MapAssets.PlainOf(Material("UnlitGeneric", body), _ => null).ShouldNotBeNull();

        (plain.Width, plain.IsTranslucent).ShouldBe((ErrorTexture.Size, translucent));
        plain.Image.Top.ToArray().ShouldBe(ErrorTexture.Rgba());
    }

    /// <remarks>
    /// **A `Refract` material takes the frame-copy branch**, whether or not this port could read its parameters, so it has
    /// no plain rectangle; nor does a material with no base texture. The control is the same body under UnlitGeneric.
    /// </remarks>
    [Test]
    public void PlainOf_ARefractOrNoBaseTexture_IsNull()
    {
        MapAssets.PlainOf(Material("Refract", string.Empty), _ => Texture(0u)).ShouldBeNull();
        MapAssets.PlainOf(VmtMaterial.Parse(Encoding.UTF8.GetBytes("\"UnlitGeneric\"\n{\n}")), _ => Texture(0u)).ShouldBeNull();
        MapAssets.PlainOf(Material("UnlitGeneric", string.Empty), _ => Texture(0u)).ShouldNotBeNull();
    }
}
