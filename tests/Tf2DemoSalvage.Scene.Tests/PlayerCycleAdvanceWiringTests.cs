using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// A player's cycle is advanced by the CLIENT, and the flag saying so has to survive the trip.
/// </summary>
/// <remarks>
/// **`CTFPlayer::CTFPlayer` calls `UseClientSideAnimation()` unconditionally**
/// (`tf_player.cpp:953`), so every TF player carries <c>m_bClientSideAnimation</c> — sent as one
/// unsigned bit from <c>DT_BaseAnimating</c> (`baseanimating.cpp:250`) — and the client advances
/// their cycle itself in <c>C_BaseAnimating::UpdateClientSideAnimation</c>
/// (`c_baseanimating.cpp:5134`), which latches and then calls <c>FrameAdvance( 0.0f )</c>.
///
/// **The consequence, and it is the whole reason these tests exist: a player's <c>m_flCycle</c> is
/// never a driving value.** It decodes to zero and stays there. Everything that moves the model
/// comes from the client's own advance, so an entity that reaches the renderer without the flag
/// holds frame zero for the entire recording while its POSITION keeps interpolating. That is
/// exactly what the owner reported twice — *"players are not even walking animating right now,
/// they just kinda slide in the run pose"*.
///
/// **Measured, B280.** The `cycle` probe drove the production pipeline over a running scout in
/// `z1800.dem` and printed, at every one of forty samples a quarter-tick apart:
///
/// <code>
///   cycle 0  seq 0  POSED seq 102 frame 0+0  drawnSeq 102  drawnSpeed 470.5  csa False
/// </code>
///
/// The sequence selection worked; the track knew the entity was client-side animated; the drawn
/// prop said <c>False</c>. <c>PlayerProps.Add</c> built the prop and never carried the flag, so
/// every player in every demo took the server-animated branch of
/// <c>EntityModelSet.Simulate</c> — <c>advanced = where.Cycle</c>, forever zero.
/// </remarks>
public sealed class PlayerCycleAdvanceWiringTests
{
    /// <summary><c>STUDIO_LOOPING</c>, <c>studio.h</c> — the sequence flag the wrap reads.</summary>
    private const int SequenceLooping = 0x0001;

    /// <remarks>
    /// **The end-to-end claim, and the only test here that fails when the wiring is lost.** It
    /// drives the scene the way the viewer does and asks what frame the skeleton was HANDED at two
    /// times a third of a second apart, through <see cref="EntityModelSet.FrameOf"/> — carried, not
    /// recomputed (B243).
    ///
    /// A third of a second of a thirty-frame-a-second animation is ten frames, so a working advance
    /// cannot land on the same frame twice; a broken one reports frame zero both times.
    /// </remarks>
    [Test]
    public void Pose_ForAClientSideAnimatedPlayer_AdvancesTheFrameOverTime()
    {
        EntityModelSet models = new() { Geometry = _ => Animated() };

        // **Without an appearance the player is never added at all.** `PlayerProps.Add` skips
        // anyone whose model the install cannot name, and the default `NoAppearance` names none —
        // so a test that left this alone would assert against an empty scene and report the frame
        // as absent rather than as unmoving.
        MomentScene scene = new(models, new ViewmodelScene(), new RecordingLogger())
        {
            Appearance = new StubAppearance(),
        };

        ScenePlayer running = Running();

        scene.Build([running], [], At(0d));
        scene.Pose(At(0d));

        (int Sequence, int Frame, float Fraction)? first = models.FrameOf(running.EntityIndex);

        scene.Build([running], [], At(1d / 3d));
        scene.Pose(At(1d / 3d));

        (int Sequence, int Frame, float Fraction)? later = models.FrameOf(running.EntityIndex);

        first.ShouldNotBeNull("the player must reach the skeleton at all");
        later.ShouldNotBeNull("the player must reach the skeleton at all");

        later.Value.Frame.ShouldNotBe(
            first.Value.Frame,
            "a TF player is client-side animated, so the client advances their cycle: a third of " +
            "a second of a 30 fps animation is ten frames, and a player whose frame does not move " +
            "slides through the map in one pose");
    }

    /// <remarks>
    /// **The control, and without it the test above cannot tell an advance from a stampede.** An
    /// entity that is NOT client-side animated takes <c>m_flCycle</c> off the wire, and the wire
    /// here says zero at both times — so its frame must be identical. If this one also advanced,
    /// the fix would be "advance everything", which is a different behaviour from the engine's and
    /// would run every server-animated door and cabinet at demo time on top of the cycle the
    /// server already stated (B259).
    /// </remarks>
    [Test]
    public void Pose_ForAServerAnimatedEntity_HoldsTheWireFrame()
    {
        EntityModelSet models = new() { Geometry = _ => Animated() };

        MomentScene scene = new(models, new ViewmodelScene(), new RecordingLogger());

        SceneProp cabinet = new(
            9,
            "models/props_gameplay/resupply_locker.mdl",
            ScenePropTrack.Classify("models/props_gameplay/resupply_locker.mdl"),
            new ScenePose { Sequence = 0, Cycle = 0f },
            null);

        scene.Build([], [cabinet], At(0d));
        scene.Pose(At(0d));

        (int Sequence, int Frame, float Fraction)? first = models.FrameOf(cabinet.EntityIndex);

        scene.Build([], [cabinet], At(1d / 3d));
        scene.Pose(At(1d / 3d));

        (int Sequence, int Frame, float Fraction)? later = models.FrameOf(cabinet.EntityIndex);

        first.ShouldNotBeNull("the prop must reach the skeleton at all");
        later.ShouldNotBeNull("the prop must reach the skeleton at all");

        later.Value.Frame.ShouldBe(
            first.Value.Frame,
            "a server-animated entity's cycle comes off the wire and the client must not advance " +
            "it: doing so runs it at demo time on top of the cycle the server already sent");
    }

    /// <summary>The flag as the timeline read it, carried onto the drawn prop.</summary>
    /// <remarks>
    /// The unit half. <see cref="PlayerProps.Add"/> is where the defect was: it built every
    /// player's prop from scratch and had no parameter for this at all, so the value the demo
    /// stated was dropped between the timeline and the renderer.
    /// </remarks>
    [Test]
    public void Add_ForAClientSideAnimatedPlayer_CarriesTheFlagToTheProp()
    {
        List<SceneProp> drawn = [];

        PlayerProps.Add([Running()], drawn, new StubAppearance(), (_, _, body) => body);

        drawn.ShouldHaveSingleItem();

        drawn[0].ClientSideAnimated.ShouldBeTrue(
            "the demo says this player animates on the client, and a prop that arrives without " +
            "that takes the server-animated branch and never moves");
    }

    /// <summary>A player the demo says is NOT client-side animated.</summary>
    /// <remarks>
    /// The control for the carry: a hardcoded <c>true</c> would pass the test above and this one
    /// exists to fail against it. Nothing in TF2 sends a player with the flag clear, so this is a
    /// statement about the plumbing rather than about the game.
    /// </remarks>
    [Test]
    public void Add_ForAPlayerTheDemoDoesNotAnimateClientSide_LeavesTheFlagClear()
    {
        List<SceneProp> drawn = [];

        PlayerProps.Add(
            [Running() with { ClientSideAnimated = false }],
            drawn,
            new StubAppearance(),
            (_, _, body) => body);

        drawn.ShouldHaveSingleItem();

        drawn[0].ClientSideAnimated.ShouldBeFalse(
            "the flag must be carried from the demo, not asserted for every player");
    }

    /// <summary>The rate the demo states scales the advance, as it does in the engine.</summary>
    /// <remarks>
    /// **<c>C_BaseAnimating::FrameAdvance</c>, <c>c_baseanimating.cpp:5493</c>:**
    ///
    /// <code>
    ///   float cyclerate = GetSequenceCycleRate( hdr, GetSequence() );
    ///   float addcycle = flInterval * cyclerate * m_flPlaybackRate;
    /// </code>
    ///
    /// The rate multiplies every advance the engine makes — here, in <c>Interpolate</c>
    /// (`c_baseanimating.cpp:5351`), in the viewmodel's own advance
    /// (`c_baseviewmodel.cpp:197`) and in each overlay layer. <c>m_flPlaybackRate</c> is sent in
    /// <c>DT_BaseAnimating</c> and this project has decoded it since B237, but only the BAKED
    /// vertex path ever multiplied by it: the skinned path advanced every entity at rate 1
    /// regardless of what the demo said.
    ///
    /// **Half rate over the same interval must reach a lower cycle**, so this compares two entities
    /// rather than asserting one number — the fixture's own frame count and rate never enter the
    /// prediction, and a change to either cannot make it pass wrongly.
    /// </remarks>
    [Test]
    public void Pose_AtHalfPlaybackRate_AdvancesHalfAsFar()
    {
        EntityModelSet models = new() { Geometry = _ => Animated() };

        MomentScene scene = new(models, new ViewmodelScene(), new RecordingLogger());

        SceneProp full = Spinning(11, rate: 1f);
        SceneProp half = Spinning(12, rate: 0.5f);

        scene.Build([], [full, half], At(0d));
        scene.Pose(At(0d));

        scene.Build([], [full, half], At(0.5d));
        scene.Pose(At(0.5d));

        (int Sequence, int Frame, float Fraction)? atFull = models.FrameOf(full.EntityIndex);
        (int Sequence, int Frame, float Fraction)? atHalf = models.FrameOf(half.EntityIndex);

        atFull.ShouldNotBeNull("the full-rate entity must reach the skeleton");
        atHalf.ShouldNotBeNull("the half-rate entity must reach the skeleton");

        atHalf.Value.Frame.ShouldBeLessThan(
            atFull.Value.Frame,
            "the engine multiplies the advance by m_flPlaybackRate, so an entity the demo says " +
            "plays at half speed must be behind one playing at full speed");
    }

    /// <summary>A client-side-animated entity playing at a stated rate.</summary>
    /// <param name="entityIndex">Which entity.</param>
    /// <param name="rate">Its <c>m_flPlaybackRate</c>.</param>
    /// <returns>The prop.</returns>
    private static SceneProp Spinning(int entityIndex, float rate) =>
        new(
            entityIndex,
            "models/props_gameplay/resupply_locker.mdl",
            ScenePropTrack.Classify("models/props_gameplay/resupply_locker.mdl"),
            new ScenePose { Sequence = 0, Cycle = 0f, PlaybackRate = rate },
            null,
            ClientSideAnimated: true);

    /// <summary>A moment at a demo time in seconds.</summary>
    /// <param name="seconds">Demo seconds.</param>
    /// <returns>The moment.</returns>
    /// <remarks>
    /// **Expressed as seconds and converted back, because <c>Seconds</c> is <c>Tick</c> times the
    /// interval** and the advance under test is measured in seconds. A one-second tick interval
    /// makes the two the same number, which keeps the arithmetic in the test visible rather than
    /// hidden behind 66.67.
    /// </remarks>
    private static MomentInfo At(double seconds) =>
        new(seconds, (int)seconds, false, null, null, 1f, 54f);

    /// <summary>A scout running forward on the ground, client-side animated as TF2 sends them.</summary>
    private static ScenePlayer Running() =>
        new(
            2,
            0f,
            0f,
            0f,
            SceneTeams.Red,
            125,
            1,
            Speed: 320f,
            MoveX: 1f,
            Flags: PlayerActivityState.OnGround,
            ClientSideAnimated: true);

    /// <summary>A model whose one sequence really has frames to advance through.</summary>
    /// <remarks>
    /// **<see cref="SyntheticSkinnedModel.With"/> builds <c>Models: [[]]</c>**, and both
    /// <c>Frames</c> and <c>CyclesPerSecond</c> read the studio bytes — so a fixture built that way
    /// reports one frame at zero cycles a second and cannot show an advance whether or not one
    /// happens. This writes the two fields those readers need.
    /// </remarks>
    private static PropModels.ModelFrames Animated()
    {
        PropModels.SkinnedModel model = SyntheticSkinnedModel.WithBones("root");

        return new PropModels.ModelFrames(
            [
                new PropVertex[]
                {
                    new(1f, 0f, 0f, 0f, 0f, MaterialIndex: 3),
                    new(0f, 1f, 0f, 1f, 0f, MaterialIndex: 3),
                    new(0f, 0f, 1f, 0f, 1f, MaterialIndex: 3),
                },
            ],
            new Dictionary<int, (int Start, int Frames, float CyclesPerSecond)>
            {
                [0] = (0, 1, 0f),
            },
            [0],
            [true],
            Skinned: model with
            {
                Models = [AnimatedStudioBytes.OneSecondLoop(animations: 3)],
                Groups = Looping(model.Groups),
            });
    }

    /// <summary>The fixture's sequences, marked looping so the advance wraps rather than clamps.</summary>
    private static List<(int Group, IReadOnlyList<StudioSequence> Sequences)> Looping(
        IReadOnlyList<(int Group, IReadOnlyList<StudioSequence> Sequences)> groups)
    {
        List<(int Group, IReadOnlyList<StudioSequence> Sequences)> looping = [];

        foreach ((int group, IReadOnlyList<StudioSequence> sequences) in groups)
        {
            List<StudioSequence> marked = [];

            foreach (StudioSequence sequence in sequences)
            {
                marked.Add(sequence with { Flags = SequenceLooping });
            }

            looping.Add((group, marked));
        }

        return looping;
    }


    // `StubAppearance` moved to its own file when a second wiring test needed it (B312).
}
