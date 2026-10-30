using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Probe.Probes;
using Tf2DemoSalvage.Scene;
using Tf2DemoSalvage.Scene.Hud;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Core.Tests;

/// <summary>
/// B516's output-level assertion: the production <see cref="VguiHud"/> over Valve's stock files, on a real SourceTV
/// tournament recording, draws a tournament player card for every RED and BLU player — and none without the ConVar.
/// </summary>
/// <remarks>
/// The unit behaviour is <c>TfSpectatorGuiConformanceTests</c>. What only this can show is the wiring: that the frame the
/// viewer paints carries the cards, read off the demo's own player resource, at the positions the shipped `.res` gives.
/// Text is not drawn (no fonts), so the cards are found by their fills and textures. cp_process_f12 is a demos.tf STV
/// recording of a 6v6 match with <c>mp_tournament 1</c>.
/// </remarks>
public sealed class SpectatorGuiCorpusTests
{
    private const int Tick = 60000;

    [Test]
    public void Frame_AnStvTournamentDemoWithTheSpecGuiConVar_PaintsACardPerPlayer()
    {
        DemoTimeline timeline = TimelineCache.For(Corpus.Demo("cp_process_f12"));
        GameArchives archives = GameArchives.Open(GameInstall.Require()).WithoutCustom();
        HudState state = HudStates.For(timeline, Tick);

        state.IsHltv.ShouldBeTrue("the specimen is a SourceTV recording");
        state.ConVars.GetBool("mp_tournament").ShouldBeTrue("the specimen is a tournament match");

        int expected = state.ScoreboardPlayers!.Count(player =>
            player.Connected && player.Team is SceneTeams.Red or SceneTeams.Blu && player.PlayerClass is >= 1 and <= 9);

        (VguiHud on, VguiSurfaceHost onHost) = Painted(archives, state, specGui: true);
        (VguiHud off, VguiSurfaceHost offHost) = Painted(archives, state, specGui: false);

        on.SpectatorGui.Visible.ShouldBeTrue();
        on.SpectatorGui.LoadedRes.ShouldBe(TfSpectatorGui.TournamentRes);
        expected.ShouldBeGreaterThanOrEqualTo(12, "a 6v6 match");

        List<TfPlayerPanel> cards = [.. on.SpectatorGui.PlayerPanels.Where(panel => panel.Visible && panel.PlayerIndex > 0)];

        cards.Count.ShouldBe(expected);

        foreach (TfPlayerPanel card in cards)
        {
            Inside(onHost, card).ShouldBeGreaterThan(0, $"card for player {card.PlayerIndex} at {card.AbsX},{card.AbsY} paints nothing");

            // The portrait is the player's class from the resource, `class_portraits/<class>[_blue]_alpha`, and it is
            // what the frame drew — not the `.res`'s placeholder image.
            string portrait = "vgui/" + card.ClassImage!.ImageName;

            portrait.ShouldStartWith("vgui/class_portraits/");
            portrait.ShouldEndWith("_alpha");
            onHost.List.Quads.ShouldContain(quad => quad.Texture == portrait, $"player {card.PlayerIndex}'s portrait {portrait} is not in the frame");

            // `UpdateBorder` (tf_playerpanel.cpp:373): a living player's card wears his team's border, a dead one's the plain.
            SceneScoreboardPlayer slot = state.ScoreboardPlayers!.Single(player => player.EntityIndex == card.PlayerIndex);
            string living = slot.Team == SceneTeams.Red ? "TFFatLineBorderRedBG" : "TFFatLineBorderBlueBG";

            card.Border.ShouldNotBeNull();
            card.Border.Name.ShouldBe(slot.Alive ? living : "TFFatLineBorder", $"player {card.PlayerIndex}");
        }

        // The control: the stock client (the ConVar's default, 0) loads Spectator.res and makes no card.
        off.SpectatorGui.LoadedRes.ShouldBe(TfSpectatorGui.SpectatorRes);
        off.SpectatorGui.PlayerPanels.ShouldBeEmpty();
        offHost.List.Quads.Count.ShouldNotBe(onHost.List.Quads.Count);
    }

    /// <summary>`engine->IsHLTV()` is the ServerInfo's `is_hltv`, carried — a POV and an STV recording of ONE session differ in it.</summary>
    [Test]
    public void IsSourceTv_PovAndStvOfOneSession_IsTheServerInfosFlag()
    {
        TimelineCache.For(Corpus.Demo("tf2-2011-build4604-spec-stv-cp_granary")).IsSourceTv.ShouldBeTrue();
        TimelineCache.For(Corpus.Demo("tf2-2011-build4604-spec-pov-cp_granary")).IsSourceTv.ShouldBeFalse();
    }

    /// <summary>
    /// A POV match recording: the recorder dies, and after the freeze cam the server opens `specgui` for him
    /// (tf_player.cpp:13992) — a `VGUIMenu` the demo carries. The panel shows from then until the server closes it on spawn.
    /// </summary>
    [Test]
    public void SpectatorGuiShownAt_APovMatchRecorderWhoDies_FollowsTheServersVguiMenus()
    {
        DemoTimeline timeline = TimelineCache.For(Corpus.Demo("rgl-pug-2026-08-10-pov"));

        timeline.IsSourceTv.ShouldBeFalse();
        timeline.SpectatorGuiChanges.ShouldContain(change => change.Show, "a recorder in a real match dies at least once");
        timeline.SpectatorGuiChanges.ShouldContain(change => !change.Show, "and spawns again");

        (int opened, _) = timeline.SpectatorGuiChanges.First(change => change.Show);
        HudState state = HudStates.For(timeline, opened);

        state.SpectatorGuiShown.ShouldBeTrue();
        TfSpectatorGui.IsShown(state).ShouldBeTrue();

        // The control: the tick before it opened, from a closed panel or none at all.
        TfSpectatorGui.IsShown(HudStates.For(timeline, opened - 1)).ShouldBe(timeline.SpectatorGuiShownAt(opened - 1));
        timeline.SpectatorGuiShownAt(timeline.FirstTick - 1).ShouldBeFalse();
    }

    private static int Inside(VguiSurfaceHost host, VguiPanel panel) => host.List.Quads.Count(quad =>
        quad.X0 >= panel.AbsX && quad.X1 <= panel.AbsX + panel.Wide && quad.Y0 >= panel.AbsY && quad.Y1 <= panel.AbsY + panel.Tall);

    private static (VguiHud Hud, VguiSurfaceHost Host) Painted(GameArchives archives, HudState state, bool specGui)
    {
        Dictionary<string, string> client = new() { ["cl_use_tournament_specgui"] = specGui ? "1" : "0" };
        HudState frame = state with { ConVars = state.ConVars with { Client = client.GetValueOrDefault } };
        VguiSurfaceHost host = new(archives.Read, archives.FullPathOnDisk, new NoFonts(), _ => (64, 64));
        VguiHud hud = new(host, new EntityModelSet());

        // Three frames, as the viewer runs them: the cards are made on the first update, take their scheme on that
        // frame's layout, and are filled in and placed by the next update.
        for (int index = 0; index < 3; index++)
        {
            host.BeginFrame(1920, 1080);
            hud.Frame(frame with { CurTime = frame.CurTime + (index * 0.2f) }, reset: index == 0);
        }

        return (hud, host);
    }
}
