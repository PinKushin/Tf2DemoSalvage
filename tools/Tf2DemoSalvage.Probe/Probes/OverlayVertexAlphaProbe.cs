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

/// <summary>Which overlays' materials read vertex alpha, and which are Patch materials (B329).</summary>
/// <remarks>
/// An overlay's distance fade reaches the screen only through vertex alpha (engine.dll 0x180110630), so
/// this counts, per installed map or one map, the overlays whose material — resolved through its Patch
/// include by the production <see cref="VmtMaterial.Load"/> — answers <see cref="VmtMaterial.TakesVertexAlpha"/>.
/// The control is the overlay total and the count of materials that could not be read.
/// <code>
///   overlay-vertex-alpha [map]
/// </code>
/// </remarks>
public sealed class OverlayVertexAlphaProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "overlay-vertex-alpha";

    /// <inheritdoc/>
    public string Summary =>
        "overlays whose material reads vertex alpha, and Patch overlay materials: overlay-vertex-alpha [map]";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        MapLocator locator = new(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder);

        if (locator.Find(arguments.Count > 0 ? arguments[0] : "cp_process_final") is not { } anyMap ||
            locator.FindGameFolder() is not { } folder ||
            Path.GetDirectoryName(anyMap) is not { } mapFolder)
        {
            output.WriteLine("No map or no TF2 install.");
            return;
        }

        GameArchives archives = GameArchives.Open(folder);
        IEnumerable<string> maps = arguments.Count > 0
            ? [anyMap]
            : Directory.EnumerateFiles(mapFolder, "*.bsp").Order(StringComparer.Ordinal);

        int overlays = 0, reads = 0, fadingReads = 0, fading = 0, patches = 0, unreadable = 0;
        SortedSet<string> patchNames = new(StringComparer.OrdinalIgnoreCase);

        foreach (string path in maps)
        {
            ReadOnlyMemory<byte> file = File.ReadAllBytes(path);
            IReadOnlyList<BspOverlay> placed;
            IReadOnlyList<string> names;
            PakFile pak;

            try
            {
                placed = BspOverlays.Read(file);
                names = [.. BspMaterials.Read(file).Select(material => material.Name)];
                pak = PakFile.ReadFrom(file);
            }
            catch (InvalidDataException exception)
            {
                output.WriteLine($"{Path.GetFileNameWithoutExtension(path)}: unreadable, {exception.Message}");
                continue;
            }

            byte[]? Find(string name) => pak.ReadFile(name) ?? archives.Read(name);

            foreach (BspOverlay overlay in placed.Where(o => o.MaterialIndex >= 0 && o.MaterialIndex < names.Count))
            {
                overlays++;
                fading += overlay.FadeMaxSquared > 0f ? 1 : 0;
                string name = names[overlay.MaterialIndex];

                if (Find($"materials/{name}.vmt") is not { } vmt)
                {
                    unreadable++;
                    continue;
                }

                if (VmtMaterial.Parse(vmt).IsPatch)
                {
                    patches++;
                    patchNames.Add(name);
                }

                if (VmtMaterial.Load(vmt, Find).TakesVertexAlpha)
                {
                    reads++;
                    fadingReads += overlay.FadeMaxSquared > 0f ? 1 : 0;
                }
            }
        }

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{overlays:N0} overlays ({fading:N0} fading): {reads:N0} read vertex alpha ({fadingReads:N0} fading); " +
            $"{patches:N0} on a Patch material; {unreadable:N0} material unreadable"));

        foreach (string name in patchNames.Take(20))
        {
            output.WriteLine($"  patch {name}");
        }
    }
}
