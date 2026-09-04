using System.Text;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// <c>$blendtintbybasealpha</c> and <c>$blendtintcoloroverbase</c>, which decide WHERE a tint
/// lands (B331).
/// </summary>
/// <remarks>
/// **The companion TF2's paint cannot do without.** A painted cosmetic modulates by `$color2`, and
/// without this branch that colour covers the whole model instead of the region the artist masked —
/// a hat dyed end to end rather than on its band. Valve's shader:
///
/// <code>
/// if (bBlendTintByBaseAlpha)
/// {
///     float3 tintedColor = albedo * g_DiffuseModulation.rgb;
///     tintedColor = lerp(tintedColor, g_DiffuseModulation.rgb, g_fTintReplacementControl);
///     albedo = lerp(albedo, tintedColor, baseColor.a);
/// }
/// else
///     albedo = albedo * g_DiffuseModulation.rgb;
/// </code>
///
/// `skin_ps20b.fxc:317-326`. Read-from-source.
///
/// **The mask is the base texture's OWN alpha**, `baseColor.a`, sampled before the modulation — not
/// the modulated `albedo.a`. The two differ the moment a material names `$alpha`, and using the
/// second would make a fading item lose its paint as it faded.
/// </remarks>
public sealed class TintBlendConformanceTests
{
    /// <remarks>
    /// **A shipped cosmetic, byte for byte in the part that matters** — `hwn2019_horrible_horns`,
    /// which the `paint` probe found painted in a real 2026 match. It declares all three variables
    /// the proxy chain needs plus the two blend controls, which is the shape every tintable TF2
    /// item has.
    /// </remarks>
    [Test]
    public void TintsByBaseAlpha_AShippedTintableCosmetic_IsOn()
    {
        VmtMaterial material = Material("""
            "VertexLitGeneric"
            {
            	"$basetexture" "models/workshop/player/items/all_class/hwn2019_horrible_horns/hwn2019_horrible_horns_color"
            	"$blendtintbybasealpha" "1"
            	"$blendtintcoloroverbase" "0"
            	"$colortint_base" "{ 69 54 55 }"
            	"$color2" "{ 69 54 55 }"
            	"$colortint_tmp" "[0 0 0]"
            }
            """);

        material.TintsByBaseAlpha.ShouldBeTrue();
        material.TintOverBase.ShouldBe(0f);

        // The colour the item defaults to, in Valve's 0-255 brace form rather than 0-1 brackets.
        material.Modulation.Red.ShouldBe(69 / 255f, 0.0001f);
        material.Modulation.Green.ShouldBe(54 / 255f, 0.0001f);
        material.Modulation.Blue.ShouldBe(55 / 255f, 0.0001f);
    }

    /// <remarks>
    /// **The control: an ordinary material takes the other branch**, multiplying the tint across the
    /// whole surface. Every material in the game but TF2's tintable items is this one, so a reader
    /// that answered true by default would change how every tinted surface draws.
    /// </remarks>
    [Test]
    public void TintsByBaseAlpha_AMaterialThatDoesNotAskForIt_IsOff()
    {
        Material("""
            "LightmappedGeneric"
            {
            	"$basetexture" "concrete/wall"
            	"$color" "[1 .5 .5]"
            }
            """).TintsByBaseAlpha.ShouldBeFalse();
    }

    /// <remarks>
    /// **Self-illumination wins, and it is a shader limit rather than an art decision.** The helper
    /// gates it — `bBlendTintByBaseAlpha = IsBoolSet( … ) &amp;&amp; !bHasSelfIllum; // Pixel shader
    /// can't do both BLENDTINTBYBASEALPHA and SELFILLUM, so let selfillum win`
    /// (`skin_dx9_helper.cpp:269`) — and the shader declares
    /// `SKIP: ( $BLENDTINTBYBASEALPHA ) &amp;&amp; ( $SELFILLUM )`, so the combination is not even
    /// compiled. A material asking for both is a question about the MATERIAL, which is why the
    /// answer lives here rather than in the renderer.
    /// </remarks>
    [Test]
    public void TintsByBaseAlpha_AMaterialAskingForSelfIllumAsWell_LetsSelfIllumWin()
    {
        VmtMaterial material = Material("""
            "VertexLitGeneric"
            {
            	"$basetexture" "models/props/console"
            	"$blendtintbybasealpha" "1"
            	"$selfillum" "1"
            }
            """);

        material.TintsByBaseAlpha.ShouldBeFalse("the pixel shader cannot do both");

        // The bystander: self-illumination is unaffected by losing the argument.
        material.IsSelfIlluminated.ShouldBeTrue();
    }

    /// <remarks>
    /// **`$blendtintcoloroverbase` is a LERP, not a flag**, so a fractional value is meaningful and
    /// the default is the end that keeps the texture's detail. Reading its default as one would
    /// paint every tintable region flat.
    /// </remarks>
    [Test]
    public void TintOverBase_WhenTheMaterialNamesNone_IsZero()
    {
        Material("""
            "VertexLitGeneric"
            {
            	"$basetexture" "models/props/console"
            	"$blendtintbybasealpha" "1"
            }
            """).TintOverBase.ShouldBe(0f);

        Material("""
            "VertexLitGeneric"
            {
            	"$basetexture" "models/props/console"
            	"$blendtintbybasealpha" "1"
            	"$blendtintcoloroverbase" "0.75"
            }
            """).TintOverBase.ShouldBe(0.75f, 0.0001f);
    }

    private static VmtMaterial Material(string text) =>
        VmtMaterial.Parse(Encoding.UTF8.GetBytes(text));
}
