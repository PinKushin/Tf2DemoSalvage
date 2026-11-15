using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// Whether each shipped included animation model's bones line up with the skeleton that includes it (B621).
/// </summary>
/// <remarks>
/// **The question**: <c>StudioAnimation.PoseBetweenInto</c> sizes and seeds its blend from the INCLUDED model's bones but
/// writes each track at the BASE model's index, so it is right only while <c>StudioBones.Remap(included, base)</c> is the
/// identity. Valve seeds base bone <c>i</c> through <c>boneMap</c> (<c>bone_setup.cpp:931-947</c>) and has no such
/// condition.
///
/// <code>
///   animation-remap
/// </code>
///
/// The players' <c>{class}_animations.mdl</c> against <c>{class}.mdl</c>, and the first-person
/// <c>c_{class}_animations.mdl</c> against <c>c_{class}_arms.mdl</c>. **The control**: one class's animations against
/// another class's skeleton must report a non-identity, or the instrument cannot see one.
/// </remarks>
public sealed class AnimationRemapProbe : IProbe
{
    private static readonly string[] Classes =
        ["scout", "soldier", "pyro", "demo", "heavy", "engineer", "medic", "sniper", "spy"];

    /// <inheritdoc/>
    public string Name => "animation-remap";

    /// <inheritdoc/>
    public string Summary =>
        "whether every player and arms animation model's bones are the identity onto their skeleton (B621): animation-remap";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (new MapLocator(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder).FindGameFolder() is not { } folder)
        {
            output.WriteLine("The game folder could not be found.");
            return;
        }

        VpkArchive archive = VpkArchive.Open(Path.Combine(folder, "tf2_misc_dir.vpk"));
        int pairs = 0;
        int identity = 0;

        foreach (string name in Classes)
        {
            foreach ((string included, string skeleton) in new[]
            {
                ($"models/player/{name}_animations.mdl", $"models/player/{name}.mdl"),
                ($"models/weapons/c_models/c_{name}_animations.mdl", $"models/weapons/c_models/c_{name}_arms.mdl"),
            })
            {
                if (Compare(archive, included, skeleton) is not { } line)
                {
                    output.WriteLine($"{included}: not found");
                    continue;
                }

                pairs++;
                identity += line.Identity ? 1 : 0;
                output.WriteLine(line.Text);
            }
        }

        output.WriteLine($"pairs {pairs}, identity {identity}, not identity {pairs - identity}");

        // The control: a class's animations onto ANOTHER class's skeleton.
        if (Compare(archive, "models/player/scout_animations.mdl", "models/player/heavy.mdl") is { } control)
        {
            output.WriteLine($"control (scout animations onto heavy): {control.Text}");
        }
    }

    private static (bool Identity, string Text)? Compare(VpkArchive archive, string included, string skeleton)
    {
        if (archive.ReadFile(included) is not { } animations || archive.ReadFile(skeleton) is not { } model)
        {
            return null;
        }

        IReadOnlyList<StudioBone> from = StudioBones.Read(animations);
        IReadOnlyList<StudioBone> to = StudioBones.Read(model);
        int[] remap = StudioBones.Remap(from, to);
        int moved = remap.Where((target, index) => target != index).Count();
        bool same = from.Count == to.Count && moved == 0;

        string first = moved == 0
            ? ""
            : "; first " + string.Join(", ", remap.Select((target, index) => (target, index)).Where(pair => pair.target != pair.index)
                .Take(4).Select(pair => $"{from[pair.index].Name} {pair.index}->{pair.target}"));

        return (same, $"{included}: {from.Count} bones onto {to.Count}, {moved} not at their own index{first}");
    }
}
