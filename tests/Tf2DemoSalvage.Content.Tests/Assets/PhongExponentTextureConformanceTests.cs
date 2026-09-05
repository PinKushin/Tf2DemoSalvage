using System.Text;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// <c>$phongexponenttexture</c>, and the three parameters that cannot act without it (B334).
/// </summary>
/// <remarks>
/// **One texture, three channels, three meanings** — and each is selected by a sentinel that reads
/// backwards from the obvious implementation.
///
/// <code>
/// float4 vSpecExpMap = tex2D( SpecExponentSampler, i.baseTexCoordDetailTexCoord.xy );
///
/// fRimMask = lerp( 1.0f, vSpecExpMap.a, g_RimMaskControl );
///
/// fSpecExp = (g_EyePos_SpecExponent.w >= 0.0) ? g_EyePos_SpecExponent.w : (1.0f + 149.0f * vSpecExpMap.r);
///
/// vSpecularTint = lerp( float3(1.0f, 1.0f, 1.0f), baseColor.rgb, vSpecExpMap.g );
/// vSpecularTint = (g_SpecularTint.r &gt;= 0.0) ? g_SpecularTint.rgb : vSpecularTint;
/// </code>
///
/// `skin_ps20b.fxc:253-276`, with the constants filled by `skin_dx9_helper.cpp:810-875`.
/// Read-from-source.
///
/// **The exponent: a positive `$phongexponent` WINS over the texture.** The helper defaults the
/// constant to `-1` and replaces it only when the material states a value above zero — *"Nonzero
/// value in material overrides map channel"* (`:825`). So the texture's red is consulted only when
/// the material states no exponent of its own.
///
/// **The tint: an all-zero `$phongtint` is the REQUEST for the albedo tint**, not a request for
/// black. The helper checks `(r == 0 &amp;&amp; g == 0 &amp;&amp; b == 0)` and only then sets `r = -1`
/// to tell the shader to read the map; with no map it falls to white instead (`:863-874`). A
/// material saying `"[0 0 0]"` is asking for `lerp(white, albedo, g)`, and reading it literally
/// would kill every highlight on the model.
///
/// **The rim mask needs three things at once**: `bHasRimMaskMap = bHasSpecularExponentTexture
/// &amp;&amp; bHasRimLight &amp;&amp; $rimmask != 0` (`:263`). Any one missing leaves the control at
/// zero, and `lerp(1, a, 0)` is 1 — no mask. That is why `$rimmask` has been counted as inert
/// rather than unimplemented.
///
/// **And Valve's own comment on the exponent is stale**, which is worth knowing before trusting a
/// comment over code here: *"If the exponent passed in as a constant is zero, use the value from
/// the map"* — the code tests `>= 0.0`, so it is NEGATIVE that selects the map, and an explicit
/// zero takes the constant path with an exponent of zero.
/// </remarks>
public sealed class PhongExponentTextureConformanceTests
{
    [Test]
    public void PhongExponentTexture_AMaterialNamingOne_ReportsIt()
    {
        Material("""
            "VertexLitGeneric"
            {
            	"$basetexture" "models/player/heavy"
            	"$bumpmap" "models/player/heavy_normal"
            	"$phong" "1"
            	"$phongexponenttexture" "models/player/heavy_exponent"
            }
            """).PhongExponentTexture.ShouldBe("models/player/heavy_exponent");
    }

    /// <remarks>
    /// **The constant wins when it is positive**, which is the branch a naive implementation gets
    /// backwards. `$phongexponent 20` beside an exponent texture still uses 20.
    /// </remarks>
    [Test]
    public void PhongExponent_APositiveConstantBesideATexture_WinsOverTheMap()
    {
        Material("""
            "VertexLitGeneric"
            {
            	"$basetexture" "models/player/heavy"
            	"$bumpmap" "models/player/heavy_normal"
            	"$phong" "1"
            	"$phongexponent" "20"
            	"$phongexponenttexture" "models/player/heavy_exponent"
            }
            """).PhongExponentFromTexture.ShouldBeFalse(
            "a positive $phongexponent overrides the map channel");
    }

    /// <remarks>
    /// **No exponent, or one that is not above zero, hands the job to the map.** The helper's
    /// default is `-1` and it is replaced only `if ( fValue > 0.f )`, so an absent `$phongexponent`
    /// and an explicit zero both select the texture.
    /// </remarks>
    [Test]
    public void PhongExponent_WithNoPositiveConstant_TakesItFromTheMap()
    {
        Material("""
            "VertexLitGeneric"
            {
            	"$basetexture" "models/player/heavy"
            	"$bumpmap" "models/player/heavy_normal"
            	"$phong" "1"
            	"$phongexponenttexture" "models/player/heavy_exponent"
            }
            """).PhongExponentFromTexture.ShouldBeTrue();

        Material("""
            "VertexLitGeneric"
            {
            	"$basetexture" "models/player/heavy"
            	"$bumpmap" "models/player/heavy_normal"
            	"$phong" "1"
            	"$phongexponent" "0"
            	"$phongexponenttexture" "models/player/heavy_exponent"
            }
            """).PhongExponentFromTexture.ShouldBeTrue("only a value ABOVE zero overrides the map");
    }

    /// <remarks>
    /// **The default exponent is 150, not 5, and this is the divergence nobody would look for**
    /// (B334). Our reader answered `Number("$phongexponent", 5f)` — the parameter's own DECLARED
    /// default from `SHADER_PARAM( PHONGEXPONENT, …, "5.0", … )`. The engine never reads that
    /// default: the helper writes `-1` unless the VMT states a positive value, and when there is no
    /// exponent texture it binds **`TEXTURE_WHITE`** to the sampler
    /// (`skin_dx9_helper.cpp:560-567`). So the shader computes `1 + 149 × 1.0`.
    ///
    /// **A declared default is what the material system would answer, not what the shader uses**,
    /// and the two differ here by a factor of thirty — 5 is a broad, dull sheen and 150 is a tight
    /// point. Seven of cp_process's 330 phong materials state no exponent.
    ///
    /// The same reasoning falsifies the naive reading of `$phongtint`, whose declared default is
    /// the nonsensical `"5.0"` for a VEC3: if declared defaults were applied, every phong material
    /// would carry a fivefold white tint.
    /// </remarks>
    [Test]
    public void PhongExponent_StatedByNothingAndWithNoMap_Is150()
    {
        Material("""
            "VertexLitGeneric"
            {
            	"$basetexture" "models/player/heavy"
            	"$bumpmap" "models/player/heavy_normal"
            	"$phong" "1"
            }
            """).PhongExponent.ShouldBe(150f, "white in the sampler gives 1 + 149 x 1");
    }

    /// <remarks>
    /// **The control: a stated exponent is used as stated**, so the test above is measuring the
    /// unstated case rather than a reader that answers 150 for everything.
    /// </remarks>
    [Test]
    public void PhongExponent_StatedPositive_IsWhatTheMaterialSays()
    {
        Material("""
            "VertexLitGeneric"
            {
            	"$basetexture" "models/player/heavy"
            	"$bumpmap" "models/player/heavy_normal"
            	"$phong" "1"
            	"$phongexponent" "20"
            }
            """).PhongExponent.ShouldBe(20f);
    }

    /// <remarks>
    /// **The control: with no texture there is nothing to take it from**, whatever the exponent
    /// says. A material with `$phongexponent 0` and no map must not claim the map path, or the
    /// renderer would sample a texture it never bound.
    /// </remarks>
    [Test]
    public void PhongExponent_WithNoTextureAtAll_NeverClaimsTheMap()
    {
        Material("""
            "VertexLitGeneric"
            {
            	"$basetexture" "models/player/heavy"
            	"$bumpmap" "models/player/heavy_normal"
            	"$phong" "1"
            	"$phongexponent" "0"
            }
            """).PhongExponentFromTexture.ShouldBeFalse();
    }

    /// <remarks>
    /// **An all-zero `$phongtint` beside a map is the request for the albedo tint** — the helper's
    /// `if ( r == 0 &amp;&amp; g == 0 &amp;&amp; b == 0 )` then `vSpecularTint[0] = -1`. Reading it
    /// literally kills the highlight on every material that asks this way.
    /// </remarks>
    [Test]
    public void PhongAlbedoTint_AnAllZeroTintBesideATexture_AsksForTheAlbedo()
    {
        Material("""
            "VertexLitGeneric"
            {
            	"$basetexture" "models/player/heavy"
            	"$bumpmap" "models/player/heavy_normal"
            	"$phong" "1"
            	"$phongtint" "[0 0 0]"
            	"$phongalbedotint" "1"
            	"$phongexponenttexture" "models/player/heavy_exponent"
            }
            """).PhongTintFromAlbedo.ShouldBeTrue();
    }

    /// <remarks>
    /// **Three controls, because any one alone would leave the test above vacuous.** A stated tint
    /// is used as stated; an all-zero tint with no map to read falls to WHITE, which is the
    /// helper's `else`; and `$phongalbedotint 0` withholds `bHasPhongTintMap` even with a map
    /// present (`skin_dx9_helper.cpp:252`).
    /// </remarks>
    [Test]
    public void PhongAlbedoTint_MissingAnyOneCondition_DoesNotAskForTheAlbedo()
    {
        Material("""
            "VertexLitGeneric"
            {
            	"$basetexture" "models/player/heavy"
            	"$bumpmap" "models/player/heavy_normal"
            	"$phong" "1"
            	"$phongtint" "[1 .7 .6]"
            	"$phongalbedotint" "1"
            	"$phongexponenttexture" "models/player/heavy_exponent"
            }
            """).PhongTintFromAlbedo.ShouldBeFalse("a stated tint is used as stated");

        Material("""
            "VertexLitGeneric"
            {
            	"$basetexture" "models/player/heavy"
            	"$bumpmap" "models/player/heavy_normal"
            	"$phong" "1"
            	"$phongtint" "[0 0 0]"
            	"$phongalbedotint" "1"
            }
            """).PhongTintFromAlbedo.ShouldBeFalse("with no map the helper falls back to white");

        Material("""
            "VertexLitGeneric"
            {
            	"$basetexture" "models/player/heavy"
            	"$bumpmap" "models/player/heavy_normal"
            	"$phong" "1"
            	"$phongtint" "[0 0 0]"
            	"$phongalbedotint" "0"
            	"$phongexponenttexture" "models/player/heavy_exponent"
            }
            """).PhongTintFromAlbedo.ShouldBeFalse("$phongalbedotint 0 withholds the tint map");
    }

    /// <remarks>
    /// **The rim mask needs all three**, which is why it has been inert rather than unimplemented:
    /// `bHasSpecularExponentTexture &amp;&amp; bHasRimLight &amp;&amp; $rimmask != 0`.
    /// </remarks>
    [Test]
    public void RimMask_WithAllThreeConditions_IsOn()
    {
        Material("""
            "VertexLitGeneric"
            {
            	"$basetexture" "models/player/heavy"
            	"$bumpmap" "models/player/heavy_normal"
            	"$phong" "1"
            	"$rimlight" "1"
            	"$rimmask" "1"
            	"$phongexponenttexture" "models/player/heavy_exponent"
            }
            """).MasksRimByExponentAlpha.ShouldBeTrue();
    }

    /// <remarks>
    /// **Each condition removed in turn**, since a conjunction is only pinned by failing it each
    /// way.
    /// </remarks>
    [Test]
    public void RimMask_MissingAnyOneCondition_IsOff()
    {
        // No exponent texture: nothing to read the alpha from.
        Material("""
            "VertexLitGeneric"
            {
            	"$basetexture" "models/player/heavy"
            	"$bumpmap" "models/player/heavy_normal"
            	"$phong" "1"  "$rimlight" "1"  "$rimmask" "1"
            }
            """).MasksRimByExponentAlpha.ShouldBeFalse();

        // No rim light: nothing to mask.
        Material("""
            "VertexLitGeneric"
            {
            	"$basetexture" "models/player/heavy"
            	"$bumpmap" "models/player/heavy_normal"
            	"$phong" "1"  "$rimmask" "1"
            	"$phongexponenttexture" "models/player/heavy_exponent"
            }
            """).MasksRimByExponentAlpha.ShouldBeFalse();

        // $rimmask zero: the control is zero and lerp(1, a, 0) is 1.
        Material("""
            "VertexLitGeneric"
            {
            	"$basetexture" "models/player/heavy"
            	"$bumpmap" "models/player/heavy_normal"
            	"$phong" "1"  "$rimlight" "1"  "$rimmask" "0"
            	"$phongexponenttexture" "models/player/heavy_exponent"
            }
            """).MasksRimByExponentAlpha.ShouldBeFalse();
    }

    /// <remarks>
    /// **The rim exponent is clamped to at least one, and we were not clamping it** (B334).
    /// `vSpecularTint[3] = max(vSpecularTint[3], 1.0f);  // Make sure this is at least 1`
    /// (`skin_dx9_helper.cpp:844`). Below one, `pow` opens the rim out into a wash across the whole
    /// lit side rather than a line along the silhouette — and at zero it is `pow(x, 0) == 1`, a
    /// uniform flood of rim colour over the model.
    /// </remarks>
    [Test]
    public void RimLightExponent_StatedBelowOne_IsClampedToOne()
    {
        Material("""
            "VertexLitGeneric"
            {
            	"$basetexture" "models/player/heavy"
            	"$bumpmap" "models/player/heavy_normal"
            	"$phong" "1"  "$rimlight" "1"  "$rimlightexponent" "0.25"
            }
            """).RimLightExponent.ShouldBe(1f);

        // The control, at both ends: the clamp is one-sided, and the declared default survives it.
        Material("""
            "VertexLitGeneric"
            {
            	"$basetexture" "models/player/heavy"
            	"$bumpmap" "models/player/heavy_normal"
            	"$phong" "1"  "$rimlight" "1"  "$rimlightexponent" "8"
            }
            """).RimLightExponent.ShouldBe(8f);

        Material("""
            "VertexLitGeneric"
            {
            	"$basetexture" "models/player/heavy"
            	"$bumpmap" "models/player/heavy_normal"
            	"$phong" "1"  "$rimlight" "1"
            }
            """).RimLightExponent.ShouldBe(4f, "its own declared default, which is above the clamp");
    }

    private static VmtMaterial Material(string text) =>
        VmtMaterial.Parse(Encoding.UTF8.GetBytes(text));
}
