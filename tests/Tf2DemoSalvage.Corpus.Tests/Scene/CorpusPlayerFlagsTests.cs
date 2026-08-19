using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// A player's <c>m_fFlags</c> reaches the scene, so crouching and jumping can be seen at all.
/// </summary>
/// <remarks>
/// **The flags were read from the wrong table, and the wrong table was written down as a fact.**
/// This project looked for <c>DT_LocalPlayerExclusive.m_fFlags</c> and documented that choice as
/// "declared in DT_LocalPlayerExclusive, so a POV demo carries it for the recorder alone while a
/// SourceTV recording carries it for every player". That sentence was invented rather than
/// measured, and it is wrong. <c>player.cpp:8183</c> declares it in the ordinary table:
///
/// <code>
/// IMPLEMENT_SERVERCLASS_ST( CBasePlayer, DT_BasePlayer )
///     ...
///     SendPropInt ( SENDINFO(m_fFlags), 0, SPROP_UNSIGNED|SPROP_CHANGES_OFTEN ),
/// </code>
///
/// No exclusivity, and <c>SPROP_CHANGES_OFTEN</c> because every player sends it constantly. A trace
/// of a real demo agrees: 119 occurrences of <c>DT_BasePlayer.m_fFlags</c> and not one of the name
/// this project was looking for.
///
/// **So the lookup never matched, for anybody, in any demo.** <c>Flags</c> came back null
/// everywhere, the activity state machine fell to its "nothing said, assume on the ground" default,
/// and no player has ever crouched or jumped in the viewer — the owner's "everyone is still just
/// running all the time". A null that means "not sent" is indistinguishable from a null that means
/// "looked in the wrong place", which is what let this survive being unit-tested.
///
/// Measured on a recording made deliberately for it: crouches, jumps, crouch-jumps and a rocket
/// jump, performed on purpose so the flags have something to say.
/// </remarks>
public sealed class CorpusPlayerFlagsTests
{
    private const string MovementDemo = "movement-test-stv-cp_process";

    /// <summary><c>FL_ONGROUND</c>, <c>const.h:148</c>.</summary>
    private const int OnGround = 1 << 0;

    /// <summary><c>FL_DUCKING</c>, "Player is fully crouched".</summary>
    private const int Ducking = 1 << 1;

    [Test]
    public void PlayerFlags_ARealDemo_CarriesGroundAndCrouchBits()
    {
        string path = Corpus.Demo(MovementDemo);

        List<ScenePlayer> players =
        [
            .. DemoTimeline.Build(File.ReadAllBytes(path))
                .Frames
                .SelectMany(frame => frame.Players)
                .Where(player => player.IsPlaying),
        ];

        players.ShouldNotBeEmpty("the recording must contain a player at all");

        // **The defect, stated as the thing that was silent.** Every one of these was null.
        List<ScenePlayer> stated = [.. players.Where(player => player.Flags is not null)];

        stated.ShouldNotBeEmpty(
            "no player reported m_fFlags; the property is on DT_BasePlayer, not DT_LocalPlayerExclusive");

        // **All four states the recording was made to produce**, because "some flag arrived" would
        // pass against a decode that returned a constant. This player deliberately stood, walked,
        // crouched, jumped and crouch-jumped, so all four combinations must appear.
        bool AnyWith(int set, int clear) =>
            stated.Any(player =>
                player.Flags is { } flags && (flags & set) == set && (flags & clear) == 0);

        AnyWith(OnGround, Ducking).ShouldBeTrue("standing or running on the ground");
        AnyWith(OnGround | Ducking, 0).ShouldBeTrue("crouched on the ground");
        AnyWith(0, OnGround | Ducking).ShouldBeTrue("airborne and not crouched — an ordinary jump");
        AnyWith(Ducking, OnGround).ShouldBeTrue("airborne and crouched — a crouch jump");
    }

    [Test]
    public void PlayerFlags_DerivedCrouchAndAirborne_FollowTheFlags()
    {
        // **The output-level half.** The property arriving is not the same as the scene reading it:
        // IsCrouched and IsAirborne are what the viewer actually asks, and a null-safe accessor
        // that answers false for everything would satisfy the test above and change nothing on
        // screen.
        string path = Corpus.Demo(MovementDemo);

        List<ScenePlayer> players =
        [
            .. DemoTimeline.Build(File.ReadAllBytes(path))
                .Frames
                .SelectMany(frame => frame.Players)
                .Where(player => player.IsPlaying && player.Flags is not null),
        ];

        players.ShouldContain(player => player.IsCrouched, "somebody crouches in this recording");
        players.ShouldContain(player => player.IsAirborne, "somebody jumps in this recording");

        // The controls: neither derived property may be stuck true either.
        players.ShouldContain(player => !player.IsCrouched);
        players.ShouldContain(player => !player.IsAirborne);
    }

    [Test]
    public void PlayerFlags_APovRecording_CarriesThemToo()
    {
        // **The claim this settles was stated as a separate defect and turned out to be the same
        // one.** When the flags were read from DT_LocalPlayerExclusive the recorder of a POV demo
        // reported null like everyone else, and that was filed as "POV demos cannot support crouch
        // and jump detection at all". It was never about the point of view: m_fFlags is on
        // DT_BasePlayer (player.cpp:8183) and is sent for every player in the PVS, so a POV demo
        // carries it exactly as a SourceTV one does.
        //
        // Asserted rather than assumed, because "the fix probably covered that too" is how a
        // second defect hides behind a first.
        string path = Corpus.Demo("movement-test-pov-cp_process");

        List<ScenePlayer> stated =
        [
            .. DemoTimeline.Build(File.ReadAllBytes(path))
                .Frames
                .SelectMany(frame => frame.Players)
                .Where(player => player.IsPlaying && player.Flags is not null),
        ];

        stated.ShouldNotBeEmpty("a POV demo carries m_fFlags for the players it can see");

        stated.ShouldContain(player => player.IsCrouched, "the recorder crouches deliberately");
        stated.ShouldContain(player => player.IsAirborne, "and jumps deliberately");
    }
}
