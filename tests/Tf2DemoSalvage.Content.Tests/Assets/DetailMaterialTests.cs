using System.IO;
using System.Text;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// The <c>$detail</c> keys a material carries, and the defaults it does not.
/// </summary>
/// <remarks>
/// **Every default here is Valve's, from the SHADER_PARAM declarations in
/// <c>lightmappedgeneric_dx9.cpp</c>** — scale 4, blend mode 0, blend factor 1, tint [1 1 1].
/// They are not round numbers chosen for tidiness: a scale of 4 rather than 1 means a material that
/// omits <c>$detailscale</c> tiles its detail texture four times per base tile, and reading that as
/// 1 puts the pattern at a quarter of its intended frequency on every surface that relies on the
/// default. That is a defect nobody can see without a side-by-side, which is why it is pinned here.
/// </remarks>
public sealed class DetailMaterialTests
{
    [Test]
    public void Detail_WhenAbsent_IsNone()
    {
        // The control for every test below. A material with no $detail must report none rather
        // than an empty string, because "" is a texture name the loader would go looking for.
        VmtMaterial material = Parse("\"LightMappedGeneric\"\n{\n\"$basetexture\" \"a/b\"\n}\n");

        material.Detail.ShouldBeNull();
    }

    [Test]
    public void Detail_NamesTheTexture()
    {
        VmtMaterial material = Parse(
            "\"LightMappedGeneric\"\n{\n" +
            "\"$basetexture\" \"concrete/concretefloor007b\"\n" +
            "\"$detail\" \"detail/noise_detail_01\"\n}\n");

        material.Detail.ShouldBe("detail/noise_detail_01");
    }

    [Test]
    public void DetailScale_WhenAbsent_IsFourNotOne()
    {
        // Four is the interesting default precisely because one is the plausible guess. A test
        // that asserted "some positive number" would pass against either.
        (float across, float down) = Parse("\"x\"\n{\n\"$detail\" \"d/e\"\n}\n").DetailScale;

        across.ShouldBe(4f);
        down.ShouldBe(4f);
    }

    [Test]
    public void DetailScale_WhenSet_IsTheStatedValue()
    {
        (float across, float down) =
            Parse("\"x\"\n{\n\"$detail\" \"d/e\"\n\"$detailscale\" \"7.5\"\n}\n").DetailScale;

        across.ShouldBe(7.5f);
        down.ShouldBe(7.5f, "one number scales both axes");
    }

    [Test]
    public void DetailScale_CanBeTwoNumbers()
    {
        // **A scalar broadcasts, a vector does not**, which is Valve's own branch in
        // SetVertexShaderTextureScaledTransform: a vector supplies U and V independently, and
        // anything else defined is read as one float and copied to both.
        //
        // Reading only the scalar form THROWS on the vector one, and a material that throws while
        // resolving its detail loses the texture altogether. Found in the log of a running viewer -
        // cp_process's concrete and brick both declare "[1.1 2.3]" - which is the case a test
        // written from the SHADER_PARAM declaration alone would never have produced, since that
        // declares $detailscale a float with a default of 4.
        (float across, float down) =
            Parse("\"x\"\n{\n\"$detailscale\" \"[1.1 2.3]\"\n}\n").DetailScale;

        across.ShouldBe(1.1f, 0.0001);
        down.ShouldBe(2.3f, 0.0001);
    }

    [Test]
    public void DetailScale_IsReadInvariantOfTheMachinesCulture()
    {
        // A material file always uses a point, and a machine set to a comma locale parses "7.5"
        // as 75 under the current culture. The result is a plausible number four times too large,
        // not an error - which is this project's recurring failure shape.
        Parse("\"x\"\n{\n\"$detailscale\" \".5\"\n}\n").DetailScale.U.ShouldBe(0.5f);
    }

    [Test]
    public void DetailBlendFactor_WhenAbsent_IsFullyApplied()
    {
        // One, not zero. Zero is the identity for every combine mode, so reading the default as
        // zero disables detail on every material that omits the key while still binding the
        // texture and reporting success.
        Parse("\"x\"\n{\n\"$detail\" \"d/e\"\n}\n").DetailBlendFactor.ShouldBe(1f);
    }

    [Test]
    public void DetailBlendFactor_WhenSet_IsTheStatedValue()
    {
        Parse("\"x\"\n{\n\"$detailblendfactor\" \"0.35\"\n}\n").DetailBlendFactor.ShouldBe(0.35f);
    }

    [Test]
    public void DetailBlendMode_WhenAbsent_IsBaseTimesDetailDoubled()
    {
        Parse("\"x\"\n{\n\"$detail\" \"d/e\"\n}\n").DetailBlendMode.ShouldBe(0);
    }

    [Test]
    public void DetailBlendMode_WhenSet_IsTheStatedMode()
    {
        // Seven is MOD2X_SELECT_TWO_PATTERNS, a mode whose behaviour differs from mode 0 only
        // where the base texture has an alpha channel - so the number has to survive intact.
        Parse("\"x\"\n{\n\"$detailblendmode\" \"7\"\n}\n").DetailBlendMode.ShouldBe(7);
    }

    [Test]
    public void DetailTint_WhenAbsent_IsWhite()
    {
        // White is the multiplicative identity, so an absent tint must not darken anything.
        Parse("\"x\"\n{\n\"$detail\" \"d/e\"\n}\n").DetailTint.ShouldBe((1f, 1f, 1f));
    }

    [Test]
    public void DetailTint_InBrackets_IsReadAsFloats()
    {
        // Valve's own defaults are written both ways: aftershock.cpp declares "[1 1 1]" and
        // cloak.cpp declares "{255 255 255}" for the same white.
        Parse("\"x\"\n{\n\"$detailtint\" \"[0.5 0.25 1]\"\n}\n")
            .DetailTint.ShouldBe((0.5f, 0.25f, 1f));
    }

    [Test]
    public void DetailTint_InBraces_IsScaledFromBytes()
    {
        // The brace form is 0-255. Reading it as floats gives a tint of 255, which saturates the
        // albedo to white rather than tinting it - bright enough to look like a lighting bug.
        (float red, float green, float blue) = Parse(
            "\"x\"\n{\n\"$detailtint\" \"{255 128 0}\"\n}\n").DetailTint;

        red.ShouldBe(1f);
        green.ShouldBe(128f / 255f, 0.0001);
        blue.ShouldBe(0f);
    }

    [Test]
    public void DetailTint_ThatIsMalformed_IsRefusedRatherThanQuietlyWhite()
    {
        // **Degrading to white would be the comfortable choice and it is the wrong one.** White is
        // the identity, so a material whose tint failed to parse would draw untinted and look
        // entirely fine - which is this repo's rule about silent fallbacks, stated as a defect: the
        // surface is wrong, nothing says so, and there is no way to find it later. The caller
        // catches this, logs the material, and carries on without a tint.
        Should.Throw<InvalidDataException>(
            () => Parse("\"x\"\n{\n\"$detailtint\" \"not a vector\"\n}\n").DetailTint);
    }

    [Test]
    public void DetailScale_ThatIsMalformed_IsRefusedRatherThanQuietlyFour()
    {
        // Same argument. A scale that fell back to the default would tile at the default frequency
        // and look like a material that simply did not set the key.
        Should.Throw<InvalidDataException>(
            () => Parse("\"x\"\n{\n\"$detailscale\" \"four\"\n}\n").DetailScale);
    }

    [Test]
    public void BumpMap_WhenAbsent_IsNone()
    {
        Parse("\"LightMappedGeneric\"\n{\n\"$basetexture\" \"a/b\"\n}\n").BumpMap.ShouldBeNull();
    }

    [Test]
    public void BumpMap_NamesTheTextureAndSaysWhetherItIsSelfShadowing()
    {
        // Verbatim from the game: TF2's own concrete declares both keys, and the suffix in the
        // path is a convention rather than the signal - $ssbump is what the material states.
        VmtMaterial material = Parse(
            "\"LightMappedGeneric\"\n{\n" +
            "\"$basetexture\" \"concrete/concretefloor007b\"\n" +
            "\"$bumpmap\" \"Concrete/concretefloor007b_height-ssbump\"\n" +
            "\"$ssbump\" \"1\"\n}\n");

        material.BumpMap.ShouldBe("Concrete/concretefloor007b_height-ssbump");
        material.IsSelfShadowingBump.ShouldBeTrue();
    }

    [Test]
    public void BumpMap_AnOrdinaryNormalMap_IsNotSelfShadowing()
    {
        // **The control, and it is the majority case.** On cp_process_final it is 14 ordinary
        // against 13 self-shadowing, so a reader that answered "self-shadowing" to everything
        // would light more than half the bumped surfaces with the wrong combine — and with a
        // combine that still produces lighting rather than an error.
        VmtMaterial material = Parse(
            "\"LightMappedGeneric\"\n{\n\"$bumpmap\" \"Metal/metaldoor001_normal\"\n}\n");

        material.BumpMap.ShouldBe("Metal/metaldoor001_normal");
        material.IsSelfShadowingBump.ShouldBeFalse();
    }

    [Test]
    public void BumpMap_ANameEndingInSsbump_IsNotEnoughOnItsOwn()
    {
        // Valve names ssbump textures with a "-ssbump" suffix and it is tempting to read that
        // instead. It is a convention, not data: a material that omits $ssbump gets the ordinary
        // combine here, and the caller settles it from the texture's own flag.
        Parse("\"x\"\n{\n\"$bumpmap\" \"brick/brickwall002_height-ssbump\"\n}\n")
            .IsSelfShadowingBump.ShouldBeFalse();
    }

    [Test]
    public void SelfIllum_WhenAbsent_IsOff()
    {
        VmtMaterial material = Parse("\"x\"{\"$basetexture\" \"a/b\"}");

        material.IsSelfIlluminated.ShouldBeFalse();
        material.SelfIllumTint.ShouldBe((1f, 1f, 1f));
    }

    [Test]
    public void SelfIllum_IsMaskedByTheBaseTexturesAlphaAndCarriesATint()
    {
        // **The base texture's alpha is the mask**, which is why a self-illuminated material must
        // keep its alpha channel through upload. Valve lerps from the lit colour to the tinted
        // albedo by that alpha, so one is fully unlit and zero is normally lit. Our upload forces
        // alpha to 255 on anything not alpha tested, which would make every self-illuminated
        // surface glow across its whole area rather than only where the lamp is.
        VmtMaterial material = Parse("\"LightmappedGeneric\"{\"$basetexture\" \"signs/light\" " +
            "\"$selfillum\" \"1\" \"$selfillumtint\" \"[1 0.8 0.5]\"}");

        material.IsSelfIlluminated.ShouldBeTrue();
        material.SelfIllumTint.ShouldBe((1f, 0.8f, 0.5f));
    }


    private static VmtMaterial Parse(string text) => VmtMaterial.Parse(Encoding.UTF8.GetBytes(text));
}
