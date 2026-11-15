using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// Whether every included model in one VPK lines up with the skeleton that includes it, the condition B621 rests on.
/// </summary>
/// <remarks>
/// The mechanism and its citations are B621 in <c>docs/RISKS.md</c>; this is only the instrument.
///
/// <code>
///   animation-remap [dir.vpk]
/// </code>
///
/// Walks every <c>.mdl</c> in the archive (default: the installed game's <c>tf2_misc_dir.vpk</c>; pass an era build's
/// to measure that build), reads its includes with <c>StudioModelGroups.Read</c> as production does, and maps each
/// included skeleton onto the base with <c>StudioBones.Remap</c>, the same call and argument order as
/// <c>PropModels.Remaps</c>. A pair is SAFE when every included bone maps to its own index and the included skeleton
/// is no longer than the base. **The control**: scout's animations onto heavy's skeleton must come back unsafe, or the
/// instrument cannot see the hazard.
/// </remarks>
public sealed class AnimationRemapProbe : IProbe
{
    private const int Listed = 30;

    /// <inheritdoc/>
    public string Name => "animation-remap";

    /// <inheritdoc/>
    public string Summary =>
        "every included model in a VPK: does its skeleton map onto the base at its own index (B621): animation-remap [dir.vpk]";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        string? folder = new MapLocator(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder).FindGameFolder();
        string? path = folder is null ? null : Path.Combine(folder, "tf2_misc_dir.vpk");

        if (arguments.Count > 0)
        {
            path = arguments[0];
        }

        if (path is null || !File.Exists(path))
        {
            output.WriteLine($"No archive: {path ?? "the game folder could not be found"}");
            return;
        }

        VpkArchive archive = VpkArchive.Open(path);
        output.WriteLine($"archive {path}");

        if (Compare(archive, "models/player/scout_animations.mdl", "models/player/heavy.mdl") is not { } control)
        {
            output.WriteLine("control: scout_animations.mdl or heavy.mdl not found - the instrument is unproven");
        }
        else
        {
            output.WriteLine($"control (scout animations onto heavy): {control.Text}");
            output.WriteLine(control.Safe ? "INSTRUMENT BROKEN: the control came back safe" : "control: unsafe, as it must be");
        }

        int models = 0;
        int pairs = 0;
        int safe = 0;
        int missing = 0;
        List<string> unsafeLines = [];

        foreach (string model in archive.Paths.Where(entry => entry.EndsWith(".mdl", StringComparison.OrdinalIgnoreCase)).Order(StringComparer.Ordinal))
        {
            if (archive.ReadFile(model) is not { } bytes || StudioModelGroups.Read(bytes) is not { Count: > 0 } includes)
            {
                continue;
            }

            models++;

            foreach (string included in includes)
            {
                if (Compare(archive, included, model) is not { } line)
                {
                    missing++;
                    continue;
                }

                pairs++;

                if (line.Safe)
                {
                    safe++;
                }
                else if (unsafeLines.Count < Listed)
                {
                    unsafeLines.Add($"{model} <- {line.Text}");
                }
            }
        }

        output.WriteLine($"models with includes {models}, pairs {pairs}, safe {safe}, unsafe {pairs - safe}, include not in this archive {missing}");

        foreach (string line in unsafeLines)
        {
            output.WriteLine($"  {line}");
        }
    }

    private static (bool Safe, string Text)? Compare(VpkArchive archive, string included, string skeleton)
    {
        if (archive.ReadFile(included) is not { } animations || archive.ReadFile(skeleton) is not { } model)
        {
            return null;
        }

        IReadOnlyList<StudioBone> from = StudioBones.Read(animations);
        IReadOnlyList<StudioBone> to = StudioBones.Read(model);
        int[] remap = StudioBones.Remap(from, to);
        int unmatched = remap.Count(target => target < 0);
        int moved = remap.Where((target, index) => target >= 0 && target != index).Count();
        bool longer = from.Count > to.Count;

        string first = moved == 0
            ? ""
            : "; first " + string.Join(", ", remap.Select((target, index) => (target, index)).Where(pair => pair.target >= 0 && pair.target != pair.index)
                .Take(4).Select(pair => $"{from[pair.index].Name} {pair.index}->{pair.target}"));

        return (moved == 0 && unmatched == 0 && !longer,
            $"{included}: {from.Count} bones onto {to.Count}, moved {moved}, unmatched {unmatched}{first}");
    }
}
