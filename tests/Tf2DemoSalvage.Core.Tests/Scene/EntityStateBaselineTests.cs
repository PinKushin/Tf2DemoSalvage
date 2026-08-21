using System.Collections.Generic;

using Tf2DemoSalvage.Core.Schema;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Core.Tests.Schema;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// The accumulator against instance baselines: what an entering entity actually is.
/// </summary>
/// <remarks>
/// **B132.** <c>EntityStateTable.Apply</c> wrote <c>DecodedEntity.Properties</c> into the state, and
/// that member is wire-faithful by design — exactly the bits the snapshot carried, which is what the
/// assembler must reproduce. An entity entering the visible set is a delta against its class's
/// instance baseline and omits everything equal to it, so for state the wire list is the wrong
/// question. The engine merges the baseline first, in <c>CL_CopyNewEntity</c>.
///
/// **The defect was invisible for as long as it was measured on players.** A player resends origin,
/// health and team constantly, so the baseline supplies only values that arrive again within a
/// second; the accumulated state converges either way. An entity whose whole state IS its baseline
/// never converges: a <c>CFogController</c> enters once at tick 1 carrying fifteen properties, none
/// of them on the wire, and is never mentioned again. It sat in the entity table of every demo in
/// the corpus with its class name and nothing else.
///
/// **A real decoder and a real encoded baseline, not a stub.** A fake source returning a fixed list
/// would prove the table calls it and nothing about whether the merge is right — the two halves
/// would have been written by the same hand to agree.
/// </remarks>
public sealed class EntityStateBaselineTests
{
    private const string Health = BaselineFixture.Table + ".m_iHealth";
    private const string Ammo = BaselineFixture.Table + ".m_iAmmo";

    [Test]
    public void Apply_AnEnterThatSentNothing_TakesItsStateFromTheClassBaseline()
    {
        // **The whole finding in one assertion.** The snapshot carries no properties at all, so
        // every value below can only have come from the baseline. This is the shape a fog
        // controller arrives in.
        EntityDecoder decoder = BaselineFixture.WithBaseline(("m_iHealth", 125), ("m_iAmmo", 32));
        EntityStateTable table = new(decoder);

        table.Apply(Enter([]));

        table.TryGet(1, out EntityState? state).ShouldBeTrue();
        state.Properties.Count.ShouldBe(2);
        state.Integer(Health).ShouldBe(125);
        state.Integer(Ammo).ShouldBe(32);
    }

    [Test]
    public void Apply_WithoutABaselineSource_KeepsOnlyWhatTheSnapshotCarried()
    {
        // **The contrast that makes the test above measure the merge rather than the fixture.**
        // Same entity, same empty snapshot, a source that knows no baselines — and the state is
        // empty. This is exactly what the table did before B132 was fixed, which is why it is
        // written down rather than assumed.
        EntityStateTable table = new(EntityBaselines.None);

        table.Apply(Enter([]));

        table.TryGet(1, out EntityState? state).ShouldBeTrue();
        state.Properties.ShouldBeEmpty();
    }

    [Test]
    public void Apply_AnEnterThatRestatesABaselineProperty_PrefersTheSnapshot()
    {
        // Merge direction. The baseline is what the entity would be if it said nothing; anything
        // it did say replaces that. Reversed, an entering player would hold the class default for
        // every property they actually sent — a full-health scout who is really on 12.
        EntityDecoder decoder = BaselineFixture.WithBaseline(("m_iHealth", 125), ("m_iAmmo", 32));
        EntityStateTable table = new(decoder);

        table.Apply(Enter(BaselineFixture.Properties(("m_iHealth", 70))));

        table.TryGet(1, out EntityState? state).ShouldBeTrue();

        // The snapshot won where it spoke.
        state.Integer(Health).ShouldBe(70);

        // And the baseline still filled in where it did not.
        state.Integer(Ammo).ShouldBe(32);
    }

    [Test]
    public void Apply_ASecondDeltaAfterAnEnter_DoesNotResurrectTheBaselineValue()
    {
        // **A delta is not merged, and this is the input where correct and broken differ.** A
        // single delta cannot tell them apart: the merge puts the snapshot last, so health would
        // read 70 either way. It takes a SECOND delta that touches something else — with baseline
        // merging on every update, that one re-applies m_iHealth 125 over the accumulated 70 and
        // the entity silently reverts to a value the server replaced.
        EntityDecoder decoder = BaselineFixture.WithBaseline(("m_iHealth", 125), ("m_iAmmo", 32));
        EntityStateTable table = new(decoder);

        table.Apply(Enter([]));
        table.Apply(Delta(BaselineFixture.Properties(("m_iHealth", 70))));
        table.Apply(Delta(BaselineFixture.Properties(("m_iAmmo", 5))));

        table.TryGet(1, out EntityState? state).ShouldBeTrue();
        state.Integer(Health).ShouldBe(70);
        state.Integer(Ammo).ShouldBe(5);
    }

    private static DecodedEntity Enter(IReadOnlyList<DecodedProperty> properties) =>
        new(1, BaselineFixture.ClassId, SerialNumber: 7, EntityUpdateType.Enter, properties);

    private static DecodedEntity Delta(IReadOnlyList<DecodedProperty> properties) =>
        new(1, BaselineFixture.ClassId, SerialNumber: 0, EntityUpdateType.Delta, properties);
}
