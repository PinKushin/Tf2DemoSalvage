using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Hud;

/// <summary>One row of <see cref="TfHudVoiceStatus"/>: `ActiveSpeaker` (hud_voicestatus.cpp:117).</summary>
/// <param name="PlayerId">`playerId`: the speaker's entity index.</param>
/// <param name="Speaking">`bSpeaking`.</param>
/// <param name="Alpha">`fAlpha`.</param>
public readonly record struct VoiceSpeaker(int PlayerId, bool Speaking, float Alpha);

/// <summary>
/// `CHudVoiceStatus` (game/client/hud_voicestatus.cpp:92): every player speaking, a row each, stacked up from the bottom of
/// the panel in the order they began (B549).
/// </summary>
/// <remarks>
/// TF2 declares it in its shipped `scripts/hudlayout.res` as `HudVoiceStatus`. A row is the speaker's team colour at alpha
/// 128, the `hud/leaderboard_dead` icon for a dead speaker, the `voice_player` icon and the name cut to fit with "..".
/// **Not modelled, and why:** the Steam avatar (`show_avatar`, 0 in TF2's layout, and a demo carries no avatar);
/// the `#Voice_UseLocation` place name under `sv_alltalk` 0, which needs `GetLastKnownPlaceName` — a nav-mesh place the
/// demo does not carry. `HudVoiceSelfStatus` is the local player's own microphone, which a demo never has.
/// </remarks>
public sealed class TfHudVoiceStatus : VguiPanel, IHudElement
{
    /// <summary>The element's panel name.</summary>
    public const string ElementName = "HudVoiceStatus";

    private const int TeamUnassigned = 0;
    private const int TeamSpectator = 1;
    private const int TeamRed = 2;
    private const int TeamBlue = 3;
    private const string DeadImage = "hud/leaderboard_dead";

    private static readonly (byte, byte, byte, byte) White = (255, 255, 255, 255);

    private readonly List<VoiceSpeaker> _speakers = [];
    private HudTexture? _voiceIcon;
    private float? _lastThink;

    /// <summary>`CHudVoiceStatus( pName )`: "HudVoiceStatus", parented to the viewport, with its panel variables (:128-156).</summary>
    /// <param name="viewport">The viewport.</param>
    public TfHudVoiceStatus(VguiPanel viewport)
        : base(viewport, ElementName)
    {
        DeclareAnimationVar("Default", VguiPanelVarType.Font, "Default");
        DeclareAnimationVar("item_tall", VguiPanelVarType.ProportionalFloat, "16");
        DeclareAnimationVar("item_wide", VguiPanelVarType.ProportionalFloat, "160");
        DeclareAnimationVar("item_spacing", VguiPanelVarType.ProportionalFloat, "2");
        DeclareAnimationVar("show_avatar", VguiPanelVarType.Bool, "0");
        DeclareAnimationVar("show_friend", VguiPanelVarType.Bool, "1");
        DeclareAnimationVar("avatar_ypos", VguiPanelVarType.ProportionalFloat, "0");
        DeclareAnimationVar("avatar_xpos", VguiPanelVarType.ProportionalFloat, "16");
        DeclareAnimationVar("avatar_tall", VguiPanelVarType.ProportionalFloat, "16");
        DeclareAnimationVar("avatar_wide", VguiPanelVarType.ProportionalFloat, "16");
        DeclareAnimationVar("show_voice_icon", VguiPanelVarType.Bool, "1");
        DeclareAnimationVar("icon_ypos", VguiPanelVarType.ProportionalFloat, "0");
        DeclareAnimationVar("icon_xpos", VguiPanelVarType.ProportionalFloat, "24");
        DeclareAnimationVar("icon_tall", VguiPanelVarType.ProportionalFloat, "16");
        DeclareAnimationVar("icon_wide", VguiPanelVarType.ProportionalFloat, "16");
        DeclareAnimationVar("show_dead_icon", VguiPanelVarType.Bool, "1");
        DeclareAnimationVar("dead_ypos", VguiPanelVarType.ProportionalFloat, "0");
        DeclareAnimationVar("dead_xpos", VguiPanelVarType.ProportionalFloat, "0");
        DeclareAnimationVar("dead_tall", VguiPanelVarType.ProportionalFloat, "16");
        DeclareAnimationVar("dead_wide", VguiPanelVarType.ProportionalFloat, "16");
        DeclareAnimationVar("text_xpos", VguiPanelVarType.ProportionalFloat, "40");
        DeclareAnimationVar("fade_in_time", VguiPanelVarType.Real, "0.0");
        DeclareAnimationVar("fade_out_time", VguiPanelVarType.Real, "0.0");
    }

    /// <summary>`m_SpeakingList`, in order.</summary>
    public IReadOnlyList<VoiceSpeaker> Speakers => _speakers;

    /// <inheritdoc/>
    public override string ClassName => "CHudVoiceStatus";

    /// <inheritdoc/>
    /// <remarks>`SetHiddenBits( 0 )` (:170).</remarks>
    public int HiddenBits => 0;

    /// <summary>`ShouldDraw` (:316): always, except in freeze cam — the base's hidden test is overridden away.</summary>
    /// <param name="state">The game state.</param>
    /// <returns>Whether it draws.</returns>
    public bool ShouldDraw(HudState state) => state.ObserverMode != ObserverModes.FreezeCam;

    /// <summary>`g_PR->GetTeamColor` for TF (c_tf_playerresource.cpp:59-62).</summary>
    /// <param name="team">The team.</param>
    /// <returns>`COLOR_RED`, `COLOR_BLUE`, `COLOR_TF_SPECTATOR` for unassigned and spectator, else `COLOR_GREY`.</returns>
    public static (byte Red, byte Green, byte Blue, byte Alpha) TeamColor(int team) => team switch
    {
        TeamRed => (255, 64, 64, 255),
        TeamBlue => (153, 204, 255, 255),
        TeamUnassigned or TeamSpectator => (245, 229, 196, 255),
        _ => (204, 204, 204, 255),
    };

    /// <inheritdoc/>
    /// <remarks>The base's, then `VidInit`'s `gHUD.GetIcon( "voice_player" )` (:203).</remarks>
    public override void ApplySchemeSettings(VguiContext context)
    {
        base.ApplySchemeSettings(context);
        _voiceIcon = HudViewport.Of(this)?.Icons?.GetIcon("voice_player");
    }

    /// <summary>`OnThink` (:206): each player's speaking state, a new speaker added at the tail, then the fades.</summary>
    protected override void OnThink()
    {
        if (HudViewport.Of(this) is not { } viewport || !Visible)
        {
            return;
        }

        HudState state = viewport.State;

        // `gpGlobals->frametime`: the host's, so a paused frame advances nothing.
        float frameTime = _lastThink is { } last ? Math.Max(0f, state.CurTime - last) : 0f;
        _lastThink = state.CurTime;

        IReadOnlySet<int> speaking = state.Speaking ?? new HashSet<int>();

        for (int i = 0; i < _speakers.Count; i++)
        {
            _speakers[i] = _speakers[i] with { Speaking = speaking.Contains(_speakers[i].PlayerId) };
        }

        // `for iPlayerIndex = 1 .. maxClients`: a speaker not yet listed joins the tail, lowest index first.
        List<int> joining = [];

        foreach (int player in speaking)
        {
            if (player >= 1 && _speakers.FindIndex(speaker => speaker.PlayerId == player) < 0)
            {
                joining.Add(player);
            }
        }

        joining.Sort();

        foreach (int player in joining)
        {
            _speakers.Add(new VoiceSpeaker(player, true, 0f));
        }

        Fade(frameTime);
    }

    /// <summary>The fade half of `OnThink` (:274-313): in to 1 while speaking, out to 0 and removed when not.</summary>
    private void Fade(float frameTime)
    {
        float fadeIn = GetFloat("fade_in_time");
        float fadeOut = GetFloat("fade_out_time");

        for (int i = _speakers.Count - 1; i >= 0; i--)
        {
            VoiceSpeaker speaker = _speakers[i];
            float alpha = speaker.Alpha;

            if (speaker.Speaking)
            {
                alpha = fadeIn > 0f ? Math.Min(1f, alpha + (frameTime / fadeIn)) : 1f;
            }
            else
            {
                alpha = fadeOut > 0f ? alpha - (frameTime / fadeOut) : 0f;

                if (alpha <= 0f)
                {
                    _speakers.RemoveAt(i);
                    continue;
                }
            }

            _speakers[i] = speaker with { Alpha = alpha };
        }
    }

    /// <inheritdoc/>
    /// <remarks>`Paint` (:324).</remarks>
    public override void Paint(IVguiSurface surface, VguiContext context)
    {
        ArgumentNullException.ThrowIfNull(surface);

        if (_voiceIcon is null || HudViewport.Of(this) is not { } viewport)
        {
            return;
        }

        HudState state = viewport.State;
        float itemTall = GetFloat("item_tall");
        float itemWide = GetFloat("item_wide");
        float textX = GetFloat("text_xpos");
        int ypos = (int)(Tall - itemTall);
        int fontTall = 0;
        VguiFontAmalgam? font = GetFont("Default");

        if (_speakers.Count > 0 && font is not null)
        {
            surface.DrawSetTextFont(font);
            surface.DrawSetTextColor(White);
            fontTall = surface.GetFontTall(font);
        }

        foreach (VoiceSpeaker speaker in _speakers)
        {
            SceneScoreboardPlayer? resource = Resource(state, speaker.PlayerId);
            float oldAlpha = surface.AlphaMultiplier;
            surface.AlphaMultiplier = oldAlpha * speaker.Alpha;

            (byte r, byte g, byte b, _) = TeamColor(Team(state, resource));
            surface.DrawSetColor((r, g, b, 128));
            surface.DrawFilledRect(0, ypos, (int)itemWide, (int)(ypos + itemTall));

            if (GetBool("show_dead_icon") && resource is not { Alive: true })
            {
                DrawDeadIcon(surface, ypos);
            }

            if (GetBool("show_voice_icon"))
            {
                _voiceIcon.DrawSelf(
                    surface,
                    (int)GetFloat("icon_xpos"),
                    (int)(ypos + GetFloat("icon_ypos")),
                    (int)GetFloat("icon_wide"),
                    (int)GetFloat("icon_tall"),
                    White);
            }

            if (font is not null)
            {
                surface.DrawSetTextColor(White);
                surface.DrawSetTextPos((int)textX, (int)(ypos + (itemTall / 2f) - (fontTall / 2)));
                surface.DrawPrintText(Fitted(surface, font, PlayerName(state, resource, speaker.PlayerId), (int)(itemWide - textX)));
            }

            ypos = (int)(ypos - (GetFloat("item_spacing") + itemTall));
            surface.AlphaMultiplier = oldAlpha;
        }
    }

    /// <summary>The dead icon (:408-421): a textured quad at the dead icon's place, full texture, white.</summary>
    private void DrawDeadIcon(IVguiSurface surface, int ypos)
    {
        float x0 = GetFloat("dead_xpos");
        float y0 = ypos + GetFloat("dead_ypos");
        float x1 = x0 + GetFloat("dead_wide");
        float y1 = y0 + GetFloat("dead_tall");

        surface.DrawSetTexture(DeadImage);
        surface.DrawSetColor(White);
        surface.DrawTexturedPolygon([new VguiVertex(x0, y0, 0f, 0f), new VguiVertex(x1, y0, 1f, 0f), new VguiVertex(x1, y1, 1f, 1f), new VguiVertex(x0, y1, 0f, 1f)]);
    }

    /// <summary>The name cut to the row (:448-468): at the glyph that crosses the space, past the fourth, two dots end it.</summary>
    private static string Fitted(IVguiSurface surface, VguiFontAmalgam font, string name, int space)
    {
        char[] text = name.ToCharArray();
        int width = 0;

        for (int j = 0; j < text.Length; j++)
        {
            width += surface.GetCharacterWidth(font, text[j]);

            if (width > space)
            {
                if (j > 3)
                {
                    text[j - 2] = '.';
                    text[j - 1] = '.';
                    return new string(text, 0, j);
                }

                break;
            }
        }

        return new string(text);
    }

    /// <summary>The player resource's slot for an entity, or null where it has none.</summary>
    private static SceneScoreboardPlayer? Resource(HudState state, int player)
    {
        foreach (SceneScoreboardPlayer slot in state.ScoreboardPlayers ?? [])
        {
            if (slot.EntityIndex == player)
            {
                return slot;
            }
        }

        return null;
    }

    /// <summary>`C_TF_PlayerResource::GetTeam` (c_tf_playerresource.cpp:84): unassigned shows as the defenders in MvM.</summary>
    private static int Team(HudState state, SceneScoreboardPlayer? resource)
    {
        int team = resource?.Team ?? TeamUnassigned;

        return team == TeamUnassigned && state.Rules.MannVsMachine ? TeamRed : team;
    }

    /// <summary>`g_PR->GetPlayerName` (c_playerresource.cpp:145): "unconnected" for a slot neither connected nor valid.</summary>
    private static string PlayerName(HudState state, SceneScoreboardPlayer? resource, int player) =>
        resource is { Connected: true } or { Valid: true } && state.Names?.GetValueOrDefault(player) is { } name
            ? name
            : "unconnected";
}
