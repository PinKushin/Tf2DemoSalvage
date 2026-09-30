using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// Whether <c>PropsAt</c> stepped like playback answers what a cold sample answers, on a real demo (B438).
/// </summary>
/// <remarks>
/// **A sample must depend on its tick and nothing else**, and the sampler keeps state between calls
/// to pay only for what changed (B259 stage C). This asks the question the viewer's frame loop poses:
/// one timeline stepped forward the way playback steps it, against a second build of the same file
/// made to rebuild from nothing at every sample. Anything that differs is an answer that depended on
/// the calls before it — which is what a scrub shows differently from playback.
///
/// **How the cold side is made cold**: two calls per sample, the first with interpolation off and the
/// second with it on. A change of that flag always rebuilds the whole sample (B399), so the second
/// call answers from nothing, and it costs two rebuilds rather than a walk over every wake from the
/// start of the demo.
///
/// **Two controls, because an instrument proves itself before it is believed.** Every fiftieth
/// sample is rebuilt cold a second time and compared with the first cold answer, which must agree —
/// otherwise the comparison, not the sampler, is what differs. And each stepped sample is compared
/// with the one before it: a window in which nothing moves could never show a difference however
/// wrong the sampler were.
///
/// Reports and asserts nothing (D126).
/// </remarks>
public sealed class SampleHistoryProbe : IProbe
{
    private const string Usage = "sample-history <demo> [from] [ticks] [step]";

    /// <summary>How often the cold-against-cold control runs, in samples.</summary>
    private const int ControlEvery = 50;

    /// <summary>How many differences are printed in full before the summary.</summary>
    private const int Shown = 12;

    /// <summary>A shared empty list, so the scalar comparison never sees two lists by reference.</summary>
    private static readonly float[] NoFloats = [];

    /// <inheritdoc/>
    public string Name => "sample-history";

    /// <inheritdoc/>
    public string Summary =>
        "whether PropsAt stepped like playback answers what a cold sample does: " + Usage;

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count < 1)
        {
            output.WriteLine(Usage);
            return;
        }

        string? path = DemoCorpus.Find(arguments[0], output);

        if (path is null)
        {
            output.WriteLine($"No demo named '{arguments[0]}'.");
            return;
        }

        byte[] bytes = File.ReadAllBytes(path);

        DemoTimeline stepped = DemoTimeline.Build(bytes);
        DemoTimeline cold = DemoTimeline.Build(bytes);

        int from = Whole(arguments, 1, (stepped.FirstTick + stepped.LastTick) / 2);
        int ticks = Whole(arguments, 2, 2000);
        double step = arguments.Count > 3
            && double.TryParse(arguments[3], NumberStyles.Float, CultureInfo.InvariantCulture, out double asked)
            && asked > 0d
                ? asked
                : 0.25d;

        List<SceneProp> now = [];
        List<SceneProp> before = [];
        List<SceneProp> fresh = [];
        List<SceneProp> again = [];

        int samples = 0;
        int differing = 0;
        int differingProps = 0;
        int moving = 0;
        int controls = 0;
        int controlsDiffering = 0;

        Dictionary<string, Tally> byClass = new(StringComparer.Ordinal);
        HashSet<int> entities = [];
        int shown = 0;
        (int Entity, double Tick)? first = null;

        for (double tick = from; tick <= from + ticks; tick += step)
        {
            (now, before) = (before, now);

            stepped.PropsAt(tick, now);

            Cold(cold, tick, fresh);

            samples++;

            if (samples > 1 && Differences(now, before, null) > 0)
            {
                moving++;
            }

            if (samples % ControlEvery == 0)
            {
                Cold(cold, tick, again);

                controls++;

                if (Differences(again, fresh, null) > 0)
                {
                    controlsDiffering++;
                }
            }

            int wrong = Differences(now, fresh, difference =>
            {
                SceneProp prop = difference.Prop;
                string key = prop.ClassName.Length > 0 ? prop.ClassName : Path.GetFileName(prop.ModelPath);

                if (!byClass.TryGetValue(key, out Tally? tally))
                {
                    byClass[key] = tally = new Tally();
                }

                tally.Add(difference);
                entities.Add(prop.EntityIndex);

                // **The first few in full**, so a number in the summary can be traced to a tick and read
                // off the entity's own track.
                if (shown++ < Shown)
                {
                    SceneProp? mine = now.Find(candidate => candidate.EntityIndex == prop.EntityIndex);

                    output.WriteLine(string.Create(
                        CultureInfo.InvariantCulture,
                        $"  tick {tick}: entity {prop.EntityIndex} {key} stepped {Describe(mine)} cold {Describe(prop)}"));
                }

                first ??= (prop.EntityIndex, tick);
            });

            if (wrong > 0)
            {
                differing++;
                differingProps += wrong;
            }
        }

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{Path.GetFileName(path)}: ticks {from}..{from + ticks} every {step}, {samples} samples, "
            + $"{stepped.Props.Count} tracks"));

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"  stepped differs from cold at {differing} of {samples} samples "
            + $"({100d * differing / Math.Max(1, samples):0.0}%), {differingProps} props in all, "
            + $"{entities.Count} distinct entities"));

        foreach ((string key, Tally tally) in byClass.OrderByDescending(pair => pair.Value.Props))
        {
            output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"    {key}: {tally}"));
        }

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"  control, cold against cold: {controlsDiffering} of {controls} samples differ (must be 0)"));

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"  control, the scene moves: {moving} of {samples - 1} steps changed some prop (must not be 0)"));

        if (first is { } earliest && stepped.TrackFor(earliest.Entity, earliest.Tick) is { } track)
        {
            Dump(output, track, earliest.Tick);
        }
    }

    /// <summary>The first differing entity's own record around the tick: keyframes, then the history the sampler reads.</summary>
    private static void Dump(TextWriter output, ScenePropTrack track, double tick)
    {
        const int Before = 40;
        const int After = 20;

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"  entity {track.EntityIndex} ({track.ModelPath}), alive {track.FirstTick}..{track.EndTick}, "
            + $"first differing at {tick}"));

        for (int index = 0; index < track.Keyframes.Count; index++)
        {
            (int at, ScenePose pose) = track.Keyframes[index];

            if (at >= tick - Before && at <= tick + After)
            {
                output.WriteLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"    keyframe {at}: ({pose.X:0.##} {pose.Y:0.##} {pose.Z:0.##}) hidden {pose.Hidden} "
                    + $"render mode {pose.RenderMode}"));
            }
        }

        InterpolatedHistory history = track.Simulation;

        for (int index = 0; index < history.Count; index++)
        {
            (int received, int flushed) = history.ArrivalAt(index);

            if (received >= tick - Before && received <= tick + After)
            {
                ReadOnlySpan<float> values = history.ValuesAt(index);

                output.WriteLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"    simulation entry {index}: changetime {history.ChangeTimeAt(index)} received {received} "
                    + $"flushed {(flushed == int.MaxValue ? "never" : flushed.ToString(CultureInfo.InvariantCulture))} "
                    + $"({values[0]:0.##} {values[1]:0.##} {values[2]:0.##})"));
            }
        }
    }

    /// <summary>Rebuilds the cold timeline's sample from nothing at one tick.</summary>
    /// <remarks>A change of the interpolation flag always rebuilds the whole sample (B399).</remarks>
    private static void Cold(DemoTimeline cold, double tick, List<SceneProp> into)
    {
        cold.PropsAt(tick, into, interpolating: false);
        cold.PropsAt(tick, into, interpolating: true);
    }

    /// <summary>Counts the props two samples disagree on, handing each difference to a reporter.</summary>
    /// <param name="sampled">The sample under test.</param>
    /// <param name="reference">What it should equal.</param>
    /// <param name="report">Called once per differing prop, or null to only count.</param>
    /// <returns>How many props differ; a membership difference counts every prop from it on.</returns>
    private static int Differences(
        List<SceneProp> sampled, List<SceneProp> reference, Action<Difference>? report)
    {
        int shared = Math.Min(sampled.Count, reference.Count);
        int differ = 0;

        for (int index = 0; index < shared; index++)
        {
            SceneProp mine = sampled[index];
            SceneProp theirs = reference[index];

            if (mine.EntityIndex != theirs.EntityIndex)
            {
                report?.Invoke(new Difference(theirs, Membership: true));

                return differ + Math.Max(sampled.Count, reference.Count) - index;
            }

            Difference difference = new(theirs)
            {
                Position = Largest(
                    mine.Pose.X - theirs.Pose.X, mine.Pose.Y - theirs.Pose.Y, mine.Pose.Z - theirs.Pose.Z),
                Angle = Largest(
                    mine.Pose.Pitch - theirs.Pose.Pitch, mine.Pose.Yaw - theirs.Pose.Yaw,
                    mine.Pose.Roll - theirs.Pose.Roll),
                Cycle = Math.Abs(mine.Pose.Cycle - theirs.Pose.Cycle),
                PoseParameter = Gap(mine.Pose.PoseParameters, theirs.Pose.PoseParameters),
                State =
                    Scalars(mine) with { Pose = default } != Scalars(theirs) with { Pose = default }
                    || Still(mine.Pose) != Still(theirs.Pose)
                    || !mine.Pose.Layers.SequenceEqual(theirs.Pose.Layers)
                    || !mine.Pose.BoneControllers.SequenceEqual(theirs.Pose.BoneControllers)
                    || !(mine.Pose.Gestures ?? []).SequenceEqual(theirs.Pose.Gestures ?? []),
            };

            if (!difference.Any)
            {
                continue;
            }

            differ++;
            report?.Invoke(difference);
        }

        if (sampled.Count != reference.Count)
        {
            report?.Invoke(new Difference(
                sampled.Count > reference.Count ? sampled[shared] : reference[shared], Membership: true));

            differ += Math.Abs(sampled.Count - reference.Count);
        }

        return differ;
    }

    /// <summary>A prop with its only reference-typed member that is not a value made equal.</summary>
    private static SceneProp Scalars(SceneProp prop) => prop with { Econ = null };

    /// <summary>A pose with every quantity measured elsewhere zeroed and its lists made equal.</summary>
    private static ScenePose Still(ScenePose pose) =>
        pose with
        {
            X = 0f,
            Y = 0f,
            Z = 0f,
            Pitch = 0f,
            Yaw = 0f,
            Roll = 0f,
            Cycle = 0f,
            PoseParameters = NoFloats,
            BoneControllers = NoFloats,
            Layers = [],
            Gestures = null,
        };

    /// <summary>One prop's moving quantities, or "absent".</summary>
    private static string Describe(SceneProp? prop) =>
        prop is null
            ? "absent"
            : string.Create(
                CultureInfo.InvariantCulture,
                $"({prop.Pose.X:0.##} {prop.Pose.Y:0.##} {prop.Pose.Z:0.##}) yaw {prop.Pose.Yaw:0.##} "
                + $"cycle {prop.Pose.Cycle:0.####} pose [{string.Join(' ', prop.Pose.PoseParameters.Select(value => value.ToString("0.####", CultureInfo.InvariantCulture)))}]");

    private static float Largest(float first, float second, float third) =>
        Math.Max(Math.Abs(first), Math.Max(Math.Abs(second), Math.Abs(third)));

    /// <summary>The largest difference between two parameter lists, or one when their lengths differ.</summary>
    private static float Gap(IReadOnlyList<float> first, IReadOnlyList<float> second)
    {
        if (first.Count != second.Count)
        {
            return 1f;
        }

        float gap = 0f;

        for (int index = 0; index < first.Count; index++)
        {
            gap = Math.Max(gap, Math.Abs(first[index] - second[index]));
        }

        return gap;
    }

    private static int Whole(IReadOnlyList<string> arguments, int at, int otherwise) =>
        arguments.Count > at
        && int.TryParse(arguments[at], NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            ? value
            : otherwise;

    /// <summary>How one prop differed between two samples, by quantity.</summary>
    private sealed record Difference(SceneProp Prop, bool Membership = false)
    {
        public float Position { get; init; }

        public float Angle { get; init; }

        public float Cycle { get; init; }

        public float PoseParameter { get; init; }

        public bool State { get; init; }

        public bool Any =>
            Membership || State || Position > 0f || Angle > 0f || Cycle > 0f || PoseParameter > 0f;
    }

    /// <summary>How often, and by how much, one class of prop differed.</summary>
    private sealed class Tally
    {
        private int _position;
        private int _angle;
        private int _cycle;
        private int _poseParameter;
        private int _state;
        private int _membership;
        private float _largestPosition;
        private float _largestAngle;
        private float _largestCycle;
        private float _largestPoseParameter;

        public int Props { get; private set; }

        public void Add(Difference difference)
        {
            Props++;

            Count(difference.Position, ref _position, ref _largestPosition);
            Count(difference.Angle, ref _angle, ref _largestAngle);
            Count(difference.Cycle, ref _cycle, ref _largestCycle);
            Count(difference.PoseParameter, ref _poseParameter, ref _largestPoseParameter);

            _state += difference.State ? 1 : 0;
            _membership += difference.Membership ? 1 : 0;
        }

        public override string ToString() =>
            string.Create(
                CultureInfo.InvariantCulture,
                $"{Props} props - position x{_position} (up to {_largestPosition:0.####}), "
                + $"angle x{_angle} (up to {_largestAngle:0.####}), "
                + $"cycle x{_cycle} (up to {_largestCycle:0.####}), "
                + $"pose parameter x{_poseParameter} (up to {_largestPoseParameter:0.####}), "
                + $"state x{_state}, membership x{_membership}");

        private static void Count(float size, ref int count, ref float largest)
        {
            if (size > 0f)
            {
                count++;
                largest = Math.Max(largest, size);
            }
        }
    }
}
