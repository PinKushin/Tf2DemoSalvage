using System.Text;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// A <c>Patch</c> material's keys, which live inside a <c>replace</c> or <c>insert</c> block.
/// </summary>
/// <remarks>
/// **Every patch this project has ever resolved was a no-op, and a test said otherwise.** The
/// parser kept only keys at depth 1 — correct for a <c>Proxies</c> block, whose <c>$basetexture</c>
/// is not the surface's — and a patch's overrides sit at depth 2:
///
/// <code>
/// "patch"
/// {
///     "include"  "materials/ICARUS/GLASSCHROME001.vmt"
///     "replace"
///     {
///         "$envmap"  "maps/cp_process_final/c1568_1728_976"
///     }
/// }
/// </code>
///
/// So <c>Parse</c> returned a material carrying <c>include</c> and nothing else, and
/// <c>ApplyPatch</c> — which drops <c>include</c> and overlays the rest — overlaid nothing. The
/// merged result was the stock material, exactly, for every patch on every map.
///
/// **The existing test passed because its fixture put the keys at the top level of the patch**,
/// which is a shape real VMTs do not use. That is the second time in one session that a fixture
/// written from the same belief as the code confirmed it: see
/// <c>docs/findings/27-cubemap-placement.md</c> for the first. The real file above is what settled
/// it, pulled straight out of cp_process_final's pakfile.
///
/// The keys here are taken from that file rather than invented, so the shape cannot drift back.
/// </remarks>
public sealed class VmtPatchBlockTests
{
    /// <summary>A real patch VMT from cp_process_final's pakfile, byte for byte.</summary>
    private const string RealPatch = """
        "patch"
        {
        	"include"		"materials/ICARUS/GLASSCHROME001.vmt"
        	"replace"
        	{
        		"$envmap"		"maps/cp_process_final/c1568_1728_976"
        	}
        }
        """;

    [Test]
    public void VmtPatch_AReplaceBlocksKeys_BecomeTheMaterialsOwn()
    {
        Parse(RealPatch).EnvMap.ShouldBe("maps/cp_process_final/c1568_1728_976");
    }

    [Test]
    public void VmtPatch_AReplaceBlock_DoesNotHideTheInclude()
    {
        // The control on the fix: flattening must not lose what already worked.
        VmtMaterial patch = Parse(RealPatch);

        patch.IsPatch.ShouldBeTrue();
        patch.Include.ShouldBe("materials/ICARUS/GLASSCHROME001.vmt");
    }

    [Test]
    public void VmtPatch_AnInsertBlock_IsReadTheSameWay()
    {
        // vbsp writes `replace`; hand-authored VMTs use `insert` for a key the included material
        // does not have. Both are patch payloads and both were being dropped.
        Parse(
            """
            "patch"
            {
                "include" "materials/models/base.vmt"
                "insert"
                {
                    "$detail" "detail/metal"
                }
            }
            """)
            .Detail.ShouldBe("detail/metal");
    }

    [Test]
    public void VmtPatch_APatchCarryingBoth_AppliesBoth()
    {
        VmtMaterial patch = Parse(
            """
            "patch"
            {
                "include" "materials/models/base.vmt"
                "replace" { "$basetexture" "models/red" }
                "insert"  { "$detail" "detail/metal" }
            }
            """);

        patch.BaseTexture.ShouldBe("models/red");
        patch.Detail.ShouldBe("detail/metal");
    }

    [Test]
    public void VmtPatch_AReplacement_ReachesTheMergedMaterial()
    {
        // **The end-to-end claim, which is the one that was false.** Parsing the block is only
        // useful if ApplyPatch then overlays it — and ApplyPatch worked all along, having been
        // handed nothing to overlay.
        VmtMaterial merged = VmtMaterial.ApplyPatch(
            Parse(RealPatch),
            Parse("""
                "LightmappedGeneric"
                {
                    "$basetexture" "icarus/glasschrome001"
                    "$envmap" "env_cubemap"
                }
                """));

        merged.Shader.ShouldBe("LightmappedGeneric", "the shader comes from the included material");
        merged.BaseTexture.ShouldBe("icarus/glasschrome001", "the included material's own keys survive");
        merged.EnvMap.ShouldBe(
            "maps/cp_process_final/c1568_1728_976", "the patch replaces the stock env_cubemap");

        merged.WantsMapCubemap.ShouldBeFalse("a patched material names its baked cubemap");
    }

    [Test]
    public void VmtPatch_AProxiesBlock_IsNotFlattened()
    {
        // **The control that keeps the fix honest, and the reason depth-1-only existed.** A Proxies
        // block carries its own keys that are NOT the surface's — a proxy animating a texture names
        // a $basetexture that must never become the material's. Flattening every nested block
        // would fix patches and break this.
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
                    }
                }
            }
            """);

        material.BaseTexture.ShouldBe("models/real");
    }

    [Test]
    public void VmtPatch_AReplaceNestedInProxies_IsIgnored()
    {
        // The two rules meet here: `replace` is only a patch payload at the material's top level.
        // A block of that name deeper in is somebody else's, and a fix keyed on the NAME alone
        // rather than on the depth would take it.
        Parse(
            """
            "VertexLitGeneric"
            {
                "$basetexture" "models/real"
                "Proxies"
                {
                    "replace"
                    {
                        "$basetexture" "models/decoy"
                    }
                }
            }
            """)
            .BaseTexture.ShouldBe("models/real");
    }

    private static VmtMaterial Parse(string text) => VmtMaterial.Parse(Encoding.UTF8.GetBytes(text));
}
