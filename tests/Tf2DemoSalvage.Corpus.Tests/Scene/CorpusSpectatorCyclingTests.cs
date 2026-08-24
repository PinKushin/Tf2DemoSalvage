using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene;

// Namespaced away from `Tf2DemoSalvage.Corpus.Tests.*`, where `Corpus` binds to the namespace.
namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// Cycling the spectated player across a real match (B145).
/// </summary>
/// <remarks>
/// **The conformance suite proves the rules; only this proves they meet real data.** Those tests
/// feed `SpectatorTarget.Next` a five-entry list written by hand from the measured shape of z1800 —
/// by the same person who wrote the search, from the same reading of `CTFPlayer::FindNextObserverTarget`.
/// It cannot say whether a real timeline produces a list that cycles sensibly.
///
/// **And the feature this belongs to is exactly the one that shipped as nothing.** `CycleTargetForward`
/// and `CycleTargetReverse` were declared, bound to the mouse, given Source command names, and
/// asserted on by three tests — with no production code reading them. Adding more tests of the same
/// kind would have left that untouched, which is why this one is about counts on real bytes.
///
/// **A POV demo is the wrong specimen and would pass while measuring nothing.** The committed era
/// POVs are the owner's solo recordings and carry no other players at all, so a cycle would find one
/// target and stop — indistinguishable from a broken search. `docs/memory/pov-demos-are-pvs-limited.md`.
/// So this asks for z1800, a nine-versus-nine match.
/// </remarks>
public sealed class CorpusSpectatorCyclingTests
{
    [Test]
    public void Next_AcrossARealMatch_VisitsEveryPlayingPlayerAndComesBack()
    {
        // **The whole cycle, walked, on a real player list.** Stepping forward once proves almost
        // nothing: an implementation that always returned the first playing player would pass it.
        // Walking the full loop and requiring it to return to the start is what distinguishes a
        // cycle from a lookup.
        DemoTimeline timeline = TimelineCache.For(Corpus.Demo("z1800"));

        int tick = timeline.FirstTick + ((timeline.LastTick - timeline.FirstTick) / 2);
        IReadOnlyList<ScenePlayer> players = timeline.PlayersAt(tick);

        // **The denominator asks production for the rule rather than restating it.** This counted
        // `Team is 2 or 3`, which was the whole filter until the engine's other clauses were
        // implemented — `IsValidObserverTarget` also refuses a player who is dead or EF_NODRAW, and
        // a dead player is still on RED or BLU. At this tick that is 4 of 24, so a team-only count
        // demanded the cycle visit four corpses and failed when it correctly would not.
        //
        // Restating the predicate here would work today and drift the next time the engine's rule
        // is understood better. Asking `CanObserve` means the cycle and its denominator cannot
        // disagree — the same argument the renderer's body-selection log makes for using the
        // renderer's own predicate rather than recomputing it.
        int playing = players.Count(SpectatorTarget.CanObserve);

        TestContext.Out.WriteLine(
            $"tick {tick}: {players.Count} entities, " +
            $"{players.Count(player => player.Team is 2 or 3)} on a team, {playing} observable");

        playing.ShouldBeGreaterThan(2, "z1800 is a nine-versus-nine match");

        int? at = SpectatorTarget.Choose(players)?.EntityIndex;
        at.ShouldNotBeNull("something must be spectatable to begin with");

        int start = at.Value;
        HashSet<int> visited = [];

        for (int step = 0; step < playing; step++)
        {
            ScenePlayer? next = SpectatorTarget.Next(players, at, reverse: false);

            next.ShouldNotBeNull($"step {step} found nobody");
            at = next.Value.EntityIndex;

            (next.Value.Team is 2 or 3).ShouldBeTrue(
                $"step {step} landed on entity {next.Value.EntityIndex}, team {next.Value.Team}");
            visited.Add(at.Value);
        }

        TestContext.Out.WriteLine(
            $"visited {visited.Count}: {string.Join(", ", visited.OrderBy(entity => entity))}");

        visited.Count.ShouldBe(playing, "every playing player should be reachable");
        at.ShouldBe(start, "and a full lap should come back to where it started");
    }

    [Test]
    public void Next_ReversedAcrossARealMatch_UndoesAForwardStep()
    {
        // **The bystander for direction.** Both arms of the wrap are written separately in the SDK,
        // so a one-armed implementation cycles forward correctly for ever and is only caught by
        // going back. Forward-then-back must be a no-op from anywhere in the list, including the
        // two ends where the wrap actually happens.
        DemoTimeline timeline = TimelineCache.For(Corpus.Demo("z1800"));

        int tick = timeline.FirstTick + ((timeline.LastTick - timeline.FirstTick) / 2);
        IReadOnlyList<ScenePlayer> players = timeline.PlayersAt(tick);

        // Observable rather than merely on a team, for the reason the forward test records: a dead
        // player is on RED or BLU and is not a valid observer target, so stepping FROM one asks the
        // cycle about somebody outside it and forward-then-back cannot return there.
        int[] playing =
        [
            .. players
                .Where(SpectatorTarget.CanObserve)
                .Select(player => player.EntityIndex)
                .OrderBy(entity => entity),
        ];

        playing.Length.ShouldBeGreaterThan(2);

        foreach (int entity in playing)
        {
            ScenePlayer? forward = SpectatorTarget.Next(players, entity, reverse: false);
            forward.ShouldNotBeNull();

            ScenePlayer? back = SpectatorTarget.Next(players, forward.Value.EntityIndex, reverse: true);
            back.ShouldNotBeNull();

            back.Value.EntityIndex.ShouldBe(entity, $"forward then back from {entity}");
        }
    }

    [Test]
    public void Next_TheSourceTvCamera_IsNeverVisitedOnARealMatch()
    {
        // **The defect this guards is a real one that already happened once**, to the default
        // target: SourceTV connects to an empty server before any player, takes the lowest slot,
        // and sorts first for ever — so the first-person view sat in a resupply room for fourteen
        // minutes. A cycle that ignored teams would walk straight back into it.
        //
        // Asserted across the match rather than at one tick, because who is on which team changes
        // and a single sample could miss.
        DemoTimeline timeline = TimelineCache.For(Corpus.Demo("z1800"));

        int first = timeline.FirstTick;
        int step = ((timeline.LastTick - first) / 40) + 1;
        int cycles = 0;

        for (int tick = first; tick <= timeline.LastTick; tick += step)
        {
            IReadOnlyList<ScenePlayer> players = timeline.PlayersAt(tick);

            int? at = SpectatorTarget.Choose(players)?.EntityIndex;

            for (int hop = 0; hop < 6 && at is not null; hop++)
            {
                if (SpectatorTarget.Next(players, at, reverse: hop % 2 == 1) is not { } next)
                {
                    break;
                }

                (next.Team is 2 or 3).ShouldBeTrue(
                    $"tick {tick} hop {hop} landed on entity {next.EntityIndex}, team {next.Team}");

                at = next.EntityIndex;
                cycles++;
            }
        }

        TestContext.Out.WriteLine($"{cycles} cycles across the match, none onto a non-playing entity");

        cycles.ShouldBeGreaterThan(100, "the walk has to actually happen for the assertion to mean anything");
    }
}
