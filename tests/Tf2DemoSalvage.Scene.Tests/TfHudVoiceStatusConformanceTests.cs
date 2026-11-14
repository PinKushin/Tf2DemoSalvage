using System.Collections.Generic;
using System.Linq;
using System.Text;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>`CHudVoiceStatus` (game/client/hud_voicestatus.cpp:92): who is speaking, stacked up from the bottom (B549).</summary>
/// <remarks>
/// The element is 145 × 400 as TF2's `hudlayout.res` sizes it; the row keys are the class defaults (`item_tall` 16,
/// `item_wide` 160, `item_spacing` 2, `text_xpos` 40, the icon at 24, the dead icon at 0), so a test reads the code's
/// numbers rather than the layout's. Entity 3 is RED and alive, 5 BLU and dead, 7 never in the resource.
/// </remarks>
public sealed class TfHudVoiceStatusConformanceTests
{
    [Test]
    public void OnThink_ASpeakerWithNoFade_IsListedAtFullAlphaAndDroppedWhenSilent()
    {
        (TfHudVoiceStatus voice, _) = Status();

        Think(voice, Speaking(3), 1f);
        voice.Speakers.Select(speaker => (speaker.PlayerId, speaker.Alpha)).ShouldBe([(3, 1f)]);

        Think(voice, Speaking(), 1.1f);
        voice.Speakers.ShouldBeEmpty("fade_out_time 0: gone at once");
    }

    /// <summary>A new speaker joins the tail (`AddToTail`, :264), whatever their index, so the first stays at the bottom.</summary>
    [Test]
    public void Paint_TwoSpeakers_StackFromTheBottomInTheOrderTheyStarted()
    {
        (TfHudVoiceStatus voice, TextRecorder surface) = Status();
        Think(voice, Speaking(5), 1f);
        Think(voice, Speaking(3, 5), 1.1f);

        voice.Paint(surface, HudViewport.Of(voice)!.Context!);

        // ypos = 400 - 16 = 384, then 384 - (2 + 16) = 366.
        surface.Calls.Where(call => call.StartsWith("fill", System.StringComparison.Ordinal))
            .ShouldBe(["fill 0 384 160 400", "fill 0 366 160 382"]);
        surface.Runs.Select(run => run.Text).ShouldBe(["blu", "red"], "5 began first, so 5 is the bottom row");
    }

    /// <summary>`g_PR->GetTeamColor( team )` at alpha 128 (:358-360): `COLOR_RED`, `COLOR_BLUE`, `COLOR_TF_SPECTATOR` for none.</summary>
    [TestCase(3, "color 255 64 64 128")]
    [TestCase(5, "color 153 204 255 128")]
    [TestCase(7, "color 245 229 196 128")]
    public void Paint_ARow_IsTheTeamColourAtHalfAlpha(int speaker, string expected)
    {
        (TfHudVoiceStatus voice, TextRecorder surface) = Status();
        Think(voice, Speaking(speaker), 1f);

        voice.Paint(surface, HudViewport.Of(voice)!.Context!);

        surface.Calls.First(call => call.StartsWith("color", System.StringComparison.Ordinal)).ShouldBe(expected);
    }

    /// <summary>`show_dead_icon` and `g_PR-&gt;IsAlive` false (:403): `hud/leaderboard_dead`, 16 × 16 at the row's left.</summary>
    [TestCase(5, true)]
    [TestCase(3, false)]
    public void Paint_ADeadSpeaker_DrawsTheDeadIcon(int speaker, bool drawn)
    {
        (TfHudVoiceStatus voice, TextRecorder surface) = Status();
        Think(voice, Speaking(speaker), 1f);

        voice.Paint(surface, HudViewport.Of(voice)!.Context!);

        surface.Calls.Contains("texture hud/leaderboard_dead").ShouldBe(drawn);
        surface.Polygons.Contains("0,384/0,0 16,384/1,0 16,400/1,1 0,400/0,1").ShouldBe(drawn);
    }

    /// <summary>
    /// The name truncated to `item_wide - text_xpos` = 120: with 10-wide glyphs the 13th crosses it at j = 12, so
    /// [10] and [11] become dots and [12] ends it (:454-468).
    /// </summary>
    [Test]
    public void Paint_ANameWiderThanTheRow_IsCutWithTwoDots()
    {
        (TfHudVoiceStatus voice, TextRecorder surface) = Status();
        Think(voice, Speaking(3) with { Names = new Dictionary<int, string> { [3] = "abcdefghijklmno" } }, 1f);

        voice.Paint(surface, HudViewport.Of(voice)!.Context!);

        surface.Runs.Select(run => run.Text).ShouldBe(["abcdefghij.."]);
    }

    /// <summary>`IsInFreezeCam()` (:318).</summary>
    [Test]
    public void ShouldDraw_InFreezeCam_IsFalse()
    {
        (TfHudVoiceStatus voice, _) = Status();

        voice.ShouldDraw(Speaking(3) with { ObserverMode = ObserverModes.FreezeCam }).ShouldBeFalse();
        voice.ShouldDraw(Speaking(3)).ShouldBeTrue("the control");
    }

    /// <summary>
    /// A layout's `fade_out_time` (:293): alpha falls by `frametime / fade_out_time` each think, and the row goes at 0.
    /// The frame time is the think's own, so a paused frame holds it.
    /// </summary>
    [Test]
    public void OnThink_AFadeOutTime_FadesTheRowBeforeDroppingIt()
    {
        (TfHudVoiceStatus voice, _) = Status();
        voice.SetAnimationValue("fade_out_time", 0.5f);
        Think(voice, Speaking(3), 1f);

        Think(voice, Speaking(), 1.25f);
        voice.Speakers.ShouldHaveSingleItem().Alpha.ShouldBe(0.5f, 1e-6f);

        Think(voice, Speaking(), 1.25f);
        voice.Speakers.ShouldHaveSingleItem().Alpha.ShouldBe(0.5f, 1e-6f, "a paused frame is no frame time");

        Think(voice, Speaking(), 1.5f);
        voice.Speakers.ShouldBeEmpty();
    }

    private static HudState Speaking(params int[] speakers) => new(InGame: true, HasLocalPlayer: true, HideHud: 0, Health: 100, Alive: true)
    {
        Speaking = new HashSet<int>(speakers),
        Names = new Dictionary<int, string> { [3] = "red", [5] = "blu", [7] = "who" },
        ScoreboardPlayers =
        [
            new SceneScoreboardPlayer(3) { Connected = true, Team = 2, Alive = true },
            new SceneScoreboardPlayer(5) { Connected = true, Team = 3, Alive = false },
        ],
    };

    private static void Think(TfHudVoiceStatus voice, HudState state, float curTime)
    {
        HudViewport viewport = HudViewport.Of(voice)!;

        viewport.Think(state with { CurTime = curTime });
        VguiLayout.SolveTraverse(viewport, viewport.Context!);
    }

    private static (TfHudVoiceStatus Voice, TextRecorder Surface) Status()
    {
        TextRecorder surface = new();
        VguiContext context = Context(surface);
        HudViewport viewport = new() { Wide = 640, Tall = 480, Context = context };
        TfHudVoiceStatus voice = new(viewport) { Wide = 145, Tall = 400 };

        viewport.Icons = HudTextures.Load(context);
        voice.PerformApplySchemeSettings(context);

        return (voice, surface);
    }

    private static VguiContext Context(TextRecorder surface)
    {
        const string Fonts = """ "Default" { "1" { "name" "Arial" "tall" "12" } } """;
        KeyValuesTree scheme = KeyValuesTree.Load(Encoding.UTF8.GetBytes($"Scheme {{ Colors {{ }} Borders {{ }} Fonts {{ {Fonts} }} }}"), "scheme.res", _ => null);
        VguiScheme colours = VguiScheme.Load(scheme);
        VguiSchemeFonts schemeFonts = VguiSchemeFonts.Load(scheme, new VguiFontManager(new FakeGdi()), _ => null, "english", 480);
        Dictionary<string, byte[]> files = new()
        {
            ["scripts/hud_textures.txt"] = Encoding.UTF8.GetBytes("""
                "sprites/640_hud"
                {
                    TextureData
                    {
                        "voice_player" { "file" "voice/icntlk_pl" "x" "0" "y" "0" "width" "32" "height" "32" }
                    }
                }
                """),
        };

        return new VguiContext(colours, VguiBorders.Load(scheme, colours, 480), scheme.Find("Fonts")!, 640, 480, "english", schemeFonts)
        {
            Surface = surface,
            Read = files.GetValueOrDefault,
        };
    }
}
