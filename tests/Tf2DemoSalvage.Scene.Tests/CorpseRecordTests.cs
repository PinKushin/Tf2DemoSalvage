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

    /// <summary>A record run to completion over one corpse, living from its death to <see cref="End"/>.</summary>
    private static CorpseRecord Recorded(SceneProp corpse)
    {
        CorpseRecord record = new([new RecordedCorpse(corpse, End)]);
        record.Run(_ => CorpsePhysicsWiringTests.Frames(), createWorld: null, surfaces: new([]), intervalPerTick: 1f / 66f, CancellationToken.None);
        record.Reached.ShouldBe(End, "the control: the pass ran to the corpse's end");
        return record;
    }
}
