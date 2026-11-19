using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene;

// Namespaced away from `Tf2DemoSalvage.Corpus.Tests.*`, where `Corpus` binds to the namespace rather than to the helper.
namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// B776 at the output: an übercharged player in a real match is DRAWN in the invulnerable skin.
/// </summary>
/// <remarks>
/// `C_TFPlayer::GetSkin` adds 2 to the team skin while `m_Shared.IsInvulnerable()` (c_tf_player.cpp:7827-7833).
/// `InvulnerableSkinConformanceTests` pins the rule on hand-built players; this walks the same call `MomentScene` makes,
/// `PlayerProps.Add`, with the clock `TimelineMoments.ServerTimeAt` gives it, on a recording whose medics pop
/// übercharges, and reads the skin off the prop that comes out. The undisguised, non-übered players of the same frame
/// are the control: they keep 0 and 1.
/// </remarks>
public sealed class CorpusInvulnerableSkinTests
{
    /// <summary>A modern 6v6 match from demos.tf: two medics, so übercharges.</summary>
    private const string Recording = "demostf-koth_ultiduo_r_b7-1490922";

    [Test]
    public void Add_AnUberchargedPlayerInARealMatch_IsDrawnInHisTeamsInvulnerableSkin()
    {
        DemoTimeline timeline = TimelineCache.For(Corpus.Demo(Recording));
        TimelineMoments moments = new(timeline);
        EchoesTheClass appearance = new();

        List<ScenePlayer> players = [];
        List<SceneProp> drawn = [];
        int ubered = 0;
        int controls = 0;
        string? first = null;

        for (int tick = timeline.FirstTick; tick <= timeline.LastTick; tick += 33)
        {
            players.Clear();
            timeline.PlayersAt(tick, players);

            if (!players.Any(player => player.Conditions.Has(PlayerConditions.Invulnerable)))
            {
                continue;
            }

            drawn.Clear();
            PlayerProps.Add(players, drawn, appearance, NoBodygroups.Instance, curTime: moments.ServerTimeAt(tick));

            foreach (SceneProp prop in drawn)
            {
                ScenePlayer player = players.First(candidate => candidate.EntityIndex == prop.EntityIndex);
                int team = PlayerSkin.ForTeam(player.Team);

                if (player.Conditions.IsInvulnerable)
                {
                    prop.Pose.Skin.ShouldBe(team + 2, $"entity {prop.EntityIndex} at tick {tick}, übercharged");
                    ubered++;
                    first ??= string.Create(
                        CultureInfo.InvariantCulture,
                        $"tick {tick} entity {prop.EntityIndex} team {player.Team} skin {prop.Pose.Skin}");
                }
                else if (!player.Conditions.Has(PlayerConditions.Disguised))
                {
                    prop.Pose.Skin.ShouldBe(team, $"entity {prop.EntityIndex} at tick {tick}, the control");
                    controls++;
                }
            }
        }

        TestContext.Out.WriteLine($"UBER {ubered} übered draws, {controls} controls; first: {first}");
        ubered.ShouldBeGreaterThan(0, "the recording has no übercharge, so the assertion read nothing");
        controls.ShouldBeGreaterThan(0, "no un-übered player beside them to compare against");
    }

    /// <summary>An appearance that names the class it was asked about, so this needs no installed game.</summary>
    private sealed class EchoesTheClass : IPlayerAppearance
    {
        public string? ModelOf(int playerClass) =>
            $"models/player/class{playerClass.ToString(CultureInfo.InvariantCulture)}.mdl";

        public string? WeaponSuffix(string? weaponClass, int? playerClass, int? weaponItem) => null;

        public string? Hands(int playerClass) => null;

        public SceneTaunt? TauntForScene(string scene) => null;

        public ItemBodygroups BodygroupsOf(int itemDefinitionIndex, int team) => ItemBodygroups.None;
    }
}
