using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;

// Namespaced away from `Tf2DemoSalvage.Corpus.Tests.*`, where `Corpus` binds to the namespace
// rather than to the helper class — the same reason `CorpusPlayerOriginTests` beside it does.
namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// What a brush entity's angles, render mode and visibility actually do over a demo — a MEASUREMENT.
/// </summary>
/// <remarks>
/// **The owner reported a BLU spawn grate on `cp_fulgur` drawn 90 degrees from its correct
/// orientation, which then stopped drawing and came back when his character could see it again.**
/// Two symptoms, and the map's entity lump makes both askable:
///
/// - Four `func_brush` entities declare non-zero angles — `-0 120 0`, `-0 105 0` twice, and
///   `-0 90 0` at origin `360 -1728 32`, which is BLU's side. Brush entity geometry is compiled in
///   its authored position and the engine rotates it at spawn, so a viewer that does not apply
///   those angles draws exactly one of them 90 degrees out.
/// - **All eighteen `func_door`s declare `rendermode 10`** — `kRenderNone`, *"Don't render."*
///   B221 made this project honour `m_nRenderMode` for the first time on 2026-08-29, the same day
///   the symptom was reported, so "the grate stopped drawing" has an obvious new suspect.
///
/// **Neither can be settled from the map alone**, because the map states a SPAWN value and the demo
/// states what the entity actually became: a server may change either. This walks the timeline and
/// reports what the recording says, which is the only thing that can tell "the map says 90" from
/// "the demo says 90" from "nobody says 90 and the rotation is ours".
///
/// Explicit, and it asserts nothing about the demo: what a community map's entities do is a fact
/// about the map (D38). The harness precondition is asserted so an empty report cannot be read as
/// an empty answer.
/// </remarks>
[Explicit("Diagnostic: reports brush entity angles, render mode and visibility over a demo.")]
public sealed class BrushEntityStateDiagnostic
{
    /// <summary>The recording the owner was watching.</summary>
    private const string Recording = "tf2-2026-pub-pov-clean";

    /// <summary>Ticks to sample across the demo.</summary>
    private const int Samples = 400;

    [Test]
    public void Decode_TheBrushEntities_ReportsTheirAnglesModeAndVisibility()
    {
        string path = Corpus.Demo(Recording);

        DemoTimeline timeline = TimelineCache.For(path);

        int first = timeline.FirstTick;
        int last = timeline.LastTick;
        int step = Math.Max(1, (last - first) / Samples);

        // Keyed by model rather than by entity index, because a brush entity IS its submodel: `*162`
        // is one piece of the map and cannot be confused with another the way a reused entity slot
        // can.
        Dictionary<string, BrushSeen> seen = new(StringComparer.OrdinalIgnoreCase);

        List<SceneProp> props = [];

        for (int tick = first; tick <= last; tick += step)
        {
            props.Clear();
            timeline.PropsAt(tick, props);

            HashSet<string> present = [];

            foreach (SceneProp prop in props)
            {
                if (!prop.ModelPath.StartsWith('*'))
                {
                    continue;
                }

                present.Add(prop.ModelPath);

                if (!seen.TryGetValue(prop.ModelPath, out BrushSeen? record))
                {
                    record = new BrushSeen();
                    seen[prop.ModelPath] = record;
                }

                record.Observe(tick, prop);
            }

            foreach (BrushSeen record in seen
                .Where(entry => !present.Contains(entry.Key))
                .Select(entry => entry.Value))
            {
                record.Absent(tick);
            }
        }

        TestContext.Out.WriteLine(
            $"{seen.Count} brush entities over ticks {first}..{last} every {step}");

        foreach ((string model, BrushSeen record) in seen.OrderBy(
            entry => Submodel(entry.Key)))
        {
            TestContext.Out.WriteLine($"{model}: {record.Describe()}");
        }

        // A precondition on the HARNESS: no brush entities at all would make every line above a
        // fact about the walk rather than about the demo.
        seen.Count.ShouldBeGreaterThan(0, "the demo yielded no brush entities at all");
    }

    /// <summary>The number in <c>*N</c>, so the report reads in map order.</summary>
    private static int Submodel(string model) =>
        int.TryParse(model.AsSpan(1), CultureInfo.InvariantCulture, out int index) ? index : 0;

    /// <summary>What one brush entity did across the walk.</summary>
    private sealed class BrushSeen
    {
        private readonly HashSet<string> _angles = [];
        private readonly HashSet<int> _modes = [];
        private readonly HashSet<int> _alphas = [];
        private int _drawn;
        private int _hidden;
        private int _missing;
        private int? _firstMissing;
        private int? _lastDrawn;
        private (float X, float Y, float Z) _low = (float.MaxValue, float.MaxValue, float.MaxValue);
        private (float X, float Y, float Z) _high = (float.MinValue, float.MinValue, float.MinValue);

        public void Observe(int tick, SceneProp prop)
        {
            ScenePose pose = prop.Pose;

            _angles.Add(string.Create(
                CultureInfo.InvariantCulture, $"{pose.Pitch:0.#} {pose.Yaw:0.#} {pose.Roll:0.#}"));

            _modes.Add(pose.RenderMode);
            _alphas.Add(pose.RenderAlpha);

            if (pose.Hidden)
            {
                _hidden++;
            }
            else
            {
                _drawn++;
                _lastDrawn = tick;
            }

            _low = (Math.Min(_low.X, pose.X), Math.Min(_low.Y, pose.Y), Math.Min(_low.Z, pose.Z));
            _high = (Math.Max(_high.X, pose.X), Math.Max(_high.Y, pose.Y), Math.Max(_high.Z, pose.Z));
        }

        /// <summary>The entity was not in the scene at all at this tick.</summary>
        /// <remarks>
        /// **Counted apart from `Hidden`, because they are different events.** An entity told not to
        /// draw is present and refusing; an entity absent from `PropsAt` never reached the scene —
        /// which is what leaving the potentially-visible set looks like. Folding them together is
        /// exactly the conflation that would hide which of the two the owner saw.
        /// </remarks>
        public void Absent(int tick)
        {
            _missing++;
            _firstMissing ??= tick;
        }

        public string Describe() =>
            $"angles [{string.Join(" | ", _angles.Take(4))}] "
            + $"mode [{string.Join(",", _modes.Order())}] "
            + $"alpha [{string.Join(",", _alphas.Order())}] "
            + $"drawn {_drawn} hidden {_hidden} absent {_missing}"
            + (_firstMissing is { } gone ? $" firstAbsent {gone}" : string.Empty)
            + (_lastDrawn is { } shown ? $" lastDrawn {shown}" : string.Empty)
            + $" z {_low.Z:0}..{_high.Z:0}";
    }
}
