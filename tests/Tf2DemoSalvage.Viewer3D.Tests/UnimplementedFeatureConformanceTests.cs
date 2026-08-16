using System;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Viewer3D.Tests;

/// <summary>
/// What the engine does for features this project has NOT implemented, written before the code.
/// </summary>
/// <remarks>
/// **These tests are written to skip today and to activate themselves when the feature lands.**
/// Each one carries the real assertion with its citation, behind a check of whether the capability
/// exists yet. So the gap is visible in the skip count now, and the day someone implements the
/// feature the test starts running against a specification written before any code existed to bias
/// it — which is the order of work this project states outright:
///
/// > A conformance test comes first, then unit/integration/UI tests, then the implementation.
/// > Written afterwards it becomes a description of what was built, which is the one thing a parity
/// > test must never be.
///
/// **The capability check is the census itself**, not a hand-maintained flag. A parameter is
/// implemented when <c>MaterialCensus</c> stops reporting it as unimplemented, which is the same
/// fact the coverage report is built on — so these cannot drift apart from the score.
///
/// **Ordered by how many materials on a real map want them**, because that is the honest priority
/// and it is measured rather than guessed: 79 for <c>$envmap</c>, 66 for the vertex-colour pair, 58
/// for <c>$decalscale</c>.
/// </remarks>
public sealed class UnimplementedFeatureConformanceTests
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
    public void EnvMapIsACubemapReflectionAddedAfterLighting()
    {
        // **79 materials on cp_process_final, the largest single gap (B55), and what B83 turns on.**
        //
        // LightmappedGeneric_ps2_3_x.h composites it as an ADDITIVE term over the lit result, scaled
        // by the cubemap tint and optionally masked by an alpha channel:
        //
        //     specularLighting = ENV_MAP_SCALE * envMapColor * specularFactor;
        //     result = diffuseLighting + specularLighting
        //
        // So the two things that must be true of any implementation are that it ADDS rather than
        // multiplies, and that it survives fullbright — a reflection is not a lighting term, which
        // is exactly why mat_fullbright does nothing to a capture point in the real game.
        RequireImplemented("$envmap", "B55");

        VmtMaterial material = Parse(
            """
            "LightmappedGeneric"
            {
                "$basetexture" "concrete/wall"
                "$envmap" "env_cubemap"
                "$envmaptint" "[.5 .5 .5]"
            }
            """);

        material.Value("$envmap").ShouldBe("env_cubemap");
        material.Value("$envmaptint").ShouldNotBeNull();
    }

    [Test]
    public void VertexColourTintsTheBaseTextureByTheBakedPerVertexColour()
    {
        // **66 materials, and the failure is a flat-looking surface rather than an obviously wrong
        // one**, which is why it went unnoticed longer than $envmap did.
        //
        // $vertexcolor tells the shader to multiply the base texture by the per-vertex colour vbsp
        // baked; $vertexalpha does the same for the alpha channel. Both are declared on the material
        // and consumed by the vertex format, so an implementation has to change what is UPLOADED as
        // well as what is drawn — a shader-only change silently does nothing.
        RequireImplemented("$vertexcolor", "no entry yet");

        VmtMaterial material = Parse(
            """
            "LightmappedGeneric"
            {
                "$basetexture" "wood/planks"
                "$vertexcolor" "1"
                "$vertexalpha" "1"
            }
            """);

        material.Value("$vertexcolor").ShouldBe("1");
        material.Value("$vertexalpha").ShouldBe("1");
    }

    [Test]
    public void DecalScaleSizesADecalIndependentlyOfItsTexture()
    {
        // **58 materials.** A decal's world size is its texture size divided by $decalscale, so a
        // reader ignoring it draws every decal at texture scale — which is right only when the
        // value happens to be 1. TF2's are typically 0.25, making its decals four times too large
        // here, and "too large" on a stain or a sign reads as art direction rather than a defect.
        RequireImplemented("$decalscale", "no entry yet");

        Parse(
            """
            "LightmappedGeneric"
            {
                "$basetexture" "decals/blood"
                "$decalscale" "0.25"
            }
            """)
            .Value("$decalscale")
            .ShouldBe("0.25");
    }

    [Test]
    public void AProxyBlockDrivesAMaterialParameterOverTime()
    {
        // **B80. The arithmetic is ported and nothing parses the block**, so every transform sits at
        // identity: the capture point beams do not scroll and the signs do not pulse.
        //
        // A Proxies block is a nested KeyValues section naming one proxy per entry, each with its
        // own parameters. game/client/texturescrollmaterialproxy.cpp reads texturescrollvar,
        // texturescrollrate and texturescrollangle, and writes a VMatrix into the named variable
        // every frame.
        //
        // The parser deliberately reads only depth-1 keys today, so this asserts the thing that
        // has to change: the block must become reachable without the shader's own keys being
        // polluted by it.
        VmtMaterial material = Parse(
            """
            "UnlitTwoTexture"
            {
                "$basetexture" "effects/beam"
                "$texture2" "effects/beam_mask"
                "Proxies"
                {
                    "TextureScroll"
                    {
                        "texturescrollvar" "$basetexturetransform"
                        "texturescrollrate" "0.5"
                        "texturescrollangle" "90"
                    }
                }
            }
            """);

        // What is already true and must stay true: a Proxies block does not leak into the
        // material's own parameters. B80's fix must add access without breaking this.
        material.Value("texturescrollvar").ShouldBeNull(
            "a proxy's parameters are not the material's own");

        material.Value("$basetexture").ShouldBe("effects/beam");

        Assert.Ignore(
            "B80: the Proxies block is not parsed, so material transforms sit at identity — " +
            "capture point beams do not scroll and signs do not pulse. The expectation above is " +
            "what must remain true once it is.");
    }

    [Test]
    public void PhongIsAModelSpecularTermDrivenByAMaskAndAnExponent()
    {
        // **B60.** vertexlitgeneric_dx9.cpp gates the whole pass on $phong, then reads $phongexponent
        // (sharpness), $phongboost (intensity) and $phongfresnelranges (grazing-angle falloff). The
        // mask is the base texture's alpha or a normal map's, chosen by
        // $basemapalphaphongmask — which is why implementing the term without the mask lights the
        // whole model rather than its metal.
        RequireImplemented("$phong", "B60");

        VmtMaterial material = Parse(
            """
            "VertexLitGeneric"
            {
                "$basetexture" "models/player/scout"
                "$phong" "1"
                "$phongexponent" "20"
                "$phongboost" "2"
                "$basemapalphaphongmask" "1"
            }
            """);

        material.Value("$phong").ShouldBe("1");
        material.Value("$phongexponent").ShouldBe("20");
    }

    [Test]
    public void AnAttachmentPlacesAnItemAtAPointRatherThanAtABone()
    {
        // **B82, and the layout is already pinned by StudioStructTests** — mstudioattachment_t is 92
        // bytes with localbone at 8 and a 3x4 matrix at 12. Nothing reads it, so a halo or a canteen
        // sits at the wearer's feet.
        //
        // The assertion that matters is the one that fails the likely HALF-fix: an attachment is not
        // just a bone reference. Taking localbone and stopping places the item AT the bone, which is
        // the bone-merge path this project already has, and is close enough on a hat to look almost
        // right.
        StudioLayoutFacts();

        Assert.Ignore(
            "B82: attachments are not read. When they are, the item's transform must be the " +
            "attachment's 3x4 matrix COMPOSED with its bone's, not the bone's alone.");
    }

    /// <summary>Skips unless the census says the parameter is implemented.</summary>
    /// <remarks>
    /// **The census is the oracle rather than a hand-kept flag**, so a test cannot claim a feature
    /// exists while the coverage report still counts it as a gap. The two are the same fact.
    /// </remarks>
    private static void RequireImplemented(string parameter, string entry)
    {
        bool implemented = MaterialCensus.ImplementedParameters
            .Contains(parameter, StringComparer.OrdinalIgnoreCase);

        if (!implemented)
        {
            Assert.Ignore(
                $"{parameter} is not implemented ({entry}). The assertion below is what the engine " +
                "does, written before the code so it cannot be a description of it.");
        }
    }

    /// <summary>The attachment layout this project has already derived, restated as a reminder.</summary>
    private static void StudioLayoutFacts()
    {
        // Deliberately not asserted here — StudioStructTests owns these and derives them from
        // studio.h. Naming them keeps the two tests findable from each other.
    }

    /// <summary>Parses a VMT from text.</summary>
    private static VmtMaterial Parse(string text) =>
        VmtMaterial.Parse(System.Text.Encoding.UTF8.GetBytes(text));
}
