using System.Collections.Generic;

using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Presentation;

namespace Tf2DemoSalvage.Viewer3D.Tests;

/// <summary>
/// What TF2 does when a spectator cycles targets, written down before implementing it (B145).
/// </summary>
/// <remarks>
/// **Every claim here is read from `source-sdk-2013`**, which ships TF2's own game code — see
/// `docs/memory/tf2-game-code-is-in-the-sdk.md`. The relevant routines are
/// `CTFPlayer::FindNextObserverTarget` and `GetNextObserverSearchStartPoint` in
/// `src/game/server/tf/tf_player.cpp`, and `ClientModeShared::HandleSpectatorKeyInput` in
/// `src/game/client/clientmode_shared.cpp`.
///
/// **The feature existed as a binding and nothing else.** `CycleTargetForward` and
/// `CycleTargetReverse` were declared, bound to the mouse buttons, given Source command names, and
/// asserted on by three tests — and no production code read them, so clicking cycled nothing. The
/// tests were not at fault: they checked that a binding table contained what it should, and it did.
/// **Nothing about a binding table can tell you whether anything consults it.**
///
/// **What is deliberately NOT copied.** `CTFPlayer::IsValidObserverTarget` also admits buildings,
/// observer points and a coached student, and rejects `target == this`. Neither transfers: this
/// viewer follows players only, and "this" is the recording client, which in a POV demo is exactly
/// who you most want to watch. Those are noted rather than implemented, because a rule copied
/// without its context is the kind that gets confidently repeated.
/// </remarks>
public sealed class SpectatorCyclingConformanceTests
{
    /// <summary>Four playing players and a SourceTV camera, the measured shape of z1800.</summary>
    /// <remarks>
    /// Entity 1 is the SourceTV camera: it connects to an empty server before any player, so it
    /// takes the lowest slot and sorts first for ever. It is on team 1 (`TEAM_SPECTATOR`) and must
    /// never be cycled to — following it produces a static view of a resupply room.
    /// </remarks>
    private static List<ScenePlayer> Match() =>
    [
        Player(entity: 1, team: 1),
        Player(entity: 2, team: 3),
        Player(entity: 5, team: 2),
        Player(entity: 9, team: 3),
        Player(entity: 12, team: 2),
    ];

    [Test]
    public void Next_Forward_TakesTheFollowingPlayer()
    {
        // `GetNextObserverSearchStartPoint` adds the direction to the current index before the
        // search begins:
        //
        //     int iDir = bReverse ? -1 : 1;
        //     startIndex += iDir;
        //
        // so cycling forward never returns the player already being watched.
        Followed(Match(), current: 5, reverse: false).ShouldBe(9);
    }

    [Test]
    public void Next_Reverse_TakesThePrecedingPlayer()
    {
        Followed(Match(), current: 5, reverse: true).ShouldBe(2);
    }

    [Test]
    public void Next_PastTheEnd_WrapsToTheStart()
    {
        // Both directions wrap, and the SDK writes each arm explicitly:
        //
        //     if (currentIndex > iMax)      currentIndex = 0;
        //     else if (currentIndex < 0)    currentIndex = iMax;
        //
        // **A one-armed wrap is the plausible bug** — it works for as long as anyone tests forward.
        Followed(Match(), current: 12, reverse: false).ShouldBe(2);
    }

    [Test]
    public void Next_BeforeTheStart_WrapsToTheEnd()
    {
        Followed(Match(), current: 2, reverse: true).ShouldBe(12);
    }

    [Test]
    public void Next_TheSourceTvCamera_IsNeverSelected()
    {
        // **The control that makes every other assertion here mean something.** Entity 1 sorts
        // first and is in the list, so a cycle that ignored teams would land on it — and the
        // symptom is a camera that stops in an empty room, which reads as a decode bug rather than
        // as a target-selection one. `tf_shareddefs.h`: only RED (2) and BLU (3) are valid teams.
        //
        // Walking the whole list is what proves it, rather than one hop that could miss by luck.
        List<ScenePlayer> players = Match();
        int at = 2;

        for (int step = 0; step < players.Count + 2; step++)
        {
            at = Followed(players, at, reverse: false);
            at.ShouldNotBe(1, $"step {step} landed on the SourceTV camera");
        }
    }

    [Test]
    public void Next_NobodyPlaying_IsNullSoTheCallerKeepsItsTarget()
    {
        // **Null, not "the first thing in the list", and the caller's use of it is the point.**
        // The SDK only assigns when the search succeeded:
        //
        //     CBaseEntity * target = FindNextObserverTarget( false );
        //     if ( target )
        //         SetObserverTarget( target );
        //
        // So a failed cycle leaves the camera where it was. The first seconds of a competitive
        // match really are SourceTV alone, and a click then must not blank the view.
        List<ScenePlayer> alone = [Player(entity: 1, team: 1)];

        SpectatorTarget.Next(alone, current: null, reverse: false).ShouldBeNull();
        SpectatorTarget.Next([], current: 3, reverse: true).ShouldBeNull();
    }

    [Test]
    public void Next_TheOnlyPlayer_StaysOnThem()
    {
        // `do { ... } while ( currentIndex != startIndex )` walks the whole list and stops where it
        // began, so with one valid target the search returns that target rather than null. The
        // camera does not move, which is right — there is nowhere else to go.
        List<ScenePlayer> one = [Player(entity: 1, team: 1), Player(entity: 4, team: 2)];

        Followed(one, current: 4, reverse: false).ShouldBe(4);
    }

    [Test]
    public void Next_FromAPlayerWhoHasLeft_StillFindsSomebody()
    {
        // A cycled-to player disconnects, dies out of the list, or switches to spectator, and then
        // the next click starts from an entity that is no longer there. The SDK guards the
        // equivalent case by resetting the index rather than by failing:
        //
        //     if ( startIndex > iMax )
        //         currentIndex = startIndex = 1;
        //
        // **Where it resumes is ours to choose and is not a citation** — the SDK's list is built
        // per search and indexed differently. What must hold is that a click does something.
        SpectatorTarget.Next(Match(), current: 99, reverse: false).ShouldNotBeNull();
        SpectatorTarget.Next(Match(), current: 99, reverse: true).ShouldNotBeNull();
    }

    [Test]
    public void Next_FromNobody_StartsWhereChooseWouldHave()
    {
        // Opening a demo and clicking straight away. Starting somewhere unrelated to the default
        // would make the first click jump for no reason a viewer could explain.
        ScenePlayer? byDefault = SpectatorTarget.Choose(Match());

        byDefault.ShouldNotBeNull();
        Followed(Match(), current: null, reverse: false).ShouldBe(byDefault.Value.EntityIndex);
    }

    [Test]
    public void Commands_CyclingActions_AreTheAttackButtonsTf2Uses()
    {
        // **The binding is not a liberty; it is what the game does.**
        // `ClientModeShared::HandleSpectatorKeyInput` dispatches on the bound COMMAND string, not
        // on a key code:
        //
        //     else if ( down && pszCurrentBinding && Q_strcmp( pszCurrentBinding, "+attack" ) == 0 )
        //     {
        //         engine->ClientCmd( "spec_next" );
        //         return 0;
        //     }
        //     else if ( down && ... Q_strcmp( pszCurrentBinding, "+attack2" ) == 0 )
        //     {
        //         engine->ClientCmd( "spec_prev" );
        //
        // and TF2's own spectator HUD labels them the same way — from `tf/resource/tf_english.txt`
        // on the installed game:
        //
        //     "TF_Spectator_CycleTargetFwdKey" "[%attack%]"
        //     "TF_Spectator_CycleTargetFwd"    "Cycle Targets (fwd)"
        //     "TF_Spectator_CycleTargetRevKey" "[%attack2%]"
        //     "TF_Spectator_CycleTargetRev"    "Cycle Targets (rev)"
        //
        // **Matching on the command string is why a rebound mouse still works.** A player who has
        // put attack on their side button gets cycling on that button, because the engine asks what
        // the key is bound to rather than which key it is.
        KeyBindings.Commands[ViewerAction.CycleTargetForward].ShouldBe("+attack");
        KeyBindings.Commands[ViewerAction.CycleTargetReverse].ShouldBe("+attack2");
    }

    [Test]
    public void Defaults_CyclingActions_AreOnTheMouseButtonsSourceNames()
    {
        // `MOUSE1`/`MOUSE2` are how a config spells them, and the spelling matters because these
        // names are compared against a config's own text (D69). `MouseLeft` is .NET's word for it
        // and would match nothing a player ever wrote.
        KeyBindings.Defaults[ViewerAction.CycleTargetForward].ShouldBe("MOUSE1");
        KeyBindings.Defaults[ViewerAction.CycleTargetReverse].ShouldBe("MOUSE2");
    }

    /// <summary>The entity a cycle lands on, asserting that it landed at all.</summary>
    /// <remarks>
    /// **Written as a helper because `Next(...)?.EntityIndex.ShouldBe(9)` cannot fail.** A null
    /// result short-circuits the whole chain, assertion included, so the test passes precisely when
    /// the feature is most broken. That is the "wrong instrument" case from `CLAUDE.md`, and it was
    /// written that way here first.
    /// </remarks>
    private static int Followed(IReadOnlyList<ScenePlayer> players, int? current, bool reverse)
    {
        ScenePlayer? next = SpectatorTarget.Next(players, current, reverse);

        next.ShouldNotBeNull("the cycle found nobody");
        return next.Value.EntityIndex;
    }

    private static ScenePlayer Player(int entity, int team) =>
        new(entity, 0f, 0f, 0f, team, 125, 1);
}
