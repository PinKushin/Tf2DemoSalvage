using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Viewer3D.Tests;

/// <summary>
/// Which materials actually declare <c>$vertexcolor</c> and <c>$vertexalpha</c>, and on what shader.
/// </summary>
/// <remarks>
/// **Asked before building anything, because the census counting a parameter does not mean the
/// renderer is missing a feature.** <c>$modblend</c> was the worked example: declared in three
/// shipped VMTs, read by nothing, no published shader implementing it — the correct implementation
/// was nothing at all.
///
/// The specific doubt here is that this renderer already carries a per-vertex colour, and already
/// multiplies every surface by it. For a static prop that channel holds the compiler's baked
/// per-vertex lighting, which is exactly what the engine uses for props. So "66 materials want
/// <c>$vertexcolor</c>" may describe a feature that is present, absent, or present-but-unconditional
/// — and those need different work.
///
/// This is a probe rather than an assertion: it reports, and the numbers go into the finding.
/// </remarks>
public sealed class VertexColourCensusProbe
{
    private const string MapName = "cp_process_final";

    [Test]
    [Explicit("A probe: reports what declares vertex colour rather than asserting anything.")]
    public void VertexColour_DeclaringShaders_AreReported()
    {
        if (Tf2Install.Folder is not { } game)
        {
            Assert.Ignore("Team Fortress 2 is not installed; set TF2_FOLDER to run this.");
            return;
        }

        string path = Path.Combine(game, "maps", MapName + ".bsp");

        if (!File.Exists(path))
        {
            Assert.Ignore($"{MapName} is not installed.");
            return;
        }

        byte[] map = File.ReadAllBytes(path);
        GameArchives archives = GameArchives.Open(game);
        PakFile pak = PakFile.ReadFrom(map);
        MapAssets assets = MapAssets.Load(map, archives, maximumTextureSize: 4);

        Dictionary<string, int> byShader = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, int> alphaByShader = new(StringComparer.OrdinalIgnoreCase);
        List<string> examples = [];

        foreach (string name in assets.Materials
            .Select(material => material.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            byte[]? bytes = pak.ReadFile($"materials/{name}.vmt");

            if (bytes is null && archives.Read($"materials/{name}.vmt") is { } stock)
            {
                bytes = stock.ToArray();
            }

            if (bytes is null)
            {
                continue;
            }

            VmtMaterial material;

            try
            {
                material = VmtMaterial.Parse(bytes);
            }
            catch (InvalidDataException)
            {
                continue;
            }

            if (material.Value("$vertexcolor") is not null)
            {
                byShader[material.Shader] = byShader.GetValueOrDefault(material.Shader) + 1;

                if (examples.Count < 8)
                {
                    examples.Add($"{name} [{material.Shader}]");
                }
            }

            if (material.Value("$vertexalpha") is not null)
            {
                alphaByShader[material.Shader] = alphaByShader.GetValueOrDefault(material.Shader) + 1;
            }
        }

        TestContext.Out.WriteLine(
            "$vertexcolor by shader: " +
            string.Join(", ", byShader.OrderByDescending(pair => pair.Value)
                .Select(pair => $"{pair.Key} x{pair.Value}")));

        TestContext.Out.WriteLine(
            "$vertexalpha by shader: " +
            string.Join(", ", alphaByShader.OrderByDescending(pair => pair.Value)
                .Select(pair => $"{pair.Key} x{pair.Value}")));

        TestContext.Out.WriteLine("examples: " + string.Join("; ", examples));
    }
}
