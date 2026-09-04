using System.IO;
using System.Linq;

using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// That a material's own <c>$basetexturetransform</c> reaches the renderer (B332).
/// </summary>
/// <remarks>
/// **The hop with nothing else watching it.** `TextureTransformConformanceTests` proves the matrix
/// is composed in Valve's order when a test calls the parser; it says nothing about whether a map
/// load ever asks. The failure is silent by construction — an unread transform is the identity,
/// which is what nearly every material legitimately has.
///
/// **And this one had a second way to be silent.** The transform rows in the material constants were
/// written as the identity at rest, because until now only a `TextureScroll` proxy ever set them —
/// so a material stating a STATIC transform and running no proxy had it decoded, carried, and then
/// overwritten. That is the same gap the modulation note in `WorldRenderer` records having shipped
/// once already, in the same four rows.
/// </remarks>
public sealed class TextureTransformWiringTests
{
    /// <remarks>
    /// **Counted, with the great majority as the control.** "A transform arrives" and "everything is
    /// transformed" are the same observation without it, and a parser that returned a transform for
    /// every material would pass a bare non-zero check while moving every texture in the map.
    /// </remarks>
    [Test]
    public void BaseTransform_ARealMapsMaterials_ArriveOnlyWhereTheVmtStatesOne()
    {
        if (Assets is not { } assets)
        {
            Assert.Ignore("the map or the game is not installed");
            return;
        }

        int stated = assets.Textures.Count(texture => texture is { BaseTransform: not null });
        int none = assets.Textures.Count(texture => texture is { BaseTransform: null });

        TestContext.Out.WriteLine(
            $"{stated} of {stated + none} materials state a $basetexturetransform");

        stated.ShouldBeGreaterThan(
            0, "cp_process_final declares $basetexturetransform and it must survive the load");

        none.ShouldBeGreaterThan(
            stated, "the great majority of a map's materials state no transform at all");
    }

    /// <remarks>
    /// **Every one of them changes something, which was not the assumption.** This test was first
    /// written the other way round — that some material states the parameter's neutral default —
    /// on the reasoning that a material asking for a transform that happens to change nothing still
    /// asked, and the census counts requests. Measured, **zero** of `cp_process_final`'s 21 are
    /// neutral: every material that names the parameter names a real transform.
    ///
    /// That makes the number a measure of what was being LOST rather than of what was asked for.
    /// Until now the four transform rows in the material constants were written as the identity at
    /// rest — only a `TextureScroll` proxy ever set them — so all 21 had their transform decoded and
    /// then overwritten.
    ///
    /// **Null and the identity are still kept apart in the type**, because the distinction is real
    /// even where this map does not exercise it: a material naming the neutral form has asked, and a
    /// census that collapsed the two would under-report the parameter on a map that states it
    /// neutrally.
    /// </remarks>
    [Test]
    public void BaseTransform_EveryOneARealMapStates_ChangesTheCoordinate()
    {
        if (Assets is not { } assets)
        {
            Assert.Ignore("the map or the game is not installed");
            return;
        }

        string[] neutral =
        [
            .. Enumerable.Range(0, assets.Textures.Count)
                .Where(index => assets.Textures[index] is { BaseTransform: { } transform }
                    && transform.IsIdentity)
                .Select(index => assets.Materials[index].Name),
        ];

        string[] stated =
        [
            .. Enumerable.Range(0, assets.Textures.Count)
                .Where(index => assets.Textures[index] is { BaseTransform: not null })
                .Select(index => assets.Materials[index].Name),
        ];

        TestContext.Out.WriteLine(
            $"stating a transform: {string.Join(", ", stated.Take(6))}"
            + (stated.Length > 6 ? $" and {stated.Length - 6} more" : string.Empty));

        neutral.ShouldBeEmpty(
            "measured: every material on this map that names $basetexturetransform names one that "
            + "changes something, so all of them were being lost");
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
