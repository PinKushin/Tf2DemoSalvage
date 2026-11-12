using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// Which shipped materials ask for <c>$basealphaenvmapmask</c> and reflect unmasked once the inits' rules apply (B720).
/// </summary>
/// <remarks>
/// **Through the production resolve.** Every material whose VMT asks for the mask (`VmtMaterial.UsesBaseAlphaAsEnvMapMask`)
/// is resolved by `MapAssets.Resolve` with no map, and its reflection's `MaskedByBaseAlpha` is read back: false there means
/// the mask was dropped (an opaque base, no base, or a VertexLitGeneric light warp), so the reflection changes. The probe
/// classifies why from the VMT, but the verdict is the resolve's.
///
/// **The control** is the flagged count itself against `vmt-param $basealphaenvmapmask`, and the "kept" column: a run in
/// which nothing keeps the mask would mean the resolve or the texture read is broken, not that the game has no alpha bases.
/// </remarks>
public sealed class BaseAlphaEnvMapMaskCensusProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "basealpha-mask";

    /// <inheritdoc/>
    public string Summary => "shipped materials whose $basealphaenvmapmask the inits drop (B720): basealpha-mask";

    /// <summary>How many affected materials to name per shader.</summary>
    private const int Named = 20;

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (new MapLocator(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder).FindGameFolder() is not { } folder)
        {
            output.WriteLine("The game is not installed.");
            return;
        }

        GameContent game = GameContent.Open(folder, NullLoggerFactory.Instance);
        PakFile noMap = PakFile.Read(EmptyZip());

        Dictionary<string, Tally> byShader = new(StringComparer.OrdinalIgnoreCase);

        foreach (string path in game.Archives.Paths()
            .Where(path => path.EndsWith(".vmt", StringComparison.OrdinalIgnoreCase) && path.StartsWith("materials/", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (game.Archives.Read(path) is not { } bytes)
            {
                continue;
            }

            VmtMaterial material;

            try
            {
                material = VmtMaterial.Load(bytes, game.Archives.Read);
            }
            catch (InvalidDataException)
            {
                // Unreadable materials are vmt-param's to count; this census is over the ones the reader accepts.
                continue;
            }

            if (!material.UsesBaseAlphaAsEnvMapMask)
            {
                continue;
            }

            if (!byShader.TryGetValue(material.Shader, out Tally? row))
            {
                row = new Tally();
                byShader[material.Shader] = row;
            }

            row.Flagged++;

            string name = path["materials/".Length..^".vmt".Length];
            ResolvedMaterial resolved = MapAssets.Resolve(
                NullLogger.Instance, name, noMap, game.Archives, 16, report: false,
                model: material.Shader.StartsWith("VertexLitGeneric", StringComparison.OrdinalIgnoreCase));

            if ((resolved.Cubemap?.Shading ?? resolved.LocalReflection) is { } shading)
            {
                row.Reflecting++;

                if (shading.MaskedByBaseAlpha)
                {
                    row.Kept++;
                }
                else
                {
                    // A skin material reads its own mask while the skin shader draws it, so this one only shows
                    // under `mat_phong 0` or `mat_bumpmap 0`.
                    if (shading.SkinMask is not null)
                    {
                        row.SkinOnly++;
                    }

                    if (material.Value("$basetexture") is null)
                    {
                        row.NoBase++;
                    }
                    else if (material.LightWarpTexture is not null)
                    {
                        row.Warp++;
                    }
                    else
                    {
                        row.Opaque++;
                    }

                    // Named only where the change shows at the default settings.
                    if (shading.SkinMask is null && row.Names.Count < Named)
                    {
                        row.Names.Add(name);
                    }
                }
            }
        }

        output.WriteLine(
            "shader: flagged / reflecting / mask kept / dropped (no base, opaque base, light warp) / of those, skin-shader only");

        foreach ((string shader, Tally row) in byShader.OrderByDescending(entry => entry.Value.Flagged))
        {
            int dropped = row.NoBase + row.Opaque + row.Warp;
            output.WriteLine(
                $"  {shader}: {row.Flagged} / {row.Reflecting} / {row.Kept} / {dropped} ({row.NoBase}, {row.Opaque}, {row.Warp}) / {row.SkinOnly}");

            foreach (string name in row.Names)
            {
                output.WriteLine($"      {name}");
            }
        }

        output.WriteLine(
            $"total: {byShader.Values.Sum(row => row.Flagged)} flagged, {byShader.Values.Sum(row => row.Reflecting)} reflecting, " +
            $"{byShader.Values.Sum(row => row.Kept)} kept, {byShader.Values.Sum(row => row.NoBase + row.Opaque + row.Warp)} dropped");
    }

    /// <summary>One shader's counts.</summary>
    private sealed class Tally
    {
        public int Flagged;
        public int Reflecting;
        public int Kept;
        public int NoBase;
        public int Opaque;
        public int Warp;
        public int SkinOnly;
        public List<string> Names { get; } = [];
    }

    private static byte[] EmptyZip()
    {
        using MemoryStream stream = new();

        using (new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            // An archive with no entries: the resolve's map pakfile, for a material read from the game alone.
        }

        return stream.ToArray();
    }
}
