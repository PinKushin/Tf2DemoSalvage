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

    private static VmtMaterial Parse(string text) => VmtMaterial.Parse(Encoding.UTF8.GetBytes(text));
}
