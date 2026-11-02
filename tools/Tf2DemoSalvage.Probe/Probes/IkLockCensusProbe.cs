using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// Every lock list the nine player classes ship, against the one condition under which sharing a bone memo across a
/// sequence's locks changes the pose.
/// </summary>
/// <remarks>
/// **The question** (iklock-compose, 2026-11-02): `SolveSequenceLocks` shares one `CBoneBitList` across a sequence's
/// `SolveLock` calls (<c>bone_setup.cpp:4295-4311</c>), and ours did not after a successful lock. The two agree to the
/// bit unless a LATER lock's `BuildBoneChain` walk — its end bone and every ancestor — passes through a bone an EARLIER
/// lock of the same list rewrote: that lock's three links, which `Studio_SolveIK`, the slam and `SolveBone` write. A
/// chain locked twice is the sharpest case of it (the second lock's end bone IS the first's link 2).
///
/// <code>
///   iklock-census
/// </code>
///
/// **Every locking sequence, not the first** (the differential once took `LocksOf`'s first list, 2 of 814). Chains come
/// from the class's own `.mdl`, locks from its `_animations.mdl`, which is how `pIKChain` forwards to the root header
/// (<c>studio.h:2536</c>). **The control** is the lock and chain totals: zero would mean the reader is broken.
/// </remarks>
public sealed class IkLockCensusProbe : IProbe
{
    private static readonly string[] Classes =
        ["scout", "soldier", "pyro", "demo", "heavy", "engineer", "medic", "sniper", "spy"];

    /// <inheritdoc/>
    public string Name => "iklock-census";

    /// <inheritdoc/>
    public string Summary =>
        "every player lock list: chains locked twice, a lock walking through an earlier lock's links, weights: iklock-census";

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

        int lists = 0;
        int locks = 0;
        int twice = 0;
        int through = 0;
        Dictionary<string, int> shapes = [];
        Dictionary<(float Position, float Rotation), int> weights = [];
        List<string> cases = [];

        foreach (string name in Classes)
        {
            if (archive.ReadFile($"models/player/{name}.mdl") is not { } player ||
                archive.ReadFile($"models/player/{name}_animations.mdl") is not { } animations)
            {
                output.WriteLine($"{name}: model or animations not found");
                continue;
            }

            IReadOnlyList<StudioIkChain> chains = StudioIkChains.Read(player);
            int[] parents = [.. StudioBones.Read(player).Select(bone => bone.Parent)];
            IReadOnlyList<StudioSequence> sequences = StudioSequences.Read(animations);
            int classLists = 0;

            for (int sequence = 0; sequence < sequences.Count; sequence++)
            {
                IReadOnlyList<StudioIkLock> list = StudioIkLocks.Read(animations, sequence);

                if (list.Count == 0)
                {
                    continue;
                }

                lists++;
                classLists++;
                locks += list.Count;

                string shape = string.Join(
                    "+",
                    list.Select(entry => entry.Chain >= 0 && entry.Chain < chains.Count ? chains[entry.Chain].Name : $"#{entry.Chain}"));
                shapes[shape] = shapes.GetValueOrDefault(shape) + 1;

                for (int later = 0; later < list.Count; later++)
                {
                    StudioIkLock entry = list[later];
                    (float, float) key = (entry.PositionWeight, entry.LocalRotationWeight);
                    weights[key] = weights.GetValueOrDefault(key) + 1;

                    if (End(entry, chains) is not { } end)
                    {
                        continue;
                    }

                    HashSet<int> walked = Walk(end, parents);

                    for (int earlier = 0; earlier < later; earlier++)
                    {
                        if (list[earlier].Chain == entry.Chain)
                        {
                            twice++;
                            Note(cases, $"{name}:{sequences[sequence].Label} locks chain {entry.Chain} twice");
                        }
                        else if (Links(list[earlier], chains).Any(walked.Contains))
                        {
                            through++;
                            Note(cases, $"{name}:{sequences[sequence].Label} lock {later} (chain {entry.Chain}) walks through lock {earlier}'s links (chain {list[earlier].Chain})");
                        }
                    }
                }
            }

            output.WriteLine(
                $"{name}: {chains.Count} chains ({string.Join(", ", chains.Select(chain => $"{chain.Name}={string.Join("/", chain.Links.Select(link => link.Bone))}"))}), " +
                $"{classLists} locking sequences of {sequences.Count}");
        }

        output.WriteLine($"lock lists {lists}, locks {locks} (the control: both nonzero, or the reader is broken)");
        output.WriteLine($"a chain locked twice in one list: {twice}");
        output.WriteLine($"a later lock walking through an earlier lock's links: {through}");

        foreach (string line in cases)
        {
            output.WriteLine($"  {line}");
        }

        output.WriteLine("lock-list shapes (chains in order): " + string.Join("; ", shapes.OrderByDescending(pair => pair.Value).Select(pair => $"{pair.Key} {pair.Value}")));
        output.WriteLine("weights (position, rotation): " + string.Join("; ", weights.OrderByDescending(pair => pair.Value).Select(pair => $"({pair.Key.Position:0.###}, {pair.Key.Rotation:0.###}) {pair.Value}")));
    }

    private static void Note(List<string> cases, string line)
    {
        if (cases.Count < 20)
        {
            cases.Add(line);
        }
    }

    private static int? End(StudioIkLock entry, IReadOnlyList<StudioIkChain> chains) =>
        entry.Chain >= 0 && entry.Chain < chains.Count && chains[entry.Chain].Links.Count > 2
            ? chains[entry.Chain].Links[2].Bone
            : null;

    private static IEnumerable<int> Links(StudioIkLock entry, IReadOnlyList<StudioIkChain> chains) =>
        entry.Chain >= 0 && entry.Chain < chains.Count
            ? chains[entry.Chain].Links.Take(3).Select(link => link.Bone)
            : [];

    /// <summary>`BuildBoneChain`'s walk from a bone: the bone and every ancestor.</summary>
    private static HashSet<int> Walk(int bone, int[] parents)
    {
        HashSet<int> walked = [];
        int at = bone;

        while (at >= 0 && at < parents.Length && walked.Add(at))
        {
            at = parents[at];
        }

        return walked;
    }
}
