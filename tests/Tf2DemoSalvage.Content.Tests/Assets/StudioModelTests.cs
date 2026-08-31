using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// Reading a model's structure out of a shipped <c>.mdl</c>.
/// </summary>
/// <remarks>
/// **The cross-file agreement is the measurement worth having.** A <c>.mdl</c> and its <c>.vvd</c>
/// are separate files written by the same compiler, so the meshes' vertex ranges must land inside
/// the vertex array, and the two files carry the same checksum. Both are facts about the CONTENT
/// that this project's reading cannot fabricate: a wrong offset anywhere in the nesting produces
/// ranges that overrun, or a checksum that does not match.
///
/// That is worth more than any fixture could be, because the fixture would be built from the same
/// belief about the layout that the reader implements.
/// </remarks>
public sealed class StudioModelTests
{
    /// <summary>Where the game is, when it is installed on this machine.</summary>
    private static string? GameFolder => GameInstall.Root;

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

        // **Not props alone, deliberately.** Props are what a map places and they are the simplest
        // models in the archive - which is exactly what makes them a weak fixture here. Nearly
        // every prop is a single model whose vertexindex is ZERO, so a reader that confuses that
        // byte offset with a vertex count divides zero by the wrong number and gets zero. The
        // sabotage was run and passed against a props-only selection.
        //
        // Characters and weapons carry several models in one file, so their vertexindex is
        // non-zero and the units matter. Both are included, and one test asserts the fixture
        // actually contains the distinguishing case rather than assuming it.
        _paths =
        [
            .. _models.Paths
                .Where(path => path.EndsWith(".MDL", StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => path, StringComparer.Ordinal)
                .Take(400)
        ];

        _paths.Count.ShouldBeGreaterThan(50, "the archive should hold plenty of models");
    }

    [Test]
    public void Read_AModelWithGeometry_HasMaterialsForIt()
    {
        // **Not every .mdl has geometry.** An animation-only model - bot_demo_animations.mdl is
        // one - carries sequences and no body parts at all, so zero meshes and zero materials is
        // a real state rather than a failed parse. That is the engine's own arrangement for
        // sharing animations between models, and demanding meshes of every file reports it as a
        // defect.
        //
        // What must hold is the pairing: geometry needs something to paint it.
        int withGeometry = 0;

        foreach (string path in _paths)
        {
            StudioModelInfo model = StudioModel.Read(_models.ReadFile(path)!);

            if (model.Meshes.Count == 0)
            {
                continue;
            }

            model.Materials.ShouldNotBeEmpty(path);
            withGeometry++;
        }

        withGeometry.ShouldBeGreaterThan(
            _paths.Count / 2, "most models in the archive should carry geometry");
    }

    [Test]
    public void Read_EveryMaterialName_IsAName()
    {
        // A texture entry is usually a bare name with no directory and no extension - but not
        // always, see MaterialPaths. Either way it must be a NAME: non-empty, no extension, and
        // nothing that climbs out of the materials folder. Read at the wrong offset it is a
        // fragment of another string or binary, so this is a real constraint.
        foreach (string path in _paths)
        {
            foreach (string material in StudioModel.Read(_models.ReadFile(path)!).Materials)
            {
                // Empty is allowed and appears in shipped files: a texture slot a model declares
                // and never paints with. So is "..", which bot_medic uses to reach
                // ..\..\effects\invulnfx_red - a real name relative to the model's own folder.
                // What must never appear is an extension; the VMT is added on resolution.
                material.EndsWith(".vmt", StringComparison.OrdinalIgnoreCase).ShouldBeFalse(path);
            }
        }
    }

    [Test]
    public void MaterialPaths_ResolveForEveryMeshOfEveryProp()
    {
        // **The measurement that made the two rules below visible.** A material that resolves
        // nowhere is a prop that draws untextured, and the earlier version of this reader lost 14
        // models to exactly that by discarding the empty folder entry.
        int unresolved = 0;
        int total = 0;

        foreach (string path in _paths)
        {
            StudioModelInfo model = StudioModel.Read(_models.ReadFile(path)!);

            for (int index = 0; index < model.Materials.Count; index++)
            {
                total++;

                bool found = model
                    .MaterialPaths(index)
                    .Any(candidate => _models.TryFind("MATERIALS/" + candidate.ToUpperInvariant() + ".VMT", out _));

                if (!found)
                {
                    unresolved++;
                }
            }
        }

        // Not all of them: a few props reference materials that live in another archive or were
        // removed from the game, and demanding perfection here would be asserting about Valve's
        // content rather than about this reader.
        unresolved.ShouldBeLessThan(total / 10, $"{unresolved} of {total} materials resolved nowhere");
    }

    [Test]
    public void MaterialPaths_RefuseToClimbOutOfTheMaterialsFolder()
    {
        // A model arrives inside a downloaded map, so this is untrusted input (D32). A folder that
        // walks up the tree must not be handed to a caller that is about to open it.
        StudioModelInfo hostile = new(
            "hostile",
            0,
            ["passwd"],
            ["../../../etc/", "models/props/", "models/props/deep/"],
            [],
            [],
            [],
            0);

        List<string> candidates = [.. hostile.MaterialPaths(0)];

        // The climb out is refused outright rather than sanitised into something else.
        candidates.ShouldNotContain("etc/passwd");
        candidates.ShouldAllBe(candidate => !candidate.Contains("..", StringComparison.Ordinal));

        // And the ordinary folders still resolve, so the refusal is targeted rather than blanket.
        candidates.ShouldContain("models/props/passwd");
    }

    [Test]
    public void MaterialPaths_ResolveAClimbThatStaysInside()
    {
        // **The case Valve's own content needs.** bot_medic names ..\..\effects\invulnfx_red,
        // which is relative to the model's material folder and lands somewhere still inside the
        // materials tree. A reader that refuses every ".." loses it.
        //
        // The expected value is arithmetic, not a guess: three segments, two of them popped, so
        // models/player/medic/ + ../../effects/invulnfx_red is models/effects/invulnfx_red.
        StudioModelInfo medic = new(
            "medic", 0, ["../../effects/invulnfx_red"], ["models/player/medic/"], [], [], [], 0);

        medic.MaterialPaths(0).ShouldContain("models/effects/invulnfx_red");
    }

    [Test]
    public void Read_EveryMeshMaterial_NamesOneTheModelCarries()
    {
        // The index is into this model's own texture table, so anything outside it says the
        // nesting was walked wrongly - and a mesh reading a neighbouring struct's bytes still
        // yields a small plausible number most of the time, which is why this is checked over two
        // hundred models rather than one.
        foreach (string path in _paths)
        {
            StudioModelInfo model = StudioModel.Read(_models.ReadFile(path)!);

            foreach (StudioMesh mesh in model.Meshes)
            {
                mesh.MaterialIndex.ShouldBeInRange(0, model.Materials.Count - 1, path);
            }
        }
    }

    [Test]
    public void Read_TheMeshes_LandInsideTheModelsOwnVertexFile()
    {
        // **The measurement that matters, and it spans two files.** The .mdl says which vertices a
        // mesh uses and the .vvd holds them; they were written by the same compiler, so every
        // range must fit. This is what catches the trap that vertexindex is a BYTE offset while
        // vertexoffset is a VERTEX count - using them in the same units scales an index by 48 and
        // runs every mesh off the end.
        int checkedModels = 0;

        foreach (string path in _paths)
        {
            string vertexPath = Path.ChangeExtension(path, ".vvd");

            if (_models.ReadFile(vertexPath) is not { } vertexFile)
            {
                continue;
            }

            StudioModelInfo model = StudioModel.Read(_models.ReadFile(path)!);
            int available = StudioVertices.Read(vertexFile).Count;

            foreach (StudioMesh mesh in model.Meshes)
            {
                mesh.FirstVertex.ShouldBeGreaterThanOrEqualTo(0, path);
                (mesh.FirstVertex + mesh.VertexCount).ShouldBeLessThanOrEqualTo(available, path);
            }

            checkedModels++;
        }

        checkedModels.ShouldBeGreaterThan(20, "the models should have vertex files beside them");
    }

    [Test]
    public void Read_TheChecksum_MatchesTheVertexFileBesideIt()
    {
        // **A value recorded twice by unrelated routes.** The compiler stamps the same number into
        // both files so the engine can refuse a mismatched pair. Reading it out of each and
        // comparing tests this project's reading of two layouts against Valve's own writer, not
        // against itself.
        int compared = 0;

        foreach (string path in _paths)
        {
            if (_models.ReadFile(Path.ChangeExtension(path, ".vvd")) is not { } vertexFile)
            {
                continue;
            }

            StudioModel.Read(_models.ReadFile(path)!).Checksum
                .ShouldBe(BitConverter.ToInt32(vertexFile, 8), path);

            compared++;
        }

        compared.ShouldBeGreaterThan(20);
    }

    [Test]
    public void Read_TheMaterialFolders_IncludeTheEmptyOneWhereValveWroteIt()
    {
        // **The empty folder is data, not corruption.** Fourteen of these props put a full relative
        // path in the texture NAME instead of the folder, and those models list "" alongside their
        // real folders - the compiler saying the name is already the path. A reader that filters
        // empty entries loses exactly those models, silently, since the rest still resolve.
        //
        // Separators are mixed within one model too: the same file lists models\props_2fort\ and
        // models\props_2fort/.
        bool sawEmpty = false;

        foreach (string path in _paths)
        {
            IReadOnlyList<string> folders =
                StudioModel.Read(_models.ReadFile(path)!).MaterialFolders;

            folders.ShouldNotBeEmpty(path);

            sawEmpty |= folders.Any(string.IsNullOrEmpty);
        }

        sawEmpty.ShouldBeTrue("some shipped prop should carry the empty folder");
    }

    [Test]
    public void TheFixture_ContainsAModelWhoseVerticesDoNotStartAtZero()
    {
        // **A guard on the fixture, not on the code.** The range check above can only tell a byte
        // offset from a vertex count when the offset is not zero, and nearly every prop's is. This
        // asserts the distinguishing case is present, so that check keeps its teeth if the
        // selection is ever narrowed.
        bool sawOffsetModel = false;

        foreach (string path in _paths)
        {
            if (StudioModel.Read(_models.ReadFile(path)!).Meshes
                .Any(mesh => mesh.FirstVertex > 0))
            {
                sawOffsetModel = true;
                break;
            }
        }

        sawOffsetModel.ShouldBeTrue(
            "some model must carry more than one model, or the units cannot be distinguished");
    }

    [Test]
    public void Read_SomethingThatIsNotAModel_Fails()
    {
        Should.Throw<InvalidDataException>(() => StudioModel.Read(new byte[512]));
    }
}
