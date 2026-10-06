using System.Text;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// VertexLitGeneric's cloak pass (`cloak_blended_pass_helper.cpp`, `vertexlitgeneric_dx9.cpp:464-519`) and the
/// `spy_invis` proxy's team tint (`c_tf_player.cpp:1720-1755`).
/// </summary>
public sealed class CloakPassConformanceTests
{
    [Test]
    public void DrawsStandardPass_UpToFourNinths_IsTrue()
    {
        // CloakBlendedPassIsFullyOpaque: clamp( Lerp( cf, 1, 1 − 1.35 ) ) ≤ 0.4 skips the standard pass, i.e. cf ≥ 0.6 / 1.35.
        CloakPass.DrawsStandardPass(0f).ShouldBeTrue();
        CloakPass.DrawsStandardPass(0.44f).ShouldBeTrue();
        CloakPass.DrawsStandardPass(0.45f).ShouldBeFalse();
        CloakPass.DrawsStandardPass(1f).ShouldBeFalse();
    }

    [Test]
    public void DrawsCloakPass_OnlyStrictlyBetweenZeroAndOne_IsTrue()
    {
        // ( CLOAKFACTOR > 0.0f ) && ( CLOAKFACTOR < 1.0f ) (vertexlitgeneric_dx9.cpp:512).
        CloakPass.DrawsCloakPass(0f).ShouldBeFalse();
        CloakPass.DrawsCloakPass(0.001f).ShouldBeTrue();
        CloakPass.DrawsCloakPass(0.999f).ShouldBeTrue();
        CloakPass.DrawsCloakPass(1f).ShouldBeFalse();
    }

    [Test]
    public void TeamTint_RedAndElse_AreSpyInvisColours()
    {
        CloakPass.TeamTint(2).ShouldBe((1f, 0.5f, 0.4f));
        CloakPass.TeamTint(3).ShouldBe((0.4f, 0.5f, 1f));
        CloakPass.TeamTint(0).ShouldBe((0.4f, 0.5f, 1f));
    }

    [Test]
    public void Read_NoCloakPassEnabled_IsNull()
    {
        CloakPass.Read(Material("\"$basetexture\" \"a\"")).ShouldBeNull();
        CloakPass.Read(Material("\"$cloakpassenabled\" \"0\"")).ShouldBeNull();
    }

    [Test]
    public void Read_Undeclared_TakesTheHelpersDefaults()
    {
        // kDefaultRefractAmount 0.1, kDefaultCloakColorTint 1 1 1, kDefaultCloakFactor 0 (cloak_blended_pass_helper.h).
        CloakPass.Read(Material("\"$cloakPassEnabled\" \"1\"")).ShouldBe(new CloakPass((1f, 1f, 1f), 0.1f, 0f));
    }

    [Test]
    public void Read_Declared_IsWhatTheMaterialSays()
    {
        CloakPass.Read(Material("\"$cloakpassenabled\" \"1\" \"$refractamount\" \"2\" \"$cloakfactor\" \".5\" \"$cloakcolortint\" \"[0.5 1 0.25]\""))
            .ShouldBe(new CloakPass((0.5f, 1f, 0.25f), 2f, 0.5f));
    }

    private static VmtMaterial Material(string body) =>
        VmtMaterial.Parse(Encoding.UTF8.GetBytes("\"VertexLitGeneric\" { " + body + " }"));
}
