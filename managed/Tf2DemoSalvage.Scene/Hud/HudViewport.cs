using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;

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
/// <param name="IdEntities">Every dropped weapon and revive marker — `cl_entitylist` for the target ID's generic branch.</param>
/// <param name="WeaponPickupTraceHit">
/// What `GetDroppedWeaponInRange`'s trace stops on (tf_player_shared.cpp:14761): `TF_WEAPON_PICKUP_RANGE` from the local
/// eye, `MASK_SOLID | CONTENTS_DEBRIS` — precomputed by whoever runs the ID trace, null when nothing was hit or run.
/// </param>
/// <param name="EyePosition">The local player's `EyePosition()`, or null where no first-person eye is known.</param>
/// <param name="Flags">The local player's `GetFlags()`: `m_fFlags`, 0 where unsent.</param>
/// <param name="FrozenFlag">`FL_FROZEN` in the demo's own flag layout — <see cref="Core.Scene.PlayerFlags.Frozen"/>.</param>
/// <param name="IsHltv">`engine->IsHLTV()`: the demo's `svc_ServerInfo` `is_hltv`.</param>
/// <param name="Objectives">`ObjectiveResource()`, or null when the demo has none.</param>
/// <param name="SpectatorGuiShown">Whether the server last opened `specgui` for the recorder with a `VGUIMenu`.</param>
/// <param name="TouchedPoint">`m_iCurrentCP`: the control point the recorder stands on, or -1.</param>
/// <param name="EscortBlue">The BLU payload panel's escort state.</param>
/// <param name="EscortRed">The RED payload panel's escort state.</param>
/// <param name="CaptureFlags">`ICaptureFlagAutoList` and `ICaptureZoneAutoList`, or null on a map without them.</param>
/// <param name="Passtime">`g_pPasstimeLogic`, its ball and the goals, or null outside Pass Time.</param>
/// <param name="WorldTrace">The world alone between two points, the fraction, 1 when clear; null where nothing traces.</param>
/// <param name="RobotDestruction">`CTFRobotDestructionLogic::GetRobotDestructionLogic()` and its robot groups, or null outside the mode.</param>
/// <param name="View">`CalcView`'s eye and angles: the camera the last frame was drawn through, or null.</param>
/// <param name="LevelBaseName">
/// `Q_FileBase( engine->GetLevelName() )`: the demo's ServerInfo map with any folder dropped — `GetLevelName` is
/// `maps/NAME.bsp`, so `workshop/cp_x.ugc123` arrives as `cp_x.ugc123` — or null with none.
/// </param>
/// <param name="Speaking">
/// `GetClientVoiceMgr()->IsPlayerSpeaking` by entity index: every speaker whose voice channel is sounding — `VoiceStatus`
/// true at its start, false at its free (B549) — or null where no voice plays.
/// </param>
/// <param name="FreezeCallouts">
/// What the freeze panel's callouts point at (B752): the local player's spawned gibs and his ragdoll, gathered when the
/// panel asks — once, a second after the freeze cam starts — or null where nothing can be gathered.
/// </param>
/// <param name="DesiredPlayerClass">
/// The local player's `m_Shared.GetDesiredPlayerClassIndex()`, `TF_CLASS_UNDEFINED` (0) where unsent or with no local
/// player — the class whose tips the spectator bar shows (B733).
/// </param>
/// <param name="Coaching">The local player's `m_bIsCoaching`: the spectator GUI's `m_bCoaching` follows it (B733).</param>
/// <param name="Student">The local player's `m_hStudent` as an entity index, 0 for none.</param>
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
    IReadOnlyDictionary<int, uint>? AccountIds = null,
    IReadOnlyList<Core.Scene.SceneIdEntity>? IdEntities = null,
    int? WeaponPickupTraceHit = null,
    (float X, float Y, float Z)? EyePosition = null,
    int Flags = 0,
    int FrozenFlag = Core.Scene.PlayerFlags.FrozenCurrent,
    bool IsHltv = false,
    bool SpectatorGuiShown = false,
    Core.Scene.SceneObjectives? Objectives = null,
    int TouchedPoint = -1,
    Core.Scene.SceneEscort EscortBlue = default,
    Core.Scene.SceneEscort EscortRed = default,
    Core.Scene.SceneFlags? CaptureFlags = null,
    Core.Scene.ScenePasstime? Passtime = null,
    FreeCamera? View = null,
    Func<System.Numerics.Vector3, System.Numerics.Vector3, float>? WorldTrace = null,
    Core.Scene.SceneRobotDestruction? RobotDestruction = null,
    string? LevelBaseName = null,
    IReadOnlySet<int>? Speaking = null,
    Func<FreezeCalloutSources?>? FreezeCallouts = null,
    int DesiredPlayerClass = 0,
    bool Coaching = false,
    int Student = 0)
{
    /// <summary>`cl_entitylist->GetEnt` for a dropped weapon or revive marker: the one at that index, or null.</summary>
    /// <param name="index">The entity index.</param>
    /// <returns>The entity.</returns>
    public Core.Scene.SceneIdEntity? IdEntity(int index)
    {
        foreach (Core.Scene.SceneIdEntity entity in IdEntities ?? [])
        {
            if (entity.EntityIndex == index)
            {
                return entity;
            }
        }

        return null;
    }

    /// <summary>
    /// `C_BasePlayer::GetSteamID( &amp;id ).GetAccountID()` (c_baseplayer.cpp:2878) by entity index: the `userinfo` `friendsID`,
    /// and 0 — the default `CSteamID`'s account — where there is none or it is 0, as `GetSteamID` then fails.
    /// </summary>
    /// <param name="index">The entity index.</param>
    /// <returns>The account ID.</returns>
    public uint AccountId(int index) => AccountIds?.GetValueOrDefault(index) ?? 0u;

    /// <summary>
    /// `GetPlayerByAccountID` (econ_item_view.cpp:1955): the first present player, by index, whose `GetSteamID` succeeds
    /// with that account — so never for account 0, which no player's `GetSteamID` answers.
    /// </summary>
    /// <param name="accountId">The account.</param>
    /// <returns>The player, or null.</returns>
    public Core.Scene.ScenePlayer? PlayerByAccountId(uint accountId)
    {
        Core.Scene.ScenePlayer? found = null;

        foreach (Core.Scene.ScenePlayer candidate in Players ?? [])
        {
            uint friends = AccountId(candidate.EntityIndex);

            if (friends != 0u && friends == accountId && (found is null || candidate.EntityIndex < found.Value.EntityIndex))
            {
                found = candidate;
            }
        }

        return found;
    }

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
        // By index, not `foreach`: an interface's enumerator is a heap object on every call, and the HUD asks per frame.
        IReadOnlyList<Core.Scene.ScenePlayer> players = Players ?? [];

        for (int at = 0; at < players.Count; at++)
        {
            if (players[at].EntityIndex == index)
            {
                return players[at];
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
    private int? _teamSent;

    private const string AnimationManifest = "scripts/hudanimations_manifest.txt";

    /// <summary>`CBaseViewport()`: named, proportional from the start, and its animation controller made.</summary>
    public HudViewport()
        : base(null, "CBaseViewport")
    {
        Proportional = true;
        Animations = new VguiAnimationController(this);
    }

    /// <summary>`vgui::MDLCache()`, for the model panels a `.res` file makes; null where no models are open.</summary>
    public IMdlCache? MdlCache { get; set; }

    /// <summary>The <c>game_newmap</c> game event, as every <c>CModelPanel</c> under the viewport hears it.</summary>
    /// <remarks>
    /// Each <c>CModelPanel</c> listens for it from its constructor (<c>basemodelpanel.cpp:56</c>) and marks itself dirty, so
    /// its models are set up again from the new level's (<c>:196-205</c>; B779 review 2). Only those panels are walked here:
    /// the viewport's own <c>game_newmap</c> (the spectator GUI's) is carried by the timeline instead (B516).
    /// </remarks>
    public void NewMap() => NewMap(this);

    private static void NewMap(VguiPanel panel)
    {
        if (panel is VguiModelPanel modelPanel)
        {
            modelPanel.OnGameEvent(VguiModelPanel.GameNewMap);
        }

        foreach (VguiPanel child in panel.Children)
        {
            NewMap(child);
        }
    }

    /// <summary>`m_pAnimController` — `GetViewportAnimationController()`.</summary>
    public VguiAnimationController Animations { get; }

    /// <summary>`gHUD`'s icons, loaded once (`CHud::Init`'s `m_bHudTexturesLoaded`), or null before the first scheme.</summary>
    public HudTextures? Icons { get; set; }

    /// <summary>
    /// `CEconItemView::GetItemName()` (econ_item_view.cpp:1131-1145): the item's description name line, built from its own
    /// attributes; empty where nothing names items.
    /// </summary>
    /// <param name="item">The item.</param>
    /// <returns>The name.</returns>
    /// <remarks>
    /// The networked quality never prefixes it (B755): the name reads `GetMarketQuality()`, which is `GetQuality()`
    /// (econ_item_interface.cpp:409), and with no backpack in a demo that is `AE_NORMAL` (econ_item_view.cpp:446-456).
    /// </remarks>
    public string ItemName(SceneItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        // The last item asked for is kept, as `m_pDescription` keeps its built description (econ_item_view.cpp:650-674):
        // a HUD line names the same item every frame it shows.
        if (Names() is not { } names)
        {
            return string.Empty;
        }

        if (!ReferenceEquals(_lastNamed.Item, item))
        {
            _lastNamed = (item, TfItemView.From(item, names.Schema) is { } view ? TfItemDescription.FullItemName(view, names) : string.Empty);
        }

        return _lastNamed.Name;
    }

    /// <summary>
    /// `CEconItemLocalizedFullNameGenerator( provider, def, GetItemQuality() )`, as the dropped weapon's ID
    /// (tf_hud_target_id.cpp:953, :957) and the "carrying" panel (tf_hud_playerstatus.cpp:323) call it: the networked
    /// quality lands in `bUseProperName`, so a quality other than 0 takes the article, and the name is the definition's
    /// at `AE_UNIQUE` (econ_item_description.h:354, .cpp:3814-3820). Empty where nothing names items.
    /// </summary>
    /// <param name="definition">`m_iItemDefinitionIndex`.</param>
    /// <param name="entityQuality">`m_iEntityQuality`.</param>
    /// <returns>The name.</returns>
    public string DefinitionName(int definition, int entityQuality)
    {
        if (Names() is not { } names)
        {
            return string.Empty;
        }

        bool useProperName = entityQuality != 0;

        if (!_definitionNames.TryGetValue((definition, useProperName), out string? name))
        {
            name = TfItemDescription.DefinitionFullName(definition, names, useProperName);
            _definitionNames[(definition, useProperName)] = name;
        }

        return name;
    }

    /// <summary>The paint kit definitions items are named with (B754), or null where there are none.</summary>
    public PaintKitDefinitions? PaintKits { get; set; }

    /// <summary>What an item panel's model is forced to and composited with (B754); null where no install supplies it.</summary>
    public WeaponModels? Weapons { get; set; }

    private TfItemDescriptionContext? _names;
    private VguiContext? _namesContext;
    private PaintKitDefinitions? _namesPaintKits;
    private (SceneItem? Item, string Name) _lastNamed = (null, string.Empty);
    private readonly Dictionary<(int Definition, bool UseProperName), string> _definitionNames = [];

    /// <summary>The schema and localizer names are built with; the kept names are dropped when either changes.</summary>
    private TfItemDescriptionContext? Names()
    {
        if (Items is not { } schema)
        {
            return null;
        }

        if (_names is null || !ReferenceEquals(_names.Schema, schema) || !ReferenceEquals(_namesContext, Context)
            || !ReferenceEquals(_namesPaintKits, PaintKits))
        {
            VguiContext? context = Context;
            PaintKitDefinitions? kits = PaintKits;

            _names = new TfItemDescriptionContext
            {
                Schema = schema,
                Find = token => context?.Localize?.Invoke(token),
                PaintKitToken = kit => kits?.DescriptionToken(kit),
            };
            _namesContext = context;
            _namesPaintKits = kits;
            _lastNamed = (null, string.Empty);
            _definitionNames.Clear();
        }

        return _names;
    }

    /// <summary>`CALL_ATTRIB_HOOK_FLOAT_ON_OTHER( player, value, name )`: a player's attributes applied to a value; null where no schema is open.</summary>
    public Func<Core.Scene.ScenePlayer, string, float, float>? PlayerAttribute { get; set; }

    /// <summary>
    /// `CALL_ATTRIB_HOOK_FLOAT( value, name )` on one of the player's weapons — `AttributeHooks.OnWeapon`; null where no
    /// schema is open.
    /// </summary>
    public Func<Core.Scene.ScenePlayer, Core.Scene.SceneItem, string, float, float>? WeaponAttribute { get; set; }

    /// <summary>`GetItemSchema()`: `items_game.txt`, for an item's per-class slot and rarity color; null where none is open.</summary>
    public Content.Assets.ItemSchema? Items { get; set; }

    /// <summary>`g_pParticleSystemMgr`'s definitions by name — the map's `.pcf` systems; null before a map loads.</summary>
    public IReadOnlyDictionary<string, Content.Assets.ParticleSystem>? ParticleSystems { get; set; }

    /// <summary>The particle materials by normalised name; null before a map loads.</summary>
    public IReadOnlyDictionary<string, ParticleMaterial>? ParticleMaterials { get; set; }

    /// <summary>`GetPlayerClassData( class )->GetModelName()` (tf_playermodelpanel.cpp:225): the class scripts' models; null where no install is open.</summary>
    public Content.Assets.PlayerClassModels? ClassModels { get; set; }

    /// <summary>The weapon and class scripts, for what a weapon's script tells the HUD; null where no install is open.</summary>
    public TfWeaponData? Scripts { get; set; }

    /// <summary>`EmitSound` on the local player for an element that has no emitter of its own — the countdowns' announcer.</summary>
    public HudSoundEmitter? SoundEmitter { get; set; }

    /// <summary>The context this frame runs under — what `OnThink` hands the controller.</summary>
    public VguiContext? Context { get; set; }

    /// <inheritdoc/>
    protected override VguiContext? RootScheme => Context;

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

    /// <summary>
    /// `gpGlobals->framecount`: one more each HUD frame. The viewport is the root of the think traversal, so it counts
    /// before any element thinks, and every element of a frame reads the same value.
    /// </summary>
    public int FrameCount { get; private set; }

    /// <summary>
    /// `g_pMaterialSystem->IsMaterialLoaded` and `materials->IsTextureLoaded`: the item images found and the item icons
    /// copied so far. Global in the engine; the HUD's lifetime here.
    /// </summary>
    internal HashSet<string> LoadedMaterials { get; } = new(StringComparer.OrdinalIgnoreCase);

    private readonly List<TickEntry> _tickSignals = [];
    private readonly List<TickEntry> _tickWalk = [];

    /// <summary>`system()->GetTimeMillis()` for a frame.</summary>
    /// <param name="state">The frame's state.</param>
    /// <returns>Milliseconds.</returns>
    public static int TimeMillis(HudState state) => VguiTickInterval.TimeMillis(state.RealTime);

    /// <summary>
    /// `ivgui()->AddTickSignal( panel, interval )`: a new panel joins the tail; a listed one, even one marked removed during
    /// the walk, restarts its interval and loses the mark (`0x180019fd0` finds it by panel and clears its +0x10 byte).
    /// </summary>
    /// <param name="signal">The panel.</param>
    /// <param name="intervalMilliseconds">The interval; 0 ticks every frame.</param>
    public void AddTickSignal(IVguiTickSignal signal, int intervalMilliseconds = 0) =>
        AddTickSignal(signal, intervalMilliseconds, atHead: false);

    /// <summary>
    /// `ivgui()->AddTickSignalToHead( panel, interval )`: a new panel joins the head, which the walk reaches last; a listed one
    /// keeps its place and restarts its interval (`vgui2.dll` `0x1800196e0` inserts only what `0x180019fd0` returns new).
    /// </summary>
    /// <param name="signal">The panel.</param>
    /// <param name="intervalMilliseconds">The interval; 0 ticks every frame.</param>
    public void AddTickSignalToHead(IVguiTickSignal signal, int intervalMilliseconds = 0) =>
        AddTickSignal(signal, intervalMilliseconds, atHead: true);

    /// <summary>
    /// `ivgui()->RemoveTickSignal( panel )` (`vgui2.dll` `0x18001b170`): removed at once outside the walk; during it, marked,
    /// skipped, and removed after.
    /// </summary>
    /// <param name="signal">The panel.</param>
    public void RemoveTickSignal(IVguiTickSignal signal)
    {
        int at = _tickSignals.FindIndex(entry => entry.Signal == signal);

        if (at < 0)
        {
            return;
        }

        if (_walkingTicks)
        {
            _tickSignals[at].Removed = true;
        }
        else
        {
            _tickSignals.RemoveAt(at);
        }
    }

    private bool _walkingTicks;

    private void AddTickSignal(IVguiTickSignal signal, int intervalMilliseconds, bool atHead)
    {
        int now = TimeMillis(State);
        TickEntry? listed = _tickSignals.Find(entry => entry.Signal == signal);

        if (listed is not null)
        {
            listed.Restart(intervalMilliseconds, now);
            return;
        }

        TickEntry added = new(signal);
        added.Restart(intervalMilliseconds, now);
        _tickSignals.Insert(atHead ? 0 : _tickSignals.Count, added);
    }

    /// <summary>One `tick_t` of vgui's list: the panel, its schedule, and the walk's deletion mark.</summary>
    private sealed class TickEntry(IVguiTickSignal signal)
    {
        private VguiTickInterval _tick = new(0);

        public IVguiTickSignal Signal { get; } = signal;

        public bool Removed { get; set; }

        public void Restart(int intervalMilliseconds, int now)
        {
            _tick = new VguiTickInterval(intervalMilliseconds);
            _tick.Start(now);
            Removed = false;
        }

        public bool Due(int now) => _tick.Due(now);
    }

    /// <summary>The item panels' shared loading schedule, static in the engine.</summary>
    internal TfItemModelPanel.Loading ItemPanelLoading { get; } = new();

    /// <summary>`CBaseViewport::OnThink`: the animations run at `curtime`.</summary>
    protected override void OnThink()
    {
        FrameCount++;

        // The tick signals run before any panel thinks, visible or not, from the list's tail to its head, each when its
        // interval has come (`CVGui::RunFrame`, vgui2.dll `0x18001b200`). A tick may add or remove one: the walk covers the
        // entries listed when it began (into a buffer kept between frames), skips one marked removed, and drops the marked
        // after. *Interpolated:* the order ticks, then the think traversal. The viewport itself is never hidden, so its
        // think always runs.
        int now = TimeMillis(State);
        _tickWalk.Clear();
        _tickWalk.AddRange(_tickSignals);
        _walkingTicks = true;

        try
        {
            for (int at = _tickWalk.Count - 1; at >= 0; at--)
            {
                TickEntry entry = _tickWalk[at];

                if (!entry.Removed && entry.Due(now))
                {
                    entry.Signal.OnTick();
                }
            }
        }
        finally
        {
            _walkingTicks = false;
            _tickSignals.RemoveAll(entry => entry.Removed);
        }

        if (Context is { } context)
        {
            Animations.UpdateAnimations(State.CurTime, context);
        }
    }

    /// <summary>This frame's game state — what an element's `OnThink` reads of the local player and `gpGlobals`.</summary>
    public HudState State { get; private set; }

    /// <summary>The ConVars in force now, before the frame's think: they exist before any `.res` is loaded.</summary>
    /// <param name="conVars">This frame's ConVars.</param>
    public void SetConVars(HudConVars conVars) => State = State with { ConVars = conVars };

    /// <summary>The state once this frame's packets are read — what the render stage's thinks and paint see.</summary>
    /// <param name="state">The state.</param>
    public void SetState(HudState state) => State = state;

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

    /// <summary>
    /// `XRES( x )` (cdll_util.h:53): `x * ( (float)ScreenWidth() / 640.0 )`, a double; the caller truncates the whole
    /// expression once, as the engine's assignment does. 640 wide with no viewport.
    /// </summary>
    /// <param name="panel">The panel asking.</param>
    /// <param name="x">The 640-wide value.</param>
    /// <returns>The scaled value, untruncated.</returns>
    public static double XRes(VguiPanel panel, double x) => x * ((Of(panel)?.Wide ?? 640) / 640.0);

    /// <summary>`YRES( y )` (cdll_util.h:54): as <see cref="XRes"/>, over 480 tall.</summary>
    /// <param name="panel">The panel asking.</param>
    /// <param name="y">The 480-tall value.</param>
    /// <returns>The scaled value, untruncated.</returns>
    public static double YRes(VguiPanel panel, double y) => y * ((Of(panel)?.Tall ?? 480) / 480.0);

    /// <summary>The ConVars a panel reads — global in the engine, so any panel's view of them is the viewport's.</summary>
    /// <param name="panel">The panel.</param>
    /// <returns>The lookup; the SDK defaults when the panel has no viewport.</returns>
    public static HudConVars ConVarsOf(VguiPanel panel) => (Of(panel)?.State ?? default).ConVars;

    /// <summary>`CHud::Think`: every element's panel shown or hidden by its `ShouldDraw`.</summary>
    /// <param name="state">The game state.</param>
    public void Think(HudState state)
    {
        // `localplayer_changeteam`, which every `CTFImagePanel` listens for (tf_imagepanel.cpp:79).
        // Against the team last SENT, not `State`: `SetState` stores a frame's state before it is thought on.
        if (_teamSent != state.Team)
        {
            _teamSent = state.Team;
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

    /// <summary>`IsRenderGroupLockedFor( NULL, group )` (hud.cpp:1126): any locker at all hides it.</summary>
    /// <param name="group">The group.</param>
    /// <returns>Whether anything locks it.</returns>
    public bool IsRenderGroupLocked(string group) => _lockers.TryGetValue(group, out List<IHudElement>? lockers) && lockers.Count > 0;

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

    /// <summary>
    /// `gHUD.RemoveHudElement( p ); delete p;` (tf_hud_itemeffectmeter.cpp:95-96): the element leaves the HUD's list, and the
    /// deleted panel leaves the viewport and its build group.
    /// </summary>
    /// <param name="element">The element, parented here.</param>
    public void RemoveHudElement(VguiPanel element)
    {
        ArgumentNullException.ThrowIfNull(element);

        if (element is IHudElement hudElement)
        {
            _elements.Remove(hudElement);
        }

        DeletePanel(element);
    }

    /// <summary>
    /// `delete p`: `~Panel` deletes its children first, and each reaches `CVGui::FreePanel` (vgui2.dll `0x18001a650`), which
    /// calls vtable +0x98, `RemoveTickSignal`; the panel leaves the build group it is registered in (`_buildGroup`) and its
    /// parent.
    /// </summary>
    /// <param name="panel">The panel.</param>
    public static void DeletePanel(VguiPanel panel)
    {
        ArgumentNullException.ThrowIfNull(panel);

        if (Of(panel) is { } viewport)
        {
            viewport.RemoveTickSignals(panel);
        }

        panel.BuildGroup?.PanelRemoved(panel);
        panel.SetParent(null);
    }

    private void RemoveTickSignals(VguiPanel panel)
    {
        foreach (VguiPanel child in panel.Children)
        {
            RemoveTickSignals(child);
        }

        if (panel is IVguiTickSignal signal)
        {
            RemoveTickSignal(signal);
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

        // `CHud::AddHudElement`, which each element's constructor reaches through `DECLARE_HUDELEMENT` — once. A panel that
        // comes back after a reparent (the objective status, from `CTFSpectatorGUI::ShowPanel`) is not registered again.
        if (child is IHudElement element && !_elements.Contains(element))
        {
            _elements.Add(element);
        }
    }
}
