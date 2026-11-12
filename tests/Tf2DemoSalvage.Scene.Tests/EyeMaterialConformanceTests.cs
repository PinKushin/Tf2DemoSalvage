using System.Text;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>The face shaders' material parameters and their defaults (B725).</summary>
/// <remarks>
/// **Read**: `InitParams_Eyes_Refract` sets each parameter the material leaves undefined (`eye_refract_helper.cpp:43-87`) to
/// `eye_refract_helper.h:50-62`'s value — dilation 0.5, glossiness 1, sphere texkill 0, ray trace 0, occlusion colour 0.33,
/// eyeball radius 0.5, parallax 0.25, cornea bump 1; teeth's `$forward` is `[1 0 0]` and `$illumfactor` 1
/// (`teeth.cpp:38-39`), and `teeth_vs20` lights without half-Lambert whatever the flag (`teeth_vs20.fxc:109`).
/// </remarks>
public sealed class EyeMaterialConformanceTests
{
    [TestCase("EyeRefract", EyeShader.EyeRefract)]
    [TestCase("eyerefract_dx9", EyeShader.EyeRefract)]
    [TestCase("Eyes", EyeShader.Eyes)]
    [TestCase("Teeth", EyeShader.Teeth)]
    [TestCase("VertexLitGeneric", null)]
    public void ShaderOf_AShaderName_IsItsFaceShaderOrNone(string name, EyeShader? expected) =>
        EyeMaterial.ShaderOf(name).ShouldBe(expected);

    [Test]
    public void Read_AnEyeRefractStatingNothing_TakesTheHelpersDefaults()
    {
        EyeMaterial eye = EyeMaterial.Read(Parse("\"EyeRefract\" { }"), EyeShader.EyeRefract, null, null, null);

        eye.Dilation.ShouldBe(0.5f);
        eye.Glossiness.ShouldBe(1f);
        eye.CorneaBumpStrength.ShouldBe(1f);
        eye.AmbientOcclusionColour.ShouldBe((0.33f, 0.33f, 0.33f));
        eye.EyeballRadius.ShouldBe(0.5f);
        eye.ParallaxStrength.ShouldBe(0.25f);
        eye.RaytraceSphere.ShouldBeFalse();
        eye.SphereTexKill.ShouldBeFalse();
        eye.HalfLambert.ShouldBeFalse();
    }

    [Test]
    public void Read_AnEyeRefractStatingEverything_TakesWhatItStates()
    {
        EyeMaterial eye = EyeMaterial.Read(
            Parse(
                """
                "EyeRefract"
                {
                    "$Dilation" "0.3"
                    "$Glossiness" "0.7"
                    "$CorneaBumpStrength" "2"
                    "$AmbientOcclColor" "[0.1 0.2 0.4]"
                    "$EyeballRadius" "0.7"
                    "$ParallaxStrength" "0.6"
                    "$RaytraceSphere" "1"
                    "$SphereTexkillCombo" "1"
                    "$halflambert" "1"
                }
                """),
            EyeShader.EyeRefract,
            null,
            null,
            null);

        eye.Dilation.ShouldBe(0.3f);
        eye.Glossiness.ShouldBe(0.7f);
        eye.CorneaBumpStrength.ShouldBe(2f);
        eye.AmbientOcclusionColour.ShouldBe((0.1f, 0.2f, 0.4f));
        eye.EyeballRadius.ShouldBe(0.7f);
        eye.ParallaxStrength.ShouldBe(0.6f);
        eye.RaytraceSphere.ShouldBeTrue();
        eye.SphereTexKill.ShouldBeTrue();
        eye.HalfLambert.ShouldBeTrue();
    }

    [Test]
    public void Read_ASphereTexkillWithoutTheRayCast_IsOff()
    {
        // `SKIP: ( $RAYTRACESPHERE == 0 ) && ( $SPHERETEXKILLCOMBO == 1 )` (eye_refract_ps2x.fxc:32-33).
        EyeMaterial.Read(Parse("\"EyeRefract\" { \"$spheretexkillcombo\" \"1\" }"), EyeShader.EyeRefract, null, null, null)
            .SphereTexKill.ShouldBeFalse();
    }

    [Test]
    public void Read_TeethStatingNothingButHalfLambert_IsForwardXIllumOneAndNeverWraps()
    {
        EyeMaterial teeth = EyeMaterial.Read(Parse("\"Teeth\" { \"$halflambert\" \"1\" }"), EyeShader.Teeth, null, null, null);

        teeth.Forward.ShouldBe((1f, 0f, 0f));
        teeth.IllumFactor.ShouldBe(1f);
        teeth.HalfLambert.ShouldBeFalse();
    }

    [Test]
    public void Read_TeethStatingForwardAndIllumFactor_TakesThem()
    {
        EyeMaterial teeth = EyeMaterial.Read(
            Parse("\"Teeth\" { \"$forward\" \"[0 1 0]\" \"$illumfactor\" \"0.4\" }"), EyeShader.Teeth, null, null, null);

        teeth.Forward.ShouldBe((0f, 1f, 0f));
        teeth.IllumFactor.ShouldBe(0.4f);
    }

    private static VmtMaterial Parse(string text) => VmtMaterial.Parse(Encoding.UTF8.GetBytes(text));
}
