using System;
using System.Collections.Generic;
using System.Linq;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// No placed prop draws in the missing-material chequer, on maps the game draws without one.
/// </summary>
/// <remarks>
/// **B229.** The owner saw pipe elbows and flat skybox panels drawn in Valve's chequer on
/// <c>cp_fulgur</c> — *"the real tf2 doesnt show the purple and black texture anywhere on this map,
/// its not a new map"*. `MapWorld` reported 19,274 triangles naming material −1, and the material
/// inventory reported every material resolved, because none of them FAILED.
///
/// **The cause was privileging skin family zero.** A mesh's <c>mstudiomesh_t::material</c> is a
/// SKINREF, and <c>g_skinref[skin][skinref]</c> turns it into a texture index
/// (<c>utils/motionmapper/motionmapper.h:134</c>). This project resolved family zero for each mesh
/// and expressed every other family as a swap from that resolved index, so a family-zero material
/// the map does not pack poisoned every family. `props_aquatic/pipe_256.mdl` is placed at skins 1
/// and 12 of 15, both of whose textures `cp_fulgur` packs; family zero's it does not.
/// `StudioSkinsConformanceTests`, in `Tf2DemoSalvage.Content.Tests`, pins the rule itself.
///
/// **This asserts on the OUTPUT, which is the only level that can fail when the wiring is wrong**
/// (`docs/memory/output-level-assertion-or-it-is-not-done.md`). The unit test above proves the
/// lookup obeys the engine when called; this proves the loader calls it, on a real map, and that
/// what comes out is what gets drawn — <see cref="MapAssets.Props"/> is the exact list
/// `MapWorldBuilder.AppendProps` reads to build the prop batches.
///
/// **It is not a claim about the map (D38).** The claim is about this project: TF2 draws every one
/// of these props, so a corner here that names no material is this project failing to read a model.
/// </remarks>
public sealed class PropMaterialResolutionTests
{
    /// <summary>The subsystem static props report under (D83).</summary>
    private const string Props = "props";

    /// <summary>The map the owner saw the chequer on.</summary>
    private const string Fulgur = "cp_fulgur";

    /// <summary>`Register`'s two ways of returning −1, for naming the models in a failure.</summary>
    private static readonly string[] Refusals =
    [
        "outside the model's own list",
        "produced no texture",
    ];

    [Test]
    public void Load_TheMapTheOwnerSawTheChequerOn_PlacesNoPropWithAnUnresolvedMaterial()
    {
        // No explicit guard: MapCache skips the calling test when the map is not installed, which
        // is the behaviour CI relies on (`docs/memory/ci-is-the-machine-without-tf2.md`).
        Unresolved(Fulgur).ShouldBe(
            0,
            "TF2 draws every one of these props; a corner that names no material draws in the "
            + "missing-material chequer, which is what the owner saw. Models that refused a "
            + "material: " + string.Join("; ", Named(Fulgur).Take(6)));
    }

    [Test]
    public void Load_TheReferenceMap_PlacesNoPropWithAnUnresolvedMaterial()
    {
        // **The control, and it is the reason this is two tests.** cp_process_final's props all
        // resolved before this change and must still. If both maps failed, the finding would be
        // about model reading in general and the search would start somewhere else entirely.
        Unresolved(MapCache.DefaultMap).ShouldBe(0);
    }

    /// <summary>How many placed corners name no material at all.</summary>
    private static int Unresolved(string mapName)
    {
        MapCache.LoadedMap loaded = MapCache.With(mapName: mapName);

        // **A precondition on the instrument.** Zero unresolved corners means either "everything
        // resolved" or "no prop was placed", and B229 spent four hypotheses on exactly that kind of
        // ambiguity. A non-empty prop list settles it.
        loaded.Assets.Props.Count.ShouldBeGreaterThan(
            0, $"{mapName} produced no placed prop geometry at all");

        return loaded.Assets.Props.Count(corner => corner.MaterialIndex < 0);
    }

    /// <summary>Every line the static-prop path wrote to say it could not paint something.</summary>
    /// <remarks>
    /// **For the failure MESSAGE, not for the assertion**, and the distinction matters. A model may
    /// legitimately refuse a material for a family nothing places — `cp_fulgur` packs
    /// `pipe256` through `pipe256c` and `pipe256f` and not `d` or `e`, and places skins 0, 1, 2 and
    /// 5 — so refusals are not by themselves a defect. What is drawn is.
    /// </remarks>
    private static IReadOnlyList<string> Named(string mapName)
    {
        IReadOnlyList<string> props = MapCache.With(mapName: mapName).Log.From(Props);

        return
        [
            .. props.Where(line => Refusals.Any(what => line.Contains(what, StringComparison.Ordinal))),
        ];
    }
}
