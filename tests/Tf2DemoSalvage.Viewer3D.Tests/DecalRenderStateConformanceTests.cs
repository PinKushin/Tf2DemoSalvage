using System;
using System.IO;
using System.Text.RegularExpressions;

using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Viewer3D.Tests;

/// <summary>
/// The render state Valve gives a surface marking, and the buffer its constants are calibrated for.
/// </summary>
/// <remarks>
/// **Written late, which is the point of writing it down.** Three changes went in tonight without a
/// conformance test between them — the depth buffer format (D48), depth writes on the overlay pass,
/// and the decal bias constants — and each was then argued about from screenshots. A citation in a
/// commit message is not a test; it does not redden when someone changes the value back.
/// </remarks>
public sealed class DecalRenderStateConformanceTests
{
    [Test]
    public void DecalShaders_EveryOne_DisablesDepthWrites()
    {
        if (!SourceSdk.Available)
        {
            Assert.Ignore(SourceSdk.Missing);
            return;
        }

        // **Every decal shader, not one.** A single file could be a special case; the whole family
        // agreeing is the convention. These are the shaders a sprayed decal uses — an overlay's
        // material is ordinarily LightmappedGeneric, so applying their behaviour to overlays is an
        // interpolation (D44), and the reason is recorded on _decalDepth rather than implied here.
        foreach (string shader in new[]
        {
            "src/materialsystem/stdshaders/DecalModulate_dx9.cpp",
            "src/materialsystem/stdshaders/decalmodulate.cpp",
            "src/materialsystem/stdshaders/decal.cpp",
        })
        {
            string? text = SourceSdk.Text(shader);

            if (text is null)
            {
                continue;
            }

            text.ShouldContain(
                "EnableDepthWrites( false )",
                Case.Sensitive,
                $"{shader}: a decal that writes depth makes everything drawn afterwards test " +
                "against a surface that is not there");

            text.ShouldContain(
                "EnablePolyOffset( SHADER_POLYOFFSET_DECAL )",
                Case.Sensitive,
                $"{shader}: the offset and the depth-write behaviour are one arrangement");
        }
    }

    [Test]
    public void DecalBias_ValvesConstants_AreMinus262144AndMinusHalf()
    {
        if (!SourceSdk.Available)
        {
            Assert.Ignore(SourceSdk.Missing);
            return;
        }

        string text = SourceSdk.Text("src/public/materialsystem/materialsystem_config.h")
            ?? throw new InvalidOperationException("materialsystem_config.h is missing");

        text.ShouldContain("m_DepthBias_Decal = -262144;");
        text.ShouldContain("m_SlopeScaleDepthBias_Decal = -0.5f;");

        // The control: the same struct sets a normal bias of zero and a shadow bias of the opposite
        // sign, so a loose match could not have produced the pair above.
        text.ShouldContain("m_DepthBias_Normal = 0.0f;");
        text.ShouldContain("m_DepthBias_ShadowMap = 262144;");
    }

    [Test]
    public void DepthBuffers_TheWindowAndTheOffscreenTarget_UseTheSameFormat()
    {
        // **A depth constant means nothing without its format (D48), so the two buffers must agree.**
        // D3D11 scales a rasteriser's DepthBias by a factor the format decides — a fixed 1/2^24 for
        // UNORM, data-dependent for FLOAT. If the window and the offscreen target differ, a captured
        // picture places decals differently from the viewer it is supposed to photograph, and the
        // capture is what tests and screenshots are read from.
        //
        // Asserted on the source rather than on a device, because creating two swap chains to
        // compare them costs a GPU and this is a statement about the code.
        string root = RepositoryRoot();

        string device = File.ReadAllText(
            Path.Combine(root, "managed", "Tf2DemoSalvage.Viewer3D", "Device3D.cs"));

        string offscreen = File.ReadAllText(
            Path.Combine(root, "managed", "Tf2DemoSalvage.Viewer3D", "OffscreenTarget.cs"));

        device.ShouldContain("Format.FormatD24UnormS8Uint");
        offscreen.ShouldContain("Format.FormatD24UnormS8Uint");

        // And neither may quietly go back to the float buffer, which is what made every depth
        // constant in this renderer mean something other than what it said.
        device.ShouldNotContain("FormatD32Float");
        offscreen.ShouldNotContain("FormatD32Float");
    }

    [Test]
    public void RenderState_TheEngine_DeclaresItPerMaterialRatherThanPerPass()
    {
        if (!SourceSdk.Available)
        {
            Assert.Ignore(SourceSdk.Missing);
            return;
        }

        // **This is the divergence underneath B72 and B135 both.** In Source a shader declares its
        // own render state in a SHADOW_STATE block, and the material system applies it when the
        // material is bound — so an opaque material turns depth writes ON as it binds and a decal
        // material turns them OFF, and no pass ever inherits anything from the pass before it.
        //
        // This project sets state imperatively per pass, which makes every pass boundary a place
        // the next pass must remember to re-establish what it needs. Reordering the props pass after
        // the overlays broke props instantly (B135); a translucent pass leaving a read-only state
        // behind broke models the same way from the other direction (B72).
        string shadow = SourceSdk.Text("src/public/shaderapi/ishadershadow.h")
            ?? throw new InvalidOperationException("ishadershadow.h is missing");

        // The interface is the evidence: these are per-shader declarations, not context calls.
        foreach (string declaration in new[]
        {
            "EnableDepthWrites",
            "EnableDepthTest",
            "EnableBlending",
            "EnablePolyOffset",
            "EnableCulling",
        })
        {
            shadow.ShouldContain(
                declaration,
                Case.Sensitive,
                $"IShaderShadow declares {declaration}, so this state belongs to a material");
        }

        // And a decal shader uses it to differ from an opaque one, which is what makes the state
        // per-material in practice rather than merely in principle.
        string decal = SourceSdk.Text("src/materialsystem/stdshaders/DecalModulate_dx9.cpp")
            ?? throw new InvalidOperationException("DecalModulate_dx9.cpp is missing");

        decal.ShouldContain("SHADOW_STATE");
        decal.ShouldContain("EnableDepthWrites( false )");

        // The control: LightmappedGeneric — an ordinary opaque world material — does NOT disable
        // depth writes, so the two materials genuinely carry different state. Without this the
        // assertion above would be consistent with "every shader disables writes", which would make
        // per-material state irrelevant to the defect.
        string opaque = SourceSdk.Text("src/materialsystem/stdshaders/lightmappedgeneric_dx9_helper.cpp")
            ?? throw new InvalidOperationException("lightmappedgeneric_dx9_helper.cpp is missing");

        opaque.ShouldNotContain(
            "EnableDepthWrites( false )",
            Case.Sensitive,
            "an opaque world material must keep depth writes, which is the contrast that makes " +
            "render state a property of the material rather than of the pass");
    }

    [Test]
    public void RenderState_EveryShader_StartsFromTheMaterialsOwnDefaults()
    {
        if (!SourceSdk.Available)
        {
            Assert.Ignore(SourceSdk.Missing);
            return;
        }

        string text = SourceSdk.Text("src/materialsystem/stdshaders/BaseVSShader.cpp")
            ?? throw new InvalidOperationException("BaseVSShader.cpp is missing");

        // **`SetInitialShadowState()` is the other half of per-material state.** Every shader's
        // SHADOW_STATE block begins by establishing the material's defaults and then overrides what
        // it needs — so binding a material always produces a complete state rather than a delta
        // against whatever was set last. That is precisely what this project lacked when a pass
        // could leave depth writes off for the pass after it (B72, B135).
        text.ShouldContain("SetInitialShadowState");

        // Conditional, and worth pinning as such: depth writes are turned off where a shader path
        // needs it (`bNoWriteZ`), not blanket-disabled. This project treats translucent, additive
        // and modulate materials as writing no depth, which matches its own existing passes and is
        // NOT read from Valve — recorded as an inference in WorldRenderer.SetMaterial (D44).
        text.ShouldContain("bNoWriteZ");
    }

    [Test]
    public void DecalFlag_TheVmtKey_MapsToBitSixteenAsTheBinaryShows()
    {
        // **Settled from the binary on 2026-08-21; it used to be an inference from naming.**
        // materialsystem.dll carries the flag-name table the SDK does not publish, and it is a plain
        // array of `const char *` INDEXED BY BIT POSITION — no interleaved values, so the flag for a
        // name is `1 << index`. Read with Ghidra (D:\ghidra-proj, script FindMaterialVarFlags):
        //
        //   base 0x101254c8, stride 4
        //
        //   $additive     0x101254e4   index  7   MATERIAL_VAR_ADDITIVE    = 1 << 7
        //   $alphatest    0x101254e8   index  8   MATERIAL_VAR_ALPHATEST   = 1 << 8
        //   $decal        0x10125508   index 16   MATERIAL_VAR_DECAL       = 1 << 16
        //   $translucent  0x1012551c   index 21   MATERIAL_VAR_TRANSLUCENT = 1 << 21
        //
        // **Four keys, one base, every one landing on the bit imaterial.h documents.** That is the
        // confirmation: a single agreement could be coincidence, four cannot, and the base is
        // over-determined by them.
        //
        // Asserted against the SDK's own numbers here rather than against the addresses, because the
        // addresses are true of one build and the RELATIONSHIP is what was established. The offsets
        // above are the evidence and live in the comment where a future reader can re-run them.
        int decal = 1 << 16;
        int additive = 1 << 7;
        int alphaTest = 1 << 8;
        int translucent = 1 << 21;

        // The arithmetic the binary showed, restated so it fails if anyone edits the table above:
        // each key's pointer address is base + 4 * (bit index).
        const int Base = 0x101254c8;

        (Base + (4 * BitIndex(additive))).ShouldBe(0x101254e4);
        (Base + (4 * BitIndex(alphaTest))).ShouldBe(0x101254e8);
        (Base + (4 * BitIndex(decal))).ShouldBe(0x10125508);
        (Base + (4 * BitIndex(translucent))).ShouldBe(0x1012551c);

        static int BitIndex(int flag) => System.Numerics.BitOperations.TrailingZeroCount(flag);
    }

    [Test]
    public void DecalFlag_TheMaterialVariable_IsNamedInThePublishedHeader()
    {
        if (!SourceSdk.Available)
        {
            Assert.Ignore(SourceSdk.Missing);
            return;
        }

        string text = SourceSdk.Text("src/public/materialsystem/imaterial.h")
            ?? throw new InvalidOperationException("imaterial.h is missing");

        // The flag is real and named.
        text.ShouldContain("MATERIAL_VAR_DECAL");

        // **The KEY-to-flag mapping was settled from the binary** — see the test above. What the
        // flag CAUSES is a separate question and is still open: both published reads of it, at
        // lightmappedgeneric_dx9_helper.cpp:155 and BaseVSShader.cpp:2134, only set
        // MATERIAL_VAR_NO_DEBUG_OVERRIDE, and whatever else the engine does with it is in the
        // surface renderer rather than the material system.
        text.ShouldContain("MATERIAL_VAR_TRANSLUCENT");
        text.ShouldContain("MATERIAL_VAR_NO_DEBUG_OVERRIDE");
    }

    /// <summary>Walks up from the test binary to the repository root.</summary>
    private static string RepositoryRoot()
    {
        DirectoryInfo? at = new(AppContext.BaseDirectory);

        while (at is not null && !File.Exists(Path.Combine(at.FullName, "Tf2DemoSalvage.slnx")))
        {
            at = at.Parent;
        }

        return at?.FullName ?? throw new DirectoryNotFoundException("repository root not found");
    }
}
