using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Viewer3D.Tests;

/// <summary>
/// Which cubemap a surface reflects — already decided, by the map compiler.
/// </summary>
/// <remarks>
/// **The expensive-looking half of <c>$envmap</c> turns out not to exist.** The obvious design is a
/// nearest-by-position search at load: read the 43 placements, and for each surface find the
/// closest. vbsp did it at compile time instead.
///
/// <c>Cubemap_CreateTexInfo</c> (<c>vbsp/cubemap.cpp:600</c>) clones the texdata under a patched
/// material name carrying the cubemap's origin, writes a Patch VMT whose <c>$envmap</c> is that
/// cubemap's baked texture, and repoints the face's texinfo at the clone. So a face's material
/// **already names the exact cubemap it reflects**, and this project reads that name for every
/// surface as it is.
///
/// **This file is two independent recordings of one fact.** <c>LUMP_CUBEMAPS</c> says where the
/// cubemaps are; the patch VMTs in the map's pakfile say which texture each material reflects. They
/// were written by different parts of the compiler and must agree — which tests the decode against
/// the engine rather than against this project's reading of it.
/// </remarks>
public sealed class CubemapAssignmentTests
{
    private const string MapName = "cp_process_final";

    [Test]
    public void CubemapAssignment_EveryNamedEnvmap_IsPlacedByTheLump()
    {
        // **The agreement, and the reason to trust either.** Every $envmap value on this map should
        // be one of the 43 names derived from the lump's positions — and both sides were produced
        // by machinery this project did not write.
        //
        // A disagreement means one of three things and the test says which: a stride error moves
        // the positions, a naming error mangles the derivation, or a material reflects something
        // that is not a map cubemap at all (which is legal and is reported rather than failed).
        (MapAssets assets, PakFile pak, GameArchives archives, byte[] map) = LoadTheMap();

        HashSet<string> placed = new(
            BspCubemaps.Read(map).Select(cubemap => BspCubemaps.TextureName(MapName, cubemap)),
            StringComparer.OrdinalIgnoreCase);

        List<string> patched = [];
        List<string> foreign = [];

        foreach (string name in MaterialNames(assets).Where(IsPatchedIntoTheMap))
        {
            if (Material(pak, archives, name) is not { EnvMap: { } envmap })
            {
                continue;
            }

            patched.Add(name);

            if (!placed.Contains(envmap))
            {
                foreign.Add($"{name} -> {envmap}");
            }
        }

        TestContext.Out.WriteLine(
            $"{patched.Count} map-patched materials reflect; " +
            $"{patched.Count - foreign.Count} name one of the {placed.Count} placed cubemaps");

        if (foreign.Count > 0)
        {
            TestContext.Out.WriteLine($"naming something else: {string.Join(", ", foreign.Take(6))}");
        }

        // **The positive control first**, because with no patched materials the loop is vacuous and
        // everything below passes having compared nothing.
        patched.Count.ShouldBeGreaterThan(
            0, "this map's pakfile carries cubemap patch VMTs and they must be readable");

        // **All of them, not most.** These are exactly the materials vbsp rewrote to point at a
        // cubemap it had just baked, so every one must name a placement in the lump. A single
        // exception would mean the two halves of the compiler disagree, which they cannot.
        foreign.ShouldBeEmpty("a patched material names a cubemap the same compile run placed");
    }

    [Test]
    public void CubemapAssignment_BrushVersusPropMaterials_OnlyBrushArePatched()
    {
        // **The split, which is the finding this file exists for.** Cubemap_CreateTexInfo works on
        // TEXINFO — brush faces — and a static prop has none. So vbsp patches the material on a
        // pane of glass built out of brushes, and leaves `models/props_spytech/wall_clock_glass`
        // reading the literal `env_cubemap` for the engine to bind at runtime by proximity to the
        // prop's origin.
        //
        // This test began as "no resolved material still asks for env_cubemap", which is false and
        // failed on 26 materials — every one of them a `models/props_*`. The assertion was wrong,
        // not the data.
        //
        // Both halves are asserted because either alone is satisfiable by an accident: if nothing
        // were patched the first count is zero, and if the pakfile lookup were broken the second
        // would be too.
        (MapAssets assets, PakFile pak, GameArchives archives, _) = LoadTheMap();

        List<string> patchedNamingACubemap = [];
        List<string> propsAwaitingRuntimeBinding = [];

        foreach (string name in MaterialNames(assets))
        {
            switch (Material(pak, archives, name))
            {
                case { WantsMapCubemap: true }:
                    propsAwaitingRuntimeBinding.Add(name);
                    break;

                case { EnvMap: not null } when IsPatchedIntoTheMap(name):
                    patchedNamingACubemap.Add(name);
                    break;

                default:
                    break;
            }
        }

        TestContext.Out.WriteLine(
            $"{patchedNamingACubemap.Count} brush materials patched to a baked cubemap; " +
            $"{propsAwaitingRuntimeBinding.Count} still read env_cubemap");

        TestContext.Out.WriteLine(
            $"unpatched, first few: {string.Join(", ", propsAwaitingRuntimeBinding.Take(4))}");

        patchedNamingACubemap.Count.ShouldBeGreaterThan(
            0, "brush faces reflecting a cubemap are patched by vbsp at compile time");

        propsAwaitingRuntimeBinding.Count.ShouldBeGreaterThan(
            0, "a static prop has no texinfo, so its material is never patched");

        // **The discriminator between the two groups**, and what makes this a split rather than two
        // counts: nothing the map patched may still be asking for the literal, and nothing still
        // asking for the literal may be a map-patched name.
        propsAwaitingRuntimeBinding.ShouldAllBe(name => !IsPatchedIntoTheMap(name));
    }

    [Test]
    public void CubemapAssignment_APatchedMaterialName_CarriesItsCubemapPosition()
    {
        // **The strongest form of the agreement.** A patched material is named
        // `<material>_<x>_<y>_<z>` and its $envmap is `c<x>_<y>_<z>` — the SAME three numbers,
        // written by two calls to GeneratePatchedName with different separators
        // (vbsp/cubemap.cpp:511).
        //
        // So the name and the value cross-check each other with no reference to the lump at all.
        // A derivation that got the separator or the ordering wrong would satisfy the first test in
        // this file — every name would still be in the placed set — and fail here, because the
        // material it is attached to names a different position.
        (MapAssets assets, PakFile pak, GameArchives archives, _) = LoadTheMap();

        int checkedPairs = 0;

        foreach (string name in MaterialNames(assets).Where(IsPatchedIntoTheMap))
        {
            if (Material(pak, archives, name) is not { EnvMap: { } envmap })
            {
                continue;
            }

            // The trailing "_x_y_z" of the material name, if it has one.
            if (Position(name) is not { } fromName)
            {
                continue;
            }

            // The "cx_y_z" tail of the envmap value.
            if (Position(envmap) is not { } fromEnvmap)
            {
                continue;
            }

            fromEnvmap.ShouldBe(fromName, $"{name} -> {envmap}");
            checkedPairs++;
        }

        TestContext.Out.WriteLine($"{checkedPairs} material/cubemap pairs agreed on a position");

        checkedPairs.ShouldBeGreaterThan(0, "no patched material was found to cross-check");
    }

    /// <summary>The three trailing integers of a patched name, or null.</summary>
    /// <remarks>
    /// Both spellings end the same way — <c>…glasschrome001_1568_1728_976</c> for a material and
    /// <c>…/c1568_1728_976</c> for a texture — so the last three integers are the position in
    /// either, whatever precedes them.
    ///
    /// **Matched with a regex anchored at the end rather than by splitting on underscores**, and
    /// the first version did split: the map is called <c>cp_process_final</c>, so the path
    /// contributes its own underscores and the texture form's last three fields come out as
    /// <c>final/c1568</c>, <c>1728</c>, <c>976</c>. Zero pairs matched and it read as a naming
    /// failure. The separator is not a reliable boundary when the map's own name contains it.
    ///
    /// <c>-?</c> because roughly half a symmetric map is negative, and a hyphen is not an
    /// underscore, so the two never collide.
    /// </remarks>
    private static (int X, int Y, int Z)? Position(string name)
    {
        Match hit = Regex.Match(
            name, @"(-?\d+)_(-?\d+)_(-?\d+)$", RegexOptions.None, TimeSpan.FromSeconds(5));

        return hit.Success &&
            int.TryParse(hit.Groups[1].Value, out int x) &&
            int.TryParse(hit.Groups[2].Value, out int y) &&
            int.TryParse(hit.Groups[3].Value, out int z)
            ? (x, y, z)
            : null;
    }

    /// <summary>Every distinct material name the map's faces use.</summary>
    private static IEnumerable<string> MaterialNames(MapAssets assets) =>
        assets.Materials.Select(material => material.Name).Distinct(StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether vbsp generated this material into the map rather than it being stock.</summary>
    /// <remarks>
    /// <c>GeneratePatchedName</c> puts every generated material under <c>maps/&lt;mapname&gt;/</c>
    /// (<c>vbsp/cubemap.cpp:511</c>), so the prefix is the marker — and it is the compiler's own,
    /// not a convention inferred from looking at filenames.
    /// </remarks>
    private static bool IsPatchedIntoTheMap(string name) =>
        name.StartsWith($"maps/{MapName}/", StringComparison.OrdinalIgnoreCase);

    /// <summary>Parses a material by name, or null when it is not there.</summary>
    /// <remarks>
    /// **The map's own pakfile is searched first, and getting that wrong hides exactly the
    /// materials this file is about.** A cubemap patch VMT is written into the map at compile time
    /// and exists in no game archive — so a lookup that consults only the VPKs finds every stock
    /// material and none of the patched ones, which reads as "no material was ever patched".
    ///
    /// That is the second time in this feature that a search of the wrong container looked like a
    /// decode defect. See <c>docs/memory/instrument-bugs-outnumber-decoder-bugs.md</c>.
    /// </remarks>
    private static VmtMaterial? Material(PakFile pak, GameArchives archives, string name)
    {
        byte[]? bytes = pak.ReadFile($"materials/{name}.vmt");

        if (bytes is null && archives.Read($"materials/{name}.vmt") is { } stock)
        {
            bytes = stock.ToArray();
        }

        if (bytes is null)
        {
            return null;
        }

        try
        {
            return VmtMaterial.Parse(bytes);
        }
        catch (InvalidDataException)
        {
            // A material this project cannot parse is a defect worth its own test, not a reason for
            // this one to fail — it measures the cubemap agreement and nothing else.
            return null;
        }
    }

    /// <summary>The reference map, loaded through the ordinary path, or skips.</summary>
    private static (MapAssets Assets, PakFile Pak, GameArchives Archives, byte[] Map) LoadTheMap()
    {
        if (Tf2Install.Folder is not { } game)
        {
            Assert.Ignore("Team Fortress 2 is not installed; set TF2_FOLDER to run this.");

            throw new InvalidOperationException("unreachable; Assert.Ignore throws");
        }

        string path = Path.Combine(game, "maps", MapName + ".bsp");

        if (!File.Exists(path))
        {
            Assert.Ignore($"{MapName} is not installed.");
        }

        // Shared: this reads material NAMES and VMTs, so the texture size is irrelevant to it and
        // taking the default means reusing a load rather than paying for a fifth one.
        byte[] map = MapCache.Bytes();

        return (
            MapCache.Load(),
            PakFile.ReadFrom(map),
            GameArchives.Open(game),
            map);
    }
}
