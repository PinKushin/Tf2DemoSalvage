using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Viewer3D.Tests;

/// <summary>
/// Whether a real map's <c>$color</c> and <c>$alpha</c> reach the renderer's material state.
/// </summary>
/// <remarks>
/// **The one test that can fail when the wiring is wrong.** <c>VmtModulationTests</c> proves
/// <c>VmtMaterial.Modulation</c> computes the right numbers when a test calls it, and says nothing
/// about whether production calls it or with what. That gap has shipped three no-ops in this
/// project with a green suite — a kill annotation matching the wrong type, a numeric lookup handed
/// strings, and a playback rate decoded, retained, unit-tested and never read.
///
/// So this asks what the unit tests cannot: load a real map through <c>MapAssets</c>, and check
/// that a material declaring a colour arrives at <c>MapTexture.Modulation</c> carrying it.
///
/// **It also carries the control.** Asserting only that SOME material is modulated passes against
/// an implementation that modulates every material, which would tint the whole map — so the
/// majority naming nothing must arrive null in the same run.
/// </remarks>
public sealed class ModulationWiringTests
{
    private const string MapName = "cp_process_final.bsp";

    [Test]
    public void ARealMapsDeclaredColoursReachTheMaterialState()
    {
        MapAssets assets = LoadTheMap();

        int carried = assets.Textures.Count(texture => texture is { Modulation: not null });
        int plain = assets.Textures.Count(texture => texture is { Modulation: null });

        TestContext.Out.WriteLine(
            $"{carried} of {carried + plain} resolved materials carry a modulation");

        carried.ShouldBeGreaterThan(
            0,
            $"{MapName} declares materials naming $color, $color2 or $alpha, and one of them must " +
            "survive into the renderer's material state");

        // **The control.** Most materials name none, and those must arrive null — otherwise "the
        // modulation is carried" and "everything is tinted" are the same observation, and a bug
        // that multiplied every surface by a constant would pass the assertion above.
        plain.ShouldBeGreaterThan(
            carried, "the great majority of a map's materials declare no colour at all");
    }

    [Test]
    public void ACarriedModulationIsNeverTheIdentity()
    {
        // A modulation of exactly white and opaque changes nothing, so carrying one is pure cost.
        // The producing side filters on IsModulated; this is the assertion that it does, measured
        // on the far side of the load rather than at the call.
        MapAssets assets = LoadTheMap();

        foreach (MapTexture? texture in assets.Textures)
        {
            if (texture is { Modulation: { } modulation })
            {
                modulation.ShouldNotBe(
                    (1f, 1f, 1f, 1f), "an identity modulation should not have been carried");
            }
        }
    }

    [Test]
    public void ACarriedModulationHoldsTheValueItsOwnVmtDeclares()
    {
        // **The value, not just the presence.** A wiring that passed a constant, or passed the
        // detail tint by mistake, satisfies both tests above and fails here.
        //
        // Read back by an independent route: the VMT is fetched from the archives by the material's
        // own name and parsed fresh, rather than being asked of the same object under test. Patch
        // materials are skipped — resolving an include here would be re-implementing MapAssets'
        // own resolution, and two copies of one algorithm can agree while both being wrong.
        MapAssets assets = LoadTheMap();
        GameArchives archives = GameArchives.Open(Tf2Install.Folder);

        List<string> compared = [];

        for (int index = 0; index < assets.Textures.Count; index++)
        {
            if (assets.Textures[index] is not { Modulation: { } carried })
            {
                continue;
            }

            string name = assets.Materials[index].Name;

            if (archives.Read($"materials/{name}.vmt") is not { } bytes)
            {
                continue;
            }

            VmtMaterial declared = VmtMaterial.Parse(bytes);

            if (declared.IsPatch)
            {
                continue;
            }

            carried.ShouldBe(declared.Modulation, $"materials/{name}.vmt");
            compared.Add(name);
        }

        TestContext.Out.WriteLine(
            $"{compared.Count} carried modulations matched their own VMT: " +
            string.Join(", ", compared.Take(8)));

        compared.Count.ShouldBeGreaterThan(
            0, "no non-patch material with a modulation was found to compare against");
    }

    /// <summary>Loads the reference map, or skips.</summary>
    /// <summary>Shared with every other test wanting this map at the default size.</summary>
    /// <remarks>
    /// This measures material STATE rather than pixels, so it does not care about the texture size
    /// — which is precisely why it takes the shared one rather than naming a number of its own.
    /// Six tests naming six "small enough" sizes was six loads.
    /// </remarks>
    private static MapAssets LoadTheMap() => MapCache.Load();
}
