using System;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Scene.Hud;

/// <summary>What `CHud::IsHidden` reads: the game state and the local player.</summary>
/// <param name="InGame">`engine->IsInGame()`.</param>
/// <param name="HasLocalPlayer">Whether there is a local player.</param>
/// <param name="HideHud">The local player's `m_Local.m_iHideHUD`, or `hidehud` when that is set.</param>
/// <param name="Health">The local player's health.</param>
/// <param name="Alive">`IsAlive()`.</param>
/// <param name="MaxHealth">`GetMaxHealth()`: the player resource's `m_iMaxHealth` for the local player.</param>
/// <param name="MaxBuffedHealth">`m_Shared.GetMaxBuffedHealth()`.</param>
/// <param name="CurTime">`gpGlobals->curtime`, which element thinks are throttled by.</param>
/// <param name="Team">The local player's team: 0 unassigned, 1 spectator, 2 RED, 3 BLU.</param>
/// <param name="Ammo">What the ammo element reads of the active weapon.</param>
/// <param name="ActiveWeapon">The active weapon's entity slot, or 0 for none.</param>
public readonly record struct HudState(
    bool InGame,
    bool HasLocalPlayer,
    int HideHud,
    int Health,
    bool Alive,
    int MaxHealth = 0,
    int MaxBuffedHealth = 0,
    float CurTime = 0f,
    int Team = 0,
    TfAmmoState Ammo = default,
    int ActiveWeapon = 0);

/// <summary>`CHudElement` (game/client/hud.cpp): a HUD panel that hides by the player's `HIDEHUD` bits.</summary>
public interface IHudElement
{
    /// <summary>`m_iHiddenBits`.</summary>
    public int HiddenBits { get; }

    /// <summary>`ShouldDraw` (hud.cpp:288): not hidden by <see cref="HudVisibility.IsHidden"/>.</summary>
    /// <param name="state">The game state.</param>
    /// <returns>Whether to draw.</returns>
    /// <remarks>Render groups are not modelled: TF registers every element in "global", which nothing locks.</remarks>
    public bool ShouldDraw(HudState state) => !HudVisibility.IsHidden(state, HiddenBits);
}

/// <summary>`CHud`'s visibility rule and the `HIDEHUD_` bits (game/shared/shareddefs.h:206).</summary>
public static class HudVisibility
{
    /// <summary>`HIDEHUD_WEAPONSELECTION`.</summary>
    public const int HideWeaponSelection = 1 << 0;

    /// <summary>`HIDEHUD_ALL`.</summary>
    public const int HideAll = 1 << 2;

    /// <summary>`HIDEHUD_HEALTH`.</summary>
    public const int HideHealth = 1 << 3;

    /// <summary>`HIDEHUD_PLAYERDEAD`.</summary>
    public const int HidePlayerDead = 1 << 4;

    /// <summary>`HIDEHUD_NEEDSUIT` — the HEV suit; a TF player always "has" it, so it never hides here.</summary>
    public const int HideNeedSuit = 1 << 5;

    /// <summary>`HIDEHUD_MISCSTATUS`.</summary>
    public const int HideMiscStatus = 1 << 6;

    /// <summary>`HIDEHUD_CHAT`.</summary>
    public const int HideChat = 1 << 7;

    /// <summary>`HIDEHUD_CROSSHAIR`.</summary>
    public const int HideCrosshair = 1 << 8;

    /// <summary>`CHud::IsHidden` (hud.cpp:951).</summary>
    /// <param name="state">The game state.</param>
    /// <param name="hudFlags">The element's hidden bits.</param>
    /// <returns>Whether the element is hidden.</returns>
    /// <remarks>
    /// Not modelled: `HIDEHUD_NEEDSUIT` (`IsSuitEquipped` is HL2's; the bit is in the player's own `m_iHideHUD` in TF when
    /// it matters) and `hud_freezecamhide` during a freeze-cam screenshot, which a demo viewer never takes.
    /// </remarks>
    public static bool IsHidden(HudState state, int hudFlags)
    {
        if (!state.InGame || !state.HasLocalPlayer || (state.HideHud & HideAll) != 0)
        {
            return true;
        }

        if ((hudFlags & HidePlayerDead) != 0 && state.Health <= 0 && !state.Alive)
        {
            return true;
        }

        return (hudFlags & state.HideHud) != 0;
    }
}

/// <summary>`CBaseViewport` (game/client/game_controls/baseviewport.cpp:160): the client's full-screen, proportional root.</summary>
/// <remarks>
/// Its build group applies `scripts/HudLayout.res` to the elements parented here, by name. `CHud::Think`
/// (hud_redraw.cpp:48) is <see cref="Think"/>: each element shows or hides by its own `ShouldDraw`.
/// </remarks>
public sealed class HudViewport : VguiEditablePanel
{
    private readonly List<IHudElement> _elements = [];
    private bool _teamSent;

    /// <summary>`CBaseViewport()`: named, and proportional from the start.</summary>
    public HudViewport()
        : base(null, "CBaseViewport") =>
        Proportional = true;

    /// <summary>This frame's game state — what an element's `OnThink` reads of the local player and `gpGlobals`.</summary>
    public HudState State { get; private set; }

    /// <summary>The viewport a panel sits under, for its `OnThink` to read <see cref="State"/>; null when it has none.</summary>
    /// <param name="panel">The panel.</param>
    /// <returns>The viewport.</returns>
    public static HudViewport? Of(VguiPanel panel)
    {
        for (VguiPanel? at = panel; at is not null; at = at.Parent)
        {
            if (at is HudViewport viewport)
            {
                return viewport;
            }
        }

        return null;
    }

    /// <summary>`CHud::Think`: every element's panel shown or hidden by its `ShouldDraw`.</summary>
    /// <param name="state">The game state.</param>
    public void Think(HudState state)
    {
        // `localplayer_changeteam`, which every `CTFImagePanel` listens for (tf_imagepanel.cpp:79).
        if (state.Team != State.Team || !_teamSent)
        {
            _teamSent = true;
            SetLocalTeam(this, state.Team);
        }

        State = state;

        foreach (IHudElement element in _elements)
        {
            if (element is VguiPanel panel)
            {
                panel.Visible = element.ShouldDraw(state);
            }
        }
    }

    private static void SetLocalTeam(VguiPanel panel, int team)
    {
        if (panel is TfImagePanel image)
        {
            image.LocalTeam = team;
        }

        foreach (VguiPanel child in panel.Children)
        {
            SetLocalTeam(child, team);
        }
    }

    /// <inheritdoc/>
    protected override void OnChildAdded(VguiPanel child)
    {
        ArgumentNullException.ThrowIfNull(child);

        base.OnChildAdded(child);

        // `CHud::AddHudElement`, which each element's constructor reaches through `DECLARE_HUDELEMENT`.
        if (child is IHudElement element)
        {
            _elements.Add(element);
        }
    }
}
