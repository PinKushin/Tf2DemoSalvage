using System.Text;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// The shader-fallback blocks a VMT names, and which of them this renderer takes (B328).
/// </summary>
/// <remarks>
/// **A block named for a SHADER supplies that material's parameters for when that shader is the one
/// drawing it.** The mechanism is the fallback chain, which the SDK states plainly:
///
/// <code>
/// SHADER_FALLBACK
/// {
///     if( g_pHardwareConfig->GetDXSupportLevel() &lt; 90 )
///         return "LightmappedGeneric_DX8";
///     return 0;
/// }
/// </code>
///
/// `lightmappedgeneric_dx9.cpp:139-145`, with `DEFINE_FALLBACK_SHADER( LightmappedGeneric,
/// LightmappedGeneric_DX8 )` registering the substitute (`lightmappedgeneric_dx8.cpp:21`).
///
/// **The measurement that made this worth doing, and it corrected a wrong claim.** These blocks were
/// first written off as "all low-end fallbacks" — an inference from the names, with nothing read
/// inside one. Over the 30,684 materials TF2 ships:
///
/// | block | materials | declaring a key ONLY there |
/// |---|---|---|
/// | `LightmappedGeneric_DX9` | 403 | `$bumpmap` 89, `$envmap` 49, `$parallaxmap` 8 |
/// | `LightmappedGeneric_DX8` | 201 | the cheap path |
/// | `LightmappedGeneric_HDR_DX9` | 63 | the HDR variant |
///
/// `tile/tilefloor018a_c17.vmt` is the worked example: a `LightmappedGeneric` material whose entire
/// bump-and-reflection setup lives inside its `LightmappedGeneric_DX9` block and nowhere else.
/// Ignoring it draws that floor flat and matte on hardware that has had bump mapping for twenty
/// years.
///
/// ```
/// dotnet run --project tools/Tf2DemoSalvage.Probe -c Release -- vmt-blocks "LightmappedGeneric_DX9"
/// ```
///
/// **The rule this renderer applies**, and each clause is load-bearing:
///
/// - **Level 90 and above only.** A `_DX8` block belongs to a DIFFERENT shader, the one the engine
///   substitutes below 90 — so "at or below our level" is the wrong test and would paint DX8 content
///   on DX9 hardware. `Water_DX80`, `Eyes_dx8` and `Refract_DX60` are all refused for this reason.
/// - **Not the HDR variants.** This project reads the LDR lightmap lump by deliberate decision (see
///   `BspLightmaps.Read`), so `_HDR_DX9` is not the path it is on.
/// - **The prefix must be the material's own shader.** A `VertexLitGeneric_DX9` block inside a
///   `LightmappedGeneric` material names somebody else's shader.
/// - **Both spellings of the level.** TF2 ships `_DX9` and `_DX90` for the same thing, and `_DX8`
///   beside `_DX80`, so a single digit is a major version and reads as ninety.
///
/// **Evidence class: shipped data, plus the SDK for the fallback mechanism.** What cannot be quoted
/// is why 403 materials name a block for `LightmappedGeneric_DX9` when no shader is REGISTERED
/// under that name anywhere in `source-sdk-2013` — only helper types and functions carry the
/// spelling. Either TF2's engine registers it and the SDK snapshot omits it, or the material system
/// matches these by a rule other than the exact name. The behaviour is settled by the data either
/// way: Valve did not ship a bump map that draws on no hardware.
/// </remarks>
public sealed class VmtFallbackShaderBlockConformanceTests
{
    /// <summary>A shipped material, byte for byte, whose bump lives only in the block.</summary>
    private const string TileFloor = """
        // envmaptint_fix
        "LightmappedGeneric"
        {
        	"$basetexture" "Tile/tilefloor018a"
        	"$surfaceprop" "tile"
        	"%keywords" "c17downtown,wasteland"

        	 "LightmappedGeneric_DX9"
        	{
        		"$bumpmap" "tile/tilefloor018a_normal"
        		"$envmap" "env_cubemap"
        		"$normalmapalphaenvmapmask" 1
        		"$envmapcontrast" 1
        		"$envmapsaturation" 1
        		"$envmaptint" "[ .80 .80 .80 ]"
        	}
        }
        """;

    [Test]
    public void Parse_TheShippedTileFloor_ReadsTheBumpAndEnvMapFromItsDx9Block()
    {
        VmtMaterial material = Material(TileFloor);

        material.BumpMap.ShouldBe("tile/tilefloor018a_normal");
        material.EnvMap.ShouldBe("env_cubemap");
    }

    /// <remarks>
    /// **A DX8 block is a different SHADER's parameters, not a lower-quality version of ours.** This
    /// is the clause that makes "at or below our level" wrong: 80 is below 95, and taking it would
    /// paint the cheap path on hardware running the expensive one.
    /// </remarks>
    [Test]
    public void Parse_ALowerLevelFallbackBlock_IsRefused()
    {
        VmtMaterial material = Material("""
            "LightmappedGeneric"
            {
            	"$basetexture" "tile/floor"

            	"LightmappedGeneric_DX8"
            	{
            		"$basetexture" "tile/floor_cheap"
            	}
            }
            """);

        material.Value("$basetexture").ShouldBe("tile/floor");
    }

    /// <remarks>
    /// **The HDR variant is refused because this renderer is LDR**, by the deliberate decision
    /// recorded on `BspLightmaps.Read`: a map compiled for both carries LDR in lump 8 and HDR in
    /// lump 53, and preferring HDR washed the map out because the two are scaled differently.
    /// Taking an `_HDR_DX9` block would be a divergence in the other direction from ignoring
    /// `_DX9`.
    /// </remarks>
    [Test]
    public void Parse_TheHdrVariantOfADx9Block_IsRefusedWhileThePlainOneIsTaken()
    {
        VmtMaterial material = Material("""
            "LightmappedGeneric"
            {
            	"$basetexture" "tile/floor"

            	"LightmappedGeneric_DX9"
            	{
            		"$envmaptint" "[.8 .8 .8]"
            	}
            	"LightmappedGeneric_HDR_DX9"
            	{
            		"$envmapcontrast" "1"
            	}
            }
            """);

        material.Value("$envmaptint").ShouldBe("[.8 .8 .8]");
        material.Value("$envmapcontrast").ShouldBeNull();
    }

    /// <remarks>
    /// **Another shader's block is not this material's**, which is what stops a fallback name being
    /// read as a generic "high quality" section. TF2 ships `VertexLitGeneric_DX9` (28 materials)
    /// and `WorldVertexTransition_DX9` (1) alongside the lightmapped ones.
    /// </remarks>
    [Test]
    public void Parse_ABlockNamingADifferentShader_IsRefused()
    {
        Material("""
            "LightmappedGeneric"
            {
            	"$basetexture" "tile/floor"

            	"VertexLitGeneric_DX9"
            	{
            		"$phong" "1"
            	}
            }
            """).Value("$phong").ShouldBeNull();
    }

    /// <remarks>
    /// **`_DX90` and `_DX9` are the same level in two spellings**, and TF2 ships both — `Water_DX90`
    /// beside `Water_DX80`. A single digit is a major version, so it reads as ninety rather than as
    /// nine.
    /// </remarks>
    [Test]
    public void Parse_TheTwoDigitSpellingOfTheLevel_IsTheSameAsTheOneDigitOne()
    {
        Material("""
            "Water"
            {
            	"$abovewater" "1"

            	"Water_DX90"
            	{
            		"$refracttexture" "_rt_WaterRefraction"
            	}
            }
            """).Value("$refracttexture").ShouldBe("_rt_WaterRefraction");
    }

    /// <remarks>
    /// **The bystander, and it is the one most at risk.** `Proxies` and a patch's `replace` are
    /// depth-two blocks too and mean entirely different things — a proxy's `$basetexture` names a
    /// texture the proxy animates, and folding it in draws the wrong picture (B81).
    /// </remarks>
    [Test]
    public void Parse_AProxyBlockBesideAFallbackBlock_StaysOutOfTheMaterialsKeys()
    {
        VmtMaterial material = Material("""
            "LightmappedGeneric"
            {
            	"$basetexture" "tile/floor"

            	"LightmappedGeneric_DX9"
            	{
            		"$bumpmap" "tile/floor_normal"
            	}
            	"Proxies"
            	{
            		"AnimatedTexture"
            		{
            			"$basetexture" "tile/not_this_one"
            		}
            	}
            }
            """);

        material.BumpMap.ShouldBe("tile/floor_normal");
        material.Value("$basetexture").ShouldBe("tile/floor");
    }

    private static VmtMaterial Material(string text) =>
        VmtMaterial.Parse(Encoding.UTF8.GetBytes(text));
}
