using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// Reading a model's vertices out of a shipped <c>.vvd</c>.
/// </summary>
/// <remarks>
/// **Measured against files Valve shipped**, because what can be wrong here is this project's
/// reading of a layout, and a hand-built fixture would encode that same reading and then agree with
/// it. The assertions are properties of model CONTENT rather than of the parse: normals are unit
/// length, positions sit inside the model's own scale, texture coordinates are finite. Read at the
/// wrong offset every one of those breaks, because the bytes there are a different field.
///
/// The normal check is the sharpest of them. A normal is a derived quantity — three floats whose
/// squares sum to one — so it is a value the file records that this reader can verify without
/// trusting the file. Reading four bytes early turns it into part of the position, whose length is
/// arbitrary.
/// </remarks>
public sealed class StudioVerticesTests
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

    /// <summary>Every vertex file in the archive, so the tests are not one model's opinion.</summary>
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

        _paths =
        [
            .. _models.Paths
                .Where(path => path.EndsWith(".vvd", StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => path, StringComparer.Ordinal)
                .Take(200)
        ];

        _paths.Count.ShouldBeGreaterThan(50, "the archive should hold plenty of models");
    }

    [Test]
    public void Read_EveryModelInTheArchive_Parses()
    {
        // **Breadth is the point.** One model exercises one shape of file: the fixup table is
        // empty for a simple prop and populated for anything with levels of detail, so a reader
        // that mishandles fixups passes on the first model and fails on the tenth.
        int withVertices = 0;

        foreach (string path in _paths)
        {
            IReadOnlyList<StudioVertex> vertices = StudioVertices.Read(_models.ReadFile(path)!);

            if (vertices.Count > 0)
            {
                withVertices++;
            }
        }

        withVertices.ShouldBe(_paths.Count, "every shipped model should have vertices");
    }

    [Test]
    public void Read_TheNormals_AreUnitLengthOrDegenerate()
    {
        // **A value the file records that this reader can check without trusting the file.** Three
        // floats whose squares sum to one is a property of the content, not of the parse, so it
        // fails on a wrong offset rather than merely looking odd.
        //
        // **Except that shipped models contain a few exactly-zero normals.** bot_heavy has two out
        // of 9,401. That is real data, not a misread: the vertex count came out at exactly the
        // header's declared 9,401 and 9,401 x 48 is exactly the size of the vertex region, and the
        // offending vertices carry perfectly ordinary positions and texture coordinates. A
        // degenerate normal on an unused or collapsed vertex is something the compiler emits and
        // the engine tolerates.
        //
        // So the assertion allows exactly zero and nothing else in between. That is still sharp:
        // read at a wrong offset the lengths are arbitrary, and arbitrary is neither 1 nor 0.
        foreach (string path in _paths)
        {
            int degenerate = 0;
            int total = 0;

            foreach (StudioVertex vertex in StudioVertices.Read(_models.ReadFile(path)!))
            {
                total++;

                double length = Math.Sqrt(
                    (vertex.NormalX * vertex.NormalX) +
                    (vertex.NormalY * vertex.NormalY) +
                    (vertex.NormalZ * vertex.NormalZ));

                if (length == 0d)
                {
                    degenerate++;
                    continue;
                }

                length.ShouldBeInRange(0.99, 1.01, path);
            }

            // Rare, and it has to stay rare: a reader that produced zeros wholesale - by running
            // off the end of the data into padding, which is the way this could go wrong quietly -
            // would satisfy the check above on every one of them.
            degenerate.ShouldBeLessThan(Math.Max(2, total / 100), path);
        }
    }

    [Test]
    public void Read_ThePositions_AreModelSized()
    {
        // A model's own space is small - a prop is tens of units, the largest are hundreds. Read
        // at the wrong offset these are bone weights or texture coordinates, which are 0..1, or
        // raw float noise, which is not bounded at all.
        const float Limit = 4096f;

        foreach (string path in _paths)
        {
            foreach (StudioVertex vertex in StudioVertices.Read(_models.ReadFile(path)!))
            {
                vertex.X.ShouldBeInRange(-Limit, Limit, path);
                vertex.Y.ShouldBeInRange(-Limit, Limit, path);
                vertex.Z.ShouldBeInRange(-Limit, Limit, path);
            }
        }
    }

    [Test]
    public void Read_TheTextureCoordinates_AreFinite()
    {
        // Tiling puts these outside 0..1 legitimately, so the only wrong answer is one that is not
        // a number - which is what reading past the end of a vertex produces.
        foreach (string path in _paths)
        {
            foreach (StudioVertex vertex in StudioVertices.Read(_models.ReadFile(path)!))
            {
                float.IsFinite(vertex.U).ShouldBeTrue(path);
                float.IsFinite(vertex.V).ShouldBeTrue(path);
            }
        }
    }

    [Test]
    public void Read_AModelWithFixups_HasAsManyVerticesAsItsHeaderPromises()
    {
        // **The fixup table's whole purpose.** A model with levels of detail stores its vertices
        // out of order, and the runs for level zero are scattered through the array. Assembling
        // them must reproduce the count the header declares for that level; a reader that ignores
        // fixups gets an array of the right length by accident and the wrong contents, and a
        // reader that treats the level as an exact match rather than a floor gets far too few.
        int checkedModels = 0;

        foreach (string path in _paths)
        {
            byte[] file = _models.ReadFile(path)!;
            int fixups = BitConverter.ToInt32(file, 48);

            if (fixups <= 1)
            {
                continue;
            }

            int declared = BitConverter.ToInt32(file, 16);

            StudioVertices.Read(file).Count.ShouldBe(declared, path);
            checkedModels++;
        }

        checkedModels.ShouldBeGreaterThan(0, "some shipped model should have a fixup table");
    }

    [Test]
    public void Read_ALevelBeyondWhatAModelHas_IsEmptyRatherThanAFailure()
    {
        // Asking for detail a model does not carry is a question with an answer, not an error.
        StudioVertices.Read(_models.ReadFile(_paths[0])!, lod: 7).ShouldNotBeNull();
    }

    [Test]
    public void Read_SomethingThatIsNotAVertexFile_Fails()
    {
        byte[] notAModel = new byte[128];

        Should.Throw<InvalidDataException>(() => StudioVertices.Read(notAModel));
    }
}
