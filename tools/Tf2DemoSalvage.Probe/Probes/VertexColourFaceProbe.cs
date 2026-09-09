using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// Which of a map's faces carry a <c>$vertexcolor</c> material, and whether they draw (B329).
/// </summary>
/// <remarks>
/// **B329 asked whether `$vertexcolor` on a brush material is inert and could not settle it.** The
/// SDK route is genuinely exhausted: the engine's world mesh builder is not published, and
/// `CVertexBuilder` points at a vertex buffer nothing initialises, so what `v.vColor` holds for a
/// world face cannot be read out of `source-sdk-2013` at all.
///
/// **The owner's correction reframed it:** *"those materials are used somewhere, so they need to
/// resolve and maybe draw if they are not triggers or something, since drawing triggers can and does
/// happen sometimes, with certain bugs"*, and then *"they may be nodraw, so it might not even be a
/// matter of drawing them, triggers are not drawn, and they are how some occlusion and doors work"*.
///
/// So the question is not "is the flag inert" but three measurable ones, and this answers all three:
/// which faces carry such a material, whether those faces are tool surfaces that never draw, and
/// whether the material RESOLVES — because a material we fail to load is a defect whatever the flag
/// does, and one on a `nodraw` trigger still matters for occlusion and for doors.
///
/// <code>
///   vertexcolor-faces cp_process_f12
///   vertexcolor-faces koth_harvest_final
/// </code>
///
/// **Buckets rather than a single rate**, because the three populations need different answers: a
/// drawn face carrying the flag is where a colour could be visible, a tool surface is where it cannot
/// be, and an unresolved material is a gap regardless.
/// </remarks>
public sealed class VertexColourFaceProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "vertexcolor-faces";

    /// <inheritdoc/>
    public string Summary =>
        "map faces whose material declares $vertexcolor, by draw state: vertexcolor-faces <map>";

    /// <summary>The flag Valve's two-branch vertex shader is compiled against.</summary>
    private const string VertexColour = "$vertexcolor";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count < 1)
        {
            output.WriteLine("vertexcolor-faces <map>");
            return;
        }

        MapLocator locator = new(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder);

        if (locator.Find(arguments[0]) is not { } path)
        {
            output.WriteLine($"No map named '{arguments[0]}'.");
            return;
        }

        if (locator.FindGameFolder() is not { } folder)
        {
            output.WriteLine("No TF2 install, so no material can be read.");
            return;
        }

        GameArchives archives = GameArchives.Open(folder);
        ReadOnlyMemory<byte> file = File.ReadAllBytes(path);
        PakFile pak = PakFile.ReadFrom(file);

        // **`BspSurfaces`, not `BspGeometry`, because only a surface carries BOTH its material index
        // and its flags.** A `BspFace` has the outline and the flags and no material at all, so a
        // probe built on it cannot join a face to the VMT that decides this question.
        IReadOnlyList<string> materials =
            [.. BspMaterials.Read(file).Select(material => material.Name)];

        IReadOnlyList<BspSurface> faces = BspSurfaces.Read(file);

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{Path.GetFileName(path)}: {faces.Count:N0} surfaces, {materials.Count:N0} materials"));

        // **Read each material once**, and remember whether it declares the flag and whether it could
        // be read at all. An unreadable material is its own finding and must not be counted as one
        // without the flag — that would be an absence produced by the reader.
        Dictionary<string, (bool Declares, bool Read)> known = new(StringComparer.OrdinalIgnoreCase);

        foreach (string material in materials)
        {
            if (known.ContainsKey(material))
            {
                continue;
            }

            // **The MAP's own pakfile first, then the game's archives**, which is the order
            // `BspLumpIndex.PakFile` documents the project searching in. Reading only the archives
            // reported 507 faces as having an unreadable material on `cp_process_f12`, and every one
            // was a cubemap patch the compiler wrote INTO the map —
            // `maps/cp_process_f12/glass/glasswindow001a_-1096_-448_1040`. That was the probe's gap,
            // not the project's, and it is exactly the absence a control is for.
            byte[]? bytes =
                pak.ReadFile($"materials/{material}.vmt") ?? archives.Read($"materials/{material}.vmt");

            known[material] = bytes is { Length: > 0 }
                ? (Declares(VmtMaterial.Parse(bytes)), true)
                : (false, false);
        }

        // **Which materials could not be read, by name.** "507 faces whose material could not be
        // read" is a rate, and a rate cannot be acted on: a material this project fails to open is a
        // defect whatever the flag does, and only the name says whether it is a patch, a path that
        // needs normalising, or something genuinely absent.
        foreach (string missing in known
            .Where(pair => !pair.Value.Read)
            .Select(pair => pair.Key)
            .Order(StringComparer.OrdinalIgnoreCase)
            .Take(8))
        {
            output.WriteLine($"    UNREADABLE {missing}");
        }

        int drawn = 0;
        int tool = 0;
        int unreadable = 0;
        Dictionary<string, int> drawnBy = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, int> toolBy = new(StringComparer.OrdinalIgnoreCase);

        foreach (BspSurface face in faces)
        {
            if (face.MaterialIndex < 0 || face.MaterialIndex >= materials.Count)
            {
                continue;
            }

            string material = materials[face.MaterialIndex];

            if (!known.TryGetValue(material, out (bool Declares, bool Read) about))
            {
                continue;
            }

            if (!about.Read)
            {
                unreadable++;
                continue;
            }

            if (!about.Declares)
            {
                continue;
            }

            // **The tool-surface test is the map's own, taken from `BspGeometry`'s set rather than a
            // second copy** — sky, nodraw, hint, skip and trigger. A face in that set is invisible in
            // game, so a colour on it can never be seen; it still exists, and the owner's point is
            // that occlusion and doors are built out of exactly these.
            if (!face.IsVisible)
            {
                tool++;
                toolBy[material] = toolBy.TryGetValue(material, out int seen) ? seen + 1 : 1;
                continue;
            }

            drawn++;
            drawnBy[material] = drawnBy.TryGetValue(material, out int had) ? had + 1 : 1;
        }

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"  $vertexcolor faces: {drawn:N0} DRAWN, {tool:N0} on tool surfaces; " +
            $"{unreadable:N0} faces whose material could not be read"));

        foreach ((string material, int count) in drawnBy.OrderByDescending(pair => pair.Value).Take(10))
        {
            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture, $"    DRAWN {count,6}  {material}"));
        }

        foreach ((string material, int count) in toolBy.OrderByDescending(pair => pair.Value).Take(6))
        {
            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture, $"    tool  {count,6}  {material}"));
        }

        // **The control: how many faces carry a material WITHOUT the flag.** A zero above reads as
        // "the flag never reaches a face" and as "this probe cannot see faces", and only this
        // separates them (`docs/memory/an-empty-search-needs-a-control.md`).
        int without = faces.Count(face =>
            face.MaterialIndex >= 0 && face.MaterialIndex < materials.Count &&
            known.TryGetValue(materials[face.MaterialIndex], out (bool Declares, bool Read) about) &&
            about.Read && !about.Declares);

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"  control: {without:N0} faces carry a material that was read and does NOT declare it"));
    }

    /// <summary>Whether a material asks for the vertex-colour branch of Valve's shader.</summary>
    /// <remarks>
    /// **`VmtMaterial.Flag` is private, so this reads the value the same way it would.** A flag is
    /// present-and-non-zero: five shipped materials declare `$vertexcolor 0`, which is the same as not
    /// declaring it, and counting those as asking for the branch would overstate the population.
    /// </remarks>
    private static bool Declares(VmtMaterial material) =>
        material.Value(VertexColour) is { } value &&
        !value.Trim().Equals("0", StringComparison.Ordinal);
}
