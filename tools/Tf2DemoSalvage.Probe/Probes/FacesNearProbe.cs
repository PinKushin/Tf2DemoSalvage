using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>Every brush face with a corner near a point, world and brush entities alike, with its material and flags.</summary>
/// <remarks>
/// **For the case <c>pixel-face</c> cannot answer**: its ray stops on a brush's solid, and its one-unit containment test
/// rejects a hit off the drawn face's plane. B669's two hits sat about 2.8 units in front of the glass and it reported
/// "0 world faces". The list is by distance to each face's corner box, not by the ray. A brush entity's faces are at its
/// model's own coordinates, so an absence here says nothing about one. The control is the floor under the point, which
/// must be listed.
/// </remarks>
public sealed class FacesNearProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "faces-near";

    /// <inheritdoc/>
    public string Summary =>
        "brush faces with a corner near a point, world and entity alike: faces-near <map> <x> <y> <z> <radius> [material substring]";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        MapLocator locator = new(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder);

        if (arguments.Count < 5 || locator.Find(arguments[0]) is not { } path)
        {
            output.WriteLine(Summary);
            return;
        }

        float x = float.Parse(arguments[1], CultureInfo.InvariantCulture);
        float y = float.Parse(arguments[2], CultureInfo.InvariantCulture);
        float z = float.Parse(arguments[3], CultureInfo.InvariantCulture);
        float radius = float.Parse(arguments[4], CultureInfo.InvariantCulture);
        string filter = arguments.Count > 5 ? arguments[5] : string.Empty;

        byte[] bytes = File.ReadAllBytes(path);
        IReadOnlyList<string> materials = [.. BspMaterials.Read(bytes).Select(material => material.Name)];

        foreach (BspSurface face in BspSurfaces.Read(bytes))
        {
            string material = face.MaterialIndex >= 0 && face.MaterialIndex < materials.Count ? materials[face.MaterialIndex] : "?";

            if (face.Vertices.Count == 0 || !material.Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // The nearest point of the face's corner box, so a large pane whose corners are all far still lists.
            float dx = Math.Max(0f, Math.Max(face.Vertices.Min(v => v.X) - x, x - face.Vertices.Max(v => v.X)));
            float dy = Math.Max(0f, Math.Max(face.Vertices.Min(v => v.Y) - y, y - face.Vertices.Max(v => v.Y)));
            float dz = Math.Max(0f, Math.Max(face.Vertices.Min(v => v.Z) - z, z - face.Vertices.Max(v => v.Z)));

            if (MathF.Sqrt((dx * dx) + (dy * dy) + (dz * dz)) > radius)
            {
                continue;
            }

            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"face {face.FaceIndex} '{material}' normal ({face.Normal.X:0.00} {face.Normal.Y:0.00} {face.Normal.Z:0.00}) flags [{face.Flags}] " +
                $"x {face.Vertices.Min(v => v.X):0}..{face.Vertices.Max(v => v.X):0} y {face.Vertices.Min(v => v.Y):0}..{face.Vertices.Max(v => v.Y):0} " +
                $"z {face.Vertices.Min(v => v.Z):0}..{face.Vertices.Max(v => v.Z):0}"));
        }
    }
}
