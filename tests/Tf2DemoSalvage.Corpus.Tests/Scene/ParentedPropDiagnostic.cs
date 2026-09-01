using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;

// Namespaced away from `Tf2DemoSalvage.Corpus.Tests.*`, where `Corpus` binds to the namespace
// rather than to the helper class — the same reason `CorpusPlayerOriginTests` beside it does.
namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// Whether the grate props reach the scene at all, and what happens to them — a MEASUREMENT.
/// </summary>
/// <remarks>
/// **This exists to TEST a hypothesis rather than to illustrate one**, on the owner's direction:
/// *"the parenting finding still stands, and i want you to check to see if thats the problem, by
/// looking harder for any diversions from valve"*. The previous round of this investigation merged
/// on an inference and had to be reverted, so the claim gets a measurement before any code moves.
///
/// **The hypothesis.** `cp_fulgur`'s gates are pairs: an invisible `func_door`
/// (<c>rendermode 10</c>) doing the motion, and a visible <c>prop_dynamic</c> —
/// <c>models/props_gameplay/door_grate003_top.mdl</c> and <c>..._bottom.mdl</c> — <b>parented</b> to
/// it. `C_BaseEntity::CalcAbsolutePosition` (<c>c_baseentity.cpp:4350</c>) has three branches in
/// order: unparented takes local, <c>EF_BONEMERGE</c> calls <c>MoveToAimEnt</c>, and everything else
/// concatenates the parent's transform with an entity-to-parent matrix built from the child's LOCAL
/// angles and origin. This project implements the first two and treats "has a parent" as "is
/// bone-merged", so the third case has no home.
///
/// **What would confirm it**: the grate props are present in the recording, carry a move parent, and
/// do not reach the drawn scene. **What would kill it**: they are absent from the demo entirely (the
/// gates are then not entities at all), or they are present and drawn (the fault is elsewhere).
///
/// Explicit, and it asserts nothing about what the demo contains (D38) beyond the precondition that
/// the walk ran.
/// </remarks>
[Explicit("Diagnostic: reports whether parented gate props reach the scene.")]
public sealed class ParentedPropDiagnostic
{
    /// <summary>The recording the owner was watching.</summary>
    private const string Recording = "tf2-2026-pub-pov-clean";

    /// <summary>The models under investigation.</summary>
    /// <remarks>
    /// The three the map parents to its gate movers, and <c>resupply_locker</c>, which it does NOT
    /// parent — `SpawnRoomEntityProbe` reads all eight of them out of `cp_fulgur`'s entity lump as
    /// `[unparented]`. The cabinet is on this list precisely because it should never appear in the
    /// transform branch below, so its presence there is a defect the same report can show.
    /// </remarks>
    private static readonly string[] Gates =
    [
        "door_grate003",
        "door_slide_large_door",
        "windowed_door",
        "resupply_locker",
    ];

    /// <summary>Ticks to sample across the demo.</summary>
    private const int Samples = 300;

    [Test]
    public void Decode_TheParentedGateProps_ReportsWhetherTheyReachTheScene()
    {
        string path = Corpus.Demo(Recording);

        DemoTimeline timeline = TimelineCache.For(path);

        int first = timeline.FirstTick;
        int last = timeline.LastTick;
        int step = Math.Max(1, (last - first) / Samples);

        Dictionary<string, GateSeen> seen = new(StringComparer.OrdinalIgnoreCase);

        List<SceneProp> props = [];

        for (int tick = first; tick <= last; tick += step)
        {
            props.Clear();
            timeline.PropsAt(tick, props);

            foreach (SceneProp prop in props)
            {
                if (!Gates.Any(gate =>
                    prop.ModelPath.Contains(gate, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                string key = $"{prop.EntityIndex} {prop.ModelPath}";

                if (!seen.TryGetValue(key, out GateSeen? record))
                {
                    record = new GateSeen();
                    seen[key] = record;
                }

                record.Observe(prop);
            }
        }

        TestContext.Out.WriteLine(
            $"{seen.Count} gate props reached PropsAt over ticks {first}..{last} every {step}");

        foreach ((string key, GateSeen record) in seen.OrderBy(
            entry => entry.Key, StringComparer.Ordinal))
        {
            TestContext.Out.WriteLine($"  {key}: {record.Describe()}");
        }

        // **The other half, and without it an empty result above has two readings.** "No gate prop
        // reached the scene" means either the demo never carried one or the scene dropped it, and
        // those are opposite conclusions. Every model path the timeline knows about is counted here,
        // so a gate model present in the demo but absent from the scene is visible as a difference
        // between the two lists.
        List<string> models = [.. timeline.ModelPaths()];

        List<string> gateModels =
        [
            .. models.Where(model =>
                Gates.Any(gate => model.Contains(gate, StringComparison.OrdinalIgnoreCase))),
        ];

        TestContext.Out.WriteLine(
            $"{gateModels.Count} gate models are named by the demo at all: "
            + string.Join(", ", gateModels));

        TestContext.Out.WriteLine($"{models.Count} model paths in the demo overall");

        // **Everything the transform branch actually claims, by model.** Three attempts at this
        // change have broken something visual, and every time the cause was an entity taking the
        // new branch that should not have. A list of what it takes is the only thing that can be
        // checked BEFORE looking at a screen — the viewmodel and its weapon must not be on it.
        Dictionary<string, int> byTransform = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, int> byBones = new(StringComparer.OrdinalIgnoreCase);

        for (int tick = first; tick <= last; tick += step)
        {
            props.Clear();
            timeline.PropsAt(tick, props);

            foreach (SceneProp prop in props)
            {
                if (prop.AttachedTo is null)
                {
                    continue;
                }

                Dictionary<string, int> into = prop.BoneMerged ? byBones : byTransform;

                into[prop.ModelPath] = into.GetValueOrDefault(prop.ModelPath) + 1;
            }
        }

        TestContext.Out.WriteLine(
            $"TRANSFORM branch claims {byTransform.Count} distinct models:");

        foreach ((string model, int times) in byTransform.OrderBy(
            entry => entry.Key, StringComparer.Ordinal))
        {
            TestContext.Out.WriteLine($"    XFORM {model} x{times}");
        }

        TestContext.Out.WriteLine($"BONE branch claims {byBones.Count} distinct models");

        // **Where each transform-parented prop's PARENT actually is.** The standing hypothesis is
        // that a brush entity's geometry is already in world space and its `m_vecOrigin` stays
        // (0,0,0) until it moves — in which case composing a child onto it puts the child at the
        // map origin, which is exactly "loads, instances, invisible". A parent sitting at a real
        // world position kills that hypothesis outright and sends the search elsewhere.
        // **Paired AT THE SAME TICK, which the first attempt at this was not.** Entity slots are
        // reused, so a map of "last pose seen per index" across a whole demo reports whoever
        // occupied the slot last — which made a spawn grate look parented to a medigun. A parent
        // has to be looked up in the same snapshot as its child or the answer is about two
        // different moments.
        HashSet<string> pairs = new(StringComparer.Ordinal);

        for (int tick = first; tick <= last; tick += step)
        {
            props.Clear();
            timeline.PropsAt(tick, props);

            Dictionary<int, SceneProp> here = [];

            foreach (SceneProp prop in props)
            {
                here[prop.EntityIndex] = prop;
            }

            foreach (SceneProp prop in props)
            {
                if (prop is not { AttachedTo: { } parent, BoneMerged: false })
                {
                    continue;
                }

                string where = here.TryGetValue(parent, out SceneProp? found)
                    ? $"{found.ModelPath} at ({found.Pose.X:0} {found.Pose.Y:0} {found.Pose.Z:0})"
                    : "ABSENT FROM THIS TICK";

                pairs.Add($"  {prop.ModelPath} -> {parent}: {where}");
            }
        }

        // **Not truncated.** A `Take(14)` here hid every `resupply_locker` pairing behind the door
        // models that sort before it, which is the exact evidence this report exists to show. A cap
        // that silently drops the tail of an ordered list drops whatever sorts last.
        foreach (string pair in pairs.Order(StringComparer.Ordinal))
        {
            TestContext.Out.WriteLine("SAMETICK" + pair);
        }

        // A precondition on the HARNESS, not a claim about the demo.
        models.Count.ShouldBeGreaterThan(0, "the demo named no models at all");
    }

    /// <summary>What one gate prop did across the walk.</summary>
    private sealed class GateSeen
    {
        private readonly HashSet<string> _origins = [];
        private readonly HashSet<int?> _parents = [];
        private readonly HashSet<int> _modes = [];
        private int _drawn;
        private int _hidden;

        public void Observe(SceneProp prop)
        {
            ScenePose pose = prop.Pose;

            _origins.Add($"({pose.X:0} {pose.Y:0} {pose.Z:0}) yaw {pose.Yaw:0}");
            _parents.Add(prop.AttachedTo);
            _modes.Add(pose.RenderMode);

            if (pose.Hidden)
            {
                _hidden++;
            }
            else
            {
                _drawn++;
            }
        }

        public string Describe() =>
            $"drawn {_drawn} hidden {_hidden} "
            + $"parent [{string.Join(",", _parents.Select(parent => parent?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "none"))}] "
            + $"mode [{string.Join(",", _modes.Order())}] "
            + $"at [{string.Join(" | ", _origins.Take(3))}]";
    }
}
