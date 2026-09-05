using System.Collections.Generic;
using System.Text;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// The declared parameters a proxy chain looks its sources up in (B340).
/// </summary>
/// <remarks>
/// **A proxy's source is found on the MATERIAL, not only among what earlier proxies wrote.**
/// `CFunctionProxy::Init` calls `pMaterial->FindVar( name, &amp;foundVar, … )`, which sees every
/// parameter the VMT declares. A chain seeded from proxy outputs alone drops any operation reading
/// a declared constant — and `dec18_dumb_bell.vmt` has one:
///
/// <code>
/// "$tintMulti" "10"
/// …
/// "Multiply" { "srcVar1" "$saturatedTint"  "srcVar2" "$tintMulti"  "resultVar" "$phongTint" }
/// </code>
///
/// So the item's phong and envmap tints lost their multiplier entirely. Found by reading a real
/// material's chain rather than by any test failing, which is why this file exists.
///
/// **The value's SHAPE is its type**, and that one material carries all three forms: `{ 111 78 41 }`
/// is a 0-255 colour divided by 255, `[0 0 0]` is a float vector used as written, and `"10"` is a
/// float var that `CBaseVSShader::ColorVarsToVector` broadcasts across three components rather than
/// treating as red alone (`BaseVSShader.cpp:681-690`).
/// </remarks>
public sealed class MaterialVariableSeedTests
{
    /// <remarks>
    /// **A bare number broadcasts**, which is the case that made the bug: `$tintMulti` is `"10"`
    /// and multiplying a vector by it must scale all three components, not just red.
    /// </remarks>
    [Test]
    public void NumericValues_ABareNumber_IsBroadcastAcrossThreeComponents()
    {
        Material("""
            "VertexLitGeneric"
            {
            	"$basetexture" "models/x"
            	"$tintMulti" "10"
            }
            """).NumericValues()["$tintMulti"].ShouldBe((10f, 10f, 10f));
    }

    /// <remarks>
    /// **Braces are 0-255 and brackets are floats**, which is Source's own distinction and not a
    /// tolerance. Reading `{ 111 78 41 }` as floats would multiply a tint by a hundred.
    /// </remarks>
    [Test]
    public void NumericValues_BracesAgainstBrackets_DifferByTwoHundredAndFiftyFive()
    {
        IReadOnlyDictionary<string, (float Red, float Green, float Blue)> values = Material("""
            "VertexLitGeneric"
            {
            	"$basetexture" "models/x"
            	"$colortint_base" "{ 111 78 41 }"
            	"$colortint_tmp" "[0 0 0]"
            	"$saturatedTint_Base" "{144 78 6}"
            }
            """).NumericValues();

        values["$colortint_base"].Red.ShouldBe(111f / 255f, 1e-5f);
        values["$colortint_base"].Green.ShouldBe(78f / 255f, 1e-5f);
        values["$colortint_base"].Blue.ShouldBe(41f / 255f, 1e-5f);

        values["$colortint_tmp"].ShouldBe((0f, 0f, 0f), "brackets are used as written");

        // No space after the brace, which the real material writes both ways.
        values["$saturatedTint_Base"].Red.ShouldBe(144f / 255f, 1e-5f);
    }

    /// <remarks>
    /// **A parameter that is not a number is left OUT, not included as zero.** A texture path read
    /// as a colour would be black, and a proxy multiplying by it would erase whatever it touched —
    /// so absence has to mean "no numeric variable here" for the refusal rule to work.
    /// </remarks>
    [Test]
    public void NumericValues_TexturesAndNames_AreLeftOut()
    {
        IReadOnlyDictionary<string, (float Red, float Green, float Blue)> values = Material("""
            "VertexLitGeneric"
            {
            	"$basetexture" "models/player/soldier/soldier_red"
            	"$bumpmap" "models/player/soldier/soldier_normal"
            	"$phong" "1"
            }
            """).NumericValues();

        values.ContainsKey("$basetexture").ShouldBeFalse("a path is not a colour");
        values.ContainsKey("$bumpmap").ShouldBeFalse();

        // The control: a flag beside them IS a number and must survive, or this test would pass
        // against a reader that dropped everything.
        values["$phong"].ShouldBe((1f, 1f, 1f));
    }

    /// <remarks>
    /// **The whole chain from one real material**, so the test measures what the game ships rather
    /// than what this file made up. Every source `dec18_dumb_bell` reads must be findable.
    /// </remarks>
    [Test]
    public void NumericValues_TheSourcesARealChainReads_AreAllPresent()
    {
        IReadOnlyDictionary<string, (float Red, float Green, float Blue)> values = Material("""
            "VertexLitGeneric"
            {
            	"$basetexture" "models/x"
            	"$colortint_base" "{ 111 78 41 }"
            	"$color2" "{ 111 78 41 }"
            	"$colortint_tmp" "[0 0 0]"
            	"$yellow" "0"
            	"$saturatedTint" "[0 0 0]"
            	"$saturatedTint_Base" "{144 78 6}"
            	"$tintMulti" "10"
            }
            """).NumericValues();

        foreach (string source in new[]
        {
            "$colortint_tmp", "$colortint_base", "$saturatedTint_Base",
            "$color2", "$yellow", "$saturatedTint", "$tintMulti",
        })
        {
            values.ContainsKey(source).ShouldBeTrue(
                $"{source} is a source in dec18_dumb_bell's proxy chain and FindVar finds it");
        }
    }

    private static VmtMaterial Material(string text) =>
        VmtMaterial.Parse(Encoding.UTF8.GetBytes(text));
}
