using System.Text;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// <c>$selfillummask</c> as the engine defines it: a replacement for the base alpha (B327).
/// </summary>
/// <remarks>
/// **Valve's own description is the whole definition**, from the parameter's declaration:
/// <c>"If we bind a texture here, it overrides base alpha (if any) for self illum"</c>
/// (`vertexlitgeneric_dx9.cpp:62`). The shader states it as one expression rather than a branch —
///
/// <code>
/// float3 vSelfIllumMask = tex2D( SelfIllumMaskSampler, i.baseTexCoord.xy );
/// vSelfIllumMask = lerp( baseColor.aaa, vSelfIllumMask, g_SelfIllumMaskControl );
/// diffuseComponent = lerp( diffuseComponent, g_SelfIllumTint * albedo, vSelfIllumMask );
/// </code>
///
/// (`vertexlit_and_unlit_generic_ps2x.fxc:441-443`), where the control is 1 exactly when a mask is
/// bound. Read-from-source.
///
/// **Two things in that are easy to lose and both are pinned below.** The mask is gated on
/// `$selfillum` — `bool bHasSelfIllumMask = IS_FLAG_SET( MATERIAL_VAR_SELFILLUM ) &amp;&amp; …`
/// (`vertexlitgeneric_dx9_helper.cpp:289`) — so a mask on a material that does not light itself is
/// inert; and it is sampled on the BASE texture's coordinates rather than a set of its own.
///
/// **53 of the 30,684 materials TF2 ships declare one, every one inside a `&gt;=DX90` block**, so
/// none of them was reachable until those blocks were read (B326). The parameter census firing on
/// `$selfillummask` in that same run is how this was found.
/// </remarks>
public sealed class SelfIllumMaskConformanceTests
{
    [Test]
    public void SelfIllumMask_AMaterialThatNamesOne_ReportsThatTexture()
    {
        Material("""
            "LightmappedGeneric"
            {
            	"$basetexture" "signs/exit"
            	"$selfillum" "1"
            	"$selfillummask" "signs/exit_illum_mask"
            }
            """).SelfIllumMask.ShouldBe("signs/exit_illum_mask");
    }

    /// <remarks>
    /// **Null rather than the base texture's name**, which is the distinction the whole feature
    /// turns on. "No mask" does not mean "nothing glows" — it means the base map's ALPHA decides,
    /// so a reader that substituted the base texture here would mask self-illumination with the
    /// albedo's colour instead of its alpha and glow in the wrong places.
    /// </remarks>
    [Test]
    public void SelfIllumMask_AMaterialThatLightsItselfWithoutOne_ReportsNull()
    {
        Material("""
            "LightmappedGeneric"
            {
            	"$basetexture" "signs/exit"
            	"$selfillum" "1"
            }
            """).SelfIllumMask.ShouldBeNull();
    }

    /// <remarks>
    /// **The parse reports what the file says; the GATE is the resolver's** — `MapAssets` refuses to
    /// load a mask on a material with no `$selfillum`, matching
    /// `IS_FLAG_SET( MATERIAL_VAR_SELFILLUM ) &amp;&amp; …`. Asserted here as the pair it is: the reader
    /// must not silently drop a declared key, because the census counts what materials ASK for.
    /// </remarks>
    [Test]
    public void SelfIllumMask_AMaterialWithNoSelfIllumFlag_StillReportsTheKeyItDeclares()
    {
        VmtMaterial material = Material("""
            "LightmappedGeneric"
            {
            	"$basetexture" "signs/exit"
            	"$selfillummask" "signs/exit_illum_mask"
            }
            """);

        material.SelfIllumMask.ShouldBe("signs/exit_illum_mask");
        material.IsSelfIlluminated.ShouldBeFalse("nothing declared $selfillum");
    }

    /// <remarks>
    /// **The shape TF2 actually ships it in**, and the reason this parameter was invisible for the
    /// life of the project: every one of the 53 materials declaring a mask puts it behind a
    /// DirectX gate (B326). A reader with `$selfillummask` implemented and DX blocks unread would
    /// pass every test above and still never see one.
    /// </remarks>
    [Test]
    public void SelfIllumMask_DeclaredInsideADxBlockAsTf2ShipsIt_IsStillRead()
    {
        Material("""
            "VertexlitGeneric"
            {
            	"$basetexture" "models/props/console"
            	"$selfillum" "1"

            	">=DX90"
            	{
            		"$selfillummask" "models/props/console_illum"
            	}
            }
            """).SelfIllumMask.ShouldBe("models/props/console_illum");
    }

    private static VmtMaterial Material(string text) =>
        VmtMaterial.Parse(Encoding.UTF8.GetBytes(text));
}
