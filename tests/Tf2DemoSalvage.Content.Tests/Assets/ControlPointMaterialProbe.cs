using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Text;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// What material the control point floor is made of, and what it asks for.
/// </summary>
/// <remarks>
/// A probe, not a test: it asserts nothing about the map, it prints what the map says so a
/// rendering question can be settled by reading rather than by guessing. Every control point in
/// cp_process renders black in the viewer while the log reports no material failing to load, so
/// the base texture is present and something in the shading is wrong.
/// </remarks>
public sealed class ControlPointMaterialProbe
{
    [Test]
    public void ControlPointFloor_ItsMaterialRequests_AreReported()
    {
        string tf = GameInstall.Require();

        string mapPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Tf2DemoSalvage",
            "maps",
            "cp_process_f12.bsp");

        if (!File.Exists(mapPath))
        {
            Assert.Ignore("the map or the game is not installed");
            return;
        }

        byte[] bytes = File.ReadAllBytes(mapPath);
        string[] names = BspMaterials.ReadNames(bytes);
        PakFile pak = PakFile.ReadFrom(bytes);

        List<VpkArchive> archives =
        [
            .. new[] { "tf2_textures_dir.vpk", "tf2_misc_dir.vpk" }
                .Select(name => Path.Combine(tf, name))
                .Where(File.Exists)
                .Select(VpkArchive.Open),
        ];

        byte[]? Find(string path)
        {
            byte[]? packed = pak.ReadFile(path);

            return packed ?? archives
                .Select(archive => archive.ReadFile(path))
                .FirstOrDefault(found => found is not null);
        }

        string[] candidates =
        [
            .. names.Where(name =>
                name.Contains("CAP", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("CONTROL", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("POINT", StringComparison.OrdinalIgnoreCase)),
        ];

        TestContext.Out.WriteLine($"CP {candidates.Length} candidate materials of {names.Length}");

        foreach (string name in candidates)
        {
            TestContext.Out.WriteLine($"CP  ==== {name}");

            if (Find("materials/" + name + ".vmt") is not { } vmt)
            {
                TestContext.Out.WriteLine("CP    (vmt not found)");
                continue;
            }

            TestContext.Out.WriteLine(
                string.Join(
                    Environment.NewLine,
                    Encoding.UTF8.GetString(vmt)
                        .Split('\n')
                        .Select(line => "CP    " + line.TrimEnd())));
        }

        // And how many materials in the whole map ask for a cubemap at all, which is the feature
        // the viewer does not implement.
        int envmapped = 0;

        foreach (string name in names)
        {
            if (Find("materials/" + name + ".vmt") is not { } vmt)
            {
                continue;
            }

            if (Encoding.UTF8.GetString(vmt).Contains("$envmap", StringComparison.OrdinalIgnoreCase))
            {
                envmapped++;
            }
        }

        TestContext.Out.WriteLine($"CP {envmapped} of {names.Length} map materials declare $envmap");

        // Which materials actually lie under a control point. The cap points are the only thing
        // rendering black, so naming their surfaces is what settles it - the material NAME never
        // had to contain "cap" for the floor beneath one to.
        IReadOnlyList<BspEntity> entities = BspEntities.ReadFrom(bytes);
        IReadOnlyList<BspSurface> surfaces = BspSurfaces.Read(bytes);

        foreach (BspEntity entity in entities)
        {
            if (!entity.TryGetValue("classname", out string type) ||
                type != "team_control_point" ||
                !entity.TryGetValue("origin", out string origin))
            {
                continue;
            }

            float[] at = [.. origin.Split(' ').Select(part => float.Parse(part, CultureInfo.InvariantCulture))];

            TestContext.Out.WriteLine($"CP  ---- control point at {origin}");

            Dictionary<string, int> beneath = [];

            foreach (BspSurface surface in surfaces)
            {
                if (!surface.IsVisible || surface.Vertices.Count == 0)
                {
                    continue;
                }

                // Flat, upward, within 256 units horizontally and 128 below: the floor a player
                // stands on to capture, and nothing on the walls around it.
                if (surface.Normal.Z < 0.7f)
                {
                    continue;
                }

                float centreX = surface.Vertices.Average(vertex => vertex.X);
                float centreY = surface.Vertices.Average(vertex => vertex.Y);
                float centreZ = surface.Vertices.Average(vertex => vertex.Z);

                if (MathF.Abs(centreX - at[0]) > 160f ||
                    MathF.Abs(centreY - at[1]) > 160f ||
                    centreZ > at[2] + 32f ||
                    centreZ < at[2] - 1024f)
                {
                    continue;
                }

                string name = surface.MaterialIndex >= 0 && surface.MaterialIndex < names.Length
                    ? names[surface.MaterialIndex]
                    : "(none)";

                beneath[name] = beneath.TryGetValue(name, out int seen) ? seen + 1 : 1;
            }

            foreach ((string name, int count) in beneath.OrderByDescending(pair => pair.Value))
            {
                bool hasEnvmap = Find("materials/" + name + ".vmt") is { } file &&
                    Encoding.UTF8.GetString(file).Contains("$envmap", StringComparison.OrdinalIgnoreCase);

                TestContext.Out.WriteLine(
                    $"CP    {count,4} faces  {name}{(hasEnvmap ? "   [$envmap]" : string.Empty)}");
            }
        }

        // Every overlay near a control point, with its material. The category view shows the disc
        // is drawn and classified as brush, so what turns it black is either its lightmap or a
        // decal painted over it - and a decal is the only one of those that can be a circle.
        IReadOnlyList<BspOverlay> overlays = BspOverlays.Read(bytes);

        foreach (BspOverlay overlay in overlays)
        {
            if (MathF.Abs(overlay.Origin.X) > 512f || MathF.Abs(overlay.Origin.Y) > 512f)
            {
                continue;
            }

            string name = overlay.MaterialIndex >= 0 && overlay.MaterialIndex < names.Length
                ? names[overlay.MaterialIndex]
                : "(none)";

            float width = overlay.Corners.Max(corner => corner.X) - overlay.Corners.Min(corner => corner.X);
            float height = overlay.Corners.Max(corner => corner.Y) - overlay.Corners.Min(corner => corner.Y);

            TestContext.Out.WriteLine(
                $"CPOV at ({overlay.Origin.X:0},{overlay.Origin.Y:0},{overlay.Origin.Z:0}) " +
                $"{width:0}x{height:0} u[{overlay.U.Start:0.##}..{overlay.U.End:0.##}] " +
                $"v[{overlay.V.Start:0.##}..{overlay.V.End:0.##}] order {overlay.RenderOrder} {name}");

            // The corner ORDER is the whole question. If corners 0 and 1 share an X and differ in
            // Y, then index 0->1 walks the V axis and the winding is Valve's uv0=bottom-left.
            TestContext.Out.WriteLine(
                "CPOV    corners " + string.Join(
                    " ", overlay.Corners.Select(corner => $"({corner.X:0.#},{corner.Y:0.#})")));

            // **The aspect ratio is the measurement.** flU spans the whole texture width and flV
            // its height, so if the texture is four times as wide as it is tall and the quad is
            // four times as long along BasisU as along BasisV, then U maps to the BasisU axis.
            // Nothing else about the corner order has to be assumed.
            if (Find("materials/" + name + ".vmt") is { } forSize &&
                VmtMaterial.Parse(forSize).BaseTexture is { } texture &&
                Find("materials/" + texture.TrimEnd('\r', '\n', ' ') + ".vtf") is { } vtf)
            {
                VtfTexture decoded = VtfTexture.Decode(vtf, 4096);

                // The alpha channel decides whether a stain tints what is under it or paints over
                // it. A decal decoded with alpha 255 everywhere is opaque however the blend state
                // is set, which is what a black disc over a control point looks like.
                long alpha = 0;
                long opaque = 0;
                long red = 0;

                for (int at = 0; at + 3 < decoded.Pixels.Length; at += 4)
                {
                    red += decoded.Pixels[at];
                    alpha += decoded.Pixels[at + 3];

                    if (decoded.Pixels[at + 3] == 255)
                    {
                        opaque++;
                    }
                }

                long pixels = decoded.Pixels.Length / 4;

                TestContext.Out.WriteLine(
                    $"CPOV    texture {decoded.Width}x{decoded.Height} " +
                    $"(aspect {(float)decoded.Width / decoded.Height:0.###}), " +
                    $"quad aspect {width / height:0.###}, " +
                    $"mean red {red / (double)pixels:0.#}, mean alpha {alpha / (double)pixels:0.#}, " +
                    $"{opaque * 100.0 / pixels:0.#}% fully opaque");
            }

            if (Find("materials/" + name + ".vmt") is { } vmtFile)
            {
                TestContext.Out.WriteLine(
                    "CPOV    vmt " + Encoding.UTF8.GetString(vmtFile)
                        .Replace("\r", " ", StringComparison.Ordinal)
                        .Replace("\n", " ", StringComparison.Ordinal)
                        .Replace("\t", " ", StringComparison.Ordinal));
            }
        }

        Assert.Pass();
    }
}
