using System.Numerics;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// The parallel attribute streams one particle system's live particles are kept in (B373).
/// </summary>
/// <remarks>
/// **These test the INVARIANT rather than any one attribute**: the streams are separate arrays that
/// must move together, so the failure this shape invites is a new stream wired into two of
/// <c>Add</c>, <c>Reap</c> and <c>Grow</c> instead of all three. Nothing throws when that happens —
/// one particle simply wears another's lifetime or plays another's animation.
/// </remarks>
[TestFixture]
public sealed class ParticleStoreConformanceTests
{
    [Test]
    public void Reap_ADeadParticleInTheMiddle_MovesEveryStreamOfTheSurvivorTogether()
    {
        // Three particles with distinguishable attributes on every stream. The first dies, so the
        // LAST is swapped into slot 0 — and every one of its attributes must arrive with it. A
        // stream missing from `Reap`'s swap leaves the dead particle's value behind, which reads as
        // the survivor having changed sequence or lifetime at the moment a neighbour died.
        ParticleStore store = new();

        store.Add(new Vector3(1f, 0f, 0f), lives: 0.5f);
        store.Add(new Vector3(2f, 0f, 0f), lives: 100f);
        store.Add(new Vector3(3f, 0f, 0f), lives: 100f);

        store.PlaySequence(0, 7);
        store.PlaySequence(1, 8);
        store.PlaySequence(2, 9);

        store.Resize(2, 42f);
        store.Fade(2, 0.25f);

        int third = store.IdOf(2);

        store.Tick(1f);

        store.Reap().ShouldBe(1);
        store.Count.ShouldBe(2);

        // Slot 0 is now the particle that was in slot 2, on every stream at once.
        store.PositionOf(0).X.ShouldBe(3f);
        store.SequenceOf(0).ShouldBe(9);
        store.RadiusOf(0).ShouldBe(42f);
        store.AlphaOf(0).ShouldBe(0.25f);
        store.LifetimeOf(0).ShouldBe(100f);
        store.IdOf(0).ShouldBe(third);
    }

    [Test]
    public void Add_PastTheInitialCapacity_KeepsEveryStreamTheSameLength()
    {
        // The streams start at 64, so this crosses `Grow` twice. A stream left out of `Grow` throws
        // IndexOutOfRange on the next Add — which is the loud failure; the quiet one is a stream
        // that grows but does not COPY, and the assertions below read values written before the
        // resize.
        ParticleStore store = new();

        for (int index = 0; index < 200; index++)
        {
            int at = store.Add(new Vector3(index, 0f, 0f), lives: 100f);

            store.PlaySequence(at, index);
            store.Resize(at, index + 1);
        }

        store.Count.ShouldBe(200);

        // The first particle, written when every array was 64 long and copied twice since.
        store.PositionOf(0).X.ShouldBe(0f);
        store.SequenceOf(0).ShouldBe(0);
        store.RadiusOf(0).ShouldBe(1f);

        store.SequenceOf(199).ShouldBe(199);
        store.RadiusOf(199).ShouldBe(200f);
    }

    [Test]
    public void IdOf_ASlotReusedAfterAReap_IsNotTheDeadParticlesId()
    {
        // **Why the id is a stream and not the index.** Every draw an initializer makes is keyed on
        // the id, so a new particle landing in a dead one's slot would inherit its lifetime and its
        // sequence — and a churning trail would visibly repeat itself.
        ParticleStore store = new();

        store.Add(Vector3.Zero, lives: 0.5f);

        int first = store.IdOf(0);

        store.Tick(1f);
        store.Reap().ShouldBe(1);

        store.Add(Vector3.Zero, lives: 100f);

        store.IdOf(0).ShouldNotBe(first);
    }

    [Test]
    public void AgeOf_AParticleBornLate_CountsFromItsOwnBirthAndNotTheSystems()
    {
        // The sheet clock is `age × rate`, so an age measured from the SYSTEM's start would put a
        // newborn particle deep into an animation the moment a long-running trail spawned it.
        ParticleStore store = new();

        store.Tick(5f);
        store.Add(Vector3.Zero, lives: 100f);

        store.AgeOf(0).ShouldBe(0f);

        store.Tick(0.25f);

        store.AgeOf(0).ShouldBe(0.25f, 0.0001d);
    }
}
