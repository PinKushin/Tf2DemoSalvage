using Tf2DemoSalvage.Core.Schema;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// <c>m_flSimulationTime</c>, which is a tick offset rather than a time.
/// </summary>
/// <remarks>
/// **The value on the wire is eight bits and means nothing without the tick it arrived on.**
/// <c>server/baseentity.cpp:265</c>:
///
/// <code>
/// SendPropInt( SENDINFO(m_flSimulationTime), SIMULATION_TIME_WINDOW_BITS,
///              SPROP_UNSIGNED|SPROP_CHANGES_OFTEN|SPROP_ENCODED_AGAINST_TICKCOUNT,
///              SendProxy_SimulationTime ),
/// </code>
///
/// with the proxy (<c>baseentity.cpp:132</c>) sending the offset from a per-entity base:
///
/// <code>
/// int ticknumber = TIME_TO_TICKS( pEntity->m_flSimulationTime );
/// int tickbase = gpGlobals->GetNetworkBase( gpGlobals->tickcount, pEntity->entindex() );
/// int addt = 0;
/// if ( ticknumber >= tickbase ) addt = ( ticknumber - tickbase ) &amp; 0xff;
/// </code>
///
/// and the client (<c>c_baseentity.cpp:344</c>) inverting it, then RE-CENTRING the result within
/// ±127 ticks of now — which is what makes an eight-bit field able to name a tick at all.
///
/// **The base is per-entity on purpose.** <c>GetNetworkBase</c> (<c>globalvars_base.h:95</c>) is
/// <c>100 * floor( (tick − entindex % 32) / 100 )</c>, and Valve's comment beside
/// <c>nTimestampRandomizeWindow</c> says why: it "prevents them from getting lockstepped", spreading
/// the moment when every entity's offset wraps at once. An implementation that used a plain
/// <c>tick / 100</c> would agree for entity 0 and drift for the other 31 residues — which is a
/// difference no synthetic single-entity fixture would catch, so the cases below vary the index.
///
/// **Why this matters here at all**: the engine timestamps an entity's interpolation history with
/// this value, not with the tick the packet arrived on
/// (<c>C_BaseEntity::GetLastChangeTime</c>, <c>c_baseentity.cpp</c>), and this project stamps every
/// keyframe with the packet tick. Whether that is a divergence is a question about real demos, and
/// answering it needs this arithmetic first (B273).
/// </remarks>
public sealed class SimulationTimeConformanceTests
{
    [Test]
    public void NetworkBase_ForEntityZero_IsTheHundredTickBoundaryBelow()
    {
        EntityState.NetworkBase(tick: 1234, entityIndex: 0).ShouldBe(1200);
    }

    /// <remarks>
    /// **The entity index shifts the boundary, which is the whole point of the randomisation.**
    /// Entity 33 has residue 1, so the division is of 1233 rather than 1234 — the same answer here,
    /// and the case below is where it is not.
    /// </remarks>
    [Test]
    public void NetworkBase_JustAfterABoundary_ShiftsWithTheEntityIndex()
    {
        // Tick 1201, entity 0: floor(1201/100)*100 = 1200.
        EntityState.NetworkBase(tick: 1201, entityIndex: 0).ShouldBe(1200);

        // Entity 5 has residue 5, so it divides 1196 and lands a whole base EARLIER.
        EntityState.NetworkBase(tick: 1201, entityIndex: 5).ShouldBe(1100);
    }

    /// <remarks>
    /// The window is 32, so entity 32 behaves as entity 0 does — asserted because a modulus written
    /// against the wrong constant (256, or the entity count) is invisible at low indices.
    /// </remarks>
    [Test]
    public void NetworkBase_ForEntity32_MatchesEntityZero()
    {
        EntityState.NetworkBase(tick: 1201, entityIndex: 32)
            .ShouldBe(EntityState.NetworkBase(tick: 1201, entityIndex: 0));
    }

    [Test]
    public void SimulationTick_ForAnEntitySimulatingOnThePacketTick_IsThePacketTick()
    {
        // The ordinary case, and the one that decides whether this project's keyframe stamping
        // diverges at all: an entity that simulated this tick sends the offset that decodes to it.
        Round(tick: 1234, entityIndex: 7, simulated: 1234).ShouldBe(1234);
    }

    [Test]
    public void SimulationTick_ForAnEntityThatSimulatedEarlier_IsTheEarlierTick()
    {
        Round(tick: 1234, entityIndex: 7, simulated: 1230).ShouldBe(1230);
    }

    /// <remarks>
    /// **Across a base boundary**, where the offset wraps and the re-centring has to undo it. An
    /// implementation without the <c>while</c> loops answers 256 ticks out — six seconds, which is
    /// not subtle, but only for entities whose base is on the far side of the boundary.
    /// </remarks>
    [Test]
    public void SimulationTick_AcrossABaseBoundary_IsStillTheSimulatedTick()
    {
        Round(tick: 1301, entityIndex: 3, simulated: 1298).ShouldBe(1298);
    }

    /// <remarks>
    /// Every entity index in the randomisation window, at a tick chosen to sit just past a boundary
    /// so the residues straddle it. This is the control against a `tick / 100` that agrees for
    /// entity 0: without the residue, half of these come back a hundred ticks wrong.
    /// </remarks>
    [Test]
    public void SimulationTick_ForEveryEntityInTheWindow_RoundTrips()
    {
        for (int entity = 0; entity < 64; entity++)
        {
            Round(tick: 1305, entityIndex: entity, simulated: 1303).ShouldBe(
                1303, $"entity {entity} must decode to the tick it simulated on");
        }
    }

    /// <remarks>
    /// **<c>m_flAnimTime</c> is the same encoding on a different table**, and the client's proxy is
    /// byte-identical to the simulation one — <c>RecvProxy_AnimTime</c>
    /// (<c>c_baseentity.cpp:316</c>) against <c>RecvProxy_SimulationTime</c>
    /// (<c>:344</c>). Only the SEND guards differ, and a decoder never runs those:
    /// <c>SendProxy_AnimTime</c> encodes when <c>ticknumber >= tickbase - 100</c> where the
    /// simulation one wants <c>>= tickbase</c>.
    ///
    /// **It lives in its own table**, <c>DT_AnimTimeMustBeFirst</c>, named for the ordering the
    /// engine needs — and confirmed against a real demo rather than assumed, since three other
    /// tables also declare an <c>m_flAnimTime</c>: the flattened <c>CObjectSentrygun</c> on the
    /// 2013 foundry recording carries <c>DT_AnimTimeMustBeFirst.m_flAnimTime</c>.
    /// </remarks>
    [Test]
    public void AnimationTick_ForAnEntityAnimatingEarlier_IsTheEarlierTick()
    {
        EntityState state = new(entityIndex: 7, 0, 0, "CObjectSentrygun");

        int tickbase = EntityState.NetworkBase(tick: 1234, entityIndex: 7);

        state.Set(
            "DT_AnimTimeMustBeFirst.m_flAnimTime",
            PropertyValue.FromInt((1230 - tickbase) & 0xff));

        state.NoteTickEncodedTimes(1234);

        state.AnimatedAtTick.ShouldBe(1230);
    }

    /// <remarks>
    /// **The control that keeps the two apart.** They share a decode and are read from different
    /// tables, so an implementation that keyed both off one property would pass every case above —
    /// this is the one where the two values differ and each must come back as itself.
    /// </remarks>
    [Test]
    public void AnimationAndSimulationTicks_WhenTheyDiffer_AreEachTheirOwn()
    {
        EntityState state = new(entityIndex: 7, 0, 0, "CObjectSentrygun");

        int tickbase = EntityState.NetworkBase(tick: 1234, entityIndex: 7);

        state.Set(
            "DT_BaseEntity.m_flSimulationTime", PropertyValue.FromInt((1234 - tickbase) & 0xff));
        state.Set(
            "DT_AnimTimeMustBeFirst.m_flAnimTime", PropertyValue.FromInt((1226 - tickbase) & 0xff));

        state.NoteTickEncodedTimes(1234);

        state.SimulatedAtTick.ShouldBe(1234);
        state.AnimatedAtTick.ShouldBe(1226);
    }

    /// <remarks>
    /// An entity that sends one and not the other is ordinary — a resting prop simulates without
    /// animating, and a client-side-animated player sends no meaningful anim time at all
    /// (<c>SendProxy_AnimTime</c> asserts <c>!IsUsingClientSideAnimation()</c>). Null must stay
    /// null rather than falling back to the other, which would be a plausible number from the
    /// wrong clock.
    /// </remarks>
    [Test]
    public void AnimationTick_WhenOnlySimulationWasSent_StaysUnknown()
    {
        EntityState state = new(entityIndex: 7, 0, 0, "CObjectSentrygun");

        state.Set("DT_BaseEntity.m_flSimulationTime", PropertyValue.FromInt(0));
        state.NoteTickEncodedTimes(1234);

        state.SimulatedAtTick.ShouldNotBeNull();
        state.AnimatedAtTick.ShouldBeNull();
    }

    /// <summary>Encodes as the server does, then decodes as this project must.</summary>
    /// <remarks>
    /// **A round trip rather than a table of expected bytes**, because the encoding is the half
    /// this project never performs — so a fixture asserting the offset would be asserting our own
    /// reading of the proxy twice. Encoding here is Valve's three lines, transcribed in the test
    /// where they cannot be mistaken for production code.
    /// </remarks>
    private static int Round(int tick, int entityIndex, int simulated)
    {
        int tickbase = EntityState.NetworkBase(tick, entityIndex);
        int addt = simulated >= tickbase ? (simulated - tickbase) & 0xff : 0;

        EntityState state = new(entityIndex, 0, 0, "CBaseEntity");

        state.Set("DT_BaseEntity.m_flSimulationTime", PropertyValue.FromInt(addt));
        state.NoteTickEncodedTimes(tick);

        return state.SimulatedAtTick ?? int.MinValue;
    }
}
