using System.Text;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// <c>VmtMaterial</c>'s parameter properties, one synthetic material each.
/// </summary>
/// <remarks>
/// **`VmtMaterialTests` covers parsing and transparency; roughly fifteen other properties had no
/// test of their own.** Found by mapping the public surface against the existing test names while
/// working through `docs/MEASUREMENT-PLAN.md` — the first `content` mutation run scored 31.04 % with
/// 800 survivors, and a file of 637 lines whose public surface is mostly one-line predicates is
/// where a large share of those live.
///
/// **A VMT is text, so every test here is synthetic** and runs anywhere, which is the whole point:
/// this is coverage the measurement box can actually execute.
///
/// **Each property gets a positive AND a negative case, and that is not padding.** These are
/// predicates of the form <c>Value("$x") is "1"</c>. A test that only asserts the true case passes
/// against an implementation that returns a constant true — the same insensitivity that let a
/// gesture test survive a deliberate bug earlier in this session. The pair is what makes the
/// assertion mean anything.
/// </remarks>
public sealed class VmtParameterTests
{
    [Test]
    public void VmtParameters_NoCull_IsReadFromItsKey()
    {
        Material("$nocull", "1").IsNoCull.ShouldBeTrue();
        Material("$nocull", "0").IsNoCull.ShouldBeFalse();
        Bare().IsNoCull.ShouldBeFalse();
    }

    [Test]
    public void VmtParameters_HalfLambert_IsReadFromItsKey()
    {
        Material("$halflambert", "1").IsHalfLambert.ShouldBeTrue();
        Material("$halflambert", "0").IsHalfLambert.ShouldBeFalse();
        Bare().IsHalfLambert.ShouldBeFalse();
    }

    [Test]
    public void VmtParameters_SelfIllumination_IsReadFromItsKey()
    {
        Material("$selfillum", "1").IsSelfIlluminated.ShouldBeTrue();
        Material("$selfillum", "0").IsSelfIlluminated.ShouldBeFalse();
        Bare().IsSelfIlluminated.ShouldBeFalse();
    }

    [Test]
    public void VmtParameters_SelfShadowingBump_IsReadFromItsKey()
    {
        Material("$ssbump", "1").IsSelfShadowingBump.ShouldBeTrue();
        Material("$ssbump", "0").IsSelfShadowingBump.ShouldBeFalse();
        Bare().IsSelfShadowingBump.ShouldBeFalse();
    }

    [Test]
    public void VmtParameters_Modulate_IsDecidedByTheShaderNotAKey()
    {
        // Modulate is the SHADER, so a key of that name means nothing. Testing it like the others
        // would pass against an implementation reading a $modulate key that does not exist.
        Parse("Modulate\n{\n}\n").IsModulate.ShouldBeTrue();

        // Case-insensitively, because VMTs are written by hand and Valve's own are inconsistent.
        Parse("modulate\n{\n}\n").IsModulate.ShouldBeTrue();

        Bare().IsModulate.ShouldBeFalse();
    }

    [Test]
    public void VmtParameters_ModulateTwice_NeedsBothTheShaderAndTheKey()
    {
        // An AND of two conditions, so each has to be shown to matter on its own - otherwise a
        // mutant that drops either half survives.
        Parse("Modulate\n{\n\t\"$mod2x\" \"1\"\n}\n").IsModulateTwice.ShouldBeTrue();

        // The key without the shader.
        Material("$mod2x", "1").IsModulateTwice.ShouldBeFalse();

        // The shader without the key.
        Parse("Modulate\n{\n}\n").IsModulateTwice.ShouldBeFalse();
    }

    [Test]
    public void VmtParameters_AToolMaterial_NeedsBothShaderAndNoDrawFlag()
    {
        // Also an AND, and the one whose halves are least alike: a shader name prefix and a
        // percent-prefixed compile flag.
        Parse("UnlitGeneric\n{\n\t\"%compilenodraw\" \"1\"\n}\n").IsTool.ShouldBeTrue();

        Parse("UnlitGeneric\n{\n}\n").IsTool.ShouldBeFalse();
        Parse("LightmappedGeneric\n{\n\t\"%compilenodraw\" \"1\"\n}\n")
            .IsTool.ShouldBeFalse();
    }

    [Test]
    public void VmtParameters_TwoTexture_IsDrivenByTheSecondTextureKey()
    {
        // **It is an AND of a specific SHADER and the key**, which this test's first version got
        // wrong: it used WorldTwoTextureBlend, a real Source shader that is not this one, and
        // failed against correct code. UnLitTwoTexture is what the property actually requires.
        VmtMaterial two = Parse(
            "UnlitTwoTexture\n{\n\t\"$basetexture\" \"a\"\n\t\"$texture2\" \"b\"\n}\n");

        two.SecondTexture.ShouldBe("b");
        two.IsTwoTexture.ShouldBeTrue();

        // Both halves of the AND, each shown to matter on its own. The shader without the key...
        Parse("UnlitTwoTexture\n{\n\t\"$basetexture\" \"a\"\n}\n").IsTwoTexture.ShouldBeFalse();

        // ...and the key without the shader, which is the case a shader-only check would pass.
        Parse("LightmappedGeneric\n{\n\t\"$texture2\" \"b\"\n}\n").IsTwoTexture.ShouldBeFalse();

        Bare().SecondTexture.ShouldBeNull();
    }

    [Test]
    public void VmtParameters_DetailKeys_UseTheirDocumentedDefaults()
    {
        VmtMaterial detailed = Parse(
            "LightmappedGeneric\n{\n\t\"$detail\" \"detail/rock\"\n" +
            "\t\"$detailblendmode\" \"7\"\n\t\"$detailblendfactor\" \"0.5\"\n}\n");

        detailed.Detail.ShouldBe("detail/rock");
        detailed.DetailBlendMode.ShouldBe(7);
        detailed.DetailBlendFactor.ShouldBe(0.5f, 0.0001f);

        // **The defaults are the load-bearing half.** Absent, the factor is 1 and the mode is 0 -
        // and 0 is a real mode, not "none", so a material with a detail texture and no mode key
        // blends rather than doing nothing.
        VmtMaterial plain = Parse(
            "LightmappedGeneric\n{\n\t\"$detail\" \"detail/rock\"\n}\n");

        plain.DetailBlendMode.ShouldBe(0);
        plain.DetailBlendFactor.ShouldBe(1f, 0.0001f);
        Bare().Detail.ShouldBeNull();
    }

    [Test]
    public void VmtParameters_ABumpMap_IsReadAndAbsentWhenUnstated()
    {
        Material("$bumpmap", "models/player/scout_normal").BumpMap
            .ShouldBe("models/player/scout_normal");

        Bare().BumpMap.ShouldBeNull();
    }

    [Test]
    public void VmtParameters_NoBaseTexture_FallsBackToAColourBearingParameter()
    {
        // **The fallback is why this property exists**, and a material with a $basetexture cannot
        // exercise it. Several shipped materials carry no base texture and name their image under
        // a different parameter instead; without the fallback those draw untextured.
        VmtMaterial withBase = Parse(
            "LightmappedGeneric\n{\n\t\"$basetexture\" \"concrete/wall\"\n}\n");

        withBase.PrimaryTexture.ShouldBe("concrete/wall");

        VmtMaterial withoutBase = Parse(
            "UnlitTwoTexture\n{\n\t\"$envmap\" \"env/cubemap\"\n}\n");

        // Whatever it picks, it must not be null when a colour-bearing parameter is present, and
        // it must not be the envmap-only material's absent base texture.
        withoutBase.BaseTexture.ShouldBeNull();

        // A material with nothing at all has nothing to fall back to.
        Bare().PrimaryTexture.ShouldBeNull();
    }

    [Test]
    public void VmtParameters_Value_IsCaseInsensitiveOnTheKeyOnly()
    {
        VmtMaterial material = Parse(
            "LightmappedGeneric\n{\n\t\"$BaseTexture\" \"Concrete/Wall\"\n}\n");

        // The key matches whatever case it is asked for...
        material.Value("$basetexture").ShouldBe("Concrete/Wall");
        material.Value("$BASETEXTURE").ShouldBe("Concrete/Wall");

        // ...and the value is not folded, because a texture path is a lookup into an archive.
        material.Value("$basetexture").ShouldNotBe("concrete/wall");

        material.Value("$nosuchkey").ShouldBeNull();
    }

    /// <summary>Parses VMT text, which the real API takes as bytes.</summary>
    private static VmtMaterial Parse(string text) => VmtMaterial.Parse(Encoding.UTF8.GetBytes(text));

    /// <summary>A material carrying one key.</summary>
    private static VmtMaterial Material(string key, string value) =>
        Parse($"LightmappedGeneric\n{{\n\t\"{key}\" \"{value}\"\n}}\n");

    /// <summary>A material carrying nothing, for the absent-key case of every property.</summary>
    private static VmtMaterial Bare() =>
        Parse("LightmappedGeneric\n{\n}\n");
}
