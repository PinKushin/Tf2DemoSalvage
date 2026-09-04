using System.Text;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// Reading the DirectX-level sub-blocks a VMT gates parameters behind (B326).
/// </summary>
/// <remarks>
/// **A block named for a DirectX support level merges its keys only when that level is met.** This
/// project draws through Direct3D 11, so every `&gt;=DX*` block TF2 ships is satisfied and every
/// `&lt;DX*` one is not — the low-end path is the branch we are never on.
///
/// **The measurement that made this worth doing, over the 30,684 materials TF2 ships:**
///
/// | block | materials |
/// |---|---|
/// | `&gt;=DX90` | 5,688 |
/// | `&lt;dx90` | 281 |
/// | `&gt;=dx90_20b` | 10 |
/// | `&lt;dx90_20b` | 5 |
///
/// and inside `>=DX90`, by far the most common key is **`$selfillum`, in 5,415 of them** — a
/// parameter this project implements and was reading in none of those materials. `$envmap` accounts
/// for 59 and is what found the gap: `gold_player.vmt` declares `$envmaptint` at the top level and
/// its `$envmap` inside the block, so a golden corpse carried the tint of a reflection it had no
/// cubemap for.
///
/// ```
/// dotnet run --project tools/Tf2DemoSalvage.Probe -c Release -- vmt-blocks ">=DX90"
/// ```
///
/// **Evidence class: shipped data plus convention, NOT read-from-source.** `source-sdk-2013`
/// publishes `shaderapidx9` and `stdshaders` but not the material system's VMT loader, so the
/// merge itself cannot be quoted. What is measured is which spellings TF2 actually uses and what
/// they contain; the rule that `>=` means "at least this level" is the convention every one of
/// those files is written against. Flagged because an interpolation that reads like a measurement
/// is how a wrong conclusion gets repeated.
///
/// **The `&lt;Shader&gt;_DX&lt;n&gt;` blocks are a DIFFERENT mechanism and deliberately not handled
/// here.** `LightmappedGeneric_DX9` (403), `Water_DX60` (46), `Eyes_dx8` (38) and their kin name a
/// whole fallback SHADER for a level rather than gating parameters within one. They are all
/// low-end fallbacks, so ignoring them is right for this renderer for the same reason `&lt;dx90` is
/// ignored — but it is right by accident of which levels TF2 ships, not by rule.
/// </remarks>
public sealed class VmtDxLevelBlockConformanceTests
{
    /// <remarks>
    /// **Byte for byte from the game**, because a synthetic fixture written from the same belief as
    /// the parser cannot falsify it. This is the material that found the gap.
    /// </remarks>
    private const string GoldPlayer = """
        "VertexlitGeneric"
        {
        	"$baseTexture" "models/player/shared/gold_player"
        	"$bumpmap" "models/effects/flat_normal"
        	"$yellow" "0"

        	">=DX90"
        	{
        		"$envmap" "cubemaps/cubemap_gold001"
        	}
        	"<DX90"
        	{
        		"$envmap" "cubemaps/cubemap_gold001"
        	}

        	"$envmaptint" "[1.5 1.2 .2]"
        }
        """;

    /// <remarks>
    /// **The real material, and it is here as the motivating case rather than as the discriminator.**
    /// A sabotage found why: `gold_player.vmt` declares the SAME `$envmap` in both its `&gt;=DX90`
    /// and its `&lt;DX90` block, so claiming DirectX 8 still produces this answer. It falsifies
    /// "the reader ignores sub-blocks" — which is what it was written for, and it was red against
    /// exactly that — and it cannot tell WHICH block was read. The test below is the one that can.
    /// </remarks>
    [Test]
    public void Parse_TheShippedGoldPlayerMaterial_ReadsTheEnvMapItGatesBehindADxBlock()
    {
        Material(GoldPlayer).EnvMap.ShouldBe("cubemaps/cubemap_gold001");
    }

    /// <remarks>
    /// **Only the high block carries the value, so this fails if the wrong condition is taken.**
    /// Every shipped `&gt;=DX90` block whose key is not repeated below — 5,415 of them declaring
    /// `$selfillum` — has this shape, so it is the common case rather than a contrived one.
    /// </remarks>
    [Test]
    public void Parse_AParameterOnlyInsideAGreaterOrEqualDx90Block_ReadsIt()
    {
        VmtMaterial material = Material("""
            "LightmappedGeneric"
            {
            	"$basetexture" "signs/exit"

            	">=DX90"
            	{
            		"$selfillum" "1"
            	}
            	"<DX90"
            	{
            		"$selfillumtint" "[0 0 0]"
            	}
            }
            """);

        material.Value("$selfillum").ShouldBe("1");

        // The other side of the same file, which is what makes this a discriminator: taking the
        // low branch instead would produce the tint and no self-illumination.
        material.Value("$selfillumtint").ShouldBeNull();
    }

    /// <remarks>
    /// **The control, and without it the test above is satisfied by ignoring the block name.** A
    /// parser that simply flattened every sub-block would read both, which is wrong in the other
    /// direction — it would apply the low-end path's `$fallbackmaterial`, `$outlinecolor` and
    /// friends, measured on 281 shipped materials.
    ///
    /// The two blocks in `gold_player.vmt` happen to declare the SAME `$envmap`, which makes it
    /// useless as a discriminator on its own — so this fixture puts a key in the low block that
    /// appears nowhere else.
    /// </remarks>
    [Test]
    public void Parse_AParameterInsideALessThanDx90Block_IgnoresIt()
    {
        VmtMaterial material = Material("""
            "LightmappedGeneric"
            {
            	"$basetexture" "concrete/wall"

            	"<dx90"
            	{
            		"$fallbackmaterial" "concrete/wall_cheap"
            	}
            }
            """);

        material.Value("$fallbackmaterial").ShouldBeNull();

        // The bystander: the shader's own keys must be untouched by the block being refused.
        material.Value("$basetexture").ShouldBe("concrete/wall");
    }

    /// <remarks>
    /// **`dx90_20b` is shader model 2.0b at dxlevel 90**, and TF2 ships ten materials gating on it
    /// against five on `&lt;dx90_20b`. It is above nothing this renderer cannot do, so it is
    /// satisfied exactly as `&gt;=DX90` is — asserted separately because a parser matching the
    /// literal string `"&gt;=DX90"` would pass every test above and drop these.
    /// </remarks>
    [Test]
    public void Parse_AParameterInsideAGreaterOrEqualDx9020bBlock_ReadsIt()
    {
        Material("""
            "VertexlitGeneric"
            {
            	"$basetexture" "models/weapons/thing"

            	">=dx90_20b"
            	{
            		"$phong" "1"
            	}
            }
            """).Value("$phong").ShouldBe("1");
    }

    /// <remarks>
    /// **Later wins, which is the order the file states and the order KeyValues merges in.** A
    /// gated key that overrode a top-level one declared AFTER it would be reading the file
    /// backwards, and `gold_player.vmt` is exactly this shape — `$envmaptint` sits below the block.
    /// </remarks>
    [Test]
    public void Parse_AGatedKeyRestatedBelowTheBlock_TakesTheLaterValue()
    {
        Material("""
            "VertexlitGeneric"
            {
            	">=DX90"
            	{
            		"$envmap" "cubemaps/first"
            	}

            	"$envmap" "cubemaps/second"
            }
            """).EnvMap.ShouldBe("cubemaps/second");
    }

    /// <remarks>
    /// **The other bystander, and the one most at risk from this change.** `Proxies` and `replace`
    /// are also depth-two blocks and mean entirely different things — a proxy's `$basetexture`
    /// names a texture the proxy animates, and folding it in draws the wrong picture (B81). A
    /// condition test written as "any block whose name is not Proxies" would break that.
    /// </remarks>
    [Test]
    public void Parse_AProxysOwnBaseTexture_StaysOutOfTheMaterialsKeys()
    {
        Material("""
            "Modulate"
            {
            	"$basetexture" "models/effects/logo"

            	"Proxies"
            	{
            		"AnimatedTexture"
            		{
            			"animatedtexturevar" "$basetexture"
            			"$basetexture" "models/effects/not_this_one"
            		}
            	}
            }
            """).Value("$basetexture").ShouldBe("models/effects/logo");
    }

    private static VmtMaterial Material(string text) =>
        VmtMaterial.Parse(Encoding.UTF8.GetBytes(text));
}
