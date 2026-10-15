using System.Collections.Generic;
using System.Linq;
using System.Text;

using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene;
using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>B516: the spectator GUI's player cards through <see cref="VguiHud.Frame"/> — a seek's skipped frames, then a paused shot.</summary>
public sealed class VguiHudSpectatorGuiTests
{
    /// <summary>
    /// A `--shot` lands after skipped frames (no layout) on a PAUSED frame, whose curtime never passes the cards' 0.1 s
    /// throttle. The cards made while skipping must still be filled on the first painted frame, not painted with the
    /// `.res`'s placeholder portrait (the first golden pair's grey cards).
    /// </summary>
    [Test]
    public void Frame_APausedShotAfterSkippedFrames_FillsTheCardsOnTheFirstPaint()
    {
        Dictionary<string, byte[]> files = new()
        {
            ["resource/ClientScheme.res"] = [],
            ["Resource/UI/SpectatorTournament.res"] = Encoding.UTF8.GetBytes("""
                "Resource/UI/SpectatorTournament.res"
                {
                    "specgui"
                    {
                        "team1_player_base_x" "10"
                        "team1_player_base_y" "100"
                        "team1_player_delta_y" "30"
                        "team2_player_base_x" "500"
                        "team2_player_base_y" "100"
                        "team2_player_delta_y" "30"
                        "playerpanels_kv"
                        {
                            "visible" "0"
                            "wide" "100"
                            "tall" "28"
                            "classimage" { "ControlName" "CTFClassImage" "fieldName" "classimage" "wide" "20" "tall" "20" "image" "../hud/class_scoutred" }
                        }
                    }
                }
                """),
        };
        VguiSurfaceHost host = new(files.GetValueOrDefault, _ => null, new FpsPanelTests.SolidGdi(), _ => (0, 0));
        VguiHud hud = new(host, new EntityModelSet());
        Dictionary<string, string> server = new() { ["mp_tournament"] = "1" };
        Dictionary<string, string> client = new() { ["cl_use_tournament_specgui"] = "1" };
        HudState state = new(InGame: true, HasLocalPlayer: true, HideHud: 0, Health: 0, Alive: false)
        {
            IsHltv = true,
            Team = 1,
            LocalIndex = 1,
            ConVars = new HudConVars(server.GetValueOrDefault, client.GetValueOrDefault),
            ScoreboardPlayers =
            [
                new SceneScoreboardPlayer(2) { Connected = true, Team = SceneTeams.Blu, PlayerClass = 5, Alive = true, Health = 150, MaxHealth = 150 },
                new SceneScoreboardPlayer(3) { Connected = true, Team = SceneTeams.Red, PlayerClass = 3, Alive = true, Health = 200, MaxHealth = 200 },
            ],
        };

        // The seek: frames run, none painted.
        for (int frame = 0; frame < 5; frame++)
        {
            host.BeginFrame(640, 480);
            hud.Frame(state with { CurTime = frame * 0.015f }, reset: frame == 0, paint: false);
        }

        // The shot: one painted frame on a paused clock.
        host.BeginFrame(640, 480);
        hud.Frame(state with { CurTime = 0.06f });

        List<TfPlayerPanel> cards = [.. hud.SpectatorGui.PlayerPanels.Where(panel => panel.PlayerIndex > 0)];

        cards.Count.ShouldBe(2);
        cards.Select(card => card.ClassImage?.ImageName).ShouldBe(["class_portraits/soldier_alpha", "class_portraits/medic_blue_alpha"]);
        host.List.Quads.ShouldContain(quad => quad.Texture == "vgui/class_portraits/medic_blue_alpha");
    }
}
