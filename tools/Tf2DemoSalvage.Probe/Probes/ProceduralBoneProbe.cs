using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// Which procedural bone rules TF2's models actually declare, over every model it ships.
/// </summary>
/// <remarks>
/// **The question B317 leaves behind.** `CalcProceduralBone` dispatches five rules
/// (`bone_setup.cpp:4932-4965`); this project implements two — `STUDIO_PROC_JIGGLE` and, since
/// B317, `STUDIO_PROC_QUATINTERP`. One tick of one demo showed the other three on no bone at all,
/// which is 44 models. TF2 ships thousands, and a rule used by one weapon in the game is still a
/// model that draws wrongly.
///
/// **Every `.mdl` in `tf2_misc_dir.vpk`, not a hand-picked list**, for the reason
/// `SequenceFlagProbe` gives: a sample chosen by someone who already has a hypothesis tends to
/// confirm it.
///
/// **The count is not the answer — `SKINNED` is.** A procedural bone nothing is weighted to computes
/// a transform that reaches no mesh, so an unimplemented rule on such a bone is bookkeeping rather
/// than a defect. That distinction is what turned B317 from "four bones out of 540, ignore it" into
/// a forearm that does not twist on every player in every demo.
/// </remarks>
public sealed class ProceduralBoneProbe : IProbe
{
    /// <inheritdoc />
    public string Name => "procedural-bones";

    /// <inheritdoc />
    public string Summary =>
        "which procedural bone rules TF2's models declare, and whether those bones carry " +
        "vertices: procedural-bones [limit]";

    /// <inheritdoc />
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (new MapLocator(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder)
                .FindGameFolder() is not { } folder)
        {
            output.WriteLine("The game folder could not be found.");
            return;
        }

        int limit = arguments.Count > 0 && int.TryParse(
            arguments[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int asked)
            ? asked
            : int.MaxValue;

        string archivePath = Path.Combine(folder, "tf2_misc_dir.vpk");

        if (!File.Exists(archivePath))
        {
            output.WriteLine($"No archive at {archivePath}.");
            return;
        }

        VpkArchive archive = VpkArchive.Open(archivePath);

        (string Name, int Type)[] rules =
        [
            ("AXISINTERP", StudioProcedureType.AxisInterpolate),
            ("QUATINTERP", StudioProcedureType.QuaternionInterpolate),
            ("AIMATBONE", StudioProcedureType.AimAtBone),
            ("AIMATATTACH", StudioProcedureType.AimAtAttachment),
            ("JIGGLE", StudioProcedureType.Jiggle),
        ];

        int[] bones = new int[rules.Length];
        int[] skinned = new int[rules.Length];
        int[] models = new int[rules.Length];
        Dictionary<int, List<string>> examples = [];

        int read = 0;
        int unreadable = 0;

        foreach (string path in archive.Paths
            .Where(entry => entry.EndsWith(".mdl", StringComparison.OrdinalIgnoreCase))
            .Take(limit))
        {
            if (archive.ReadFile(path) is not { } bytes)
            {
                unreadable++;
                continue;
            }

            read++;

            ReadOnlyMemory<byte> model = bytes;

            bool[] seenHere = new bool[rules.Length];

            IReadOnlyList<StudioBone> table = StudioBones.Read(model);

            for (int bone = 0; bone < table.Count; bone++)
            {
                // **The flag AND the type, because `CalcProceduralBone` tests both.** Its whole
                // body is inside `if ( boneFlags(iBone) & BONE_ALWAYS_PROCEDURAL )` and only then
                // switches on `proctype` — a bone carrying a rule without the flag is never asked.
                if ((table[bone].Flags & StudioBoneFlags.AlwaysProcedural) == 0 ||
                    table[bone].ProcedureType == 0)
                {
                    continue;
                }

                for (int rule = 0; rule < rules.Length; rule++)
                {
                    if (table[bone].ProcedureType != rules[rule].Type)
                    {
                        continue;
                    }

                    bones[rule]++;

                    if (!seenHere[rule])
                    {
                        seenHere[rule] = true;
                        models[rule]++;
                    }

                    bool carriesVertices =
                        (table[bone].Flags & StudioBoneFlags.UsedByVertexLod0) != 0;

                    if (carriesVertices)
                    {
                        skinned[rule]++;
                    }

                    if (!examples.TryGetValue(rule, out List<string>? shown))
                    {
                        examples[rule] = shown = [];
                    }

                    if (shown.Count < 5)
                    {
                        shown.Add(
                            $"{Path.GetFileName(path)}:{table[bone].Name}" +
                            $"{(carriesVertices ? " SKINNED" : " no-verts")}");
                    }

                    break;
                }
            }
        }

        output.WriteLine(
            $"{read} models read from {Path.GetFileName(archivePath)}, {unreadable} unreadable");

        for (int rule = 0; rule < rules.Length; rule++)
        {
            string shown = examples.TryGetValue(rule, out List<string>? found)
                ? string.Join(", ", found)
                : "none";

            output.WriteLine(
                $"  {rules[rule].Name,-12} {bones[rule],6} bones ({skinned[rule]} skinned) " +
                $"across {models[rule]} models   {shown}");
        }
    }
}
