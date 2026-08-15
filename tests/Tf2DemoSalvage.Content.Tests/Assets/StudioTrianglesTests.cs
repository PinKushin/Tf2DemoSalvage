using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// Reading a model's triangles out of a shipped <c>.dx90.vtx</c>.
/// </summary>
/// <remarks>
/// **This is the file where a wrong reading looks most like a right one.** Indices are small
/// integers; read them at the wrong offset, or skip the strip group's own vertex indirection, and
/// every one of them still points at a real vertex of the same model. The result is a recognisable
/// shape with its surfaces shuffled — no exception, no impossible number.
///
/// So the assertions are about the RELATIONSHIPS all three files must satisfy together: every index
/// inside the mesh the <c>.mdl</c> declared, a whole number of triangles, and — the sharp one — the
/// triangles having plausible area when their vertices are fetched from the <c>.vvd</c>. A shuffled
/// index buffer produces slivers and enormous triangles spanning the model, which is a measurable
/// difference rather than an aesthetic one.
/// </remarks>
public sealed class StudioTrianglesTests
{
    /// <summary>Where the game is, when it is installed on this machine.</summary>
    private static string? GameFolder
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
                if (!string.IsNullOrWhiteSpace(root) &&
                    File.Exists(Path.Combine(root, "tf2_misc_dir.vpk")))
                {
                    return root;
                }
            }

            return null;
        }
    }

    private VpkArchive _models = null!;
    private IReadOnlyList<string> _paths = [];

    [SetUp]
    public void RequireTheGame()
    {
        if (GameFolder is not { } folder)
        {
            Assert.Ignore("Team Fortress 2 is not installed; set TF2_FOLDER to run these.");
            return;
        }

        _models = VpkArchive.Open(Path.Combine(folder, "tf2_misc_dir.vpk"));

        // Only models that have all three files, since the point is their agreement.
        _paths =
        [
            .. _models.Paths
                .Where(path => path.EndsWith(".DX90.VTX", StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => path, StringComparer.Ordinal)
                .Take(200)
        ];

        _paths.Count.ShouldBeGreaterThan(50, "the archive should hold plenty of models");
    }

    [Test]
    public void Read_EveryModel_ProducesWholeTriangles()
    {
        int withTriangles = 0;

        foreach ((StudioModelInfo model, byte[] indexFile, _) in Complete())
        {
            IReadOnlyList<IReadOnlyList<StudioCorner>> meshes = StudioTriangles.Read(indexFile, model);

            // One entry per STRIP GROUP now, and a mesh may have more than one, so this is a
            // floor rather than an equality - see StudioCorner for why the grouping changed.
            meshes.Count.ShouldBeGreaterThanOrEqualTo(model.Meshes.Count, model.Name);

            foreach (IReadOnlyList<StudioCorner> mesh in meshes)
            {
                (mesh.Count % 3).ShouldBe(0, model.Name);
            }

            if (meshes.Any(mesh => mesh.Count > 0))
            {
                withTriangles++;
            }
        }

        withTriangles.ShouldBeGreaterThan(20, "shipped models should produce triangles");
    }

    [Test]
    public void Read_EveryIndex_LandsInsideTheMeshTheModelDeclared()
    {
        // **The cross-file relationship.** A mesh owns a run of the vertex file's vertices, and
        // the index file names which of them form triangles. An index outside that run means the
        // strip group's vertex indirection was skipped or misread, and because it would still be a
        // real vertex of the same model, nothing but this check reports it.
        foreach ((StudioModelInfo model, byte[] indexFile, _) in Complete())
        {
            IReadOnlyList<IReadOnlyList<StudioCorner>> meshes = StudioTriangles.Read(indexFile, model);

            for (int index = 0; index < meshes.Count; index++)
            {
                StudioMesh mesh = model.Meshes[index];

                foreach (StudioCorner corner in meshes[index])
                {
                    corner.Vertex.ShouldBeInRange(0, Math.Max(0, mesh.VertexCount - 1), model.Name);
                }
            }
        }
    }

    [Test]
    public void Read_TheTriangles_AreLocalToTheirOwnMesh()
    {
        // **The measurement a shuffled index buffer cannot survive.** Fetch each triangle's three
        // vertices and measure its longest edge against the mesh's OWN bounding diagonal. A
        // correctly ordered mesh joins neighbouring vertices, so its edges are a small fraction of
        // the whole; indices read at the wrong offset, or without the strip group's vertex
        // indirection, connect vertices from opposite ends, so the longest edge approaches the
        // diagonal itself.
        //
        // **Relative, not absolute.** An absolute bound of 512 units failed on
        // bots/boss_bot/carrier.mdl at 1,388 - the giant robot carrier, which is genuinely that
        // large. That was the bound being wrong about model scale, not the reader being wrong
        // about indices, and an absolute limit tuned to pass it would have been too loose to catch
        // a shuffle in anything smaller.
        //
        // **Mean, not maximum.** The longest edge is not the discriminator either: an ambulance's
        // side panel is one quad spanning 38% of the model, so a max-edge bound tight enough to
        // catch a shuffle rejects real geometry. A shuffle moves every edge, so it moves the mean,
        // while a few legitimately large faces do not.
        int measured = 0;

        foreach ((StudioModelInfo model, byte[] indexFile, byte[] vertexFile) in Complete())
        {
            IReadOnlyList<StudioVertex> vertices = StudioVertices.Read(vertexFile);
            IReadOnlyList<IReadOnlyList<StudioCorner>> meshes = StudioTriangles.Read(indexFile, model);

            for (int index = 0; index < meshes.Count; index++)
            {
                StudioMesh mesh = model.Meshes[index];
                IReadOnlyList<StudioCorner> triangles = meshes[index];

                if (triangles.Count < 300 || mesh.FirstVertex + mesh.VertexCount > vertices.Count)
                {
                    // Small meshes are excluded deliberately: a mesh of a few triangles can
                    // legitimately span its own extent, so correct and shuffled predict the same
                    // observation and it has nothing to say.
                    continue;
                }

                double total = 0;
                int edges = 0;

                for (int corner = 0; corner + 2 < triangles.Count; corner += 3)
                {
                    StudioVertex a = vertices[mesh.FirstVertex + triangles[corner].Vertex];
                    StudioVertex b = vertices[mesh.FirstVertex + triangles[corner + 1].Vertex];
                    StudioVertex c = vertices[mesh.FirstVertex + triangles[corner + 2].Vertex];

                    total += Edge(a, b) + Edge(b, c) + Edge(a, c);
                    edges += 3;
                }

                double mean = total / edges;
                double diagonal = Diagonal(vertices, mesh);

                mean.ShouldBeLessThan(
                    diagonal / 4d,
                    $"{model.Name} mesh {index}: mean edge {mean:F1} of a {diagonal:F1} span");

                measured++;
            }
        }

        measured.ShouldBeGreaterThan(20);
    }

    /// <summary>How far across a mesh is, corner to corner.</summary>
    private static double Diagonal(IReadOnlyList<StudioVertex> vertices, StudioMesh mesh)
    {
        float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;

        for (int index = 0; index < mesh.VertexCount; index++)
        {
            StudioVertex vertex = vertices[mesh.FirstVertex + index];

            minX = Math.Min(minX, vertex.X);
            minY = Math.Min(minY, vertex.Y);
            minZ = Math.Min(minZ, vertex.Z);
            maxX = Math.Max(maxX, vertex.X);
            maxY = Math.Max(maxY, vertex.Y);
            maxZ = Math.Max(maxZ, vertex.Z);
        }

        double dx = maxX - minX;
        double dy = maxY - minY;
        double dz = maxZ - minZ;

        return Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
    }

    [Test]
    public void Read_AnIndexFileForADifferentModel_IsRefused()
    {
        // The engine's own check: one checksum stamped into all three files. A mismatched set must
        // be refused rather than drawn as nonsense.
        (StudioModelInfo model, byte[] indexFile, _) = Complete().First();

        StudioModelInfo impostor = model with { Checksum = model.Checksum + 1 };

        Should.Throw<InvalidDataException>(() => StudioTriangles.Read(indexFile, impostor));
    }

    [Test]
    public void Read_SomethingThatIsNotAnIndexFile_Fails()
    {
        StudioModelInfo model = new("x", 0, [], [], [], []);

        Should.Throw<InvalidDataException>(() => StudioTriangles.Read(new byte[64], model));
    }

    private static double Edge(StudioVertex from, StudioVertex to)
    {
        double dx = from.X - to.X;
        double dy = from.Y - to.Y;
        double dz = from.Z - to.Z;

        return Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
    }

    /// <summary>Every model in the selection that has all three of its files.</summary>
    private IEnumerable<(StudioModelInfo Model, byte[] Indices, byte[] Vertices)> Complete()
    {
        foreach (string path in _paths)
        {
            string stem = path[..^".DX90.VTX".Length];

            if (_models.ReadFile(stem + ".MDL") is not { } modelFile ||
                _models.ReadFile(stem + ".VVD") is not { } vertexFile ||
                _models.ReadFile(path) is not { } indexFile)
            {
                continue;
            }

            StudioModelInfo model;

            try
            {
                model = StudioModel.Read(modelFile);
            }
            catch (InvalidDataException)
            {
                continue;
            }

            if (model.Meshes.Count == 0)
            {
                continue;
            }

            yield return (model, indexFile, vertexFile);
        }
    }
}
