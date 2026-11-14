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
/// B549 item 2's output-level assertion: the production <see cref="VguiHud"/> over TF2's shipped `hudlayout.res` and
/// `hud_textures.txt` paints a `HudVoiceStatus` row for a speaker in the speaker's team colour, and nothing without one.
/// </summary>
/// <remarks>
/// The unit behaviour is <c>TfHudVoiceStatusConformanceTests</c>; what only this shows is the wiring — the element made,
/// laid out where TF2's layout puts it, and finding its `voice_player` icon. No text is drawn (no fonts).
/// </remarks>
public sealed class VoiceStatusCorpusTests
{
    private const int Tick = 60000;

    [Test]
    public void Frame_ASpeakerOnTheStockLayout_PaintsATeamRowWithTheVoiceIcon()
    {
        DemoTimeline timeline = TimelineCache.For(Corpus.Demo("cp_process_f12"));
        GameArchives archives = GameArchives.Open(GameInstall.Require()).WithoutCustom();
        HudState state = HudStates.For(timeline, Tick);
        SceneScoreboardPlayer speaker = state.ScoreboardPlayers!.First(player => player.Connected && player.Team == SceneTeams.Red);

        (VguiHud silent, VguiSurfaceHost silentHost) = SpectatorGuiCorpusTests.Painted(archives, state, specGui: false);
        Inside(silentHost, silent.VoiceStatus).ShouldBeEmpty("the control: nobody speaking paints nothing there");

        (VguiHud hud, VguiSurfaceHost host) = SpectatorGuiCorpusTests.Painted(
            archives, state with { Speaking = new HashSet<int> { speaker.EntityIndex } }, specGui: false);
        TfHudVoiceStatus voice = hud.VoiceStatus;
        List<VguiQuad> quads = Inside(host, voice);

        voice.Visible.ShouldBeTrue();
        (voice.AbsX + voice.Wide).ShouldBe(1920, "xpos r145: flush with the right edge");
        quads.Exists(quad => quad.Texture == null && quad.Red == 255 && quad.Green == 64 && quad.Blue == 64 && quad.AlphaTopLeft == 128)
            .ShouldBeTrue("RED's row: COLOR_RED at alpha 128");
        quads.ShouldContain(quad => quad.Texture != null, "the voice_player icon");
    }

    private static List<VguiQuad> Inside(VguiSurfaceHost host, VguiPanel panel) =>
        [.. host.List.Quads.Where(quad =>
            quad.X0 >= panel.AbsX && quad.X1 <= panel.AbsX + panel.Wide && quad.Y0 >= panel.AbsY && quad.Y1 <= panel.AbsY + panel.Tall)];
}
