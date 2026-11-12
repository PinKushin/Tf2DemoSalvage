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
/// Which shipped vertex-lit materials name an envmap that <c>InitParamsVertexLitGeneric_DX9</c> undefines (B720).
/// </summary>
/// <remarks>
/// **The candidates are chosen from the VMT; the verdict is the production resolve's.** A VertexLitGeneric or
/// UnlitGeneric material with <c>$envmap</c>, <c>$bumpmap</c> and either <c>$basealphaenvmapmask</c> or <c>$envmapmask</c>
/// is a candidate (`vertexlitgeneric_dx9_helper.cpp:197-211`); each is resolved by `MapAssets.Resolve` with no map and
/// reported as reflecting or not, split by whether it carries <c>$normalmapalphaenvmapmask</c> (which, kept, clears the
/// base-alpha flag first and spares the envmap), <c>$phong</c>, and <c>$basemapalphaphongmask 1</c> (the one
/// <c>$phong</c> case that runs <c>InitParamsSkin_DX9</c> instead, under <c>mat_phong 1</c>).
///
/// **The control** is the reflecting count of the spared rows (with <c>$normalmapalphaenvmapmask</c>): none of them
/// reflecting would mean the resolve or the archive read is broken, not that the engine drops them.
/// </remarks>
public sealed class EnvMapUndefinedCensusProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "envmap-undefined";

    /// <inheritdoc/>
    public string Summary => "shipped vertex-lit materials whose $envmap InitParams undefines (B720): envmap-undefined";

    /// <summary>How many materials to name per row.</summary>
    private const int Named = 12;

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
        SortedDictionary<string, Tally> rows = new(StringComparer.Ordinal);

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
                // Unreadable materials are vmt-param's to count.
                continue;
            }

            bool vertexLit = material.Shader.StartsWith("VertexLitGeneric", StringComparison.OrdinalIgnoreCase) ||
                material.Shader.StartsWith("UnlitGeneric", StringComparison.OrdinalIgnoreCase);
            // The raw flag (B720 review): a material asking for both masks is a candidate too, since which one survives
            // is the params pass's question.
            bool baseAlpha = material.HasBaseAlphaEnvMapMaskFlag;
            bool envMapMask = material.Value("$envmapmask") is not null;
            bool normalMapAlpha = material.UsesNormalMapAlphaAsEnvMapMask;

            // The control rows: a bump map with `$normalmapalphaenvmapmask` and neither other mask keeps its envmap at
            // every setting's params pass that keeps the flag, so these must reflect at the defaults.
            if (!vertexLit || material.EnvMap is null || material.BumpMap is null || !(baseAlpha || envMapMask || normalMapAlpha))
            {
                continue;
            }

            string mask = (baseAlpha, envMapMask) switch
            {
                (true, true) => "basealpha+envmapmask",
                (true, false) => "basealpha",
                (false, true) => "envmapmask",
                _ => "control",
            };
            string key =
                $"{mask}" +
                $" nmaem={(normalMapAlpha ? 1 : 0)}" +
                $" phong={(material.HasPhong ? 1 : 0)}" +
                $" basemapalphaphongmask={(material.BaseMapAlphaPhongMaskIsOne ? 1 : 0)}" +
                $" warp={(material.LightWarpTexture is not null ? 1 : 0)}" +
                $" shader={(material.Shader.StartsWith("VertexLitGeneric", StringComparison.OrdinalIgnoreCase) ? "vlg" : "unlit")}";

            if (!rows.TryGetValue(key, out Tally? row))
            {
                row = new Tally();
                rows[key] = row;
            }

            row.Candidates++;
            string name = path["materials/".Length..^".vmt".Length];
            ResolvedMaterial resolved = MapAssets.Resolve(
                NullLogger.Instance, name, noMap, game.Archives, 16, report: false,
                model: material.Shader.StartsWith("VertexLitGeneric", StringComparison.OrdinalIgnoreCase));

            if ((resolved.Cubemap?.Shading ?? resolved.LocalReflection) is { } shading)
            {
                row.Reflecting++;

                // Off the skin shader, per setting: what the bind swaps in (null there reflects nothing).
                if (shading.SkinMask is not null)
                {
                    row.PhongOff += shading.Generic is null ? 0 : 1;
                    row.BumpOff += shading.GenericWithBumpMapOff is null ? 0 : 1;
                    row.BothOff += shading.GenericWithBothOff is null ? 0 : 1;
                }
            }

            if (row.Names.Count < Named && mask != "control")
            {
                row.Names.Add(name);
            }
        }

        // The instrument's control: the `control` rows must exist and reflect, or the resolve or the archive read is broken.
        int controls = rows.Where(entry => entry.Key.StartsWith("control", StringComparison.Ordinal)).Sum(entry => entry.Value.Candidates);
        int controlsReflecting = rows.Where(entry => entry.Key.StartsWith("control", StringComparison.Ordinal)).Sum(entry => entry.Value.Reflecting);
        output.WriteLine(controls == 0
            ? "control row absent: no bumped $normalmapalphaenvmapmask material found, so nothing below can be believed"
            : $"control: {controlsReflecting} of {controls} bumped $normalmapalphaenvmapmask materials reflect at the defaults");

        output.WriteLine(
            "candidates / reflecting through the resolve (default settings) / skin materials reflecting under mat_phong 0, mat_bumpmap 0, both");

        foreach ((string key, Tally row) in rows)
        {
            output.WriteLine($"  {key}: {row.Candidates} / {row.Reflecting} / {row.PhongOff}, {row.BumpOff}, {row.BothOff}");

            foreach (string name in row.Names)
            {
                output.WriteLine($"      {name}");
            }
        }

        output.WriteLine(
            $"total, controls excluded: {rows.Values.Sum(row => row.Candidates) - controls} candidates, " +
            $"{rows.Values.Sum(row => row.Reflecting) - controlsReflecting} reflecting");
    }

    /// <summary>One row's counts.</summary>
    private sealed class Tally
    {
        public int Candidates;
        public int Reflecting;
        public int PhongOff;
        public int BumpOff;
        public int BothOff;
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
