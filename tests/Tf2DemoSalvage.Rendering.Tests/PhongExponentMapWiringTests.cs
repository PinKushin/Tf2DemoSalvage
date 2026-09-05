using System.IO;
using System.Linq;

using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// That the phong exponent map and its sentinels reach a real map's materials (B334).
/// </summary>
/// <remarks>
/// **The hop nothing else watches.** `PhongExponentTextureConformanceTests` proves the VMT reader
/// answers correctly when a test hands it text; it says nothing about whether a map load asks, or
/// whether the answer survives `MapAssets`. Three no-ops have shipped here with green component
/// suites for exactly that reason.
///
/// **And the exponent default is the assertion that could not have been written before.** A phong
/// material stating no `$phongexponent` had been drawn at 5 — the parameter's DECLARED default —
/// where the engine draws it at 150, because it binds white to the exponent sampler and computes
/// `1 + 149 × 1`. That is a factor of thirty, on materials nobody would think to check, and no
/// component test could see it: the reader was returning exactly what the SDK's `SHADER_PARAM` line
/// says.
/// </remarks>
public sealed class PhongExponentMapWiringTests
{
    /// <remarks>
    /// **Counted with the majority as the control**, the same shape as the transform wiring test.
    /// "Phong arrives" and "everything has phong" are the same observation without it.
    ///
    /// **This test asserted the 150 default and was passing for the wrong reason.** It counted
    /// materials whose arrived exponent equals 150 and called them "materials that state no
    /// `$phongexponent`" — an unfaithful proxy, because a material STATING 150 is indistinguishable
    /// from one falling back to it once the value has arrived. cp_process_final has exactly one
    /// material at 150, `models/props_gameplay/bottle001`, and reading its VMT settles it:
    ///
    /// <code>
    /// "$phong" "1"
    /// "$phongexponent" "150"
    /// </code>
    ///
    /// It states 150. **Zero of this map's materials take the engine's default**, so B334's
    /// exponent change alters nothing here — and the assertion that "proved" otherwise would have
    /// gone on passing against a reader that had never been fixed.
    ///
    /// The real denominator is 170 of the 30,684 materials TF2 ships
    /// (`vmt-param $phong !$phongexponent`) — paint-kit tools, flame balls, taunt props and
    /// weapon warpaints, which are model materials rather than map ones. The claim belongs where
    /// the VMT is visible, and it lives in `PhongExponentTextureConformanceTests` and
    /// `PhongConformanceTests`.
    /// </remarks>
    [Test]
    public void Phong_ARealMapsMaterials_CarryTheExponentTheEngineWouldUse()
    {
        if (Assets is not { } assets)
        {
            Assert.Ignore("the map or the game is not installed");
            return;
        }

        int withPhong = assets.Phong.Count(phong => phong is not null);
        int without = assets.Phong.Count(phong => phong is null);

        // **NAMED, not just counted**, because a count cannot be looked at and a name can — and
        // because naming it is what caught the wrong reading above.
        string[] exponents =
        [
            .. Enumerable.Range(0, assets.Phong.Count)
                .Where(index => assets.Phong[index] is not null)
                .Select(index =>
                    $"{assets.Materials[index].Name}={assets.Phong[index]!.Value.Exponent}"),
        ];

        TestContext.Out.WriteLine(
            $"{withPhong} of {withPhong + without} materials have phong: "
            + string.Join(", ", exponents.Take(8))
            + (exponents.Length > 8 ? $" and {exponents.Length - 8} more" : string.Empty));

        withPhong.ShouldBeGreaterThan(0, "cp_process_final's models ask for phong");

        without.ShouldBeGreaterThan(
            withPhong, "the great majority of a map's materials are brushwork with no highlight");

        // **The spread is the assertion, and it is what a broken reader would lose.** A reader that
        // ignored the VMT would hand every phong material the same number — whether that number is
        // 5, 150, or anything else — so counting DISTINCT exponents catches a constant where an
        // equality against any one value cannot.
        assets.Phong
            .Where(phong => phong is not null)
            .Select(phong => phong!.Value.Exponent)
            .Distinct()
            .Count()
            .ShouldBeGreaterThan(
                1, "these materials state different exponents and must not collapse to one");
    }

    /// <remarks>
    /// **Every exponent the load produces must be one the shader can use.** The renderer encodes
    /// "read the map" as a NEGATIVE constant — Valve's own sentinel — so a value between the two is
    /// the one shape that would be silently wrong: `pow(x, 0)` is 1 for every pixel, which floods a
    /// model with highlight rather than removing it.
    /// </remarks>
    [Test]
    public void Phong_EveryExponentAMapLoadProduces_IsUsableOrTheMapSentinel()
    {
        if (Assets is not { } assets)
        {
            Assert.Ignore("the map or the game is not installed");
            return;
        }

        float[] unusable =
        [
            .. assets.Phong
                .Where(phong => phong is { } present &&
                    !(present.Exponent > 0f) &&
                    !(present.ExponentFromMap && present.Exponent > 0f))
                .Select(phong => phong!.Value.Exponent)
                .Where(exponent => exponent == 0f),
        ];

        unusable.ShouldBeEmpty("an exponent of zero raises every dot product to the power zero");
    }

    /// <remarks>
    /// **The list must be parallel to the materials, and that is the assertion that can fail.**
    /// Every other list in `MapAssets` is indexed by material number; a new one that is appended in
    /// some paths and not others goes wrong silently, because the renderer's bounds check
    /// (`index &lt; assets.PhongExponentMaps.Count`) turns a short list into "no exponent map" for
    /// every material past the end rather than into an error.
    ///
    /// **cp_process_final resolves ZERO exponent maps, and that is stated rather than asserted.**
    /// Measured: 0 of 412. The 1,862 shipped materials that name one are cosmetics, weapons and
    /// bots, which enter this same table through the PROP path when a demo loads them — so this map
    /// cannot exercise the texture itself, and an assertion pretending otherwise would be a
    /// prediction about Valve's level design. What it can prove is the plumbing, which is what a
    /// short list would break. `PhongExponentTextureReadingTests` reads a real shipped material that
    /// does name one.
    /// </remarks>
    [Test]
    public void PhongExponentMaps_ARealMapsMaterials_AreIndexedAlongsideThem()
    {
        if (Assets is not { } assets)
        {
            Assert.Ignore("the map or the game is not installed");
            return;
        }

        int resolved = assets.PhongExponentMaps.Count(map => map is not null);

        TestContext.Out.WriteLine(
            $"{resolved} of {assets.PhongExponentMaps.Count} materials resolved an exponent map");

        assets.PhongExponentMaps.Count.ShouldBe(
            assets.Materials.Count, "the list is indexed by material and must be parallel to them");

        // The control on that: every OTHER per-material list is the same length, so a comparison
        // against `Materials` alone would also pass if the table stopped growing altogether.
        assets.PhongExponentMaps.Count.ShouldBe(
            assets.SelfIllumMasks.Count, "and parallel to its neighbours, which grow in one call");
    }

    private static MapAssets? Assets
    {
        get
        {
            if (GameInstall.Root is not { } tf ||
                !File.Exists(Path.Combine(tf, "maps", "cp_process_final.bsp")))
            {
                return null;
            }

            return MapCache.Load();
        }
    }
}
