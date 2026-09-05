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
/// Which sequence and animation flags TF2's own models set.
/// </summary>
/// <remarks>
/// **`CalcPoseSingle` branches on four flags this project does not read** — `STUDIO_REALTIME`,
/// `STUDIO_CYCLEPOSE`, `STUDIO_ALLZEROS` and `STUDIO_HIDDEN` — and whether that matters is a
/// question about Valve's CONTENT, not about the engine. The engine says what each does; only the
/// models say whether anything uses it.
///
/// <code>
///   sequence-flags
///   sequence-flags 500
/// </code>
///
/// **Every `.mdl` in `tf2_misc_dir.vpk`, not a hand-picked list.** A sample chosen by someone who
/// expects a zero returns a zero. The optional argument caps how many files are read, for a quick
/// look; without it the census is the whole archive.
///
/// **Denominators on every row**, because a zero without one is a fact about the probe rather than
/// about the game (`docs/memory/an-empty-search-needs-a-control.md`). The rows that MUST come back
/// nonzero — `STUDIO_LOOPING`, `STUDIO_DELTA` — are the control: if those are zero the reader is
/// broken, not the content.
/// </remarks>
public sealed class SequenceFlagProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "sequence-flags";

    /// <inheritdoc/>
    public string Summary =>
        "which sequence and animation flags real models set: sequence-flags [model limit]";

    /// <inheritdoc/>
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

        int limit = arguments.Count > 0
            ? int.Parse(arguments[0], CultureInfo.InvariantCulture)
            : int.MaxValue;

        string archivePath = Path.Combine(folder, "tf2_misc_dir.vpk");

        if (!File.Exists(archivePath))
        {
            output.WriteLine($"No archive at {archivePath}.");
            return;
        }

        VpkArchive archive = VpkArchive.Open(archivePath);

        // **Both tables, because the two flag words are different.** A sequence's flags and the
        // flags of the animations behind it are separate fields that happen to share bit values,
        // and reading one for the other is how B284 stayed unsolved for an hour.
        //
        // **Spelled from `studio.h:3078-3088` rather than from `StudioFlags`**, which is internal to
        // the Content assembly. The same choice `AutoLayerTests` makes for `STUDIO_LOCAL`.
        (string Name, int Bit)[] sequenceFlags =
        [
            ("STUDIO_LOOPING", 0x0001),
            ("STUDIO_SNAP", 0x0002),
            ("STUDIO_DELTA", 0x0004),
            ("STUDIO_AUTOPLAY", 0x0008),
            ("STUDIO_POST", 0x0010),
            ("STUDIO_ALLZEROS", AllZeros),
            ("STUDIO_CYCLEPOSE", CyclePose),
            ("STUDIO_REALTIME", Realtime),
            ("STUDIO_LOCAL", 0x0200),
            ("STUDIO_HIDDEN", Hidden),
        ];

        int[] sequenceCounts = new int[sequenceFlags.Length];
        int[] animationCounts = new int[sequenceFlags.Length];
        Dictionary<int, List<string>> examples = [];

        int models = 0;
        int sequences = 0;
        int animations = 0;
        int unreadable = 0;
        int zeroCorners = 0;
        int ordinaryZeroCorners = 0;
        int localZeroSequences = 0;
        int poseKeyed = 0;
        int poseKeyedGrids = 0;
        int locked = 0;
        int lockedChains = 0;
        int playerLocked = 0;
        int weighted = 0;
        List<string> playerLockExamples = [];
        List<string> weightExamples = [];
        List<string> ordinaryExamples = [];
        List<string> poseKeyExamples = [];
        List<string> lockExamples = [];

        foreach (string path in archive.Paths
            .Where(entry => entry.EndsWith(".mdl", StringComparison.OrdinalIgnoreCase))
            .Take(limit))
        {
            if (archive.ReadFile(path) is not { } bytes)
            {
                unreadable++;
                continue;
            }

            models++;

            ReadOnlyMemory<byte> model = bytes;

            int local = -1;

            foreach (StudioSequence sequence in StudioSequences.Read(model))
            {
                sequences++;
                local++;

                // **Two behaviours recorded as unimplemented on an ASSUMPTION about content**
                // (B310): that TF2's blend grids are evenly spaced, so `Studio_LocalPoseParameter`
                // never takes its key-search branch, and that its sequences never lock IK chains,
                // so `AccumulatePose`'s `AddSequenceLocks`/`SolveSequenceLocks` bracket is inert.
                // Neither had been measured.
                (int poseKeys, int ikLocks) = StudioSequences.Unimplemented(model, local);

                if (poseKeys != 0)
                {
                    poseKeyed++;

                    // **The output-level check, not a second reading of the field** (B310). The
                    // count above says the sequence DECLARES keys; this says the grid the reader
                    // built actually carries them, which is the wiring a unit test of `Locate`
                    // cannot see. A branch written and never fed is the fault this audit keeps
                    // finding, twice in its own work today.
                    if (sequence.Blend is { } grid && grid.HasPoseKeys)
                    {
                        poseKeyedGrids++;
                    }

                    if (poseKeyExamples.Count < 3)
                    {
                        poseKeyExamples.Add($"{Path.GetFileName(path)}:{sequence.Label}");
                    }
                }

                if (ikLocks > 0)
                {
                    locked++;
                    lockedChains += ikLocks;

                    // **Whether a normal match ever draws one.** The census walks the ARCHIVE, so
                    // it counts what ships rather than what a demo loads — and the first examples
                    // were Halloween boss models, which no ordinary game contains. A count that
                    // cannot separate those from a player model would rank this by shipped volume
                    // instead of by what is on screen, which is the axis `docs/PARITY-AUDIT.md`
                    // was once wrongly ranked by.
                    // **A lock with both weights at zero is a no-op**, and the engine still runs
                    // the whole bracket for it. Counting the weights decides whether implementing
                    // this changes any pixel or merely spends time — the same question the
                    // all-zeros corners answered the other way.
                    foreach (StudioIkLock entry in StudioIkLocks.Read(model, local))
                    {
                        if (entry.PositionWeight > 0f || entry.LocalRotationWeight > 0f)
                        {
                            weighted++;
                        }

                        if (weightExamples.Count < 4 &&
                            (entry.PositionWeight > 0f || entry.LocalRotationWeight > 0f))
                        {
                            weightExamples.Add(
                                $"{Path.GetFileName(path)}:{sequence.Label} chain {entry.Chain} " +
                                $"pos {entry.PositionWeight:0.##} rot {entry.LocalRotationWeight:0.##}");
                        }
                    }

                    if (path.Contains("/player/", StringComparison.OrdinalIgnoreCase))
                    {
                        playerLocked++;

                        if (playerLockExamples.Count < 4)
                        {
                            playerLockExamples.Add(
                                $"{Path.GetFileName(path)}:{sequence.Label} x{ikLocks}");
                        }
                    }

                    if (lockExamples.Count < 3)
                    {
                        lockExamples.Add(
                            $"{Path.GetFileName(path)}:{sequence.Label} x{ikLocks}");
                    }
                }

                Count(sequenceFlags, sequenceCounts, sequence.Flags, examples, path, sequence.Label);

                // **The question `ScaleBones` turns on.** An all-zeros corner reached by a DELTA
                // sequence is already nothing — expanding it to identity and blending gives what
                // the engine's `QuaternionIdentityBlend` gives. Reached by an ORDINARY sequence it
                // is not: we would expand it to the BIND POSE and blend toward that, where the
                // engine still scales toward identity, and a collapsed corner makes
                // `CalcPoseSingle` return false so the sequence contributes nothing at all.
                // **The ANIMATION's own delta bit, not the sequence's**, and the first version of
                // this probe read the sequence's — the exact confusion the paragraph above cites
                // B284 for, made while writing a comment about it. `PoseIsAllZeros` reads
                // `animdesc.flags`, `StudioAnimation.IsDelta` reads `animdesc.flags`, and a TF2
                // taunt is an ordinary sequence whose animations are additive. Reading the
                // sequence reported 810 of 810 "ordinary", which was a fact about the wrong field.
                foreach (int corner in Corners(sequence))
                {
                    int animationFlags = StudioAnimation.Flags(model, corner);

                    if ((animationFlags & AllZeros) == 0)
                    {
                        continue;
                    }

                    zeroCorners++;

                    if ((animationFlags & 0x0004) != 0)
                    {
                        continue;
                    }

                    ordinaryZeroCorners++;

                    if (ordinaryExamples.Count < 4)
                    {
                        ordinaryExamples.Add($"{Path.GetFileName(path)}:{sequence.Label}");
                    }
                }

                // **The one residue of `bResult`, and it is not covered by the delta argument
                // above.** When the effective corner is all-zeros the engine returns false, which
                // skips `SlerpBones` AND `AddLocalLayers`. Skipping the slerp of an all-zeros delta
                // adds nothing, so that half is a no-op — but a LOCAL autolayer of that sequence
                // would not be applied at all, and that is a real difference. It needs the two to
                // coincide on one sequence.
                if ((sequence.Flags & 0x0200) == 0)
                {
                    continue;
                }

                foreach (int corner in Corners(sequence))
                {
                    if ((StudioAnimation.Flags(model, corner) & AllZeros) != 0)
                    {
                        localZeroSequences++;
                        break;
                    }
                }
            }

            for (int animation = 0; animation < StudioAnimation.Count(model); animation++)
            {
                animations++;

                Count(
                    sequenceFlags,
                    animationCounts,
                    StudioAnimation.Flags(model, animation),
                    examples,
                    path,
                    $"anim {animation}");
            }
        }

        output.WriteLine(
            $"{models} models read from {Path.GetFileName(archivePath)}" +
            $" ({unreadable} unreadable): {sequences} sequences, {animations} animations");

        output.WriteLine("flag                    sequences     animations   example");

        for (int at = 0; at < sequenceFlags.Length; at++)
        {
            string example = examples.TryGetValue(sequenceFlags[at].Bit, out List<string>? seen)
                ? string.Join(", ", seen.Take(2))
                : "none";

            output.WriteLine(
                $"{sequenceFlags[at].Name,-22} {sequenceCounts[at],6} of {sequences,-6} " +
                $"{animationCounts[at],6} of {animations,-6}  {example}");
        }

        output.WriteLine(
            $"all-zeros corners reached by a sequence: {zeroCorners}, " +
            $"of which {ordinaryZeroCorners} by an ORDINARY (non-delta) sequence");

        output.WriteLine(
            ordinaryExamples.Count > 0
                ? $"  {string.Join(", ", ordinaryExamples)}"
                : "  none — every all-zeros corner is reached by a delta animation");

        output.WriteLine(
            $"STUDIO_LOCAL sequences with an all-zeros corner: {localZeroSequences} " +
            "(the only case where CalcPoseSingle returning false loses real work)");

        output.WriteLine(
            $"posekeyindex != 0 (uneven blend grid): {poseKeyed} of {sequences}, " +
            $"of which {poseKeyedGrids} reached Locate with their keys" +
            (poseKeyExamples.Count > 0 ? $"  {string.Join(", ", poseKeyExamples)}" : string.Empty));

        output.WriteLine(
            // **Was "not implemented", and B311 implemented it on 2026-09-04** — measured at 88
            // locks applied on the pose path for `tf2-2026-pub-pov-clean` at tick 14051. A census
            // that keeps saying a feature is missing after it lands is an instrument reporting a
            // false absence, which is the one thing this file exists to avoid.
            $"numiklocks > 0 (AddSequenceLocks/SolveSequenceLocks, implemented B311): " +
            $"{locked} of {sequences} sequences, {lockedChains} chains" +
            (lockExamples.Count > 0 ? $"  {string.Join(", ", lockExamples)}" : string.Empty));

        output.WriteLine(
            $"  of those, {playerLocked} are under models/player/ — what an ordinary match draws" +
            (playerLockExamples.Count > 0
                ? $": {string.Join(", ", playerLockExamples)}"
                : ", so none"));

        output.WriteLine(
            $"  {weighted} of {lockedChains} locks carry a non-zero weight — the rest are no-ops " +
            "the engine still runs the bracket for" +
            (weightExamples.Count > 0 ? $": {string.Join("; ", weightExamples)}" : string.Empty));
    }

    /// <summary>`STUDIO_ALLZEROS` (<c>studio.h:3083</c>) — the animation carries no real data.</summary>
    private const int AllZeros = 0x0020;

    /// <summary>`STUDIO_CYCLEPOSE` (<c>studio.h:3085</c>) — cycle comes from a pose parameter.</summary>
    private const int CyclePose = 0x0080;

    /// <summary>`STUDIO_REALTIME` (<c>studio.h:3086</c>) — cycle comes from the clock.</summary>
    private const int Realtime = 0x0100;

    /// <summary>`STUDIO_HIDDEN` (<c>studio.h:3088</c>) — hidden from tool selection lists.</summary>
    private const int Hidden = 0x0400;

    /// <summary>Every animation a sequence reaches: its grid's corners, or its single animation.</summary>
    private static IEnumerable<int> Corners(StudioSequence sequence)
    {
        if (sequence.Blend is not { } grid)
        {
            yield return sequence.Animation;
            yield break;
        }

        for (int x = 0; x < grid.GroupX; x++)
        {
            for (int y = 0; y < grid.GroupY; y++)
            {
                yield return grid.Animation(x, y);
            }
        }
    }

    private static void Count(
        (string Name, int Bit)[] flags,
        int[] counts,
        int word,
        Dictionary<int, List<string>> examples,
        string path,
        string label)
    {
        for (int at = 0; at < flags.Length; at++)
        {
            if ((word & flags[at].Bit) == 0)
            {
                continue;
            }

            counts[at]++;

            if (!examples.TryGetValue(flags[at].Bit, out List<string>? seen))
            {
                seen = [];
                examples[flags[at].Bit] = seen;
            }

            if (seen.Count < 2)
            {
                seen.Add($"{Path.GetFileName(path)}:{label}");
            }
        }
    }
}
