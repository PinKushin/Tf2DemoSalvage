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
/// Which shipped materials sample <c>$envmapmask</c>, and which keep their envmap under <c>mat_specular 0</c> (B720 part 5),
/// through the production <c>MapAssets.Resolve</c>.
/// </summary>
/// <remarks>
/// **Two tables.** The first takes every shipped VertexLitGeneric or UnlitGeneric material with an <c>$envmap</c> and an
/// <c>$envmapmask</c>, keyed by what decides the answer (shader, <c>$phong</c>, <c>$basemapalphaphongmask 1</c>, bump map,
/// warp, <c>$basealphaenvmapmask</c>), and counts how many reflect at the defaults, how many of those sample the mask at
/// the defaults and under <c>mat_phong 0</c>, and how many name <c>$envmapmaskframe</c> or <c>$envmapmasktransform</c>
/// (neither is read). The second takes every shipped material with an <c>$envmap</c> that resolves a reflection and counts,
/// per shader, how many <c>mat_specular 0</c> undefines and how many keep it.
///
/// **The control** is a row that must be full: a plain VertexLitGeneric with an envmap, a base, no mask, no bump map, no
/// warp, no <c>$phong</c> must reflect, and none of them may sample a mask. And every sampling row must carry a loaded
/// texture; a sampled row with none is the instrument's defect.
/// </remarks>
public sealed class EnvMapMaskCensusProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "envmap-mask";

    /// <inheritdoc/>
    public string Summary => "shipped materials sampling $envmapmask, and those mat_specular 0 leaves reflecting (B720 part 5): envmap-mask [all]";

    private const int Named = 8;

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
        SortedDictionary<string, Tally> masks = new(StringComparer.Ordinal);
        SortedDictionary<string, (int Undefined, int Kept)> specular = new(StringComparer.OrdinalIgnoreCase);
        Tally control = new();
        int missingTexture = 0;

        // `envmap-mask all` names every sampling material, to find one a corpus map draws.
        int named = arguments.Count > 0 && arguments[0] == "all" ? int.MaxValue : Named;
        int both = 0;
        SortedDictionary<string, int> keptBecause = new(StringComparer.OrdinalIgnoreCase);
        List<string> keptNamed = [];
        List<string> controlMisses = [];
        int unexplainedMisses = 0;

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

            if (material.EnvMap is null)
            {
                continue;
            }

            bool vertexLit = material.Shader.StartsWith("VertexLitGeneric", StringComparison.OrdinalIgnoreCase);
            bool modelShader = vertexLit || material.Shader.StartsWith("UnlitGeneric", StringComparison.OrdinalIgnoreCase);
            string name = path["materials/".Length..^".vmt".Length];
            ResolvedMaterial resolved;

            try
            {
                resolved = MapAssets.Resolve(NullLogger.Instance, name, noMap, game.Archives, 16, report: false, model: modelShader);
            }
            catch (InvalidDataException failure)
            {
                // Counted and named, not dropped: a material the resolve refuses is outside both tables.
                output.WriteLine($"  unresolved {name}: {failure.Message}");
                continue;
            }
            MapEnvmapShading? shading = resolved.Cubemap?.Shading ?? resolved.LocalReflection;

            if (shading is { } reflecting)
            {
                (int undefined, int kept) = specular.GetValueOrDefault(material.Shader);
                specular[material.Shader] = reflecting.UndefinedWithoutSpecular ? (undefined + 1, kept) : (undefined, kept + 1);

                // Why a model shader keeps it: no base, or the skin params pass (`$phong` + `$basemapalphaphongmask 1`).
                if (modelShader && !reflecting.UndefinedWithoutSpecular)
                {
                    string why = (material.Value("$basetexture") is null, material.HasPhong && material.BaseMapAlphaPhongMaskIsOne) switch
                    {
                        (true, _) => "no base",
                        (false, true) => "skin params",
                        _ => "other",
                    };
                    string tally = $"{material.Shader}, {why}";
                    keptBecause[tally] = keptBecause.GetValueOrDefault(tally) + 1;

                    if (keptNamed.Count < Named * 2 && why != "skin params")
                    {
                        keptNamed.Add($"{why}: {name}");
                    }
                }

                both += reflecting.MaskedByMaskTexture && reflecting.MaskedByBaseAlpha ? 1 : 0;
            }

            bool hasMask = material.Value("$envmapmask") is not null;
            bool plain = vertexLit && !hasMask && material.BumpMap is null && material.LightWarpTexture is null &&
                !material.HasPhong && material.Value("$basetexture") is not null;

            if (!modelShader || !(hasMask || plain))
            {
                continue;
            }

            Tally row;

            if (plain)
            {
                row = control;
            }
            else
            {
                string key =
                    $"{(vertexLit ? "vlg" : "unlit")}" +
                    $" phong={(material.HasPhong ? 1 : 0)}" +
                    $" basemapalphaphongmask={(material.BaseMapAlphaPhongMaskIsOne ? 1 : 0)}" +
                    $" bump={(material.BumpMap is not null ? 1 : 0)}" +
                    $" warp={(material.LightWarpTexture is not null ? 1 : 0)}" +
                    $" basealpha={(material.HasBaseAlphaEnvMapMaskFlag ? 1 : 0)}";

                if (!masks.TryGetValue(key, out Tally? found))
                {
                    found = new Tally();
                    masks[key] = found;
                }

                row = found;
            }

            row.Candidates++;
            row.Frame += material.Value("$envmapmaskframe") is { } frame && frame.Trim() != "0" ? 1 : 0;
            row.Transform += material.Value("$envmapmasktransform") is not null ? 1 : 0;

            if (shading is not { } drawn)
            {
                // Named with what the resolve had, so a control row that fails can be read rather than counted.
                if (plain)
                {
                    controlMisses.Add($"{name}: $envmap \"{material.EnvMap}\", resolved cube {(resolved.Cubemap is null ? "none" : "yes")}");

                    // `env_cubemap` always resolves a local reflection on a plain material; only a named cube can be absent.
                    unexplainedMisses += material.WantsMapCubemap ? 1 : 0;
                }

                continue;
            }

            row.Reflecting++;
            row.Sampled += drawn.MaskedByMaskTexture ? 1 : 0;
            row.SampledPhongOff += drawn.MaskedByMaskTexture || drawn.Generic is { MaskedByMaskTexture: true } ? 1 : 0;
            missingTexture += (drawn.MaskedByMaskTexture || drawn.Generic is { MaskedByMaskTexture: true }) && drawn.MaskTexture is null ? 1 : 0;

            if (drawn.MaskedByMaskTexture && row.Names.Count < named)
            {
                row.Names.Add(name);
            }
        }

        // The instrument's control: every plain material reflects unless its own `$envmap` names no cube that loads, none
        // samples a mask, and no sampling row lacks its texture. A miss is printed with its envmap so the reason is read.
        bool controlPasses = control.Candidates > 0 && control.Sampled == 0 && missingTexture == 0 && unexplainedMisses == 0;
        output.WriteLine(
            $"control {(controlPasses ? "PASS" : "FAIL")} (plain VertexLitGeneric, base + envmap, no mask/bump/warp/phong): " +
            $"{control.Candidates} candidates, {control.Reflecting} reflect, {control.Sampled} sample a mask (must be 0), " +
            $"{missingTexture} sampling rows with no loaded mask texture (must be 0); not reflecting (an env_cubemap one fails it):");

        foreach (string miss in controlMisses)
        {
            output.WriteLine($"      {miss}");
        }
        output.WriteLine("$envmapmask: candidates / reflecting / sampled at the defaults / sampled under mat_phong 0 / name a frame / name a transform");

        foreach ((string key, Tally row) in masks)
        {
            output.WriteLine($"  {key}: {row.Candidates} / {row.Reflecting} / {row.Sampled} / {row.SampledPhongOff} / {row.Frame} / {row.Transform}");

            foreach (string each in row.Names)
            {
                output.WriteLine($"      {each}");
            }
        }

        output.WriteLine(
            $"total: {masks.Values.Sum(row => row.Candidates)} candidates, {masks.Values.Sum(row => row.Sampled)} sampled at the defaults");
        output.WriteLine($"sampling the mask beside a surviving $basealphaenvmapmask (the shader's SKIP): {both}");
        output.WriteLine("mat_specular 0, materials resolving a reflection: undefined / kept, by shader");

        foreach ((string shader, (int undefined, int kept)) in specular)
        {
            output.WriteLine($"  {shader}: {undefined} / {kept}");
        }

        output.WriteLine("model shaders kept under mat_specular 0, by shader and reason:");

        foreach ((string tally, int count) in keptBecause)
        {
            output.WriteLine($"  {tally}: {count}");
        }

        foreach (string each in keptNamed)
        {
            output.WriteLine($"      {each}");
        }
    }

    /// <summary>One row's counts.</summary>
    private sealed class Tally
    {
        public int Candidates;
        public int Reflecting;
        public int Sampled;
        public int SampledPhongOff;
        public int Frame;
        public int Transform;
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
