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
/// <param name="Rules">`TFGameRules()` as the HUD reads it.</param>
/// <param name="RealTime">`gpGlobals->realtime`: wall-clock seconds, which demo speed and pause do not change.</param>
/// <param name="ObserverMode">The local player's `GetObserverMode()`.</param>
/// <param name="WeaponClass">The active weapon's server class, or null with none.</param>
/// <param name="PlayerClass">The local player's class, 0 with none.</param>
/// <param name="Conditions">The local player's conditions.</param>
/// <param name="Fov">`GetFOV()` — the field of view the view is drawn at.</param>
/// <param name="LocalIndex">The local player's entity index, 0 with none.</param>
/// <param name="ObserverTarget">`GetObserverTarget()`'s entity index, 0 with none — the HLTV camera's on SourceTV.</param>
/// <param name="Players">Every player at this tick — `cl_entitylist` for the elements that look others up.</param>
/// <param name="Names">`GetPlayerName` by entity index.</param>
/// <param name="ServerTime">`gpGlobals->curtime` on the server's clock — the one networked times such as a timer's end are on.</param>
/// <param name="RoundState">`State_Get()`: `m_iRoundState`, or null with no game rules.</param>
/// <param name="RoundTimers">Every `team_round_timer`.</param>
/// <param name="IdTarget">`C_TFPlayer::GetIDTarget()` — <c>m_iIDEntIndex</c>, precomputed by whoever runs the crosshair
/// world/entity trace (<see cref="Tf2DemoSalvage.Scene.Hud.IdTargetTrace"/>); null when nothing has computed it yet, in
/// which case <see cref="Tf2DemoSalvage.Scene.Hud.TfMainTargetId"/> treats it as "no target" rather than guessing.</param>
/// <param name="Teams">Every `CTFTeam`.</param>
/// <param name="WorldToScreen">
/// `engine->WorldToScreenMatrix()`: the view's world-to-clip matrix, row-major with the translation in the last row (this
/// project's convention — <c>FreeCamera.ToMatrix</c>); null where no view is drawn.
/// </param>
/// <param name="Buildings">Every Engineer building — `cl_entitylist` for the target ID's object branch.</param>
/// <param name="ScoreboardPlayers">
/// Every player slot `CTFClientScoreBoardDialog::UpdatePlayerList` would list, off the player
/// resource — see <see cref="Tf2DemoSalvage.Core.Scene.SceneScoreboardPlayer"/>.
/// </param>
/// <param name="ConVars">
/// Every console variable the HUD reads, by name — `ConVarRef` / `cvar->FindVar` rather than a field per var. A
/// replicated var reads the server's value off the demo; one nothing set reads Valve's declared default.
/// </param>
/// <param name="KeyLookupBinding">
/// `engine->Key_LookupBinding( command )` (e.g. tf_hud_target_id.cpp:1034's "+attack2"): the key bound to a command in
/// the viewer's own binding table (D101), or null when nothing is bound — shown as no key rather than a guessed one.
/// Null as a whole where no bindings are open.
/// </param>
/// <param name="AccountIds">Each player's `userinfo` `friendsID` by entity index — see <see cref="AccountId"/>.</param>
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
    int ActiveWeapon = 0,
    Core.Scene.SceneGameRules Rules = default,
    float RealTime = 0f,
    int ObserverMode = 0,
    string? WeaponClass = null,
    int PlayerClass = 0,
    Core.Scene.PlayerConditions Conditions = default,
    float Fov = 90f,
    int LocalIndex = 0,
    int ObserverTarget = 0,
    IReadOnlyList<Core.Scene.ScenePlayer>? Players = null,
    IReadOnlyDictionary<int, string>? Names = null,
    float ServerTime = 0f,
    int? RoundState = null,
    IReadOnlyList<Core.Scene.SceneRoundTimer>? RoundTimers = null,
    int? IdTarget = null,
    IReadOnlyList<Core.Scene.SceneTeam>? Teams = null,
    IReadOnlyList<Core.Scene.SceneBuilding>? Buildings = null,
    IReadOnlyList<Core.Scene.SceneScoreboardPlayer>? ScoreboardPlayers = null,
    float[]? WorldToScreen = null,
    HudConVars ConVars = default,
    Func<string, string?>? KeyLookupBinding = null,
    IReadOnlyDictionary<int, uint>? AccountIds = null)
{
    /// <summary>
    /// `C_BasePlayer::GetSteamID( &amp;id ).GetAccountID()` (c_baseplayer.cpp:2878) by entity index: the `userinfo` `friendsID`,
    /// and 0 — the default `CSteamID`'s account — where there is none or it is 0, as `GetSteamID` then fails.
    /// </summary>
    /// <param name="index">The entity index.</param>
    /// <returns>The account ID.</returns>
    public uint AccountId(int index) => AccountIds?.GetValueOrDefault(index) ?? 0u;

    /// <summary>`cl_entitylist->GetEnt` for a building: the one at that index, or null.</summary>
    /// <param name="index">The entity index.</param>
    public Core.Scene.SceneBuilding? Building(int index)
    {
        foreach (Core.Scene.SceneBuilding building in Buildings ?? [])
        {
            if (building.EntityIndex == index)
            {
                return building;
            }
        }

        return null;
    }

    /// <summary>
    /// `CTFPlayerShared::GetMaxBuffedHealth` (tf_player_shared.cpp:2235) on the client: `tf_max_health_boost` (1.5,
    /// `FCVAR_DEVELOPMENTONLY`) of the buffing base floored to a 5, never below the larger of the max and current health.
    /// </summary>
    /// <param name="maxHealthForBuffing">`GetMaxHealthForBuffing()` — or, for a disguise, `GetDisguiseMaxHealth()` (:2299).</param>
    /// <param name="maxHealth">`GetMaxHealth()`.</param>
    /// <param name="health">`GetHealth()`.</param>
    /// <returns>The overheal maximum.</returns>
    public static int GetMaxBuffedHealth(int maxHealthForBuffing, int maxHealth, int health)
    {
        int roundDown = (int)MathF.Floor(maxHealthForBuffing * 1.5f / 5f) * 5;

        // "Don't allow overheal total to be less than the buffable + unbuffable max health or the current health" (:2258).
        return Math.Max(roundDown, Math.Max(maxHealth, health));
    }

    /// <summary>`GR_STATE_STALEMATE` (teamplayroundbased_gamerules.h:69).</summary>
    public const int RoundStateStalemate = 7;

    /// <summary>A `team_round_timer` by entity index, or null — `ClientEntityList().GetEnt` cast to `CTeamRoundTimer`.</summary>
    /// <param name="index">The entity index.</param>
    public Core.Scene.SceneRoundTimer? RoundTimer(int index)
    {
        foreach (Core.Scene.SceneRoundTimer timer in RoundTimers ?? [])
        {
            if (timer.EntityIndex == index)
            {
                return timer;
            }
        }

        return null;
    }

    /// <summary>`GetGlobalTFTeam( iTeamNum )`: the team by its team number, or null.</summary>
    /// <param name="teamNumber">2 for RED, 3 for BLU.</param>
    public Core.Scene.SceneTeam? TeamStanding(int teamNumber)
    {
        foreach (Core.Scene.SceneTeam team in Teams ?? [])
        {
            if (team.TeamNumber == teamNumber)
            {
                return team;
            }
        }

        return null;
    }

    /// <summary>`cl_entitylist->GetEnt` for a player: the one at that index, or null.</summary>
    /// <param name="index">The entity index.</param>
    /// <returns>The player.</returns>
    public Core.Scene.ScenePlayer? Player(int index)
    {
        foreach (Core.Scene.ScenePlayer player in Players ?? [])
        {
            if (player.EntityIndex == index)
            {
                return player;
            }
        }

        return null;
    }
}

/// <summary>`CHudElement` (game/client/hud.cpp): a HUD panel that hides by the player's `HIDEHUD` bits.</summary>
public interface IHudElement
{
    /// <summary>`m_iHiddenBits`.</summary>
    public int HiddenBits { get; }

    /// <summary>`m_HudRenderGroups`: "global" from the constructor (hud.cpp:245), plus whatever the element registers.</summary>
    public IReadOnlyList<string> RenderGroups => HudVisibility.GlobalOnly;

    /// <summary>`GetRenderGroupPriority` (hud.cpp:372): 0 unless the element overrides it.</summary>
    public int RenderGroupPriority => 0;

    /// <summary>`CHudElement::ShouldDraw` (hud.cpp:288): not hidden by <see cref="HudVisibility.IsHidden"/>, nor locked out.</summary>
    /// <param name="state">The game state.</param>
    /// <returns>Whether to draw.</returns>
    public bool ShouldDraw(HudState state) => HudVisibility.ShouldDraw(state, this);
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

    /// <summary>`HIDEHUD_TARGET_ID` (shareddefs.h:224).</summary>
    public const int HideTargetId = 1 << 16;

    /// <summary>`HIDEHUD_MATCH_STATUS`.</summary>
    public const int HideMatchStatus = 1 << 17;

    /// <summary>Only "global", which `CHudElement`'s constructor registers every element in.</summary>
    public static IReadOnlyList<string> GlobalOnly { get; } = ["global"];

    /// <summary>`CHudElement::ShouldDraw` (hud.cpp:288): not hidden, and no group of its locked against it.</summary>
    /// <param name="state">The game state.</param>
    /// <param name="element">The element.</param>
    /// <returns>Whether to draw.</returns>
    public static bool ShouldDraw(HudState state, IHudElement element)
    {
        ArgumentNullException.ThrowIfNull(element);

        if (IsHidden(state, element.HiddenBits))
        {
            return false;
        }

        if (element is not VguiPanel panel || HudViewport.Of(panel) is not { } viewport)
        {
            return true;
        }

        foreach (string group in element.RenderGroups)
        {
            if (viewport.IsRenderGroupLockedFor(element, group))
            {
                return false;
            }
        }

        return true;
    }

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
    private readonly Dictionary<string, List<IHudElement>> _lockers = new(StringComparer.Ordinal);
    private bool _teamSent;

    private const string AnimationManifest = "scripts/hudanimations_manifest.txt";

    /// <summary>`CBaseViewport()`: named, proportional from the start, and its animation controller made.</summary>
    public HudViewport()
        : base(null, "CBaseViewport")
    {
        Proportional = true;
        Animations = new VguiAnimationController(this);
    }

    /// <summary>`m_pAnimController` — `GetViewportAnimationController()`.</summary>
    public VguiAnimationController Animations { get; }

    /// <summary>`gHUD`'s icons, loaded once (`CHud::Init`'s `m_bHudTexturesLoaded`), or null before the first scheme.</summary>
    public HudTextures? Icons { get; set; }

    /// <summary>An item's full name as its description shows it, by definition and quality — or null where nothing names items.</summary>
    public Func<int?, int, string?>? ItemName { get; set; }

    /// <summary>`CALL_ATTRIB_HOOK_FLOAT_ON_OTHER( player, value, name )`: a player's attributes applied to a value; null where no schema is open.</summary>
    public Func<Core.Scene.ScenePlayer, string, float, float>? PlayerAttribute { get; set; }

    /// <summary>
    /// `CALL_ATTRIB_HOOK_FLOAT( value, name )` on one of the player's weapons — `AttributeHooks.OnWeapon`; null where no
    /// schema is open.
    /// </summary>
    public Func<Core.Scene.ScenePlayer, Core.Scene.SceneItem, string, float, float>? WeaponAttribute { get; set; }

    /// <summary>`GetItemSchema()`: `items_game.txt`, for an item's per-class slot and rarity color; null where none is open.</summary>
    public Content.Assets.ItemSchema? Items { get; set; }

    /// <summary>`GetPlayerClassData( class )->GetModelName()` (tf_playermodelpanel.cpp:225): the class scripts' models; null where no install is open.</summary>
    public Content.Assets.PlayerClassModels? ClassModels { get; set; }

    /// <summary>The weapon and class scripts, for what a weapon's script tells the HUD; null where no install is open.</summary>
    public TfWeaponData? Scripts { get; set; }

    /// <summary>The context this frame runs under — what `OnThink` hands the controller.</summary>
    public VguiContext? Context { get; set; }

    /// <summary>`LoadHudAnimations` (baseviewport.cpp): each `file` in the manifest, the first wiping what was loaded.</summary>
    /// <param name="context">The scheme, screen and filesystem.</param>
    /// <returns>Whether the manifest was found.</returns>
    public bool LoadHudAnimations(VguiContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Read?.Invoke(AnimationManifest) is not { } bytes)
        {
            return false;
        }

        bool clear = true;

        foreach (Content.Assets.KeyValuesTree entry in Content.Assets.KeyValuesTree.Load(bytes, AnimationManifest, context.Read).Children)
        {
            if (string.Equals(entry.Name, "file", StringComparison.OrdinalIgnoreCase))
            {
                Animations.SetScriptFile(this, entry.Value ?? string.Empty, clear, context);
                clear = false;
            }
        }

        return true;
    }

    /// <summary>`CBaseViewport::OnThink`: the animations run at `curtime`.</summary>
    protected override void OnThink()
    {
        if (Context is { } context)
        {
            Animations.UpdateAnimations(State.CurTime, context);
        }
    }

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

    /// <summary>The ConVars a panel reads — global in the engine, so any panel's view of them is the viewport's.</summary>
    /// <param name="panel">The panel.</param>
    /// <returns>The lookup; the SDK defaults when the panel has no viewport.</returns>
    public static HudConVars ConVarsOf(VguiPanel panel) => (Of(panel)?.State ?? default).ConVars;

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

    /// <summary>`CHud::LockRenderGroup` (hud.cpp:1016): an element joins the group's lockers once.</summary>
    /// <param name="group">The group's name.</param>
    /// <param name="locker">The element hiding lower-priority ones.</param>
    public void LockRenderGroup(string group, IHudElement locker)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(locker);

        if (!_lockers.TryGetValue(group, out List<IHudElement>? lockers))
        {
            _lockers[group] = lockers = [];
        }

        if (!lockers.Contains(locker))
        {
            lockers.Add(locker);
        }
    }

    /// <summary>`CHud::UnlockRenderGroup` (hud.cpp:1065).</summary>
    /// <param name="group">The group's name.</param>
    /// <param name="locker">The element releasing its lock.</param>
    public void UnlockRenderGroup(string group, IHudElement locker)
    {
        ArgumentNullException.ThrowIfNull(group);

        if (_lockers.TryGetValue(group, out List<IHudElement>? lockers))
        {
            lockers.Remove(locker);
        }
    }

    /// <summary>
    /// `CHud::IsRenderGroupLockedFor` (hud.cpp:1104): the queue's head — the highest-priority locker — hides an element
    /// of strictly lower priority.
    /// </summary>
    /// <param name="element">The element asking.</param>
    /// <param name="group">One of its groups.</param>
    /// <returns>Whether that group hides it.</returns>
    /// <remarks>**Interpolated:** of lockers of equal top priority the first to lock is the head; a heap's order among ties is its own.</remarks>
    public bool IsRenderGroupLockedFor(IHudElement element, string group)
    {
        ArgumentNullException.ThrowIfNull(element);

        if (!_lockers.TryGetValue(group, out List<IHudElement>? lockers) || lockers.Count == 0)
        {
            return false;
        }

        IHudElement head = lockers[0];

        foreach (IHudElement locker in lockers)
        {
            if (locker.RenderGroupPriority > head.RenderGroupPriority)
            {
                head = locker;
            }
        }

        return !ReferenceEquals(head, element) && head.RenderGroupPriority > element.RenderGroupPriority;
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
