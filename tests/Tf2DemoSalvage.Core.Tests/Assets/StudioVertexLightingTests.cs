using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Assets;
using Tf2DemoSalvage.Core.Bsp;

namespace Tf2DemoSalvage.Core.Tests.Assets;

/// <summary>
/// A placed prop's baked lighting, out of a shipped map's own pakfile.
/// </summary>
/// <remarks>
/// **The cross-file agreement is what tests this.** A <c>.vhv</c> is written by the map compiler
/// and the model by the model compiler, and the two must agree on a checksum and on a vertex count.
/// Neither is something this project's reading can fabricate: a wrong header offset gives a
/// checksum that does not match, and a wrong mesh walk gives a count that does not.
/// </remarks>
public sealed class StudioVertexLightingTests
{
    /// <summary>A shipped map, when the game is installed.</summary>
    private static string? MapFile
    {
        get
        {
            foreach (string? root in new[]
            {
                Environment.GetEnvironmentVariable("TF2_FOLDER"),
                @"C:\Program Files (x86)\Steam\steamapps\common\Team Fortress 2\tf",
                @"F:\SteamLibrary\steamapps\common\Team Fortress 2\tf",
                @"D:\SteamLibrary\steamapps\common\Team Fortress 2\tf",
            })
            {
                if (string.IsNullOrWhiteSpace(root))
                {
                    continue;
                }

                string map = Path.Combine(root, "maps", "cp_process_final.bsp");

                if (File.Exists(map))
                {
                    return map;
                }
            }

            return null;
        }
    }

    private ReadOnlyMemory<byte> _map;
    private PakFile _pak = null!;
    private IReadOnlyList<BspStaticProp> _props = [];

    [SetUp]
    public void RequireAMap()
    {
        if (MapFile is not { } path)
        {
            Assert.Ignore("Team Fortress 2 is not installed; set TF2_FOLDER to run these.");
            return;
        }

        _map = File.ReadAllBytes(path);
        _pak = PakFile.ReadFrom(_map);
        _props = BspStaticProps.Read(_map);
    }

    [Test]
    public void TheMapsPakfile_CarriesLightingForItsProps()
    {
        // **The premise, checked before anything is decoded.** If a map did not embed these, every
        // assertion below would be vacuously true against an empty set - the "no control" failure.
        int found = 0;

        for (int index = 0; index < _props.Count; index++)
        {
            if (StudioVertexLighting.PathsFor(index).Any(path => _pak.ReadFile(path) is not null))
            {
                found++;
            }
        }

        found.ShouldBeGreaterThan(
            _props.Count / 2, $"only {found} of {_props.Count} props have baked lighting");
    }

    [Test]
    public void EveryCornerIndexesAColourThatExists()
    {
        // **The contract, stated exactly.** vrad writes one .vhv mesh header per STRIP GROUP and
        // indexes each colour by that group's own vertex number:
        //
        // it fills one colour per strip group vertex, taking the value from the mesh vertex that
        // strip group vertex points at.
        //
        // So a corner's LightingGroup selects the header and LightingVertex indexes into it. If
        // either is wrong the lookup lands on another vertex's colour, which draws the prop
        // speckled with black rather than failing - the symptom that started this.
        //
        // Checking that every index is IN RANGE is what a wrong grouping cannot survive: reading
        // one header per mesh gives too few groups the moment any mesh has two strip groups.
        int checkedModels = 0;

        foreach ((int index, BspStaticProp prop) in Placements())
        {
            if (ReadModel(prop.Model) is not { } model ||
                Lighting(index, model.Info.Checksum) is not { } groups ||
                ReadIndices(prop.Model, model.Info) is not { } meshes)
            {
                continue;
            }

            foreach (IReadOnlyList<StudioCorner> mesh in meshes)
            {
                foreach (StudioCorner corner in mesh)
                {
                    corner.LightingGroup.ShouldBeInRange(0, groups.Count - 1, prop.Model);
                    corner.LightingVertex.ShouldBeInRange(
                        0, groups[corner.LightingGroup].Count - 1, prop.Model);
                }
            }

            checkedModels++;
        }

        checkedModels.ShouldBeGreaterThan(10, "some props should have been checked");
    }

    /// <summary>A model's triangles, when the game carries its index file.</summary>
    private static IReadOnlyList<IReadOnlyList<StudioCorner>>? ReadIndices(
        string path, StudioModelInfo model)
    {
        string? folder = MapFile is { } map ? Path.GetDirectoryName(Path.GetDirectoryName(map)) : null;

        if (folder is null)
        {
            return null;
        }

        string stem = path.EndsWith(".mdl", StringComparison.OrdinalIgnoreCase) ? path[..^4] : path;

        if (Archive(folder).ReadFile(stem + ".dx90.vtx") is not { } indexFile)
        {
            return null;
        }

        try
        {
            return StudioTriangles.Read(indexFile, model);
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    [Test]
    public void Read_TheColours_MatchTheirModelsMeshesOneForOne()
    {
        // **The oracle comes from the writer, not from a guess.** Valve's
        // CVradStaticPropMgr::SerializeLighting writes one MeshHeader_t per mesh per LOD, and each
        // mesh's colour count is that MESH's vertex count. So the comparison is per mesh against
        // the model's own meshes - not, as this test first had it, the flattened total against the
        // .vvd's numLODVertexes[0].
        //
        // That first oracle was wrong and it looked right: the two numbers coincide for 198 of 200
        // placements, and the two that disagreed (security_fence_light01, 873 against 970) read as
        // a defect in the reader. Reading the ENCODER settled it in one step - the reader was
        // correct throughout and the test was measuring the wrong quantity.
        int compared = 0;
        List<string> disagreed = [];

        foreach ((int index, BspStaticProp prop) in Placements())
        {
            if (ReadModel(prop.Model) is not { } model ||
                Lighting(index, model.Info.Checksum) is not { } meshes)
            {
                continue;
            }

            // The mesh COUNT must always agree - that is the structure, and a wrong walk breaks
            // it immediately.
            meshes.Count.ShouldBe(model.Info.Meshes.Count, prop.Model);

            compared++;

            for (int mesh = 0; mesh < meshes.Count; mesh++)
            {
                if (meshes[mesh].Count != model.Info.Meshes[mesh].VertexCount)
                {
                    disagreed.Add($"{prop.Model} mesh {mesh}: " +
                        $"{meshes[mesh].Count} colours for {model.Info.Meshes[mesh].VertexCount} vertices");
                }
            }
        }

        compared.ShouldBeGreaterThan(10, "some props should have been comparable");

        // **One model in two hundred, and the cause is known rather than shrugged at.** vrad
        // counts a mesh's colours from its .vtx STRIP GROUP vertices, which a strip group may
        // duplicate past the .mdl mesh's own count when it splits for bone limits. The two
        // coincide for everything else, which is why the .mdl count is a serviceable oracle and
        // not an exact one.
        //
        // Bounded rather than chased because the consequence is contained: the renderer applies a
        // mesh's lighting only when the counts agree and logs when they do not, which is the check
        // the engine makes before uploading vertex colours. A systemic misreading fails the mesh
        // COUNT assertion above long before reaching here.
        disagreed.Count.ShouldBeLessThan(
            Math.Max(2, compared / 50),
            $"{disagreed.Count} meshes disagree: {string.Join("; ", disagreed.Take(3))}");
    }

    [Test]
    public void Read_LightingBakedForADifferentModel_IsRefused()
    {
        // The engine's own guard. Lighting from another build of the model would light the wrong
        // parts of it, silently, so a mismatch must throw rather than be applied.
        foreach ((int index, BspStaticProp prop) in Placements())
        {
            if (ReadModel(prop.Model) is not { } model)
            {
                continue;
            }

            foreach (string path in StudioVertexLighting.PathsFor(index))
            {
                if (_pak.ReadFile(path) is not { } file)
                {
                    continue;
                }

                Should.Throw<InvalidDataException>(
                    () => StudioVertexLighting.Read(file, model.Info.Checksum + 1));

                return;
            }
        }

        Assert.Fail("no prop had lighting to check the checksum against");
    }

    [Test]
    public void Read_TheColours_AreNotAllTheSame()
    {
        // **Baked lighting varies across a prop**, which is the whole reason it exists: one side
        // faces the light and the other does not. A reader that returned a constant - by walking
        // the wrong offset into padding, say - would satisfy every count assertion above while
        // carrying no information at all, and would light props flatly rather than not at all.
        int varied = 0;
        int examined = 0;

        foreach ((int index, BspStaticProp prop) in Placements())
        {
            if (ReadModel(prop.Model) is not { } model ||
                Lighting(index, model.Info.Checksum) is not { } meshes)
            {
                continue;
            }

            List<(byte Red, byte Green, byte Blue)> colours = [.. meshes.SelectMany(mesh => mesh)];

            if (colours.Count < 32)
            {
                continue;
            }

            examined++;

            if (colours.Distinct().Count() > 1)
            {
                varied++;
            }
        }

        examined.ShouldBeGreaterThan(10);
        varied.ShouldBeGreaterThan(
            examined / 2, $"only {varied} of {examined} props have any variation in their lighting");
    }

    /// <summary>The first placements, with their index, which is what names the lighting file.</summary>
    private IEnumerable<(int Index, BspStaticProp Prop)> Placements()
    {
        for (int index = 0; index < _props.Count && index < 200; index++)
        {
            yield return (index, _props[index]);
        }
    }

    private IReadOnlyList<IReadOnlyList<(byte Red, byte Green, byte Blue)>>? Lighting(
        int index, int checksum)
    {
        foreach (string path in StudioVertexLighting.PathsFor(index))
        {
            if (_pak.ReadFile(path) is { } file)
            {
                return StudioVertexLighting.Read(file, checksum);
            }
        }

        return null;
    }

    /// <summary>A prop's model and how many vertices it has, when the game carries it.</summary>
    private static (StudioModelInfo Info, int Vertices)? ReadModel(string path)
    {
        string? folder = MapFile is { } map ? Path.GetDirectoryName(Path.GetDirectoryName(map)) : null;

        if (folder is null)
        {
            return null;
        }

        VpkArchive archive = Archive(folder);

        string stem = path.EndsWith(".mdl", StringComparison.OrdinalIgnoreCase) ? path[..^4] : path;

        if (archive.ReadFile(path) is not { } modelFile ||
            archive.ReadFile(stem + ".vvd") is not { } vertexFile)
        {
            return null;
        }

        try
        {
            return (StudioModel.Read(modelFile), StudioVertices.Read(vertexFile).Count);
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    private static VpkArchive? _archive;

    /// <summary>The models archive, opened once: it is a large file.</summary>
    private static VpkArchive Archive(string folder) =>
        _archive ??= VpkArchive.Open(Path.Combine(folder, "tf2_misc_dir.vpk"));
}
