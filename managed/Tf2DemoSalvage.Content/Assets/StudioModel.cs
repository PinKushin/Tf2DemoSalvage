using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Tf2DemoSalvage.Content.Assets;

/// <summary>One drawable run of a model's vertices, all sharing a material.</summary>
/// <param name="MaterialIndex">Which of the model's materials paints it.</param>
/// <param name="FirstVertex">Where its vertices begin in the model's vertex array.</param>
/// <param name="VertexCount">How many it has.</param>
public readonly record struct StudioMesh(int MaterialIndex, int FirstVertex, int VertexCount);

/// <summary>A model's structure, from its <c>.mdl</c>.</summary>
/// <param name="Name">The model's own name, as the compiler recorded it.</param>
/// <param name="Checksum">Must match the <c>.vvd</c> and <c>.vtx</c> that go with it.</param>
/// <param name="Materials">Material names, without a directory.</param>
/// <param name="MaterialFolders">Where to look for them, relative to <c>materials/</c>.</param>
/// <param name="Meshes">The runs, in the order the index data walks them.</param>
/// <param name="SelectedModels">
/// Which model each body part contributed, chosen by the entity's <c>m_nBody</c>.
/// </param>
/// <remarks>
/// **The bodygroup selection has to travel, because the <c>.vtx</c> mirrors this structure.** A
/// model's triangles are stored body part by body part and model by model in step with the
/// <c>.mdl</c>, so a reader that picks one model here and walks all of them there desynchronises
/// the two. The symptom is not a wrong picture but "an index file's strip groups do not fit either
/// known layout" — which reads as a corrupt file rather than as two walks disagreeing, and is
/// exactly what the tests reported when this was changed in one place only.
/// </remarks>
public sealed record StudioModelInfo(
    string Name,
    int Checksum,
    IReadOnlyList<string> Materials,
    IReadOnlyList<string> MaterialFolders,
    IReadOnlyList<StudioMesh> Meshes,
    IReadOnlyList<int> SelectedModels)
{
    /// <summary>Where a material might be, in the order worth trying.</summary>
    /// <param name="materialIndex">Which of <see cref="Materials"/>.</param>
    /// <returns>Paths under <c>materials/</c>, forward-slashed, without an extension.</returns>
    /// <exception cref="ArgumentOutOfRangeException">No such material.</exception>
    /// <remarks>
    /// **A name is not always a name.** Measured across 200 shipped props: most texture entries are
    /// bare, like <c>rock_02</c>, and are found by joining one of the model's folders to them. But
    /// fourteen of them carry a full relative path instead — <c>models/props_2fort/window005</c> —
    /// and those models list an EMPTY folder alongside their real ones. The empty string is not
    /// corruption; it is the compiler saying "the name is already the path", and a reader that
    /// discards empty folders loses exactly those models.
    ///
    /// **Separators are mixed within a single model.** The same file lists
    /// <c>models\props_2fort\</c> and <c>models\props_2fort/</c>. Both are normalised here, so
    /// nothing downstream has to care which the author's tools produced.
    ///
    /// **A path that climbs out is refused.** A model arrives inside a downloaded map (D32), so a
    /// folder of <c>../../../windows/system32/</c> is input, not a mistake. Candidates containing a
    /// <c>..</c> segment are dropped rather than returned for a caller to open.
    /// </remarks>
    public IEnumerable<string> MaterialPaths(int materialIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(materialIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(materialIndex, Materials.Count);

        string name = Normalise(Materials[materialIndex]);

        foreach (string folder in MaterialFolders)
        {
            if (Resolve(Normalise(folder) + name) is { } candidate)
            {
                yield return candidate;
            }
        }

        // The name alone, last. A model with no usable folder still names something, and for the
        // entries that carry a full path this is the candidate that actually resolves.
        if (Resolve(name) is { } bare)
        {
            yield return bare;
        }
    }

    /// <summary>Forward slashes, and no leading one.</summary>
    private static string Normalise(string path) =>
        path.Replace((char)92, '/').TrimStart('/');

    /// <summary>
    /// Resolves a candidate's <c>.</c> and <c>..</c> segments, or refuses it.
    /// </summary>
    /// <returns>The resolved path, or null if it climbs above the materials folder.</returns>
    /// <remarks>
    /// **`..` in a material name is legitimate, and Valve uses it.** bot_medic.mdl names
    /// <c>..\..\effects\invulnfx_red</c>, which is relative to the model's own material folder
    /// and resolves to <c>effects/invulnfx_red</c>. A reader that refuses any candidate containing
    /// <c>..</c> loses those materials, so the surface draws untextured.
    ///
    /// **And a model arrives inside a downloaded map (D32), so it is untrusted input.** Allowing
    /// <c>..</c> unresolved would let a hostile file name anything on disk.
    ///
    /// Both are satisfied by resolving rather than by matching: walk the segments, pop on
    /// <c>..</c>, and refuse only when the stack would go empty — which is the point at which the
    /// path has actually left the folder rather than merely mentioned leaving it.
    /// </remarks>
    private static string? Resolve(string candidate)
    {
        if (candidate.Length == 0 || candidate.Contains(':', StringComparison.Ordinal))
        {
            return null;
        }

        List<string> segments = [];

        foreach (string segment in candidate.Split('/'))
        {
            switch (segment)
            {
                case "" or ".":
                    break;

                case "..":
                    if (segments.Count == 0)
                    {
                        return null;
                    }

                    segments.RemoveAt(segments.Count - 1);
                    break;

                default:
                    segments.Add(segment);
                    break;
            }
        }

        return segments.Count == 0 ? null : string.Join('/', segments);
    }
}

/// <summary>
/// A model's structure: which vertices belong to which material.
/// </summary>
/// <remarks>
/// **Read from Valve's published <c>studio.h</c>** — offsets and their derivation are in
/// <c>docs/findings/11-models.md</c>. Nothing here came from a decompiler.
///
/// The nesting is body part, then model, then mesh, and only the last level matters for drawing:
/// a mesh names a material and a run of vertices. The two levels above it exist for bodygroups —
/// swapping a soldier's helmet for a hat — which a static prop never uses.
///
/// **Three offset conventions appear in one file and nothing marks which is which.**
///
/// - Every <c>...index</c> field is relative to **the struct that contains it**. This is the one
///   that bites, because <c>studiohdr_t</c> sits at offset zero, so at the top level the two
///   readings agree and a reader that assumes file-relative works perfectly until it reaches a mesh.
/// - <c>cdtextureindex</c> points at an array of ints that ARE file-relative, because the strings
///   they name are addressed from the header.
/// - <c>mstudiomodel_t.vertexindex</c> is a **byte** offset into the vertex data, while
///   <c>mstudiomesh_t.vertexoffset</c> is a **vertex** count relative to it. Mixing them up scales
///   an index by 48 and lands inside the model, on real vertices, drawing the wrong surface.
///
/// **A material name carries no directory and there is more than one candidate folder.** The
/// texture entry is a bare name like <c>rock_02</c>, and the model separately lists the folders to
/// search — usually one, sometimes several. Resolution is left to the caller, which is the layer
/// that knows about archives.
/// </remarks>
public static class StudioModel
{
    /// <summary>'IDST', the identifier at the front of a model file.</summary>
    private const int Identifier = 0x54534449;

    /// <summary>The oldest and newest versions this reader accepts.</summary>
    /// <remarks>
    /// TF2 ships 44 to 49. The bound is a sanity check rather than a compatibility claim: the
    /// fields read here have been in the same places for the whole range, and a file declaring
    /// version 3,000 is not a model.
    /// </remarks>
    private const int MinimumVersion = 44;
    private const int MaximumVersion = 49;

    private const int NameOffset = 12;
    private const int NameBytes = 64;

    private const int TextureCountOffset = 204;
    private const int TextureIndexOffset = 208;
    private const int FolderCountOffset = 212;
    private const int FolderIndexOffset = 216;
    private const int BodyPartCountOffset = 232;
    private const int BodyPartIndexOffset = 236;

    private const int TextureStride = 64;
    private const int BodyPartStride = 16;
    private const int ModelStride = 148;
    private const int MeshStride = 116;

    private const int BodyPartModelCountOffset = 4;

    /// <summary><c>mstudiobodyparts_t.base</c>: this part's place value within <c>m_nBody</c>.</summary>
    private const int BodyPartBaseOffset = 8;

    private const int BodyPartModelIndexOffset = 12;

    private const int ModelMeshCountOffset = 72;
    private const int ModelMeshIndexOffset = 76;
    private const int ModelVertexCountOffset = 80;
    private const int ModelVertexIndexOffset = 84;

    private const int MeshMaterialOffset = 0;
    private const int MeshVertexCountOffset = 8;
    private const int MeshVertexOffset = 12;

    /// <summary>Bytes per vertex in the <c>.vvd</c>, which <c>vertexindex</c> is measured in.</summary>
    private const int VertexBytes = 48;

    /// <summary>The most of anything this reader will build from one file.</summary>
    /// <remarks>A model from a downloaded map is untrusted input (D32).</remarks>
    private const int MaximumCount = 65_536;

    /// <summary>Reads a model's structure.</summary>
    /// <param name="file">The <c>.mdl</c>'s bytes.</param>
    /// <param name="body">
    /// The entity's <c>m_nBody</c>, which selects one model from each body part. Zero takes each
    /// part's first, which is what the engine shows for an entity that never sets it.
    /// </param>
    /// <returns>Its materials and the runs of vertices they paint.</returns>
    /// <exception cref="InvalidDataException">The file is not a readable model.</exception>
    public static StudioModelInfo Read(ReadOnlyMemory<byte> file, int body = 0)
    {
        ReadOnlySpan<byte> bytes = file.Span;

        if (bytes.Length < BodyPartIndexOffset + sizeof(int))
        {
            throw new InvalidDataException(string.Create(
                CultureInfo.InvariantCulture,
                $"A model of {bytes.Length:N0} bytes is too short to hold its header."));
        }

        if (BinaryPrimitives.ReadInt32LittleEndian(bytes) != Identifier)
        {
            throw new InvalidDataException("This is not a model: it does not begin 'IDST'.");
        }

        int version = BinaryPrimitives.ReadInt32LittleEndian(bytes[4..]);

        if (version is < MinimumVersion or > MaximumVersion)
        {
            throw new InvalidDataException(
                $"A model declares version {version}, outside the {MinimumVersion} to " +
                $"{MaximumVersion} this reader knows.");
        }

        (List<StudioMesh> meshes, List<int> chosen) = ReadMeshes(bytes, body);

        return new StudioModelInfo(
            ReadFixedString(bytes.Slice(NameOffset, NameBytes)),
            BinaryPrimitives.ReadInt32LittleEndian(bytes[8..]),
            ReadMaterials(bytes),
            ReadFolders(bytes),
            meshes,
            chosen);
    }

    private static List<string> ReadMaterials(ReadOnlySpan<byte> file)
    {
        int count = Count(file, TextureCountOffset, "textures");
        int at = Offset(file, TextureIndexOffset, count, TextureStride, "textures");

        List<string> materials = new(count);

        for (int index = 0; index < count; index++)
        {
            int entry = at + (index * TextureStride);

            // Relative to the texture entry, not to the file. The distinction is invisible for the
            // first entry of a file whose textures happen to start at zero, and wrong everywhere
            // else.
            materials.Add(ReadStringAt(
                file, entry + BinaryPrimitives.ReadInt32LittleEndian(file[entry..])));
        }

        return materials;
    }

    /// <summary>The folders to search for this model's materials.</summary>
    /// <remarks>
    /// **These offsets are file-relative, unlike everything else in the file**, because the engine
    /// addresses the strings from the header rather than from the array. Reading them as relative
    /// to the array lands a little way into the file, usually inside another string, and produces
    /// a folder name that is a plausible-looking fragment of a different path.
    /// </remarks>
    private static List<string> ReadFolders(ReadOnlySpan<byte> file)
    {
        int count = Count(file, FolderCountOffset, "material folders");
        int at = Offset(file, FolderIndexOffset, count, sizeof(int), "material folders");

        List<string> folders = new(count);

        for (int index = 0; index < count; index++)
        {
            folders.Add(ReadStringAt(
                file, BinaryPrimitives.ReadInt32LittleEndian(file[(at + (index * sizeof(int)))..])));
        }

        return folders;
    }

    /// <summary>Which of a body part's models the packed body number selects.</summary>
    /// <remarks>
    /// <c>GetBodygroup</c>, <c>shared/animation.cpp:876</c>. A part with a zero or negative base
    /// would divide by zero, so it takes its first model — a malformed part should cost itself and
    /// nothing else.
    /// </remarks>
    private static int Select(ReadOnlySpan<byte> file, int partAt, int models, int body)
    {
        int place = BinaryPrimitives.ReadInt32LittleEndian(file[(partAt + BodyPartBaseOffset)..]);

        return place <= 0 || models <= 0 ? 0 : (body / place) % models;
    }

    private static (List<StudioMesh> Meshes, List<int> Chosen) ReadMeshes(
        ReadOnlySpan<byte> file, int body)
    {
        List<int> chosen = [];

        int parts = Count(file, BodyPartCountOffset, "body parts");
        int partsAt = Offset(file, BodyPartIndexOffset, parts, BodyPartStride, "body parts");

        List<StudioMesh> meshes = [];

        for (int part = 0; part < parts; part++)
        {
            int partAt = partsAt + (part * BodyPartStride);

            int models = Count(file, partAt + BodyPartModelCountOffset, "models");
            int modelsAt = Relative(
                file, partAt, partAt + BodyPartModelIndexOffset, models, ModelStride, "models");

            // **A body part contributes ONE of its models, not all of them.** This is what a
            // bodygroup is: a part offers alternatives — a head with a hat and a head without, a
            // control point sign reading A, B or C — and the entity's m_nBody picks one per part.
            //
            // Reading them all draws every alternative at once. Measured on cp_process, that is
            // all three capture point labels stacked in the same place, which also makes the
            // hologram they sit in read as far more opaque than it should, because three
            // translucent signs are blended where one belongs.
            //
            // Valve's selection, from GetBodygroup in shared/animation.cpp:876, is the body number
            // divided by that part's base, modulo how many models the part has.
            //
            // A part's base is the place value it occupies in the packed number, so the parts
            // divide m_nBody between them like digits of a mixed-radix integer.
            int selected = Select(file, partAt, models, body);

            chosen.Add(selected);
            ReadModelMeshes(file, modelsAt + (selected * ModelStride), meshes);
        }

        return (meshes, chosen);
    }

    private static void ReadModelMeshes(ReadOnlySpan<byte> file, int modelAt, List<StudioMesh> into)
    {
        // **A byte offset, not a vertex index.** Dividing is what makes the mesh offsets below
        // line up with the .vvd; using it directly multiplies every index by 48 and still lands
        // inside the model, on real vertices, drawing a wrong surface rather than failing.
        int firstVertex =
            BinaryPrimitives.ReadInt32LittleEndian(file[(modelAt + ModelVertexIndexOffset)..]) /
            VertexBytes;

        int vertices = BinaryPrimitives.ReadInt32LittleEndian(
            file[(modelAt + ModelVertexCountOffset)..]);

        int meshes = Count(file, modelAt + ModelMeshCountOffset, "meshes");
        int meshesAt = Relative(
            file, modelAt, modelAt + ModelMeshIndexOffset, meshes, MeshStride, "meshes");

        for (int mesh = 0; mesh < meshes; mesh++)
        {
            int meshAt = meshesAt + (mesh * MeshStride);

            int material = BinaryPrimitives.ReadInt32LittleEndian(file[(meshAt + MeshMaterialOffset)..]);
            int offset = BinaryPrimitives.ReadInt32LittleEndian(file[(meshAt + MeshVertexOffset)..]);
            int count = BinaryPrimitives.ReadInt32LittleEndian(file[(meshAt + MeshVertexCountOffset)..]);

            if (offset < 0 || count < 0 || offset + count > vertices)
            {
                throw new InvalidDataException(string.Create(
                    CultureInfo.InvariantCulture,
                    $"A mesh claims vertices {offset:N0} to {offset + count:N0} of a model with " +
                    $"{vertices:N0}."));
            }

            into.Add(new StudioMesh(material, firstVertex + offset, count));
        }
    }

    private static int Count(ReadOnlySpan<byte> file, int at, string what)
    {
        if (at + sizeof(int) > file.Length)
        {
            throw new InvalidDataException($"A model ends before its {what} count.");
        }

        int count = BinaryPrimitives.ReadInt32LittleEndian(file[at..]);

        if (count is < 0 or > MaximumCount)
        {
            throw new InvalidDataException(string.Create(
                CultureInfo.InvariantCulture, $"A model declares {count:N0} {what}."));
        }

        return count;
    }

    /// <summary>An index field read as file-relative, checked to hold what it claims.</summary>
    private static int Offset(
        ReadOnlySpan<byte> file, int at, int count, int stride, string what) =>
        Relative(file, 0, at, count, stride, what);

    /// <summary>An index field read as relative to a base, checked to hold what it claims.</summary>
    private static int Relative(
        ReadOnlySpan<byte> file, int origin, int at, int count, int stride, string what)
    {
        if (at + sizeof(int) > file.Length)
        {
            throw new InvalidDataException($"A model ends before its {what} offset.");
        }

        long start = origin + BinaryPrimitives.ReadInt32LittleEndian(file[at..]);

        if (count == 0)
        {
            return 0;
        }

        if (start < 0 || start + ((long)count * stride) > file.Length)
        {
            throw new InvalidDataException(string.Create(
                CultureInfo.InvariantCulture,
                $"A model puts {count:N0} {what} at {start:N0} of {file.Length:N0} bytes."));
        }

        return (int)start;
    }

    /// <summary>A null-terminated string somewhere in the file.</summary>
    private static string ReadStringAt(ReadOnlySpan<byte> file, int at)
    {
        if (at < 0 || at >= file.Length)
        {
            throw new InvalidDataException(string.Create(
                CultureInfo.InvariantCulture,
                $"A model names a string at {at:N0} of {file.Length:N0} bytes."));
        }

        return ReadFixedString(file[at..]);
    }

    /// <summary>Reads up to a null terminator, as UTF-8.</summary>
    /// <remarks>
    /// UTF-8 rather than ASCII, as every string in this project is. A path is a path whatever the
    /// author's keyboard produced, and ASCII replaces what it cannot read with a question mark -
    /// which turns a name into a plausible wrong one rather than failing.
    /// </remarks>
    private static string ReadFixedString(ReadOnlySpan<byte> bytes)
    {
        int end = bytes.IndexOf((byte)0);

        return Encoding.UTF8.GetString(end < 0 ? bytes : bytes[..end]);
    }
}
