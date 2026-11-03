using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>What a static prop's computed cube is made of: per material struck, the rays, their light and both reflectivities.</summary>
/// <remarks>
/// **The struck faces and their light are production's**: the same <see cref="SurfaceColour"/>, built as `LevelLighting` builds it. The engine multiplies by `IMaterial::GetReflectivity` (`FUN_1801b74f0`, vtable slot 31), and this project by the map's texdata
/// value, so the two are printed side by side: the VTF header's, at offset 32, is the representative texture's own.
/// </remarks>
public sealed class CubeRaysProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "cube-rays";

    /// <inheritdoc/>
    public string Summary =>
        "what a static prop's 162-ray cube struck, per material, with texdata and VTF reflectivity: cube-rays <map> <x> <y> <z>";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        MapLocator locator = new(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder);

        if (arguments.Count < 4 || locator.Find(arguments[0]) is not { } path || locator.FindGameFolder() is not { } folder)
        {
            output.WriteLine(Summary);
            return;
        }

        Vector3 origin = new(
            float.Parse(arguments[1], CultureInfo.InvariantCulture),
            float.Parse(arguments[2], CultureInfo.InvariantCulture),
            float.Parse(arguments[3], CultureInfo.InvariantCulture));

        byte[] bytes = File.ReadAllBytes(path);
        GameArchives archives = GameArchives.Open(folder);
        PakFile pak = PakFile.ReadFrom(bytes);
        MapLevel level = MapLevel.Read(bytes, NullLogger.Instance);
        IReadOnlyList<BspMaterial> materials = level.Materials;

        if (level.Leaves is not { } tree || level.LeafFaces is not { } leafFaces || level.LightSamples is not { } samples)
        {
            output.WriteLine("this map lacks the lumps the rays read");
            return;
        }

        // As `LevelLighting.StaticPropAmbientOf` builds it.
        SurfaceColour walker = new(DecalWorld.From(tree, leafFaces, level.Surfaces, level.Terrain), samples, static _ => null);

        Vector3? skyAmbient = level.WorldLights.FirstOrDefault(light => light.Kind == WorldLightKind.SkyAmbient) is { } sky
            ? new Vector3(sky.Intensity.Red, sky.Intensity.Green, sky.Intensity.Blue)
            : null;

        AmbientCube cube = StaticPropAmbient.Compute(
            origin,
            (start, end) => walker.LightVec(start, end, styles: false),
            texdata => texdata >= 0 && texdata < materials.Count
                ? new Vector3(materials[texdata].Reflectivity.Red, materials[texdata].Reflectivity.Green, materials[texdata].Reflectivity.Blue)
                : Vector3.Zero,
            skyAmbient);

        output.WriteLine($"cube at {origin}: +x {cube.PositiveX} -x {cube.NegativeX} +y {cube.PositiveY} -y {cube.NegativeY} +z {cube.PositiveZ} -z {cube.NegativeZ}");

        Dictionary<int, (int Rays, Vector3 Light, float WeightY)> byTexdata = [];

        foreach (Vector3 normal in SphericalSampleNormals.All)
        {
            (DecalFace? face, Vector3 light) = walker.LightVec(origin, origin + (normal * StaticPropAmbient.RayLength), styles: false);
            int key = face?.Texdata ?? -1;
            byTexdata.TryGetValue(key, out (int Rays, Vector3 Light, float WeightY) sum);
            byTexdata[key] = (sum.Rays + 1, sum.Light + light, sum.WeightY + MathF.Max(0f, -normal.Y));
        }

        foreach ((int texdata, (int rays, Vector3 light, float weightY)) in byTexdata.OrderByDescending(pair => pair.Value.WeightY))
        {
            if (texdata < 0 || texdata >= materials.Count)
            {
                output.WriteLine($"{rays,4} rays, no face");
                continue;
            }

            BspMaterial material = materials[texdata];
            (float R, float G, float B)? header = HeaderReflectivity(material.Name, pak, archives);

            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"{rays,4} rays (weight toward -y {weightY:0.00}) mean light ({light.X / rays:0.000} {light.Y / rays:0.000} {light.Z / rays:0.000}) texdata {material.Reflectivity} header {header?.ToString() ?? "none"} {material.Name}"));
        }
    }

    /// <summary>The representative texture's `reflectivity`, offset 32 of its VTF header.</summary>
    private static (float R, float G, float B)? HeaderReflectivity(string name, PakFile pak, GameArchives archives)
    {
        byte[]? Find(string file) => pak.ReadFile(file) ?? archives.Read(file);

        if (Find($"materials/{name}.vmt") is not { } vmt || VmtMaterial.Load(vmt, Find).BaseTexture is not { } texture)
        {
            return null;
        }

        if (Find($"materials/{texture.Replace('\\', '/')}.vtf") is not { Length: >= 44 } vtf)
        {
            return null;
        }

        return (
            BinaryPrimitives.ReadSingleLittleEndian(vtf.AsSpan(32)),
            BinaryPrimitives.ReadSingleLittleEndian(vtf.AsSpan(36)),
            BinaryPrimitives.ReadSingleLittleEndian(vtf.AsSpan(40)));
    }
}
