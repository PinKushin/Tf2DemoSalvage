using System.Collections.Generic;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// A `$distancealpha` UnlitGeneric material — TF2's HUD backgrounds — read from its VMT, and the constants the helper
/// hands the pixel shader (vertexlitgeneric_dx9_helper.cpp:995-1066).
/// </summary>
/// <remarks>
/// Values are `materials/hud/ammo_blue_bg.vmt`'s, as shipped in tf2_misc. Without this the ammo and class backgrounds
/// drew as flat rectangles (2026-10-17, against TF2 on cp_process_f12).
/// </remarks>
public sealed class VguiDistanceAlphaConformanceTests
{
    private static readonly Dictionary<string, string> AmmoBlueBg = new(System.StringComparer.OrdinalIgnoreCase)
    {
        ["$distancealpha"] = "1",
        ["$distancealphafromdetail"] = "1",
        ["$detailblendmode"] = "8",
        ["$softedges"] = "1",
        ["$edgesoftnessstart"] = "0.45",
        ["$edgesoftnessend"] = "0.3",
        ["$scaleedgesoftnessbasedonscreenres"] = "1",
        ["$outline"] = "1",
        ["$outlinecolor"] = "[0.7 0.7 0.5]",
        ["$outlinestart0"] = "0.3",
        ["$outlinestart1"] = "0.49",
        ["$outlineend0"] = "0.49",
        ["$outlineend1"] = "0.73",
        ["$scaleoutlinesoftnessbasedonscreenres"] = "1",
        ["$glow"] = "1",
        ["$glowcolor"] = "[0 0 0]",
        ["$glowalpha"] = "0.45",
        ["$glowstart"] = "0.03",
        ["$glowend"] = "0.5",
        ["$glowx"] = "-0.001",
        ["$glowy"] = "-0.001",
    };

    [Test]
    public void Read_AMaterialWithoutDistanceAlpha_IsNull() =>
        VguiDistanceAlpha.Read(_ => null).ShouldBeNull();

    [Test]
    public void Read_TheAmmoBackground_TakesItsSwitches()
    {
        VguiDistanceAlpha material = VguiDistanceAlpha.Read(AmmoBlueBg.GetValueOrDefault)!;

        (material.FromDetail, material.SoftMask, material.Outline, material.Glow)
            .ShouldBe((true, true, true, true));
    }

    /// <remarks>
    /// At 1280×720: `flResScale = max( 0.5, max( 1024/1280, 768/720 ) )` = 1.0667 (:1014); the soft edge widens about its
    /// mid-point (:1018-1020), as does the outline's inner start and outer end (:1027-1030). `$outlinealpha` is not in the
    /// VMT and `InitFloatParam` sets it to 1 (:159); `$detailblendfactor` likewise 1 (:87).
    /// </remarks>
    [Test]
    public void Constants_TheAmmoBackgroundAt1280By720_AreTheHelpersC5ToC9()
    {
        VguiDistanceAlpha material = VguiDistanceAlpha.Read(AmmoBlueBg.GetValueOrDefault)!;
        float scale = 768f / 720f;
        float softMid = 0.375f;
        float startMid = (0.3f + 0.49f) / 2f;
        float endMid = (0.49f + 0.73f) / 2f;

        float[] expected =
        [
            -0.001f, -0.001f, 0.03f, 0.5f,
            0f, 0f, 0f, 0.45f,
            softMid + (scale * (0.45f - softMid)), softMid + (scale * (0.3f - softMid)), 0f, 0f,
            0.7f, 0.7f, 0.5f, 1f,
            0.3f, endMid + (scale * (0.73f - endMid)), 0.49f, startMid + (scale * (0.49f - startMid)),
        ];

        float[] constants = material.Constants(1280, 720);

        constants.Length.ShouldBe(20);

        for (int index = 0; index < expected.Length; index++)
        {
            constants[index].ShouldBe(expected[index], 1e-5f, $"constant {index}");
        }
    }

    /// <remarks>
    /// The route the HUD's backgrounds take: `DrawSetTextureFile` → `MapAssets.ResolveVguiMaterial`. Loose files in a
    /// `tf` folder with no gameinfo.txt, which `GameArchives.Open` reads as the mod folder.
    /// </remarks>
    [Test]
    public void ResolveVguiMaterial_ADistanceAlphaMaterial_CarriesItsParametersAndDetail()
    {
        string root = System.IO.Path.Combine(TestContext.CurrentContext.WorkDirectory, "da-" + System.Guid.NewGuid().ToString("N"));
        string hud = System.IO.Path.Combine(root, "materials", "hud");
        System.IO.Directory.CreateDirectory(hud);

        try
        {
            byte[] texel = Tf2DemoSalvage.Content.Tests.Assets.VtfFixture.Build(
                Tf2DemoSalvage.Content.Assets.VtfFormat.Bgr888, width: 1, height: 1, mips: 1, images: new byte[3]);
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(hud, "grad.vtf"), texel);
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(hud, "mask.vtf"), texel);
            System.IO.File.WriteAllText(
                System.IO.Path.Combine(hud, "bg.vmt"),
                "\"UnlitGeneric\" { \"$basetexture\" \"hud\\grad\" \"$detail\" \"hud\\mask\" \"$detailblendmode\" 8 " +
                "\"$distancealpha\" 1 \"$distancealphafromdetail\" 1 \"$softedges\" 1 \"$edgesoftnessstart\" 0.45 \"$translucent\" 1 }");

            MapTexture? texture = MapAssets.ResolveVguiMaterial(
                Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance,
                "vgui/../hud/bg",
                Tf2DemoSalvage.Content.Assets.PakFile.Read(System.ReadOnlyMemory<byte>.Empty),
                Tf2DemoSalvage.Content.Assets.GameArchives.Open(root),
                4096);

            texture.ShouldNotBeNull();
            texture.Value.Vgui.ShouldNotBeNull();
            texture.Value.Vgui.DistanceAlpha.ShouldNotBeNull();
            texture.Value.Vgui.Detail.ShouldNotBeNull("the mask is the shape");
            (texture.Value.Vgui.DistanceAlpha.SoftStart, texture.Value.Vgui.Detail.Value.Mode).ShouldBe((0.45f, 8));
        }
        finally
        {
            System.IO.Directory.Delete(root, recursive: true);
        }
    }

    /// <remarks>
    /// `TextureCombine` runs under `#if DETAILTEXTURE` (vertexlit_and_unlit_generic_ps2x.fxc:262-279), so a `$detail`
    /// material with no `$distancealpha` still combines it. `$detailscale` is UnlitGeneric's own default of 4
    /// (unlitgeneric_dx9.cpp:20, vertexlitgeneric_dx9_helper.cpp:90). `$detailtint` is not an UnlitGeneric parameter:
    /// `m_nDetailTint` stays -1 and the helper sets c10 to white (vertexlitgeneric_dx9_helper.cpp:988-993).
    /// </remarks>
    [Test]
    public void ResolveVguiMaterial_ADetailWithoutDistanceAlpha_CarriesItsCombineWithAWhiteTint()
    {
        string root = System.IO.Path.Combine(TestContext.CurrentContext.WorkDirectory, "dt-" + System.Guid.NewGuid().ToString("N"));
        string hud = System.IO.Path.Combine(root, "materials", "hud");
        System.IO.Directory.CreateDirectory(hud);

        try
        {
            byte[] texel = Tf2DemoSalvage.Content.Tests.Assets.VtfFixture.Build(
                Tf2DemoSalvage.Content.Assets.VtfFormat.Bgr888, width: 1, height: 1, mips: 1, images: new byte[3]);
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(hud, "grad.vtf"), texel);
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(hud, "grain.vtf"), texel);
            System.IO.File.WriteAllText(
                System.IO.Path.Combine(hud, "plain.vmt"),
                "\"UnlitGeneric\" { \"$basetexture\" \"hud\\grad\" \"$detail\" \"hud\\grain\" \"$detailblendmode\" 5 " +
                "\"$detailblendfactor\" 0.25 \"$detailtint\" \"[1 0 0]\" }");

            MapTexture? texture = MapAssets.ResolveVguiMaterial(
                Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance,
                "hud/plain",
                Tf2DemoSalvage.Content.Assets.PakFile.Read(System.ReadOnlyMemory<byte>.Empty),
                Tf2DemoSalvage.Content.Assets.GameArchives.Open(root),
                4096);

            texture.ShouldNotBeNull();
            texture.Value.Vgui.ShouldNotBeNull();
            texture.Value.Vgui.DistanceAlpha.ShouldBeNull();
            MapDetail detail = texture.Value.Vgui.Detail.ShouldNotBeNull();
            (detail.Mode, detail.BlendFactor, detail.Scale, detail.Tint).ShouldBe((5, 0.25f, (4f, 4f), (1f, 1f, 1f)));
        }
        finally
        {
            System.IO.Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public void Read_AMaterialWithNeitherDetailNorDistanceAlpha_IsNull() =>
        VguiShading.Read(Tf2DemoSalvage.Content.Assets.VmtMaterial.Parse("\"UnlitGeneric\" { \"$basetexture\" \"x\" }"u8.ToArray()), detail: null)
            .ShouldBeNull();

    /// <remarks>
    /// The two scale flags each gate their own block (vertexlitgeneric_dx9_helper.cpp:1012-1031): with only
    /// `$scaleedgesoftnessbasedonscreenres`, the soft edge widens and the outline stays as written; with only
    /// `$scaleoutlinesoftnessbasedonscreenres`, the reverse. At 1280×720 the scale is 768/720.
    /// </remarks>
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(false, false)]
    public void Constants_EachScaleFlagAlone_ScalesOnlyItsOwnRange(bool edges, bool outline)
    {
        Dictionary<string, string> vmt = new(AmmoBlueBg, System.StringComparer.OrdinalIgnoreCase)
        {
            ["$scaleedgesoftnessbasedonscreenres"] = edges ? "1" : "0",
            ["$scaleoutlinesoftnessbasedonscreenres"] = outline ? "1" : "0",
        };
        float scale = 768f / 720f;
        float softMid = 0.375f;
        float startMid = (0.3f + 0.49f) / 2f;
        float endMid = (0.49f + 0.73f) / 2f;

        float[] constants = VguiDistanceAlpha.Read(vmt.GetValueOrDefault)!.Constants(1280, 720);

        (float, float) soft = edges ? (softMid + (scale * (0.45f - softMid)), softMid + (scale * (0.3f - softMid))) : (0.45f, 0.3f);
        (float, float) outer = outline ? (endMid + (scale * (0.73f - endMid)), startMid + (scale * (0.49f - startMid))) : (0.73f, 0.49f);

        constants[8].ShouldBe(soft.Item1, 1e-5f, "soft start");
        constants[9].ShouldBe(soft.Item2, 1e-5f, "soft end");
        constants[17].ShouldBe(outer.Item1, 1e-5f, "outline end1");
        constants[19].ShouldBe(outer.Item2, 1e-5f, "outline start1");
    }

    [Test]
    public void Constants_AtTheNominal1024By768_LeaveTheSoftEdgeAsWritten()
    {
        VguiDistanceAlpha material = VguiDistanceAlpha.Read(AmmoBlueBg.GetValueOrDefault)!;

        float[] constants = material.Constants(1024, 768);

        (constants[8], constants[9]).ShouldBe((0.45f, 0.3f));
    }
}
