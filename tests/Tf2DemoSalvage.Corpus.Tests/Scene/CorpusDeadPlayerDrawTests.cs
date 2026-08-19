using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// A dead player is not drawn, because the engine stops drawing them.
/// </summary>
/// <remarks>
/// **TF2 has no death animation for the player model, and that is a fact about Valve's code rather
/// than a gap in ours.** <c>CMultiPlayerAnimState::HandleDying</c> exists, but <c>m_bDying</c> can
/// only be set by <c>PLAYERANIMEVENT_DIE</c> — and that event is raised nowhere in the entire
/// <c>game/</c> tree, which was checked with <c>PLAYERANIMEVENT_JUMP</c> as a control to prove the
/// search worked. Its handler is <c>Assert( 0 ); // Should be here - not supporting this yet!</c>.
///
/// What actually happens on death is in <c>tf_player.cpp:15637</c>, at the end of
/// <c>CreateRagdollEntity</c>:
///
/// <code>
/// // Turn off the player.
/// AddSolidFlags( FSOLID_NOT_SOLID );
/// AddEffects( EF_NODRAW | EF_NOSHADOW );
/// </code>
///
/// The corpse on screen is a separate <c>CTFRagdoll</c> entity with physics. With ragdolls turned
/// off the player simply disappears, after a single frame of the model in its reference pose —
/// hands at the sides, no sequence playing. That is the owner's own description of the game and it
/// is exactly what the code above produces.
///
/// **So the defect this measures is ours.** <c>DemoTimeline</c> gated players on
/// <see cref="EntityState.IsVisible"/>, which is about the PVS, rather than on
/// <see cref="EntityState.IsDrawn"/>, which also tests <c>EF_NODRAW</c>. A dead player therefore
/// kept being drawn — and once B100 began choosing an activity from the movement flags, a corpse
/// with <c>FL_ONGROUND</c> clear was drawn as <c>ACT_MP_JUMP_FLOAT</c>. Seventeen seconds of
/// "airborne" in a movement recording turned out to be a respawn, not a rocket jump.
///
/// **Dead players stay in the timeline as data and are marked undrawable**, rather than being
/// dropped. The scoreboard, the kill feed and the player list all need someone who has just died,
/// and dropping them would also make the control below unmeasurable through this API — a test that
/// cannot see a dead player cannot prove it declined to draw one.
/// </remarks>
public sealed class CorpusDeadPlayerDrawTests
{
    /// <summary>
    /// A recording made deliberately to exercise movement, in which the owner also died once.
    /// </summary>
    private const string MovementDemo = "movement-test-stv-cp_process";

    [Test]
    public void Timeline_DeadPlayers_AreNeverDrawn()
    {
        string path = Corpus.Demo(MovementDemo);

        DemoTimeline timeline = TimelineCache.For(path);

        List<ScenePlayer> dead = [];
        List<ScenePlayer> alive = [];

        foreach (TimelineFrame frame in timeline.Frames)
        {
            foreach (ScenePlayer player in frame.Players)
            {
                (player.IsAlive ? alive : dead).Add(player);
            }
        }

        // **Both controls, and the test is worthless without them.** If the recording contained no
        // death the assertion below would pass against any code at all, and if it contained no
        // living player it would pass against code that draws nobody.
        dead.ShouldNotBeEmpty(
            "this recording must contain a death, or the assertion below measures nothing");

        alive.ShouldNotBeEmpty(
            "this recording must contain a living player, or 'nothing is drawn' would pass");

        alive.ShouldContain(
            player => player.Drawn,
            "a living player must be drawn, or the flag is simply always false");

        // The measurement itself. Before the fix every one of these was drawn, standing or jumping.
        List<ScenePlayer> drawnCorpses = [.. dead.Where(player => player.Drawn)];

        drawnCorpses.ShouldBeEmpty(
            $"{drawnCorpses.Count} dead player-ticks were drawn; the engine sets EF_NODRAW on death " +
            "and draws a separate ragdoll entity instead");
    }
}
