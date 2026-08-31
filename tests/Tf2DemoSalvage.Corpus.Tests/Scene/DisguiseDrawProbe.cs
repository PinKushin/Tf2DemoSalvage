using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene;

// Namespaced away from `Tf2DemoSalvage.Corpus.Tests.*`, where `Corpus` binds to the namespace
// rather than to the helper class — the same reason `CorpusPlayerOriginTests` beside it does.
namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// What a disguised spy's DRAWN prop actually says — a MEASUREMENT.
/// </summary>
/// <remarks>
/// **The owner, after disguises were implemented and the timeline was shown to carry them:** *"spy
/// still looks the same"*. The timeline probe proved the decode — 15 disguised sightings with the
/// right classes, teams and `IsEnemy` — and proved nothing whatever about what reaches a screen.
///
/// **That gap is this project's most reliable bug** and it has its own memory entry:
/// `docs/memory/output-level-assertion-or-it-is-not-done.md`. A unit test proves a component works
/// when called with the values the test chose; it says nothing about whether production calls it,
/// or with what. Three no-ops shipped in one session that way, every one with a green suite.
///
/// So this walks the SAME call `MomentScene` makes — <c>PlayerProps.Add</c> — and reports the
/// `SceneProp` that comes out: the model path and the skin family, for every player the recording
/// says is disguised. If those match the undisguised answer, the wiring is not connected however
/// well the rules are tested.
///
/// Reports numbers, asserts only the harness precondition (D38).
/// </remarks>
[Explicit("Diagnostic: reports the drawn model and skin of every disguised player.")]
public sealed class DisguiseDrawProbe
{
    /// <summary>The recording the owner was watching.</summary>
    private const string Recording = "tf2-2026-pub-pov-clean";

    /// <summary>Ticks to sample across the demo.</summary>
    private const int Samples = 400;

    [Test]
    public void Draw_ADisguisedSpy_ReportsTheModelAndSkinItGets()
    {
        DemoTimeline timeline = TimelineCache.For(Corpus.Demo(Recording));

        // **A stand-in for the installed game, so this runs without one.** The real
        // `IPlayerAppearance` reads `items_game.txt`; what is being measured here is WHICH class is
        // asked for, so a resolver that simply names the class it was given is not a weaker
        // instrument — it is a more direct one.
        NamesTheClass appearance = new();

        List<ScenePlayer> players = [];
        List<SceneProp> drawn = [];
        HashSet<string> reported = new(StringComparer.Ordinal);
        HashSet<string> opening = new(StringComparer.Ordinal);

        int first = timeline.FirstTick;
        int last = timeline.LastTick;

        for (int tick = first; tick <= last; tick += Math.Max(1, (last - first) / Samples))
        {
            players.Clear();
            timeline.PlayersAt(tick, players);

            if (!players.Any(player => player.Conditions.Has(PlayerConditions.Disguised)))
            {
                continue;
            }

            drawn.Clear();
            PlayerProps.Add(players, drawn, appearance);

            foreach (ScenePlayer player in players
                .Where(player => player.Conditions.Has(PlayerConditions.Disguised)))
            {
                // **Every prop at that entity index, not the first.** The owner reports "a red
                // player drawing INSIDE his actual player model" — two models at one position,
                // which a `FirstOrDefault` cannot see at all. An instrument that reports the first
                // match is blind to the exact defect being hunted.
                List<SceneProp> all =
                    [.. drawn.Where(candidate => candidate.EntityIndex == player.EntityIndex)];

                if (all.Count != 1)
                {
                    reported.Add(
                        $"entity {player.EntityIndex.ToString(CultureInfo.InvariantCulture)} "
                        + $"DREW {all.Count.ToString(CultureInfo.InvariantCulture)} PROPS: "
                        + string.Join(
                            " | ",
                            all.Select(one =>
                                $"'{one.ModelPath}' skin "
                                + one.Pose.Skin.ToString(CultureInfo.InvariantCulture))));
                }

                SceneProp? prop = all.Count > 0 ? all[0] : null;

                reported.Add(
                    $"entity {player.EntityIndex.ToString(CultureInfo.InvariantCulture)} "
                    + $"class {player.PlayerClass?.ToString(CultureInfo.InvariantCulture) ?? "?"} "
                    + $"enemy {player.IsEnemy} "
                    + $"as {player.DisguiseClass?.ToString(CultureInfo.InvariantCulture) ?? "none"}"
                    + $"/{player.DisguiseTeam?.ToString(CultureInfo.InvariantCulture) ?? "none"} "
                    + $"-> drew "
                    + (prop is { } shown
                        ? $"'{shown.ModelPath}' skin "
                            + shown.Pose.Skin.ToString(CultureInfo.InvariantCulture)
                        : "NOTHING"));
            }
        }

        foreach (string line in reported.Order(StringComparer.Ordinal).Take(16))
        {
            TestContext.Out.WriteLine($"DRAWN {line}");
        }

        TestContext.Out.WriteLine($"DRAWN {reported.Count} distinct disguised draws");

        // **Does a PLAYER entity also produce a prop track?** `MomentScene` builds its draw list as
        // the demo's props PLUS the players, so a player that appears in both is drawn twice — which
        // is the owner's report exactly: "a red player drawing inside his actual player model".
        // Player slots are 1..MaxPlayers, so a prop track down there is the thing to find.
        // **What else is standing where a disguised spy is.** The owner reports a red player model
        // INSIDE the spy's own, and neither the player path nor a player-slot prop track produces a
        // second model — so whatever it is has its own entity, and naming it beats guessing.
        List<SceneProp> near = [];

        for (int tick = first; tick <= last; tick += Math.Max(1, (last - first) / Samples))
        {
            players.Clear();
            timeline.PlayersAt(tick, players);

            foreach (ScenePlayer spy in players
                .Where(player => player.Conditions.Has(PlayerConditions.Disguised)))
            {
                // **Another PLAYER standing inside him.** The owner: it looked like a red DEMO,
                // not a second spy — so the intruder has its own class and team, which only a
                // player prop can supply. Position, because two players are two world transforms.
                foreach (ScenePlayer other in players.Where(other =>
                    other.EntityIndex != spy.EntityIndex
                    && Math.Abs(other.X - spy.X) < 48f
                    && Math.Abs(other.Y - spy.Y) < 48f
                    && Math.Abs(other.Z - spy.Z) < 96f))
                {
                    reported.Add(
                        $"INSIDE spy {spy.EntityIndex.ToString(CultureInfo.InvariantCulture)} "
                        + $"(team {spy.Team?.ToString(CultureInfo.InvariantCulture) ?? "?"}, "
                        + $"as {spy.DisguiseClass?.ToString(CultureInfo.InvariantCulture) ?? "none"}"
                        + $"/{spy.DisguiseTeam?.ToString(CultureInfo.InvariantCulture) ?? "none"}): "
                        + $"entity {other.EntityIndex.ToString(CultureInfo.InvariantCulture)} "
                        + $"class {other.PlayerClass?.ToString(CultureInfo.InvariantCulture) ?? "?"} "
                        + $"team {other.Team?.ToString(CultureInfo.InvariantCulture) ?? "?"} "
                        + $"drawn {other.Drawn} life "
                        + (other.LifeState?.ToString(CultureInfo.InvariantCulture) ?? "?"));
                }

                near.Clear();
                timeline.PropsAt(tick, near);

                // **By OWNERSHIP, not by position.** A bone-merged prop's pose is (0,0,0) by
                // construction — `FollowEntity` zeroes it — so a proximity test against the spy's
                // world position can never match one, and a first version of this check found
                // nothing while every cosmetic and weapon he carries was hanging off him. That is
                // the instrument being blind to the exact thing it was pointed at.
                foreach (SceneProp other in near.Where(other =>
                    other.AttachedTo == spy.EntityIndex || other.OwnedBy == spy.EntityIndex))
                {
                    reported.Add(
                        $"NEAR spy {spy.EntityIndex.ToString(CultureInfo.InvariantCulture)}: "
                        + $"entity {other.EntityIndex.ToString(CultureInfo.InvariantCulture)} "
                        + $"'{other.ModelPath}' skin "
                        + other.Pose.Skin.ToString(CultureInfo.InvariantCulture));
                }
            }
        }

        foreach (ScenePropTrack track in timeline.Props
            .Where(track => track.EntityIndex is > 0 and <= 33)
            .OrderBy(track => track.EntityIndex))
        {
            TestContext.Out.WriteLine(
                $"LOWPROP entity {track.EntityIndex.ToString(CultureInfo.InvariantCulture)} "
                + $"serial {track.SerialNumber.ToString(CultureInfo.InvariantCulture)} "
                + $"item {track.ItemDefinitionIndex?.ToString(CultureInfo.InvariantCulture) ?? "none"} "
                + $"{track.ClassName} '{track.ModelPath}'");
        }

        TestContext.Out.WriteLine(
            $"LOWPROP {timeline.Props.Count(track => track.EntityIndex is > 0 and <= 33)} "
            + $"prop tracks sit in player slots");

        // **The opening ticks, EVERY one of them.** The owner: the double-draw is "at the start".
        // The sweep above steps by ~70 ticks from FirstTick, so it lands on 0, 70, 140 — and this
        // session already measured entities being re-created in a burst at ticks 11..13, taking
        // their class baseline's values until their own arrive. A sample every 70 ticks steps
        // straight over that window, which makes the transient invisible to it by construction
        // rather than by bad luck.
        for (int tick = first; tick <= Math.Min(last, first + 200); tick++)
        {
            players.Clear();
            timeline.PlayersAt(tick, players);

            foreach (ScenePlayer player in players)
            {
                foreach (ScenePlayer other in players.Where(other =>
                    other.EntityIndex > player.EntityIndex
                    && Math.Abs(other.X - player.X) < 48f
                    && Math.Abs(other.Y - player.Y) < 48f
                    && Math.Abs(other.Z - player.Z) < 96f))
                {
                    opening.Add(
                        $"tick {tick.ToString(CultureInfo.InvariantCulture)}: "
                        + $"{player.EntityIndex.ToString(CultureInfo.InvariantCulture)} "
                        + $"(class {player.PlayerClass?.ToString(CultureInfo.InvariantCulture) ?? "?"}"
                        + $"/team {player.Team?.ToString(CultureInfo.InvariantCulture) ?? "?"}) "
                        + $"and {other.EntityIndex.ToString(CultureInfo.InvariantCulture)} "
                        + $"(class {other.PlayerClass?.ToString(CultureInfo.InvariantCulture) ?? "?"}"
                        + $"/team {other.Team?.ToString(CultureInfo.InvariantCulture) ?? "?"}) "
                        + $"both at ({player.X:0} {player.Y:0} {player.Z:0})");
                }
            }
        }

        foreach (string line in opening.Order(StringComparer.Ordinal).Take(14))
        {
            TestContext.Out.WriteLine($"OPENING {line}");
        }

        TestContext.Out.WriteLine(
            $"OPENING {opening.Count} overlapping player pairs in the first 200 ticks");

        timeline.PlayerTracks.Count.ShouldBeGreaterThan(0, "the demo produced no players at all");
    }

    /// <summary>An appearance that answers with the class number it was asked about.</summary>
    /// <remarks>
    /// **Deliberately not the real one.** The question is which class `PlayerProps` ASKS for, and a
    /// resolver that echoes it makes the answer readable without an installed game — and without the
    /// item schema's own gaps standing between the measurement and its subject.
    /// </remarks>
    private sealed class NamesTheClass : IPlayerAppearance
    {
        public string? ModelOf(int playerClass) =>
            $"models/player/class{playerClass.ToString(CultureInfo.InvariantCulture)}.mdl";

        public string? WeaponSuffix(string? weaponClass, int? playerClass) => null;

        public bool Airwalks(int playerClass) => true;

        public string? Hands(int playerClass) => null;
    }
}
