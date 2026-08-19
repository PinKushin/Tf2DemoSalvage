using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// Deciding whether a reused entity slot is still the same object.
/// </summary>
/// <remarks>
/// **B92, and the identity rule is the engine's, not ours.** An entity is identified by its index
/// AND its serial number; the engine hands an index to the next object once the last one is gone,
/// and bumps the serial so the two can be told apart. <c>EntityStateTable</c> was already using
/// exactly that a few files away.
///
/// **What was there instead was a hand-rolled rule — compare the model path — and it is wrong in
/// both directions.**
///
/// It cannot see a change that happened: two consecutive rockets in one slot share a model, so the
/// second's positions were appended to the first's track and the pair drew as one object
/// teleporting. That is the case the old comment described, and could not detect.
///
/// And it reports changes that did not happen: an entity may change model while remaining itself.
/// <c>team_control_point.cpp:569</c> calls <c>SetModel( STRING(m_TeamData[m_iTeam].iszModel) )</c>
/// **every time a point is captured**, so a capture point changing hands ended its track and split
/// one object into two. Players changing class do the same.
///
/// So the model comparison is gone rather than kept as a fallback. A proxy for identity that
/// disagrees with the real one in both directions is not a safety net.
///
/// Extracted onto the track so it can be tested at all — the logic previously lived inside a private
/// eight-parameter method with no unit test, which is why a comment was the only statement of intent
/// and nothing checked it against its own example.
///
/// **And there is no fallback**, because the engine's rule needs none here: identity is settled by
/// the state table before a track ever sees a serial. A first draft of this carried a nullable
/// serial with a null-means-continue branch that could not execute.
/// </remarks>
public sealed class TrackIdentityTests
{
    [Test]
    public void TrackIdentity_TheSameSerialInTheSameSlot_IsTheSameObject()
    {
        ScenePropTrack track = new(entityIndex: 7, modelPath: "a.mdl", serialNumber: 3);

        track.Continues(serialNumber: 3).ShouldBeTrue();
    }

    [Test]
    public void TrackIdentity_ADifferentSerial_IsADifferentObject()
    {
        // The case the model check could not see: two rockets, one slot, same model.
        ScenePropTrack track = new(entityIndex: 7, modelPath: "a.mdl", serialNumber: 3);

        track.Continues(serialNumber: 4).ShouldBeFalse();
    }

    [Test]
    public void TrackIdentity_ChangingModel_DoesNotEndATrack()
    {
        // **The other direction, and the one that was silently wrong until now.** A capture point
        // calls SetModel on every capture (team_control_point.cpp:569), so under the old rule every
        // capture ended the track and started a new one — one object drawn as several, each with a
        // fragment of the match.
        //
        // Identity is the serial. The model is what the object currently looks like, which is a
        // property that changes.
        ScenePropTrack track = new(entityIndex: 7, modelPath: "red.mdl", serialNumber: 3);

        track.Continues(serialNumber: 3).ShouldBeTrue();
    }

    [Test]
    public void TrackIdentity_NoUnknownSerial_ExistsToFallBackFrom()
    {
        // **An earlier draft took a nullable serial and treated null as "continue".** That path
        // could never execute: the serial reaching a track comes from an EntityState, whose
        // SerialNumber is a plain int, and the state table has already applied the engine's create
        // rule before handing it over — a serial is compared only on an enter, and a new occupant
        // gets a new state.
        //
        // So identity is settled upstream and the value is authoritative. A fallback here would be
        // dead code wearing the costume of a safety net, and this test records that the type says
        // so rather than leaving the question open for someone to "fix" later.
        typeof(ScenePropTrack).GetProperty(nameof(ScenePropTrack.SerialNumber))!
            .PropertyType.ShouldBe(typeof(int));

        typeof(EntityState).GetProperty(nameof(EntityState.SerialNumber))!
            .PropertyType.ShouldBe(typeof(int));
    }
}
