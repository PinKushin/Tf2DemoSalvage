using System;
using System.Globalization;

namespace Tf2DemoSalvage.Scene.Hud;

/// <summary>`CHudArenaCapPointCountdown` (game/client/tf/tf_hud_arena_capturepoint.cpp:34): arena's last five seconds before its point unlocks.</summary>
/// <remarks>
/// Valve ticks it with every vgui tick; this updates each think. **Not ported:** the announcer lines (:142-191) and the "mid"
/// render group.
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
    /// <remarks>`OnTick` (:113), after the HUD's `ShouldDraw` has set the visibility; `IsInFreezeCam` hides it (:101).</remarks>
    protected override void OnThink()
    {
        if (HudViewport.Of(this) is not { } hud || !Visible)
        {
            return;
        }

        int? seconds = hud.State.ObserverMode == Core.Scene.ObserverModes.FreezeCam ? null : SecondsShown(hud.State);

        Visible = seconds is not null;

        if (seconds is { } shown)
        {
            SetDialogVariable("capturetime", shown.ToString(CultureInfo.InvariantCulture));
        }
    }
}
