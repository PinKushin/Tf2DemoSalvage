using System.Collections.Generic;
using System.Numerics;
using System.Threading;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// Every corpse simulated once, straight through, and a seek reading the record (D181).
/// </summary>
/// <remarks>
/// **The control is straight-through live play.** The record's whole claim is that it holds what the viewer would have computed
/// playing forward tick by tick, so each test puts the same corpses through the live path and compares, rather than asserting a
/// number derived a second way.
/// </remarks>
public sealed class CorpseRecordTests
{
    private const int Death = 66;

    private const int End = 132;

    /// <remarks>
    /// **The record equals straight-through play, tick for tick and to the bit** — the same corpse drawn every tick from its death
    /// through the live <see cref="EntityModelSet"/>, against the record's pass over the same prop.
    /// </remarks>
    [Test]
    public void Run_OneCorpse_RecordsWhatStraightThroughPlayComputes()
    {
        SceneProp corpse = CorpsePhysicsWiringTests.Corpse() with { FirstTick = Death };
        CorpseRecord record = Recorded(corpse);

        EntityModelSet live = new() { Geometry = _ => CorpsePhysicsWiringTests.Frames() };
        List<SceneProp> drawn = [corpse];
        live.Add(drawn, _ => CorpsePhysicsWiringTests.Frames());

        for (int tick = Death; tick <= End; tick++)
        {
            live.CurrentTick = tick;
            live.UpdateClientSideAnimations(drawn);
            live.Instances(drawn, [], seconds: tick * (double)live.IntervalPerTick);

            record.TryGet(corpse.EntityIndex, Death, tick, out (Vector3 Position, Quaternion Orientation)[]? state)
                .ShouldBeTrue($"the record covers tick {tick}");
            state![0].Position.ShouldBe(live.Corpses.Roots[corpse.EntityIndex], $"tick {tick}");
        }

        live.Corpses.Roots[corpse.EntityIndex].Z.ShouldBeLessThan(-1f, "the control: the corpse fell, so the ticks differ");
    }

    /// <remarks>
    /// **The record's set forgets what it resolved, tick by tick** (finding 82): it has no world collation, and every
    /// tick hands it a new corpse object, so a resolution kept until a collation swept it was kept for the whole demo —
    /// 0.7 GB of heap on f12 and a GC pause about 1 ms a frame higher. Three hundred ticks hold the last two.
    /// </remarks>
    [Test]
    public void AdvanceCorpsesAlone_ThreeHundredTicksOfNewObjects_HoldsOnlyTheLastTwo()
    {
        EntityModelSet models = new() { Geometry = _ => CorpsePhysicsWiringTests.Frames() };
        SceneProp first = CorpsePhysicsWiringTests.Corpse() with { FirstTick = Death };
        models.Add([first], _ => CorpsePhysicsWiringTests.Frames());

        for (int tick = Death; tick < Death + 300; tick++)
        {
            models.CurrentTick = tick;
            models.AdvanceCorpsesAlone([first with { }], tick * (double)models.IntervalPerTick);
        }

        models.ResolutionsHeld.ShouldBeLessThanOrEqualTo(2);
    }

    /// <remarks>
    /// **A seek anywhere the record has reached reads it: no world is built and nothing is stepped**, and the corpse stands where
    /// the record says.
    /// </remarks>
    [Test]
    public void Advance_WithARecordThatHasReachedTheTick_ReadsItAndStepsNothing()
    {
        SceneProp corpse = CorpsePhysicsWiringTests.Corpse() with { FirstTick = Death };
        CorpseRecord record = Recorded(corpse);

        EntityModelSet models = new() { Geometry = _ => CorpsePhysicsWiringTests.Frames() };
        models.Corpses.Record = record;
        List<SceneProp> drawn = [corpse];
        models.Add(drawn, _ => CorpsePhysicsWiringTests.Frames());

        models.CurrentTick = 100;
        models.Instances(drawn, [], seconds: 100 * (double)models.IntervalPerTick);

        models.Corpses.Rebuilds.ShouldBe(0, "no world was built");
        models.Corpses.Steps.ShouldBe(0, "nothing was stepped");
        record.TryGet(corpse.EntityIndex, Death, 100, out (Vector3 Position, Quaternion Orientation)[]? state).ShouldBeTrue();
        models.Corpses.Roots[corpse.EntityIndex].ShouldBe(state![0].Position, "the corpse stands where the record says");
    }

    /// <remarks>**Past what the record has reached, the live path runs as before** — D179's replay is the fallback.</remarks>
    [Test]
    public void Advance_PastWhatTheRecordHasReached_FallsBackToTheLiveWorld()
    {
        SceneProp corpse = CorpsePhysicsWiringTests.Corpse() with { FirstTick = Death };

        EntityModelSet models = new() { Geometry = _ => CorpsePhysicsWiringTests.Frames() };
        models.Corpses.Record = new CorpseRecord([new RecordedCorpse(corpse, End)]);
        List<SceneProp> drawn = [corpse];
        models.Add(drawn, _ => CorpsePhysicsWiringTests.Frames());

        models.CurrentTick = 100;
        models.Instances(drawn, [], seconds: 100 * (double)models.IntervalPerTick);

        models.Corpses.Rebuilds.ShouldBe(1, "the unrun record left the live world to do it");
        models.Corpses.Steps.ShouldBe(100 - Death);
    }

    /// <remarks>
    /// **Each corpse carries its own resolved prop to its next tick** (B574, finding 90): the record hands a corpse back as
    /// the last tick resolved it, so its sequence is not copied whole every tick. Two corpses far apart, the second
    /// listed first and dying later, so the alive list's order and its indices into the record differ for five ticks:
    /// a write-back to the wrong corpse moves one onto the other's prop. The record must equal straight-through play of
    /// the same two, whose drawn list keeps what each tick resolved, as the live frame's does.
    /// </remarks>
    [Test]
    public void Run_TwoCorpsesDyingApart_RecordsWhatStraightThroughPlayComputes()
    {
        SceneProp early = CorpsePhysicsWiringTests.Corpse() with { FirstTick = Death };
        SceneProp late = early with
        {
            EntityIndex = early.EntityIndex + 1,
            FirstTick = Death + 5,
            Pose = early.Pose with { X = early.Pose.X + 2000f },
        };

        CorpseRecord record = new([new RecordedCorpse(late, End), new RecordedCorpse(early, End)]);
        record.Run(_ => CorpsePhysicsWiringTests.Frames(), createWorld: null, surfaces: new([]), intervalPerTick: 1f / 66f, CancellationToken.None);

        EntityModelSet live = new() { Geometry = _ => CorpsePhysicsWiringTests.Frames() };
        live.Add([early, late], _ => CorpsePhysicsWiringTests.Frames());
        List<SceneProp> drawn = [early];

        for (int tick = Death; tick <= End; tick++)
        {
            if (tick == late.FirstTick)
            {
                drawn.Insert(0, late);
            }

            live.CurrentTick = tick;
            live.UpdateClientSideAnimations(drawn);
            live.Instances(drawn, [], seconds: tick * (double)live.IntervalPerTick);

            foreach (SceneProp corpse in drawn)
            {
                record.TryGet(corpse.EntityIndex, corpse.FirstTick!.Value, tick, out (Vector3 Position, Quaternion Orientation)[]? state)
                    .ShouldBeTrue($"the record covers entity {corpse.EntityIndex} at tick {tick}");
                state![0].Position.ShouldBe(live.Corpses.Roots[corpse.EntityIndex], $"entity {corpse.EntityIndex} at tick {tick}");
            }
        }
    }

    /// <summary>A record run to completion over one corpse, living from its death to <see cref="End"/>.</summary>
    private static CorpseRecord Recorded(SceneProp corpse)
    {
        CorpseRecord record = new([new RecordedCorpse(corpse, End)]);
        record.Run(_ => CorpsePhysicsWiringTests.Frames(), createWorld: null, surfaces: new([]), intervalPerTick: 1f / 66f, CancellationToken.None);
        record.Reached.ShouldBe(End, "the control: the pass ran to the corpse's end");
        return record;
    }
}
