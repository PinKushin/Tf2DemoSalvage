using System.Text;
using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// A <c>Water</c> material's parameters as the shader sees them after <c>water.cpp</c>'s <c>SHADER_PARAM</c> defaults
/// and <c>SHADER_INIT_PARAMS</c> (<c>stdshaders/water.cpp:27-140</c>).
/// </summary>
public sealed class MapWaterConformanceTests
{
    private static MapWater Read(string body) =>
        MapWater.From(VmtMaterial.Parse(Encoding.UTF8.GetBytes("\"Water\"\n{\n" + body + "\n}")), null);

    [Test]
    public void From_NothingDeclared_TakesTheShadersDefaults()
    {
        MapWater water = Read(string.Empty);

        water.RefractTint.ShouldBe((1f, 1f, 1f));
        water.ReflectTint.ShouldBe((1f, 1f, 1f));
        water.RefractAmount.ShouldBe(0f);
        water.ReflectAmount.ShouldBe(0.8f);

        // "material %s needs to have a $fogcolor." — and it is set to red.
        water.FogColor.ShouldBe((1f, 0f, 0f));

        // "***need to set $abovewater for material %s" — and it is set to 1.
        water.AboveWater.ShouldBeTrue();
        water.Scroll1.ShouldBe((0f, 0f));
        water.ReflectBlendFactor.ShouldBe(1f);
        water.CheapWaterStart.ShouldBe(500f);
        water.CheapWaterEnd.ShouldBe(1000f);
        water.FollowsViewLod.ShouldBeFalse();
        water.BumpTransform.ShouldBe(TextureTransform.Identity);
    }

    [Test]
    public void From_TheTwoFortSurface_ReadsWhatItDeclares()
    {
        // ctf_2fort's water/water_2fort.vmt as the probe prints it, the parts the shader reads.
        MapWater water = Read(
            "\"$abovewater\" 1\n\"$refracttexture\" \"_rt_WaterRefraction\"\n\"$refractamount\" \".32\"\n" +
            "\"$refractblur\" 1\n\"$fogcolor\" \"{51 43 13}\"\n\"$fogstart\" -100\n\"$fogend\" 400\n" +
            "\"$bottommaterial\" \"water/water_2fort_beneath.vmt\"\n\"Proxies\"\n{\n\"WaterLOD\"\n{\n\"dummy\" 0\n}\n}");

        water.RefractAmount.ShouldBe(0.32f);
        water.FogColor.Red.ShouldBe(51f / 255f, 1e-6f);
        water.FogStart.ShouldBe(-100f);
        water.FogEnd.ShouldBe(400f);
        water.BottomMaterial.ShouldBe("water/water_2fort_beneath.vmt");
        water.FollowsViewLod.ShouldBeTrue();

        // **`$refractblur` is not a parameter the shader declares** — BLURRY_REFRACT reads `$blurrefract`.
        water.BlurRefract.ShouldBeFalse();
        water.View.RefractTexture.ShouldBeTrue();
        water.View.ForceExpensive.ShouldBeTrue();
    }

    [Test]
    public void From_ScrollAndBlur_SelectTheirCombos()
    {
        MapWater water = Read("\"$scroll1\" \"[.01 .02 .03]\"\n\"$blurrefract\" 1\n\"$nofresnel\" 1");

        water.Scroll1.ShouldBe((0.01f, 0.02f));
        water.BlurRefract.ShouldBeTrue();
        water.NoFresnel.ShouldBeTrue();
    }
}
