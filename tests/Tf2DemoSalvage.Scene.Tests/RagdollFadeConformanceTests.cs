using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// A corpse expires the way <c>C_TFRagdoll::ClientThink</c> expires one — never while it is on
/// screen, and 15 seconds after creation if it never was (B315).
/// </summary>
/// <remarks>
/// **Reading only where the timer is SET gives a cited wrong answer.** `CreateTFRagdoll` ends with
/// <c>StartFadeOut( cl_ragdoll_fade_time.GetFloat() )</c> (`c_tf_player.cpp:869`) and the convar
/// defaults to 15, so "a corpse lasts 15 seconds" looks settled. The think is where it lives:
///
/// <code>
/// if ( IsRagdollVisible() )
/// {
///     …
///     StartFadeOut( cl_ragdoll_fade_time.GetFloat() * 0.33f );
///     return;
/// }
///
/// if ( m_fDeathTime &lt; gpGlobals-&gt;curtime ) { EndFadeOut(); return; }
/// </code>
///
/// `c_tf_player.cpp:1532-1553`. The timer is re-armed at a THIRD of the convar on every think the
/// corpse is visible, and the function returns before the expiry test — so a corpse being looked at
/// never expires at all, and one that has left view goes 4.95 seconds later.
///
/// **Why this is not a detail.** Without it the viewer draws every corpse from its death until the
/// server destroys the entity, and the server keeps one ragdoll per player until that player's NEXT
/// death (`UTIL_Remove`, `tf_player.cpp:15602`). Measured on `serveme-627619-stv-2026-08-07`: 57
/// corpses simultaneously undeleted against a twelve-player roster.
/// </remarks>
public sealed class RagdollFadeConformanceTests
{
    /// <remarks>
    /// **The `* 0.33f` is carried rather than rounded.** 15 × 0.33 is 4.95, not 5, and writing 5
    /// would be this project tidying up an engine constant — the same class of change as rounding a
    /// clamp. It is asserted here so a later "simplification" reddens.
    /// </remarks>
    [Test]
    public void Seconds_ForTheTwoDelays_AreTheConvarAndAThirdOfIt()
    {
        RagdollFade.NeverSeenSeconds.ShouldBe(15f);
        RagdollFade.AfterLeavingViewSeconds.ShouldBe(4.95f, 1e-5d);
    }

    [Test]
    public void Gone_ForACorpseNeverSeen_IsTrueAfterFifteenSeconds()
    {
        RagdollFade fade = OneSecondPerTick();

        fade.Gone(Corpse, seconds: 100d, visible: false).ShouldBeFalse();
        fade.Gone(Corpse, seconds: 114.9d, visible: false).ShouldBeFalse();
        fade.Gone(Corpse, seconds: 115.1d, visible: false).ShouldBeTrue();
    }

    /// <remarks>
    /// **The control that says the visibility branch RETURNS.** A reading that re-armed the timer
    /// and then fell through to the expiry test would still expire a watched corpse, just later —
    /// and every assertion about the unwatched case would pass identically.
    /// </remarks>
    [Test]
    public void Gone_ForACorpseWatchedThroughout_IsNeverTrue()
    {
        RagdollFade fade = OneSecondPerTick();

        for (double at = 100d; at < 400d; at += 1d)
        {
            fade.Gone(Corpse, at, visible: true).ShouldBeFalse();
        }

        fade.Gone(Corpse, seconds: 400d, visible: true).ShouldBeFalse();
    }

    /// <remarks>
    /// **This is the case that actually tests the engine's `return`, and the obvious one above does
    /// not.** Removing the early return from the visible branch leaves
    /// `Gone_ForACorpseWatchedThroughout_IsNeverTrue` GREEN: watching from before the timer expires
    /// re-arms it a few seconds ahead on every call, so the stale expiry check never fires. Correct
    /// and broken predict the same observation, which makes that test insensitive to the very line
    /// it appears to cover — the "wrong condition" failure `CLAUDE.md` describes, found by working
    /// out what a sabotage would do rather than by reading the test.
    ///
    /// **The distinguishing input is a corpse first seen AFTER its unseen timer would have run.**
    /// Created at tick 100 with the clock at seconds-per-tick, it would expire unseen at 115; asked
    /// about for the first time at 120 and visible, the engine's branch returns before ever reaching
    /// the expiry test. Without the return it is compared against the stale 115, and a corpse the
    /// viewer is looking straight at vanishes.
    ///
    /// It arises here and not in the engine because the client thinks every frame from creation,
    /// where this viewer can be opened mid-match or seeked forward past a death.
    /// </remarks>
    [Test]
    public void Gone_ForACorpseFirstSeenAfterItsUnseenTimer_IsFalse()
    {
        RagdollFade fade = OneSecondPerTick();

        fade.Gone(Corpse, seconds: 120d, visible: true).ShouldBeFalse();
    }

    /// <remarks>
    /// **The case that distinguishes the two delays.** A corpse watched and then abandoned goes on
    /// the SHORT timer, not the long one — so this is red against an implementation that re-arms
    /// with `cl_ragdoll_fade_time` instead of a third of it, which is the easier misreading.
    /// </remarks>
    [Test]
    public void Gone_AfterLeavingView_IsTrueFourAndAHalfSecondsLater()
    {
        RagdollFade fade = OneSecondPerTick();

        fade.Gone(Corpse, seconds: 100d, visible: true).ShouldBeFalse();

        fade.Gone(Corpse, seconds: 104.5d, visible: false).ShouldBeFalse();
        fade.Gone(Corpse, seconds: 105.5d, visible: false).ShouldBeTrue();
    }

    /// <remarks>
    /// **`EndFadeOut` destroys the entity, so expiry is permanent** — `ClearRagdoll`,
    /// `SetRenderMode( kRenderNone )`, `DestroyBoneAttachments` (`c_tf_player.cpp:1634-1640`). A
    /// corpse that came back the moment the camera turned towards it would flicker every time a
    /// viewer panned across a spot where somebody once died.
    /// </remarks>
    [Test]
    public void Gone_ForAnExpiredCorpseNowLookedAt_StaysTrue()
    {
        RagdollFade fade = OneSecondPerTick();

        fade.Gone(Corpse, seconds: 116d, visible: false).ShouldBeTrue();
        fade.Gone(Corpse, seconds: 117d, visible: true).ShouldBeTrue();
    }

    /// <remarks>
    /// **The engine cannot seek and this must**, which is the one place the two part company
    /// (D131). Scrubbing backwards past a corpse's death has to bring it back, or a rewind shows a
    /// map that is missing every body it showed the first time through.
    /// </remarks>
    [Test]
    public void Gone_AfterTheClockRunsBackwards_IsFalseAgain()
    {
        RagdollFade fade = OneSecondPerTick();

        fade.Gone(Corpse, seconds: 116d, visible: false).ShouldBeTrue();

        fade.Rewound();

        fade.Gone(Corpse, seconds: 100d, visible: false).ShouldBeFalse();
    }

    /// <remarks>
    /// **A bystander, because one corpse cannot tell "expired that one" from "expired all of them".**
    /// The state is keyed per corpse and a dictionary keyed on something shared — the entity index
    /// alone, say, which corpses reuse — would expire a fresh corpse the moment its predecessor's
    /// timer ran out.
    /// </remarks>
    [Test]
    public void Gone_ForASecondCorpseInTheSameSlot_IsIndependent()
    {
        RagdollFade fade = OneSecondPerTick();

        SceneRagdoll successor = Corpse with { Serial = 2, FirstTick = 8000 };

        fade.Gone(Corpse, seconds: 100d, visible: false).ShouldBeFalse();
        fade.Gone(Corpse, seconds: 116d, visible: false).ShouldBeTrue();

        fade.Gone(successor, seconds: 116d, visible: false)
            .ShouldBeFalse("a new corpse in a reused slot starts its own timer");
    }

    /// <summary>A fade whose ticks are seconds, so every prediction below is plain arithmetic.</summary>
    /// <remarks>
    /// **One second per tick is not a real server rate and that is the point.** A TF2 server runs 66
    /// or 33, and either would put every boundary in this suite on a fractional second where a
    /// prediction has to be carried to several decimals — which is how two frame-boundary
    /// predictions went wrong earlier in this work
    /// (`docs/memory/predictions-must-not-sit-on-a-boundary.md`). The rate is an input to the
    /// arithmetic, not part of the claim; `RagdollPropsTests` exercises it with a real one.
    /// </remarks>
    private static RagdollFade OneSecondPerTick() => new(1f);

    private static SceneRagdoll Corpse =>
        new(EntityIndex: 40,
            Serial: 1,
            PlayerClass: 5,
            Team: SceneTeams.Red,
            X: 0f,
            Y: 0f,
            Z: 0f,
            Gib: false,
            Burning: false,
            FeignDeath: false,
            WasDisguised: false,
            FirstTick: 100,
            LastTick: 6000);
}
