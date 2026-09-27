using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Hud;

/// <summary>`CTFClassImage` (tf_hud_playerstatus.h:29): the class portrait's image, named by team, class and cloak.</summary>
/// <param name="parent">The parent, or null.</param>
/// <param name="name">The panel's name, or null.</param>
public sealed class TfClassImage(VguiPanel? parent, string? name) : VguiImagePanel(parent, name)
{
    /// <summary>`TF_TEAM_BLUE` (tf_shareddefs.h).</summary>
    private const int TeamBlue = 3;

    /// <summary>`g_szBlueClassImages` (tf_hud_playerstatus.cpp:47), indexed by class.</summary>
    private static readonly string[] BlueClassImages =
    [
        "", "../hud/class_scoutblue", "../hud/class_sniperblue", "../hud/class_soldierblue", "../hud/class_demoblue",
        "../hud/class_medicblue", "../hud/class_heavyblue", "../hud/class_pyroblue", "../hud/class_spyblue",
        "../hud/class_engiblue", "../hud/class_scoutblue",
    ];

    /// <summary>`g_szRedClassImages` (tf_hud_playerstatus.cpp:62).</summary>
    private static readonly string[] RedClassImages =
    [
        "", "../hud/class_scoutred", "../hud/class_sniperred", "../hud/class_soldierred", "../hud/class_demored",
        "../hud/class_medicred", "../hud/class_heavyred", "../hud/class_pyrored", "../hud/class_spyred",
        "../hud/class_engired", "../hud/class_scoutred",
    ];

    /// <inheritdoc/>
    public override string ClassName => "CTFClassImage";

    /// <summary>`SetClass` (tf_hud_playerstatus.cpp:1121): blue for BLU and red for every other team, then the cloak suffix.</summary>
    /// <param name="team">The team.</param>
    /// <param name="playerClass">The class, 0 through 10.</param>
    /// <param name="cloakState">0, 1 for `_halfcloak`, 2 for `_cloak`.</param>
    /// <remarks>
    /// Valve indexes the tables unchecked; a class outside them — only a corrupt 4-bit `m_nDisguiseClass` could be one —
    /// reads past the array there, and here it is the empty name, which sets nothing.
    /// </remarks>
    public void SetClass(int team, int playerClass, int cloakState)
    {
        string[] images = team == TeamBlue ? BlueClassImages : RedClassImages;
        string image = playerClass >= 0 && playerClass < images.Length ? images[playerClass] : string.Empty;

        image += cloakState switch
        {
            2 => "_cloak",
            1 => "_halfcloak",
            _ => string.Empty,
        };

        // `Q_strlen( szImage ) > 0` — so class 0 with a cloak suffix still sets "_cloak", as Valve's does.
        if (image.Length > 0)
        {
            SetImage(image);
        }
    }
}

/// <summary>`CTFHudPlayerClass` (tf_hud_playerstatus.cpp:89-562): the class portrait, the spy's disguise, and a carried weapon.</summary>
/// <remarks>
/// <para>
/// `OnThink` (:184) runs every 0.5 s of `curtime`. On any change of class, team, cloak level, loadout slot of the held
/// weapon, `cl_hud_playerclass_use_playermodel`, or — for a spy — disguise, it either shows the 3D model panel
/// (`m_bUsePlayerModel &amp;&amp; m_pPlayerModelPanel &amp;&amp; m_pPlayerModelPanelBG`, :269) or the 2D class image
/// (:276).
/// </para>
/// <para>
/// **Not ported:** the one-time `ShowConfirmDialog` in `UpdateModelPanel` (:425-434), a modal asking whether to keep the
/// model — it changes `cl_hud_playerclass_use_playermodel` from the player's answer, not the HUD's drawing.
/// </para>
/// </remarks>
public sealed class TfHudPlayerClass : VguiEditablePanel
{
    private const string ResFile = "resource/UI/HudPlayerClass.res";
    private const int TeamUnassigned = 0;
    private const int ClassUndefined = 0;
    private const int ClassSpy = 8;

    // `LOADOUT_POSITION_PRIMARY` (tf_item_constants.h:51).
    private const int LoadoutPositionPrimary = 0;

    private readonly TfClassImage _classImage;
    private readonly TfImagePanel _classImageBg;
    private readonly TfImagePanel _spyImage;
    private readonly TfImagePanel _spyOutlineImage;
    private readonly TfImagePanel _playerModelPanelBg;
    private readonly VguiEditablePanel _carryingWeaponPanel;
    private readonly TfExLabel _carryingLabel;
    private readonly VguiLabel _carryingOwnerLabel;
    private readonly TfImagePanel _carryingBg;

    private float _nextThink;
    private float _lastCurTime;
    private int _team = TeamUnassigned;
    private int _class = ClassUndefined;
    private int _disguiseTeam = TeamUnassigned;
    private int _disguiseClass = ClassUndefined;
    private int? _disguiseWeapon;
    private int _cloakLevel;
    private int _loadoutPosition = LoadoutPositionPrimary;
    private int _killStreak;
    private bool _usePlayerModel;

    /// <summary>`CTFHudPlayerClass( parent, name )`, its `.res` children made up front so the `.res` finds them by name.</summary>
    /// <param name="parent">The parent.</param>
    /// <param name="name">The name.</param>
    /// <param name="mdlCache">`vgui::MDLCache()`, for `classmodelpanel`.</param>
    public TfHudPlayerClass(VguiPanel? parent, string? name, IMdlCache mdlCache)
        : base(parent, name)
    {
        _classImage = new TfClassImage(this, "PlayerStatusClassImage");
        PlayerModelPanel = new TfPlayerModelPanel(this, "classmodelpanel", mdlCache);
        _spyImage = new TfImagePanel(this, "PlayerStatusSpyImage");
        _spyOutlineImage = new TfImagePanel(this, "PlayerStatusSpyOutlineImage");
        _classImageBg = new TfImagePanel(this, "PlayerStatusClassImageBG");
        _playerModelPanelBg = new TfImagePanel(this, "classmodelpanelBG");
        _carryingWeaponPanel = new VguiEditablePanel(this, "CarryingWeapon");
        _carryingBg = new TfImagePanel(_carryingWeaponPanel, "CarryingBackground");
        _carryingLabel = new TfExLabel(_carryingWeaponPanel, "CarryingLabel");
        _ = new TfExLabel(_carryingWeaponPanel, "CarryingLabelDropShadow");
        _carryingOwnerLabel = new VguiLabel(_carryingWeaponPanel, "OwnerLabel");

        // `m_bUsePlayerModel = cl_hud_playerclass_use_playermodel.GetBool()` (:110).
        _usePlayerModel = UsePlayerModelConVar;
    }

    /// <summary>`cl_hud_playerclass_use_playermodel` (:39, "1", `FCVAR_ARCHIVE`) — the watcher's, from their config.</summary>
    private bool UsePlayerModelConVar => HudViewport.ConVarsOf(this).GetBool("cl_hud_playerclass_use_playermodel");

    /// <summary>`m_pPlayerModelPanel`: `classmodelpanel`, a `CTFPlayerModelPanel` (:167).</summary>
    public TfPlayerModelPanel PlayerModelPanel { get; }

    /// <summary>`m_pClassImage`.</summary>
    public TfClassImage ClassImage => _classImage;

    /// <summary>`m_pSpyImage`.</summary>
    public TfImagePanel SpyImage => _spyImage;

    /// <summary>`m_pSpyOutlineImage`.</summary>
    public TfImagePanel SpyOutlineImage => _spyOutlineImage;

    /// <summary>`m_pClassImageBG`.</summary>
    public TfImagePanel ClassImageBackground => _classImageBg;

    /// <summary>`m_pPlayerModelPanelBG`.</summary>
    public TfImagePanel PlayerModelPanelBackground => _playerModelPanelBg;

    /// <summary>`m_pCarryingWeaponPanel`.</summary>
    public VguiEditablePanel CarryingWeaponPanel => _carryingWeaponPanel;

    /// <summary>`m_pCarryingLabel`.</summary>
    public TfExLabel CarryingLabel => _carryingLabel;

    /// <summary>`m_pCarryingOwnerLabel`.</summary>
    public VguiLabel CarryingOwnerLabel => _carryingOwnerLabel;

    /// <summary>`m_pCarryingBG`.</summary>
    public TfImagePanel CarryingBackground => _carryingBg;

    /// <summary>The events the constructor listens for (:112-114).</summary>
    public static IReadOnlySet<string> ListensFor { get; } =
        new HashSet<string>(["localplayer_changedisguise", "post_inventory_application", "localplayer_pickup_weapon"], StringComparer.Ordinal);

    /// <summary>`Reset` (:138): think in 0.05 s, and hide the disguise images.</summary>
    /// <param name="curTime">`gpGlobals->curtime`.</param>
    public void Reset(float curTime)
    {
        _nextThink = curTime + 0.05f;
        HudViewport.Of(this)?.Animations.StartAnimationSequence("HudSpyDisguiseHide");
    }

    /// <inheritdoc/>
    public override void ApplySchemeSettings(VguiContext context)
    {
        // `ApplySchemeSettings` (:148).
        LoadControlSettings(ResFile, context);

        _team = TeamUnassigned;
        _class = ClassUndefined;
        _disguiseTeam = TeamUnassigned;
        _disguiseClass = ClassUndefined;
        _disguiseWeapon = null;
        _nextThink = 0f;
        _cloakLevel = 0;
        _loadoutPosition = LoadoutPositionPrimary;

        base.ApplySchemeSettings(context);
    }

    /// <inheritdoc/>
    protected override void OnThink()
    {
        if (HudViewport.Of(this) is { } viewport)
        {
            Think(viewport.State);
        }
    }

    /// <summary>`OnThink` (:184) with the state it reads.</summary>
    /// <param name="state">The game state.</param>
    public void Think(HudState state)
    {
        // A seek backwards restarts playback, which `ResetHUD` answers with `Reset` — as `TfHudPlayerHealth` does.
        if (state.CurTime < _lastCurTime)
        {
            Reset(state.CurTime);
        }

        _lastCurTime = state.CurTime;

        if (_nextThink > state.CurTime)
        {
            return;
        }

        _nextThink = state.CurTime + 0.5f;

        if (!state.HasLocalPlayer || state.Player(state.LocalIndex) is not { } player)
        {
            return;
        }

        HudViewport? viewport = HudViewport.Of(this);
        int team = player.Team ?? TeamUnassigned;
        bool teamChange = false;

        if (_team != team)
        {
            teamChange = true;
            _team = team;
        }

        int cloakLevel = 0;
        bool cloakChange = false;
        float invisible = PlayerInvisibility.Percent(player, state.ServerTime, HasMotionCloak(player, viewport));

        // Compared as doubles, as the engine's `0.9`/`0.1` literals promote it.
        if (invisible > 0.9)
        {
            cloakLevel = 2;
        }
        else if (invisible > 0.1)
        {
            cloakLevel = 1;
        }

        if (cloakLevel != _cloakLevel)
        {
            _cloakLevel = cloakLevel;
            cloakChange = true;
        }

        // `GetLoadoutSlot( m_nClass )` with the class from the last change (:222). An unsent definition index is 0, the
        // engine's default for `m_iItemDefinitionIndex`.
        bool loadoutPositionChange = false;
        int loadoutSlot = player.ActiveWeapon is not null && viewport?.Items is { } items
            ? items.LoadoutSlot(player.WeaponItem ?? 0, _class)
            : LoadoutPositionPrimary;

        if (_loadoutPosition != loadoutSlot)
        {
            _loadoutPosition = loadoutSlot;
            loadoutPositionChange = true;
        }

        bool playerClassModeChange = false;

        bool usePlayerModel = state.ConVars.GetBool("cl_hud_playerclass_use_playermodel");

        if (_usePlayerModel != usePlayerModel)
        {
            _usePlayerModel = usePlayerModel;
            playerClassModeChange = true;
        }

        int playerClass = player.PlayerClass ?? ClassUndefined;
        PlayerConditions cond = player.Conditions;
        bool forceEyeUpdate = false;

        if (_class != playerClass || teamChange || cloakChange || loadoutPositionChange || playerClassModeChange ||
            (_class == ClassSpy &&
             (_disguiseClass != (player.DisguiseClass ?? ClassUndefined) ||
              _disguiseTeam != (player.DisguiseTeam ?? TeamUnassigned) ||
              _disguiseWeapon != player.DisguiseWeapon)))
        {
            forceEyeUpdate = true;
            _class = playerClass;

            if (_class == ClassSpy && cond.Has(PlayerConditions.Disguised))
            {
                if (!cond.Has(PlayerConditions.Disguising))
                {
                    _disguiseTeam = player.DisguiseTeam ?? TeamUnassigned;
                    _disguiseClass = player.DisguiseClass ?? ClassUndefined;
                    _disguiseWeapon = player.DisguiseWeapon;
                }
            }
            else
            {
                _disguiseTeam = TeamUnassigned;
                _disguiseClass = ClassUndefined;
                _disguiseWeapon = null;
            }

            if (_usePlayerModel)
            {
                PlayerModelPanel.Visible = true;
                _playerModelPanelBg.Visible = true;

                UpdateModelPanel(player);
            }
            else
            {
                PlayerModelPanel.Visible = false;
                _playerModelPanelBg.Visible = false;
                _classImage.Visible = true;
                _classImageBg.Visible = true;

                int cloakState = playerClass == ClassSpy ? _cloakLevel : 0;

                if (_disguiseTeam != TeamUnassigned || _disguiseClass != ClassUndefined)
                {
                    _spyImage.Visible = true;
                    _classImage.SetClass(_disguiseTeam, _disguiseClass, cloakState);
                }
                else
                {
                    _spyImage.Visible = false;
                    _classImage.SetClass(_team, _class, cloakState);
                }
            }
        }

        UpdateCarryingWeapon(state, player, viewport);

        if (_usePlayerModel)
        {
            int killStreak = player.KillStreak ?? 0;
            bool playSparks = killStreak != _killStreak && killStreak > 0;

            _killStreak = killStreak;

            // `m_pPlayerModelPanel->SetEyeGlowEffect( ..., bForceEyeUpdate, bPlaySparks )` (:400) — a `CTFPlayerModelPanel`
            // method; these are its two flags, handed over when that class is ported.
            EyeGlowUpdate = (forceEyeUpdate, playSparks);
        }
    }

    /// <summary>The last `bForceEyeUpdate`/`bPlaySparks` pair `OnThink` (:391-401) computed for `SetEyeGlowEffect`.</summary>
    public (bool Force, bool Sparks) EyeGlowUpdate { get; private set; }

    /// <summary>`FireGameEvent` (:519), named as the other elements' handlers are.</summary>
    /// <param name="gameEvent">The event.</param>
    /// <param name="state">The state it is handled in.</param>
    public void HandleGameEvent(SceneGameEvent gameEvent, HudState state)
    {
        ArgumentNullException.ThrowIfNull(gameEvent);

        ScenePlayer? local = state.HasLocalPlayer ? state.Player(state.LocalIndex) : null;

        switch (gameEvent.Name)
        {
            case "localplayer_changedisguise":
                bool fadeIn = gameEvent.GetInt("disguised") != 0;

                _spyImage.SetAnimationValue("alpha", fadeIn ? 0f : 255f);
                _spyOutlineImage.SetAnimationValue("alpha", 0f);
                _spyImage.Visible = true;
                _spyOutlineImage.Visible = true;
                HudViewport.Of(this)?.Animations.StartAnimationSequence(fadeIn ? "HudSpyDisguiseFadeIn" : "HudSpyDisguiseFadeOut");

                UpdateModelPanel(local);
                break;

            case "post_inventory_application":
                // "Force a refresh. if this is for the local player" (:550).
                if (local is not null && gameEvent.Roster.TryGetValue(state.LocalIndex, out Core.Net.PlayerInfo info)
                    && info.UserId == gameEvent.GetInt("userid"))
                {
                    UpdateModelPanel(local);
                }

                break;

            case "localplayer_pickup_weapon":
                UpdateModelPanel(local);
                break;

            default:
                break;
        }
    }

    /// <summary>
    /// Whether `C_TFPlayer::OnDataChanged` would fire `localplayer_changedisguise` (c_tf_player.cpp:4788-4797): the old class
    /// a spy, and the disguise condition or disguise class changed since the last update.
    /// </summary>
    /// <param name="before">The local player at the previous update.</param>
    /// <param name="after">The local player now.</param>
    /// <returns>The event's `disguised`, or null when it does not fire.</returns>
    public static bool? LocalPlayerChangeDisguise(ScenePlayer before, ScenePlayer after)
    {
        bool wasDisguised = before.Conditions.Has(PlayerConditions.Disguised);
        bool disguised = after.Conditions.Has(PlayerConditions.Disguised);

        return before.PlayerClass == ClassSpy && (wasDisguised != disguised || before.DisguiseClass != after.DisguiseClass)
            ? disguised
            : null;
    }

    /// <summary>`UpdateModelPanel` (:412).</summary>
    private void UpdateModelPanel(ScenePlayer? player)
    {
        if (!_usePlayerModel)
        {
            return;
        }

        if (player is not { IsAlive: true })
        {
            return;
        }

        // "hide old UI" (:436).
        _spyImage.Visible = false;
        _classImage.Visible = false;
        _classImageBg.Visible = false;

        if (!PlayerModelPanel.Visible || HudViewport.Of(this)?.Items is not { } schema)
        {
            return;
        }

        int itemSlot = _loadoutPosition;
        TfItemView? weapon = null;
        int playerClass;
        int team;
        bool disguised = player.Value.Conditions.Has(PlayerConditions.Disguised);

        if (disguised)
        {
            playerClass = player.Value.DisguiseClass ?? ClassUndefined;
            team = player.Value.DisguiseTeam ?? TeamUnassigned;

            if (player.Value.DisguiseWeaponItem is { } disguiseWeapon && TfItemView.From(disguiseWeapon, schema) is { } view)
            {
                weapon = view;
                itemSlot = schema.LoadoutSlot(view.DefinitionIndex, playerClass);
            }
        }
        else
        {
            playerClass = player.Value.PlayerClass ?? ClassUndefined;
            team = player.Value.Team ?? TeamUnassigned;

            if (WeaponForLoadoutSlot(player.Value, itemSlot, schema) is { } held)
            {
                weapon = TfItemView.From(held, schema);
            }
        }

        PlayerModelPanel.ClearCarriedItems();
        PlayerModelPanel.SetToPlayerClass(playerClass);
        PlayerModelPanel.SetTeam(team);

        if (weapon is not null)
        {
            PlayerModelPanel.AddCarriedItem(weapon);
        }

        // `for ( int wbl = pPlayer->GetNumWearables()-1; wbl >= 0; wbl-- )` (:488): last worn first.
        IReadOnlyList<SceneItem> items = player.Value.Items ?? [];

        for (int index = items.Count - 1; index >= 0; index--)
        {
            SceneItem item = items[index];

            // `IsViewModelWearable()`: a `CTFWearableVM` (tf_item_wearable.cpp:58); then the disguise filters (:497-501).
            if (item.IsWeapon || item.ClassName == "CTFWearableVM" || item.IsDisguiseWearable != disguised)
            {
                continue;
            }

            if (TfItemView.From(item, schema) is { } worn)
            {
                PlayerModelPanel.AddCarriedItem(worn);
            }
        }

        PlayerModelPanel.HoldItemInSlot(itemSlot);
    }

    /// <summary>
    /// `dynamic_cast&lt; CTFWeaponBase* &gt;( GetEntityForLoadoutSlot( slot ) )` (tf_player_shared.cpp:11928): in a wearable
    /// slot a matching wearable answers first and fails the cast; otherwise the first weapon whose slot matches.
    /// </summary>
    private static SceneItem? WeaponForLoadoutSlot(ScenePlayer player, int slot, Content.Assets.ItemSchema schema)
    {
        int playerClass = player.PlayerClass ?? ClassUndefined;
        IReadOnlyList<SceneItem> items = player.Items ?? [];

        if (Content.Assets.ItemSchema.IsWearableSlot(slot))
        {
            foreach (SceneItem item in items)
            {
                if (!item.IsWeapon && item.DefinitionIndex is { } worn && schema.LoadoutSlot(worn, playerClass) == slot)
                {
                    return null;
                }
            }
        }

        foreach (SceneItem item in items)
        {
            if (item.IsWeapon && item.DefinitionIndex is { } definition && schema.LoadoutSlot(definition, playerClass) == slot)
            {
                return item;
            }
        }

        return null;
    }

    /// <summary>The carried-weapon panel (:305-389): shown with a weapon someone else owns, sized to its two labels.</summary>
    private void UpdateCarryingWeapon(HudState state, ScenePlayer player, HudViewport? viewport)
    {
        // "Don't show if we're disguised (the panels overlap)".
        bool show = _disguiseClass == ClassUndefined;

        if (player.ActiveWeapon is not null)
        {
            uint itemAccount = player.WeaponAccountId ?? 0u;

            // "We're holding a weapon we dont own!"
            if (state.AccountId(player.EntityIndex) != itemAccount)
            {
                VguiContext? context = viewport?.Context;
                int definition = player.WeaponItem ?? 0;

                _carryingWeaponPanel.SetDialogVariable(
                    "carrying",
                    VguiLocalize.ConstructString("%s1", 128, viewport?.ItemName?.Invoke(definition, player.WeaponQuality ?? 0) ?? string.Empty));

                if (context is not null)
                {
                    _carryingLabel.SetColorStr(viewport?.Items?.RarityColor(definition) ?? "TanLight", context);
                }

                bool hasOwner = false;

                // "Bots will not work here, so don't fill this out if there's no owner".
                if (PlayerByAccountId(state, itemAccount) is { } owner)
                {
                    string ownerName = state.Names?.GetValueOrDefault(owner.EntityIndex) ?? string.Empty;

                    _carryingOwnerLabel.SetText(VguiLocalize.ConstructString(context?.Localize?.Invoke("TF_WhoDropped"), 128, ownerName), null);
                    hasOwner = true;
                }
                else
                {
                    _carryingOwnerLabel.SetText(string.Empty, null);
                }

                // "Resize the panel to just be the width of whichever label is longer" (:352). Valve also keeps the
                // taller of the two, `nMaxTall`, and never reads it.
                _carryingLabel.SizeToContents();
                int maxWide = Math.Max(0, _carryingLabel.GetContentSize().Wide);

                _carryingOwnerLabel.SizeToContents();
                maxWide = Math.Max(maxWide, _carryingOwnerLabel.GetContentSize().Wide);

                // `YRES( 2 )` is `2 * ( (float)ScreenHeight() / 480.0 )`, a double the sum is truncated from (cdll_util.h:54).
                double yres2 = 2 * ((float)(context?.ScreenTall ?? 480) / 480.0);

                _carryingBg.Wide = maxWide + (_carryingLabel.X * 2);
                _carryingBg.Tall = hasOwner
                    ? (int)(_carryingOwnerLabel.Y + _carryingOwnerLabel.Tall + yres2)
                    : (int)(_carryingLabel.Y + _carryingLabel.Tall + yres2);
            }
            else
            {
                show = false;
            }
        }
        else
        {
            show = false;
        }

        if (state.Rules.PlayerDestruction && player.HasTheFlag)
        {
            show = false;
        }

        _carryingWeaponPanel.Visible = show;
    }

    /// <summary>
    /// `m_bMotionCloak` (tf_player_shared.cpp:7022): the first `TF_WEAPON_INVIS` among the weapons, `HasMotionCloak` —
    /// `CALL_ATTRIB_HOOK_INT( iMode, set_weapon_mode ) == INVIS_MOTION_CLOAK` (tf_weapon_invis.h:22-26, :66-68).
    /// </summary>
    private static bool HasMotionCloak(ScenePlayer player, HudViewport? viewport)
    {
        if (viewport?.WeaponAttribute is not { } hook)
        {
            return false;
        }

        foreach (SceneItem item in player.Items ?? [])
        {
            if (item.IsWeapon && item.ClassName == "CTFWeaponInvis")
            {
                return AttributeHooks.RoundFloatToInt(hook(player, item, "set_weapon_mode", 0f)) == 2;
            }
        }

        return false;
    }

    /// <summary>`GetPlayerByAccountID` (econ_item_view.cpp:1955): the first present player, by index, whose `GetSteamID` succeeds with that account.</summary>
    private static ScenePlayer? PlayerByAccountId(HudState state, uint accountId)
    {
        ScenePlayer? found = null;

        foreach (ScenePlayer candidate in state.Players ?? [])
        {
            uint friends = state.AccountId(candidate.EntityIndex);

            if (friends != 0u && friends == accountId && (found is null || candidate.EntityIndex < found.Value.EntityIndex))
            {
                found = candidate;
            }
        }

        return found;
    }
}
