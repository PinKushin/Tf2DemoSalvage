using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// Which per-bone flags TF2's own models actually set.
/// </summary>
/// <remarks>
/// **`mstudiobone_t::flags` drives more of the engine than any other field on a bone** — the bone
/// mask, the merge cache, the procedural rules, and which of two blends `SlerpBones` runs. This
/// project reads the field and tests three of the bits, and had no idea which ones real content
/// uses.
///
/// <code>
///   bone-flags z1800
///   bone-flags z1800 20000
/// </code>
///
/// **Written for B292, whose flag may be rare**, and kept general because the same walk answers the
/// open questions beside it: how many bones are marked for bone merge (an unmarked one makes a
/// wearer build its whole skeleton, `bone_merge_cache.cpp:95`) and how many are procedural (B182,
/// where TF2's cosmetics lean on jiggle bones this project does not simulate).
///
/// **Denominators, always.** Every row prints the count of bones examined beside the count carrying
/// the bit, because a zero with no denominator is a fact about the probe
/// (<c>docs/memory/an-empty-search-needs-a-control.md</c>).
/// </remarks>
public sealed class BoneFlagProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "bone-flags";

    /// <inheritdoc/>
    public string Summary => "which per-bone flags real models set: bone-flags <demo> [tick]";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count == 0)
        {
            output.WriteLine("bone-flags <demo> [tick]");
            return;
        }

        string? path = DemoCorpus.Find(arguments[0], output);

        if (path is null)
        {
            output.WriteLine($"No demo named '{arguments[0]}'.");
            return;
        }

        byte[] file = File.ReadAllBytes(path);
        DemoTimeline timeline = DemoTimeline.Build(file);

        MapLocator locator = new(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder);
        string mapName = Tf2DemoSalvage.Core.Container.DemoHeader.Parse(file).MapName;

        if (mapName.Length == 0 ||
            locator.Find(mapName) is not { } mapPath ||
            locator.FindGameFolder() is not { } folder)
        {
            output.WriteLine("The demo's map or the game could not be found.");
            return;
        }

        int tick = arguments.Count > 1
            ? int.Parse(arguments[1], CultureInfo.InvariantCulture)
            : timeline.FirstTick + ((timeline.LastTick - timeline.FirstTick) / 2);

        List<SceneProp> props = [];
        timeline.PropsAt(tick, props);

        List<ScenePlayer> players = [];
        timeline.PlayersAt(tick, players);

        GameContent game = GameContent.Open(folder, NullLoggerFactory.Instance);
        new WeaponPropModels().Resolve(props, players, game.Weapons.For);

        LoadedMap map = LoadedMap.Read(
            File.ReadAllBytes(mapPath), game, timeline, 0, NullLoggerFactory.Instance);

        if (map.Assets is not { } assets)
        {
            output.WriteLine("The map loaded with no assets.");
            return;
        }

        (string Name, int Bit)[] flags =
        [
            ("BONE_FIXED_ALIGNMENT", StudioBoneFlags.FixedAlignment),
            ("BONE_USED_BY_BONE_MERGE", StudioBoneFlags.UsedByBoneMerge),
            ("BONE_ALWAYS_PROCEDURAL", StudioBoneFlags.AlwaysProcedural),
            ("BONE_PHYSICALLY_SIMULATED", StudioBoneFlags.PhysicallySimulated),
            ("BONE_PHYSICS_PROCEDURAL", StudioBoneFlags.PhysicsProcedural),
            ("BONE_USED_BY_HITBOX", StudioBoneFlags.UsedByHitbox),
            ("BONE_USED_BY_ATTACHMENT", StudioBoneFlags.UsedByAttachment),
            ("BONE_USED_BY_VERTEX_LOD0", StudioBoneFlags.UsedByVertexLod0),
        ];

        int[] counts = new int[flags.Length];
        Dictionary<int, List<string>> examples = [];

        // **`proctype` decides WHICH rule computes a procedural bone, and the five are not
        // interchangeable.** `CalcProceduralBone` (`bone_setup.cpp:4932`) handles AXISINTERP,
        // QUATINTERP, AIMATBONE and AIMATATTACH and returns false for anything else; JIGGLE is
        // handled separately in `BuildTransformations`. So the flag count alone cannot say which
        // implementation a model is waiting on.
        Dictionary<int, int> byType = [];
        Dictionary<int, List<string>> typeExamples = [];

        int bones = 0;
        int models = 0;

        foreach (string model in props
            .Select(prop => prop.ModelPath)
            .Where(model => model.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(model => model, StringComparer.Ordinal))
        {
            if (assets.Geometry(model)?.Skinned is not { } skinned)
            {
                continue;
            }

            models++;

            foreach (StudioBone bone in skinned.Bones)
            {
                bones++;

                if (bone.ProcedureType != 0)
                {
                    byType[bone.ProcedureType] = byType.GetValueOrDefault(bone.ProcedureType) + 1;

                    if (!typeExamples.TryGetValue(bone.ProcedureType, out List<string>? shown))
                    {
                        typeExamples[bone.ProcedureType] = shown = [];
                    }

                    if (shown.Count < 4)
                    {
                        shown.Add($"{Path.GetFileName(model)}:{bone.Name}");
                    }
                }

                for (int flag = 0; flag < flags.Length; flag++)
                {
                    if ((bone.Flags & flags[flag].Bit) == 0)
                    {
                        continue;
                    }

                    counts[flag]++;

                    if (!examples.TryGetValue(flag, out List<string>? named))
                    {
                        examples[flag] = named = [];
                    }

                    if (named.Count < 4)
                    {
                        named.Add($"{Path.GetFileName(model)}:{bone.Name}");
                    }
                }
            }
        }

        output.WriteLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"{Path.GetFileName(path)} on {mapName} at tick {tick}: " +
                $"{bones} bones across {models} skinned models"));

        for (int flag = 0; flag < flags.Length; flag++)
        {
            string named = examples.TryGetValue(flag, out List<string>? some)
                ? string.Join(", ", some)
                : "none";

            output.WriteLine(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{flags[flag].Name,-28} {counts[flag],6} of {bones}   {named}"));
        }

        string[] rules =
            ["none", "AXISINTERP", "QUATINTERP", "AIMATBONE", "AIMATATTACH", "JIGGLE"];

        foreach ((int type, int count) in byType.OrderBy(entry => entry.Key))
        {
            string rule = type >= 0 && type < rules.Length
                ? rules[type]
                : $"unknown {type.ToString(CultureInfo.InvariantCulture)}";

            output.WriteLine(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"proctype {rule,-24} {count,6} of {bones}   " +
                    $"{string.Join(", ", typeExamples[type])}"));
        }

        if (byType.Count == 0)
        {
            output.WriteLine("proctype: no bone declares a procedural rule");
        }
    }
}
