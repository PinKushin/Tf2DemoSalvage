using System.Collections.Generic;
using System.Numerics;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// `CFleckParticles::Create` (`fx_fleck.cpp:134`): a new burst of flecks joins the newest live `FX_DebrisFlecks` emitter whose
/// bounding box, grown by the new spawn ± 5, stays under 120 across — and `CreateFleckParticles` then sets that emitter's
/// collision up again at the new spawn, so the older flecks bounce off the newer impact's planes.
/// </summary>
public sealed class FleckMergeConformanceTests
{
    [Test]
    public void Join_ANewBurstNearALiveOne_FoldsIntoItAndTakesItsCollision()
    {
        ImpactEffect older = Burst(new Vector3(0f, 0f, 0f), 3);
        ImpactEffect newer = Burst(new Vector3(20f, 0f, 0f), 2);
        ImpactParticleCollision newerCollision = newer.Emitters[0].Collision;

        FleckMerge.Join(newer, [older]);

        older.Emitters[0].Particles.Count.ShouldBe(5);
        older.Emitters[0].Collision.ShouldBeSameAs(newerCollision);
        older.Emitters[0].Maxs.X.ShouldBe(25f);
        newer.Emitters.ShouldBeEmpty();
    }

    /// <remarks>200 units apart: the grown box is far past 120 across, so the new burst keeps its own emitter.</remarks>
    [Test]
    public void Join_ANewBurstFarFromTheLiveOne_KeepsItsOwnEmitter()
    {
        ImpactEffect older = Burst(new Vector3(0f, 0f, 0f), 3);
        ImpactEffect newer = Burst(new Vector3(200f, 0f, 0f), 2);

        FleckMerge.Join(newer, [older]);

        older.Emitters[0].Particles.Count.ShouldBe(3);
        newer.Emitters.Count.ShouldBe(1);
    }

    /// <remarks>An emitter whose flecks have all gone has left the merge list with them.</remarks>
    [Test]
    public void Join_AnOlderEmitterWithNoFlecksLeft_IsNotJoined()
    {
        ImpactEffect older = Burst(new Vector3(0f, 0f, 0f), 0);
        ImpactEffect newer = Burst(new Vector3(20f, 0f, 0f), 2);

        FleckMerge.Join(newer, [older]);

        newer.Emitters.Count.ShouldBe(1);
    }

    /// <remarks>The list is walked from its head, and a new emitter is added at the head: the newest live one wins.</remarks>
    [Test]
    public void Join_TwoLiveEmittersInReach_JoinsTheNewest()
    {
        ImpactEffect oldest = Burst(new Vector3(0f, 0f, 0f), 1);
        ImpactEffect middle = Burst(new Vector3(10f, 0f, 0f), 1);
        ImpactEffect newer = Burst(new Vector3(20f, 0f, 0f), 1);

        FleckMerge.Join(newer, [middle, oldest]);

        middle.Emitters[0].Particles.Count.ShouldBe(2);
        oldest.Emitters[0].Particles.Count.ShouldBe(1);
    }

    private static ImpactEffect Burst(Vector3 spawn, int flecks)
    {
        ImpactEmitter emitter = new(ImpactEmitterKind.Fleck) { Mins = spawn - new Vector3(5f), Maxs = spawn + new Vector3(5f) };

        for (int i = 0; i < flecks; i++)
        {
            emitter.Particles.Add(new ImpactParticle("effects/fleck_cement1", spawn, Vector3.UnitZ, 3f));
        }

        ImpactEffect effect = new();

        effect.Emitters.Add(emitter);

        return effect;
    }
}
