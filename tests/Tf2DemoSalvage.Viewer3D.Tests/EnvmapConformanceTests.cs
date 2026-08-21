using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Viewer3D.Tests;

/// <summary>
/// The <c>$envmap</c> pipeline, specified from the SDK before any of it is built.
/// </summary>
/// <remarks>
/// **The largest single rendering gap this project has, by the map's own count.** The material
/// census reports 79 of <c>cp_process_final</c>'s 410 materials asking for <c>$envmap</c> — nearly
/// one surface in five — with <c>$envmaptint</c> at 49 and <c>$basealphaenvmapmask</c> at 29 behind
/// it. Nothing reflects: metal, glass and painted surfaces all read matte, which is half of why a
/// capture point disc looks flat. Filed as B55.
///
/// **Written before the implementation, which is the only time it can be written honestly.** A
/// parity test authored afterwards is a description of what was built. Every assertion here is
/// either a quotation from published source or arithmetic on one, and none of it can run yet — the
/// skips are the specification's way of saying "not yet", and they activate as pieces land.
///
/// **Three of these invert the obvious reading**, which is the reason to write them down rather
/// than trusting an implementer's instinct at the time:
///
/// - <c>dcubemapsample_t.size</c> of **0 means the default size**, not a zero-pixel cubemap.
/// - <c>$envmapcontrast</c> defaults to **0**, where 0 is normal and 1 is <c>colour * colour</c>.
/// - <c>$envmapsaturation</c> defaults to **1**, where 1 is normal and 0 is greyscale.
///
/// So one of the pair defaults low and the other high, and they mean opposite things at the same
/// number. An implementation defaulting both to zero greys out every reflection in the map; one
/// defaulting both to one squares every reflection.
/// </remarks>
public sealed class EnvmapConformanceTests
{
    [SetUp]
    public void RequireTheSdk()
    {
        if (!SourceSdk.Available)
        {
            Assert.Ignore(SourceSdk.Missing);
        }
    }

    [Test]
    public void Envmap_ACubemap_IsPlacedByPositionAndNamedAfterIt()
    {
        // **A map does not name its cubemaps; it places them, and the name is derived.**
        // dcubemapsample_t, bspfile.h:992, is the whole of LUMP_CUBEMAPS 42:
        //
        //     struct dcubemapsample_t
        //     {
        //         int           origin[3];   // position of light snapped to the nearest integer
        //                                    // the filename for the vtf file is derived from the position
        //         unsigned char size;        // 0 - default
        //                                    // otherwise, 1<<(size-1)
        //     };
        //
        // That comment is the specification for resolving `$envmap "env_cubemap"`: the material
        // says "the nearest one" and the renderer has to find it by position and then construct a
        // filename. This project has already SEEN the result of that convention without reading the
        // lump — MapAssetsTests records patch VMTs in the map's own pakfile named
        // `icarus/glasschrome001_544_1952_929.vmt`, and those three numbers are an origin.
        //
        // **16 bytes, not 13, and this comment said the opposite when it was written.** Three ints
        // and a byte is thirteen bytes of content; C++ pads the struct to its own four-byte
        // alignment and `SwapLumpToDisk<dcubemapsample_t>` (bsplib.cpp:4891) writes `sizeof`, so
        // three unnamed bytes are on disk. `DECLARE_BYTESWAP_DATADESC()` adds none of it — it
        // expands to static members and friend templates only (datamap.h:318).
        //
        // The wrong version is kept here rather than quietly replaced, because of how it failed:
        // the reader built on it produced a FIRST cubemap at (0, 0, 608), entirely plausible, and a
        // second at (-2147483648, -2147483642, 1879048200). Ten synthetic tests passed against it,
        // because their fixtures were 13 bytes wide to match the same belief.
        // docs/findings/27-cubemap-placement.md.
        RequireCubemapsRead();

        BspCubemaps.Stride.ShouldBe(16, "sizeof(dcubemapsample_t), padding included");

        string source = Sdk("src/public/bspfile.h");

        source.ShouldContain(
            "the filename for the vtf file is derived from the position",
            Case.Sensitive,
            "bspfile.h states how a cubemap is named");
    }

    [Test]
    public void Envmap_ASizeOfZero_MeansTheDefault()
    {
        // **The inverted default, and the one that silently produces nothing.** The declaration
        // spells out both halves — `0 - default` and `otherwise, 1<<(size-1)` — so zero is an
        // escape value rather than a value. Feeding it through the shift gives `1 << -1`, which in
        // C# is `1 << 31` because the shift count is masked to five bits: a cubemap claiming to be
        // two billion pixels square.
        //
        // Asserted as arithmetic rather than as a string, because the trap is what an implementer
        // computes and not what they read. size 1 is 1, size 7 is 64.
        RequireCubemapsRead();

        Shift(1).ShouldBe(1);
        Shift(7).ShouldBe(64);

        static int Shift(byte size) => 1 << (size - 1);
    }

    [Test]
    public void Envmap_TheCubeFaceOrder_IsPositiveAndNegativeXyz()
    {
        // **Valve's face names are misleading and their ORDER is not.** The enum reads
        // RIGHT, LEFT, BACK, FRONT, UP, DOWN, with two of them annotated in visible bafflement:
        //
        //     CUBEMAP_FACE_BACK,	// NOTE: This face is in the +y direction?!?!?
        //     CUBEMAP_FACE_FRONT,	// NOTE: This face is in the -y direction!?!?
        //
        // The punctuation is Valve's, and it is the tell: the names do not match Source's own
        // convention (X forward, Y left, Z up), so "BACK" being +y looks like a bug in the format.
        //
        // The enum declared eleven lines below it settles what the order actually is:
        //
        //     enum LookDir_t { LOOK_DOWN_X = 0, LOOK_DOWN_NEGX, LOOK_DOWN_Y,
        //                      LOOK_DOWN_NEGY, LOOK_DOWN_Z, LOOK_DOWN_NEGZ };
        //
        // Same length, same positions, and the two entries Valve annotated agree exactly — BACK at
        // index 2 with LOOK_DOWN_Y, FRONT at index 3 with LOOK_DOWN_NEGY. So the face order is
        // +X, -X, +Y, -Y, +Z, -Z, and the names are simply wrong rather than the axes being strange.
        //
        // **That is D3D11's TextureCube order exactly**, so the upload is the identity for faces 0
        // to 5 and no swizzle is needed — provided the reflection vector is computed in Source's
        // own space, which this renderer does work in (its height cut reads input.pos.z as height).
        //
        // Asserted rather than noted because "no mapping needed" is the kind of conclusion that
        // gets quietly reversed by someone who reads only the face names.
        RequireCubemapsRead();

        string header = Sdk("src/public/vtf/vtf.h");

        header.ShouldContain("CUBEMAP_FACE_BACK,	// NOTE: This face is in the +y direction?!?!?");
        header.ShouldContain("CUBEMAP_FACE_FRONT,	// NOTE: This face is in the -y direction!?!?");

        // The corroborating enum, whose order is the derivation.
        int look = header.IndexOf("LOOK_DOWN_X = 0", StringComparison.Ordinal);
        int faces = header.IndexOf("CUBEMAP_FACE_RIGHT = 0", StringComparison.Ordinal);

        faces.ShouldBeGreaterThan(0);
        look.ShouldBeGreaterThan(faces, "LookDir_t is declared below CubeMapFaceIndex_t");

        foreach (string direction in
            new[] { "LOOK_DOWN_X", "LOOK_DOWN_NEGX", "LOOK_DOWN_Y", "LOOK_DOWN_NEGY", "LOOK_DOWN_Z", "LOOK_DOWN_NEGZ" })
        {
            header.ShouldContain(direction);
        }

        // Six cube faces plus the spheremap. If Valve ever adds one, the identity mapping stops
        // being safe and this is where that is noticed.
        VtfTexture.CubeFaceCount.ShouldBe(7);
    }

    [Test]
    public void Envmap_TheSpheremap_IsNotUploadedAsASeventhFace()
    {
        // A TextureCube has six faces and the file has seven. The seventh is a different
        // PROJECTION of the same room, not a seventh direction, so it is dropped — and dropping it
        // has to be deliberate, because uploading six of seven "in order" is also what a reader
        // that never noticed the spheremap would do, and that reader is right by accident only as
        // long as the spheremap stays last.
        //
        // It is last: CUBEMAP_FACE_SPHEREMAP sits after DOWN and immediately before the count.
        RequireCubemapsRead();

        string header = Sdk("src/public/vtf/vtf.h");

        int down = header.IndexOf("CUBEMAP_FACE_DOWN", StringComparison.Ordinal);
        int spheremap = header.IndexOf("CUBEMAP_FACE_SPHEREMAP", StringComparison.Ordinal);
        int count = header.IndexOf("CUBEMAP_FACE_COUNT", StringComparison.Ordinal);

        down.ShouldBeGreaterThan(0);
        spheremap.ShouldBeGreaterThan(down, "the spheremap follows the six cube faces");
        count.ShouldBeGreaterThan(spheremap, "and nothing follows the spheremap");
    }

    [Test]
    public void Envmap_TheReflection_IsAddedToTheDiffuse()
    {
        // **The single most consequential line, and the easiest to get wrong from intuition.**
        // lightmappedgeneric_ps2_3_x.h:548:
        //
        //     HALF3 result = diffuseComponent + specularLighting;
        //
        // Added. Not lerped, not multiplied. A reflection makes a surface BRIGHTER; it does not
        // replace the surface's own colour in proportion to some reflectivity. An implementation
        // that blends instead darkens every reflective surface toward the cubemap's average, which
        // reads as a wash rather than as shine and is the failure that looks almost right.
        //
        // The whole term is zero when there is no cubemap (line 521 initialises it to black), so
        // addition is also what makes the unimplemented state correct rather than merely absent.
        RequireEnvmapDrawn();

        Sdk("src/materialsystem/stdshaders/lightmappedgeneric_ps2_3_x.h")
            .ShouldContain("result = diffuseComponent + specularLighting");
    }

    [Test]
    public void Envmap_TheFresnelTerm_IsRaisedToTheFifth()
    {
        // Grazing angles reflect more; a surface faced head-on reflects least. Lines 528-532:
        //
        //     HALF fresnel = 1.0 - dot( worldSpaceNormal, eyeVect );
        //     fresnel = pow( fresnel, 5.0 );
        //     fresnel = fresnel * g_OneMinusFresnelReflection + g_FresnelReflection;
        //
        // The exponent is five — Schlick's approximation — and it is what keeps a floor from
        // mirroring the ceiling when looked at from above. Omitting it applies the cubemap at full
        // strength everywhere, which turns every metal surface into a chrome ball.
        //
        // **And it is applied LAST**, after tint, contrast and saturation (line 544). Order is not
        // arbitrary here: squaring for contrast before scaling by fresnel is a different picture
        // from squaring after, because squaring is not linear.
        RequireEnvmapDrawn();

        string shader = Sdk("src/materialsystem/stdshaders/lightmappedgeneric_ps2_3_x.h");

        shader.ShouldContain("fresnel = pow( fresnel, 5.0 )");

        // The order, asserted as positions rather than as prose: tint, then contrast, then
        // saturation, then fresnel.
        int tint = shader.IndexOf("specularLighting *= g_EnvmapTint", StringComparison.Ordinal);
        int contrast = shader.IndexOf("g_EnvmapContrast )", StringComparison.Ordinal);
        int saturation = shader.IndexOf("g_EnvmapSaturation )", StringComparison.Ordinal);
        int fresnel = shader.IndexOf("specularLighting *= fresnel", StringComparison.Ordinal);

        tint.ShouldBeGreaterThan(0);
        contrast.ShouldBeGreaterThan(tint);
        saturation.ShouldBeGreaterThan(contrast);
        fresnel.ShouldBeGreaterThan(saturation);
    }

    [Test]
    public void Envmap_ContrastAndSaturation_DefaultToOppositeEnds()
    {
        // **The pair that cannot both be defaulted to the same number.** Their SHADER_PARAM
        // declarations carry the meaning in their own help text (lightmappedgeneric_dx9.cpp:42-43):
        //
        //     SHADER_PARAM( ENVMAPCONTRAST,   ..., "0.0", "contrast 0 == normal 1 == color*color" )
        //     SHADER_PARAM( ENVMAPSATURATION, ..., "1.0", "saturation 0 == greyscale 1 == normal" )
        //
        // Contrast is normal at ZERO and saturation is normal at ONE. Both are lerps, and each is
        // written toward the end that is not the default:
        //
        //     specularLighting = lerp( specularLighting, specularLightingSquared, g_EnvmapContrast );
        //     specularLighting = lerp( greyScale, specularLighting, g_EnvmapSaturation );
        //
        // A material naming neither must come out exactly as the raw cubemap sample times the tint.
        RequireEnvmapDrawn();

        Dictionary<string, string> defaults = ShaderDefaults();

        defaults["ENVMAPCONTRAST"].ShouldBe("0.0");
        defaults["ENVMAPSATURATION"].ShouldBe("1.0");
        defaults["ENVMAPTINT"].ShouldBe("[1 1 1]");

        // Stated as the arithmetic an implementation has to satisfy, because the numbers alone do
        // not say which way each lerp runs. With the defaults, both lerps are the identity.
        float sample = 0.6f;

        Lerp(sample, sample * sample, Number(defaults["ENVMAPCONTRAST"])).ShouldBe(sample, 0.0001f);
        Lerp(0.123f, sample, Number(defaults["ENVMAPSATURATION"])).ShouldBe(sample, 0.0001f);

        static float Lerp(float from, float to, float by) => from + ((to - from) * by);

        static float Number(string text) =>
            float.Parse(text, CultureInfo.InvariantCulture);
    }

    [Test]
    public void Envmap_Greyscale_UsesTheLumaWeights()
    {
        // Line 541:
        //
        //     HALF3 greyScale = dot( specularLighting, HALF3( 0.299f, 0.587f, 0.114f ) );
        //
        // The Rec.601 luma weights, not a third each. They sum to one, so a flat grey reflection is
        // unchanged either way — which is exactly why an average passes a casual check and is wrong
        // on every coloured reflection, greening what should stay red.
        RequireEnvmapDrawn();

        string shader = Sdk("src/materialsystem/stdshaders/lightmappedgeneric_ps2_3_x.h");

        shader.ShouldContain("HALF3( 0.299f, 0.587f, 0.114f )");

        // The property that makes the weights checkable independently of their spelling.
        (0.299f + 0.587f + 0.114f).ShouldBe(1f, 0.0001f);
    }

    [Test]
    public void Envmap_TheThreeMasks_AreMutuallyExclusive()
    {
        // **Not a choice an implementation gets to make.** A material can mask its reflection three
        // ways — a dedicated $envmapmask texture, the base texture's alpha, or the normal map's
        // alpha — and the shader's own SKIP list forbids every pairing of them
        // (lightmappedgeneric_ps2_3_x.h:5-8):
        //
        //     SKIP: $NORMALMAPALPHAENVMAPMASK && $BASEALPHAENVMAPMASK
        //     SKIP: $NORMALMAPALPHAENVMAPMASK && $ENVMAPMASK
        //     SKIP: $BASEALPHAENVMAPMASK && $ENVMAPMASK
        //
        // A SKIP is a combination for which no shader is even compiled, so a material naming two is
        // asking for something that does not exist. Knowing that up front is what turns "which one
        // wins" from a design decision into a non-question.
        RequireEnvmapDrawn();

        string shader = Sdk("src/materialsystem/stdshaders/lightmappedgeneric_ps2_3_x.h");

        shader.ShouldContain("SKIP: $NORMALMAPALPHAENVMAPMASK && $BASEALPHAENVMAPMASK");
        shader.ShouldContain("SKIP: $NORMALMAPALPHAENVMAPMASK && $ENVMAPMASK");
        shader.ShouldContain("SKIP: $BASEALPHAENVMAPMASK && $ENVMAPMASK");
    }

    [Test]
    public void Envmap_TheBaseAlphaMask_IsInverted()
    {
        // The one whose sense is backwards, annotated in the source by whoever implemented it:
        //
        //     specularFactor *= 1.0 - blendedAlpha; // Reversing alpha blows!
        //
        // So on a $basealphaenvmapmask material an OPAQUE texel reflects least. Getting this
        // backwards inverts the shine on 29 of cp_process_final's materials — the reflection
        // appears exactly where the artist masked it out.
        //
        // **And it costs the material its transparency**, which is the second half and is easy to
        // miss: three lines below, `alpha *= baseColor.a` is guarded by `!bBaseAlphaEnvmapMask`.
        // The alpha channel has been spent on the mask and cannot also mean opacity.
        RequireEnvmapDrawn();

        string shader = Sdk("src/materialsystem/stdshaders/lightmappedgeneric_ps2_3_x.h");

        shader.ShouldContain("specularFactor *= 1.0 - blendedAlpha");
        shader.ShouldContain("if( !bBaseAlphaEnvmapMask && !bSelfIllum )");
    }

    [Test]
    public void Envmap_TheLiteralEnvCubemap_IsRefusedOnBrushesAndKeptOnModels()
    {
        // **The two shaders disagree about the literal `env_cubemap`, and that disagreement is the
        // whole reason a prop needs machinery a wall does not.**
        //
        // LightmappedGeneric — brushwork — throws it away outright
        // (lightmappedgeneric_dx9_helper.cpp:83):
        //
        //     if( stricmp( params[info.m_nEnvmap]->GetStringValue(), "env_cubemap" ) == 0 )
        //     {
        //         Warning( "env_cubemap used on world geometry without rebuilding map. . ignoring: %s\n", ... );
        //         params[info.m_nEnvmap]->SetUndefined();
        //     }
        //
        // A brush face therefore reflects only what vbsp already patched into its material, which is
        // what this project reads and why B55 needed no search at load.
        //
        // VertexLitGeneric — models — carries no such rejection anywhere in the file. It calls
        // `pShader->LoadCubeMap( info.m_nEnvmap, ... )` on whatever the material says, and
        // `env_cubemap` resolves to the cubemap the engine has bound as local
        // (`BindLocalCubemap`, imaterialsystem.h:1200). **So on a model the literal is not a
        // compile leftover, it is the request**, and a renderer that skips it draws every reflective
        // prop matte.
        //
        // **The absence is asserted WITH its positive control in the same sweep.** A count of zero
        // in vertexlitgeneric proves nothing on its own — the file could have been renamed, or the
        // spelling could differ — so the same search must find the string where it is known to be.
        // Five absence claims in this project have turned out to be facts about the grep.
        RequireEnvmapDrawn();

        string brush = Sdk("src/materialsystem/stdshaders/lightmappedgeneric_dx9_helper.cpp");
        string model = Sdk("src/materialsystem/stdshaders/vertexlitgeneric_dx9_helper.cpp");

        brush.ShouldContain(
            "env_cubemap used on world geometry without rebuilding map",
            Case.Sensitive,
            "the control: LightmappedGeneric rejects the literal, so the search works");

        model.ShouldNotContain(
            "env_cubemap",
            Case.Insensitive,
            "VertexLitGeneric never refuses the literal, so a model keeps it to runtime");

        model.ShouldContain(
            "LoadCubeMap( info.m_nEnvmap",
            Case.Sensitive,
            "and loads whatever $envmap names, literal included");
    }

    [Test]
    public void Envmap_AModelsCubemap_IsTheClosestPlacementToItsOrigin()
    {
        // **Which cubemap "the local one" means, and this is the half the engine keeps closed.**
        // `BindLocalCubemap( ITexture * )` is published as an interface (imaterialsystem.h:1200)
        // and every caller that chooses the texture for a world model is inside the engine. The
        // client tree binds one only in `basemodelpanel.cpp`, and it binds a fixed default.
        //
        // **Valve's own nearest-cubemap rule IS published**, in the compiler, and this implements
        // that one: `Cubemap_FindClosestCubemap`, vbsp/cubemap.cpp:835. Two passes —
        //
        //     // Look for cubemaps in front of the surface first.
        //     float flDist = vecDelta.NormalizeInPlace();
        //     float flDot = DotProduct( vecDelta, pPlane->normal );
        //     if ( ( flDot >= 0.0f ) && ( flDist < flMinDist ) )
        //
        //     // Didn't find anything in front search for closest.
        //     if( iMinCubemap == -1 )
        //         ... flDist = vecDelta.Length(); if ( flDist < flMinDist ) ...
        //
        // **The first pass cannot apply to a model and the source says why**: it needs
        // `pPlane->normal`, the plane of one brush side, and the function returns -1 before doing
        // anything at all when handed no side. A model is a thousand triangles facing every
        // direction and has no such plane. So the applicable rule is the second pass — nearest by
        // straight-line distance — which is also what the function reduces to for any surface with
        // no cubemap in front of it.
        //
        // **Evidence class, stated because these are not equal**: the rule is READ FROM PUBLISHED
        // SOURCE; that the engine's runtime binding for a model uses this same rule is
        // INTERPOLATED, from Valve applying it at compile time to the same question. Nothing
        // published states the runtime rule, and a decompile of the engine would be needed to
        // settle it. Flagged rather than smoothed over, per docs/DECISIONS.md D44.
        RequireEnvmapDrawn();

        string compiler = Sdk("src/utils/vbsp/cubemap.cpp");

        compiler.ShouldContain(
            "int Cubemap_FindClosestCubemap( const Vector &entityOrigin, side_t *pSide )",
            Case.Sensitive,
            "the rule this implements, by its own signature");

        compiler.ShouldContain(
            "// Didn't find anything in front search for closest.",
            Case.Sensitive,
            "and its second pass, which is the one a model can use");

        // The arithmetic itself, predicted against a hand-placed set rather than a real map: a
        // point between two placements takes the nearer, and moving it past the midpoint switches
        // the answer. A search that returned the first entry, or the last, passes one of these
        // rows and fails the other.
        BspCubemap[] placed =
        [
            new BspCubemap(0, 0, 0, 32),
            new BspCubemap(100, 0, 0, 32),
            new BspCubemap(0, 400, 0, 32),
        ];

        BspCubemaps.Closest(placed, 40f, 0f, 0f).ShouldBe(0);
        BspCubemaps.Closest(placed, 60f, 0f, 0f).ShouldBe(1);
        BspCubemaps.Closest(placed, 0f, 300f, 0f).ShouldBe(2);

        // Squared distance versus true distance cannot be told apart by a nearest search, but
        // AXIS-BLINDNESS can: a search that forgot Z answers 0 here and the correct one answers 1.
        //
        // The 30-unit offset on X is load-bearing. Placements sharing X and Y exactly would make a
        // Z-blind search see a TIE rather than a wrong answer, and the tie is then resolved by the
        // comparison operator — so the row would be measuring that instead, and would pass against
        // a Z-blind search whose operator happened to break ties the other way. Measured: it did.
        BspCubemaps.Closest([new BspCubemap(0, 0, 0, 32), new BspCubemap(30, 0, 500, 32)], 0f, 0f, 480f)
            .ShouldBe(1);

        // A map with no cubemaps at all is legal and draws matte; -1 says so rather than throwing
        // or naming a placement that does not exist.
        BspCubemaps.Closest([], 0f, 0f, 0f).ShouldBe(-1);
    }

    /// <summary>
    /// Set to run the skipped assertions anyway, to check the SPECIFICATION rather than the code.
    /// </summary>
    /// <remarks>
    /// **A conformance test written before its feature is unverified prose until something runs
    /// it.** It skips, so a wrong citation, a typo in a quoted line or an arithmetic slip sits
    /// there silently and surfaces months later as a failure blamed on the new implementation.
    ///
    /// Every assertion in this file is a claim about the ENGINE — a line quoted from published
    /// source, a declared default, or arithmetic on one — and none of them touches this project's
    /// code. So all of them can be checked today, and the only thing stopping them is a guard whose
    /// job is to keep the suite honest about what is built.
    ///
    /// <c>TF2DEMOSALVAGE_CHECK_SPEC=1</c> lifts the guard. It is deliberately not on in the gate:
    /// the ordinary run must report these as "not implemented", because that is true.
    /// </remarks>
    private static bool CheckingTheSpecification =>
        Environment.GetEnvironmentVariable("TF2DEMOSALVAGE_CHECK_SPEC") is "1";

    /// <summary>Skips while nothing reads LUMP_CUBEMAPS.</summary>
    /// <remarks>
    /// Asked of the type system rather than tracked by hand, so the day a reader appears these
    /// start running without anyone having to remember them.
    /// </remarks>
    private static void RequireCubemapsRead()
    {
        if (CheckingTheSpecification)
        {
            return;
        }

        if (Type.GetType("Tf2DemoSalvage.Content.Bsp.BspCubemaps, Tf2DemoSalvage.Content") is null)
        {
            Assert.Ignore(
                "LUMP_CUBEMAPS 42 is unread; nothing reflects. B55. The assertion below is what " +
                "the engine does, written before the code so it cannot be a description of it.");
        }
    }

    /// <summary>Skips while the renderer does not draw reflections.</summary>
    private static void RequireEnvmapDrawn()
    {
        if (CheckingTheSpecification)
        {
            return;
        }

        if (!MaterialCensus.ImplementedParameters.Contains("$envmap", StringComparer.OrdinalIgnoreCase))
        {
            Assert.Ignore(
                "$envmap is not implemented; 79 of cp_process_final's 410 materials ask for it " +
                "and every one draws matte. B55.");
        }
    }

    /// <summary>Reads an SDK file, or fails loudly.</summary>
    private static string Sdk(string path) =>
        SourceSdk.Text(path) ?? throw new InvalidOperationException($"{path} is missing from the SDK");

    /// <summary>The defaults the lightmapped shader declares, from its SHADER_PARAM list.</summary>
    /// <remarks>
    /// The third argument of a <c>SHADER_PARAM</c> is the default as a material would write it, so
    /// these are part of the specification rather than an implementation's choice.
    /// </remarks>
    private static Dictionary<string, string> ShaderDefaults()
    {
        Dictionary<string, string> defaults = new(StringComparer.Ordinal);

        foreach (Match hit in Regex.Matches(
            Sdk("src/materialsystem/stdshaders/lightmappedgeneric_dx9.cpp"),
            @"SHADER_PARAM\(\s*([A-Z0-9_]+)\s*,\s*[A-Z_]+\s*,\s*""([^""]*)""",
            RegexOptions.None,
            TimeSpan.FromSeconds(10)))
        {
            defaults.TryAdd(hit.Groups[1].Value, hit.Groups[2].Value);
        }

        return defaults;
    }
}
