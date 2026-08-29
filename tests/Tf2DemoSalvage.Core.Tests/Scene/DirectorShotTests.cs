using System.Collections.Generic;

using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// The director's <c>hltv_chase</c> shots reach the timeline.
/// </summary>
/// <remarks>
/// **Authored, because no demo within reach carries the event.** Measured before writing this:
/// `hltv_chase` and `hltv_status` appear zero times in either the 2013 badlands specimen or
/// `cp_process_f12`, while `player_death` appears in both — so the absence is a fact about the
/// recordings rather than about this reader. Both are point-of-view demos, and only a SourceTV
/// recording has a director.
///
/// That is what the writer is for: `docs/memory/author-the-specimen-the-corpus-lacks.md`. A feature
/// the corpus cannot exercise is not a feature that goes untested; it is one whose input has to be
/// written.
///
/// **What this can and cannot establish.** It shows the event is decoded, carried to the timeline,
/// and answered per tick with the right carry-forward. It cannot show that a REAL director's events
/// look like these — the field names come from `C_HLTVCamera::FireGameEvent`, and if the wire
/// spelled one differently this would pass and the feature would still be dead. Only a SourceTV
/// demo settles that, and the corpus has none.
/// </remarks>
public sealed class DirectorShotTests
{
    [Test]
    public void DirectorAt_AfterAChaseEvent_ReportsWhatTheDirectorAsked()
    {
        DemoTimeline timeline = Watching(
            Chase(target: 3, second: 7, distance: 150f, theta: 45f, phi: 20f));

        // Asked first, because "the event never arrived" and "the tick lookup missed it" are
        // different faults and the null below cannot tell them apart.
        timeline.HasDirector.ShouldBeTrue("the hltv_chase event should have reached the timeline");

        DirectorShot shot = timeline.DirectorAt(timeline.LastTick).ShouldNotBeNull();

        shot.Target.ShouldBe(3);
        shot.SecondTarget.ShouldBe(7);
        shot.Distance.ShouldBe(150f, 0.01f);
        shot.Theta.ShouldBe(45f, 0.01f);
        shot.Phi.ShouldBe(20f, 0.01f);
    }

    [Test]
    public void DirectorAt_BeforeTheFirstShot_IsNull()
    {
        // **A demo with no director must answer nothing, not a default.** Answering
        // `DirectorShot.Default` would make "the director asked for 96 units" and "there is no
        // director" the same value, and the chase camera would have no way to tell a SourceTV
        // recording from a point-of-view one.
        Watching().DirectorAt(0).ShouldBeNull();
    }

    [Test]
    public void DirectorAt_ForAFieldTheEventDoesNotDeclare_KeepsTheLastValue()
    {
        // **`m_flOffset = event->GetFloat( "offset", m_flOffset )` is where the carry-forward
        // actually bites, and it is NOT about a field an event chose to omit.** A game event body
        // carries every field its definition declares — the encoder refuses one that is missing —
        // so a declared field is always present and its default never used. `offset` is different:
        // the stock director never sets it (`CHLTVDirector::StartChaseCameraShot` writes target1,
        // target2, distance, phi, theta and ineye, and nothing else), so it is absent from the
        // definition and falls back on every single event.
        //
        // That is the opposite of what this test asserted first, and the encoder is what corrected
        // it: writing a message without a declared field throws rather than producing a short body.
        DemoTimeline timeline = Watching(
            Chase(target: 3, second: 0, distance: 150f, theta: 45f, phi: 20f),
            Chase(target: 5, second: 0, distance: 300f, theta: 10f, phi: 5f));

        timeline.DirectorAt(timeline.LastTick).ShouldNotBeNull()
            .Offset.ShouldBe(0f, 0.01f, "no event declares offset, so it stays at Reset's value");
    }

    [Test]
    public void DirectorAt_WhenTheSecondTargetIsZero_ReportsNone()
    {
        // **Zero is how the director says "no second target", not a missing field.** `target2` is
        // declared, so it is always on the wire; a shot framing one player sends zero and the
        // camera then takes the target's own yaw rather than looking towards somebody.
        DemoTimeline timeline = Watching(
            Chase(target: 3, second: 7, distance: 150f, theta: 0f, phi: 0f),
            Chase(target: 3, second: 0, distance: 150f, theta: 0f, phi: 0f));

        timeline.DirectorAt(timeline.LastTick).ShouldNotBeNull().SecondTarget.ShouldBe(0);
    }

    [Test]
    public void DirectorAt_ForAnInEyeShot_SaysSo()
    {
        // `SetMode( bInEye ? OBS_MODE_IN_EYE : OBS_MODE_CHASE )`.
        DemoTimeline timeline = Watching(
            Chase(target: 4, second: 0, distance: 96f, theta: 0f, phi: 0f, inEye: true));

        timeline.DirectorAt(timeline.LastTick).ShouldNotBeNull().InEye.ShouldBeTrue();
    }

    [Test]
    public void DirectorAt_ForAChaseShot_IsNotInEye()
    {
        // The control for the case above: a decoder answering `true` always would pass that one.
        DemoTimeline timeline = Watching(
            Chase(target: 4, second: 0, distance: 96f, theta: 0f, phi: 0f));

        timeline.DirectorAt(timeline.LastTick).ShouldNotBeNull().InEye.ShouldBeFalse();
    }

    /// <summary>One shot, with every field the definition declares.</summary>
    /// <remarks>
    /// **All six, because the encoder requires them** — and so does the wire. Written as `short`
    /// to match `SetInt`, which is how the director writes every one of them.
    /// </remarks>
    private static GameEventMessage Chase(
        int target, int second, float distance, float theta, float phi, bool inEye = false) =>
        new(
            1,
            DirectorShot.ChaseEvent,
            new Dictionary<string, object?>
            {
                ["target1"] = (short)target,
                ["target2"] = (short)second,
                ["distance"] = (short)distance,
                ["phi"] = (short)phi,
                ["theta"] = (short)theta,
                ["ineye"] = (short)(inEye ? 1 : 0),
            });

    /// <summary>The event's declaration, without which it cannot be encoded or decoded.</summary>
    /// <remarks>
    /// **Every field is an integer, because the director writes them with <c>SetInt</c>.**
    /// <c>CHLTVDirector::StartChaseCameraShot</c> (<c>hltvdirector.cpp:440</c>) sets target1,
    /// target2, distance, phi, theta and ineye that way, and the client reads them back with
    /// <c>GetFloat</c>, which coerces. That is why <c>DirectorShot</c> accepts several numeric CLR
    /// types rather than casting to one: the definition decides the type, not the reader.
    ///
    /// **`offset` is absent on purpose.** The stock director never sends it — it is in
    /// `FireGameEvent`'s read list but not in any `Start*Shot`, so it keeps whatever `Reset` left
    /// (zero) unless something else sets it. Declaring a field the game does not would be inventing
    /// wire format.
    /// </remarks>
    private static GameEventListMessage Declaration { get; } = new(
    [
        new GameEventDefinition(
            1,
            DirectorShot.ChaseEvent,
            [
                new GameEventField("target1", GameEventValueType.Short),
                new GameEventField("target2", GameEventValueType.Short),
                new GameEventField("distance", GameEventValueType.Short),
                new GameEventField("phi", GameEventValueType.Short),
                new GameEventField("theta", GameEventValueType.Short),
                new GameEventField("ineye", GameEventValueType.Short),
            ]),
    ]);

    /// <summary>A demo carrying the event declaration and the shots given.</summary>
    /// <remarks>
    /// **A DataTables command is required, and leaving it out fails SILENTLY.**
    /// <c>DemoTimeline.Build</c> returns an empty timeline when a demo declares no send tables
    /// (<c>DemoTimeline.cs:830</c>), which is right — without a schema nothing can be decoded — but
    /// it means a specimen missing one produces a timeline with no error and no content. The first
    /// version of these tests used `SyntheticDemo.Containing`, which writes a lone packet, and
    /// every assertion failed as "the director said nothing" rather than as "this demo was never
    /// parsed".
    /// </remarks>
    private static DemoTimeline Watching(params INetMessage[] messages) =>
        DemoTimeline.Build(SyntheticDemo.From(
            SyntheticDemo.DefaultProtocol,
            SyntheticDemo.DataTables(new Core.Schema.DemoSchema([], [])),
            SyntheticDemo.Packet(SyntheticDemo.DefaultProtocol, 0, [Declaration, .. messages])));
}
