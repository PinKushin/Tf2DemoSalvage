using System.Linq;
using System.Text;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// Reading the <c>Proxies</c> block out of a VMT.
/// </summary>
/// <remarks>
/// **The parser deliberately dropped this block and was right to**, for the reason its comment
/// gives: a <c>Proxies</c> block carries its own <c>$basetexture</c> naming a texture a proxy
/// animates, and folding that into the material's keys draws the wrong picture. Patches were
/// exempted from the same rule earlier and needed their own handling; proxies need a third
/// treatment again — kept, but kept SEPARATELY.
///
/// **The real fixture is a TF2 material, byte for byte**, because a synthetic one written from the
/// same belief as the parser cannot falsify it. `cappoint_logo_blue` is on every control point map
/// and carries the case inconsistency that matters: it writes <c>Sineperiod</c> and <c>SineMax</c>
/// where the engine's own <c>Init</c> reads <c>sinePeriod</c> and <c>sineMax</c>. KeyValues is
/// case-insensitive and so is this.
/// </remarks>
public sealed class VmtProxyBlockTests
{
    /// <summary>A real TF2 material, as shipped.</summary>
    private const string CapPointLogo = """
        "Modulate"
        {
        	"$basetexture" "models/effects/cappoint_logo_blue"
        //	"$additive" "1"
        	"$alpha" "1"
        	"$modblend" ".63"
        	"$model" "1"
        	"$mod2x" "1"
        	"Proxies"
        	{
        //		"Equals"
        //		{
        //			"srcvar1" "$modblend"
        //			"resultvar"  "$alpha"
        //		}
        		"Sine"
        		{
        			"Sineperiod" ".3"
        			"SineMax" ".7"
        			"SineMin" ".6"
        			"resultVar" "$alpha"
        		}
        	}
        }
        """;

    [Test]
    public void VmtProxies_ARealMaterialsProxy_IsRead()
    {
        VmtMaterial material = Parse(CapPointLogo);

        material.Proxies.Count.ShouldBe(1);
        material.Proxies[0].Name.ShouldBe("Sine");
    }

    [Test]
    public void VmtProxies_ACommentedOutProxy_IsNotRead()
    {
        // The `Equals` proxy above is commented out line by line. A parser that stripped comments
        // only at the top level, or that matched block names before stripping them, would find two
        // proxies and animate `$alpha` from a variable nothing maintains.
        Parse(CapPointLogo).Proxies.ShouldAllBe(proxy => proxy.Name != "Equals");
    }

    [Test]
    public void VmtProxies_Arguments_AreReadCaseInsensitively()
    {
        // **The case inconsistency is in Valve's own shipped file.** It writes `Sineperiod` and
        // `SineMax`; the engine's Init reads "sinePeriod" and "sineMax". KeyValues does not care
        // and neither can this — a case-sensitive lookup silently gets the default and oscillates
        // at the wrong rate rather than failing.
        MaterialProxy sine = Parse(CapPointLogo).Proxies[0];

        sine.Argument("sineperiod").ShouldBe(".3");
        sine.Argument("SINEPERIOD").ShouldBe(".3");
        sine.Argument("sineMax").ShouldBe(".7");
        sine.Argument("sinemin").ShouldBe(".6");
        sine.Argument("resultvar").ShouldBe("$alpha");
    }

    [Test]
    public void VmtProxies_AnAbsentArgument_IsNullNotEmpty()
    {
        // So the caller can tell "not stated" from "stated as nothing" and apply the engine's
        // default rather than parsing an empty string as zero.
        Parse(CapPointLogo).Proxies[0].Argument("timeOffset").ShouldBeNull();
    }

    [Test]
    public void VmtProxies_AProxysKeys_DoNotBecomeTheMaterialsOwn()
    {
        // **The rule the parser already had, which must survive.** A proxy's `$basetexture` names
        // the texture it animates, not the surface's.
        VmtMaterial material = Parse(
            """
            "VertexLitGeneric"
            {
                "$basetexture" "models/real"
                "Proxies"
                {
                    "TextureScroll"
                    {
                        "$basetexture" "models/decoy"
                        "textureScrollVar" "$baseTextureTransform"
                    }
                }
            }
            """);

        material.BaseTexture.ShouldBe("models/real");
        material.Proxies[0].Argument("$basetexture").ShouldBe("models/decoy");
    }

    [Test]
    public void VmtProxies_SeveralProxies_AreKeptInOrder()
    {
        // A material may run more than one, and they are applied in the order written — two proxies
        // writing the same variable means the last wins, which is only well defined if the order
        // survives parsing.
        VmtMaterial material = Parse(
            """
            "UnlitGeneric"
            {
                "$basetexture" "effects/beam"
                "Proxies"
                {
                    "TextureScroll" { "textureScrollVar" "$baseTextureTransform" }
                    "Sine" { "resultVar" "$alpha" }
                }
            }
            """);

        material.Proxies.Select(proxy => proxy.Name).ShouldBe(["TextureScroll", "Sine"]);
    }

    [Test]
    public void VmtProxies_AMaterialWithNone_HasNone()
    {
        // The control, and the common case: an empty list rather than a null one, so no caller
        // needs a guard.
        Parse("\"LightmappedGeneric\" { \"$basetexture\" \"concrete/floor\" }")
            .Proxies.ShouldBeEmpty();
    }

    private static VmtMaterial Parse(string text) => VmtMaterial.Parse(Encoding.UTF8.GetBytes(text));
}
