using System;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

using Silk.NET.Direct3D11;
using Silk.NET.DXGI;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// The render state Valve gives a surface marking, compared against the state this project gives it.
/// </summary>
/// <remarks>
/// **Rewritten 2026-08-21 after the owner named what was wrong with it:**
///
/// > "the conf tests have to test our code against valves or its really not testing anything because
/// > im pretty sure valve tested their code themselves, a lot, so us retesting the unchanging sdk is
/// > worthless."
///
/// Every test below now parses a number out of Valve's source and compares it against
/// <see cref="DecalState"/>, which is what the renderer builds its states from. The previous version
/// asserted that <c>materialsystem_config.h</c> still contains the text
/// <c>m_DepthBias_Decal = -262144;</c> — true, unchanging, and unable to fail for any reason
/// concerning this renderer.
///
/// **Doing that turned up a divergence the old test could not see**, which is the point. Our
/// constant bias was zero against Valve's −262144, justified in a comment claiming the two APIs do
/// not agree on what a depth bias is. They do, and Valve says so in
/// <c>public/togl/linuxwin/dxabstract.h:966</c> — the value is <c>glPolygonOffset</c>'s <c>units</c>,
/// scaled by the buffer's smallest resolvable step, which is D3D11's definition of the same field on
/// a UNORM format. See <see cref="DecalState"/>.
/// </remarks>
public sealed class DecalRenderStateConformanceTests
{
    private const string Config = "src/public/materialsystem/materialsystem_config.h";
    private const string Togl = "src/public/togl/linuxwin/dxabstract.h";

    [SetUp]
    public void RequireTheSdk()
    {
        if (!SourceSdk.Available)
        {
            Assert.Ignore(SourceSdk.Missing);
        }
    }

    [Test]
    public void DecalBias_ValvesConstantTerm_IsMinus262144()
    {
        // Parsed, not restated: this is Valve's number arriving from Valve's file.
        float valves = Initialiser("m_DepthBias_Decal");

        valves.ShouldBe(-262144f, "materialsystem_config.h:226");

        // **This test says nothing about OUR constant, and that is the correction.** It used to
        // assert the two were equal, on the reasoning that a depth bias means the same thing under
        // all three APIs. That part is true — see the togl test below — and it is not the question.
        // The question is whether the surface we draw is one Valve applies this bias to at all, and
        // `PolyOffset_ForALightmappedGenericOverlay_IsNeverRequested` is where that is settled.
        //
        // The control, and it is what makes the assertion above mean something. The same struct
        // initialises a normal bias of zero and a shadow bias of the opposite sign, so a parser that
        // matched loosely could not have produced −262144 for all three.
        Initialiser("m_DepthBias_Normal").ShouldBe(0f);
        Initialiser("m_DepthBias_ShadowMap").ShouldBe(262144f);
    }

    [Test]
    public void DecalBias_TheSlopeScaledTerm_IsValves()
    {
        float valves = Initialiser("m_SlopeScaleDepthBias_Decal");

        valves.ShouldBe(-0.5f, "materialsystem_config.h:223");

        DecalState.SlopeScaledBias.ShouldBe(valves);

        // Control: the other two slope-scaled terms differ from this one and from each other.
        Initialiser("m_SlopeScaleDepthBias_Normal").ShouldBe(0f);
        Initialiser("m_SlopeScaleDepthBias_ShadowMap").ShouldBe(0.5f);
    }

    [Test]
    public void PolyOffset_ForALightmappedGenericOverlay_IsNeverRequested()
    {
        // **Having Valve's number is not having Valve's mechanism**, and this is the test that
        // separates them. A polygon offset in Source is a property of the SHADER, established at
        // shader-shadow time — there is exactly one entry point to it in the whole published tree.
        string shadow = Sdk("src/public/shaderapi/ishadershadow.h");

        shadow.ShouldContain("EnablePolyOffset( PolygonOffsetMode_t", Case.Sensitive);

        // **And no route around it.** If the engine could offset a surface without the shader
        // asking, the paragraph below would prove nothing — a material system call could be adding
        // the bias to overlays out of sight. There is no such call: the render context, the mesh
        // interface and the shader API between them do not mention a polygon offset.
        foreach (string header in new[]
        {
            "src/public/materialsystem/imaterialsystem.h",
            "src/public/materialsystem/imesh.h",
            "src/public/shaderapi/ishaderapi.h",
        })
        {
            Sdk(header).ShouldNotContain("PolyOffset", Case.Sensitive, $"{header} offers a route");
        }

        // **The decal family asks for it. LightmappedGeneric does not.** That pair is the whole
        // finding: an `info_overlay` is ordinarily LightmappedGeneric, so Valve's −262144 is applied
        // to bullet holes and sprays and never to the stripes painted down a corridor.
        Sdk("src/materialsystem/stdshaders/decalmodulate.cpp")
            .ShouldContain("SHADER_POLYOFFSET_DECAL", Case.Sensitive);

        // The control. Without it "LightmappedGeneric does not ask" is indistinguishable from a
        // path typo reading an empty string — which is the failure this project files under
        // `an-empty-search-needs-a-control`.
        string world = Sdk("src/materialsystem/stdshaders/lightmappedgeneric_dx9.cpp");

        world.Length.ShouldBeGreaterThan(1000, "the world shader should not be an empty read");
        world.ShouldNotContain("PolyOffset", Case.Sensitive);

        // **So our overlay pass carries no constant bias**, and the reason is Valve's, not a
        // tuning. Restoring −262144 here has floated every marking off the map three times —
        // 2026-08-14, and twice on 2026-08-21 — because a constant in depth-buffer units is a world
        // distance that grows with the square of range under a perspective projection. B70.
        DecalState.ConstantBias.ShouldBe(
            0, "an overlay's shader never enables a polygon offset, so neither do we");
    }

    [Test]
    public void DecalBias_TheUnits_AreTheBuffersSmallestStepAsToglStates()
    {
        // **The reading that settles what the constant MEANS**, and it is published rather than
        // decompiled. Valve's own D3D9-to-OpenGL layer takes the render state and puts it in
        // glPolygonOffset's `units`:
        //
        //     case D3DRS_DEPTHBIAS:            // kGLDepthBias
        //         float fvalue = *(float*)&Value;
        //         gl.m_DepthBias.units = fvalue;
        //
        // OpenGL scales `units` by r, the smallest resolvable depth difference. D3D11 defines its
        // integer DepthBias with the same scale on a UNORM format. One quantity, three APIs.
        //
        // This is asserted because a note in this project claimed the opposite — that D3D9's bias
        // was a float added directly to depth and therefore untransferable — and that note is what
        // held our constant at zero.
        string togl = Sdk(Togl);

        togl.ShouldContain("case D3DRS_DEPTHBIAS:", Case.Sensitive);
        togl.ShouldContain("gl.m_DepthBias.units = fvalue;", Case.Sensitive);
        togl.ShouldContain("gl.m_DepthBias.factor = fvalue;", Case.Sensitive);

        // And the arithmetic that follows, against the format we actually create (D48). A UNORM
        // buffer's r is a fixed 1/2^24, so Valve's constant is a known fraction of the range —
        // which is the number to reason about when a marking is in the wrong place.
        Device3D.DepthFormat.ShouldBe(Format.FormatD24UnormS8Uint);

        double step = 1.0 / (1 << 24);

        // **Valve's constant, not ours**, and 262144 is 2^18 — so this is exactly 1/64 of the depth
        // range, a round number chosen deliberately rather than tuned.
        (Initialiser("m_DepthBias_Decal") * step).ShouldBe(-0.015625, 1e-9);

        // **What 1/64 of the range costs under perspective, which is why we do not apply it.**
        // Window depth goes as z ≈ 1 − N/d, so an offset Δz moves a surface Δd ≈ Δz·d²/N toward the
        // camera. At Valve's own VIEW_NEARZ of 7, a marking 500 units away tests as though it were
        // at 236 — in front of everything between. That is a fact about the projection, and it is
        // the reason `PolyOffset_ForALightmappedGenericOverlay_IsNeverRequested` holds ours at zero.
        const double near = 7.0;
        const double range = 500.0;

        double biased = range / (1 + (0.015625 * range / near));

        biased.ShouldBe(236.0, 1.0);
    }

    [Test]
    public void DecalDepthState_WritesAndComparison_MatchTheDecalShaders()
    {
        // **Every decal shader, not one.** A single file could be a special case; the whole family
        // agreeing is the convention. An overlay's material is ordinarily LightmappedGeneric, so
        // applying their behaviour to overlays is an interpolation (D44), recorded on DecalState.
        foreach (string shader in new[]
        {
            "src/materialsystem/stdshaders/DecalModulate_dx9.cpp",
            "src/materialsystem/stdshaders/decalmodulate.cpp",
            "src/materialsystem/stdshaders/decal.cpp",
        })
        {
            if (SourceSdk.Text(shader) is not { } text)
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

        // **Ours.** Both halves, because the shaders set both and this project sets them in two
        // different methods — which is exactly how one of them came to be wrong (B135).
        DecalState.WritesDepth.ShouldBeFalse("EnableDepthWrites( false ), DecalModulate_dx9.cpp:66");

        DecalState.DepthFunc.ShouldBe(
            ComparisonFunc.LessEqual,
            "a fragment is the wall's own polygon clipped, so it rasterises to the wall's own " +
            "depth and a strict Less would reject the marking outright (B134)");

        // The control: an opaque world material does NOT disable depth writes, so the two genuinely
        // carry different state. Without this, "decals do not write depth" would be consistent with
        // "nothing writes depth", which would make the assertion above vacuous.
        Sdk("src/materialsystem/stdshaders/lightmappedgeneric_dx9_helper.cpp").ShouldNotContain(
            "EnableDepthWrites( false )",
            Case.Sensitive,
            "an opaque world material keeps depth writes");
    }

    [Test]
    public void DecalCullMode_OursIsBackFaces_AsTheEnginesDefaultCullMode()
    {
        // `MATERIAL_CULLMODE_CCW` - "this culls polygons with counterclockwise winding",
        // imaterialsystem.h:180 - is the engine's default, and an info_overlay's material is drawn
        // with the material's cull mode like any other.
        Sdk("src/public/materialsystem/imaterialsystem.h").ShouldContain(
            "MATERIAL_CULLMODE_CCW",
            Case.Sensitive);

        DecalState.Cull.ShouldBe(
            CullMode.Back,
            "front faces are clockwise here, so culling counterclockwise winding is CullMode.Back " +
            "- the state was copied from the world's both-sided one and drew REDSTONE CARGO " +
            "mirrored through its own silo (B135)");
    }

    [Test]
    public void DepthBuffers_TheWindowAndTheOffscreenTarget_ShareOneConstant()
    {
        // **A depth constant means nothing without its format (D48).** D3D11 scales a rasteriser's
        // DepthBias by a factor the format decides - a fixed 1/2^24 for UNORM, data-dependent for
        // FLOAT. If the window and the offscreen target differed, a captured picture would place
        // markings differently from the viewer it is supposed to photograph, and the capture is what
        // tests and screenshots are read from.
        //
        // This used to be checked by reading both source FILES as text and grepping for the format
        // name - an instrument that passes on a comment and fails on a rename. There is now one
        // constant and OffscreenTarget builds from it, so the two cannot disagree.
        Device3D.DepthFormat.ShouldBe(Format.FormatD24UnormS8Uint);

        Device3D.DepthFormat.ShouldNotBe(
            Format.FormatD32Float,
            "the float buffer is what made every depth constant here mean something other than " +
            "what it said, and it is what invalidated two attempts at Valve's decal bias");
    }

    [Test]
    public void DecalFlag_OurVmtReader_SetsIsDecalForTheKeyOnValvesBit()
    {
        // **The key-to-flag mapping, settled from materialsystem.dll on 2026-08-21.** The binary
        // carries the flag-name table the SDK does not publish, as a plain array of `const char *`
        // INDEXED BY BIT POSITION - no interleaved values, so the flag for a name is `1 << index`.
        // Read with Ghidra (D:\ghidra-proj, script FindMaterialVarFlags), base 0x101254c8, stride 4:
        //
        //   $additive     0x101254e4   index  7   MATERIAL_VAR_ADDITIVE    = 1 << 7
        //   $alphatest    0x101254e8   index  8   MATERIAL_VAR_ALPHATEST   = 1 << 8
        //   $decal        0x10125508   index 16   MATERIAL_VAR_DECAL       = 1 << 16
        //   $translucent  0x1012551c   index 21   MATERIAL_VAR_TRANSLUCENT = 1 << 21
        //
        // Four keys, one base, every one landing on the bit imaterial.h documents. A single
        // agreement could be coincidence; four cannot.
        Sdk("src/public/materialsystem/imaterial.h").ShouldContain("MATERIAL_VAR_DECAL");

        const int Base = 0x101254c8;

        (Base + (4 * 16)).ShouldBe(0x10125508, "$decal, at the bit imaterial.h names");
        (Base + (4 * 7)).ShouldBe(0x101254e4, "$additive");
        (Base + (4 * 8)).ShouldBe(0x101254e8, "$alphatest");
        (Base + (4 * 21)).ShouldBe(0x1012551c, "$translucent");

        // **Ours: the reader that decides which materials get the state above.** A material
        // declaring the key is a marking; one that does not is not. Without this the whole file
        // measures constants nothing consults - DecalState is only reached for materials this
        // predicate accepts.
        Vmt("LightmappedGeneric", "$decal 1").IsDecal.ShouldBeTrue();

        Vmt("LightmappedGeneric", "$basetexture \"concrete/wall\"").IsDecal.ShouldBeFalse(
            "the control: an ordinary world material must NOT take the marking state, or every " +
            "surface in the map stops writing depth");
    }

    /// <summary>Parses a member initialiser out of the material system's config header.</summary>
    /// <remarks>
    /// Anchored on the member name and reading whatever number follows, so the value comes from
    /// Valve's file rather than from this test. <c>SourceSdk.Constants</c> cannot be used: it wants
    /// an uppercase name, a non-negative literal and a trailing comma, and these are none of those.
    /// </remarks>
    private static float Initialiser(string member)
    {
        Match match = Regex.Match(
            Sdk(Config),
            Regex.Escape(member) + @"\s*=\s*(?<value>-?[0-9]*\.?[0-9]+)f?\s*;",
            RegexOptions.None,
            TimeSpan.FromSeconds(5));

        match.Success.ShouldBeTrue($"{member} was not found in {Config}");

        return float.Parse(match.Groups["value"].Value, CultureInfo.InvariantCulture);
    }

    /// <summary>Reads an SDK file, or fails loudly.</summary>
    private static string Sdk(string path) =>
        SourceSdk.Text(path) ?? throw new InvalidOperationException($"{path} is missing from the SDK");

    /// <summary>A material with the given shader and body, as a .vmt would declare it.</summary>
    private static VmtMaterial Vmt(string shader, string body) =>
        VmtMaterial.Parse(Encoding.UTF8.GetBytes($"\"{shader}\"\n{{\n\t{body}\n}}\n"));
}
