using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Viewer3D;

namespace Tf2DemoSalvage.Viewer3D.Tests;

/// <summary>
/// What a real map asks for that this project does not do, asserted rather than logged.
/// </summary>
/// <remarks>
/// **Every bug this class is written against was visible in a log and invisible in a test.** B55
/// spent an hour finding that 42 of 189 materials declare an unimplemented <c>$envmap</c>; B81 found
/// the census that reports such things had never looked at prop materials at all, 1,034 of them; B83
/// then spent four hypotheses on capture points that draw wrong, while four REFUSED vertex-lighting
/// files sat inside a total that read as ordinary.
///
/// The common shape is not that the information was missing. It is that **nothing failed**. A log
/// line is not a signal until someone reads it, and the person who needed to read it was trying to
/// explain a picture rather than auditing a load.
///
/// So this asserts the inventory:
///
/// - every unimplemented parameter a real map asks for is on a named list, so a NEW one fails;
/// - the census actually examined props, so an instrument that goes blind fails instead of
///   reporting clean;
/// - no prop's baked lighting was refused, because a refusal draws a prop as though the compiler
///   never lit it.
///
/// **These need Team Fortress 2 installed** and skip without it, like every other test here that
/// reads a real map. That is a real limit: the gap they guard is exactly the kind that only appears
/// on real content, and a machine without the game is not checking it.
/// </remarks>
public sealed class AssetCoverageConformanceTests
{
    /// <summary>The map these are measured on: it has capture points, props and displacements.</summary>
    private const string MapName = "cp_process_final.bsp";

    /// <summary>Where Team Fortress 2 is, or null when it is not installed.</summary>
    /// <remarks>
    /// **The same resolution the other map tests use, and writing a third copy is what skipped this
    /// whole class on its first run.** An env-var-only lookup returned nothing on a machine with the
    /// game installed, and three tests reported "skipped" — which in a suite reads as "not
    /// applicable here" rather than "your helper is worse than the one next door".
    /// </remarks>
    private static string? GameFolder
    {
        get
        {
            string? configured = Environment.GetEnvironmentVariable("TF2_FOLDER");

            if (!string.IsNullOrWhiteSpace(configured) && Directory.Exists(configured))
            {
                return configured;
            }

            foreach (string root in new[]
            {
                @"C:\Program Files (x86)\Steam\steamapps\common\Team Fortress 2\tf",
                @"F:\SteamLibrary\steamapps\common\Team Fortress 2\tf",
                @"D:\SteamLibrary\steamapps\common\Team Fortress 2\tf",
            })
            {
                if (File.Exists(Path.Combine(root, "tf2_textures_dir.vpk")))
                {
                    return root;
                }
            }

            return null;
        }
    }

    [Test]
    public void TheCensusExaminesPropsAndNotOnlyBrushwork()
    {
        // **The B81 catcher, and the most important test in this file.** The census reported a
        // clean bill for months while never looking at a single prop material — an instrument
        // answering confidently about a subset it had silently chosen. A coverage report that
        // cannot fail this way is worth very little, because "nothing unimplemented" and "nothing
        // examined" produce identical output.
        MapAssets assets = Load();

        int propMaterials = assets.Materials.Count - assets.BrushMaterialCount;

        propMaterials.ShouldBeGreaterThan(
            0,
            "the census examined no prop materials, which is what B81 was: a report that reads " +
            "clean because it never looked");

        TestContext.Out.WriteLine(
            $"{assets.Materials.Count} materials examined, {propMaterials} of them from props");
    }

    [Test]
    public void NoPropsBakedLightingWasRefused()
    {
        // **A refusal is this project failing on data the game uses.** The prop then draws with
        // white vertex colours and is indistinguishable from one the compiler never lit, which is
        // how four of them hid inside "without baked lighting" while B83 looked elsewhere.
        //
        // Zero is the target and the assertion. If this goes red, the names are the finding — go
        // and look at those props rather than relaxing the test.
        Load();

        IReadOnlyList<string> refused = PropModels.RejectedPropLighting;

        foreach (string rejection in refused)
        {
            TestContext.Out.WriteLine(rejection);
        }

        refused.ShouldBeEmpty(
            $"{refused.Count} placements shipped baked lighting this project would not apply, so " +
            "they draw as though the compiler never lit them: " + string.Join("; ", refused));
    }

    [Test]
    public void EveryUnimplementedParameterTheMapAsksForIsAlreadyKnown()
    {
        // **A gap stated is a decision; a gap unstated is an oversight.** The point is not that
        // this project implements everything — it plainly does not — but that the list of what it
        // skips is deliberate and reviewed. A parameter appearing here that is not on the list
        // means a real map wants something nobody has considered, which is precisely the state
        // B55 was in for an hour.
        MapAssets assets = Load();

        foreach ((string parameter, int materials) in assets.UnimplementedParameters)
        {
            TestContext.Out.WriteLine($"{parameter}: {materials} materials");
        }

        string[] surprising =
        [
            .. assets.UnimplementedParameters.Keys
                .Where(parameter => !Known.Contains(parameter))
                .OrderBy(parameter => parameter, StringComparer.OrdinalIgnoreCase),
        ];

        surprising.ShouldBeEmpty(
            "a real map asks for these and nothing in docs/RISKS.md accounts for them: " +
            string.Join(", ", surprising));
    }

    /// <summary>Parameters this project knowingly does not implement, each with its entry.</summary>
    /// <remarks>
    /// Kept as a list rather than a count so that adding one is a deliberate act with a place to
    /// write down why. The B numbers are the reason each is acceptable for now.
    /// </remarks>
    private static readonly HashSet<string> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        // **Reflection — B55.** The largest single gap and the one B83 turned on: 79 materials on
        // this map once props are counted, nearly double the 42 measured before B81 fixed the
        // census's blindness to them.
        "$envmap", "$envmaptint", "$envmapcontrast", "$envmapsaturation", "$envmapframe",
        "$basealphaenvmapmask", "$basemapalphaenvmapmask", "$normalmapalphaenvmapmask",

        // **Model specular and rim light — B60.** Draws models flatter than the game does.
        "$phong", "$phongexponent", "$phongboost", "$phongfresnelranges", "$phongtint",
        "$basemapalphaphongmask", "$rimlight", "$rimlightboost", "$rimlightexponent",

        // **Per-vertex tint — 66 materials here.** Brushwork tinted by baked vertex colour draws
        // untinted, which is a flat-looking surface rather than an obviously wrong one.
        "$vertexcolor", "$vertexalpha",

        // **Signage and distance-field text.** TF2's signs are drawn with an outline shader whose
        // parameters this project ignores entirely; the sign draws as its base texture.
        "$outline", "$outlinealpha", "$outlinecolor",
        "$outlineend0", "$outlineend1", "$outlinestart0", "$outlinestart1",
        "$distancealpha", "$distanceclamped", "$distanceinverted",
        "$edgesoftnessstart", "$edgesoftnessend", "$softedges", "$endalpha",

        // **Decals and overlays.** $decalscale sizes a decal this project places at texture scale.
        "$decalscale", "$nodecal",

        // **Blend and transform parameters** that modify how the base textures combine.
        "$blendmodulatetexture", "$bumpmap2", "$basetexturetransform", "$seamless_scale",
        "$seamless_detail",

        // **Colour and alpha modulation**, applied per material by the engine and not here.
        "$color", "$color2", "$alpha", "$one", "$reflectivity",
        "$AllowAlphaToCoverage", "$AlphaTestReference",

        // **Team and distance conditionals**, which select content rather than shade it.
        "$teammatch", "$matchinverted", "$playerdistance", "$fadedistance",

        // **Declarations rather than effects**: $model says the material is for a model, $nofog
        // exempts it from fog this project does not draw either.
        "$model", "$nofog",
    };

    /// <summary>Loads the map, skipping when the game is not installed.</summary>
    private static MapAssets Load()
    {
        if (GameFolder is not { } game)
        {
            // Assert.Ignore throws, so nothing after this runs; the throw satisfies the compiler
            // about the null case without a forgiving operator.
            throw new IgnoreException("Team Fortress 2 is not installed; set TF2_FOLDER to run this.");
        }

        string map = Path.Combine(game, "maps", MapName);

        if (!File.Exists(map))
        {
            Assert.Ignore($"{MapName} is not installed.");
        }

        return MapAssets.Load(
            File.ReadAllBytes(map), GameArchives.Open(game), maximumTextureSize: 512);
    }
}
