using System.Text;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// The texture to draw for a material whose shader this renderer does not implement.
/// </summary>
/// <remarks>
/// **Not every material has a <c>$basetexture</c>.** TF2 paints eyes with <c>EyeRefract</c>, which
/// composes one from an iris, a cornea normal map, an occlusion map and a light warp — and names no
/// base texture at all. A loader that asks only for <c>$basetexture</c> finds nothing and draws the
/// missing-texture chequer, which is what put purple eyes on every player.
///
/// The answer is not to implement the shader. It is to draw the texture carrying the material's
/// colour, so that an eye looks like an eye rather than like a bug.
/// </remarks>
public sealed class VmtPrimaryTextureTests
{
    [Test]
    public void AnEyeMaterial_FallsBackToItsIris()
    {
        // TF2's real eye material, shortened. $Iris is the eye's colour; the rest shape it.
        VmtMaterial eye = Parse(
            """
            "EyeRefract"
            {
                "$Iris"          "models/player/shared/eye-iris-blue"
                "$CorneaTexture" "models/player/shared/eye-cornea"
            }
            """);

        eye.BaseTexture.ShouldBeNull("EyeRefract genuinely names no base texture");

        eye.PrimaryTexture.ShouldBe("models/player/shared/eye-iris-blue");
    }

    [Test]
    public void AnOrdinaryMaterial_StillUsesItsBaseTexture()
    {
        // **The control.** A fallback that fired for everything would quietly repaint surfaces with
        // whichever parameter happened to match first, and a wall is not an eye.
        VmtMaterial wall = Parse(
            """
            "LightmappedGeneric"
            {
                "$basetexture" "concrete/wall"
                "$iris"        "should/not/win"
            }
            """);

        wall.PrimaryTexture.ShouldBe("concrete/wall");
    }

    [Test]
    public void AMaterialWithNeither_HasNoPrimaryTexture()
    {
        // A tool material names no texture of any kind, and inventing one would paint a surface the
        // engine deliberately leaves blank.
        VmtMaterial tool = Parse(
            """
            "UnlitGeneric"
            {
                "%compilenodraw" "1"
            }
            """);

        tool.PrimaryTexture.ShouldBeNull();
    }

    [Test]
    public void VmtPrimaryTexture_AModulateMaterial_IsNotOpaque()
    {
        // **The exact material that made a capture point a dark slab**, reduced to the keys that
        // decide it. Modulate declares neither $translucent nor $additive and its $alpha is a
        // proxy result rather than a constant below one, so every predicate this project had
        // answered "opaque" — and a shader whose whole purpose is to multiply what is behind it was
        // painted as solid geometry over the sign it was meant to shade.
        //
        // The control is the lit half of the same pair: it must stay additive, or fixing the dark
        // one would simply move the fault.
        VmtMaterial dark = Parse(
            """
            "Modulate"
            {
                "$basetexture" "models/effects/cappoint_logo_blue"
                "$modblend" ".63"
                "$model" "1"
                "$mod2x" "1"
            }
            """);

        dark.IsModulate.ShouldBeTrue();
        dark.IsModulateTwice.ShouldBeTrue();
        dark.IsAdditive.ShouldBeFalse();

        VmtMaterial lit = Parse(
            """
            "UnLitTwoTexture"
            {
                "$basetexture" "models/effects/cappoint_logo_blue"
                "$additive" "1"
                "$model" "1"
            }
            """);

        lit.IsModulate.ShouldBeFalse();
        lit.IsAdditive.ShouldBeTrue();
    }

    [Test]
    public void VmtPrimaryTexture_AModulateWithoutModTwice_DoesNotDouble()
    {
        // $mod2x is what lets a modulating material brighten as well as darken, so the two want
        // different blend factors and cannot be collapsed into one flag.
        VmtMaterial once = Parse(
            """
            "Modulate"
            {
                "$basetexture" "models/effects/smoke"
            }
            """);

        once.IsModulate.ShouldBeTrue();
        once.IsModulateTwice.ShouldBeFalse();
    }

    private static VmtMaterial Parse(string text) => VmtMaterial.Parse(Encoding.UTF8.GetBytes(text));
}
