using System;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// An `UnlitGeneric` model material takes no light of any kind, and a model material's `$color` is a GAMMA colour (B514).
/// </summary>
/// <remarks>
/// *Read from published source*, all in `src/materialsystem/stdshaders`:
///
/// - **No light.** `vertexlitgeneric_dx9_helper.cpp:394` — `bool hasDiffuseLighting = bVertexLitGeneric;` — is the
///   pixel shader's `DIFFUSELIGHTING` combo (`:815`), and `vertexlit_and_unlit_generic_ps2x.fxc:344-348` starts the
///   diffuse at 1 and replaces it with the vertex shader's colour only under that combo or `$vertexcolor`. The light
///   state is never fetched either: `LightState_t lightState = { 0, false, false, false }; if ( bVertexLitGeneric … )
///   GetDX9LightState` (`:1250-1254`). So a static prop's baked colours, an ambient cube and a lamp all do nothing to
///   it. `UnlitTwoTexture` has no lighting input at all.
/// - **Gamma modulation.** `ComputeModulationColor` is `$color` times `$color2`, and every model shader hands it to the
///   pixel shader through `SetModulationPixelShaderDynamicState_LinearColorSpace` (`vertexlitgeneric_dx9_helper.cpp:1456`,
///   `skin_dx9_helper.cpp:731`, `unlittwotexture_dx9.cpp:218`), which is `color > 1 ? color : GammaToLinear( color )`
///   per channel (`BaseVSShader.cpp:652-661`). `GammaToLinear` (`mathlib/color_conversion.cpp:273-291`) is 0 below 0,
///   1 from 0.95 up, and otherwise a 256-entry table of `pow( i / 255, 2.2 )` read at `round( gamma * 255 )`.
///   `LightmappedGeneric` sends the colour as it is (`lightmappedgeneric_dx9_helper.cpp:677-688`).
///
/// The viewer multiplied the linear albedo by the raw gamma numbers and by whatever light the model draw carried.
/// On cp_process_f12's sky cards — placement #477, `skycards_forest512dip`, skin 2, `skycards_forest1b`,
/// `$color2 [.46 .46 .64]`, baked colours averaging (1.186 1.165 1.292) — that is (0.546 0.536 0.827) where the
/// engine's is (0.180 0.180 0.374): three times too bright, and lavender (`prop-shade cp_process_f12 skycards`).
/// </remarks>
public sealed class UnlitModelMaterialConformanceTests
{
    private const string Helper = "src/materialsystem/stdshaders/vertexlitgeneric_dx9_helper.cpp";

    [Test]
    public void Sdk_TheLinesThisPortRestsOn_AreAsQuoted()
    {
        string helper = Sdk(Helper);

        helper.ShouldContain("bool hasDiffuseLighting = bVertexLitGeneric;", Case.Sensitive);
        helper.ShouldContain("LightState_t lightState = { 0, false, false, false };", Case.Sensitive);
        helper.ShouldContain("pShader->SetModulationPixelShaderDynamicState_LinearColorSpace( 1 );", Case.Sensitive);

        Sdk("src/materialsystem/stdshaders/vertexlit_and_unlit_generic_ps2x.fxc")
            .ShouldContain("if( bDiffuseLighting || bVertexColor )", Case.Sensitive);
        Sdk("src/materialsystem/stdshaders/BaseVSShader.cpp")
            .ShouldContain("color[0] = color[0] > 1.0f ? color[0] : GammaToLinear( color[0] );", Case.Sensitive);
        Sdk("src/materialsystem/stdshaders/skin_dx9_helper.cpp")
            .ShouldContain("pShader->SetModulationPixelShaderDynamicState_LinearColorSpace( 1 );", Case.Sensitive);
        Sdk("src/materialsystem/stdshaders/unlittwotexture_dx9.cpp")
            .ShouldContain("SetModulationPixelShaderDynamicState_LinearColorSpace( 1 );", Case.Sensitive);
        Sdk("src/mathlib/color_conversion.cpp").ShouldContain("int index = RoundFloatToInt( gamma * 255.0f );", Case.Sensitive);
    }

    /// <remarks>
    /// 0.46 reads entry 117 (0.46 · 255 = 117.3) and 0.64 entry 163; 0.23 entry 59 (58.65 rounds up). 0.95 and 1 are 1
    /// outright; 0.949 is still the table (entry 242). Above 1 the channel is kept, which is how a material glows.
    /// </remarks>
    [TestCase(0.46f, 117)]
    [TestCase(0.64f, 163)]
    [TestCase(0.23f, 59)]
    [TestCase(0.949f, 242)]
    [TestCase(0f, 0)]
    public void ModulationToLinear_AChannelBelowPointNineFive_IsTheTableEntryAtItsRoundedByte(float gamma, int entry) =>
        ModelMaterialShading.ModulationToLinear(gamma).ShouldBe(MathF.Pow(entry / 255f, 2.2f), 1e-6f);

    [TestCase(0.95f, 1f)]
    [TestCase(1f, 1f)]
    [TestCase(-0.2f, 0f)]
    [TestCase(1.5f, 1.5f)]
    [TestCase(4f, 4f)]
    public void ModulationToLinear_AChannelOutsideTheTable_IsOneZeroOrItself(float gamma, float expected) =>
        ModelMaterialShading.ModulationToLinear(gamma).ShouldBe(expected);

    [TestCase("VertexLitGeneric", true)]
    [TestCase("UnlitGeneric", true)]
    [TestCase("UnLitGeneric", true)]
    [TestCase("UnlitTwoTexture", true)]
    [TestCase("LightmappedGeneric", false)]
    [TestCase("WorldVertexTransition", false)]
    public void ConvertsModulation_EachShader_IsWhetherItSendsTheColourInLinearSpace(string shader, bool expected) =>
        ModelMaterialShading.ConvertsModulation(shader).ShouldBe(expected);

    [TestCase("VertexLitGeneric", true)]
    [TestCase("LightmappedGeneric", true)]
    [TestCase("UnlitGeneric", false)]
    [TestCase("UnLitGeneric", false)]
    [TestCase("UnlitTwoTexture", false)]
    public void TakesDiffuseLighting_EachShader_IsWhetherItsPixelShaderReadsAnyLight(string shader, bool expected) =>
        ModelMaterialShading.TakesDiffuseLighting(shader).ShouldBe(expected);

    /// <summary>
    /// The output-level half, on the real material: `skycards_forest1b` from cp_process_f12's own table, one texel of
    /// its real texture drawn through the production model draw.
    /// </summary>
    /// <remarks>
    /// `$color2 [.46 .46 .64]` in linear is ( pow( 117/255, 2.2 ), same, pow( 163/255, 2.2 ) ) = (0.1801 0.1801 0.3736),
    /// and with no light at all the pixel is the texel times that — the same with nothing, with placement #477's baked
    /// colours, and with a bright cube and a lamp. Before the port the three differed and none was right.
    /// (`mat_drawflat` cannot be the albedo here: it whitens AFTER the modulation and would erase the subject.)
    /// </remarks>
    [Test]
    public void DrawModelPose_ASkyCardMaterialUnderColoursACubeAndALamp_IsItsLinearModulationEveryTime()
    {
        const string Map = "cp_process_f12";
        using OffscreenTarget? target = OffscreenTarget.TryCreate(64, 64);

        if (target is null || !MapCache.Exists(Map))
        {
            Assert.Ignore("no Direct3D, or cp_process_f12 or the game is not installed");
            return;
        }

        MapAssets assets = MapCache.Load(mapName: Map);
        int material = Enumerable.Range(0, assets.Materials.Count).FirstOrDefault(
            index => assets.Materials[index].Name.Equals("models/props_skybox/skycards_forest1b", StringComparison.OrdinalIgnoreCase), -1);

        material.ShouldBeGreaterThanOrEqualTo(0, "the control: the map's props name the material");
        assets.Shaders[material].ShouldBe("UnLitGeneric", StringCompareShould.IgnoreCase);

        // The brightest texel the alpha test keeps: every corner takes its coordinate, so the sampler reads it exactly.
        MapTexture texture = assets.Textures[material].ShouldNotBeNull();
        byte[] rgba = texture.Image.ToRgba(texture.Width, texture.Height).ToArray();
        int texel = Enumerable.Range(0, rgba.Length / 4)
            .Where(at => rgba[(at * 4) + 3] == 255)
            .MaxBy(at => rgba[at * 4] + rgba[(at * 4) + 1] + rgba[(at * 4) + 2]);

        (rgba[texel * 4] + rgba[(texel * 4) + 1] + rgba[(texel * 4) + 2]).ShouldBeGreaterThan(150, "the control: a texel bright enough to measure on");
        (float U, float V) uv = (((texel % texture.Width) + 0.5f) / texture.Width, ((texel / texture.Width) + 0.5f) / texture.Height);

        WorldVertex Corner(float x, float z) => new(x, 0f, z, uv.U, uv.V, 0f, 0f, 0f) { NormalY = -1f, NormalZ = 0f };

        WorldVertex[] face =
            [Corner(-200f, -200f), Corner(200f, -200f), Corner(200f, 200f), Corner(-200f, -200f), Corner(200f, 200f), Corner(-200f, 200f)];

        float[] camera = new FreeCamera { Origin = (0f, -300f, 0f), Angles = (0f, 90f, 0f), Aspect = 1f }.ToMatrix();
        float[] identity = [1f, 0f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 1f];

        (int Red, int Green, int Blue) Draw(AmbientCube? light, float[]? colours, LocalLight[]? locals)
        {
            target.Clear(0f, 0f, 0f);
            target.DrawModelPose(
                face, [new WorldBatch(material, 0, 6)], camera, identity, assets,
                light: light, bothSides: true, bakedColours: colours, locals: locals);

            return target.PixelAt(32, 32);
        }

        (float, float, float) bright = (3f, 3f, 3f);
        AmbientCube cube = new(bright, bright, bright, bright, bright, bright);
        LocalLight[] lamp = [new LocalLight(0f, -150f, 0f, 5f, 5f, 5f, 0f, 0f, 1f / (150f * 150f), 1000f)];
        float[] baked = [.. Enumerable.Range(0, 6).SelectMany(_ => new[] { 1.186f, 1.165f, 1.292f })];

        // The texel, read linear as the sampler reads it, times the modulation in linear, and no light.
        (int Red, int Green, int Blue) expected = (
            SrgbTarget.Encode(SrgbTarget.Decode(rgba[texel * 4]) * Math.Pow(117.0 / 255.0, 2.2)),
            SrgbTarget.Encode(SrgbTarget.Decode(rgba[(texel * 4) + 1]) * Math.Pow(117.0 / 255.0, 2.2)),
            SrgbTarget.Encode(SrgbTarget.Decode(rgba[(texel * 4) + 2]) * Math.Pow(163.0 / 255.0, 2.2)));

        (int Red, int Green, int Blue) alone = Draw(null, null, null);
        (int Red, int Green, int Blue) coloured = Draw(null, baked, null);
        (int Red, int Green, int Blue) lit = Draw(cube, null, lamp);

        TestContext.Out.WriteLine($"SKYCARD forest1b: alone {alone}, under its baked colours {coloured}, under a cube and a lamp {lit}; the engine's {expected}");

        Near(alone, expected);
        Near(coloured, expected);
        Near(lit, expected);
    }

    private static void Near((int Red, int Green, int Blue) actual, (int Red, int Green, int Blue) expected)
    {
        actual.Red.ShouldBeInRange(expected.Red - 1, expected.Red + 1);
        actual.Green.ShouldBeInRange(expected.Green - 1, expected.Green + 1);
        actual.Blue.ShouldBeInRange(expected.Blue - 1, expected.Blue + 1);
    }

    private static string Sdk(string path) =>
        SourceSdk.Text(path) ?? throw new InvalidOperationException($"{path} is missing from the SDK");
}
