using System;
using System.Globalization;

namespace Tf2DemoSalvage.Scene.Hud;

/// <summary>`CHudArenaCapPointCountdown` (game/client/tf/tf_hud_arena_capturepoint.cpp:34): arena's last five seconds before its point unlocks.</summary>
/// <remarks>
/// Valve ticks it with every vgui tick; this updates each think. **Not ported:** the "mid" render group.
/// </remarks>
/// <param name="viewport">The viewport.</param>
public sealed class TfHudArenaCapPointCountdown(VguiPanel viewport) : VguiEditablePanel(viewport, "HudArenaCapPointCountdown"), IHudElement
{
    /// <inheritdoc/>
    public override string ClassName => "CHudArenaCapPointCountdown";

    /// <inheritdoc/>
    public int HiddenBits => HudVisibility.HideMiscStatus;

    /// <summary>`iTimeLeft`: `ceil( GetCapturePointTime() - curtime )` when it shows (:137-159), or null when it hides.</summary>
    /// <param name="state">The game state.</param>
    /// <returns>The seconds shown, or null.</returns>
    public static int? SecondsShown(HudState state)
    {
        if (!state.Rules.Present || state.Rules.GameType != Core.Scene.SceneGameRules.GameTypeArena || state.RoundState != HudState.RoundStateStalemate
            || !state.HasLocalPlayer)
        {
            return null;
        }

        int left = (int)MathF.Ceiling(state.Rules.CapturePointEnableTime - state.CurTime);

        return left is > 0 and <= 5 ? left : null;
    }

    /// <inheritdoc/>
    public override void ApplySchemeSettings(VguiContext context)
    {
        LoadControlSettings("resource/UI/HudArenaCapPointCountdown.res", context);
        base.ApplySchemeSettings(context);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// `OnTick` (:113), after the HUD's `ShouldDraw` has set the visibility, with the announcer's lines (:142-191);
    /// `IsInFreezeCam` hides it (:101) without stopping the lines.
    /// </remarks>
    protected override void OnThink()
    {
        if (HudViewport.Of(this) is not { } hud || !Visible)
        {
            return;
        }

        HudState state = hud.State;

        if (!state.Rules.Present || state.Rules.GameType != Core.Scene.SceneGameRules.GameTypeArena
            || state.RoundState != HudState.RoundStateStalemate || !state.HasLocalPlayer)
        {
            Visible = false;
            return;
        }

        int left = (int)MathF.Ceiling(state.Rules.CapturePointEnableTime - state.CurTime);

        // A seek lands as the skipped ticks would have left it: the lines already passed spent, not played at once.
        if (SeekLanded)
        {
            SeekLanded = false;

            for (int line = 1; line <= 5; line++)
            {
                _fire[line] = left > 5 || left <= 0 || line <= left;
            }

            _fire[0] = left > 0;
        }

        if (left is > 5 or <= 0)
        {
            if (left <= 0 && _fire[0])
            {
                _fire[0] = false;
                hud.SoundEmitter?.Invoke("Announcer.AM_CapEnabledRandom");
            }

            for (int line = 1; line <= 5; line++)
            {
                _fire[line] = true;
            }

            Visible = false;
            return;
        }

        Visible = state.ObserverMode != Core.Scene.ObserverModes.FreezeCam;
        SetDialogVariable("capturetime", left.ToString(CultureInfo.InvariantCulture));

        // One line per tick, five down to one; the last arms "capture enabled".
        for (int line = 5; line >= 1; line--)
        {
            if (left <= line && _fire[line])
            {
                _fire[line] = false;
                _fire[0] |= line == 1;
                hud.SoundEmitter?.Invoke(TfControlPointCountdown.CountdownLine(line));
                break;
            }
        }
    }

    /// <summary>Set by a seek: the next think arms the lines by the time left, as a replay of the skipped ticks would.</summary>
    public bool SeekLanded { get; set; }

    // `m_bFire0SecRemain` through `m_bFire5SecRemain`, all set at construction (:73-78).
    private readonly bool[] _fire = [true, true, true, true, true, true];
}
