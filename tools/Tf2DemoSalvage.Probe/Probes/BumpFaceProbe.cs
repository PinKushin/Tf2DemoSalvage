using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>Which of a map's faces are bump lit, by material, beside the material the map itself ships (B514).</summary>
/// <remarks>
/// A face reads the three directional lightmaps only when its texinfo carries <c>SURF_BUMPLIGHT</c>, which vbsp sets
/// from the material. This joins the two: per material, faces, displacement faces, and how many of each carry the
/// flag — then the VMT as the map's pakfile holds it, raw, because a <c>patch</c> material is where a parameter gets
/// lost. A measurement, not a test (D38).
/// </remarks>
public sealed class BumpFaceProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "bump-faces";

    /// <inheritdoc/>
    public string Summary =>
        "per material: faces, displacement faces and how many are SURF_BUMPLIGHT, with the map's own VMT: bump-faces <map> [material substring]";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count < 1)
        {
            output.WriteLine(Summary);
            return;
        }

        MapLocator locator = new(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder);

        if (locator.Find(arguments[0]) is not { } path)
        {
            output.WriteLine($"No map named '{arguments[0]}'.");
            return;
        }

        string filter = arguments.Count > 1 ? arguments[1] : string.Empty;
        ReadOnlyMemory<byte> file = File.ReadAllBytes(path);
        PakFile pak = PakFile.ReadFrom(file);
        GameArchives? archives = locator.FindGameFolder() is { } folder ? GameArchives.Open(folder) : null;
        IReadOnlyList<string> materials = [.. BspMaterials.Read(file).Select(material => material.Name)];
        Dictionary<string, (int Faces, int Bumped, int Terrain, int TerrainBumped)> rows = new(StringComparer.OrdinalIgnoreCase);

        IReadOnlyList<BspSurface> surfaces = BspSurfaces.Read(file);
        IReadOnlyList<BspFaceLighting> lighting = BspLightmaps.ReadAll(file);

        foreach (BspSurface face in surfaces)
        {
            if (face.MaterialIndex < 0 || face.MaterialIndex >= materials.Count)
            {
                continue;
            }

            string material = materials[face.MaterialIndex];
            bool bumped = (face.Flags & SurfaceProperties.BumpLight) != 0;
            rows.TryGetValue(material, out (int Faces, int Bumped, int Terrain, int TerrainBumped) row);
            rows[material] = (
                row.Faces + 1,
                row.Bumped + (bumped ? 1 : 0),
                row.Terrain + (face.IsDisplacement ? 1 : 0),
                row.TerrainBumped + (face.IsDisplacement && bumped ? 1 : 0));
        }

        output.WriteLine("faces  bumped  terrain  terrain-bumped  material");

        foreach ((string material, (int Faces, int Bumped, int Terrain, int TerrainBumped) row) in rows
            .Where(pair => pair.Key.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(pair => pair.Value.Terrain)
            .ThenByDescending(pair => pair.Value.Faces))
        {
            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"{row.Faces,5}  {row.Bumped,6}  {row.Terrain,7}  {row.TerrainBumped,14}  {material}"));

            if (filter.Length == 0)
            {
                continue;
            }

            // The light itself: the flat set against the three directional ones, mean green over the material's faces.
            double[] light = new double[4];
            long luxels = 0;

            for (int index = 0; index < surfaces.Count && index < lighting.Count; index++)
            {
                if (surfaces[index].MaterialIndex < 0 || surfaces[index].MaterialIndex >= materials.Count ||
                    !string.Equals(materials[surfaces[index].MaterialIndex], material, StringComparison.OrdinalIgnoreCase) ||
                    !lighting[index].IsBumped)
                {
                    continue;
                }

                BspLightmap[] sets = [lighting[index].Flat, .. lighting[index].Directional];

                for (int set = 0; set < sets.Length; set++)
                {
                    ReadOnlySpan<byte> texels = sets[set].Pixels.Span;

                    for (int at = 0; at + 4 <= texels.Length; at += 4)
                    {
                        light[set] += BspLightmaps.Load(texels.Slice(at, 4)).Green;
                    }
                }

                luxels += lighting[index].Flat.Pixels.Length / 4;
            }

            if (luxels > 0)
            {
                output.WriteLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"       light (green, mean): flat {light[0] / luxels:0.000}; directional {light[1] / luxels:0.000} {light[2] / luxels:0.000} {light[3] / luxels:0.000}"));
            }

            if (pak.ReadFile($"materials/{material}.vmt") is { } vmt)
            {
                output.WriteLine(Encoding.UTF8.GetString(vmt).TrimEnd());
            }

            // The bump map's mean, because an ssbump texel IS three light weights: their sum is the scale on the light.
            if ((pak.ReadFile($"materials/{material}.vmt") ?? archives?.Read($"materials/{material}.vmt")) is { } bytes &&
                VmtMaterial.Parse(bytes).Value("$bumpmap") is { Length: > 0 } bump &&
                archives?.Read("materials/" + bump.Replace((char)92, '/').Trim('/') + ".vtf") is { } pixels)
            {
                VtfTexture image = VtfTexture.Decode(pixels);
                double[] sum = new double[3];

                for (int at = 0; at + 3 < image.Pixels.Length; at += 4)
                {
                    sum[0] += image.Pixels[at];
                    sum[1] += image.Pixels[at + 1];
                    sum[2] += image.Pixels[at + 2];
                }

                double count = image.Pixels.Length / 4 * 255.0;
                output.WriteLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"       $bumpmap {bump}: mean {sum[0] / count:0.000} {sum[1] / count:0.000} {sum[2] / count:0.000}, sum {(sum[0] + sum[1] + sum[2]) / count:0.000}; {image.Format}, VTF flags 0x{BitConverter.ToUInt32(pixels, 20):X8}, bumpscale {BitConverter.ToSingle(pixels, 48):0.###}"));
            }
        }
    }
}
