using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;

using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Diagnostics;
using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Schema;

namespace Tf2DemoSalvage.Core.Scene;

/// <summary>One player, at one moment.</summary>
/// <param name="EntityIndex">The entity's slot, stable for as long as it lives.</param>
/// <param name="X">World position across.</param>
/// <param name="Y">World position along.</param>
/// <param name="Z">World height.</param>
/// <param name="Team">Which team, when the demo has said; 2 is RED and 3 is BLU.</param>
/// <param name="Health">Current health, when known.</param>
/// <param name="PlayerClass">Which of the nine classes, when known; 1 is Scout through 9 Engineer.</param>
/// <param name="Yaw">Which way the body faces, in degrees, interpolated with the position.</param>
/// <param name="Speed">How fast the player is moving horizontally, in units a second.</param>
/// <param name="LifeState">0 alive, 1 dying, 2 dead; absent means alive.</param>
/// <param name="MoveX">The <c>move_x</c> pose parameter: how much of the motion is forward.</param>
/// <param name="MoveY">The <c>move_y</c> pose parameter: how much of it is sideways.</param>
/// <param name="Flags">
/// The player's <c>m_fFlags</c>, carrying the crouch and ground bits, or <c>null</c> when the
/// recording did not say. Declared in <c>DT_LocalPlayerExclusive</c>, so a POV demo carries it for
/// the recorder alone while a SourceTV recording carries it for every player.
/// </param>
/// <param name="JumpActivity">
/// <c>CTFPlayerAnimState::HandleJumping</c>'s answer at this tick: the activity it leaves when it returns true —
/// the air-walk, a jump phase, or <c>ACT_MP_STAND_IDLE</c> untouched — or <c>null</c> when it returns false (B437).
/// Stepped by <see cref="PlayerGestureFeed.HandleJumping"/>, which keeps the latch, the jump clock from the
/// `PLAYERANIMEVENT_JUMP` event and the class script.
/// </param>
/// <param name="ActivityOverride">
/// Which of the player's own activity tables `CTFPlayerAnimState::ActivityOverride` walks before the weapon's —
/// kart, competitive loser, loser or carrying a building (B437).
/// </param>
/// <param name="CompetitiveWinner">
/// `TF_COND_COMPETITIVE_WINNER`, which turns certain stands into `ACT_MP_COMPETITIVE_WINNERSTATE` after the weapon's
/// table (`tf_playeranimstate.cpp:142-151`).
/// </param>
/// <param name="DiscontinuitySeconds">
/// When this player last JUMPED — a teleport or a respawn — so a sequence change at that moment
/// cuts rather than cross-fading (B346). `CheckForSequenceChange` empties the transition queue on
/// <c>(seqdesc.flags &amp; STUDIO_SNAP) || !bInterpolate</c> (<c>sequence_Transitioner.cpp:41</c>),
/// and <c>bInterpolate</c> is the networked <c>m_ubInterpolationFrame</c> parity holding still.
/// Zero means never, which is what a player who is not teleported reports.
/// </param>
/// <param name="Conditions">
/// The five <c>m_nPlayerCond</c> bitfields, read as <c>CTFPlayerShared::InCond</c> does.
/// </param>
/// <param name="BurningFor">
/// Seconds since this player caught fire, or null when they are not alight (B336). The engine reads
/// <c>m_flBurnEffectStartTime</c>, which is set CLIENT-SIDE when <c>TF_COND_BURNING</c> is added
/// (<c>tf_player_shared.cpp:7306</c>) and networked nowhere — so this watches the same transition
/// the client watches rather than reading a field the demo does not carry. It drives
/// <c>CProxyBurnLevel</c>, and through it <c>$detailblendfactor</c> on the fire overlay.
/// </param>
/// <param name="DisguiseClass">Which class a disguised spy appears to be, <c>m_nDisguiseClass</c>.</param>
/// <param name="DisguiseTeam">Which team they appear to be on, <c>m_nDisguiseTeam</c>.</param>
/// <param name="DisguiseMaskClass">
/// <c>m_nMaskClass</c>, read only when an enemy spy is disguised AS a spy — the one case where the
/// mask offset comes from somewhere other than the disguise class (<c>tf_player_shared.h:375</c>).
/// </param>
/// <param name="IsEnemy">
/// Whether this player is an enemy of the RECORDER, which is what <c>IsEnemyPlayer</c> asks of the
/// local player (<c>c_tf_player.cpp:5384</c>). A disguise only fools the other team.
/// </param>
/// <param name="EyePitch">
/// How far up or down the player is looking, in degrees, or <c>null</c> when the recording did not
/// send eye angles. Drives the <c>body_pitch</c> pose parameter, which aims the torso —
/// <c>ComputePoseParam_AimPitch</c> sets it to the NEGATED eye pitch.
/// </param>
/// <param name="EyeYaw">
/// Where the player is LOOKING, which is not where their body is drawn once they turn on the spot.
/// <paramref name="Yaw"/> is the feet.
/// </param>
/// <param name="AimYaw">
/// The <c>body_yaw</c> pose parameter — how far the torso is twisted from the feet, already negated
/// as the engine negates it.
/// </param>
/// <param name="WaterLevel">
/// How deep in water they are — 0 dry, 1 feet, 2 waist, 3 eyes (<c>player.cpp:1961</c>). Waist deep
/// is where the animation changes: both <c>HandleJumping</c> and <c>HandleSwimming</c> test
/// <c>&gt;= WL_Waist</c>, so a player who leaps into water swims rather than falling with their
/// legs tucked.
/// </param>
/// <param name="ActiveWeapon">
/// Which entity is the weapon in hand, or <c>null</c> when none is held. Decoded from
/// <c>m_hActiveWeapon</c> on <c>DT_BaseCombatCharacter</c>, so it arrives for every player in the
/// PVS rather than for the recorder alone.
/// </param>
/// <param name="WeaponClass">
/// That weapon's server class, such as <c>CTFRevolver</c>. It is what decides the suffix on every
/// body activity — <c>CTFWeaponBase::ActivityList</c> picks an <c>acttable_t</c> from the weapon's
/// role, so a medic's medigun drives <c>ACT_MP_RUN_SECONDARY</c> where a scattergun drives
/// <c>ACT_MP_RUN_PRIMARY</c>.
/// </param>
/// <param name="WeaponItem">
/// Which item in TF2's schema that weapon is, or <c>null</c> when the demo predates the item system
/// or nothing is held. It is what turns "a scattergun" into a model path: the weapon a player sees
/// in their own hands is a client-side entity a recording cannot carry, so the index into
/// <c>items_game.txt</c> is the only route to it. See <c>EntityState.ItemDefinitionIndex</c>.
/// </param>
/// <param name="Drawn">
/// Whether the engine would draw this player's model, which is <c>EF_NODRAW</c> rather than
/// anything about life state. TF2 turns the player off on death — <c>AddEffects( EF_NODRAW |
/// EF_NOSHADOW )</c> at the end of <c>CreateRagdollEntity</c>, <c>tf_player.cpp:15637</c> — and
/// spawns a separate <c>CTFRagdoll</c> to be the corpse. A dead player stays in this list as data
/// for the scoreboard and the kill feed with this false.
/// </param>
/// <param name="ObserverMode">
/// What the player is watching through — <c>m_iObserverMode</c>, <c>shareddefs.h:492</c> — or
/// <c>null</c> when the recording never said, which means <see cref="ObserverModes.None"/>. It is
/// the engine's own answer to "is this a first-person view", and the reason it matters here is that
/// a player who goes to spectator is still ALIVE: liveness cannot distinguish them, and this can.
/// See <see cref="ScenePlayer.InFirstPersonView"/>.
/// </param>
/// <param name="ObserverTarget">
/// Who the player is observing — <c>m_hObserverTarget</c> — or <c>null</c> for nobody. In-eye, it is whose eyes a POV
/// recorder is seeing through (B417).
/// </param>
/// <param name="Gestures">
/// The gestures this player has going, one per occupied slot in slot order, or <c>null</c> when
/// they have none. Filled from the <c>CTEPlayerAnimEvent</c> temp entities the demo carries, which
/// is the ONLY place a player's gestures appear: <c>tf_player.cpp:774</c> excludes
/// <c>overlay_vars</c> from the player's send table, so <c>m_AnimOverlay</c> is never networked for
/// a player. See <see cref="PlayerGestureFeed"/>.
/// </param>
/// <param name="HeadScale">
/// TF2's per-BONE head scale, <c>m_flHeadScale</c> — distinct from the whole-model scale, and 1 for
/// everything but a Halloween or MvM effect (B312). Carried on the PLAYER rather than read from the
/// pose because `PlayerProps.Add` builds a player's pose field by field: a value that is not a
/// parameter here is one the renderer never sees, which is how three fields have been lost before.
/// </param>
/// <param name="TorsoScale">
/// <c>m_flTorsoScale</c>. Compresses the spine toward the pelvis rather than scaling anything.
/// </param>
/// <param name="HandScale">
/// <c>m_flHandScale</c>. Scales each hand and every bone below it.
/// </param>
/// <param name="MaxSpeed">
/// <c>m_flMaxspeed</c>, the player's top speed now — what <c>GetStepSoundVelocities</c> scales a walk and a run
/// from. Null when not sent.
/// </param>
/// <param name="ClientSideAnimated">
/// Whether the client runs this player's animation cycle itself — <c>m_bClientSideAnimation</c>,
/// one unsigned bit from <c>DT_BaseAnimating</c> (<c>baseanimating.cpp:250</c>).
/// <c>CTFPlayer::CTFPlayer</c> calls <c>UseClientSideAnimation()</c> unconditionally
/// (<c>tf_player.cpp:953</c>), so in practice every TF player sends it set.
///
/// **It is on the player rather than left to the prop path because a player never goes through the
/// prop path.** <c>PropsAt</c> copies the flag off the track; <c>PlayersAt</c> builds its own
/// record, so without this the value the demo stated is dropped between the timeline and the
/// renderer — and a player whose cycle is not advanced holds frame zero while their position
/// interpolates, which is B280.
/// </param>
/// <remarks>
/// **Not everything here is playing.** A spectator and a SourceTV camera are <c>CTFPlayer</c>
/// entities with real positions that fly around the map, and drawing them puts dots where nobody
/// is standing. The team number separates them, and it is the engine's own:
/// <c>TEAM_UNASSIGNED</c> is 0, <c>TEAM_SPECTATOR</c> is 1, and TF2's own
/// <c>TF_TEAM_RED = LAST_SHARED_TEAM + 1</c> makes RED 2 and BLU 3.
/// </remarks>
public readonly record struct ScenePlayer(
    int EntityIndex,
    float X,
    float Y,
    float Z,
    int? Team,
    int? Health,
    int? PlayerClass,
    float Yaw = 0f,
    float Speed = 0f,
    int? LifeState = null,
    float MoveX = 0f,
    float MoveY = 0f,
    int? Flags = null,
    bool Drawn = true,
    PlayerActivity? JumpActivity = null,
    PlayerActivityOverride ActivityOverride = PlayerActivityOverride.None,
    bool CompetitiveWinner = false,
    double DiscontinuitySeconds = 0d,

    // **The spy disguise, and the side we are on.** `C_TFPlayer::ValidateModelIndex` and `GetSkin`
    // both branch on `InCond( TF_COND_DISGUISED ) && IsEnemyPlayer()`, so all three travel
    // together: the conditions say whether a disguise is up, the disguise fields say what it is,
    // and `IsEnemy` says whether we are the one meant to be fooled.
    //
    // `IsEnemy` is computed HERE rather than in the scene, because `IsEnemyPlayer` compares against
    // the LOCAL player (`c_tf_player.cpp:5384`) and in a recording that is the recorder — which
    // only the timeline knows.
    PlayerConditions Conditions = default,
    float? BurningFor = null,
    int? DisguiseClass = null,
    int? DisguiseTeam = null,
    int? DisguiseMaskClass = null,
    bool IsEnemy = false,
    float? EyePitch = null,
    float? EyeYaw = null,
    float? AimYaw = null,
    int? WaterLevel = null,
    int? ActiveWeapon = null,
    string? WeaponClass = null,
    int? WeaponItem = null,
    int? ObserverMode = null,
    int? ObserverTarget = null,
    bool ClientSideAnimated = false,
    IReadOnlyList<SceneGesture>? Gestures = null,
    float HeadScale = 1f,
    float TorsoScale = 1f,
    float HandScale = 1f,
    float? MaxSpeed = null)
{
    /// <summary>What TF's `HandleDucking` and `HandleMoving` ask beyond the flags (B437); the scene adds the model's half.</summary>
    public TfPosture Posture { get => D.Posture; init => _detail = ScenePlayerDetail.Normal(D with { Posture = value }); }

    /// <summary>`GetMaxHealth()` — the player resource's `m_iMaxHealth`, which `C_TFPlayer` reads; null when not sent.</summary>
    /// <remarks>A full heal to it clears the player's model decals (`C_TFPlayer::OnDataChanged`, B415).</remarks>
    public int? MaxHealth { get => D.MaxHealth; init => _detail = ScenePlayerDetail.Normal(D with { MaxHealth = value }); }

    /// <summary>`GetMaxHealthForBuffing()` — the resource's `m_iMaxBuffedHealth`, misnamed ("we can't fix it now because of demos").</summary>
    public int? MaxHealthForBuffing { get => D.MaxHealthForBuffing; init => _detail = ScenePlayerDetail.Normal(D with { MaxHealthForBuffing = value }); }

    /// <summary>`GetHealth()` — the player's own `m_iHealth`, which the HUD reads; <see cref="Health"/> prefers the resource's.</summary>
    public int? EntityHealth { get => D.EntityHealth; init => _detail = ScenePlayerDetail.Normal(D with { EntityHealth = value }); }

    /// <summary>`m_Local.m_iHideHUD` — sent to the player it belongs to, so the recorder's alone; an observer's is `HIDEHUD_HEALTH`.</summary>
    public int? HideHud { get => D.HideHud; init => _detail = ScenePlayerDetail.Normal(D with { HideHud = value }); }

    /// <summary>The active weapon's `m_iClip1` — `DT_LocalWeaponData`, so the recorder's alone; -1 for a weapon with no clip.</summary>
    public int? WeaponClip1 { get => D.WeaponClip1; init => _detail = ScenePlayerDetail.Normal(D with { WeaponClip1 = value }); }

    /// <summary>The active weapon's `m_iPrimaryAmmoType` — `DT_LocalWeaponData`; -1 for none.</summary>
    public int? WeaponPrimaryAmmoType { get => D.WeaponPrimaryAmmoType; init => _detail = ScenePlayerDetail.Normal(D with { WeaponPrimaryAmmoType = value }); }

    /// <summary>`m_iAmmo` by ammo type, or null when never sent.</summary>
    public IReadOnlyList<int>? Ammo { get => D.Ammo; init => _detail = ScenePlayerDetail.Normal(D with { Ammo = value }); }

    /// <summary>What the player carries and wears, with each item's attributes — the recorder's alone; null otherwise.</summary>
    public IReadOnlyList<SceneItem>? Items { get => D.Items; init => _detail = ScenePlayerDetail.Normal(D with { Items = value }); }

    /// <summary>The player's own `m_AttributeList` — what `CAttributeContainerPlayer` applies first; null when empty.</summary>
    public IReadOnlyList<EconAttributeValue>? OwnAttributes { get => D.OwnAttributes; init => _detail = ScenePlayerDetail.Normal(D with { OwnAttributes = value }); }

    /// <summary>`m_Shared.m_hDisguiseTarget` (tf_player_shared.cpp:581, sent to everyone): whom a spy is disguised as, or null.</summary>
    public int? DisguiseTarget { get => D.DisguiseTarget; init => _detail = ScenePlayerDetail.Normal(D with { DisguiseTarget = value }); }

    /// <summary>`m_Shared.m_iDisguiseHealth` (:582): the health a disguise shows.</summary>
    public int? DisguiseHealth { get => D.DisguiseHealth; init => _detail = ScenePlayerDetail.Normal(D with { DisguiseHealth = value }); }

    /// <summary>`m_Shared.m_nPlayerState` (tf_player_shared.cpp:543, sent to everyone): `TF_STATE_ACTIVE` 0 … `TF_STATE_DYING` 3.</summary>
    public int? PlayerState { get => D.PlayerState; init => _detail = ScenePlayerDetail.Normal(D with { PlayerState = value }); }

    /// <summary>
    /// `m_Shared.m_bCarryingObject` (tf_player_shared.cpp:562, sent to everyone): `IsCarryingObject()`
    /// (tf_player_shared.h:601) — set while a building is on the Engineer's back.
    /// </summary>
    public bool CarryingObject { get => D.CarryingObject; init => _detail = ScenePlayerDetail.Normal(D with { CarryingObject = value }); }

    /// <summary>
    /// `m_iStunFlags` (tf_player_shared.cpp:372, sent to everyone): `TF_STUN_CONTROLS`/`TF_STUN_LOSER_STATE` etc
    /// (tf_shareddefs.h:1333-1342). On the client this is what `GetActiveStunInfo()->iStunFlags` reads
    /// (tf_player_shared.cpp:7462-7463) — the client keeps no separate stun list.
    /// </summary>
    public int? StunFlags { get => D.StunFlags; init => _detail = ScenePlayerDetail.Normal(D with { StunFlags = value }); }

    /// <summary>
    /// `m_iStunIndex` (:420, sent to everyone): `GetActiveStunInfo()` on the client is non-null exactly when this is
    /// `&gt;= 0` (:7474-7475).
    /// </summary>
    public int? StunIndex { get => D.StunIndex; init => _detail = ScenePlayerDetail.Normal(D with { StunIndex = value }); }

    /// <summary>`m_bIsMiniBoss` (c_tf_player.cpp:3779): what `C_TFPlayer::IsHealthBarVisible` returns outside MvM's regen case.</summary>
    public bool IsMiniBoss { get => D.IsMiniBoss; init => _detail = ScenePlayerDetail.Normal(D with { IsMiniBoss = value }); }

    /// <summary>
    /// `m_nActiveWpnClip` (tf_player.cpp:760): the active weapon's clip, sent only to the medics healing this player
    /// (`SendProxy_SendHealersDataTable`, :505) — so a medic's recording carries his heal target's; null otherwise.
    /// </summary>
    public int? ActiveWeaponClip { get => D.ActiveWeaponClip; init => _detail = ScenePlayerDetail.Normal(D with { ActiveWeaponClip = value }); }

    /// <summary>
    /// `DT_BaseEntity.m_flGravity`, `GetGravity()` — what StartGravity and FinishGravity scale gravity by when nonzero
    /// (gamemovement.cpp:1250, :1689). 0 when unsent: on the wire in a 2026 demo, absent from the 2009, 2013 and ESEA ones
    /// (*measured*, probe `schema`).
    /// </summary>
    public float Gravity { get => D.Gravity; init => _detail = ScenePlayerDetail.Normal(D with { Gravity = value }); }

    /// <summary>`m_Shared.m_nStreaks[ kTFStreak_Kills ]` (:604): the kill streak.</summary>
    public int? KillStreak { get => D.KillStreak; init => _detail = ScenePlayerDetail.Normal(D with { KillStreak = value }); }

    /// <summary>
    /// `m_Shared.m_flInvisChangeCompleteTime` (tf_player_shared.cpp:576, `SendPropTime`, sent to everyone): when a cloak or
    /// decloak finishes, on the server's clock — what `InvisibilityThink` (:7977) ramps `GetPercentInvisible` by. Null unsent.
    /// </summary>
    public float? InvisChangeCompleteTime { get => D.InvisChangeCompleteTime; init => _detail = ScenePlayerDetail.Normal(D with { InvisChangeCompleteTime = value }); }

    /// <summary>
    /// The screen overlay the LOCAL player's condition hooks have left in `view-&gt;SetScreenOverlayMaterial`, as
    /// <see cref="ScreenOverlay"/> steps it; null for nobody's, and for every player but the recorder.
    /// </summary>
    public string? ScreenOverlayMaterial { get => D.ScreenOverlayMaterial; init => _detail = ScenePlayerDetail.Normal(D with { ScreenOverlayMaterial = value }); }

    /// <summary>`m_Shared.m_flCloakMeter` (:586): 0..100 — a motion cloak at 0 fades by speed (`InvisibilityThink`, :8019).</summary>
    public float? CloakMeter { get => D.CloakMeter; init => _detail = ScenePlayerDetail.Normal(D with { CloakMeter = value }); }

    /// <summary>`m_Shared.m_hDisguiseWeapon` (:584) as its entity slot, or null for none.</summary>
    public int? DisguiseWeapon { get => D.DisguiseWeapon; init => _detail = ScenePlayerDetail.Normal(D with { DisguiseWeapon = value }); }

    /// <summary>The item <see cref="DisguiseWeapon"/> is, or null for none or unsent.</summary>
    public SceneItem? DisguiseWeaponItem { get => D.DisguiseWeaponItem; init => _detail = ScenePlayerDetail.Normal(D with { DisguiseWeaponItem = value }); }

    /// <summary>`GetSkinOverride()`: `m_iPlayerSkinOverride` (c_tf_player.cpp:3833), 1 for a zombie; null when unsent.</summary>
    public int? PlayerSkinOverride { get => D.PlayerSkinOverride; init => _detail = ScenePlayerDetail.Normal(D with { PlayerSkinOverride = value }); }

    /// <summary>`GetDisguisedSkinOverride()`: `m_Shared.m_nDisguiseSkinOverride` (tf_player_shared.cpp:579).</summary>
    public int? DisguiseSkinOverride { get => D.DisguiseSkinOverride; init => _detail = ScenePlayerDetail.Normal(D with { DisguiseSkinOverride = value }); }

    /// <summary>`m_Shared.m_iDecapitations` (tf_player_shared.cpp:558, sent to everyone): the Eyelander's heads, or null.</summary>
    public int? Decapitations { get => D.Decapitations; init => _detail = ScenePlayerDetail.Normal(D with { Decapitations = value }); }

    /// <summary>`m_Shared.m_flRageMeter` (tf_player_shared.cpp:517, `DT_TFPlayerSharedLocal`): 0..100, the recorder's alone.</summary>
    public float? RageMeter { get => D.RageMeter; init => _detail = ScenePlayerDetail.Normal(D with { RageMeter = value }); }

    /// <summary>`m_Shared.m_bRageDraining` (:518, `DT_TFPlayerSharedLocal`): `IsRageDraining()`.</summary>
    public bool RageDraining { get => D.RageDraining; init => _detail = ScenePlayerDetail.Normal(D with { RageDraining = value }); }

    /// <summary>`m_Shared.m_flItemChargeMeter[ slot ]` (:521, `DT_TFPlayerSharedLocal`): per loadout slot through `MISC2`, 0..100.</summary>
    public IReadOnlyList<float>? ItemChargeMeter { get => D.ItemChargeMeter; init => _detail = ScenePlayerDetail.Normal(D with { ItemChargeMeter = value }); }

    /// <summary>`m_Shared.m_flHypeMeter` (:570): `GetScoutHypeMeter()`, 0..100.</summary>
    public float? HypeMeter { get => D.HypeMeter; init => _detail = ScenePlayerDetail.Normal(D with { HypeMeter = value }); }

    /// <summary>`m_Shared.m_iRevengeCrits` (:559): `GetRevengeCrits()`.</summary>
    public int? RevengeCrits { get => D.RevengeCrits; init => _detail = ScenePlayerDetail.Normal(D with { RevengeCrits = value }); }

    /// <summary>`m_Shared.m_flRuneCharge` (:607): `GetRuneCharge()`, 0..100 in 8 bits.</summary>
    public float? RuneCharge { get => D.RuneCharge; init => _detail = ScenePlayerDetail.Normal(D with { RuneCharge = value }); }

    /// <summary>`m_flKartNextAvailableBoost` (c_tf_player.cpp:3824): when a kart's boost is ready again, server clock.</summary>
    public float? KartNextAvailableBoost { get => D.KartNextAvailableBoost; init => _detail = ScenePlayerDetail.Normal(D with { KartNextAvailableBoost = value }); }

    /// <summary>`m_iKartHealth` (c_tf_player.cpp:3825): `GetKartHealth()`, the kart's damage percent.</summary>
    public int? KartHealth { get => D.KartHealth; init => _detail = ScenePlayerDetail.Normal(D with { KartHealth = value }); }

    /// <summary>`m_iSpawnCounter` (tf_player.cpp:817): toggled at each spawn; a change runs `ClientPlayerRespawn` (c_tf_player.cpp:4543).</summary>
    public int? SpawnCounter { get => D.SpawnCounter; init => _detail = ScenePlayerDetail.Normal(D with { SpawnCounter = value }); }

    /// <summary>
    /// `m_vecVelocity` (player.cpp:8140-8142, `DT_LocalPlayerExclusive`): the recorder's own velocity, which is its
    /// `GetAbsVelocity` on the client; null for anyone else.
    /// </summary>
    /// <remarks>Inline rather than in the shared half: it changes on 75% of f12's player-frames (D217).</remarks>
    public (float X, float Y, float Z)? Velocity { get; init; }

    /// <summary>The movement state prediction restores (D205, B450); null for anyone but the recorder.</summary>
    /// <remarks>Inline rather than in the shared half: it changes on 92% of f12's player-frames (D217).</remarks>
    public SceneLocalMovement? Movement { get; init; }

    /// <summary>The active weapon's `m_iAccountID` (econ_item_view.cpp:187): whose item it is; null with none held or unsent.</summary>
    public uint? WeaponAccountId { get => D.WeaponAccountId; init => _detail = ScenePlayerDetail.Normal(D with { WeaponAccountId = value }); }

    /// <summary>
    /// `GetDeathTime()`: `m_flDeathTime`, `DT_LocalPlayerExclusive` (c_baseplayer.cpp:254) — so 0 for anyone but the recorder,
    /// whose client entity is zeroed when allocated (c_baseentity.cpp:3840) and never receives it.
    /// </summary>
    public float DeathTime { get => D.DeathTime; init => _detail = ScenePlayerDetail.Normal(D with { DeathTime = value }); }

    /// <summary>The active weapon's `m_iEntityQuality` (econ_item_view.cpp:188).</summary>
    public int? WeaponQuality { get => D.WeaponQuality; init => _detail = ScenePlayerDetail.Normal(D with { WeaponQuality = value }); }

    /// <summary>
    /// `HasTheFlag()` (tf_player_shared.cpp:11223) with no exceptions: `m_hItem` (tf_player.cpp:798, sent to everyone) names
    /// an item whose `GetItemID()` is `TF_ITEM_CAPTURE_FLAG` — a `CCaptureFlag`, the only `C_TFItem` there is.
    /// </summary>
    public bool HasTheFlag { get => D.HasTheFlag; init => _detail = ScenePlayerDetail.Normal(D with { HasTheFlag = value }); }

    /// <summary>
    /// The medigun among the weapons that arrived — `Weapon_OwnsThisID( TF_WEAPON_MEDIGUN )` over `m_hMyWeapons` — as its
    /// `m_flChargeLevel`, `m_iEntityQuality` and definition; null when none arrived, which for anyone but the recorder is
    /// every point-of-view demo.
    /// </summary>
    public (float Charge, int Quality, int? Definition)? Medigun { get => D.Medigun; init => _detail = ScenePlayerDetail.Normal(D with { Medigun = value }); }

    /// <summary>
    /// The medigun in hand, when the active weapon is one: its `m_hHealingTarget` (`GetHealTarget`, what `MedicGetHealTarget`
    /// returns — tf_player_shared.cpp:13021) and its `m_flChargeLevel`, which `CWeaponMedigun::ClientThink` hands the heal
    /// target's `SetHealer` (tf_weapon_medigun.cpp:2299). Both are sent to everyone, so every medic in view has one.
    /// </summary>
    public (int? HealTarget, float Charge)? ActiveMedigun { get => D.ActiveMedigun; init => _detail = ScenePlayerDetail.Normal(D with { ActiveMedigun = value }); }

    /// <summary>`m_iFOV` (player.cpp:8186, sent to everyone): the zoomed field of view, 0 for none.</summary>
    public int? Fov { get => D.Fov; init => _detail = ScenePlayerDetail.Normal(D with { Fov = value }); }

    /// <summary>`m_iFOVStart` (:8187): where a zoom lerps from.</summary>
    public int? FovStart { get => D.FovStart; init => _detail = ScenePlayerDetail.Normal(D with { FovStart = value }); }

    /// <summary>`m_flFOVTime` (:8188): when the zoom began.</summary>
    public float? FovTime { get => D.FovTime; init => _detail = ScenePlayerDetail.Normal(D with { FovTime = value }); }

    /// <summary>`m_Local.m_flFOVRate`: how long the zoom takes — `DT_Local`, so the recorder's alone.</summary>
    public float? FovRate { get => D.FovRate; init => _detail = ScenePlayerDetail.Normal(D with { FovRate = value }); }

    /// <summary>`m_iDefaultFOV` (:8189): the player's `fov_desired`, clamped to 75..90 by the server (tf_gamerules.cpp:10275).</summary>
    public int? DefaultFov { get => D.DefaultFov; init => _detail = ScenePlayerDetail.Normal(D with { DefaultFov = value }); }

    /// <summary>`m_vecViewOffset[2]` (c_baseplayer.cpp:229, `DT_LocalPlayerExclusive`): the recorder's eye height, `CheckWater`'s eye point.</summary>
    public float? ViewOffsetZ { get => D.ViewOffsetZ; init => _detail = ScenePlayerDetail.Normal(D with { ViewOffsetZ = value }); }

    /// <summary>`m_Shared.m_flMovementStunTime` (tf_player_shared.cpp:368): the active stun's duration.</summary>
    public float? MovementStunTime { get => D.MovementStunTime; init => _detail = ScenePlayerDetail.Normal(D with { MovementStunTime = value }); }

    /// <summary>`m_Shared.m_iMovementStunAmount` (:369): the active stun's amount, 0..255.</summary>
    public int? MovementStunAmount { get => D.MovementStunAmount; init => _detail = ScenePlayerDetail.Normal(D with { MovementStunAmount = value }); }

    /// <summary>`m_Shared.m_iMovementStunParity` (:370): changes with each new stun; the client restarts its stun clock then (:1440).</summary>
    public int? MovementStunParity { get => D.MovementStunParity; init => _detail = ScenePlayerDetail.Normal(D with { MovementStunParity = value }); }

    /// <summary>`m_bAllowMoveDuringTaunt` (c_tf_player.cpp:3793).</summary>
    public bool AllowMoveDuringTaunt { get => D.AllowMoveDuringTaunt; init => _detail = ScenePlayerDetail.Normal(D with { AllowMoveDuringTaunt = value }); }

    /// <summary>`m_flCurrentTauntMoveSpeed` (c_tf_player.cpp:3800): a moving taunt's or a kart's current speed.</summary>
    public float? CurrentTauntMoveSpeed { get => D.CurrentTauntMoveSpeed; init => _detail = ScenePlayerDetail.Normal(D with { CurrentTauntMoveSpeed = value }); }

    /// <summary>`m_flVehicleReverseTime` (c_tf_player.cpp:3801): when a stopped kart starts reversing.</summary>
    public float? VehicleReverseTime { get => D.VehicleReverseTime; init => _detail = ScenePlayerDetail.Normal(D with { VehicleReverseTime = value }); }

    /// <summary>`m_hGrapplingHookTarget` (c_tf_player.cpp:3827) as its entity slot, or null for none.</summary>
    public int? GrapplingHookTarget { get => D.GrapplingHookTarget; init => _detail = ScenePlayerDetail.Normal(D with { GrapplingHookTarget = value }); }

    /// <summary>`m_iTauntItemDefIndex` (c_tf_player.cpp:3799): the taunt item when no loadout slot names it.</summary>
    public int? TauntItemDefIndex { get => D.TauntItemDefIndex; init => _detail = ScenePlayerDetail.Normal(D with { TauntItemDefIndex = value }); }

    /// <summary>`m_nActiveTauntSlot` (c_tf_player.cpp:3798): the taunt's loadout slot, −1 (`LOADOUT_POSITION_INVALID`) for none.</summary>
    public int? ActiveTauntSlot { get => D.ActiveTauntSlot; init => _detail = ScenePlayerDetail.Normal(D with { ActiveTauntSlot = value }); }

    /// <summary>The rarely-changing half, shared between frames by reference (D217); null reads as every default.</summary>
    private readonly ScenePlayerDetail? _detail;

    private ScenePlayerDetail D => _detail ?? ScenePlayerDetail.Empty;

    /// <summary>The detail as a whole: set in one assignment by the timeline, and handed between frames to share it.</summary>
    internal ScenePlayerDetail Detail { get => _detail ?? ScenePlayerDetail.Empty; init => _detail = ScenePlayerDetail.Normal(value); }

    /// <summary>Whether the player is crouched, when the recording says.</summary>
    /// <remarks>
    /// <c>FL_DUCKING</c>. Null flags mean the recording never said, which is every player but the
    /// recorder in a POV demo — a SourceTV recording carries them for all of them. So this answers
    /// false for "not crouched" and for "not known" alike, and a caller that needs to tell them
    /// apart must look at <see cref="Flags"/> itself.
    ///
    /// **Written as a null check rather than as a masked comparison, because the obvious form is
    /// wrong.** <c>(Flags &amp; Ducking) != 0</c> lifts to a nullable comparison, and in C# a null
    /// compared with <c>!=</c> to zero is TRUE — so every player whose flags never arrived read as
    /// permanently crouched. Caught by the completeness test that checks each property differs from
    /// its default, which is the one place that shape shows up as a value rather than as a crash.
    /// </remarks>
    public bool IsCrouched =>
        Flags is { } ducking && (ducking & PlayerActivityState.Ducking) != 0;

    /// <summary>Whether the player is off the ground, when the recording says.</summary>
    /// <remarks>
    /// <c>FL_ONGROUND</c> absent. Null flags answer false rather than true, deliberately: an
    /// unknown state should draw a player standing on the floor rather than permanently falling.
    /// </remarks>
    public bool IsAirborne => Flags is { } flags && (flags & PlayerActivityState.OnGround) == 0;

    /// <summary>Whether this is someone actually playing, rather than watching.</summary>
    /// <remarks>
    /// **The distinction a map view has to make.** Team 0 is unassigned and team 1 is spectator;
    /// only 2 and 3 are playing. A viewer that draws everything shows the SourceTV camera as a
    /// player, and it moves convincingly - it follows the action, because that is its job.
    /// </remarks>
    public bool IsPlaying => Team is SceneTeams.Red or SceneTeams.Blu;

    /// <summary>Whether this player is alive and standing in the world.</summary>
    /// <remarks>
    /// **A dead player is still on a team and still has a position — the position of whoever they
    /// are spectating.** So a corpse drawn as a player appears standing inside the living player
    /// it is watching, and several of them stack into one heap. That is what "two soldiers in a
    /// ball" was.
    ///
    /// **Absent means alive**, because <c>LIFE_ALIVE</c> is zero and a delta-compressed format
    /// only sends what changed. Reading absence as unknown would hide everyone who has not died.
    /// </remarks>
    public bool IsAlive => LifeState is null or 0;

    /// <summary>Whether the engine would consider this player's view first person.</summary>
    /// <remarks>
    /// **<c>C_BasePlayer::LocalPlayerInFirstPersonView</c>, <c>c_baseplayer.cpp:1919</c>:**
    ///
    /// <code>
    ///   int ObserverMode = pLocalPlayer->GetObserverMode();
    ///   if ( ( ObserverMode == OBS_MODE_NONE ) || ( ObserverMode == OBS_MODE_IN_EYE ) )
    ///   {
    ///       return !input->CAM_IsThirdPerson() &amp;&amp; ...;
    ///   }
    ///
    ///   // Not looking at the local player, e.g. in a replay in third person mode or freelook.
    ///   return false;
    /// </code>
    ///
    /// An allowlist of two, and the SDK's own comment warns against treating the enum as ordered:
    /// <c>OBS_MODE_POI</c> was inserted at 6 *"due to tons of hard-coded '&lt;ROAMING' enum
    /// compares"*. So this compares against the two values rather than against a threshold.
    ///
    /// **Absent means <see cref="ObserverModes.None"/>**, because zero is the default and a
    /// delta-compressed format sends only what changed — the same rule as
    /// <see cref="IsAlive"/>. A recording that never mentions the field is a recording of someone
    /// who never observed, not an unknown.
    /// </remarks>
    public bool InFirstPersonView =>
        ObserverMode is null or ObserverModes.None or ObserverModes.InEye;
}

/// <summary>The engine's observer modes, <c>shareddefs.h:492</c>.</summary>
/// <remarks>
/// Sent as three bits unsigned (<c>player.cpp:8184</c>), which is exactly enough for 0..7 — every
/// value the enum defines fits and none is unrepresentable.
///
/// **In <c>DT_BasePlayer</c> proper rather than <c>DT_LocalPlayerExclusive</c>**, so it arrives for
/// every player in any recording, not only for the one holding the camera.
/// </remarks>
public static class ObserverModes
{
    /// <summary>Not in spectator mode — playing.</summary>
    public const int None = 0;

    /// <summary>The death cam animation.</summary>
    public const int DeathCam = 1;

    /// <summary>Zooms to a target and freeze-frames on them.</summary>
    public const int FreezeCam = 2;

    /// <summary>A fixed camera position.</summary>
    public const int Fixed = 3;

    /// <summary>Following a player in first person.</summary>
    public const int InEye = 4;

    /// <summary>Following a player in third person.</summary>
    public const int Chase = 5;

    /// <summary>A PASSTIME point of interest.</summary>
    /// <remarks>
    /// **Inserted in the MIDDLE of the enum**, and the SDK says why: *"added in the middle of the
    /// enum due to tons of hard-coded '&lt;ROAMING' enum compares"*. Anything here comparing modes
    /// by ordering rather than by value would be wrong for a reason Valve documented in advance.
    /// </remarks>
    public const int PointOfInterest = 6;

    /// <summary>Free roaming — where TF2 puts a player who goes to spectator.</summary>
    public const int Roaming = 7;
}

/// <summary>The engine's team numbers.</summary>
/// <remarks>
/// From <c>shareddefs.h</c> and <c>tf_shareddefs.h</c>: the first two are shared by every Source
/// game, and TF2 numbers its own from <c>LAST_SHARED_TEAM + 1</c>.
/// </remarks>
public static class SceneTeams
{
    /// <summary>Connected but not yet on a team.</summary>
    public const int Unassigned = 0;

    /// <summary>Watching rather than playing; includes a SourceTV camera.</summary>
    public const int Spectator = 1;

    /// <summary>RED.</summary>
    public const int Red = 2;

    /// <summary>BLU.</summary>
    public const int Blu = 3;
}

/// <summary>Where the time went while a demo was decoded, in milliseconds.</summary>
/// <param name="Commands">Splitting the file into demo commands.</param>
/// <param name="Schema">Parsing <c>dem_datatables</c> into send tables.</param>
/// <param name="Messages">Decoding each packet's net messages.</param>
/// <param name="Entities">Entity delta decode, plus applying and recording what it produced.</param>
/// <param name="Sampling">The per-packet walks: viewmodels, fog and soundscape.</param>
/// <param name="Viewmodels">The viewmodel share of <paramref name="Sampling"/>, which contains it.</param>
/// <param name="Frames">Building each moved packet's frame: players, game rules, and the HUD's entity lists.</param>
/// <param name="Total">The whole of <see cref="DemoTimeline.Build"/>.</param>
/// <remarks>
/// **Building the timeline is thirty seconds on a fourteen-minute match and had no columns at
/// all** (B265). It was one opaque number logged from outside, which is the state the FRAME was in
/// before it was split — and splitting the frame is what took it from 96 to 447 fps, because the
/// fat turned out to be somewhere nobody had guessed. The same rule applies to a number the owner
/// waits through every time he opens a real demo.
///
/// **`Sampling` is separated from `Entities` deliberately**, because they are opposite shapes:
/// entity work is proportional to what the demo SAID, and the sampling walks are proportional to
/// what exists times how many packets there are. Only the second kind can be large for a reason
/// nobody intended.
/// </remarks>
public readonly record struct TimelinePhases(
    double Commands,
    double Schema,
    double Messages,
    double Entities,
    double Sampling,
    double Viewmodels,
    double Frames,
    double Total);

/// <summary>Where everyone was at one tick.</summary>
/// <param name="Tick">The demo tick this was recorded at.</param>
/// <param name="Players">Every player with a known position.</param>
/// <param name="RecorderTeam">
/// The local player's team at this tick, or <c>null</c> when no player entity sits at the
/// recorder's slot, where the engine's own <c>pLocalPlayer &amp;&amp;</c> guards fall through. A
/// SourceTV recording has a local player, the SourceTV client on the spectator team
/// (<c>docs/findings/58</c>).
/// <para>
/// **Per frame rather than per demo, because a player can switch teams mid-recording.** Everything
/// that compares against the local player — <c>IsEnemyPlayer</c>, a spawn wall's own team — would
/// otherwise answer with whichever team happened to be resolved last.
/// </para>
/// </param>
/// <param name="RoundState">
/// <c>m_iRoundState</c> from the game rules at this tick, or <c>null</c> when the demo carries no
/// game rules entity. <c>GR_STATE_TEAM_WIN</c> is 5.
/// </param>
/// <param name="Rules">The game rules the client's HUD tests at this tick.</param>
/// <param name="ServerTick">
/// The last `net_Tick` — the server's `gpGlobals->tickcount`, the clock networked times such as `m_flFOVTime` are on — or
/// null before one arrived.
/// </param>
/// <param name="RoundTimers">Every `team_round_timer`, or null when there is none.</param>
/// <param name="Buildings">Every Engineer building, or null when there is none.</param>
/// <param name="Teams">Every `CTFTeam` (RED, BLU, and any others sent), or null when there is none.</param>
/// <param name="ScoreboardPlayers">
/// Every player slot the scoreboard's `UpdatePlayerList` would list, or null when there is no
/// resource entity. See <see cref="SceneScoreboardPlayer"/>.
/// </param>
/// <param name="IdEntities">Every dropped weapon and revive marker, or null when there is none.</param>
public readonly record struct TimelineFrame(
    int Tick,
    IReadOnlyList<ScenePlayer> Players,
    int? RecorderTeam = null,
    int? RoundState = null,
    SceneGameRules Rules = default,
    int? ServerTick = null,
    IReadOnlyList<SceneRoundTimer>? RoundTimers = null,
    IReadOnlyList<SceneBuilding>? Buildings = null,
    IReadOnlyList<SceneTeam>? Teams = null,
    IReadOnlyList<SceneScoreboardPlayer>? ScoreboardPlayers = null,
    IReadOnlyList<SceneIdEntity>? IdEntities = null);

/// <summary>What the client's HUD asks of `TFGameRules()` and the logic entities at one tick.</summary>
/// <param name="MannVsMachine">`IsMannVsMachineMode()`: `m_bPlayingMannVsMachine` (tf_gamerules.h:606).</param>
/// <param name="HalloweenScenario">`m_halloweenScenario`, which `IsHalloweenScenario` compares (tf_gamerules.h:1549): 3 is
/// Lakeside, 4 Hightower, 5 Doomsday.</param>
/// <param name="PlayerDestruction">Whether a `CTFPlayerDestructionLogic` exists — the only logic whose `GetType()` is
/// `TYPE_PLAYER_DESTRUCTION`.</param>
public readonly record struct SceneGameRules(bool MannVsMachine, int HalloweenScenario, bool PlayerDestruction)
{
    /// <summary>`TF_GAMETYPE_ARENA` (tf_shareddefs.h:283).</summary>
    public const int GameTypeArena = 4;

    /// <summary>`IsInWaitingForPlayers()`: `m_bInWaitingForPlayers` (teamplayroundbased_gamerules.cpp:108).</summary>
    public bool WaitingForPlayers { get; init; }

    /// <summary>`InOvertime()`: `m_bInOvertime` (:110).</summary>
    public bool Overtime { get; init; }

    /// <summary>`InSetup()`: `m_bInSetup` (:111).</summary>
    public bool Setup { get; init; }

    /// <summary>`m_bStopWatch` (:120).</summary>
    public bool StopWatch { get; init; }

    /// <summary>`m_nGameType` (tf_gamerules.cpp:1486); `IsInArenaMode()` is <see cref="GameTypeArena"/>.</summary>
    public int GameType { get; init; }

    /// <summary>`GetCurrentMatchGroup()`: `m_nMatchGroupType` (tf_gamerules.cpp:1536), `k_eTFMatchGroup_Invalid` (-1) unsent.</summary>
    public int MatchGroup { get; init; } = -1;

    /// <summary>
    /// `IsCompetitiveMode()` (tf_gamerules.cpp:2214): the match group's description is competitive or casual — of the
    /// registered groups, ladder 6v6 (2), casual 12v12 (7) and the event placeholder (8; tf_match_description_*.cpp).
    /// </summary>
    public bool IsCompetitiveMode => MatchGroup is 2 or 7 or 8;

    /// <summary>`m_flNextRespawnWave[ RED ]`, `[ BLU ]` (teamplayroundbased_gamerules.cpp:97), server time.</summary>
    public (float Red, float Blue) NextRespawnWave { get; init; }

    /// <summary>`m_TeamRespawnWaveTimes[ RED ]`, `[ BLU ]` (:98): -1, the constructor's (:425), until the map sets one.</summary>
    public (float Red, float Blue) TeamRespawnWaveTimes { get; init; } = (-1f, -1f);

    /// <summary>
    /// `CTFRobotDestructionLogic`'s `m_flRedTeamRespawnScale`, `m_flBlueTeamRespawnScale` (tf_logic_robot_destruction.cpp:1440-1441),
    /// or null with no such logic.
    /// </summary>
    public (float Red, float Blue)? RobotDestructionRespawnScale { get; init; }

    /// <summary>`GetRoundsPlayed()`: `m_nRoundsPlayed` (teamplayroundbased_gamerules.cpp:116).</summary>
    public int RoundsPlayed { get; init; }

    /// <summary>`m_flMapResetTime` (teamplayroundbased_gamerules.cpp:95), server time the map clock started.</summary>
    public float MapResetTime { get; init; }

    /// <summary>`CTFMinigameLogic::GetMinigameLogic()->GetActiveMinigame()` (tf_logic_halloween_2014.h:190): the logic
    /// entity's `m_hActiveMinigame` (tf_logic_halloween_2014.cpp:726) names an entity.</summary>
    public bool ActiveMinigame { get; init; }

    /// <summary>`IsInKothMode()`: `m_bPlayingKoth` (:1507).</summary>
    public bool Koth { get; init; }

    /// <summary>`CTFPlayerResource::GetPartyLeaderRedTeamIndex()`: `m_iPartyLeaderRedTeamIndex` (c_tf_playerresource.cpp:45), 0 unsent.</summary>
    public int PartyLeaderRed { get; init; }

    /// <summary>`GetPartyLeaderBlueTeamIndex()`: `m_iPartyLeaderBlueTeamIndex` (:46).</summary>
    public int PartyLeaderBlue { get; init; }

    /// <summary>`GetEventTeamStatus()`: `m_iEventTeamStatus` (:47) — 1 invaders are pyros, 2 heavies.</summary>
    public int EventTeamStatus { get; init; }

    /// <summary>`HasPremadeParties()` (c_tf_playerresource.h:65): both leaders set.</summary>
    public bool HasPremadeParties => PartyLeaderRed > 0 && PartyLeaderBlue > 0;

    /// <summary>`MapHasMatchSummaryStage()`: `m_bMapHasMatchSummaryStage` (tf_gamerules.cpp:1473).</summary>
    public bool MapHasMatchSummaryStage { get; init; }

    /// <summary>`ShowMatchSummary()`: `m_bShowMatchSummary` (:1540).</summary>
    public bool ShowMatchSummary { get; init; }

    /// <summary>`ObjectiveResource()->GetTimerToShowInHUD()`: `m_iTimerToShowInHUD` (team_objectiveresource.cpp:25), 0 for none.</summary>
    public int TimerToShowInHud { get; init; }

    /// <summary>`GetBlueKothRoundTimer()`: `m_hBlueKothTimer`'s entity index (tf_gamerules.cpp:1513), or null.</summary>
    public int? BlueKothTimer { get; init; }

    /// <summary>`GetRedKothRoundTimer()`: `m_hRedKothTimer`'s entity index (:1512), or null.</summary>
    public int? RedKothTimer { get; init; }

    /// <summary>`IsInTraining()`: `m_bIsInTraining` (tf_gamerules.h:678,1161; RecvPropBool tf_gamerules.cpp:1427).</summary>
    public bool InTraining { get; init; }

    /// <summary>
    /// `GetWinningTeam()`: `m_iWinningTeam` (teamplayroundbased_gamerules.h:1747,1754; RecvPropInt
    /// teamplayroundbased_gamerules.cpp:89) — null before any round has been won.
    /// </summary>
    public int? WinningTeam { get; init; }

    /// <summary>
    /// `IsMatchTypeCompetitive()` (tf_gamerules.cpp:2237): competitive ONLY, unlike <see cref="IsCompetitiveMode"/> — of
    /// the registered groups, only ladder 6v6 (2) and the event placeholder (8) set `MATCH_TYPE_COMPETITIVE`
    /// (`tf_match_description_comp.cpp`'s `CLadderMatchGroupDescription`, which the placeholder also derives from); casual
    /// 12v12 (7) sets `MATCH_TYPE_CASUAL` (`tf_match_description_casual.cpp:54`).
    /// </summary>
    public bool IsMatchTypeCompetitive => MatchTypeCompetitive(MatchGroup);

    /// <summary><see cref="IsMatchTypeCompetitive"/> of a raw `m_nMatchGroupType`, for a reader holding no rules (B112).</summary>
    /// <param name="matchGroup">`m_nMatchGroupType`, -1 when unsent.</param>
    /// <returns>Whether the match group's description is `MATCH_TYPE_COMPETITIVE`.</returns>
    /// <remarks>
    /// **The one copy of the rule.** The gesture context asks it at the moment an event arrives, from the live game
    /// rules entity, before any <see cref="SceneGameRules"/> for that tick exists.
    /// </remarks>
    public static bool MatchTypeCompetitive(int matchGroup) => matchGroup is 2 or 8;

    /// <summary>`IsHolidayMap( n )`: `m_nMapHolidayType` (tf_gamerules.h:595), 0 (`kHoliday_None`) when unsent.</summary>
    public int MapHolidayType { get; init; }

    /// <summary>
    /// `GetRopesHolidayLightsAllowed()`: `m_bRopesHolidayLightsAllowed` (tf_gamerules.h:1050, RecvPropBool
    /// tf_gamerules.cpp:1483) — the constructor's true (:1644) when unsent.
    /// </summary>
    public bool RopesHolidayLightsAllowed { get; init; } = true;

    /// <summary>`IsPowerupMode()`: `m_bPowerupMode` (RecvPropBool tf_gamerules.cpp:1467), the Mannpower grapple's mode.</summary>
    public bool PowerupMode { get; init; }

    /// <summary>Whether a game rules entity exists — `TFGameRules()` is not null.</summary>
    public bool Present { get; init; }
}

/// <summary>One corpse, as <c>DT_TFRagdoll</c> describes it.</summary>
/// <param name="EntityIndex">Its entity slot.</param>
/// <param name="Serial">
/// The slot's serial, which is what distinguishes one corpse from the next occupant of the same
/// slot. Without it a match's dead are undercounted by more than half, because slots are reused
/// briskly and every reuse collapses into its predecessor (B92's lesson, met again).
/// </param>
/// <param name="PlayerClass">
/// <c>m_iClass</c>, which is what the model is derived from. The class NUMBER rather than a path,
/// because turning it into <c>models/player/spy.mdl</c> needs the game install and that is the
/// Scene layer's job — the same split a player already uses.
/// </param>
/// <param name="Team">
/// <c>m_iTeam</c>. The skin follows from it directly: <c>m_nSkin = (m_iTeam == TF_TEAM_RED) ? 0 : 1</c>
/// (`c_tf_player.cpp:712`).
/// </param>
/// <param name="X">Where it came to rest — <c>m_vecRagdollOrigin</c>.</param>
/// <param name="Y">As above.</param>
/// <param name="Z">As above.</param>
/// <param name="Gib">
/// <c>m_bGib</c>, and it is NETWORKED — `RecvPropBool( RECVINFO( m_bGib ) )`
/// (`c_tf_player.cpp:524`). Whether a death gibbed is recorded rather than guessed, which is worth
/// stating because the animation-versus-physics choice beside it is not (D136).
/// </param>
/// <param name="Burning">
/// <c>m_bBurning</c>, which the client uses to pick a burning corpse material.
/// </param>
/// <param name="FeignDeath">
/// <c>m_bFeignDeath</c>. A dead ringer spy's corpse copies its body group from the player unless
/// this is set without <c>m_bWasDisguised</c>.
/// </param>
/// <param name="WasDisguised">
/// <c>m_bWasDisguised</c>, the other half of that condition.
/// </param>
/// <param name="FirstTick">
/// When the corpse entered. A corpse must not be drawn before the death that made it, and the
/// entity's own arrival is the only record of when that was — nothing on <c>DT_TFRagdoll</c> carries
/// a time.
/// </param>
/// <param name="LastTick">
/// The last tick the corpse was spoken about. **Not when it stops being drawn**, which the engine
/// decides with <c>StartFadeOut( cl_ragdoll_fade_time.GetFloat() )</c> on a client-side timer
/// (`c_tf_player.cpp:869`) rather than by the server removing it.
/// </param>
/// <param name="Yaw">
/// Which way it faces, taken from the player it was — <c>SetAbsAngles( pPlayer-&gt;GetRenderAngles() )</c>
/// (`c_tf_player.cpp:766`). **Not on the corpse's own table**, which being `NOBASE` has no
/// `m_angRotation`; zero when the player could not be resolved, which is what leaves every body in a
/// match facing the same way.
/// </param>
/// <param name="DamageCustom">
/// <c>m_iDamageCustom</c> — how the death happened, and the only thing that decides whether a death
/// ANIMATION is even possible. `GetSequenceForDeath` switches on it and returns -1 for everything
/// but headshots, decapitations and backstabs (`tf_player_shared.cpp:13441-13454`).
/// </param>
/// <param name="PlayerIndex">
/// Which player entity this corpse was, already resolved from whichever of the two fields its era
/// sends (B319). Null when the player could not be found — out of the recorder's PVS, or gone.
/// **The corpse's cosmetics hang off this**, since the engine builds them by walking the PLAYER's
/// wearable list rather than the corpse's own <c>m_hRagWearables</c>, which nothing draws from.
/// </param>
/// <param name="Worn">
/// The models the player was wearing at the moment of death, captured then because they are gone
/// later (B320). The engine does the same and at the same moment:
/// <c>CreateBoneAttachmentsFromWearables</c> runs from inside <c>CreateTFRagdoll</c> and reads
/// <c>GetNumWearables()</c> on the PLAYER (`c_tf_player.cpp:10169`).
/// </param>
/// <param name="OnGround">
/// <c>m_bOnGround</c>, which vetoes the death animation: <c>if ( !m_bOnGround &amp;&amp; bPlayDeathAnim
/// &amp;&amp; !bPlayDeathInAir ) bPlayDeathAnim = false;</c> (`c_tf_player.cpp:838-839`), under Valve's
/// comment *"Don't play most death anims in the air (headshot, etc)"*.
/// </param>
/// <param name="Gold">
/// <c>m_bGoldRagdoll</c> — a Saxxy or Golden Wrench kill, which paints the whole corpse with
/// <c>models/player/shared/gold_player.vmt</c>.
/// </param>
/// <param name="Ice">
/// <c>m_bIceRagdoll</c> — a Spy-cicle backstab, painting it with
/// <c>models/player/shared/ice_player.vmt</c>. **Tested second in the engine and so wins over gold**
/// (`c_tf_player.cpp:962-971`).
/// </param>
/// <param name="Force">
/// <c>m_vecForce</c> — the impulse the kill put in, in kg·in/s
/// (<c>vphysics_interface.h:803</c>). Applied at <paramref name="ForceBone"/>'s body and then
/// spread over every other by mass share (<c>ragdoll_shared.cpp:661-681</c>). **This is what makes
/// a rocket death look different from a bullet death**, and without it every corpse falls straight
/// down however it was killed.
/// </param>
/// <param name="Velocity">
/// <c>m_vecRagdollVelocity</c> — the player's OWN motion at the moment of death, slammed onto the
/// corpse with <c>SetAbsVelocity( m_vecRagdollVelocity )</c> in both branches of
/// <c>CreateTFRagdoll</c> (`c_tf_player.cpp:774`, `:800`). Not the same thing as
/// <c>RagdollApplyAnimationAsVelocity</c>, which gives each LIMB its own velocity from two poses
/// 0.05 s apart; this gives the whole body the player's.
/// </param>
/// <param name="ForceBone">
/// <c>m_nForceBone</c> — which body took the hit. **An ELEMENT index by the time the solver sees
/// it**: `ragdoll.list[forceBone]`, guarded by `if ( forceBone &gt;= 0 &amp;&amp; forceBone &lt;
/// ragdoll.listCount )` (`ragdoll_shared.cpp:660`). Negative is a real value meaning "no bone", and
/// the centre-of-mass push is simply skipped.
/// </param>
/// <param name="Carried">
/// Everything the player had equipped at death — cosmetics AND weapons — with the held one marked,
/// for the corpse's <c>m_nBody</c> (B395). Distinct from <paramref name="Worn"/>, which is the
/// wearables that hang off the body; see <see cref="SceneCarriedItem"/> for why the engine needs
/// both sets.
/// </param>
/// <param name="Cloaked">
/// <c>m_bCloaked</c>: the corpse of a backstab by a knife that disguises its killer — Your Eternal Reward
/// (<c>ShouldDisguiseOnBackstab</c>, `tf_player.cpp:12707-12715`). **Not a feign death's**, whose ragdoll
/// (<c>CreateFeignDeathRagdoll</c>, `:15795`) never sets it. <c>C_TFRagdoll::ClientThink</c> fades it out by
/// <c>m_flPercentInvisible += frametime</c> to 1 (`c_tf_player.cpp:1392-1399`).
/// </param>
/// <remarks>
/// **The reason corpses are invisible is that they were never DESCRIBED, not that they were lost.**
/// `DT_TFRagdoll` is `NOBASE`, so it inherits no model index, no skin, no body and no angles; a prop
/// path asks for a model and correctly draws nothing. 299 in one match
/// (`docs/PARITY-AUDIT.md` #4). Everything the client needs to build those fields IS on the table,
/// which is what this carries.
/// </remarks>
public readonly record struct SceneRagdoll(
    int EntityIndex,
    int Serial,
    int? PlayerClass,
    int? Team,
    float X,
    float Y,
    float Z,
    bool Gib,
    bool Burning,
    bool FeignDeath,
    bool WasDisguised,
    int FirstTick,
    int LastTick,
    float Yaw = 0f,
    int? DamageCustom = null,
    int? PlayerIndex = null,
    IReadOnlyList<SceneWornItem>? Worn = null,
    bool OnGround = false,
    bool Gold = false,
    bool Ice = false,
    (float X, float Y, float Z)? Force = null,
    (float X, float Y, float Z)? Velocity = null,
    int? ForceBone = null,
    IReadOnlyList<SceneCarriedItem>? Carried = null,
    bool Cloaked = false);

/// <summary>One thing a corpse was wearing when it died.</summary>
/// <param name="Model">The model to draw, bone-merged onto the corpse.</param>
/// <param name="ItemDefinitionIndex">
/// Its econ item definition, or null when the entity did not say. **The two filters the engine
/// applies that this project could not, before B324, both key on it**: an item whose `drop_type` is
/// `drop` becomes a falling gib instead of staying on the body, and a decapitation drops the head
/// and misc slots. Both need `items_game.txt`, which the decode layer has no access to — so the
/// index travels and the Scene layer, which can open the install, does the filtering.
/// </param>
public readonly record struct SceneWornItem(string Model, int? ItemDefinitionIndex);

/// <summary>One item a player had equipped when he died, for the corpse's body (B395).</summary>
/// <param name="ItemDefinitionIndex">Which item, so the schema can say what it hides.</param>
/// <param name="Weapon">
/// Whether it is a weapon rather than a wearable. **Not cosmetic bookkeeping: it decides WHEN the
/// item applies.** `RecalculatePlayerBodygroups` runs three passes in a fixed order
/// (`tf_player_shared.cpp:13693-13709`) — weapons without the deployed-only flag, then wearables,
/// then deployed-only weapons — so two items touching the same body part resolve by that order.
/// </param>
/// <param name="Deployed">
/// Whether this was the weapon in his hands. Eight shipped items declare
/// <c>bHideBodygroupsDeployedOnly</c> and all eight are weapons, so the flag decides whether their
/// hidden parts apply — `if ( bHideBodygroupsDeployedOnly &amp;&amp; pPlayer-&gt;GetActiveWeapon() != pWpn )
/// continue;` (`tf_weaponbase.cpp:6226`).
/// </param>
/// <remarks>
/// **A second list beside <see cref="SceneWornItem"/>, because the engine asks two questions of two
/// different sets.** What hangs off a corpse is the econ WEARABLE list —
/// `CreateBoneAttachmentsFromWearables` walks `GetWearable( wbl )` (`c_tf_player.cpp:10178`), and a
/// weapon is a `CTFWeaponBase`, not in it. What the corpse's `m_nBody` is came from the PLAYER's
/// whole equipment, weapons included, because it is `pPlayer->GetBody()` copied whole (`:790-793`).
/// Filtering one list to serve both would be wrong in one direction or the other.
/// </remarks>
public readonly record struct SceneCarriedItem(
    int ItemDefinitionIndex, bool Weapon, bool Deployed);


/// <summary>
/// A demo turned into player positions over time.
/// </summary>
/// <remarks>
/// **Built once and kept, because a demo cannot be seeked.** It is a forward-only stream of deltas
/// against the previous state, so there is no way to know where anyone stood at tick 4,000 without
/// replaying every tick before it. Scrubbing backwards would mean re-reading from the start each
/// time, which is why the whole thing is walked once and the answers stored.
///
/// The cost is bounded and small next to the map: a few thousand frames of at most a couple of
/// dozen players, against a 24 MB BSP and its textures.
///
/// **A frame per packet, not per tick.** Positions arrive with <c>svc_PacketEntities</c> and the
/// server does not send one every tick, so the frames are irregular by nature and
/// <see cref="PlayersAt(int)"/> answers with the most recent one rather than requiring an exact
/// match.
/// </remarks>
public sealed class DemoTimeline
{
    /// <summary>The entity that carries every player's team and class.</summary>
    /// <remarks>
    /// **Team and class do not travel on the player entity, and on modern demos they are not there
    /// at all.** A positioned modern player carries only its health among the three; era demos do
    /// send <c>DT_BaseEntity.m_iTeamNum</c> on the player, which is what made this look like it
    /// worked before it was measured.
    ///
    /// Both live on a single <c>CTFPlayerResource</c> entity as arrays indexed by entity index —
    /// <c>m_iTeam.003</c>, <c>m_iPlayerClass.003</c> — which is one entity for the whole server
    /// rather than a copy per player.
    /// </remarks>
    private const string ResourceClass = "CTFPlayerResource";

    /// <summary>The class every player entity has, spectators and the SourceTV camera included.</summary>
    private const string PlayerClass = "CTFPlayer";

    /// <summary>The corpse entity, which is a separate entity from the player who died.</summary>
    /// <remarks>
    /// **A death is not an animation on the player** — the player goes `EF_NODRAW` and a
    /// `CTFRagdoll` appears (`docs/memory/death-is-ef-nodraw-not-an-animation.md`). So the corpse
    /// has its own slot, its own lifetime, and its own table.
    /// </remarks>
    private const string RagdollClass = "CTFRagdoll";

    /// <summary>The entity that carries the atmosphere.</summary>
    private const string FogControllerClass = "CFogController";

    /// <summary>The entity a choreographed scene arrives on (B351).</summary>
    private const string SceneEntityClass = "CSceneEntity";

    /// <summary>The table that declares <c>m_iState</c>, so a class reaching it is a weapon.</summary>
    /// <remarks>
    /// `SendPropInt( SENDINFO(m_iState), 8, SPROP_UNSIGNED )` lives in the MAIN
    /// `DT_BaseCombatWeapon` table (`basecombatweapon_shared.cpp:2871`) rather than in
    /// `DT_LocalWeaponData`, which is what makes another player's weapon answerable at all.
    /// </remarks>
    private const string CombatWeaponTable = "DT_BaseCombatWeapon";

    /// <summary>The entity that carries the round.</summary>
    /// <remarks>
    /// **`CTFGameRulesProxy`, not the teamplay one it inherits from.** A demo's schema declares
    /// three proxies — `CGameRulesProxy`, `CTeamplayRoundBasedRulesProxy` and `CTFGameRulesProxy` —
    /// and TF2 instantiates the last, which reaches `m_iRoundState` through
    /// `teamplayroundbased_gamerules_data`.
    /// </remarks>
    private const string GameRulesClass = "CTFGameRulesProxy";

    /// <summary>Where the round state is, flattened.</summary>
    private const string RoundStateProperty = "DT_TeamplayRoundBasedRules.m_iRoundState";

    /// <summary>`GetWinningTeam()`: `m_iWinningTeam` (teamplayroundbased_gamerules.cpp:89), read by the HUD and by `IsLoser`.</summary>
    private const string WinningTeamProperty = "DT_TeamplayRoundBasedRules.m_iWinningTeam";

    /// <summary>`GetCurrentMatchGroup()`: `m_nMatchGroupType` (tf_gamerules.cpp:1536), read by the HUD and by `IsLoser`.</summary>
    private const string MatchGroupProperty = "DT_TFGameRules.m_nMatchGroupType";

    /// <summary>`TF_CLASS_HEAVYWEAPONS` (tf_shareddefs.h:212), whose minigun freezes the air-walk while it spins.</summary>
    private const int HeavyClass = 6;

    /// <summary>`SND_IGNORE_PHONEMES`, soundflags.h:128.</summary>
    private const int IgnorePhonemesFlag = 1 << 8;

    /// <summary>`m_bPlayingMannVsMachine` (tf_gamerules.cpp:1517), reached like the round through the proxy.</summary>
    private const string MannVsMachineProperty = "DT_TFGameRules.m_bPlayingMannVsMachine";

    /// <summary>`m_halloweenScenario` (tf_gamerules.cpp:1531).</summary>
    private const string HalloweenScenarioProperty = "DT_TFGameRules.m_halloweenScenario";

    /// <summary>`IMPLEMENT_NETWORKCLASS_ALIASED( TFPlayerDestructionLogic, … )` (tf_logic_player_destruction.cpp:54).</summary>
    private const string PlayerDestructionClass = "CTFPlayerDestructionLogic";

    /// <summary>The objective resource, which names the timer the HUD shows.</summary>
    private const string ObjectiveResourceClass = "CTFObjectiveResource";

    /// <summary>A `team_round_timer`.</summary>
    private const string RoundTimerClass = "CTeamRoundTimer";

    /// <summary>A team entity — `DT_TFTeam`, which inherits `DT_Team` (`tf_team.cpp:43`, `team.cpp:40`).</summary>
    private const string TeamClass = "CTFTeam";

    // The pass's own helpers, read only by it: part of the builder, left where they stood (D217).
    private sealed partial class Builder
    {
    /// <summary>Every round timer, as the time panel reads it; null when there is none, which costs no allocation.</summary>
    private static List<SceneRoundTimer>? RoundTimers(EntityStateTable entities)
    {
        List<SceneRoundTimer>? timers = null;

        foreach (EntityState timer in entities.OfClass(RoundTimerClass))
        {
            (timers ??= []).Add(new SceneRoundTimer(timer.EntityIndex)
            {
                Paused = timer.Integer("DT_TeamRoundTimer.m_bTimerPaused") is > 0,
                TimeRemaining = timer.Number("DT_TeamRoundTimer.m_flTimeRemaining") ?? 0f,
                EndTime = timer.Number("DT_TeamRoundTimer.m_flTimerEndTime") ?? 0f,
                MaxLength = timer.Integer("DT_TeamRoundTimer.m_nTimerMaxLength") ?? 0,
                Disabled = timer.Integer("DT_TeamRoundTimer.m_bIsDisabled") is > 0,
                ShowInHud = timer.Integer("DT_TeamRoundTimer.m_bShowInHUD") is > 0,
                Length = timer.Integer("DT_TeamRoundTimer.m_nTimerLength") ?? 0,
                SetupLength = timer.Integer("DT_TeamRoundTimer.m_nSetupTimeLength") ?? 0,

                // The constructor's `RT_STATE_NORMAL` (teamplay_round_timer.cpp:227) until the state is sent.
                State = timer.Integer("DT_TeamRoundTimer.m_nState") ?? SceneRoundTimer.StateNormal,
                ShowTimeRemaining = timer.Integer("DT_TeamRoundTimer.m_bShowTimeRemaining") is > 0,
                CaptureWatch = timer.Integer("DT_TeamRoundTimer.m_bInCaptureWatchState") is > 0,
                StopWatch = timer.Integer("DT_TeamRoundTimer.m_bStopWatchTimer") is > 0,
                TotalTime = timer.Number("DT_TeamRoundTimer.m_flTotalTime") ?? 0f,
            });
        }

        return timers;
    }

    /// <summary>`CObjectSentrygun` (tf_obj_sentrygun.cpp:131).</summary>
    private const string SentrygunClass = "CObjectSentrygun";

    /// <summary>`CObjectDispenser` (tf_obj_dispenser.cpp:86).</summary>
    private const string DispenserClass = "CObjectDispenser";

    /// <summary>`CObjectTeleporter` (tf_obj_teleporter.cpp:85).</summary>
    private const string TeleporterClass = "CObjectTeleporter";

    /// <summary>
    /// Every Engineer building `CTargetID::UpdateID`'s object branch reads (tf_hud_target_id.cpp:719);
    /// null when there is none, which costs no allocation.
    /// </summary>
    private static List<SceneBuilding>? Buildings(EntityStateTable entities)
    {
        List<SceneBuilding>? buildings = null;

        foreach (EntityState building in entities.OfClass(SentrygunClass)
            .Concat(entities.OfClass(DispenserClass))
            .Concat(entities.OfClass(TeleporterClass)))
        {
            (buildings ??= []).Add(new SceneBuilding(building.EntityIndex)
            {
                Health = building.Integer("DT_BaseObject.m_iHealth") ?? 0,
                MaxHealth = building.Integer("DT_BaseObject.m_iMaxHealth") ?? 0,
                ObjectType = building.Integer("DT_BaseObject.m_iObjectType") ?? 0,
                ObjectMode = building.Integer("DT_BaseObject.m_iObjectMode") ?? 0,
                Team = building.Integer("DT_BaseEntity.m_iTeamNum"),
                BuilderEntityIndex = EntityState.Slot(building.Integer("DT_BaseObject.m_hBuilder")),
                Sapped = building.Integer("DT_BaseObject.m_bHasSapper") is > 0,
                Disabled = building.Integer("DT_BaseObject.m_bDisabled") is > 0,
                Building = building.Integer("DT_BaseObject.m_bBuilding") is > 0,
                Placing = building.Integer("DT_BaseObject.m_bPlacing") is > 0,
                Carried = building.Integer("DT_BaseObject.m_bCarried") is > 0,
                MiniBuilding = building.Integer("DT_BaseObject.m_bMiniBuilding") is > 0,
                DisposableBuilding = building.Integer("DT_BaseObject.m_bDisposableBuilding") is > 0,
                UpgradeLevel = building.Integer("DT_BaseObject.m_iUpgradeLevel") ?? 0,
                HighestUpgradeLevel = building.Integer("DT_BaseObject.m_iHighestUpgradeLevel") ?? 0,
                PlasmaDisabled = building.Integer("DT_BaseObject.m_bPlasmaDisable") is > 0,
                UpgradeMetal = building.Integer("DT_BaseObject.m_iUpgradeMetal") ?? 0,
                UpgradeMetalRequired = building.Integer("DT_BaseObject.m_iUpgradeMetalRequired") ?? 0,
                PercentageConstructed = building.Number("DT_BaseObject.m_flPercentageConstructed") ?? 0f,
                SentryState = building.Integer("DT_ObjectSentrygun.m_iState"),
                SentryAmmoShells = building.Integer("DT_ObjectSentrygun.m_iAmmoShells"),
                SentryAmmoRockets = building.Integer("DT_ObjectSentrygun.m_iAmmoRockets"),
                DispenserAmmoMetal = building.Integer("DT_ObjectDispenser.m_iAmmoMetal"),
                TeleporterState = building.Integer("DT_ObjectTeleporter.m_iState"),
                TeleporterRechargeTime = building.Number("DT_ObjectTeleporter.m_flRechargeTime"),
                TeleporterRechargeDuration = building.Number("DT_ObjectTeleporter.m_flCurrentRechargeDuration"),
                Mins = building.Vector("DT_CollisionProperty.m_vecMins"),
                Maxs = building.Vector("DT_CollisionProperty.m_vecMaxs"),
                SolidType = building.Integer("DT_CollisionProperty.m_nSolidType") ?? 0,
                SolidFlags = building.Integer("DT_CollisionProperty.m_usSolidFlags") ?? 0,
                Position = building.Origin(),
            });
        }

        return buildings;
    }

    /// <summary>
    /// Every dropped weapon and revive marker — the entities whose `IsVisibleToTargetID` can answer true
    /// (tf_dropped_weapon.cpp:473, tf_revive.h:58); null when there is none, which costs no allocation.
    /// </summary>
    private static List<SceneIdEntity>? IdEntities(EntityStateTable entities, Func<int, string?> modelPath)
    {
        List<SceneIdEntity>? found = null;

        foreach (EntityState weapon in entities.OfClass("CTFDroppedWeapon"))
        {
            (found ??= []).Add(IdEntity(weapon, SceneIdEntityKind.DroppedWeapon) with
            {
                // `SetModel` (tf_dropped_weapon.cpp:98): `VPhysicsInitNormal` (:113) collides as this model's .phy.
                Model = weapon.ModelIndex() is { } index ? modelPath(index) : null,
                ItemValid = weapon.Integer("DT_ScriptCreatedItem.m_bInitialized") is > 0,
                ItemDefinition = weapon.ItemDefinitionIndex(),
                ItemQuality = weapon.Integer("DT_ScriptCreatedItem.m_iEntityQuality") ?? 0,
                AccountId = unchecked((uint)(weapon.Integer("DT_ScriptCreatedItem.m_iAccountID") ?? 0)),
                ChargeLevel = weapon.Number("DT_TFDroppedWeapon.m_flChargeLevel") ?? 0f,
            });
        }

        foreach (EntityState marker in entities.OfClass("CTFReviveMarker"))
        {
            (found ??= []).Add(IdEntity(marker, SceneIdEntityKind.ReviveMarker) with
            {
                Health = marker.Integer("DT_TFReviveMarker.m_iHealth") ?? 0,
                MaxHealth = marker.Integer("DT_TFReviveMarker.m_iMaxHealth") ?? 0,
                OwnerEntityIndex = EntityState.Slot(marker.Integer("DT_TFReviveMarker.m_hOwner")),
            });
        }

        return found;
    }

    /// <summary>The recorder's <c>DT_Local</c> movement state (<see cref="SceneLocalMovement"/>); null when it is not sent.</summary>
    private static SceneLocalMovement? LocalMovement(EntityState player)
    {
        if (player.Number("DT_Local.m_flDucktime") is not { } duckTime)
        {
            return null;
        }

        return new SceneLocalMovement
        {
            Ducked = player.Integer("DT_Local.m_bDucked") is > 0,
            Ducking = player.Integer("DT_Local.m_bDucking") is > 0,
            InDuckJump = player.Integer("DT_Local.m_bInDuckJump") is > 0,
            DuckTime = duckTime,
            DuckJumpTime = player.Number("DT_Local.m_flDuckJumpTime") ?? 0f,
            JumpTime = player.Number("DT_Local.m_flJumpTime") ?? 0f,
            FallVelocity = player.Number("DT_Local.m_flFallVelocity") ?? 0f,
            AllowAutoMovement = player.Integer("DT_Local.m_bAllowAutoMovement") is not 0,
            BaseVelocity = player.Vector("DT_LocalPlayerExclusive.m_vecBaseVelocity") ?? default,
            TickBase = player.Integer("DT_LocalPlayerExclusive.m_nTickBase"),
            DuckTimer = player.Number("DT_TFPlayerShared.m_flDuckTimer") ?? 0f,
            AirDucked = player.Integer("DT_TFPlayerShared.m_nAirDucked") ?? 0,
            AirDash = player.Integer(AirDashProperty) ?? player.Integer(LaunchAirDashProperty) ?? 0,
        };
    }

    /// <summary>An entity's networked solidity, or null when its <c>m_Collision</c> states no solid type.</summary>
    private static SceneCollision? Collision(EntityState entity) =>
        entity.Integer("DT_CollisionProperty.m_nSolidType") is { } solidType
            ? new SceneCollision(
                solidType,
                entity.Integer("DT_CollisionProperty.m_usSolidFlags") ?? 0,
                entity.Integer("DT_BaseEntity.m_CollisionGroup") ?? 0)
            {
                Mins = entity.Vector("DT_CollisionProperty.m_vecMins") ?? default,
                Maxs = entity.Vector("DT_CollisionProperty.m_vecMaxs") ?? default,
            }
            : null;

    /// <summary>What every <see cref="SceneIdEntity"/> shares: team, placement and collision.</summary>
    private static SceneIdEntity IdEntity(EntityState entity, SceneIdEntityKind kind) => new(entity.EntityIndex, kind)
    {
        Team = entity.Integer("DT_BaseEntity.m_iTeamNum"),
        Position = entity.Origin(),
        Angles = entity.Angles(),
        Mins = entity.Vector("DT_CollisionProperty.m_vecMins"),
        Maxs = entity.Vector("DT_CollisionProperty.m_vecMaxs"),
        SolidType = entity.Integer("DT_CollisionProperty.m_nSolidType") ?? 0,
        SolidFlags = entity.Integer("DT_CollisionProperty.m_usSolidFlags") ?? 0,
    };

    /// <summary>Every team entity, as `CTFHudMatchStatus` reads scores; null when there is none, which costs no allocation.</summary>
    private static List<SceneTeam>? Teams(EntityStateTable entities)
    {
        List<SceneTeam>? teams = null;

        foreach (EntityState team in entities.OfClass(TeamClass))
        {
            (teams ??= []).Add(new SceneTeam(team.Integer("DT_Team.m_iTeamNum") ?? 0)
            {
                Score = team.Integer("DT_Team.m_iScore") ?? 0,
                RoundsWon = team.Integer("DT_Team.m_iRoundsWon") ?? 0,
                Name = team.Text("DT_Team.m_szTeamname") ?? string.Empty,
                Players = team.TeamPlayers(),
            });
        }

        return teams;
    }

    /// <summary>`MAX_PLAYERS` on `TF_DLL` (`shareddefs.h:254`): how many resource slots to walk.</summary>
    private const int MaxPlayerSlots = 101;

    /// <summary>
    /// Every player slot `CTFClientScoreBoardDialog::UpdatePlayerList` would list — connected or
    /// valid, whether or not the slot has a positioned entity this tick (`tf_clientscoreboard.cpp:1284-1286`);
    /// null when there is no resource entity, which costs no allocation.
    /// </summary>
    private static List<SceneScoreboardPlayer>? ScoreboardPlayers(EntityStateTable entities)
    {
        if (entities.OfClass(ResourceClass).FirstOrDefault() is not { } resource)
        {
            return null;
        }

        List<SceneScoreboardPlayer>? players = null;

        for (int playerIndex = 1; playerIndex <= MaxPlayerSlots; playerIndex++)
        {
            string slot = playerIndex.ToString("D3", CultureInfo.InvariantCulture);
            bool connected = resource.Integer($"m_bConnected.{slot}") is > 0;
            bool valid = resource.Integer($"m_bValid.{slot}") is > 0;

            if (!connected && !valid)
            {
                continue;
            }

            (players ??= []).Add(new SceneScoreboardPlayer(playerIndex)
            {
                Connected = connected,
                Valid = valid,
                Team = resource.Integer($"m_iTeam.{slot}"),
                Alive = resource.Integer($"m_bAlive.{slot}") is > 0,
                Score = resource.Integer($"m_iScore.{slot}") ?? 0,
                TotalScore = resource.Integer($"m_iTotalScore.{slot}") ?? 0,
                Deaths = resource.Integer($"m_iDeaths.{slot}") ?? 0,
                Ping = resource.Integer($"m_iPing.{slot}") ?? 0,
                PlayerClass = resource.Integer($"m_iPlayerClass.{slot}"),
                ActiveDominations = resource.Integer($"m_iActiveDominations.{slot}") ?? 0,
                Health = resource.Integer($"m_iHealth.{slot}") ?? 0,
                MaxHealth = resource.Integer($"m_iMaxHealth.{slot}") ?? 0,
                NextRespawnTime = resource.Number($"m_flNextRespawnTime.{slot}") ?? 0f,
                ChargeLevel = resource.Integer($"m_iChargeLevel.{slot}") ?? 0,
            });
        }

        return players;
    }
    }

    private static readonly string[] TeamProperties =
    [
        "DT_BaseEntity.m_iTeamNum",
        "DT_BaseCombatCharacter.m_iTeamNum",
    ];

    /// <summary>An item entity's wire attribute inputs.</summary>
    /// <remarks>
    /// `INVALID_ITEM_ID` is `(itemid_t)-1` (`econ_item_constants.h:443`): both networked halves all-ones. Never-sent reads
    /// as invalid, which routes an era demo — no econ system at all — to the definition's own attributes at resolve time.
    /// </remarks>
    private static EconAttributeWire EconWire(EntityState state)
    {
        long? high = state.Integer("DT_ScriptCreatedItem.m_iItemIDHigh");
        long? low = state.Integer("DT_ScriptCreatedItem.m_iItemIDLow");

        return new EconAttributeWire(
            state.EconAttributes(EconAttributeList.Local),
            state.EconAttributes(EconAttributeList.NetworkedForDemos),
            high is { } h && low is { } l && !(h == uint.MaxValue && l == uint.MaxValue));
    }

    private sealed partial class Builder
    {
    /// <summary>`MedicGetChargeLevel`'s medigun (tf_player_shared.cpp:13038): the first `CWeaponMedigun` in `m_hMyWeapons`.</summary>
    /// <remarks>
    /// The charge is sent twice — `DT_LocalTFWeaponMedigunData` to the owner, the 12-bit `DT_TFWeaponMedigunDataNonLocal` to
    /// everyone else (tf_weapon_medigun.cpp:124, :135) — into one member; the non-local table is declared second, so where
    /// both arrive it is the value left.
    /// </remarks>
    private static (float Charge, int Quality, int? Definition)? MedigunOf(EntityState player, EntityStateTable entities)
    {
        foreach (int slot in player.MyWeapons())
        {
            if (entities.TryGet(slot, out EntityState? weapon) && weapon.ClassName == "CWeaponMedigun")
            {
                float charge = weapon.Number("DT_TFWeaponMedigunDataNonLocal.m_flChargeLevel")
                    ?? weapon.Number("DT_LocalTFWeaponMedigunData.m_flChargeLevel")
                    ?? 0f;

                return (charge, weapon.Integer("DT_ScriptCreatedItem.m_iEntityQuality") ?? 0, weapon.ItemDefinitionIndex());
            }
        }

        return null;
    }

    /// <summary>The items a player carries and wears — the attribute providers `ProvideTo` registers — or null for none.</summary>
    /// <remarks>Weapons first, then wearables; `m_hMyWeapons` is local-only, so only the recorder's arrive.</remarks>
    private static List<SceneItem>? CarriedItems(EntityState player, EntityStateTable entities)
    {
        IReadOnlyList<int> weapons = player.MyWeapons();

        if (weapons.Count == 0)
        {
            return null;
        }

        List<SceneItem> items = [];

        foreach ((IReadOnlyList<int> slots, bool isWeapon) in new[] { (weapons, true), (player.MyWearables(), false) })
        {
            foreach (int slot in slots)
            {
                if (entities.TryGet(slot, out EntityState? item))
                {
                    items.Add(ItemOf(slot, item, isWeapon));
                }
            }
        }

        return items;
    }

    /// <summary>The list this player had last frame when every element equals this frame's, else this frame's.</summary>
    /// <remarks>
    /// **Every frame's player keeps its lists for the life of the demo, and they almost never change** (B433): on z1800 the
    /// per-frame item copies were 2.19 million `SceneItem`s, 2.21 million wires and 315 thousand lists — ~340 MB of the
    /// timeline's 1.1 GB — and the gesture slots another 315 thousand. Both element types are immutable records with
    /// structural equality (an item's wire compares by value), so handing one instance to two frames is indistinguishable
    /// to every reader.
    /// </remarks>
    private static List<T>? SameAsBefore<T>(Dictionary<int, List<T>> before, int player, List<T>? now)
    {
        if (now is null)
        {
            return null;
        }

        if (before.TryGetValue(player, out List<T>? last) && last.SequenceEqual(now))
        {
            return last;
        }

        now.TrimExcess();
        before[player] = now;
        return now;
    }

    /// <summary>The player's previous detail when every value is unchanged, so frames share one copy (D217); otherwise this one.</summary>
    /// <param name="before">Each player's last stored detail, updated here.</param>
    /// <param name="player">The player's entity index.</param>
    /// <param name="now">This frame's detail.</param>
    /// <returns>The instance to store.</returns>
    private static ScenePlayerDetail SharedDetail(Dictionary<int, ScenePlayerDetail> before, int player, ScenePlayerDetail now)
    {
        if (before.TryGetValue(player, out ScenePlayerDetail? last) && now.SameValues(last))
        {
            return last;
        }

        before[player] = now;
        return now;
    }
    }

    /// <summary>One item entity as a <see cref="SceneItem"/>, with its quality and `m_bDisguiseWearable` (tf_item_wearable.cpp:33).</summary>
    private static SceneItem ItemOf(int slot, EntityState item, bool isWeapon) =>
        new(slot, item.ClassName, item.ItemDefinitionIndex(), EconWire(item), isWeapon)
        {
            Quality = item.Integer("DT_ScriptCreatedItem.m_iEntityQuality"),
            IsDisguiseWearable = item.Integer("DT_TFWearable.m_bDisguiseWearable") is > 0,
            ChargeBeginTime = item.Number("DT_PipebombLauncherLocalData.m_flChargeBeginTime")
                ?? item.Number("DT_ParticleCannon.m_flChargeBeginTime") ?? 0f,
            PrimaryAmmoType = item.Integer("DT_LocalWeaponData.m_iPrimaryAmmoType"),
            EffectBarRegenTime = item.Number("DT_LocalTFWeaponData.m_flEffectBarRegenTime") ?? 0f,
            LastFireTime = item.Number("DT_LocalTFWeaponData.m_flLastFireTime") ?? 0f,            Energy = item.Number("DT_TFWeaponBase.m_flEnergy") ?? 0f,
            KillComboClass = item.Integer("DT_TFWeaponBase.m_nKillComboClass") ?? 0,
            KillComboCount = item.Integer("DT_TFWeaponBase.m_nKillComboCount") ?? 0,
            KnifeExists = item.Integer("DT_TFWeaponKnife.m_bKnifeExists") is > 0,
            KnifeMeltTimestamp = item.Number("DT_TFWeaponKnife.m_flKnifeMeltTimestamp") ?? 0f,
            KnifeRegenerateDuration = item.Number("DT_TFWeaponKnife.m_flKnifeRegenerateDuration") ?? 0f,
            MinicritCharge = item.Number("DT_WeaponChargedSMG.m_flMinicritCharge") ?? 0f,
            RocketPackEnabled = item.Integer("DT_TFWeaponRocketPack.m_bEnabled") is > 0,
            NumCharges = item.Integer("DT_TFPowerupBottle.m_usNumCharges") ?? 0,
            FlameThrowerState = item.Integer("DT_WeaponFlameThrower.m_iWeaponState") ?? 0,
            ChargeLevel = item.Number("DT_LocalTFWeaponMedigunData.m_flChargeLevel")
                ?? item.Number("DT_TFWeaponMedigunDataNonLocal.m_flChargeLevel") ?? 0f,
            ChargeRelease = item.Integer("DT_WeaponMedigun.m_bChargeRelease") is > 0,
            ChargeResistType = item.Integer("DT_WeaponMedigun.m_nChargeResistType") ?? 0,
        };

    private sealed partial class Builder
    {
    /// <summary>`m_Shared.GetDisguiseWeapon()`'s item (tf_hud_playerstatus.cpp:457), or null.</summary>
    private static SceneItem? DisguiseWeaponItem(EntityState player, EntityStateTable entities) =>
        EntityState.Slot(player.Integer("DT_TFPlayerShared.m_hDisguiseWeapon")) is { } slot && entities.TryGet(slot, out EntityState? weapon)
            ? ItemOf(slot, weapon, isWeapon: true)
            : null;

    /// <summary>`m_flItemChargeMeter`, one per slot through `LAST_LOADOUT_SLOT_WITH_CHARGE_METER` (tf_player_shared.h:1124); null unsent.</summary>
    private static float[]? ItemChargeMeters(EntityState player)
    {
        // `LOADOUT_POSITION_MISC2` is 10 (tf_item_constants.h:66, :85).
        float[]? meters = null;

        for (int slot = 0; slot <= 10; slot++)
        {
            if (player.Number($"m_flItemChargeMeter.{slot:D3}") is { } meter)
            {
                meters ??= new float[11];
                meters[slot] = meter;
            }
        }

        return meters;
    }

    /// <summary>`m_iAmmo`, sent to its owner alone (`DT_BCCLocalPlayerExclusive`); null when this player's never arrived.</summary>
    private static int[]? AmmoCounts(EntityState player)
    {
        // `MAX_AMMO_SLOTS` is 32 (shareddefs.h); TF uses the first seven, `TF_AMMO_COUNT`.
        int[]? counts = null;

        for (int slot = 0; slot < 32; slot++)
        {
            if (player.Integer($"m_iAmmo.{slot:D3}") is { } count)
            {
                counts ??= new int[32];
                counts[slot] = count;
            }
        }

        return counts;
    }
    }

    private static readonly string[] HealthProperties =
    [
        "DT_BasePlayer.m_iHealth",
        "DT_TFPlayerScoringDataExclusive.m_iHealth",
    ];

    private readonly TimelineFrames _frames;

    private readonly List<ScenePropTrack> _props;

    private readonly Dictionary<int, ScenePropTrack> _trackByEntity = [];

    private readonly List<ScenePropTrack> _playerTracks;

    /// <summary>Every command <c>CDemoPlayer</c>'s view half reads, in stream order (B56).</summary>
    /// <remarks>
    /// Each signon and packet with its <c>democmdinfo_t</c>, and every <c>dem_synctick</c> and <c>dem_stop</c>: the
    /// two commands that end <c>ParseAheadForInterval</c>. The other four it skips without a tick test
    /// (<c>0x180072e60</c>'s jump table), so they are not kept. Zeroed views included: the engine's current view is
    /// the last packet READ, whatever it holds.
    /// </remarks>
    private readonly List<DemoViewCommand> _viewCommands;

    /// <summary>Which of <see cref="_viewCommands"/> carry a camera — a non-zero origin — in order.</summary>
    /// <remarks>
    /// A sorted list rather than a dictionary because the question asked is "what was the camera at
    /// or before this tick" — the viewer draws between packets, so an exact-match lookup answers
    /// nothing on most frames. Binary search over the ticks is what makes that cheap enough to do
    /// per frame.
    /// </remarks>
    private readonly List<int> _recordedViews = [];

    /// <summary>Every <c>hltv_chase</c> the director sent, in tick order.</summary>
    private readonly List<(int Tick, DirectorShot Shot)> _director = [];

    /// <summary>Whether the recording carries a director at all.</summary>
    /// <remarks>
    /// **False for a point-of-view demo, and that is the normal case rather than a gap.** Only a
    /// SourceTV recording has a director choosing shots; a POV demo carries the player's own camera
    /// and never sends <c>hltv_chase</c>.
    /// </remarks>
    public bool HasDirector => _director.Count > 0;

    /// <summary>What the director last asked for at or before a tick.</summary>
    /// <param name="tick">The tick being drawn.</param>
    /// <returns>The shot, or null when the recording has no director.</returns>
    /// <remarks>
    /// **The LAST shot at or before the tick, because a shot persists until the next one.** The
    /// director sends an event when it changes its mind, not every tick, so anything that sampled
    /// only the current tick would find a shot on one frame in a hundred and nothing on the rest.
    /// </remarks>
    public DirectorShot? DirectorAt(int tick)
    {
        if (_director.Count == 0 || tick < _director[0].Tick)
        {
            return null;
        }

        int low = 0;
        int high = _director.Count - 1;

        while (low < high)
        {
            int middle = (low + high + 1) / 2;

            if (_director[middle].Tick <= tick)
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }

        return _director[low].Shot;
    }

    /// <summary>Each tick a viewmodel was described, and what it said.</summary>
    /// <remarks>
    /// Sampled per tick rather than stored per frame, for the same reason the recorded views are:
    /// the viewer draws between packets and wants the most recent answer, not an exact match.
    /// </remarks>
    private readonly List<(int Tick, SceneViewmodel Weapon)> _viewmodels = [];

    /// <summary>Every change to the atmosphere, in tick order.</summary>
    /// <remarks>
    /// Recorded on change rather than per tick: a fog controller sends its state on entry and then
    /// rarely, so a per-tick list would be tens of thousands of identical entries for a handful of
    /// distinct values.
    ///
    /// **The VIEW's fog, world and sky together, null included** (B139): a player whose handle moves
    /// to no controller loses fog, and a change-only list of non-null values could not say so.
    /// </remarks>
    private readonly List<(int Tick, ViewFog Fog)> _fog = [];

    /// <summary>The world fog samples that are not "none", for <see cref="FogSamples"/>.</summary>
    private readonly List<(int Tick, SceneFog Fog)> _worldFogSamples = [];

    private readonly List<SceneSound> _sounds = [];

    private readonly List<(int Tick, SceneSoundscape Soundscape)> _soundscapes = [];

    /// <summary>Whether any viewmodel in this demo names an owner.</summary>
    /// <remarks>
    /// **This is what separates a point-of-view recording from a SourceTV one**, and it is a
    /// property of the whole demo rather than of one entity. A client receives only its own
    /// viewmodel and the server never says whose it is, so a POV demo names nobody; a SourceTV
    /// recording carries one per player and names each. Asked once here rather than per lookup so
    /// the answer cannot vary with the tick being drawn.
    /// </remarks>
    private readonly bool _viewmodelsNameOwners;

    private DemoTimeline(
        List<TimelineFrame> frames,
        List<ScenePropTrack>? props = null,
        List<ScenePropTrack>? playerTracks = null,
        List<DemoViewCommand>? viewCommands = null,
        List<(int Tick, SceneViewmodel Weapon)>? viewmodels = null,
        List<(int Tick, ViewFog Fog)>? fog = null,
        List<SceneSound>? sounds = null,
        List<(int Tick, SceneSoundscape Soundscape)>? soundscapes = null,
        List<(int Tick, DirectorShot Shot)>? director = null,
        FirstStatedRoster? firstStated = null)
    {
        _director = director ?? [];
        _soundscapes = soundscapes ?? [];
        _viewCommands = viewCommands ?? [];

        // A zeroed origin is not a camera at the world origin but the absence of one: SourceTV leaves every
        // democmdinfo_t blank, and the signon packets carry nothing yet.
        for (int index = 0; index < _viewCommands.Count; index++)
        {
            if (_viewCommands[index].IsPacket && _viewCommands[index].View.Origin != (0f, 0f, 0f))
            {
                _recordedViews.Add(index);
            }
        }

        _viewmodels = viewmodels ?? [];
        _fog = fog ?? [];

        foreach ((int tick, ViewFog view) in _fog)
        {
            if (view.World is { } world)
            {
                _worldFogSamples.Add((tick, world));
            }
        }

        _sounds = sounds ?? [];

        _viewmodelsNameOwners =
            _viewmodels.Exists(recorded => recorded.Weapon.OwnerEntityIndex is not null);

        _frames = new TimelineFrames(frames, firstStated ?? new FirstStatedRoster());
        _props = props ?? [];
        _playerTracks = playerTracks ?? [];

        // Last track wins where a slot was reused: the later occupant is the one still alive at
        // any tick a caller can ask about after it started, and At answers null before that.
        foreach (ScenePropTrack track in _props)
        {
            _trackByEntity[track.EntityIndex] = track;
        }

        foreach (ScenePropTrack track in _playerTracks)
        {
            _trackByEntity[track.EntityIndex] = track;
        }

        // **Every occupant of each slot, built here and never lazily** (B548): `TrackFor(entity, tick)` and the
        // move-child walk ask it per child per sample, and a cache written on first read is a write a concurrent
        // reader can race. Props then players, the order `TrackFor` has always searched.
        foreach (ScenePropTrack track in _props.Concat(_playerTracks))
        {
            if (!_tracksBySlot.TryGetValue(track.EntityIndex, out List<ScenePropTrack>? occupants))
            {
                _tracksBySlot[track.EntityIndex] = occupants = [];
            }

            occupants.Add(track);
        }
    }

    /// <summary>Every track by slot, in the order <see cref="TrackFor(int, double)"/> searches them.</summary>
    private readonly Dictionary<int, List<ScenePropTrack>> _tracksBySlot = [];

    /// <summary>The first tick after this one at which any occupant of a slot is born, or infinity (B548).</summary>
    private double NextBirthInSlot(int entityIndex, double tick)
    {
        double next = double.PositiveInfinity;

        if (_tracksBySlot.TryGetValue(entityIndex, out List<ScenePropTrack>? occupants))
        {
            foreach (ScenePropTrack occupant in occupants)
            {
                if (occupant.FirstTick > tick && occupant.FirstTick < next)
                {
                    next = occupant.FirstTick;
                }
            }
        }

        return next;
    }

    /// <summary>Every model-bearing entity the demo carried, with its pose over time.</summary>
    /// <summary>How far behind the packet each entity's simulation tick was, bucketed.</summary>
    private int[] _simulationLag = new int[LagBuckets];

    /// <summary>Buckets in <see cref="SimulationLag(int)"/>, covering −8 to +8 ticks.</summary>
    public const int LagBuckets = 17;

    /// <summary>Where a lag of zero sits in the histogram.</summary>
    public const int LagZero = 8;

    /// <summary>The slot counting updates that carried no simulation time, past the buckets.</summary>
    private const int LagUnknownBucket = LagBuckets;

    /// <summary>How many entity updates lagged the packet by a given number of ticks.</summary>
    /// <param name="bucket">0 to <see cref="LagBuckets"/> − 1; <see cref="LagZero"/> is no lag.</param>
    /// <returns>The count, with the end buckets holding everything beyond ±8.</returns>
    /// <remarks>
    /// **The engine timestamps an interpolation history entry with the entity's SIMULATION time,
    /// not with the packet's tick** — <c>C_BaseEntity::GetLastChangeTime</c> returns
    /// <c>GetSimulationTime()</c> for simulation-latched variables, which is origin and angles
    /// among others, and <c>OnLatchInterpolatedVariables</c> hands that to every watcher
    /// (<c>c_baseentity.cpp:2806</c>). This project stamps every keyframe with the packet tick, so
    /// this histogram is the size of that divergence, measured rather than argued (B273).
    ///
    /// **Measured 2026-09-02.** On the 2013 SourceTV foundry demo, 32% of updates carry a
    /// simulation tick equal to the packet's, 31% are four ticks ahead of it, and 35% lag by eight
    /// or more — the last group being entities that do not simulate every tick, whose stale
    /// timestamp costs nothing because they are not moving. On the 2011 viaduct point-of-view
    /// recording the four-tick cluster is 81%.
    ///
    /// **Kept because the follow-up needs it.** Changing what a keyframe is stamped with alters
    /// what is on screen, so it is the owner's call rather than a silent correction — and this is
    /// the number that call should be made on.
    /// </remarks>
    public int SimulationLag(int bucket) => _simulationLag[bucket];

    /// <summary>Entity updates whose entity never sent a simulation time.</summary>
    /// <remarks>
    /// **The control for the histogram.** A count of zero here says every update carried the value,
    /// so the distribution above describes the demo rather than describing which entities happened
    /// to answer. Measured as zero on both demos it was first run against — which is what makes the
    /// clusters in it worth reading.
    /// </remarks>
    public int SimulationLagUnknown { get; private set; }

    /// <summary>The same, for the animation clock.</summary>
    private int[] _animationLag = new int[LagBuckets];

    /// <summary>How many entity updates' animation stamp lagged the packet by a given amount.</summary>
    /// <param name="bucket">0 to <see cref="LagBuckets"/> − 1; <see cref="LagZero"/> is no lag.</param>
    /// <returns>The count, with the end buckets holding everything beyond ±8.</returns>
    /// <remarks>
    /// **The engine's OTHER latch clock**, and it is a separate measurement because the two answer
    /// separately: <c>GetLastChangeTime</c> returns <c>GetAnimTime()</c> for
    /// <c>LATCH_ANIMATION_VAR</c> — pose parameters, bone controllers, flexes, overlay layers —
    /// where the simulation clock serves origin and angles.
    /// </remarks>
    public int AnimationLag(int bucket) => _animationLag[bucket];

    /// <summary>Entity updates whose entity never sent an animation time.</summary>
    /// <remarks>
    /// **Expected to be LARGE where the simulation equivalent is zero**, and that is not a fault:
    /// a resting prop simulates without animating, and a player using client-side animation sends
    /// none at all — <c>SendProxy_AnimTime</c> asserts <c>!IsUsingClientSideAnimation()</c>.
    /// </remarks>
    public int AnimationLagUnknown { get; private set; }

    /// <summary>The gap between the two clocks, where an update carried both.</summary>
    private int[] _clockGap = new int[LagBuckets];

    /// <summary>Simulation tick minus animation tick, for updates that sent both.</summary>
    /// <param name="bucket">0 to <see cref="LagBuckets"/> − 1; <see cref="LagZero"/> is agreement.</param>
    /// <returns>The count, with the end buckets holding everything beyond ±8.</returns>
    /// <remarks>
    /// **The measurement that decides whether one keyframe can serve both clocks.** The engine
    /// keeps a separate interpolation history per variable and stamps each with its own
    /// <c>GetLastChangeTime</c>; this project keeps ONE keyframe per entity per packet, carrying
    /// simulation-latched fields and animation-latched fields together. That is faithful exactly to
    /// the extent that the two clocks agree when both are sent — so the answer is measured rather
    /// than assumed, and it is here for anyone who later wonders whether the single history was a
    /// shortcut.
    /// </remarks>
    public int ClockGap(int bucket) => _clockGap[bucket];

    /// <summary>The simulation-lag histogram split by server class.</summary>
    /// <remarks>
    /// **What tells a clock offset from a real disagreement.** If every class sits in the same
    /// bucket the difference between the packet tick and the simulation tick is a constant, which
    /// moves the whole scene together and is invisible; if classes sit in DIFFERENT buckets the
    /// entities disagree with each other by that many ticks, which is what a viewer sees.
    /// </remarks>
    public IReadOnlyDictionary<string, int[]> SimulationLagByClass { get; private set; } =
        new Dictionary<string, int[]>();


    public IReadOnlyList<ScenePropTrack> Props => _props;

    /// <summary>Every sound the recording plays, in tick order.</summary>
    /// <remarks>
    /// **A flat list rather than tracks, because a sound is an instant and not a state.** There is
    /// no "what is entity 12 sounding like at tick 4000" to answer — it made a noise at a tick and
    /// the player owns it from there. Kept in tick order so playback walks a cursor forward and a
    /// seek is a binary search, rather than a scan per frame.
    /// </remarks>
    public IReadOnlyList<SceneSound> Sounds => _sounds;

    /// <summary>Every player, with the pose the interpolator works from.</summary>
    /// <remarks>
    /// **Separate from <see cref="Props"/> because these carry no model.** A player's model is
    /// resolved from the installed game rather than from the demo — see
    /// <c>PlayerClassModels</c> — so a consumer walking <see cref="Props"/> to draw models would
    /// find entries it could only report as missing assets.
    /// </remarks>
    public IReadOnlyList<ScenePropTrack> PlayerTracks => _playerTracks;

    /// <summary>Every distinct model this recording will ever ask for.</summary>
    /// <returns>Model paths, without duplicates and in no particular order.</returns>
    /// <remarks>
    /// **For precaching, which is when the engine loads models too.**
    /// <c>CBaseEntity::PrecacheModel</c> sits behind <c>IsPrecacheAllowed()</c> and warns on an
    /// out-of-order precache, because Source loads models at level load rather than on sight.
    /// Packing on sight cost 385 ms in one frame when a crowd of props came into view (B163, D86).
    ///
    /// **Answered here rather than assembled by the caller**, because the three collections it has
    /// to reach into are not all public and one of them never was: <see cref="Props"/> and
    /// <see cref="PlayerTracks"/> are, and the viewmodels are private. A caller that knew about two
    /// of the three would precache most models and leave weapon switches to hitch — which is
    /// precisely the case a first-person viewer meets most often.
    ///
    /// **Player tracks are included even though they carry no model of their own.** Their path is
    /// empty or a placeholder, and an empty entry costs a packer nothing to reject; the class models
    /// a player actually wears come from the installed game and are the caller's to add.
    /// </remarks>
    public IEnumerable<string> ModelPaths()
    {
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

        // Every model each entity named in its life, not only its last (B548).
        foreach (ScenePropTrack track in _props.Concat(_playerTracks))
        {
            foreach (string path in track.ModelPaths)
            {
                if (path.Length > 0 && seen.Add(path))
                {
                    yield return path;
                }
            }
        }

        // The weapons a first-person view puts in the player's hands. A weapon switch changes this
        // model, which is why leaving them out would leave a hitch every few seconds in exactly the
        // view the owner reported it from.
        foreach ((int _, SceneViewmodel weapon) in _viewmodels)
        {
            if (weapon.ModelPath is { Length: > 0 } && seen.Add(weapon.ModelPath))
            {
                yield return weapon.ModelPath;
            }
        }
    }

    /// <summary>Every distinct sound this recording will ever play.</summary>
    /// <returns>Sound names, without duplicates and in no particular order.</returns>
    /// <remarks>
    /// **The sibling of <see cref="ModelPaths"/>, and for the same reason** (D87, B163). Valve does
    /// not merely prefer to load audio at level load — <c>CBaseEntity::PrecacheSound</c> refuses to
    /// do it later: <c>SoundEmitterSystem.cpp:1497</c> is
    /// <c>if ( !CBaseEntity::IsPrecacheAllowed() )</c> followed by
    /// <c>Assert( !"CBaseEntity::PrecacheSound:  too late" )</c>. Decoding on first play is the same
    /// departure D86 caught for models, sitting behind the same guard.
    ///
    /// **Measured 2026-08-25, and it is what the owner had been hearing.** Of eleven slow frames in
    /// one run, six were dominated by the sound step at 27-91 ms while posing and drawing sat at
    /// 1.7-2.6 ms. Only ONE decode logged a stall, because the per-decode threshold is 30 ms and a
    /// frame that starts three sounds pays three decodes that each fall under it — which is why an
    /// instrument watching single decodes reported almost nothing while the frames were visibly
    /// freezing.
    ///
    /// **Names, not paths, because the name is what the schedule replays.** A name may be a script
    /// rather than a file — see <c>SoundScript</c> — and resolving it is the caller's job, exactly
    /// as it is when the sound is played.
    ///
    /// A stop carries a name but plays nothing, so decoding it would read a file to throw it away.
    /// </remarks>
    /// <remarks>
    /// Not <c>SoundNames</c>, which is the <c>soundprecache</c> string table READER
    /// (<c>Core/Net/SoundNames.cs</c>) and is used by name inside this very class.
    /// </remarks>
    public IEnumerable<string> SoundsToPrecache()
    {
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

        foreach (SceneSound sound in _sounds)
        {
            if (!sound.IsStop && sound.Name is { Length: > 0 } && seen.Add(sound.Name))
            {
                yield return sound.Name;
            }
        }
    }

    /// <summary>Every recorded moment, in tick order.</summary>
    public IReadOnlyList<TimelineFrame> Frames => _frames;

    /// <summary>Every recorded change to the atmosphere, in tick order.</summary>
    /// <remarks>
    /// Exposed alongside <see cref="FogAt"/> because the two answer different questions and a test
    /// that can only ask the second cannot tell "no fog was recorded" from "fog was recorded after
    /// the tick I asked about". That distinction cost a diagnosis here.
    /// </remarks>
    public IReadOnlyList<(int Tick, SceneFog Fog)> FogSamples => _worldFogSamples;

    /// <summary>Every soundscape change the recording carries, in tick order.</summary>
    /// <remarks>
    /// **Empty for a SourceTV recording, and that is the format rather than a gap.** `m_audio` is
    /// sent only to the player who owns the entity, so a SourceTV demo — which owns no player —
    /// carries nobody's. See <see cref="SceneSoundscape"/>.
    /// </remarks>
    public IReadOnlyList<(int Tick, SceneSoundscape Soundscape)> Soundscapes => _soundscapes;

    /// <summary>The soundscape in force at a tick, or <c>null</c> before the first sample.</summary>
    /// <param name="tick">The tick to ask about.</param>
    /// <returns>The soundscape, or <c>null</c>.</returns>
    /// <remarks>
    /// Walks forward keeping the last sample at or before the tick, the same way fog and viewmodels
    /// are read: a soundscape persists until the player enters another one, so the absence of a
    /// sample means "unchanged", never "none".
    /// </remarks>
    public SceneSoundscape? SoundscapeAt(int tick)
    {
        SceneSoundscape? found = null;

        foreach ((int at, SceneSoundscape soundscape) in _soundscapes)
        {
            if (at > tick)
            {
                break;
            }

            found = soundscape;
        }

        return found;
    }

    /// <summary>How many times a fog controller was seen in the entity table, across all packets.</summary>
    /// <remarks>
    /// **A diagnostic, and it exists because "no fog" has two causes that look identical.** Either
    /// the demo has no controller, or it has one and something between the entity table and
    /// <c>SceneFog</c> is dropping it. A count separates them in one number; without it the only
    /// way to tell was to decode the demo twice by different routes and compare.
    /// </remarks>
    public int FogControllersSeen { get; private init; }

    /// <summary>The most properties any fog controller carried, which B132 says is zero.</summary>
    public int FogControllerProperties { get; private init; }

    /// <summary>The atmosphere at a tick, or null when the demo records none.</summary>
    /// <param name="tick">The tick being drawn.</param>
    /// <returns>The fog in force, or null.</returns>
    /// <remarks>
    /// **Walks forward keeping the last sample at or before the tick**, which is how every other
    /// lookup here works and is what a networked value means: it holds until the server changes it.
    ///
    /// Null before the first sample as well as when there is none at all, because fog that begins
    /// partway through a demo did not exist before then. Drawing the eventual value from tick zero
    /// would be inventing atmosphere the recording does not have.
    /// </remarks>
    public SceneFog? FogAt(int tick) => ViewFogAt(tick).World;

    /// <summary>The 3D skybox's fog at a tick, <c>m_skybox3d.fog</c>, or null for none (B139).</summary>
    /// <param name="tick">The tick being drawn.</param>
    /// <returns>The sky fog in force, or null.</returns>
    public SceneFog? SkyFogAt(int tick) => ViewFogAt(tick).Sky;

    private ViewFog ViewFogAt(int tick)
    {
        ViewFog found = default;

        foreach ((int at, ViewFog fog) in _fog)
        {
            if (at > tick)
            {
                break;
            }

            found = fog;
        }

        return found;
    }

    /// <summary>The local player's entity, or <c>null</c> before <c>svc_ServerInfo</c>.</summary>
    /// <remarks>
    /// <c>svc_ServerInfo</c>'s player slot, plus one: entity indices are one-based and slot zero is
    /// the first player. Named by the demo rather than worked out — a first-person camera needs the
    /// recorder's class to know their eye height, and identifying them by "whichever player moves
    /// like the camera" would be an instrument that agrees with its own hypothesis.
    ///
    /// In a SourceTV recording this is the SourceTV client's own <c>CTFPlayer</c>, a spectator,
    /// which is the engine's local player there too (<c>docs/findings/58</c>). It has no view to
    /// follow: <see cref="HasRecordedView"/> is false, so the viewer spectates a chosen player instead.
    /// </remarks>
    public int? RecorderEntityIndex { get; private init; }

    /// <summary>Every corpse the demo created, with what its own table said about it.</summary>
    /// <remarks>
    /// **Kept apart from the prop tracks, because a corpse cannot become one.** `DT_TFRagdoll` is
    /// `NOBASE` and carries no model index, so the track machinery — which keys everything on a
    /// model path — has nothing to build from and would file every corpse in the model-less bucket
    /// that nothing drawing ever reads (`PARITY-AUDIT.md` #4).
    ///
    /// **Every corpse in the demo rather than the ones alive at a tick**, which is the shape the
    /// measurement wanted first: "299 in one match, all decoded, none drawn" is a claim about the
    /// recording. Per-tick presence is the next step and needs a lifetime, not just a list.
    /// </remarks>
    public IReadOnlyList<SceneRagdoll> Corpses { get; private init; } = [];

    /// <summary>Every explosion the demo carried, in fire order (B415).</summary>
    /// <remarks>
    /// **The feed itself rather than a list, because a blast is asked for by WINDOW.** An explosion's effect runs
    /// for a second or two after it fires, so a viewer at tick <c>n</c> wants everything back to <c>n</c> minus
    /// that duration — <see cref="ExplosionFeed.Between"/> is that search, and handing out a bare list would put a
    /// copy of it in every caller.
    /// </remarks>
    public ExplosionFeed Explosions { get; private init; } = new();

    /// <summary>Every hitscan shot the demo carried, in fire order (B415) — asked for by window, like explosions.</summary>
    public ShotFeed Shots { get; private init; } = new();

    /// <summary>Every decal the demo's temp entities carried, in fire order, and the table naming them (B415).</summary>
    public DecalFeed Decals { get; private init; } = new();

    /// <summary>Every light style's pattern over the demo, from the <c>lightstyles</c> table.</summary>
    public LightStyleFeed LightStyles { get; private init; } = new();

    /// <summary>Every `CTETFBlood`, in fire order (B415).</summary>
    public BloodFeed Blood { get; private init; } = new();

    /// <summary>Every `CTEEffectDispatch`, in fire order, and the table naming them (B415).</summary>
    public EffectDispatchFeed Dispatches { get; private init; } = new();

    /// <summary>Every weapon muzzle flash, in tick order (B415).</summary>
    public MuzzleFlashFeed MuzzleFlashes { get; private init; } = new();

    /// <summary>Every medigun beam, in the order they started (B396).</summary>
    public HealBeamFeed HealBeams { get; private init; } = new();

    /// <summary>Every rope's <c>SetForce</c> impulse, in arrival order (B478).</summary>
    public RopeImpulseFeed RopeImpulses { get; private init; } = new();

    /// <summary>When each entity was dormant — left the PVS and still held by the client (B478).</summary>
    public EntityDormancy Dormancy { get; private init; } = new();

    /// <summary>The <c>point_camera</c> whose view is in <c>_rt_Camera</c>, tick by tick (B511).</summary>
    public PointCameraFeed PointCameras { get; private init; } = new();

    /// <summary>The <c>env_tonemap_controller</c> overrides the client holds, tick by tick (B514).</summary>
    public TonemapFeed Tonemap { get; private init; } = new();

    /// <summary>The <c>env_sun</c>s the client holds, tick by tick (B378).</summary>
    public SunFeed Suns { get; private init; } = new();

    /// <summary>The medigun's server class — every medigun item is one, the Kritzkrieg and Quick-Fix included.</summary>
    private const string MedigunClass = HealBeamFeed.MedigunClassName;

    /// <summary>Every `CTETFParticleEffect`, as its `"ParticleEffect"` dispatch, in fire order (B415).</summary>
    public TfParticleEffectFeed TfParticleEffects { get; private init; } = new();

    /// <summary>Every `CTESparks`, `CTEMetalSparks` and `CTEArmorRicochet`, in fire order (B415).</summary>
    public SparkFeed Sparks { get; private init; } = new();

    /// <summary>Every choreographed scene that started playing, in tick order (B351).</summary>
    /// <remarks>
    /// **A start rather than a per-tick state, for the reason <see cref="SceneChoreography"/>
    /// gives**: TF2 runs a scene's clock client-side from the moment playback begins, so one tick
    /// says everything a viewer needs.
    ///
    /// **Flat rather than per-actor**, because a partner taunt is one scene animating two players
    /// and splitting it would lose which two were paired.
    /// </remarks>
    public IReadOnlyList<SceneChoreography> Scenes { get; private init; } = [];

    /// <summary>The weapon a player is holding, as they would see it.</summary>
    /// <param name="tick">The tick being drawn.</param>
    /// <param name="playerEntityIndex">The player whose view is being shown.</param>
    /// <returns>Their viewmodel, or <c>null</c> when the demo describes none for them.</returns>
    /// <remarks>
    /// **Two cases, both measured rather than assumed.** A point-of-view recording carries exactly
    /// one viewmodel and never names an owner — you only ever receive your own — so an unowned one
    /// belongs to whoever is being followed. A modern SourceTV recording carries one per player and
    /// names each, so it is matched by owner. Requiring an owner would find nothing on eight of the
    /// nine corpus demos, and the weapon would simply never appear.
    ///
    /// At or before the tick, like every other per-tick lookup here: the demo speaks at packet
    /// ticks and the viewer draws between them.
    ///
    /// **The main hand, because a player has two viewmodels and only one is the weapon.**
    /// <c>MAX_VIEWMODELS</c> is 2 and slot 1 is the off hand, which TF2 gives to the spy's watch
    /// and to grenades. Ignoring the slot answers with whichever entity was described last, and on
    /// the corpus's 2009 badlands recording that is the watch — so the weapon on screen stayed
    /// <c>v_watch_spy</c> across a change of class from soldier to scout.
    ///
    /// **The off hand is drawn as well as the main hand, not instead of it** — the owner, who has
    /// played the class: "main viewmodel doesnt get hidden when a spy goes invis, the watch just
    /// comes up and everything goes transparent". So this answers with one weapon short of what a
    /// spy actually sees, which is a smaller error than the wrong weapon and is its own piece of
    /// work. See <c>docs/findings/04-entities.md</c>.
    /// </remarks>
    public SceneViewmodel? ViewmodelAt(int tick, int playerEntityIndex) =>
        Viewmodel(tick, playerEntityIndex, mainHand: true);

    /// <summary>The model in a player's other hand, which for TF2 is the spy's watch.</summary>
    /// <param name="tick">The tick being drawn.</param>
    /// <param name="playerEntityIndex">The player whose view is being shown.</param>
    /// <returns>Their off-hand viewmodel, or <c>null</c> when they carry none.</returns>
    /// <remarks>
    /// **Drawn as well as the main hand, not instead of it.** The owner, who has played the class:
    /// "main viewmodel doesnt get hidden when a spy goes invis, the watch just comes up and
    /// everything goes transparent". A cloaking spy has both on screen, so a viewer answering only
    /// with <see cref="ViewmodelAt"/> is one model short of what that player saw.
    ///
    /// **The watch is the only thing that uses slot 1.** <c>tf_weaponbase_grenade.cpp:74</c> also
    /// calls <c>SetViewModelIndex( 1 )</c> and reads as a second case, but TF2's throwable grenades
    /// were cut before release: the class is still linked and no shipped item names it. So this
    /// answers null for every class but a spy, which is the ordinary case rather than a failure.
    /// </remarks>
    public SceneViewmodel? OffHandViewmodelAt(int tick, int playerEntityIndex) =>
        Viewmodel(tick, playerEntityIndex, mainHand: false);

    /// <summary>Whichever of a player's two viewmodels was asked for, at or before a tick.</summary>
    /// <remarks>
    /// One walk for both hands, because the rule about owners is the same for each and having it
    /// written twice is how the two would come to disagree.
    /// </remarks>
    private SceneViewmodel? Viewmodel(int tick, int playerEntityIndex, bool mainHand)
    {
        SceneViewmodel? found = null;

        foreach ((int at, SceneViewmodel weapon) in _viewmodels)
        {
            if (at > tick)
            {
                break;
            }

            // **An unowned viewmodel is the follower's only when the demo names no owners at all.**
            // That is the point-of-view shape: one viewmodel, no owner, because a client receives
            // only its own. A SourceTV recording carries one per player and names them — and when
            // one of thirty-seven fails to resolve an owner, treating "unowned" as "anybody's" hands
            // that one to every player who has none. Measured on z1800: following a sniper drew a
            // demoman's arms.
            if (weapon.IsMainHand == mainHand &&
                (weapon.OwnerEntityIndex == playerEntityIndex ||
                 (weapon.OwnerEntityIndex is null && !_viewmodelsNameOwners)))
            {
                found = weapon;
            }
        }

        // **Filtered at the END, on the latest state, never while walking.** Rejecting hidden
        // samples inside the loop would leave `found` holding an older visible one, which is the
        // stale-sample bug this whole flag exists to avoid: a spy who puts the watch away would
        // keep it in frame for the rest of the demo.
        return found is { IsOnScreen: true } ? found : null;
    }

    /// <summary>Every distinct viewmodel model the demo ever describes.</summary>
    /// <remarks>
    /// **A viewmodel is not a prop, and that is precisely why this is needed.** It carries no
    /// origin, so it is deliberately absent from <see cref="Props"/> — and a viewer that builds its
    /// load set by walking the prop tracks therefore never loads the arms, never packs them, and
    /// draws nothing while reporting that it resolved a model. That is what happened: the log said
    /// <c>viewmodel c_demo_arms.mdl ... 0 instances</c> for every frame, with the file sitting in
    /// the archive the whole time.
    ///
    /// Distinct because a match changes weapons constantly and the same few arms recur; the set is
    /// one entry per class in practice.
    /// </remarks>
    public IEnumerable<string> ViewmodelModels =>
        _viewmodels
            .Select(recorded => recorded.Weapon.ModelPath)
            .Where(path => path.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether this demo carries a recorded camera at all.</summary>
    /// <remarks>
    /// **A SourceTV recording's local player is a spectator, and it leaves <c>democmdinfo_t</c> zeroed**, so the
    /// point-of-view camera has nothing to follow and the viewer has to offer something else —
    /// spectating a chosen player, as the engine does. Asked once, because a per-frame null check
    /// cannot tell "not yet" from "never".
    /// </remarks>
    public bool HasRecordedView => _recordedViews.Count > 0;

    /// <summary>Everybody who played, by user id — the roster from <c>userinfo</c>.</summary>
    /// <remarks>
    /// **Keyed by user id rather than by slot**, because a slot reused by a later joiner overwrites
    /// its first occupant and this map is the one that has to remember everybody. `RosterBuilder`
    /// carries the measurement that forced the distinction.
    /// </remarks>
    public IReadOnlyDictionary<int, PlayerInfo> Roster { get; private init; } =
        new Dictionary<int, PlayerInfo>();

    /// <summary>Every named game event but `hltv_chase`, in stream order — what the HUD's listeners hear.</summary>
    public IReadOnlyList<SceneGameEvent> GameEvents { get; private init; } = [];

    /// <summary>The chat's user messages — `SayText`, `SayText2`, `TextMsg` — in stream order, bodies as sent.</summary>
    public IReadOnlyList<SceneUserMessage> UserMessages { get; private init; } = [];

    /// <summary>Every <c>svc_VoiceData</c> with a body, in stream order — what the client's voice path plays.</summary>
    public IReadOnlyList<SceneVoice> Voice { get; private init; } = [];

    /// <summary>Every <c>svc_VoiceInit</c>, in stream order — which codec <see cref="Voice"/> is in.</summary>
    public IReadOnlyList<SceneVoiceInit> VoiceInits { get; private init; } = [];

    /// <summary>The voice codec a packet arrived under, or null when no <c>svc_VoiceInit</c> preceded it.</summary>
    /// <param name="voice">One of <see cref="Voice"/>.</param>
    /// <returns>The codec of the init in force, by stream order.</returns>
    public string? VoiceCodecOf(SceneVoice voice) =>
        voice is { Init: >= 0 } && voice.Init < VoiceInits.Count ? VoiceInits[voice.Init].Codec : null;

    /// <summary>Which entity a person named on the command line is.</summary>
    /// <param name="who">A player name, a user id, or an entity index.</param>
    /// <returns>The entity index to spectate, or null when nobody matches.</returns>
    /// <remarks>
    /// The matching lives in <see cref="PlayerLookup"/> so it can be tested against a roster built
    /// by hand, which HAS ground truth where a corpus demo only has whoever is in it (D38).
    /// </remarks>
    public int? EntityForPlayer(string? who) => PlayerLookup.Resolve(Roster.Values, who);

    /// <summary>The camera the recording was made through at a tick.</summary>
    /// <param name="tick">The tick being drawn.</param>
    /// <returns>The most recently stated view, or <c>null</c> before the first one.</returns>
    /// <remarks>
    /// **At or before, not exactly at.** The demo speaks at packet ticks and the viewer draws
    /// between them, so an exact-match lookup answers nothing on most frames and the view would
    /// flicker back to another camera. Before the first packet the answer really is nothing —
    /// inventing the first view would place the camera somewhere the recording never was.
    /// </remarks>
    public RecordedView? RecordedViewAt(int tick)
    {
        if (_recordedViews.Count == 0 || tick < _viewCommands[_recordedViews[0]].Tick)
        {
            return null;
        }

        int low = 0;
        int high = _recordedViews.Count - 1;

        while (low < high)
        {
            // Rounded up, so the search moves towards the later entry and cannot stall on low.
            int middle = low + ((high - low + 1) / 2);

            if (_viewCommands[_recordedViews[middle]].Tick <= tick)
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }

        return _viewCommands[_recordedViews[low]].View;
    }

    /// <summary>Every command <c>CDemoPlayer</c>'s view half reads, in stream order — what a <see cref="DemoPlayer"/> plays.</summary>
    internal IReadOnlyList<DemoViewCommand> ViewCommands => _viewCommands;

    /// <summary><c>cl.m_nMaxClients</c>: <c>svc_ServerInfo</c>'s player capacity, or zero before one arrived.</summary>
    /// <remarks>One is a single-player listen server, the only case <c>InterpolateViewpoint</c> rolls the view back for.</remarks>
    public int MaxClients { get; private init; }

    /// <summary><c>GetClientInterpAmount()</c>: the watcher's interp under the recording server's bounds, in seconds.</summary>
    /// <remarks>The same amount every track's delay is built from (<see cref="ClientInterp.Amount"/>).</remarks>
    public double ClientInterpAmount { get; private init; }

    /// <summary>The first tick with positions, or zero when the demo has none.</summary>
    public int FirstTick => _frames.Count > 0 ? _frames.TickAt(0) : 0;

    /// <summary>The last tick with positions, or zero.</summary>
    public int LastTick => _frames.Count > 0 ? _frames.TickAt(_frames.Count - 1) : 0;

    /// <summary>Seconds per tick, as the server that recorded this demo ran.</summary>
    /// <remarks>
    /// **Read from the demo, never assumed.** <c>svc_ServerInfo</c> states it, and it is not always
    /// TF2's usual 0.015 — the rate is a server setting, so it varies with how a given box was set
    /// up rather than with when the demo was recorded. A demo played back at the wrong rate looks
    /// like a slow or fast server rather than like a defect.
    ///
    /// Zero when no <c>svc_ServerInfo</c> arrived, which leaves the choice of a default to the
    /// caller rather than burying one here.
    /// </remarks>
    public float IntervalPerTick { get; private init; }

    /// <summary>What the recording server had its replicated ConVars set to.</summary>
    /// <remarks>
    /// **Never null, and a fresh one answers with Valve's declared defaults** — which is exactly
    /// what the engine does for a server that changed nothing, so a timeline built from tracks for
    /// a test is not a special case needing a guard at every reader.
    ///
    /// This is where a mod's altered movement arrives. A vanilla competitive server sends forty
    /// values without touching movement and every one of these stays at its default;
    /// <c>ServerConVars.Changed</c> is what distinguishes that from a jump or surf server.
    /// </remarks>
    public ServerConVars ServerConVars { get; private init; } = new();

    /// <summary>The settings after each <c>net_SetConVar</c>, by the tick it was read at, in stream order.</summary>
    private IReadOnlyList<(int Tick, ServerConVars ConVars)> ConVarChanges { get; init; } = [];

    /// <summary>Valve's defaults, what a tick before the first <c>net_SetConVar</c> runs under.</summary>
    private readonly ServerConVars _defaultConVars = new();

    /// <summary>The replicated ConVars as the client had them at a tick: every <c>net_SetConVar</c> read by then (B450).</summary>
    /// <param name="tick">The demo tick.</param>
    /// <returns>The settings, Valve's defaults before anything was sent.</returns>
    /// <remarks>
    /// <see cref="ServerConVars"/> is the last value of the whole recording; a reader of one moment asks this, because the
    /// client applies each message as it reads it.
    /// </remarks>
    public ServerConVars ServerConVarsAt(int tick)
    {
        IReadOnlyList<(int Tick, ServerConVars ConVars)> changes = ConVarChanges;
        int low = 0;
        int high = changes.Count;

        while (low < high)
        {
            int middle = (low + high) / 2;

            if (changes[middle].Tick <= tick)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low == 0 ? _defaultConVars : changes[low - 1].ConVars;
    }

    /// <summary>Every <c>dem_usercmd</c>, in stream order — the recorder's input, which prediction re-runs (D205).</summary>
    public IReadOnlyList<RecordedUserCommand> UserCommands { get; private init; } = [];

    /// <summary>Each packet's tick and the last usercmd its state includes, in stream order (D205).</summary>
    public IReadOnlyList<(int Tick, int Acknowledged)> PacketAcknowledgements { get; private init; } = [];

    /// <summary>The checksum of the map this was recorded on, when the demo said.</summary>
    /// <remarks>
    /// **`svc_ServerInfo`'s `mapCRC`, which identifies the map's VERSION where its name does not.**
    /// `cp_badlands` in 2017 is not `cp_badlands` in 2026, and the viewer loads by name out of
    /// whatever TF2 install is present — so an old demo is drawn against geometry it was never
    /// recorded on, and every consequence looks like a rendering defect. Three were investigated as
    /// such before the owner identified the real cause (D113, finding 41).
    ///
    /// **Null when the demo carried no `svc_ServerInfo`**, which is a real case for a truncated or
    /// hand-authored file — and distinguishable from "the CRCs differ", which is the point of it
    /// being nullable rather than zero.
    /// </remarks>
    public uint? MapCrc { get; private init; }

    /// <summary>`engine->IsHLTV()`: the demo's `svc_ServerInfo` `is_hltv`, as written — false with no ServerInfo.</summary>
    public bool IsSourceTv { get; private init; }

    /// <summary>The user message the server opens a viewport panel with — `CBasePlayer::ShowViewPortPanel`.</summary>
    private const string SpectatorGuiMenu = "VGUIMenu";

    /// <summary>Every `VGUIMenu` for `specgui` the recorder was sent, in tick order: whether it opened or closed the panel.</summary>
    public IReadOnlyList<(int Tick, bool Show)> SpectatorGuiChanges { get; private init; } = [];

    /// <summary>Whether the server had the spectator GUI open at a tick: the last `specgui` `VGUIMenu` at or before it.</summary>
    /// <param name="tick">The tick.</param>
    /// <returns>Its show flag, or false before the first.</returns>
    public bool SpectatorGuiShownAt(int tick)
    {
        bool shown = false;

        foreach ((int at, bool show) in SpectatorGuiChanges)
        {
            if (at > tick)
            {
                break;
            }

            shown = show;
        }

        return shown;
    }

    /// <summary>The network protocol the demo's header declares, or 0 for a timeline built from nothing.</summary>
    /// <remarks>
    /// **What selects an old demo's content era** (B380, D160). A networked sequence number indexes the
    /// model the recording client loaded, and the protocol is the one thing every demo states that
    /// narrows which client that was. It does not DATE a demo — an old client still records — but it
    /// bounds it: protocol 14 was written by no build later than protocol 15's first.
    /// </remarks>
    public int NetworkProtocol { get; private init; }

    /// <summary>`FL_FROZEN` in the layout this demo's `m_fFlags` was written with — <see cref="PlayerFlags.Frozen"/>.</summary>
    public int FrozenFlag => FlagLayout.Frozen;

    /// <summary>The `m_fFlags` list this demo was written with — <see cref="PlayerFlagLayout.For"/>.</summary>
    public PlayerFlagLayout FlagLayout { get; private init; } = PlayerFlagLayout.Current;

    /// <summary>The map hash <c>svc_ServerInfo</c> carries beside the checksum, when it has one.</summary>
    /// <remarks>
    /// **The instrument for modern demos, because the CRC is dead in them.** Measured across gcor:
    /// 2007 through 2011 carry a real `mapCRC` — and the 2008 and 2011 POV/STV pairs each agree with
    /// themselves, so the field is genuine — while 2013 onward and `z1800` all carry `0xFFFFFFFF`,
    /// the CRC32 init value. Valve stopped computing it somewhere between 2011 and 2013.
    ///
    /// So a version check needs both: the CRC for the old era, this for the new one.
    /// </remarks>
    public IReadOnlyList<byte>? MapHash { get; private init; }

    /// <summary>Where <see cref="Build"/> spent its time. Zero for a timeline built any other way.</summary>
    public TimelinePhases Phases { get; private init; }

    /// <summary>Builds a timeline directly from tracks, for tests that need an exact motion.</summary>
    /// <param name="tracks">The entity tracks the timeline should answer from.</param>
    /// <returns>A timeline with no frames and these tracks.</returns>
    /// <remarks>
    /// **A seam, and a narrow one on purpose.** Some behaviour here is a function of an entity's
    /// motion over time — speed, and the movement pose parameters derived from it — and asserting
    /// on it from a real demo means hunting for a tick where a player happens to be running, which
    /// measures the corpus rather than the code. Two keyframes 200 units apart state the condition
    /// exactly.
    ///
    /// Internal rather than public: this is not a way to build a timeline, it is a way to ask one a
    /// question. Nothing outside the tests should be assembling tracks by hand.
    /// </remarks>
    /// <param name="frames">
    /// Frames to answer <see cref="FrameAt"/> from, for tests whose subject is per-frame state —
    /// the recorder's team, which stage C bakes into a persistent prop and must rebuild on a
    /// switch. Empty when omitted, which answers a null team at every tick.
    /// </param>
    /// <param name="corpses">
    /// Ragdolls the timeline should report, for tests whose subject is what a corpse draws (B395).
    /// Corpses are not prop tracks and never reach the track walk — see `RagdollProps` — so a
    /// timeline assembled without them answers no corpses whatever its tracks hold.
    /// </param>
    internal static DemoTimeline ForTracks(
        List<ScenePropTrack> tracks,
        List<TimelineFrame>? frames = null,
        IReadOnlyList<SceneRagdoll>? corpses = null) =>
        new(frames ?? [], tracks) { Corpses = corpses ?? [] };

    /// <summary>A point-of-view timeline with the recorder's input, for tests that drive prediction (B501).</summary>
    /// <param name="frames">The recorder's networked state; entity 1 is the recorder.</param>
    /// <param name="flagLayout">The `m_fFlags` list the demo was written with.</param>
    /// <param name="commands">The recorded usercmds.</param>
    /// <param name="acknowledgements">Each packet's tick and the last command it acknowledges.</param>
    /// <param name="intervalPerTick">Seconds per tick.</param>
    /// <returns>The timeline.</returns>
    /// <remarks>Same seam as <see cref="ForTracks"/>: a way to ask a question, not to build a timeline.</remarks>
    internal static DemoTimeline ForRecorder(
        List<TimelineFrame> frames,
        PlayerFlagLayout flagLayout,
        IReadOnlyList<RecordedUserCommand> commands,
        IReadOnlyList<(int Tick, int Acknowledged)> acknowledgements,
        float intervalPerTick) =>
        new(frames)
        {
            RecorderEntityIndex = 1,
            MaxClients = 24,
            FlagLayout = flagLayout,
            UserCommands = commands,
            PacketAcknowledgements = acknowledgements,
            IntervalPerTick = intervalPerTick,
        };

    /// <summary>A timeline whose tracks are PLAYERS, with one frame naming them.</summary>
    /// <param name="tracks">The tracks, which go in the player list rather than the prop list.</param>
    /// <param name="players">The players that frame carries, matched to the tracks by entity.</param>
    /// <returns>A timeline whose <see cref="PlayersAt(double, ICollection{ScenePlayer}, bool, RecordedView?, RecorderFeet?, ValueTuple{float, float, float}?)"/> answers.</returns>
    /// <remarks>
    /// **The distinction this exists to make is the one B258 turned on.** `ForTracks` puts its
    /// tracks in `_props`, and `PropsAt` is therefore the only way to reach them — which is how two
    /// tests came to assert that the PROP path computes `move_x`, `move_y` and `Speed`. It does not
    /// any more, and in production it never saw a player to compute them for: player tracks live in
    /// `_playerTracks` and `PropsAt` iterates `_props`. Measured on `tf2-2026-pub-pov-clean`, zero
    /// of 79 prop groups are `CTFPlayer`.
    /// </remarks>
    internal static DemoTimeline ForPlayerTracks(
        List<ScenePropTrack> tracks, IReadOnlyList<ScenePlayer> players) =>
        new(
            [new TimelineFrame(0, players), new TimelineFrame(13, players)],
            props: null,
            playerTracks: tracks);

    /// <summary>A timeline carrying nothing but these sounds, for testing the precache list.</summary>
    internal static DemoTimeline ForSounds(List<SceneSound> sounds) =>
        new([], props: null, playerTracks: null, viewCommands: null, viewmodels: null,
            fog: null, sounds: sounds);

    /// <summary>A timeline carrying all three sources of a MODEL path (B335).</summary>
    /// <param name="props">Prop tracks, which is where most models come from.</param>
    /// <param name="players">Player tracks, which live in their own list.</param>
    /// <param name="viewmodels">The weapons a first-person view holds, by tick.</param>
    /// <returns>A timeline whose <see cref="ModelPaths"/> sees all three.</returns>
    /// <remarks>
    /// **The sibling of <see cref="ForSounds"/>, and for the same reason.** The sound precache list
    /// got a seam of its own because building a demo to test three loops and a set exercises the
    /// entity decoder instead of the thing under test; the model list needs one more because its
    /// sources are three lists rather than one, and the property that matters — a single set spanning
    /// all three — cannot be seen from any one of them.
    ///
    /// **`ForTracks` and `ForPlayerTracks` exist beside this and fill exactly one list each**, which
    /// is what makes them the wrong seam here: a version of `ModelPaths` that walked props alone
    /// would pass against either.
    /// </remarks>
    internal static DemoTimeline ForEverything(
        List<ScenePropTrack>? props = null,
        List<ScenePropTrack>? players = null,
        List<(int Tick, SceneViewmodel Weapon)>? viewmodels = null) =>
        new([], props: props, playerTracks: players, viewCommands: null, viewmodels: viewmodels);

    /// <summary>A timeline carrying what a PERFORMANCE is made of: the players, the scenes they act in, the sounds, the corpses (D213).</summary>
    /// <param name="frames">Frames naming the players, which is where the held weapons and their classes are read from.</param>
    /// <param name="scenes">Every scene playback, as <see cref="Scenes"/> reports them.</param>
    /// <param name="sounds">Every sound, in tick order.</param>
    /// <param name="corpses">Ragdolls the timeline should report.</param>
    /// <param name="intervalPerTick">Seconds per tick.</param>
    /// <returns>A timeline whose <see cref="Scenes"/>, <see cref="Sounds"/>, <see cref="Corpses"/> and <see cref="Frames"/> answer.</returns>
    /// <remarks>
    /// Same seam as <see cref="ForTracks"/>, for the consumers that read a recording's lists whole rather than sampling
    /// a tick — the appearance a level builds from them, and the faces. A test of those states the two scenes and the
    /// one voice line it is about instead of opening a thirty-minute match to find them.
    /// </remarks>
    internal static DemoTimeline ForPerformance(
        List<TimelineFrame> frames,
        List<SceneChoreography>? scenes = null,
        List<SceneSound>? sounds = null,
        IReadOnlyList<SceneRagdoll>? corpses = null,
        float intervalPerTick = 0.015f) =>
        new(frames, sounds: sounds)
        {
            Scenes = scenes ?? [],
            Corpses = corpses ?? [],
            IntervalPerTick = intervalPerTick,
        };

    /// <summary>How many times a decode reports its progress — often enough for a bar to move, rarely enough to cost nothing.</summary>
    private const int ProgressReports = 200;

    /// <summary>Walks a demo and records where everyone was.</summary>
    /// <param name="file">The whole demo file, header included.</param>
    /// <param name="progress">
    /// Told the fraction of the demo's commands walked — about two hundred times a demo, rising, and exactly <c>1</c> at the end —
    /// or null for no reports. For the viewer's loading screen: an 80-second decode is otherwise 80 seconds of nothing.
    /// </param>
    /// <param name="client">The viewer's `cl_interp` settings; null for TF2's defaults.</param>
    /// <param name="classes">
    /// The installed game's class scripts, whose `DontDoAirwalk` and `DontDoNewJump` the anim state reads (B437); null
    /// for the engine's default, which is a class that air-walks and jumps the new way.
    /// </param>
    /// <returns>The timeline, empty when the demo carries no schema or no entities.</returns>
    /// <exception cref="ArgumentException">The file is too short to hold a header.</exception>
    /// <remarks>
    /// **A demo with no <c>dem_datatables</c> yields an empty timeline rather than an error.** Some
    /// files genuinely have none, and a viewer that refused to open them would be refusing exactly
    /// the salvage cases this project exists for.
    /// </remarks>
    public static DemoTimeline Build(
        ReadOnlyMemory<byte> file,
        Action<double>? progress = null,
        ClientInterp? client = null,
        IClassAnimationScripts? classes = null)
    {
        DemoTimeline built = BuildTimeline(file, progress, client ?? new ClientInterp(), classes);
        progress?.Invoke(1d);
        return built;
    }

    private static DemoTimeline BuildTimeline(
        ReadOnlyMemory<byte> file, Action<double>? progress, ClientInterp client, IClassAnimationScripts? classes)
    {
        if (Start(file, progress, client, classes, out List<DemoCommand> commands) is not { } builder)
        {
            return new DemoTimeline([]);
        }

        builder.Run(commands);
        return builder.Finish();
    }

    /// <summary>
    /// Builds as <see cref="Build"/> does, but stops after <paramref name="snapshotAfter"/> commands, snapshots, runs on
    /// to the end, then RESUMES from the snapshot and finishes from there (D217).
    /// </summary>
    /// <param name="file">The whole demo file.</param>
    /// <param name="snapshotAfter">How many commands to walk before the snapshot.</param>
    /// <param name="stopAfter">How many commands the RESUMED builder walks in all before finishing; the end by default.</param>
    /// <returns>The timeline the resumed builder produces, which must equal a straight build's.</returns>
    /// <remarks>
    /// **The live builder runs past the snapshot before it is abandoned** so the resume is tested against output lists
    /// that have grown and changed since — which is what a seek does after playing on.
    /// </remarks>
    internal static DemoTimeline BuildThroughSnapshot(ReadOnlyMemory<byte> file, int snapshotAfter, int stopAfter = int.MaxValue)
    {
        if (Start(file, null, new ClientInterp(), null, out List<DemoCommand> commands) is not { } builder)
        {
            return new DemoTimeline([]);
        }

        int at = Math.Clamp(snapshotAfter, 0, commands.Count);
        int stop = Math.Clamp(stopAfter, at, commands.Count);

        builder.Run(commands.Take(at));
        BuilderSnapshot snapshot = builder.Snapshot();
        builder.Run(commands.Skip(at));

        // **Stopped short of the end, as a seek is**: whatever the live run did after `stop` must not show.
        Builder resumed = Builder.Resume(snapshot);
        resumed.Run(commands.Take(stop).Skip(at));
        return resumed.Finish();
    }

    /// <summary>
    /// Snapshots twice during one run (after <paramref name="earlier"/> and <paramref name="later"/> commands), runs on
    /// to the end, then resumes the FIRST snapshot named, walks <paramref name="walk"/> commands from it, resumes the
    /// SECOND named and finishes at <paramref name="stopAfter"/> — a seek, then another (D217).
    /// </summary>
    /// <param name="file">The whole demo file.</param>
    /// <param name="earlier">The earlier snapshot's command.</param>
    /// <param name="later">The later snapshot's command.</param>
    /// <param name="backFirst">True to resume the earlier one first and then the later (back, then forward).</param>
    /// <param name="walk">Commands walked after the first resume before the second.</param>
    /// <param name="stopAfter">Where the second resume stops.</param>
    /// <returns>The timeline the second resume finishes.</returns>
    internal static DemoTimeline BuildResumingTwice(
        ReadOnlyMemory<byte> file, int earlier, int later, bool backFirst, int walk, int stopAfter)
    {
        if (Start(file, null, new ClientInterp(), null, out List<DemoCommand> commands) is not { } builder)
        {
            return new DemoTimeline([]);
        }

        builder.Run(commands.Take(earlier));
        BuilderSnapshot first = builder.Snapshot();
        builder.Run(commands.Take(later).Skip(earlier));
        BuilderSnapshot second = builder.Snapshot();
        builder.Run(commands.Skip(later));

        (BuilderSnapshot one, int oneAt, BuilderSnapshot two, int twoAt) =
            backFirst ? (first, earlier, second, later) : (second, later, first, earlier);

        Builder.Resume(one).Run(commands.Skip(oneAt).Take(walk));

        Builder resumed = Builder.Resume(two);
        resumed.Run(commands.Take(stopAfter).Skip(twoAt));
        return resumed.Finish();
    }

    /// <summary>Walks <paramref name="at"/> commands and measures what a snapshot there allocates (D217).</summary>
    /// <param name="file">The whole demo file.</param>
    /// <param name="at">Where the snapshot is taken.</param>
    /// <returns>The whole snapshot's bytes and each field's.</returns>
    internal static (long Total, IReadOnlyList<(string Field, long Bytes)> Fields) MeasureSnapshot(ReadOnlyMemory<byte> file, int at)
    {
        if (Start(file, null, new ClientInterp(), null, out List<DemoCommand> commands) is not { } builder)
        {
            return (0, []);
        }

        builder.Run(commands.Take(at));
        return builder.SnapshotCost();
    }

    /// <summary>Snapshots after <paramref name="at"/> commands, finishes the live builder, then resumes the snapshot.</summary>
    /// <param name="file">The whole demo file.</param>
    /// <param name="at">Where the snapshot is taken.</param>
    /// <remarks>The resume must refuse, because Finish handed the shared lists out (D217).</remarks>
    internal static void ResumeAfterFinish(ReadOnlyMemory<byte> file, int at)
    {
        if (Start(file, null, new ClientInterp(), null, out List<DemoCommand> commands) is not { } builder)
        {
            return;
        }

        builder.Run(commands.Take(at));
        BuilderSnapshot snapshot = builder.Snapshot();
        builder.Run(commands.Skip(at));
        builder.Finish();

        Builder.Resume(snapshot);
    }

    /// <summary>Builds as <see cref="Build"/> does but only through the first <paramref name="stopAfter"/> commands.</summary>
    /// <param name="file">The whole demo file.</param>
    /// <param name="stopAfter">How many commands to walk.</param>
    /// <returns>The timeline of that prefix — what a resumed build stopped at the same command must equal.</returns>
    internal static DemoTimeline BuildThrough(ReadOnlyMemory<byte> file, int stopAfter)
    {
        if (Start(file, null, new ClientInterp(), null, out List<DemoCommand> commands) is not { } builder)
        {
            return new DemoTimeline([]);
        }

        builder.Run(commands.Take(Math.Clamp(stopAfter, 0, commands.Count)));
        return builder.Finish();
    }

    /// <summary>How many commands a demo holds after its header — the range <see cref="BuildThroughSnapshot"/> cuts in.</summary>
    /// <param name="file">The whole demo file.</param>
    /// <returns>The count.</returns>
    internal static int CommandCount(ReadOnlyMemory<byte> file) => DemoCommandReader.Read(file[DemoHeader.SizeBytes..]).Count();

    /// <summary>
    /// A demo walked once, keeping a checkpoint every few seconds and no whole-demo output: any tick is answered by a
    /// WINDOW, the nearest earlier checkpoint decoded forward past it (D217 step 3).
    /// </summary>
    /// <remarks>
    /// **The video editor's arrangement**, as the owner asked for it: keyframes are the checkpoints, the demo's own
    /// commands are the bytes between them, and a seek is restore plus decode-forward. Each checkpoint owns its state and
    /// output tail, so seeks in any order are exact — the back-then-forward case the shared-list marks refused.
    /// </remarks>
    internal sealed class Checkpoints
    {
        private readonly List<DemoCommand> _commands;
        private readonly List<(int Command, int Tick, Builder State)> _checkpoints;
        private readonly Builder.EndFacts _end;

        /// <summary>What every resume shares, built once for the walk rather than per window.</summary>
        private readonly IReadOnlySet<object> _inputs;

        private Checkpoints(List<DemoCommand> commands, List<(int, int, Builder)> checkpoints, Builder.EndFacts end)
        {
            _commands = commands;
            _checkpoints = checkpoints;
            _end = end;
            _inputs = checkpoints[0].Item3.Inputs();
        }

        /// <summary>The least reach any checkpoint kept: what every window can answer for.</summary>
        public WindowReach Reach { get; private init; } = new(0, 0);

        /// <summary>The interpolation settings the walk was decoded under, which every window finishes with.</summary>
        public ClientInterp Client { get; private init; } = new();

        /// <summary>The demo's first frame's tick — its START, which a window's own FirstTick (its cut) is not.</summary>
        public int FirstTick { get; private init; }

        /// <summary>The demo's seconds per tick.</summary>
        public double IntervalPerTick { get; private init; }

        /// <summary>The ticks the checkpoints were taken at, in order.</summary>
        public IEnumerable<int> Ticks => _checkpoints.Select(checkpoint => checkpoint.Tick);

        /// <summary>
        /// Walks the demo with the reach <paramref name="settings"/> give every reader (D220), and walks again if the
        /// demo's FINAL server bounds, which every window's interpolation uses, need further than a checkpoint kept.
        /// </summary>
        /// <param name="file">The whole demo file.</param>
        /// <param name="everyTicks">The spacing between checkpoints.</param>
        /// <param name="settings">The viewer's settings; when they change, ask <see cref="Covers"/> and walk again if not.</param>
        /// <param name="created">When given, counts what every checkpoint after the first creates rather than shares, by type.</param>
        /// <param name="cancel">Stops the walk between two packets.</param>
        /// <returns>The checkpoints, or null for a demo with no data tables.</returns>
        /// <exception cref="OperationCanceledException">Cancelled before the walk finished.</exception>
        public static Checkpoints? Walk(
            ReadOnlyMemory<byte> file,
            int everyTicks,
            WindowSettings settings,
            Dictionary<Type, (long Objects, long Elements)>? created = null,
            CancellationToken cancel = default)
        {
            ArgumentNullException.ThrowIfNull(settings);

            if (Walk(file, everyTicks, settings.Interp, builder => builder.ReachFor(settings), created, cancel) is not { } walked)
            {
                return null;
            }

            WindowReach needed = WindowReach.For(settings, walked._end.ServerConVars, walked.IntervalPerTick);

            if (walked.Reach.Covers(needed))
            {
                return walked;
            }

            // The first walk's counts describe checkpoints thrown away; only the walk returned is counted.
            created?.Clear();
            return Walk(file, everyTicks, settings.Interp, _ => needed, created, cancel);
        }

        /// <summary>Walks with a FIXED reach, for a test or probe that needs the trim to bite at a chosen depth.</summary>
        /// <param name="file">The whole demo file.</param>
        /// <param name="everyTicks">The spacing between checkpoints.</param>
        /// <param name="lookbackTicks">How much output before a checkpoint it keeps.</param>
        /// <param name="trackLookbackTicks">How much track history before a checkpoint it keeps.</param>
        /// <param name="client">The viewer's interpolation settings.</param>
        /// <param name="created">When given, counts what every checkpoint after the first creates rather than shares, by type.</param>
        /// <returns>The checkpoints, or null for a demo with no data tables.</returns>
        public static Checkpoints? Walk(
            ReadOnlyMemory<byte> file,
            int everyTicks,
            int lookbackTicks,
            int trackLookbackTicks = int.MaxValue,
            ClientInterp? client = null,
            Dictionary<Type, (long Objects, long Elements)>? created = null)
        {
            WindowReach reach = new(Math.Min(lookbackTicks, trackLookbackTicks), lookbackTicks);
            return Walk(file, everyTicks, client ?? new ClientInterp(), _ => reach, created, CancellationToken.None);
        }

        /// <summary>Whether windows of this walk answer for <paramref name="settings"/>, or the demo must be walked again.</summary>
        /// <param name="settings">The viewer's settings now.</param>
        /// <returns>
        /// True when the interpolation settings are the ones walked with — they are decoded into every checkpoint — and
        /// every reader's reach under them is no further than what was kept.
        /// </returns>
        public bool Covers(WindowSettings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);
            return settings.Interp == Client && Reach.Covers(WindowReach.For(settings, _end.ServerConVars, IntervalPerTick));
        }

        private static Checkpoints? Walk(
            ReadOnlyMemory<byte> file,
            int everyTicks,
            ClientInterp client,
            Func<Builder, WindowReach> reachAt,
            Dictionary<Type, (long Objects, long Elements)>? created,
            CancellationToken cancel)
        {
            if (Start(file, null, client, null, out List<DemoCommand> commands) is not { } builder)
            {
                return null;
            }

            WindowReach least = new(int.MaxValue, int.MaxValue);

            // Every class flattened now, so no window ever writes the flattening cache all checkpoints share.
            builder.FlattenEveryClass();
            List<(int, int, Builder)> checkpoints = [(0, int.MinValue, builder.Checkpoint(null, out Dictionary<object, object> prior))];
            int next = int.MinValue;
            int from = 0;

            for (int index = 0; index < commands.Count; index++)
            {
                // **Packets only**: they are what frames are made of, and their ticks rise. A signon command carries the
                // server's tick at the moment recording began (`Finish` re-times its sounds for that reason), far past
                // the first packets' ticks, so it would place a checkpoint thousands of ticks into the future.
                if (commands[index].Type != DemoCommandType.Packet)
                {
                    continue;
                }

                // Between packets, so a viewer closing or opening another demo stops a walk within one spacing.
                cancel.ThrowIfCancellationRequested();

                int tick = commands[index].Tick;

                if (next == int.MinValue && tick > 0)
                {
                    next = tick + everyTicks;
                }

                if (next != int.MinValue && tick >= next)
                {
                    builder.Run(commands.Skip(from).Take(index - from));
                    from = index;
                    // The reach under the settings and the server's bounds as they stand at this checkpoint.
                    WindowReach reach = reachAt(builder);
                    least = new WindowReach(Math.Min(least.TrackTicks, reach.TrackTicks), Math.Min(least.OutputTicks, reach.OutputTicks));
                    builder.Trim(Cut(tick, reach.OutputTicks), Cut(tick, reach.TrackTicks));
                    checkpoints.Add((index, tick, builder.Checkpoint(prior, out prior, created)));
                    next = tick + everyTicks;
                }
            }

            builder.Run(commands.Skip(from));

            // Only the end-of-demo facts are kept from the walk itself; the rest of it is garbage once this returns.
            return new Checkpoints(commands, checkpoints, builder.End())
            {
                Reach = least.TrackTicks == int.MaxValue ? reachAt(builder) : least,
                Client = client,
                IntervalPerTick = builder.IntervalPerTick,
                FirstTick = builder.StartTick,
            };
        }

        /// <summary>A tick less a reach, saturating rather than wrapping for an unbounded one.</summary>
        private static int Cut(int tick, int reach) => (int)Math.Max(int.MinValue, (long)tick - reach);

        /// <summary>The timeline from the checkpoint at or before <paramref name="tick"/> through <paramref name="through"/>.</summary>
        /// <param name="tick">The first tick the window must answer.</param>
        /// <param name="through">The last tick it must answer.</param>
        /// <returns>A timeline whose answers in that range equal a whole build's.</returns>
        /// <remarks>
        /// **Safe to call from several threads at once**: it writes nothing it shares. Each window copies its checkpoint;
        /// the schema, its flattening cache (filled by the walk) and the end facts are only read.
        /// </remarks>
        public DemoTimeline Window(int tick, int through) => Window(tick, through, out _);

        /// <inheritdoc cref="Window(int, int)"/>
        /// <param name="tick">The first tick the window must answer.</param>
        /// <param name="through">The last tick it must answer.</param>
        /// <param name="spent">Where it spent its time, in milliseconds: copying the checkpoint, decoding forward, finishing.</param>
        public DemoTimeline Window(int tick, int through, out (double Copy, double Decode, double Finish) spent) =>
            Window(tick, through, CancellationToken.None, out spent);

        /// <inheritdoc cref="Window(int, int)"/>
        /// <param name="tick">The first tick the window must answer.</param>
        /// <param name="through">The last tick it must answer.</param>
        /// <param name="cancel">Stops the decode between two commands; the window is then never finished.</param>
        /// <param name="spent">Where it spent its time, in milliseconds.</param>
        /// <exception cref="OperationCanceledException">Cancelled before the window was finished.</exception>
        public DemoTimeline Window(int tick, int through, CancellationToken cancel, out (double Copy, double Decode, double Finish) spent) =>
            Window(tick, through, onCommand: null, cancel, out spent);

        /// <inheritdoc cref="Window(int, int, CancellationToken, out ValueTuple{double, double, double})"/>
        /// <param name="tick">The first tick the window must answer.</param>
        /// <param name="through">The last tick it must answer.</param>
        /// <param name="onCommand">Told before each command is decoded — a test's hook to cancel mid-decode; null for none.</param>
        /// <param name="cancel">Stops the decode between two commands.</param>
        /// <param name="spent">Where it spent its time, in milliseconds.</param>
        internal DemoTimeline Window(int tick, int through, Action? onCommand, CancellationToken cancel, out (double Copy, double Decode, double Finish) spent)
        {
            int at = _checkpoints.FindLastIndex(checkpoint => checkpoint.Tick <= tick);
            (int command, _, Builder state) = _checkpoints[Math.Max(0, at)];

            long from = Stopwatch.GetTimestamp();
            Builder resumed = Builder.FromCheckpoint(state, _inputs);
            long copied = Stopwatch.GetTimestamp();
            int end = command;

            // Through every command of the next tick too, so the last frame asked for is final rather than mid-tick.
            while (end < _commands.Count && (_commands[end].Type != DemoCommandType.Packet || _commands[end].Tick <= through + 1))
            {
                end++;
            }

            // Checked between commands, so a scrub's abandoned window stops within one command of being cancelled.
            resumed.Run(_commands.Skip(command).Take(end - command).TakeWhile(_ =>
            {
                onCommand?.Invoke();
                return !cancel.IsCancellationRequested;
            }));
            cancel.ThrowIfCancellationRequested();
            long ran = Stopwatch.GetTimestamp();
            DemoTimeline window = resumed.Finish(_end);
            spent = (
                Stopwatch.GetElapsedTime(from, copied).TotalMilliseconds,
                Stopwatch.GetElapsedTime(copied, ran).TotalMilliseconds,
                Stopwatch.GetElapsedTime(ran).TotalMilliseconds);
            return window;
        }

        /// <summary>What the checkpoints allocate in all, measured by copying each once.</summary>
        /// <returns>Bytes, and the largest single checkpoint.</returns>
        public (long Total, long Largest) Cost()
        {
            long total = 0;
            long largest = 0;

            foreach ((_, _, Builder state) in _checkpoints)
            {
                long before = GC.GetAllocatedBytesForCurrentThread();
                Builder copy = Builder.FromCheckpoint(state, _inputs);
                long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
                GC.KeepAlive(copy);
                total += bytes;
                largest = Math.Max(largest, bytes);
            }

            return (total, largest);
        }

        /// <summary>One checkpoint's bytes by field, as <c>SnapshotCost</c> reports a snapshot's.</summary>
        /// <param name="index">Which checkpoint.</param>
        /// <returns>Its fields, largest first.</returns>
        public IReadOnlyList<(string Field, long Bytes)> CostByField(int index) =>
            _checkpoints[index].State.SnapshotCost().Fields;

        /// <summary>
        /// The direct control on a checkpoint staying untouched: its value hash, before and after two windows are built
        /// from it — the first resume decoding forward, then a second (D217 step 4).
        /// </summary>
        /// <param name="index">Which checkpoint.</param>
        /// <returns>Whether either window changed any value the checkpoint holds, shared frozen state included.</returns>
        /// <remarks>
        /// **Why not answers alone**: a window that wrote its checkpoint changes the NEXT window from it, and a later seek
        /// can still agree with a whole build by luck; and comparing a resume with a fresh one cannot see a write into
        /// what they share. The hash follows that shared state.
        /// </remarks>
        public bool WindowsWriteTheirCheckpoint(int index)
        {
            (_, int tick, Builder state) = _checkpoints[index];
            int through = index + 1 < _checkpoints.Count ? _checkpoints[index + 1].Tick - 1 : tick + 1;
            string before = StateCopy.Digest(state, _inputs);
            GC.KeepAlive(Window(tick, through));
            GC.KeepAlive(Window(tick, through));
            return StateCopy.Digest(state, _inputs) != before;
        }

        /// <summary>
        /// What every checkpoint together KEEPS, by owner, in approximate bytes: each object counted once, under the first
        /// owner of the first checkpoint that reaches it (D217 step 4, part 3).
        /// </summary>
        /// <returns>Owners, largest first.</returns>
        /// <remarks>
        /// **First reach wins**, so an object two checkpoints share is the earlier one's, and one two owners share is the
        /// one walked first; owner shells are not counted. The control: the sum sits near the measured retained heap.
        /// </remarks>
        /// <param name="byType">When given, filled with the bytes of each owner by the type holding them.</param>
        public IReadOnlyList<(string Owner, long Bytes)> KeptByOwner(Dictionary<string, Dictionary<Type, long>>? byType = null)
        {
            HashSet<object> seen = new(ReferenceEqualityComparer.Instance);
            Dictionary<string, long> bytes = [];

            foreach ((_, _, Builder state) in _checkpoints)
            {
                foreach ((string name, object owned, bool shell) in state.Owners(_inputs))
                {
                    if (!shell)
                    {
                        Dictionary<Type, long>? types = null;

                        if (byType is not null && !byType.TryGetValue(name, out types))
                        {
                            types = byType[name] = [];
                        }

                        bytes[name] = bytes.GetValueOrDefault(name) + StateCopy.Footprint(owned, _inputs, seen, types);
                    }
                }
            }

            return [.. bytes.Select(entry => (entry.Key, entry.Value)).OrderByDescending(entry => entry.Value)];
        }

        /// <summary>What resuming one checkpoint creates, by owning field and type (D217 step 4).</summary>
        /// <param name="index">Which checkpoint.</param>
        /// <returns>The rows, largest first.</returns>
        public IReadOnlyList<(string Owner, Type Type, long Objects, long Elements)> ResumeCensusByOwner(int index) =>
            _checkpoints[index].State.CensusByOwner();

        /// <summary>What resuming one checkpoint creates, by type: the census of a resume copy (D217 step 4).</summary>
        /// <param name="index">Which checkpoint.</param>
        /// <returns>Objects and array elements created, by type.</returns>
        public Dictionary<Type, (long Objects, long Elements)> ResumeCensus(int index)
        {
            Dictionary<Type, (long Objects, long Elements)> created = [];
            GC.KeepAlive(Builder.FromCheckpoint(_checkpoints[index].State, _inputs, created));
            return created;
        }
    }

    /// <summary>A builder's state between two commands, and its output lists' marks (D217).</summary>
    private sealed record BuilderSnapshot(Builder Builder, (int Count, object? Last)[] Marks);

    private static Builder? Start(
        ReadOnlyMemory<byte> file,
        Action<double>? progress,
        ClientInterp client,
        IClassAnimationScripts? classes,
        out List<DemoCommand> commands)
    {
        long buildFrom = Stopwatch.GetTimestamp();
        DemoHeader header = DemoHeader.Parse(file.Span);
        long phaseFrom = Stopwatch.GetTimestamp();

        commands =
            [.. DemoCommandReader.Read(file[DemoHeader.SizeBytes..])];

        long commandTicks = Stopwatch.GetTimestamp() - phaseFrom;

        // **`DemoCommand` is a readonly record STRUCT, so FirstOrDefault yields default(T) rather
        // than null and a `is not { }` guard on it can never fire.** This block used to read
        // `DemoCommand? tables = commands.FirstOrDefault(...)`, which compiles, looks like the
        // familiar reference-type idiom and is dead: the implicit conversion wraps the default
        // struct in a non-null nullable, so a demo carrying no dem_datatables fell through to
        // SendTableParser with an empty payload and threw "the payload ends mid-table after 0
        // bytes" instead of returning an empty timeline.
        //
        // Found by a synthetic demo built without the command — every real demo has one, so the
        // corpus could not reach this path. The type check below is what the nullable pattern was
        // meant to express: DemoCommandType has no zero member on purpose, so a defaulted struct
        // cannot collide with a genuine command.
        DemoCommand dataTables = commands.FirstOrDefault(
            command => command.Type == DemoCommandType.DataTables);

        return dataTables.Type != DemoCommandType.DataTables
            ? null
            : new Builder(header, commands, dataTables, progress, client, classes, buildFrom, commandTicks);
    }

    /// <summary>
    /// <see cref="BuildTimeline"/>'s single forward pass, as an object: what were its locals are these fields, under the
    /// same names, so the pass can be stopped between commands, copied and resumed (D217).
    /// </summary>
    /// <remarks>
    /// **The body is indented as it was when it was a method**, one level shallower than a nested class would put it, so
    /// this commit's diff shows only what changed. A whitespace-only reindent is a separate pass.
    /// </remarks>
    private sealed partial class Builder
    {
        private readonly Action<double>? progress;
        private readonly ClientInterp client;
        private readonly IClassAnimationScripts? classes;
        private readonly DemoHeader header;
        private readonly List<DemoCommand> commands;
        private readonly long buildFrom;
        private readonly long commandTicks;
        private readonly long schemaTicks;
        private long messageTicks;
        private readonly int[] simulationLag;
        private readonly int[] animationLag;
        private readonly int[] clockGap;
        private readonly Dictionary<string, int[]> lagByClass;
        private readonly Dictionary<int, SceneRagdoll> corpses;
        private readonly TailList<SceneRagdoll> replaced;
        private long entityTicks;
        private long samplingTicks;
        private long frameTicks;
        private long viewmodelTicks;
        private readonly List<int> viewmodelEntities;
        private readonly HashSet<int> touchedEntities;
        private readonly Dictionary<int, int> viewmodelWeapon;
        private readonly NetDecodeState state;
        private readonly DemoSchema schema;
        private readonly EntityDecoder decoder;
        private readonly EntityStateTable entities;
        private readonly HashSet<int> mergesItself;
        private readonly HashSet<int> combatWeapons;
        private readonly Dictionary<int, string> effectClassNames;
        private readonly PlayerGestureFeed gestures;
        private readonly List<(int Fires, DecodedTempEntity Effect)> queuedGestures;
        private readonly EffectFeeds feeds;
        private readonly MuzzleFlashFeed muzzleFlashes;
        private readonly HealBeamFeed healBeams;
        private readonly RopeImpulseFeed ropeImpulses;
        private readonly EntityDormancy dormancy;
        private readonly PointCameraFeed pointCameras;
        private readonly TonemapFeed tonemap;
        private readonly SunFeed suns;
        private readonly TailList<TimelineFrame> frames;
        private readonly FirstStatedRoster firstStated;
        private float interval;
        private readonly Dictionary<int, int> lastHeld;
        private readonly Dictionary<int, int> burningSince;
        private PlayerConditions overlayConditions;
        private string? screenOverlay;
        private readonly Dictionary<int, int> jumpParity;
        private readonly Dictionary<int, int> jumpedAt;
        private readonly Dictionary<int, (int Tick, float X, float Y, float Z)> lastHeight;
        private readonly Dictionary<int, FeetYaw> feet;
        private readonly Dictionary<int, List<SceneItem>> carriedBefore;
        private readonly Dictionary<int, List<SceneGesture>> gesturedBefore;
        private readonly Dictionary<int, ScenePlayerDetail> detailBefore;
        private readonly ModelPrecache precache;
        private readonly LightStyleFeed lightStyles;
        private readonly int protocol;
        private readonly SoundNames soundNames;
        private readonly TailList<SceneSound> sounds;
        private readonly ScenePrecache scenePrecache;
        private readonly ChunkedList<SceneChoreography> choreography;
        private readonly Dictionary<int, int> playingScenes;
        private readonly TailList<(int Tick, SceneSoundscape Soundscape)> soundscapes;
        private readonly TailList<(int Tick, DirectorShot Shot)> director;
        private readonly Dictionary<int, ScenePropTrack> tracks;
        private readonly List<ScenePropTrack> props;
        private readonly List<ScenePropTrack> playerTracks;
        private readonly TailList<DemoViewCommand> viewCommands;
        private readonly TailList<RecordedUserCommand> userCommands;
        private readonly TailList<(int Tick, int Acknowledged)> packetAcknowledgements;
        private int? recorderSlot;
        private int maxClients;
        private uint? mapCrc;
        private bool isSourceTv;
        private IReadOnlyList<byte>? mapHash;
        private readonly ServerConVars serverConVars;
        private readonly TailList<(int Tick, ServerConVars ConVars)> conVarHistory;
        private readonly TailList<(int Tick, SceneViewmodel Weapon)> viewmodels;
        private readonly TailList<(int Tick, ViewFog Fog)> fogSamples;
        private int fogControllersSeen;
        private int fogProperties;
        private readonly Dictionary<int, SceneViewmodel> lastViewmodel;
        private readonly Dictionary<int, PlayerInfo> bySlot;
        private readonly Dictionary<int, PlayerInfo> everyone;
        private readonly TailList<SceneGameEvent> gameEvents;
        private readonly TailList<SceneUserMessage> userMessages;
        private readonly TailList<SceneVoice> voice;
        private readonly TailList<SceneVoiceInit> voiceInits;

        // Deep-copied with the builder rather than marked: small, and a plain list (D217).
        private readonly List<(int Tick, bool Show)> spectatorGuiChanges;
        private IReadOnlyDictionary<int, PlayerInfo>? rosterAtEvent;
        private int walked;
        private readonly int reportEvery;

        /// <summary>Sets up the pass as <see cref="BuildTimeline"/> did before its first command.</summary>
        public Builder(
            DemoHeader header,
            List<DemoCommand> commands,
            DemoCommand dataTables,
            Action<double>? progress,
            ClientInterp client,
            IClassAnimationScripts? classes,
            long buildFrom,
            long commandTicks)
        {
        this.header = header;
        this.commands = commands;
        this.progress = progress;
        this.client = client;
        this.classes = classes;
        this.buildFrom = buildFrom;
        this.commandTicks = commandTicks;
        messageTicks = 0;

        // Seventeen buckets for −8 to +8 ticks, and one past them for updates that said nothing —
        // the control that tells a real distribution from a partial one (B273).
        simulationLag = new int[LagBuckets + 1];
        animationLag = new int[LagBuckets + 1];
        clockGap = new int[LagBuckets + 1];
        lagByClass = [];

        // Keyed by entity so a corpse updated across several packets is one corpse, and so the
        // last update — the one carrying its resting place — is the one kept.
        corpses = [];

        // Corpses whose SLOT has since been taken by another. They are complete and cannot be
        // updated again, so they leave the live map rather than being overwritten by their successor.
        replaced = [];
        entityTicks = 0;
        samplingTicks = 0;
        frameTicks = 0;
        viewmodelTicks = 0;

        // Entity indices seen carrying a viewmodel model index, kept ascending (B265).
        viewmodelEntities = [];

        // Which entities THIS packet updated, and which weapon each viewmodel last named — the two
        // things the viewmodel sampler needs to know whether it can skip re-deriving a sample.
        touchedEntities = [];
        viewmodelWeapon = [];

        state = new() { NetworkProtocol = (ushort)header.NetworkProtocol };

        long phaseFrom = Stopwatch.GetTimestamp();

        schema = SendTableParser.Parse(
            dataTables.Payload.Span, (ushort)header.NetworkProtocol);

        schemaTicks = Stopwatch.GetTimestamp() - phaseFrom;

        decoder = new(
            schema, EntityDecoder.ClassIdBits(schema.ServerClasses.Count));

        // **Given the decoder, because an entering entity is a delta against its class baseline.**
        // Without it every entity whose whole state equals its baseline accumulates as an empty
        // one - see IEntityBaselines, and B132, which is what that cost.
        entities = new(decoder);

        // **Class names come from dem_datatables, not from svc_ClassInfo.** TF2 sets the
        // "create on client" flag and sends no names, so a reader waiting for that message names
        // nothing and finds no players while decoding every entity correctly.
        // **Which classes bone-merge themselves, computed once from the schema** (B231).
        // `CEconWearable::Spawn` calls `AddEffects( EF_BONEMERGE )` outside its server-only guard
        // (`econ_wearable.cpp:112`), so every client sets it for every wearable it creates and the
        // flag never travels — measured, 26 of 26 `CTFWearable` entities send no `m_fEffects` at
        // all. This viewer is the only reader that never runs `Spawn`, so it derives the same
        // answer from the class, which `dem_datatables` does carry.
        //
        // Per class rather than per entity because the answer cannot vary between two instances of
        // one class, and the walk is over send tables rather than over anything cheap.
        mergesItself = [];

        // **Which classes ARE combat weapons, so an absent `m_iState` can mean its default rather
        // than "not a weapon"** (B245). The two readings of absence want opposite answers: a
        // `CTFWearable` never sends the property and must always draw, while a weapon that has not
        // restated it since re-entering the visible set is at `WEAPON_NOT_CARRIED` and must not.
        //
        // Only the schema can tell them apart, and it can: `m_iState` is declared by
        // `DT_BaseCombatWeapon` (`basecombatweapon_shared.cpp:2871`), so a class whose table chain
        // reaches that table has the field and a class that does not never had it.
        //
        // Per class for the same reason `mergesItself` is: the answer cannot vary between two
        // instances, and the walk is over send tables rather than over anything cheap.
        combatWeapons = [];

        // **Names for temp entities, which carry a class id and nothing else** (B282). A player's
        // animation layers are excluded from the wire entirely (`tf_player.cpp:774`), so a reload,
        // a flinch or an attack gesture reaches a demo only as a `CTEPlayerAnimEvent` effect — and
        // an effect names its class by id.
        effectClassNames = [];

        foreach (ServerClass serverClass in schema.ServerClasses)
        {
            entities.SetClassName(serverClass.Id, serverClass.ClassName);
            effectClassNames[serverClass.Id] = serverClass.ClassName;

            if (SchemaClasses.BoneMergesItself(schema, serverClass.TableName))
            {
                mergesItself.Add(serverClass.Id);
            }

            if (SchemaClasses.Inherits(schema, serverClass.TableName, CombatWeaponTable))
            {
                combatWeapons.Add(serverClass.Id);
            }
        }

        // **Every player's gesture slots, filled from the temp entity stream** (B282). Not per
        // frame: a gesture is an EVENT with a start, and the slot it lands in holds until something
        // replaces it or its sequence runs out — which only the scene can decide, because only the
        // scene has the model that says how long the sequence is.
        gestures = new();
        queuedGestures = [];

        // **Every explosion, from the same stream** (B415). A one-shot at a tick rather than a state at every
        // tick, so it is a list in fire order and not a per-frame sample — see `ExplosionFeed`.
        feeds = new();
        muzzleFlashes = new();
        healBeams = new();
        ropeImpulses = new();
        dormancy = new();
        pointCameras = new();
        tonemap = new();
        suns = new();

        frames = [];

        // Each player's first-stated team and class, filled into earlier frames when they are READ (D217).
        firstStated = new();

        interval = 0f;

        // **The jump clock is not here**: it was FL_ONGROUND clearing, on the claim that a demo carries no jump event.
        // It does — PLAYERANIMEVENT_JUMP in `CTEPlayerAnimEvent` — and `PlayerGestureFeed.Jumping` keeps it.

        // **The last weapon each player was seen holding, for a corpse's bodygroups** (B395).
        // Measured on `demostf-cp_process_f12-2026-08-07`: `m_hActiveWeapon` is readable at the
        // death tick for only **15 of 204** corpses — and when it IS readable it matches one of
        // that player's own bone-merged children **15 times out of 15**. So the comparison was
        // never the problem and the value simply is not on the wire at the moment of death; a
        // dying player is not holding anything. The engine has the same information from the tick
        // before, because its client copies `m_nBody` off a player it has been tracking all along.
        lastHeld = [];

        // **When each player caught fire** (B336), and the reasoning is the same as the line above.
        // `CProxyBurnLevel` ramps `$detailblendfactor` from `m_flBurnEffectStartTime`, which is set
        // CLIENT-SIDE by `CTFPlayerShared::OnAddBurning` to `gpGlobals->curtime` when
        // `TF_COND_BURNING` is ADDED (`tf_player_shared.cpp:7306`) and cleared by `OnRemoveBurning`
        // (`:6884`). Nothing networks it. The client has no more information than a demo does — the
        // moment the bit turns on — so watching the transition reproduces the client rather than
        // inventing a time for it.
        burningSince = [];

        // **The local player's screen overlay, stepped by the condition hooks as they fire** (`ScreenOverlay`): one
        // slot, written by each `OnAdd*` and cleared by an `OnRemove*` only when it holds that hook's own material, so
        // it is history and only this in-order loop can keep it.
        overlayConditions = default;
        screenOverlay = null;

        // **When each player last JUMPED — a teleport or a respawn** (B346). The wire carries
        // `m_ubInterpolationFrame`, a parity whose value means nothing and whose CHANGE means a
        // discontinuity: `IncrementInterpolationFrame` (`baseentity.cpp:8471`), declared under the
        // comment "Call this to cause a discontinuity (teleport)" (`baseentity.h:878`).
        //
        // **Players need their own tracker even though props have one**, and that is the whole
        // reason this exists. Measured on `tf2-2026-pub-pov-cheater`: of 332 sends that change the
        // field on a live entity, ALL 332 are `CTFPlayer` — so the prop-track path stamped zero
        // across 570 tracks while every unit test passed. A player is not a prop track.
        jumpParity = [];

        // The stamp itself, in ticks, carried onto the player the same way the two above are.
        jumpedAt = [];

        // Where each player was last tick, so a vertical speed can be differenced. The client does
        // the same: GetOuterAbsVelocity calls EstimateAbsVelocity on the client, which estimates
        // from position history rather than reading a networked velocity.
        lastHeight = [];

        // **Where each player's feet point**, which is where their body is drawn. The engine runs
        // this per client frame; a demo gives ticks, so it runs per tick. Stateful by nature — the
        // feet lag the eyes and catch up over several of them.
        feet = [];

        // **No air-walk latch here: the gesture feed keeps `m_bInAirWalk`** (B112). This set was a second latch for the
        // body alone — set on a fast rise even while ducked, cleared only by the ground — and the reload read nothing,
        // so the two could not have agreed. The engine has one, read by both; so does the feed.

        // Each player's last carried-items list, handed out again while nothing in it changes (B433).
        carriedBefore = [];
        gesturedBefore = [];
        detailBefore = [];

        precache = new();
        lightStyles = new();
        protocol = header.NetworkProtocol;

        // **The sounds the recording plays, and the table that names them.** Both are needed
        // together: svc_Sounds carries a NUMBER, and the number is an index into this demo's own
        // soundprecache, so a decoder without the table produces sounds nobody can open (B168).
        soundNames = new();
        sounds = [];

        // **The taunts, and the table that names them** — the same arrangement, for the same reason:
        // `m_nSceneStringIndex` is a number and only this demo's own `Scenes` table says which
        // compiled scene it is (B351). `_playing` remembers which scene entities were already
        // running, because the engine acts on the TRANSITION rather than the value
        // (`c_sceneentity.cpp:327`) and a taunt sitting at true for a thousand ticks began once.
        scenePrecache = new();
        choreography = [];
        playingScenes = [];

        // Sampled on change like fog, and present only in a point-of-view recording — see
        // SceneSoundscape for why a SourceTV demo carries nobody's (B173).
        soundscapes = [];

        // Every shot the director called, so the chase camera can be framed as the recording asks
        // rather than always from C_HLTVCamera::Reset's defaults.
        director = [];

        // Live tracks by slot, plus every track ever started. A slot is reused when its occupant
        // is destroyed, so the two are not the same list - keeping only the live ones would lose
        // every rocket the moment the next one took its index.
        tracks = [];
        props = [];
        playerTracks = [];
        viewCommands = [];
        userCommands = [];
        packetAcknowledgements = [];
        recorderSlot = null;
        maxClients = 0;
        mapCrc = null;
        isSourceTv = false;
        mapHash = null;

        // **What the server had its replicated ConVars set to** (D106). Built here rather than by
        // a caller because the values arrive as messages in this stream and nowhere else, and the
        // engine applies them the same way: at signon, and again whenever one changes mid-match.
        serverConVars = new();
        conVarHistory = [];
        viewmodels = [];
        fogSamples = [];
        fogControllersSeen = 0;
        fogProperties = 0;

        // What each viewmodel entity last said, so an unchanged one is not recorded again. Keyed
        // by entity because a player carries two and they interleave.
        lastViewmodel = [];

        // Who is in which slot now, and everybody who ever played. Two maps because a slot reused
        // by a later joiner overwrites the first occupant — see `RosterBuilder`.
        bySlot = [];
        everyone = [];

        // The game events, each sharing one copy of the roster until the roster next changes.
        gameEvents = [];
        userMessages = [];
        voice = [];
        voiceInits = [];
        spectatorGuiChanges = [];
        rosterAtEvent = null;

        walked = 0;
        reportEvery = Math.Max(1, commands.Count / ProgressReports);
        }

        /// <summary>Walks commands, continuing from wherever the pass stands.</summary>
        /// <param name="batch">The next commands of the demo, in order.</param>
        public void Run(IEnumerable<DemoCommand> batch)
        {
        foreach (DemoCommand command in batch)
        {
            if (progress is not null && walked++ % reportEvery == 0)
            {
                progress((double)walked / commands.Count);
            }

            // **The two commands that end `ParseAheadForInterval`** (B56): kept where they sit among the packets.
            if (command.Type is DemoCommandType.SyncTick or DemoCommandType.Stop)
            {
                viewCommands.Add(new DemoViewCommand(command.Type, command.Tick, default));
            }

            // **The recorder's input, which prediction re-runs** (D205); a SourceTV demo records none.
            if (command.Type == DemoCommandType.UserCmd && command.Prologue.Length >= sizeof(int))
            {
                userCommands.Add(RecordedUserCommand.From(command));
            }

            if (command.Type == DemoCommandType.Packet && RecordedUserCommand.Acknowledged(command.Prologue.Span) is { } acknowledged)
            {
                packetAcknowledgements.Add((command.Tick, acknowledged));
            }

            // **The tables as they stood when recording began** (B452) are walked like a packet of
            // creates: the engine rebuilds every client table from the block, and a recording
            // started mid-match finds a player baseline, a precache or a roster entry only here.
            bool block = command.Type == DemoCommandType.StringTables;

            if (!block && command.Type is not (DemoCommandType.Signon or DemoCommandType.Packet))
            {
                continue;
            }

            // **The recorder's own camera, kept while the file is already open.** A viewer asking
            // for it per frame cannot re-walk a 39 MB demo, and this loop is the only pass over
            // the commands there is. Every packet, zeroed ones included: the demo player's current
            // view is the last packet read, and a zeroed one is the engine's "set nothing".
            if (!block)
            {
                viewCommands.Add(new DemoViewCommand(
                    command.Type,
                    command.Tick,
                    command.Prologue.Length >= RecordedView.SizeBytes ? RecordedView.Parse(command.Prologue.Span) : default));
            }

            bool moved = false;

            // The events due on frames between the last packet and this one, against the last packet (B437).
            if (command.Type == DemoCommandType.Packet)
            {
                FireGestures(
                    queuedGestures, command.Tick - 1, interval, entities, AlwaysLoser(serverConVars), classes, gestures);
            }

            // Per packet, because "did the demo mention this entity" is a question about THIS
            // packet and nothing earlier.
            touchedEntities.Clear();

            // Materialised so the decode is timed here rather than being spread through the
            // switch below by lazy enumeration, which would attribute it to whichever case
            // happened to pull the next message.
            long readFrom = Stopwatch.GetTimestamp();

            IReadOnlyList<INetMessage> messages = block
                ? DemoStringTables.AsCreates(command.Payload.Span)
                : [.. NetMessageReader.Read(command.Payload.Span, state).Messages];

            messageTicks += Stopwatch.GetTimestamp() - readFrom;

            foreach (INetMessage message in messages)
            {
                // **Instance baselines, because an entering entity is a delta against its class.**
                // Applying them changed no count on any file in the corpus, era or modern - but
                // "it changed nothing measurable here" is not evidence that it never will, and the
                // format says an entity entering the visible set is sent against this rather than
                // in full.
                switch (message)
                {
                    // **The server's own tick rate, not a constant.** Early servers ran 33 tick,
                    // and a demo replayed at the wrong rate looks like a slow or fast server
                    // rather than like a defect.
                    case ServerInfoMessage server when server.IntervalPerTick > 0f:
                        interval = server.IntervalPerTick;

                        // **Which map, and WHICH VERSION of it.** The name alone does not identify
                        // a map — `cp_badlands` in 2017 is not `cp_badlands` in 2026 — and the CRC
                        // is what the engine uses to tell a client its `.bsp` is the wrong one.
                        // Decoded since the container work and compared against nothing until
                        // D113, which cost an evening of chasing rendering defects that were a
                        // mismatched map (finding 41).
                        mapCrc = server.MapCrc;
                        isSourceTv = server.IsSourceTv;
                        mapHash = server.MapHash;

                        // **Which entity the recording was made from**, named by the demo rather
                        // than inferred. A first-person camera needs the recorder's class for the
                        // eye height, and picking whichever player moves like the camera would be
                        // an instrument that agrees with its own hypothesis.
                        recorderSlot = server.PlayerSlot;

                        // `cl.m_nMaxClients`, which decides InterpolateViewpoint's rollback (B56).
                        maxClients = server.MaxPlayers;
                        continue;

                    // **What the server changed, which for a mod is the whole of how it plays.**
                    // Decoded and round-tripped since the container work and consumed by nothing
                    // until D106 — so a server that raised `sv_maxspeed` sent the value, this
                    // project read it correctly, and every reader used a baked constant instead.
                    case SetConVarMessage convars:
                        serverConVars.Apply(convars);
                        conVarHistory.Add((command.Tick, serverConVars.Snapshot()));
                        continue;

                    // **The director telling the camera how to frame this shot** — `hltv_chase`,
                    // which `C_HLTVCamera::FireGameEvent` (`hltvcamera.cpp:776`) reads to set the
                    // mode, both targets, and every chase parameter. Without it the chase camera
                    // runs on `Reset`'s defaults for ever, which is what it did until now: a demo
                    // could ask for a wider shot or a different angle and be ignored.
                    case GameEventMessage { Name: DirectorShot.ChaseEvent } chase:
                        director.Add((command.Tick, DirectorShot.From(chase.Values, director.Count > 0 ? director[^1].Shot : null)));
                        continue;

                    // **Every other event, for the HUD's listeners** — the death notices first. The
                    // roster goes with it as it stands now, because the listener resolves user ids and
                    // names while handling the event and a slot can hold someone else later.
                    case GameEventMessage { Name: { } eventName } gameEvent:
                        rosterAtEvent ??= new Dictionary<int, PlayerInfo>(bySlot);
                        gameEvents.Add(new SceneGameEvent(command.Tick, eventName, gameEvent.Values, rosterAtEvent));
                        continue;

                    // **The chat's user messages, bodies kept** — the reader parses `SayText2` into a chat line, but the
                    // HUD needs what `CBaseHudChat::MsgFunc_SayText2` reads, colour codes and all.
                    // **Voice, for the client's voice path** — `CClientState::ProcessVoiceData` and `Voice_Init`.
                    case VoiceDataMessage { BodyBits: > 0 } spoken:
                        voice.Add(new SceneVoice(command.Tick, spoken.Client, spoken.Proximity != 0, spoken.Body, voiceInits.Count - 1));
                        continue;

                    case VoiceInitMessage init:
                        voiceInits.Add(new SceneVoiceInit(command.Tick, init.Codec, init.Quality, init.SampleRate));
                        continue;

                    case ChatMessage chat:
                        userMessages.Add(new SceneUserMessage(command.Tick, SceneUserMessage.SayText2, chat.Body));
                        continue;

                    case UserMessage { UserMessageType: SceneUserMessage.SayText or SceneUserMessage.SayText2 or SceneUserMessage.TextMsg } user:
                        userMessages.Add(new SceneUserMessage(command.Tick, user.UserMessageType, user.Body));
                        continue;

                    // **`ShowViewPortPanel( "specgui", ... )`** — the server opening or closing the spectator GUI for the
                    // recorder (player.cpp:2260, :2294; tf_player.cpp:13992 after the freeze cam), as `__MsgFunc_VGUIMenu`
                    // reads it (B516). SourceTV is never sent one; it shows the panel itself at `game_newmap`.
                    case UserMessage { Name: SpectatorGuiMenu, Fields: { } menu }
                        when menu.Any(field => field is { Key: "panel", Value: "specgui" }):
                        spectatorGuiChanges.Add((command.Tick, menu.Any(field => field is { Key: "show", Value: true })));
                        continue;

                    case UserMessage { Name: SceneUserMessage.PlayerPickupWeapon } pickup:
                        userMessages.Add(new SceneUserMessage(command.Tick, pickup.UserMessageType, pickup.Body) { Name = pickup.Name });
                        continue;

                    // **A create — and the dem_stringtables block, read as one — is a fresh table**
                    // (`NameTable.Replace`, B452): every create below replaces, every update merges.
                    case CreateStringTableMessage { Name: BaselineBuilder.TableName } create:
                        BaselineBuilder.Replace(create.Entries, decoder);
                        continue;

                    case UpdateStringTableMessage update
                        when state.StringTableName(update.TableId) == BaselineBuilder.TableName:
                        BaselineBuilder.Apply(update.Entries, decoder);
                        continue;

                    // Which model each m_nModelIndex names. Without this every entity carrying a
                    // model resolves to nothing and the scene is players on an empty map.
                    case CreateStringTableMessage { Name: ModelPrecache.TableName } models:
                        precache.Replace(models.Entries);
                        continue;

                    // **The same arrangement for sounds, and it needs both messages.** A table is
                    // created once and then updated as the round goes on — a sound first played
                    // mid-match is added by an update, so handling only the create resolves the
                    // opening minute and nothing after it.
                    // **The roster, so a person can be named rather than counted.** A viewer
                    // spectating an STV recording has eighteen players to choose between and no way
                    // to say which; an entity index is a number nobody knows. The owner's own
                    // framing: *"allow the input of a player name or id to first person cam a
                    // specific player on boot"*.
                    //
                    // Both halves are read, for the reason `RosterBuilder` gives: the slot map says
                    // who is here NOW and the by-user-id map says who PLAYED, and a slot reused by
                    // a later joiner silently loses the first occupant from the former.
                    case CreateStringTableMessage { Name: RosterBuilder.TableName } roster:
                        RosterBuilder.Replace(roster.Entries, bySlot, everyone);
                        rosterAtEvent = null;
                        continue;

                    case UpdateStringTableMessage rosterUpdate
                        when state.StringTableName(rosterUpdate.TableId) == RosterBuilder.TableName:
                        RosterBuilder.Apply(rosterUpdate.Entries, bySlot, everyone);
                        rosterAtEvent = null;
                        continue;

                    // **Which compiled scene each `m_nSceneStringIndex` names**, and the only thing
                    // on the wire that says which taunt played (B351). Both messages, for the
                    // reason the sounds table gives: a table is created once and added to as the
                    // round goes on.
                    case CreateStringTableMessage { Name: ScenePrecache.TableName } sceneTable:
                        scenePrecache.Replace(sceneTable.Entries);
                        continue;

                    case UpdateStringTableMessage sceneUpdate
                        when state.StringTableName(sceneUpdate.TableId) == ScenePrecache.TableName:
                        scenePrecache.Apply(sceneUpdate.Entries);
                        continue;

                    case CreateStringTableMessage { Name: SoundNames.TableName } soundTable:
                        soundNames.Add(soundTable);
                        continue;

                    case UpdateStringTableMessage soundUpdate
                        when state.StringTableName(soundUpdate.TableId) == SoundNames.TableName:
                        soundNames.Add(soundUpdate, SoundNames.TableName);
                        continue;

                    // **Every sound the server plays, placed at the tick it was sent on.** Decoded
                    // here rather than at playback because the body is a delta-compressed bit
                    // stream: each sound is written against the one before it, so it can only be
                    // read in order and only once.
                    case SoundsMessage { Count: > 0 } played:
                        foreach (DecodedSound sound in SoundDecoder.Decode(
                            played.Body.Span, played.Count, played.BodyBits, (ushort)protocol))
                        {
                            sounds.Add(new SceneSound(
                                command.Tick,

                                // Empty rather than dropped when the number names nothing: an
                                // unresolvable sound still happened, and discarding it would make a
                                // gap in the table look like silence in the recording.
                                soundNames.Resolve(sound.SoundNumber) ?? string.Empty,
                                sound.SoundNumber,
                                sound.EntityIndex,
                                sound.Channel,
                                sound.Volume,
                                sound.SoundLevel,
                                sound.Pitch,
                                sound.DelaySeconds,
                                sound.OriginX,
                                sound.OriginY,
                                sound.OriginZ,
                                sound.IsAmbient,
                                (sound.Flags & SoundDecoder.StopFlag) != 0,
                                command.Type == DemoCommandType.Signon)
                            {
                                ChangesVolume = (sound.Flags & SoundDecoder.ChangeVolumeFlag) != 0,
                                ChangesPitch = (sound.Flags & SoundDecoder.ChangePitchFlag) != 0,

                                // `SND_IGNORE_PHONEMES` (soundflags.h:128): the mouth skips this one (B513).
                                IgnoresPhonemes = (sound.Flags & IgnorePhonemesFlag) != 0,

                                // `nSpeakerEntity` (soundinfo.h:366): the mouth the mixer moves when not -1 (B513).
                                SpeakerEntity = sound.SpeakerEntity,
                            });
                        }

                        continue;

                    // **The SERVER's tick, which is not the demo's** (B273). `net_Tick` carries
                    // `gpGlobals->tickcount` as the server had it, and that is the number
                    // `m_flSimulationTime`'s offset was encoded against — a demo's own command tick
                    // starts near zero while a server has been up for hours.
                    // **The SERVER's tick, which is not the demo's.** `net_Tick` carries
                    // `gpGlobals->tickcount` as the server had it — the number
                    // `m_flSimulationTime` and `m_flAnimTime` are encoded against — while a demo's
                    // own commands are numbered from the start of the recording. The two axes are
                    // never mixed: what leaves this decode is a LAG, the difference between two
                    // server-axis numbers, which is then applied to a demo tick (B273).
                    case NetTickMessage netTick:
                        entities.PacketTick = netTick.Tick;
                        continue;

                    // **A player's gestures arrive here and nowhere else** (B282). TF2 excludes
                    // `overlay_vars` from the player's send table (`tf_player.cpp:774`), so the
                    // animation layers a reload or a flinch would occupy are not on the wire at
                    // all; what IS sent is `CTEPlayerAnimEvent` (`tf_player.cpp:324`), a temp
                    // entity naming the player and a `PlayerAnimEvent_t`.
                    //
                    // **The posture is read when the event FIRES** (B437), because the engine picks the
                    // activity inside `DoAnimationEvent` (`tf_playeranimstate.cpp:969`), which `CL_FireEvents`
                    // calls an interpolation window after arrival — queued here, fired by FireGestures.
                    case TempEntitiesMessage effects when effects.BodyBits > 0:
                        RecordEffects(
                            decoder,
                            effects,
                            command.Tick,
                            interval,
                            client.Amount(serverConVars),
                            effectClassNames,
                            entities,
                            queuedGestures,
                            feeds);
                        continue;

                    // **A rope's impulse** (`C_RopeKeyframe::ReceiveMessage`, `c_rope.cpp:2082-2095`, B478): dispatched to
                    // the entity at that index, and taken only when the class id is the rope's own.
                    case EntityMessage entityMessage:
                        if (entities.TryGet(entityMessage.EntityIndex, out EntityState? addressed))
                        {
                            ropeImpulses.Record(entityMessage, addressed.ClassName, addressed.ClassId, command.Tick);
                        }

                        continue;

                    // **The decal names**, which `m_nIndex` points into. Both messages, as every precache table needs.
                    case CreateStringTableMessage { Name: DecalFeed.TableName } decalTable:
                        feeds.Decals.Names.Replace(decalTable.Entries);
                        continue;

                    case UpdateStringTableMessage decalUpdate
                        when state.StringTableName(decalUpdate.TableId) == DecalFeed.TableName:
                        feeds.Decals.Names.Apply(decalUpdate.Entries);
                        continue;

                    // **The effect names**, which `m_iEffectName` points into.
                    case CreateStringTableMessage { Name: EffectDispatchFeed.TableName } effectTable:
                        feeds.Dispatches.Names.Replace(effectTable.Entries);
                        continue;

                    case UpdateStringTableMessage effectUpdate
                        when state.StringTableName(effectUpdate.TableId) == EffectDispatchFeed.TableName:
                        feeds.Dispatches.Names.Apply(effectUpdate.Entries);
                        continue;

                    // **The particle system names**, which a `"ParticleEffect"` dispatch's `m_nHitBox` points into.
                    case CreateStringTableMessage { Name: EffectDispatchFeed.ParticleTableName } particleTable:
                        feeds.Dispatches.ParticleNames.Replace(particleTable.Entries);
                        continue;

                    case UpdateStringTableMessage particleUpdate
                        when state.StringTableName(particleUpdate.TableId) == EffectDispatchFeed.ParticleTableName:
                        feeds.Dispatches.ParticleNames.Apply(particleUpdate.Entries);
                        continue;

                    // **The light style patterns**, which animate the world's switchable and flickering lights.
                    case CreateStringTableMessage { Name: LightStyleFeed.TableName } styleTable:
                        lightStyles.Replace(styleTable.Entries);
                        continue;

                    case UpdateStringTableMessage styleUpdate
                        when state.StringTableName(styleUpdate.TableId) == LightStyleFeed.TableName:
                        // A signon packet's tick is not a playback tick (`koth_harvest_event` stamps its creation
                        // 7,170), so what arrives there holds from the start.
                        lightStyles.Apply(
                            styleUpdate.Entries, command.Type == DemoCommandType.Signon ? null : command.Tick);
                        continue;

                    case UpdateStringTableMessage update
                        when state.StringTableName(update.TableId) == ModelPrecache.TableName:
                        precache.Apply(update.Entries);
                        continue;

                    // The second model table, which is where every cosmetic lives. A negative
                    // m_nModelIndex is a dynamic model, and the even ones are networked through
                    // here - see ModelPrecache.Path.
                    case CreateStringTableMessage { Name: ModelPrecache.DynamicTableName } dynamic:
                        precache.ReplaceDynamic(dynamic.Entries);
                        continue;

                    case UpdateStringTableMessage update
                        when state.StringTableName(update.TableId) == ModelPrecache.DynamicTableName:
                        precache.ApplyDynamic(update.Entries);
                        continue;

                    default:
                        break;
                }

                // Stryker disable once : a mutant that empties the guard body leaves 'snapshot'
                // unassigned (CS0165), and Safe Mode then drops every mutation in this method — B410.
                if (message is not PacketEntitiesMessage snapshot || snapshot.LengthBits <= 0)
                {
                    continue;
                }

                long entityFrom = Stopwatch.GetTimestamp();

                foreach (DecodedEntity entity in
                    decoder.Decode(snapshot.Body.Span, snapshot, snapshot.LengthBits))
                {
                    entities.Apply(entity);
                    dormancy.Observe(entity.EntityIndex, entity.UpdateType, command.Tick, entity.SerialNumber);
                    pointCameras.Observe(
                        entity.EntityIndex,
                        entities.TryGet(entity.EntityIndex, out EntityState? observed) ? observed?.ClassName ?? string.Empty : string.Empty,
                        entity.UpdateType);
                    tonemap.Observe(
                        entity.EntityIndex,
                        entities.TryGet(entity.EntityIndex, out EntityState? controller) ? controller : null,
                        entity.UpdateType);
                    suns.Observe(
                        entity.EntityIndex,
                        entities.TryGet(entity.EntityIndex, out EntityState? sun) ? sun : null,
                        entity.UpdateType);

                    touchedEntities.Add(entity.EntityIndex);

                    if (entity.UpdateType is EntityUpdateType.Leave or EntityUpdateType.Delete)
                    {
                        healBeams.Leave(entity.EntityIndex, command.Tick);
                    }

                    if (combatWeapons.Contains(entity.ClassId) &&
                        entities.TryGet(entity.EntityIndex, out EntityState? weaponState))
                    {
                        if (entity.UpdateType is not (EntityUpdateType.Leave or EntityUpdateType.Delete) &&
                            string.Equals(weaponState.ClassName, MedigunClass, StringComparison.Ordinal))
                        {
                            healBeams.Observe(
                                entity.EntityIndex,
                                entity.UpdateType == EntityUpdateType.Enter,
                                EntityState.Slot(weaponState.Integer(HealBeamFeed.TargetKey)),
                                weaponState.Integer(HealBeamFeed.ChargeReleaseKey) is 1,
                                weaponState.Integer("DT_BaseEntity.m_iTeamNum") ?? 0,
                                command.Tick,
                                weaponState.ItemDefinitionIndex(),
                                weaponState.Owner(),
                                weaponState.Integer(HealBeamFeed.HealingKey) is { } healingBit ? healingBit != 0 : null);
                        }

                        muzzleFlashes.Observe(
                            entity.EntityIndex,
                            entity.UpdateType == EntityUpdateType.Enter,
                            isWeapon: true,
                            weaponState.Integer(MuzzleFlashFeed.ParityKey),
                            weaponState.ItemDefinitionIndex(),
                            weaponState.Integer("DT_BaseEntity.m_iTeamNum") ?? 0,
                            command.Tick,
                            weaponState.Owner());
                    }

                    // **The first-person flash travels in the VIEWMODEL's own counter** (B415), which the server bumps beside
                    // the weapon's and sends only to the owner.
                    if (entities.TryGet(entity.EntityIndex, out EntityState? viewmodelState) &&
                        viewmodelState.ViewmodelMuzzleFlashParity() is { } viewmodelParity)
                    {
                        int? flashing = viewmodelState.ViewmodelWeapon();
                        EntityState? held = flashing is { } w && entities.TryGet(w, out EntityState? found) ? found : null;

                        muzzleFlashes.ObserveViewmodel(
                            entity.EntityIndex,
                            entity.UpdateType == EntityUpdateType.Enter,
                            viewmodelParity,
                            flashing,
                            viewmodelState.ViewmodelOwner(),
                            held?.ItemDefinitionIndex(),
                            held?.Integer("DT_BaseEntity.m_iTeamNum") ?? 0,
                            command.Tick);
                    }

                    // **Noticed here, where the cost is proportional to what the demo said**
                    // (B265). An entity is a viewmodel for its whole life, so this asks once per
                    // update rather than once per entity per packet — and the sampler below then
                    // visits twenty-two entities instead of six hundred. Kept ascending so the
                    // recorded order matches what walking `All` produced.
                    if (entities.TryGet(entity.EntityIndex, out EntityState? applied) &&
                        applied.ViewmodelModelIndex() is not null &&
                        !viewmodelEntities.Contains(entity.EntityIndex))
                    {
                        int seat = viewmodelEntities.BinarySearch(entity.EntityIndex);

                        viewmodelEntities.Insert(seat < 0 ? ~seat : seat, entity.EntityIndex);
                    }

                    RecordProp(
                        entity, entities, precache, tracks, props, playerTracks,
                        mergesItself, combatWeapons, protocol, command.Tick, interval,
                        simulationLag, animationLag, clockGap, lagByClass, corpses, replaced,
                        lastHeld);
                }

                entityTicks += Stopwatch.GetTimestamp() - entityFrom;

                moved = true;
            }

            // **After the packet's messages, not before.** Sampling first reads the table as it
            // stood at the PREVIOUS tick, so an entity that enters on this packet is missed
            // entirely — and on a demo whose viewmodel enters once and never changes, that means
            // it is never recorded at all.
            long sampleFrom = Stopwatch.GetTimestamp();

            RecordViewmodels(
                entities, precache, protocol, command.Tick, lastViewmodel, viewmodels,
                viewmodelEntities, touchedEntities, viewmodelWeapon);

            viewmodelTicks += Stopwatch.GetTimestamp() - sampleFrom;

            // **Sampled here for the same reason and recorded only on CHANGE.** A fog controller
            // sends its whole state on entry and then rarely again, so a keyframe per tick would be
            // tens of thousands of identical entries; a keyframe per change is a handful, and the
            // lookup walks forward keeping the last one exactly as the viewmodels do.
            foreach (EntityState entity in entities.All)
            {
                if (entity.ClassName == FogControllerClass)
                {
                    fogControllersSeen++;

                    // **The controller's property count, which is the number that diagnosed B132.**
                    // Zero here while a trace of the same demo shows the entity entering with
                    // fifteen properties is the whole finding, and it is cheap enough to keep.
                    fogProperties = Math.Max(fogProperties, entity.Properties.Count);
                }

                // **The soundscape a player is standing in, sampled the same way and for the same
                // reason.** It is per-player private data — `m_audio` lives in `DT_Local`, sent
                // through `SendProxy_SendLocalDataTable`'s `SetOnly( objectID - 1 )` — so at most
                // one entity in a recording carries it, and only a point-of-view recording has one
                // at all. Whichever entity has it is the one whose ears the demo was recorded from.
                if (entity.SoundscapeIndex() is { } soundscape)
                {
                    SceneSoundscape sampled = new(
                        soundscape,
                        entity.SoundscapePositionBits() ?? 0,
                        Positions(entity),
                        entity.SoundscapeEntity() ?? -1);

                    // **One sample per tick: the last.** A SourceTV demo can carry `m_audio` on several players
                    // (f12: 1,055,037 samples, 281 MB, for 96,589 frames), and alternating between them defeated the
                    // dedup below, storing every player's every tick. `SoundscapeAt` keeps the last sample at or
                    // before a tick, so a sample superseded within its own tick could never be returned (D217).
                    if (soundscapes.Count > 0 && soundscapes[^1].Tick == command.Tick)
                    {
                        soundscapes.RemoveAt(soundscapes.Count - 1);
                    }

                    if (soundscapes.Count == 0 || soundscapes[^1].Soundscape != sampled)
                    {
                        soundscapes.Add((command.Tick, sampled));
                    }
                }

                // **The fog used to be read here, from the first enabled controller, with a `break`
                // after it** — which also stopped this walk before any entity after the controller
                // was asked for its soundscape. The view's fog is chosen by the local player's
                // handle instead, below (B139).
            }

            // The events due on this packet's tick, once it is applied and before its HandleJumping (B437).
            FireGestures(queuedGestures, command.Tick, interval, entities, AlwaysLoser(serverConVars), classes, gestures);

            // **The view's fog, as `UpdateFogController` chooses it** (B139), recorded on change —
            // null included, because a player whose handle leaves every controller loses fog.
            ViewFog viewFog = ViewFog.From(entities);

            // The camera `DrawMonitors` would leave in `_rt_Camera` after this packet (B511), on change.
            tonemap.Sample(command.Tick);
            suns.Sample(command.Tick, index => precache.Path(ModelPrecache.Unpack(index, protocol)));
            pointCameras.Sample(
                command.Tick,
                index => entities.TryGet(index, out EntityState? camera) ? camera : null,
                index => dormancy.IsDormant(index, command.Tick));

            if (fogSamples.Count == 0 ? viewFog != default : fogSamples[^1].Fog != viewFog)
            {
                fogSamples.Add((command.Tick, viewFog));
            }

            // **Every scene entity, on every tick, and recorded only on the TRANSITION to playing.**
            // `C_SceneEntity::OnDataChanged` compares `m_bWasPlaying` against `m_bIsPlayingBack` and
            // starts the scene on each actor only when they differ (`c_sceneentity.cpp:327`), so a
            // taunt held true for its whole length began at one tick. The scene index is part of the
            // key, because a slot reused by a second taunt is a second start.
            //
            // **Outside the `moved` gate on purpose.** That gate skips ticks where nothing the
            // sampler cares about changed, and a taunt beginning is exactly the kind of one-tick
            // event that would fall in the gap.
            foreach (EntityState entity in entities.OfClass(SceneEntityClass))
            {
                // Stryker disable all : the guard condition spans two lines, so a mutant that
                // empties the guard body leaves 'index' unassigned at its use below (CS0165), and
                // Safe Mode then drops every mutation in this method — B410.
                if (entity.ScenePlayingBack() is not true ||
                    entity.SceneStringIndex() is not { } index)
                {
                    // **The stop is a transition too, and it has to reach the actors.** The engine
                    // walks the same actor list on the way down — `ClearSceneEvents` then
                    // `RemoveChoreoScene` (`c_sceneentity.cpp:344`) — and what that does to the
                    // gesture depends on whether the scene loops, which only the archive knows.
                    bool wasPlaying = playingScenes.Remove(entity.EntityIndex, out int stopped);

                    if (wasPlaying)
                    {
                        // The stop, on the run it ends (B513): `StopPlayback` clears every actor's scene events.
                        int run = choreography.FindLastIndex(one => one.EntityIndex == entity.EntityIndex);

                        if (run >= 0 && choreography[run].StoppedTick is null)
                        {
                            choreography[run] = choreography[run] with { StoppedTick = command.Tick };
                        }
                    }

                    if (wasPlaying && scenePrecache.Path(stopped) is { Length: > 0 } was)
                    {
                        foreach (int actor in entity.SceneActors())
                        {
                            gestures.StopScene(actor, was, command.Tick * interval);
                        }
                    }

                    continue;
                }

                // Stryker restore all

                if (playingScenes.TryGetValue(entity.EntityIndex, out int already) &&
                    already == index)
                {
                    continue;
                }

                // A slot that switches scenes while playing ends the first run where the second begins (B513).
                if (playingScenes.ContainsKey(entity.EntityIndex))
                {
                    int previous = choreography.FindLastIndex(one => one.EntityIndex == entity.EntityIndex);

                    if (previous >= 0 && choreography[previous].StoppedTick is null)
                    {
                        choreography[previous] = choreography[previous] with { StoppedTick = command.Tick };
                    }
                }

                playingScenes[entity.EntityIndex] = index;

                // A scene whose name the table cannot give is still recorded as having started, with
                // an empty name: the tick and the actors are facts about the recording, and dropping
                // them would make a missing table look like a demo where nobody taunted.
                string scene = scenePrecache.Path(index) ?? string.Empty;
                IReadOnlyList<int> actors = entity.SceneActors();

                choreography.Add(new SceneChoreography(
                    command.Tick, entity.EntityIndex, scene, actors));

                // **Straight into the gesture feed, at the VCD slot the engine uses.** Every actor,
                // because a partner taunt animates both players and each one's own model resolves
                // the sequence separately — `DispatchStartGesture` is called per actor
                // (`c_sceneentity.cpp:511`).
                foreach (int actor in actors)
                {
                    gestures.RecordScene(actor, scene, command.Tick * interval);
                }
            }

            // **A scene entity deleted while playing has stopped too** (B513): `C_SceneEntity::UpdateOnRemove` unloads
            // the scene, clearing every actor's events. Only the run's stop is marked; the slot's record is left, so
            // the gesture keeps what it did before.
            foreach (int slot in playingScenes.Keys)
            {
                if (!entities.TryGet(slot, out _))
                {
                    int run = choreography.FindLastIndex(one => one.EntityIndex == slot);

                    if (run >= 0 && choreography[run].StoppedTick is null)
                    {
                        choreography[run] = choreography[run] with { StoppedTick = command.Tick };
                    }
                }
            }

            samplingTicks += Stopwatch.GetTimestamp() - sampleFrom;

            if (!moved)
            {
                continue;
            }

            long frameFrom = Stopwatch.GetTimestamp();
            List<ScenePlayer> players = [];
            EntityState? resource = entities.OfClass(ResourceClass).FirstOrDefault();

            // **The recorder's team, resolved BEFORE the loop that needs it.** `IsEnemyPlayer`
            // compares against the local player, and in a recording that is whoever recorded it.
            // Reading it from the entity table rather than from the list being built is what makes
            // the answer independent of the order players happen to be walked in — a first version
            // searched the partial list and reported every player below the recorder's entity index
            // as friendly, whatever their team.
            //
            // In a SourceTV recording this is the SourceTV client's own team, spectator: the
            // engine's switch falls through to `return false`, so a spectator sees every spy
            // undisguised.
            int? recorderTeam =
                recorderSlot is { } recording
                && entities.TryGet(recording + 1, out EntityState? recorder)
                    ? Coached(entities, recorder, resource)
                        ?? ResourceTeam(resource, recording + 1)
                        ?? First(recorder, TeamProperties)
                    : null;

            // Asked once a frame rather than per player: the round state and the ConVar are the frame's.
            EntityState? rulesNow = entities.OfClass(GameRulesClass).FirstOrDefault();
            bool alwaysLoserNow = AlwaysLoser(serverConVars);

            foreach (EntityState player in entities.OfClass(PlayerClass))
            {
                // **Remembered before the visibility guard, because a dying player fails it.**
                // The value is wanted at the moment of death and is absent exactly then, so the
                // useful reading is the last one from while he was alive (B395).
                if (entities.Resolve(player.ActiveWeaponHandle()) is { } holding)
                {
                    lastHeld[player.EntityIndex] = holding;
                }

                // `SyncConditions` runs on every update the local player receives, dormant or not; its team is
                // `m_pOuter->GetTeamNumber()`, the entity's own.
                if (player.EntityIndex == recorderSlot + 1)
                {
                    PlayerConditions conditionsNow = player.Conditions();

                    screenOverlay = ScreenOverlay.Step(
                        screenOverlay, overlayConditions, conditionsNow, First(player, TeamProperties));
                    overlayConditions = conditionsNow;
                }

                // **A dormant player's animation state is cleared, every frame it stays dormant** (B112). The client
                // keeps a dormant player in its animation list, and `Update` finds `IsDormant()` and calls
                // `ClearAnimationState` (`multiplayer_animstate.cpp:1390`, `tf_playeranimstate.cpp:370-374`).
                if (!player.IsVisible)
                {
                    gestures.ClearAnimationState(player.EntityIndex);
                    continue;
                }

                // Stryker disable once : a mutant that empties the guard body leaves 'origin's
                // fields unassigned (CS0170), and Safe Mode then drops every mutation in this
                // method — B410.
                if (player.Origin() is not { } origin)
                {
                    continue;
                }

                // The resource's arrays are keyed by entity index, zero padded to three digits.
                string slot = player.EntityIndex.ToString("D3", CultureInfo.InvariantCulture);

                // **HandleJumping's answer, stepped here because only this loop sees the ticks in order** (B437).
                PlayerActivity? jumpActivity = null;

                // **The burn clock, on the same reasoning and in the same loop** (B336). Null when
                // the player is not alight, which is what `m_flBurnEffectStartTime == 0` means to
                // `CProxyBurnLevel` — it defaults `flBurnStartTime` to 0 and does nothing at all
                // unless the value is above it.
                float? burningFor = null;

                if (player.Conditions().Has(PlayerConditions.Burning))
                {
                    if (!burningSince.TryGetValue(player.EntityIndex, out int caught))
                    {
                        caught = command.Tick;
                        burningSince[player.EntityIndex] = caught;
                    }

                    // Null while the interval is unknown, exactly as the jump clock is: the first
                    // frames arrive before `net_tick` states one, and a zero interval would peg
                    // every burn at its own first instant for ever.
                    burningFor = interval > 0f ? (command.Tick - caught) * interval : null;
                }
                else
                {
                    // `OnRemoveBurning` clears the start time, so a second burn is a new clock
                    // rather than a continuation of the first.
                    burningSince.Remove(player.EntityIndex);
                }

                // **The discontinuity parity, watched for a CHANGE** (B346). The first value seen
                // is not one: a player entering the world already carries whatever parity it had,
                // and treating that as a jump would clear the transition queue for every player on
                // the frame they appear. Measured: 12,929 of 13,261 sends are on an ENTER update.
                if (player.NoInterpolationParity() is { } noInterp)
                {
                    if (jumpParity.TryGetValue(player.EntityIndex, out int wasParity) &&
                        noInterp != wasParity)
                    {
                        jumpedAt[player.EntityIndex] = command.Tick;
                    }

                    jumpParity[player.EntityIndex] = noInterp;
                }

                // **The rise, differenced from the last height this player was seen at.** Only the
                // upward component matters: the air-walk test is on velocity.z alone.
                float? rising = null;

                // How fast in all three dimensions, which is what the feet-yaw test uses:
                // `vecVelocity.Length() > 1.0f` rather than the horizontal speed the activity
                // choice uses.
                float travelling = 0f;

                // `GetOuterXYSpeed()`, the same differencing without the vertical: what HandleMoving's cancel asks.
                float horizontal = 0f;

                if (lastHeight.TryGetValue(
                        player.EntityIndex, out (int Tick, float X, float Y, float Z) before) &&
                    command.Tick > before.Tick &&
                    interval > 0f)
                {
                    float elapsed = (command.Tick - before.Tick) * interval;

                    rising = (origin.Z - before.Z) / elapsed;

                    float acrossX = origin.X - before.X;
                    float acrossY = origin.Y - before.Y;
                    float upward = origin.Z - before.Z;

                    travelling = MathF.Sqrt(
                        (acrossX * acrossX) + (acrossY * acrossY) + (upward * upward)) / elapsed;
                    horizontal = MathF.Sqrt((acrossX * acrossX) + (acrossY * acrossY)) / elapsed;
                }

                lastHeight[player.EntityIndex] = (command.Tick, origin.X, origin.Y, origin.Z);

                // **A dead player's origin is not where they died — it is where they are
                // WATCHING.** The entity follows whoever they spectate, so drawing a corpse at its
                // current origin puts it standing inside a living player, and several of them
                // stack into one heap.
                //
                // So the last position held while alive is kept and used until they respawn, which
                // leaves a body roughly where it fell. TF2 leaves a ragdoll there; this is a
                // standing stand-in for one until ragdolls are simulated (B58).
                int? life = player.LifeState();
                bool alive = life is null or 0;

                // **`CTFPlayerAnimState::Update`'s own gate, ahead of anything `HandleJumping` does** (B112). A custom
                // model without the class's animations (`tf_playeranimstate.cpp:340-366`), `EF_NODRAW` or death
                // (`multiplayer_animstate.cpp:1381-1395`) mean the frame is not animated at all: `ClearAnimationState`
                // runs instead, which clears the air-walk, the jump and every gesture slot.
                bool firingHeavy =
                    player.PlayerClass() == HeavyClass && player.Conditions().Has(PlayerConditions.Aiming);

                // **The player's own activity table, chosen every frame `TranslateActivity` runs** (B437), from the
                // same `IsLoser` the gestures ask and the carry flag the HUD reads. `HandleJumping`'s duck asks it too.
                PlayerActivityOverride activityTable = ActivityTableOf(player, rulesNow, alwaysLoserNow);

                // **What TF's HandleDucking and HandleMoving ask beyond the flags** (B437): the real `IsLoser()`,
                // `IsAiming()`, the air dash and the weapon. The deployed hold is stepped below.
                EntityState? wielded = entities.Resolve(player.ActiveWeaponHandle()) is { } wieldedSlot &&
                    entities.TryGet(wieldedSlot, out EntityState? wieldedWeapon)
                        ? wieldedWeapon
                        : null;
                TfPosture posture = new(
                    IsLoser: IsLoser(player, rulesNow, alwaysLoserNow),
                    IsAiming: IsAiming(player, wielded?.ClassName),
                    AimsMinigun: string.Equals(wielded?.ClassName, MinigunClass, StringComparison.Ordinal),
                    AirDashing: (player.Integer(AirDashProperty) ?? player.Integer(LaunchAirDashProperty) ?? 0) > 0);

                if (!player.IsDrawn || !alive || player.CustomModelWithoutClassAnimations())
                {
                    gestures.ClearAnimationState(player.EntityIndex);
                }
                else if (player.Flags() is { } stateFlags)
                {
                    // **`CTFPlayerAnimState::HandleJumping`, stepped once a tick as the engine steps it once a frame**
                    // (B112, B437): the air-walk latch the body and the reload both read, and — only when the air-walk
                    // block does not run — the jump, timed from the jump EVENT (`m_flJumpStartTime`) rather than from
                    // the ground flag, so a rocket jump or a fall is airborne without jumping. Both clears make the
                    // landing gesture themselves, because a demo carries no event for it. The rise is the differenced
                    // height above, the client's own `EstimateAbsVelocity`; a heavy spinning his minigun returns before
                    // either (`tf_playeranimstate.cpp:1439-1440`); a grappling hook whose handle resolves keeps the
                    // block alive with no rise at all (`:1446`). The class script is the installed game's, carried in;
                    // without it the engine's default. Null while the interval is unknown, as before.
                    PlayerActivity? answered = gestures.HandleJumping(
                        player.EntityIndex,
                        rising,
                        stateFlags,
                        waistDeep: player.WaterLevel() >= PlayerActivityState.WaistDeepWaterLevel,
                        grappling: entities.Resolve(player.GrapplingHookTarget()) is not null,
                        firingHeavy: firingHeavy,
                        seconds: command.Tick * interval,
                        classes?.ScriptOf(player.PlayerClass()) ?? default,
                        ModelCrouchWalks(player, entities, classes, activityTable));

                    jumpActivity = interval > 0f ? answered : null;

                    // **`HandleMoving` runs only when nothing before it answered** (`CalcMainActivity`,
                    // `multiplayer_animstate.cpp:955-965`), and only then does moving cancel the deployed hold
                    // (`tf_playeranimstate.cpp:1301-1305`). The duck asked is the class model's, as above.
                    PlayerActivity reached = PlayerActivityState.For(
                        stateFlags,
                        horizontal,
                        player.WaterLevel() >= PlayerActivityState.WaistDeepWaterLevel,
                        alive,
                        answered,
                        posture with { LacksCrouchWalk = !ModelCrouchWalks(player, entities, classes, activityTable) });

                    if (answered is null && PlayerActivityState.IsMoving(reached))
                    {
                        gestures.StepMoving(player.EntityIndex, horizontal);
                    }

                    posture = posture with
                    {
                        HoldsDeployedPose = gestures.HoldsDeployedPose(player.EntityIndex, command.Tick * interval),
                    };
                }

                // **The yaw has to be carried here too, and was not.** Every argument below is
                // positional and the list stopped at LifeState, so Yaw took the record's default of
                // zero — every player in a frame faced due east regardless of where they were
                // looking. The track path gained eye angles and this one did not, which is the
                // shape a default has whenever it is also a legitimate value: nothing reports a
                // missing yaw, because zero IS a yaw.
                //
                // Read from the same place the track reads it, so the two cannot disagree: the eye
                // angles when the demo sends them, and m_angRotation when it does not. Normalised
                // to (−180, 180] like every other angle here, because the wire carries this one as
                // 0..360 — without it the same direction is held as two numbers a full turn apart,
                // measured as 220.997 against −139.003, and anything comparing or interpolating
                // them is wrong by 360 at the wrap.
                float facing = Normalize(
                    player.EyeAngles() is { } eyes ? eyes.Yaw : player.Angles()?.Yaw ?? 0f);

                // **How far up or down they are looking, which aims the torso.**
                // ComputePoseParam_AimPitch is one line — `SetPoseParameter( m_iAimPitch,
                // -flAimPitch )` where flAimPitch is m_flEyePitch — and body_pitch is what it sets.
                // Kept separate from the pose's own Pitch, which stays zero: a player model stands
                // upright however far the eyes are pitched, and rolling the whole body by the view
                // would lay them on their side every time they looked up.
                float? lookingAt = player.EyeAngles() is { } view ? Normalize(view.Pitch) : null;

                // **The feet, advanced once per tick, and only once the eye yaw is known.** They
                // follow the eyes while moving and stay planted while the player turns on the spot,
                // which is the whole difference between this and using the eye yaw as the body yaw.
                FeetYaw standing = feet.TryGetValue(player.EntityIndex, out FeetYaw known)
                    ? known
                    : default;

                standing.Advance(facing, travelling, interval > 0f ? interval : 0f);
                feet[player.EntityIndex] = standing;

                // **The weapon in hand, dereferenced once as `GetActiveWeapon()` does** (B105). `m_hActiveWeapon` is a
                // handle, and its serial is checked against the slot's occupant (B231), so a slot that has changed
                // hands names no weapon — and no item for `GetActivityWeaponRole` to read the `anim_slot` of — rather
                // than whatever moved in. Every field below that describes the weapon reads this one entity.
                int? activeSlot = entities.Resolve(player.ActiveWeaponHandle());
                EntityState? active = activeSlot is { } inHand && entities.TryGet(inHand, out EntityState? held)
                    ? held
                    : null;

                // **The dead are reported where the entity actually is, which is wherever they are
                // spectating from.** This used to hold the last living position and yaw so a body
                // stayed roughly where it fell, standing in for a ragdoll nobody had built yet.
                // That stand-in is gone: the engine does not draw a dead player at all, so there
                // was never a corpse for it to approximate, and holding the position meant the
                // timeline reported a coordinate the demo does not contain. A viewer that wants a
                // body waits for B58 and draws the CTFRagdoll entity, which is where the engine
                // keeps one.
                players.Add(new ScenePlayer(
                    player.EntityIndex,
                    origin.X,
                    origin.Y,
                    origin.Z,
                    resource?.Integer($"m_iTeam.{slot}") ?? First(player, TeamProperties),
                    resource?.Integer($"m_iHealth.{slot}") ?? First(player, HealthProperties),
                    resource?.Integer($"m_iPlayerClass.{slot}"),
                    LifeState: life,
                    // **The FEET, which is what the engine renders the body at** —
                    // `m_angRender[YAW] = m_flCurrentFeetYaw`. Equal to the eye yaw whenever the
                    // player is moving, so this changes nothing except while turning on the spot,
                    // where the feet should stay planted and the torso twist.
                    Yaw: standing.Current,
                    EyeYaw: facing,
                    AimYaw: standing.AimYaw(facing),

                    // Null on a POV demo for everyone but the recorder, because the send prop is in
                    // DT_LocalPlayerExclusive; a SourceTV recording carries it for every player.
                    Flags: player.Flags(),

                    // **What the engine itself uses to decide whether a view is first person**
                    // (B225). Unlike `Flags` above, this one is in `DT_BasePlayer` proper, so it
                    // arrives for everybody in either kind of recording. Without it a player who
                    // goes to spectator is indistinguishable from one still playing — spectating is
                    // not dying, so `LifeState` says nothing about it — and the viewer drew their
                    // last weapon over a free-roaming camera.
                    ObserverMode: player.ObserverMode(),

                    // Whose eyes an in-eye observer is in (B417).
                    ObserverTarget: player.ObserverTarget(),

                    // **EF_NODRAW, which is how the engine hides a corpse.** On death the server
                    // spawns a CTFRagdoll and then turns the player off with
                    // `AddEffects( EF_NODRAW | EF_NOSHADOW )` (tf_player.cpp:15637), so the body on
                    // screen is a different entity and the player itself is simply not drawn. TF2
                    // has no death animation to play instead: HandleDying is unreachable, because
                    // PLAYERANIMEVENT_DIE is raised nowhere in the game tree and its handler is an
                    // Assert(0).
                    //
                    // Read from the effects field rather than only from the life state, because
                    // that is what the engine tests. EF_NODRAW is also how a player is hidden while
                    // taunting into a cutscene or riding a teleporter, and life state cannot answer
                    // those.
                    //
                    // **The life state is ANDed in as well, and the reason is a gate in the
                    // engine rather than a shortcut here.** A dead player does not keep EF_NODRAW
                    // for the whole of their death: StateThinkDYING removes it again for the
                    // deathcam, commented `// still draw player body` (tf_player.cpp:13934). That
                    // whole branch is conditional on `m_hRagdoll` being non-null — the body is
                    // re-shown only once a corpse exists to justify it. We do not build ragdolls
                    // yet (B58), so that condition is false for every death we could render, and
                    // the engine's own answer for our situation is that the effect stays on.
                    //
                    // Measured on movement-test-stv-cp_process: 322 of 535 dead player-ticks
                    // carried EF_NODRAW on the wire and 213 did not, and the 213 are exactly the
                    // deathcam window above.
                    //
                    // When ragdolls land this becomes wrong and should follow EF_NODRAW alone.
                    Drawn: player.IsDrawn && alive,

                    // **The weapon, and its class resolved here while the table is in hand.** A
                    // handle is only an entity slot; what the animation needs is which weapon it
                    // is, and only this loop can see both. Resolved rather than carried as a bare
                    // index so no consumer has to keep the entity table alive to make sense of it.
                    JumpActivity: jumpActivity,

                    ActivityOverride: activityTable,
                    CompetitiveWinner: player.Conditions().Has(PlayerConditions.CompetitiveWinner),

                    // **In seconds, like every other clock on this record**, so the consumer
                    // compares stamps rather than converting ticks itself (B346).
                    DiscontinuitySeconds:
                        jumpedAt.TryGetValue(player.EntityIndex, out int jumpTick)
                            ? jumpTick * interval
                            : 0d,
                    EyePitch: lookingAt,
                    WaterLevel: player.WaterLevel(),
                    MaxSpeed: player.MaxSpeed(),
                    ActiveWeapon: activeSlot,
                    WeaponClass: active?.ClassName,

                    // Read here rather than left to the caller, because this is the only pass over
                    // the entity table there is — a viewer asking later would have to re-walk the
                    // demo to find out which item the weapon was.
                    WeaponItem: active?.ItemDefinitionIndex(),

                    // **The disguise, and whose side we are on.** `C_TFPlayer::ValidateModelIndex`
                    // and `GetSkin` both branch on
                    // `InCond( TF_COND_DISGUISED ) && IsEnemyPlayer()`, so the three travel
                    // together and `Disguise` applies them.
                    Conditions: player.Conditions(),
                    BurningFor: burningFor,
                    DisguiseClass: player.DisguiseClass(),
                    DisguiseTeam: player.DisguiseTeam(),
                    DisguiseMaskClass: player.DisguiseMaskClass(),

                    // **`IsEnemyPlayer` asks about the LOCAL player** (`c_tf_player.cpp:5384`),
                    // which in a recording is whoever recorded it — and only the timeline knows
                    // that. Computed here so the scene never has to guess whose eyes it is behind.
                    IsEnemy: IsEnemyOfRecorder(
                        recorderTeam,
                        resource?.Integer($"m_iTeam.{slot}") ?? First(player, TeamProperties)),

                    // **Whether the CLIENT advances this player's cycle** (B280).
                    // `CTFPlayer::CTFPlayer` calls `UseClientSideAnimation()` unconditionally
                    // (`tf_player.cpp:953`), so `m_bClientSideAnimation` arrives set for every
                    // player and `C_BaseAnimating::UpdateClientSideAnimation`
                    // (`c_baseanimating.cpp:5134`) runs `FrameAdvance( 0.0f )` on them each frame.
                    // A player's own `m_flCycle` is therefore never a driving value — it decodes
                    // to zero and stays there — so a renderer told this is false leaves them in
                    // one pose while their position keeps interpolating.
                    //
                    // **Read off the entity here rather than off the track, deliberately.** The
                    // track's copy is the prop path's, and a player never takes the prop path;
                    // deriving it a second way is exactly the second route that has produced wrong
                    // answers here before. This is the same accessor the track's own assignment
                    // uses, applied to the same entity.
                    ClientSideAnimated: player.ClientSideAnimation() is { } clientSide &&
                        clientSide != 0,

                    // **The gesture slots, because a player's animation layers are excluded from
                    // the wire** (B282, `tf_player.cpp:774`). Each slot holds the last trigger the
                    // demo raised for it and the tick it arrived on; whether it is still playing
                    // depends on the sequence its activity resolves to, which only the scene can
                    // answer. Null when the player has raised nothing, so the common case costs no
                    // allocation.
                    Gestures: SameAsBefore(gesturedBefore, player.EntityIndex, GesturesFor(gestures, player.EntityIndex)),

                    // **The three per-BONE scales** (B312), defaulting to 1 exactly as
                    // `C_TFPlayer`'s own members do (`c_tf_player.cpp:577`) — a demo that never
                    // sends them is one where TF2 would also have used 1, so this is the engine's
                    // default rather than a fallback for missing data.
                    HeadScale: player.BoneScales().Head ?? 1f,
                    TorsoScale: player.BoneScales().Torso ?? 1f,
                    HandScale: player.BoneScales().Hand ?? 1f)
                {
                    // **Handed the last frame's instance whenever every value is the same** (D217), the way
                    // `SameAsBefore` shares the item lists: 1,498 MB of f12's 2,948 MB timeline was this half,
                    // copied unchanged into each of 1,255,344 player-frames.
                    Detail = SharedDetail(detailBefore, player.EntityIndex, new ScenePlayerDetail
                    {
                    MaxHealth = resource?.Integer($"m_iMaxHealth.{slot}"),
                    MaxHealthForBuffing = resource?.Integer($"m_iMaxBuffedHealth.{slot}"),
                    EntityHealth = player.Integer("DT_BasePlayer.m_iHealth"),
                    HideHud = player.Integer("DT_Local.m_iHideHUD"),
                    // `SendPropIntWithMinusOneFlag`: sent through `SendProxy_IntAddOne`, so the wire holds the clip plus one
                    // and `RecvProxy_IntSubOne` takes it off (sendproxy.cpp:39, recvproxy.cpp:26) — -1, no clip, sends 0.
                    WeaponClip1 = active?.Integer("DT_LocalWeaponData.m_iClip1") - 1,
                    WeaponPrimaryAmmoType = active?.Integer("DT_LocalWeaponData.m_iPrimaryAmmoType"),
                    Ammo = AmmoCounts(player),
                    Items = SameAsBefore(carriedBefore, player.EntityIndex, CarriedItems(player, entities)),
                    OwnAttributes = player.EconAttributes(EconAttributeList.Local) is { Count: > 0 } own ? own : null,
                    DisguiseTarget = EntityState.Slot(player.Integer("DT_TFPlayerShared.m_hDisguiseTarget")),
                    DisguiseHealth = player.Integer("DT_TFPlayerShared.m_iDisguiseHealth"),
                    PlayerState = player.Integer("DT_TFPlayerShared.m_nPlayerState"),
                    CarryingObject = player.Integer(CarryingObjectProperty) is > 0,
                    StunFlags = player.StunFlags(),
                    StunIndex = player.StunIndex(),
                    IsMiniBoss = player.Integer("DT_TFPlayer.m_bIsMiniBoss") is > 0,
                    ActiveWeaponClip = player.Integer("DT_TFSendHealersDataTable.m_nActiveWpnClip"),
                    Gravity = player.Number("DT_BaseEntity.m_flGravity") ?? 0f,
                    KillStreak = player.Integer("m_nStreaks.000"),
                    InvisChangeCompleteTime = player.Number("DT_TFPlayerShared.m_flInvisChangeCompleteTime"),
                    ScreenOverlayMaterial = player.EntityIndex == recorderSlot + 1 ? screenOverlay : null,
                    CloakMeter = player.Number("DT_TFPlayerShared.m_flCloakMeter"),
                    DisguiseWeapon = EntityState.Slot(player.Integer("DT_TFPlayerShared.m_hDisguiseWeapon")),
                    DisguiseWeaponItem = DisguiseWeaponItem(player, entities),
                    Decapitations = player.Integer("DT_TFPlayerShared.m_iDecapitations"),
                    RageMeter = player.Number("DT_TFPlayerSharedLocal.m_flRageMeter"),
                    RageDraining = player.Integer("DT_TFPlayerSharedLocal.m_bRageDraining") is > 0,
                    ItemChargeMeter = ItemChargeMeters(player),
                    HypeMeter = player.Number("DT_TFPlayerShared.m_flHypeMeter"),
                    RevengeCrits = player.Integer("DT_TFPlayerShared.m_iRevengeCrits"),
                    RuneCharge = player.Number("DT_TFPlayerShared.m_flRuneCharge"),
                    KartNextAvailableBoost = player.Number("DT_TFPlayer.m_flKartNextAvailableBoost"),
                    KartHealth = player.Integer("DT_TFPlayer.m_iKartHealth"),
                    SpawnCounter = player.Integer("DT_TFPlayer.m_iSpawnCounter"),
                    DeathTime = player.Number("DT_LocalPlayerExclusive.m_flDeathTime") ?? 0f,
                    PlayerSkinOverride = player.Integer("DT_TFPlayer.m_iPlayerSkinOverride"),
                    DisguiseSkinOverride = player.Integer("DT_TFPlayerShared.m_nDisguiseSkinOverride"),
                    WeaponAccountId = active?.Integer("DT_ScriptCreatedItem.m_iAccountID") is { } account
                        ? unchecked((uint)account)
                        : null,
                    WeaponQuality = active?.Integer("DT_ScriptCreatedItem.m_iEntityQuality"),
                    HasTheFlag = EntityState.Slot(player.Integer("DT_TFPlayer.m_hItem")) is { } heldItem
                        && entities.TryGet(heldItem, out EntityState? heldEntity)
                        && heldEntity.ClassName == "CCaptureFlag",
                    Medigun = MedigunOf(player, entities),
                    ActiveMedigun = active is { ClassName: "CWeaponMedigun" } medigun
                        ? (EntityState.Slot(medigun.Integer("DT_WeaponMedigun.m_hHealingTarget")),
                            medigun.Number("DT_TFWeaponMedigunDataNonLocal.m_flChargeLevel")
                                ?? medigun.Number("DT_LocalTFWeaponMedigunData.m_flChargeLevel") ?? 0f)
                        : null,
                    Fov = player.Integer("DT_BasePlayer.m_iFOV"),
                    FovStart = player.Integer("DT_BasePlayer.m_iFOVStart"),
                    FovTime = player.Number("DT_BasePlayer.m_flFOVTime"),
                    FovRate = player.Number("DT_Local.m_flFOVRate"),
                    DefaultFov = player.Integer("DT_BasePlayer.m_iDefaultFOV"),
                    ViewOffsetZ = player.Number("DT_LocalPlayerExclusive.m_vecViewOffset[2]"),
                    MovementStunTime = player.Number("DT_TFPlayerShared.m_flMovementStunTime"),
                    MovementStunAmount = player.Integer("DT_TFPlayerShared.m_iMovementStunAmount"),
                    MovementStunParity = player.Integer("DT_TFPlayerShared.m_iMovementStunParity"),
                    AllowMoveDuringTaunt = player.Integer("DT_TFPlayer.m_bAllowMoveDuringTaunt") is > 0,
                    CurrentTauntMoveSpeed = player.Number("DT_TFPlayer.m_flCurrentTauntMoveSpeed"),
                    VehicleReverseTime = player.Number("DT_TFPlayer.m_flVehicleReverseTime"),
                    GrapplingHookTarget = EntityState.Slot(player.Integer("DT_TFPlayer.m_hGrapplingHookTarget")),
                    TauntItemDefIndex = player.Integer("DT_TFPlayer.m_iTauntItemDefIndex"),
                    ActiveTauntSlot = player.Integer("DT_TFPlayer.m_nActiveTauntSlot"),

                    // TF's HandleDucking and HandleMoving inputs (B437).
                    Posture = posture,
                    }),

                    // Inline, not in the shared half: on a SourceTV demo they change almost every tick (D217).
                    Velocity = player.Number("DT_LocalPlayerExclusive.m_vecVelocity[0]") is { } velocityX
                        ? (velocityX,
                            player.Number("DT_LocalPlayerExclusive.m_vecVelocity[1]") ?? 0f,
                            player.Number("DT_LocalPlayerExclusive.m_vecVelocity[2]") ?? 0f)
                        : null,
                    Movement = LocalMovement(player),
                });
            }

            // **Only when the tick advanced.** Several commands can share a tick, and recording a
            // frame for each would make the timeline's own ordering ambiguous — PlayersAt would
            // then depend on which of them it happened to find first.
            // **The round, from the game rules entity there is exactly one of.** `m_iRoundState`
            // is declared in `DT_TeamplayRoundBasedRules` and reaches `CTFGameRulesProxy` through
            // `teamplayroundbased_gamerules_data` — confirmed present in a modern demo's own
            // schema. Null when the demo has no such entity, which every pre-2009 era specimen
            // does not.
            EntityState? gameRules = entities.OfClass(GameRulesClass).FirstOrDefault();
            int? roundState = gameRules?.Integer(RoundStateProperty);
            SceneGameRules rules = new(
                gameRules?.Integer(MannVsMachineProperty) is > 0,
                gameRules?.Integer(HalloweenScenarioProperty) ?? 0,
                entities.OfClass(PlayerDestructionClass).Any())
            {
                WaitingForPlayers = gameRules?.Integer("DT_TeamplayRoundBasedRules.m_bInWaitingForPlayers") is > 0,
                Overtime = gameRules?.Integer("DT_TeamplayRoundBasedRules.m_bInOvertime") is > 0,
                Setup = gameRules?.Integer("DT_TeamplayRoundBasedRules.m_bInSetup") is > 0,
                StopWatch = gameRules?.Integer("DT_TeamplayRoundBasedRules.m_bStopWatch") is > 0,
                GameType = gameRules?.Integer("DT_TFGameRules.m_nGameType") ?? 0,
                RoundsPlayed = gameRules?.Integer("DT_TeamplayRoundBasedRules.m_nRoundsPlayed") ?? 0,
                MapResetTime = gameRules?.Number("DT_TeamplayRoundBasedRules.m_flMapResetTime") ?? 0f,
                ActiveMinigame = entities.OfClass("CTFMinigameLogic").FirstOrDefault() is { } minigameLogic
                    && EntityState.Slot(minigameLogic.Integer("DT_TFMinigameLogic.m_hActiveMinigame")) is not null,
                MatchGroup = gameRules?.Integer(MatchGroupProperty) ?? -1,
                Koth = gameRules?.Integer("DT_TFGameRules.m_bPlayingKoth") is > 0,
                ShowMatchSummary = gameRules?.Integer("DT_TFGameRules.m_bShowMatchSummary") is > 0,
                MapHasMatchSummaryStage = gameRules?.Integer("DT_TFGameRules.m_bMapHasMatchSummaryStage") is > 0,
                PartyLeaderRed = entities.OfClass(ResourceClass).FirstOrDefault()?.Integer("DT_TFPlayerResource.m_iPartyLeaderRedTeamIndex") ?? 0,
                PartyLeaderBlue = entities.OfClass(ResourceClass).FirstOrDefault()?.Integer("DT_TFPlayerResource.m_iPartyLeaderBlueTeamIndex") ?? 0,
                EventTeamStatus = entities.OfClass(ResourceClass).FirstOrDefault()?.Integer("DT_TFPlayerResource.m_iEventTeamStatus") ?? 0,
                BlueKothTimer = EntityState.Slot(gameRules?.Integer("DT_TFGameRules.m_hBlueKothTimer")),
                RedKothTimer = EntityState.Slot(gameRules?.Integer("DT_TFGameRules.m_hRedKothTimer")),
                TimerToShowInHud = entities.OfClass(ObjectiveResourceClass).FirstOrDefault()?.Integer("DT_BaseTeamObjectiveResource.m_iTimerToShowInHUD") ?? 0,
                InTraining = gameRules?.Integer("DT_TFGameRules.m_bIsInTraining") is > 0,
                WinningTeam = gameRules?.Integer(WinningTeamProperty),
                MapHolidayType = gameRules?.Integer("DT_TFGameRules.m_nMapHolidayType") ?? 0,
                RopesHolidayLightsAllowed = gameRules?.Integer("DT_TFGameRules.m_bRopesHolidayLightsAllowed") is not 0,
                PowerupMode = gameRules?.Integer("DT_TFGameRules.m_bPowerupMode") is > 0,
                Present = gameRules is not null,
                NextRespawnWave = (gameRules?.Number("m_flNextRespawnWave.002") ?? 0f, gameRules?.Number("m_flNextRespawnWave.003") ?? 0f),
                TeamRespawnWaveTimes = (gameRules?.Number("m_TeamRespawnWaveTimes.002") ?? -1f, gameRules?.Number("m_TeamRespawnWaveTimes.003") ?? -1f),
                // `GetRobotDestructionLogic()`, which player destruction's logic also registers as (it derives from it).
                RobotDestructionRespawnScale = entities.OfClass("CTFRobotDestructionLogic").Concat(entities.OfClass(PlayerDestructionClass)).FirstOrDefault() is { } robotLogic
                    ? (robotLogic.Number("DT_TFRobotDestructionLogic.m_flRedTeamRespawnScale") ?? 0f,
                        robotLogic.Number("DT_TFRobotDestructionLogic.m_flBlueTeamRespawnScale") ?? 0f)
                    : null,
            };

            int? serverTick = entities.PacketTick > 0 ? entities.PacketTick : null;
            IReadOnlyList<SceneRoundTimer>? roundTimers = RoundTimers(entities);
            IReadOnlyList<SceneBuilding>? buildings = Buildings(entities);
            IReadOnlyList<SceneTeam>? teams = Teams(entities);
            IReadOnlyList<SceneScoreboardPlayer>? scoreboardPlayers = ScoreboardPlayers(entities);
            IReadOnlyList<SceneIdEntity>? idEntities = IdEntities(entities, index => precache.Path(ModelPrecache.Unpack(index, protocol)));
            frameTicks += Stopwatch.GetTimestamp() - frameFrom;

            // Kept for the life of the demo, so not at `Add`'s doubled capacity (B433).
            players.TrimExcess();

            if (frames.Count > 0 && frames[^1].Tick >= command.Tick)
            {
                frames[^1] = new TimelineFrame(
                    frames[^1].Tick, players, recorderTeam, roundState, rules, serverTick, roundTimers, buildings, teams, scoreboardPlayers, idEntities);
                continue;
            }

            // The frame before is final now — only the last frame is ever replaced — so the roster may read it.
            if (frames.Count > 0)
            {
                firstStated.Observe(frames[^1]);
            }

            firstFrameTick ??= command.Tick;
            frames.Add(new TimelineFrame(command.Tick, players, recorderTeam, roundState, rules, serverTick, roundTimers, buildings, teams, scoreboardPlayers, idEntities));
        }
        }

        /// <summary>The lists the pass only appends to, or replaces the LAST element of — its output so far.</summary>
        /// <remarks>
        /// **Marked by length rather than copied** (D217): a snapshot keeps each one's count and last element, and a
        /// restore cuts the live list back to them. In these lists everything before the last element is never touched
        /// again, which is what makes the mark enough: `frames[^1]` is replaced within a tick and `soundscapes` drops its
        /// last sample when a later one arrives on the same tick.
        ///
        /// **`choreography` is NOT one of them**: a run's stop is written into it at whatever index the run sits
        /// (`FindLastIndex` by entity), behind later runs, so it is deep-copied with the rest of the state. It was
        /// listed here at first, and a resume stopped part-way then reported a scene stopping in the future — caught by
        /// the partial-resume case of `TimelineBuilderSnapshotTests`. A list joins this set only when every write to it
        /// is an append or touches the last element.
        /// </remarks>
        private ITailList[] Outputs() =>
        [
            frames, sounds, soundscapes, director, viewCommands, userCommands, packetAcknowledgements, conVarHistory,
            viewmodels, fogSamples, gameEvents, userMessages, replaced, voice, voiceInits,
        ];

        /// <summary>A copy of the pass as it stands between two commands, from which it can be resumed.</summary>
        /// <returns>The snapshot.</returns>
        /// <remarks>
        /// **Resuming is restore-and-continue**: the output lists are shared with the live builder rather than copied,
        /// so a snapshot is resumed by a builder that replaces this one (<see cref="Resume"/>), not beside it.
        /// </remarks>
        public BuilderSnapshot Snapshot()
        {
            lease.Refuse("snapshot");
            ITailList[] outputs = Outputs();
            (int Count, object? Last)[] marks = new (int, object?)[outputs.Length];

            for (int index = 0; index < outputs.Length; index++)
            {
                marks[index] = (outputs[index].Count, outputs[index].Last);
            }

            return new BuilderSnapshot(StateCopy.Of(this, Shared(outputs)), marks);
        }

        /// <summary>A builder at the snapshot's moment, its output lists cut back to the snapshot's marks.</summary>
        /// <param name="snapshot">Where to resume from; it can be resumed again later.</param>
        /// <returns>The builder, ready for the commands after the snapshot.</returns>
        /// <exception cref="InvalidOperationException">
        /// The output lists no longer reach the snapshot — an EARLIER snapshot was resumed since and cut them — or they were
        /// handed out by <see cref="Finish"/>.
        /// </exception>
        /// <remarks>
        /// **Marks can only cut, never restore what was cut.** Resuming an earlier snapshot shortens the shared lists;
        /// a later snapshot's marks then point past their end, and resuming it would silently leave a gap (S1 at 100
        /// frames then S2 at 500 gave 101 frames). A back-then-forward seek therefore needs the later snapshot to own what
        /// lies between — per-window output, D217's step 3 — and until then it is refused here.
        /// </remarks>
        public static Builder Resume(BuilderSnapshot snapshot)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            snapshot.Builder.lease.Refuse("resume");

            ITailList[] outputs = snapshot.Builder.Outputs();

            for (int index = 0; index < outputs.Length; index++)
            {
                if (outputs[index].Count < snapshot.Marks[index].Count - 1)
                {
                    throw new InvalidOperationException(
                        "Cannot resume: an earlier snapshot was resumed since, and the output lists no longer reach this one (D217).");
                }
            }

            Builder resumed = StateCopy.Of(snapshot.Builder, snapshot.Builder.Shared(outputs));

            for (int index = 0; index < outputs.Length; index++)
            {
                (int count, object? last) = snapshot.Marks[index];
                ITailList list = outputs[index];

                // Only the last element can have changed since, so keep everything before it and put it back. Restoring
                // it is the exact state; a replay would also overwrite it with the same value, which is why no test can
                // tell the two apart — replay is deterministic.
                while (list.Count > Math.Max(0, count - 1))
                {
                    list.RemoveLast();
                }

                if (count > 0)
                {
                    list.AddBoxed(last);
                }
            }

            return resumed;
        }

        /// <summary>What one snapshot allocates, in all and by field (each field copied alone, so shared parts count twice).</summary>
        /// <returns>The whole snapshot's bytes, then each field's, largest first.</returns>
        /// <remarks>The `timeline-resume` probe's instrument for D217's question of what a checkpoint costs.</remarks>
        public (long Total, IReadOnlyList<(string Field, long Bytes)> Fields) SnapshotCost()
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            BuilderSnapshot snapshot = Snapshot();
            long total = GC.GetAllocatedBytesForCurrentThread() - before;
            GC.KeepAlive(snapshot);

            HashSet<object> shared = Shared(Outputs());
            List<(string Field, long Bytes)> fields = [];

            // Reaching private fields is the instrument: it copies each one as the snapshot does.
#pragma warning disable S3011
            foreach (System.Reflection.FieldInfo field in typeof(Builder).GetFields(
                         System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic))
#pragma warning restore S3011
            {
                if (field.GetValue(this) is not { } value || value.GetType().IsValueType || shared.Contains(value))
                {
                    continue;
                }

                long from = GC.GetAllocatedBytesForCurrentThread();
                object copy = StateCopy.Of(value, shared);
                fields.Add((field.Name, GC.GetAllocatedBytesForCurrentThread() - from));
                GC.KeepAlive(copy);
            }

            // The schema is shared by identity; what copying it WOULD cost, for the question of whether to share it.
            long schemaFrom = GC.GetAllocatedBytesForCurrentThread();
            object schemaCopy = StateCopy.Of(schema, new HashSet<object>());
            fields.Add(("schema, if it were copied", GC.GetAllocatedBytesForCurrentThread() - schemaFrom));
            GC.KeepAlive(schemaCopy);

            return (total, [.. fields.OrderByDescending(field => field.Bytes)]);
        }

        /// <summary>
        /// What a resume CREATES, by owning field two levels down (<c>entities._byClass</c>) and type — D217 step 4's
        /// instrument for which structure owns each array a resume copies.
        /// </summary>
        /// <returns>Each owner's largest created type by elements, largest first.</returns>
        /// <remarks>
        /// Each owner is copied alone with no prior, as a resume copies, so an object two owners share counts under both.
        /// The control: the totals over owners are at least the whole resume's (<c>ResumeCensus</c>).
        /// </remarks>
        public IReadOnlyList<(string Owner, Type Type, long Objects, long Elements)> CensusByOwner()
        {
            HashSet<object> shared = SharedInputs();
            // The builder's own shell, and below each owner that is descended into, that owner's own: a census of the
            // fields alone left them out, and the control (whole-resume census) caught it.
            List<(string, Type, long, long)> rows = [("(builder)", typeof(Builder), 1, 1)];

            foreach ((string name, object owned, bool shell) in Owners(shared))
            {
                if (shell)
                {
                    rows.Add((name, owned.GetType(), 1, 1));
                    continue;
                }

                Dictionary<Type, (long Objects, long Elements)> created = [];
                GC.KeepAlive(StateCopy.Of(owned, shared, prior: null, out _, created));

                foreach ((Type type, (long objects, long elements)) in created)
                {
                    rows.Add((name, type, objects, elements));
                }
            }

            return [.. rows.OrderByDescending(row => row.Item4)];
        }

        /// <summary>
        /// The builder's state by owner, two fields down: each owner, and a shell row for each field descended into.
        /// </summary>
        /// <param name="shared">The inputs every copy shares, which are no one's state.</param>
        /// <returns>Each owner's name and object; <c>Shell</c> marks a descended field's own object.</returns>
        internal IEnumerable<(string Name, object Value, bool Shell)> Owners(IReadOnlySet<object> shared)
        {
            // Reaching private fields is the instrument: it walks each one as a copy does.
#pragma warning disable S3011
            const System.Reflection.BindingFlags Instance =
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;

            foreach (System.Reflection.FieldInfo field in typeof(Builder).GetFields(Instance))
            {
                if (field.GetValue(this) is not { } value || value.GetType().IsValueType || shared.Contains(value))
                {
                    continue;
                }

                bool whole = value is System.Collections.IEnumerable || value.GetType().Namespace?.StartsWith("System", StringComparison.Ordinal) == true;

                if (whole)
                {
                    yield return (field.Name, value, false);
                    continue;
                }

                yield return (field.Name, value, true);

                foreach (System.Reflection.FieldInfo inner in value.GetType().GetFields(Instance))
                {
                    if (inner.GetValue(value) is { } owned && !owned.GetType().IsValueType && !shared.Contains(owned))
                    {
                        yield return ($"{field.Name}.{inner.Name}", owned, false);
                    }
                }
            }
#pragma warning restore S3011
        }

        /// <summary>
        /// Drops every piece of output no question at or after <paramref name="cut"/> can read (D217 step 3): track
        /// histories, tracks that ended before it, and the front of the tick-ordered output lists.
        /// </summary>
        /// <param name="cut">The earliest tick still to be answered, already moved back by the caller's lookback.</param>
        /// <remarks>
        /// **What a checkpoint is made of.** Run, trim, copy: what is left is the decoder's own state (entity table,
        /// baselines, string tables, feeds in flight) plus each output's tail. Sounds are kept whole, because a loop
        /// started long ago is still playing (`docs/memory/a-loop-is-state-not-an-event.md`); they are small.
        ///
        /// **Kept whole, and why** (D220 review): <c>playingScenes</c> and <c>corpses</c> are live state — the scene each
        /// slot is playing, the corpse each slot holds now — read by the next update, not output. <c>replaced</c>, the
        /// corpses a slot has since replaced, is a corpse's whole life, which D220 carries rather than reaches for: how
        /// long one is drawn depends on whether it is in view (<c>C_TFRagdoll::ClientThink</c>), which no setting bounds.
        /// Trimmed here besides the lists: scene runs that stopped before the cut, which cover no tick a window answers,
        /// and the spectator-GUI changes before the one in force.
        /// </remarks>
        /// <param name="trackCut">
        /// The same for track histories, which need far less: only what the interpolation reaches back over (one delay and
        /// the spline's older samples). Measured on f12, histories kept for a 4 s lookback were most of what every
        /// checkpoint copied afresh, because every moving track's histories change between two checkpoints.
        /// </param>
        public void Trim(int cut, int trackCut)
        {
            lease.Refuse("trim");

            foreach (ScenePropTrack track in props.Concat(playerTracks))
            {
                track.TrimBefore(trackCut);
            }

            props.RemoveAll(track => track.EndTick <= trackCut);
            playerTracks.RemoveAll(track => track.EndTick <= trackCut);

            // Frames at the TRACK cut, plus the frame in force at every game event inside the feed span: the HUD feed
            // resolves each event it replays against the players and rules at that event's own tick
            // (`HudEventFeed.Resolve`, D220), and nothing else reads a frame from behind the track cut. Every frame of
            // the span was 800 ticks of frames per checkpoint, overlapping the next: on f12 it nearly doubled the heap.
            frames.DropBefore(
                frame => frame.Tick,
                trackCut,
                [.. gameEvents.Where(fired => fired.Tick >= cut && fired.Tick < trackCut).Select(fired => fired.Tick).Distinct()]);
            soundscapes.DropBefore(sample => sample.Tick, cut);
            director.DropBefore(shot => shot.Tick, cut);
            viewCommands.DropBefore(command => command.Tick, cut);
            userCommands.DropBefore(command => command.Tick, cut);
            packetAcknowledgements.DropBefore(ack => ack.Tick, cut);
            conVarHistory.DropBefore(change => change.Tick, cut);
            // By stream: `viewmodels` interleaves every player's two hands, each written only on CHANGE, so the record in
            // force for one can sit far behind the cut — a plain tick cut lost it (f12: 50 of 80 seeks differed).
            viewmodels.DropBefore(sample => sample.Tick, sample => (sample.Weapon.OwnerEntityIndex, sample.Weapon.IsMainHand), cut);
            fogSamples.DropBefore(sample => sample.Tick, cut);
            gameEvents.DropBefore(fired => fired.Tick, cut);
            userMessages.DropBefore(message => message.Tick, cut);

            // A run is written behind the tail (its stop), so this list is chunked but not a TailList, and it is rebuilt
            // when anything stopped before the cut: a match holds few runs at once, so the rebuild copies little.
            if (choreography.FindLastIndex(run => run.StoppedTick < cut) >= 0)
            {
                List<SceneChoreography> live = [.. choreography.Where(run => run.StoppedTick is null || run.StoppedTick >= cut)];
                choreography.Clear();
                live.ForEach(choreography.Add);
            }

            // At or before: the change in force at the cut is kept.
            int gui = spectatorGuiChanges.FindLastIndex(change => change.Tick < cut);

            if (gui > 0)
            {
                spectatorGuiChanges.RemoveRange(0, gui);
            }
        }

        /// <summary>A copy of the pass that owns all of its state and output, from which any number of windows resume.</summary>
        /// <returns>The checkpoint; <see cref="Trim"/> first, or it is as large as the whole timeline.</returns>
        /// <remarks>
        /// **Owns its output, unlike <see cref="Snapshot"/>**, so resuming one never cuts what another reads, and a seek
        /// back then forward is as good as forward alone — the case marks had to refuse.
        /// </remarks>
        /// <param name="prior">The previous checkpoint's copy map, whose unchanged objects this one shares; null for none.</param>
        /// <param name="copies">This checkpoint's copy map, for the next.</param>
        /// <param name="created">When given, counts what the checkpoint creates rather than shares, by type.</param>
        public Builder Checkpoint(
            Dictionary<object, object>? prior,
            out Dictionary<object, object> copies,
            Dictionary<Type, (long Objects, long Elements)>? created = null)
        {
            lease.Refuse("checkpoint");
            // Freezing every entity's properties, so the resume copy shares them rather than copying ~200k slots (D217 step 4).
            return StateCopy.Of(this, SharedInputs(), prior, out copies, created, freeze: true);
        }

        /// <summary>The facts only the END of a demo knows, which every window of it reports (D217 step 3).</summary>
        /// <param name="ServerConVars">The final server ConVars, which bound the interpolation.</param>
        /// <param name="FirstStated">Each player's first stated team and class, the final frame observed.</param>
        /// <param name="Everyone">Everyone who played, by user id.</param>
        internal sealed record EndFacts(ServerConVars ServerConVars, FirstStatedRoster FirstStated, Dictionary<int, PlayerInfo> Everyone);

        /// <summary>This pass's end facts, once its last command has run: its final frame is observed here, being final.</summary>
        /// <returns>The facts, which nothing writes afterwards — the pass is finished — so every window shares them.</returns>
        public EndFacts End()
        {
            if (frames.Count > 0)
            {
                firstStated.Observe(frames[^1]);
            }

            return new EndFacts(serverConVars, firstStated, everyone);
        }

        /// <summary>The first frame's tick, kept when a trim drops that frame: the demo's start, for a walk's windows.</summary>
        private int? firstFrameTick;

        /// <summary>The demo's first frame's tick, or 0 for a demo with none — what <c>FirstTick</c> is on a whole build.</summary>
        public int StartTick => firstFrameTick ?? 0;

        /// <summary>Seconds per tick as the demo's server info stated it, or 0 before it has.</summary>
        public double IntervalPerTick => interval;

        /// <summary>Every reader's reach under <paramref name="settings"/> and the server's bounds as they stand now (D220).</summary>
        /// <param name="settings">The viewer's settings.</param>
        /// <returns>The reach.</returns>
        public WindowReach ReachFor(WindowSettings settings) => WindowReach.For(settings, serverConVars, interval);

        /// <summary>Fills the decoder's flattening cache for every class, so no resume of a checkpoint writes it.</summary>
        public void FlattenEveryClass() => decoder.FlattenEveryClass();

        /// <summary>A builder resumed from a checkpoint, which stays untouched for the next resume.</summary>
        /// <param name="checkpoint">The checkpoint.</param>
        /// <returns>The builder, ready for the commands after it.</returns>
        /// <param name="inputs">What every copy shares rather than copies — <see cref="Inputs"/>, built once per walk.</param>
        /// <param name="created">When given, counts what the resume copy creates, by type (D217 step 4's census).</param>
        public static Builder FromCheckpoint(
            Builder checkpoint,
            IReadOnlySet<object> inputs,
            Dictionary<Type, (long Objects, long Elements)>? created = null) =>
            StateCopy.Of(checkpoint, inputs, prior: null, out _, created);

        /// <summary>
        /// What every copy of this builder and of its checkpoints shares: the same set for a whole walk, since the schema,
        /// its flattening cache (filled before the first checkpoint) and the commands do not change.
        /// </summary>
        /// <returns>A new set; a walk builds it once and hands it to every resume.</returns>
        public HashSet<object> Inputs() => SharedInputs();

        /// <summary>What every copy of this builder shares rather than copies: the schema, its caches and the commands.</summary>
        private HashSet<object> SharedInputs()
        {
            // The schema and everything under it never change once parsed; walked once, then shared by every snapshot.
            schemaGraph ??= StateCopy.Reachable(schema);

            HashSet<object> shared = new(schemaGraph, ReferenceEqualityComparer.Instance) { commands, schemaGraph };
            shared.UnionWith(decoder.SchemaDerived());

            if (classes is not null)
            {
                shared.Add(classes);
            }

            return shared;
        }

        /// <summary>What a copy of this builder shares with it instead of copying: the inputs and the outputs.</summary>
        private HashSet<object> Shared(ITailList[] outputs)
        {
            HashSet<object> shared = SharedInputs();
            shared.Add(lease);

            foreach (ITailList output in outputs)
            {
                shared.Add(output);
            }

            return shared;
        }

        /// <summary>Whether the output lists this builder shares with its snapshots have been handed out in a timeline.</summary>
        /// <remarks>
        /// **One per family**: the live builder, every snapshot of it and every builder resumed from one share it, because
        /// they share the output lists. Once <see cref="Finish"/> has put those lists in a timeline — and re-timed and
        /// re-sorted `sounds` in place — a resume would cut lists a caller already holds, and resume from a sorted
        /// `sounds`. So both are refused, rather than either being allowed to do something silently wrong.
        /// </remarks>
        private sealed class OutputLease
        {
            private bool handedOut;

            public void HandOut()
            {
                Refuse("finish");
                handedOut = true;
            }

            public void Refuse(string what)
            {
                if (handedOut)
                {
                    throw new InvalidOperationException(
                        $"Cannot {what}: this builder's output lists were already handed out by Finish (D217).");
                }
            }
        }

        private readonly OutputLease lease = new();

        /// <summary>Every object under the schema, shared by snapshots rather than copied; built at the first one.</summary>
        private HashSet<object>? schemaGraph;

        /// <summary>Runs the post-passes and hands over the timeline; the builder, its snapshots and their resumes are spent afterwards.</summary>
        /// <returns>The timeline.</returns>
        /// <param name="whole">
        /// For a WINDOW (D217 step 3): the end-of-demo facts of the walk over the whole demo (<see cref="End"/>), which the
        /// window cannot know from its own commands. Null for a straight build, where this builder's own are those.
        /// </param>
        public DemoTimeline Finish(EndFacts? whole = null)
        {
        lease.HandOut();
        EndFacts end = whole ?? End();

        // **The signon's sounds are the ambience already playing, and its clock is not the
        // recording's.** Measured on movement-test-pov-cp_process: six )ambient/machine_hum.wav
        // arrive from the signon stamped tick 4654 while every packet sound runs from 30 upward, so
        // the signon carries the server's tick at the moment recording began. Left alone, the map's
        // hum starts seventy seconds in and sorting by the stated tick buries the opening minute
        // behind it.
        //
        // Moved to the first tick anything else happens on, because that is when a viewer starts
        // hearing the map — not to zero, since a demo's ticks do not start at zero
        // (docs/memory/demo-ticks-do-not-start-at-zero.md) and zero would sort before a recording
        // that opens at 30 and leave a gap nothing fills.
        //
        // **On a copy**: the builder's own list is a tail list its snapshots share, and re-timing or re-sorting it in
        // place would hand a later resume a sorted list (D217).
        List<SceneSound> heard = [.. sounds];

        if (heard.Count > 0)
        {
            int firstPacketTick = int.MaxValue;

            foreach (SceneSound sound in heard)
            {
                if (!sound.FromSignon && sound.Tick < firstPacketTick)
                {
                    firstPacketTick = sound.Tick;
                }
            }

            if (firstPacketTick < int.MaxValue)
            {
                for (int index = 0; index < heard.Count; index++)
                {
                    if (heard[index].FromSignon)
                    {
                        heard[index] = heard[index] with { Tick = firstPacketTick };
                    }
                }
            }

            // **A stable sort, so sounds sent in one message keep the order the server wrote them
            // in.** Within a tick that order is the engine's own, and several sounds on one channel
            // replace each other — reordering them would change which one survives.
            heard = [.. heard.OrderBy(sound => sound.Tick)];
        }

        // The viewer's own interp (`cl_interp` and friends from its config), under the server's final bounds.
        double interpAmount = client.Amount(end.ServerConVars);
        int delayTicks = ScenePropTrack.DelayTicksFor(interval, interpAmount);

        foreach (ScenePropTrack track in props.Concat(playerTracks))
        {
            track.InterpolationDelayTicks = delayTicks;
        }

        return new DemoTimeline(
            frames.HandOut(), props, playerTracks, viewCommands.HandOut(), viewmodels.HandOut(), fogSamples.HandOut(), heard,
            soundscapes.HandOut(), director.HandOut(), end.FirstStated)
        {
            MaxClients = maxClients,
            ClientInterpAmount = interpAmount,
            FogControllersSeen = fogControllersSeen,
            FogControllerProperties = fogProperties,
            IntervalPerTick = interval,
            Roster = end.Everyone,
            GameEvents = gameEvents.HandOut(),
            UserMessages = userMessages.HandOut(),
            Voice = voice.HandOut(),
            VoiceInits = voiceInits.HandOut(),
            SpectatorGuiChanges = [.. spectatorGuiChanges],
            RecorderEntityIndex = recorderSlot is { } recorded ? recorded + 1 : null,
            Corpses = [.. replaced, .. corpses.Values],
            Explosions = feeds.Explosions,
            Shots = feeds.Shots,
            Decals = feeds.Decals,
            LightStyles = lightStyles,
            Blood = feeds.Blood,
            Dispatches = feeds.Dispatches,
            MuzzleFlashes = muzzleFlashes,
            HealBeams = healBeams,
            RopeImpulses = ropeImpulses,
            Dormancy = dormancy,
            PointCameras = pointCameras,
            Tonemap = tonemap,
            Suns = suns,
            TfParticleEffects = feeds.TfParticleEffects,
            Sparks = feeds.Sparks,
            Scenes = choreography,
            ServerConVars = end.ServerConVars,
            ConVarChanges = conVarHistory.HandOut(),
            UserCommands = userCommands.HandOut(),
            PacketAcknowledgements = packetAcknowledgements.HandOut(),
            MapCrc = mapCrc,
            IsSourceTv = isSourceTv,
            MapHash = mapHash,
            NetworkProtocol = header.NetworkProtocol,
            FlagLayout = PlayerFlagLayout.For(schema),
            _simulationLag = simulationLag,
            SimulationLagUnknown = simulationLag[LagUnknownBucket],
            _animationLag = animationLag,
            AnimationLagUnknown = animationLag[LagUnknownBucket],
            _clockGap = clockGap,
            SimulationLagByClass = lagByClass,
            Phases = new TimelinePhases(
                Milliseconds(commandTicks),
                Milliseconds(schemaTicks),
                Milliseconds(messageTicks),
                Milliseconds(entityTicks),
                Milliseconds(samplingTicks),
                Milliseconds(viewmodelTicks),
                Milliseconds(frameTicks),
                Milliseconds(Stopwatch.GetTimestamp() - buildFrom)),
        };
        }

    private static double Milliseconds(long ticks) => ticks * 1000d / Stopwatch.Frequency;

    /// <summary>Records where a model-bearing entity was, if this update said anything about it.</summary>
    /// <remarks>
    /// **Only entities the snapshot actually mentioned.** Walking the whole entity table every
    /// frame would ask several hundred entities to repeat themselves across a hundred thousand
    /// frames, and produce identical keyframes that the track then discards — the same answer for
    /// tens of millions of times the work. A demo states what changed; this records exactly that.
    /// </remarks>
    /// <summary>What an entity draws as, or <c>null</c> when nothing can say.</summary>
    /// <returns>
    /// A model path; the empty string for a player, whose model is not in the demo at all.
    /// </returns>
    /// <remarks>
    /// **A player's model is never sent.** <c>CTFPlayerClassShared::GetModelName</c> looks it up
    /// locally from <c>m_iClass</c> through the class data table, and only
    /// <c>m_iszCustomModel</c> travels. So a player is recognised by class and given a track with
    /// no model: the poses are what the interpolator needs, and the model is resolved from the
    /// installed game by whoever draws it.
    /// </remarks>
    /// <summary>Samples any viewmodel the entity table currently describes.</summary>
    /// <remarks>
    /// **Read from the entity table rather than from the snapshot's changed properties**, because
    /// a viewmodel is mostly silent: its model index arrives once and then only the sequence
    /// changes, so a reader looking at what a packet CHANGED would see a weapon with no model for
    /// almost every tick of the demo.
    ///
    /// Only recorded when something differs from the last sample. A viewmodel that has not changed
    /// costs nothing, which matters because z1800 carries 37 of them across 95,480 updates.
    /// </remarks>
    /// <summary>Records every viewmodel that changed on this tick.</summary>
    /// <remarks>
    /// **Deduplicated per entity, not against the tail of the list.** A player has two viewmodels
    /// and a demo describing both writes them alternately, so a check against the previous entry
    /// never matches and every tick records both — which is how the wrong one came to win.
    /// </remarks>
    /// <summary>One player's gesture slots, or null when they have none.</summary>
    /// <param name="feed">The gesture feed.</param>
    /// <param name="entityIndex">The player.</param>
    /// <returns>The slots in slot order, or null.</returns>
    /// <remarks>
    /// **Null rather than an empty list**, because most players hold no gesture in most frames and
    /// this runs once per player per sampled tick. The scene reads it as "nothing to layer".
    /// </remarks>
    private static List<SceneGesture>? GesturesFor(
        PlayerGestureFeed feed, int entityIndex)
    {
        List<SceneGesture> gestures = [];

        feed.For(entityIndex, gestures);

        return gestures.Count > 0 ? gestures : null;
    }

    /// <summary>Decodes a temp entities body once and offers every effect in it to each feed.</summary>
    /// <param name="decoder">The entity decoder, which knows the effect tables.</param>
    /// <param name="message">The message.</param>
    /// <param name="tick">The demo tick the packet arrived on.</param>
    /// <param name="interval">Seconds per tick, for the feeds that want time rather than ticks.</param>
    /// <param name="interpolation">`GetClientInterpAmount()`, in seconds.</param>
    /// <param name="classNames">Class id to name, since an effect names its class by id.</param>
    /// <param name="entities">The entity table, for whether a blast struck a player.</param>
    /// <param name="gestures">Gesture events waiting for their fire tick, in arrival order.</param>
    /// <param name="feeds">The one-shot effect feeds.</param>
    /// <remarks>
    /// **A body that will not read is skipped rather than fatal**, which is the rule everywhere
    /// else in this project: a demo is salvaged, and a temp entities body is independent of every
    /// other message in the packet.
    ///
    /// **The posture comes from the entity table**, not from the sampled `ScenePlayer`, because
    /// this runs while the packet is being applied and the sampler has not run yet. It is the same
    /// entity and the same accessors either way.
    ///
    /// **Every effect is offered to each feed rather than filtered here** (B415). This used to keep exactly one
    /// class and `continue` past the rest, which threw away 15,014 of `demostf-cp_process_f12`'s 42,188 temp
    /// entities — every explosion, tracer, impact and decal in a 26-minute match. Each feed now says for itself
    /// whether an effect is its business, so adding one is adding a feed rather than editing this filter.
    /// </remarks>
    private static void RecordEffects(
        EntityDecoder decoder,
        TempEntitiesMessage message,
        int tick,
        double interval,
        double interpolation,
        Dictionary<int, string> classNames,
        EntityStateTable entities,
        List<(int Fires, DecodedTempEntity Effect)> gestures,
        EffectFeeds feeds)
    {
        try
        {
            foreach (DecodedTempEntity effect in decoder.DecodeTempEntities(
                message.Body.Span, message.Count, message.BodyBits))
            {
                // Stryker disable once : a mutated condition leaves 'className' unassigned below, CS0165 — B410.
                if (!classNames.TryGetValue(effect.ClassId, out string? className))
                {
                    continue;
                }

                int fires = FireTick(tick, effect.DelaySeconds, interval, interpolation);

                // A zero count is one effect sent reliably, the only kind a demo skip still queues (B504).
                if (feeds.Record(
                        className,
                        effect,
                        fires,
                        index => IsPlayer(entities, index),
                        index => Shooter(entities, index),
                        reliable: message.Count == 0))
                {
                    continue;
                }

                // Queued, not recorded: the event reads the player when it FIRES (B437) — see FireGestures.
                if (string.Equals(className, PlayerGestureFeed.EventClassName, StringComparison.Ordinal))
                {
                    gestures.Add((fires, effect));
                }
            }
        }
        catch (Exception error)
            when (error is System.IO.InvalidDataException or System.IO.EndOfStreamException)
        {
            // Skipped for the same reason a sounds body is: everything else in this packet is
            // independent of it, and salvaging what is readable is the point of the project.
        }
    }

    /// <summary>`tf_always_loser.GetBool()`: the int of the float, as `ConVar::GetBool` reads it.</summary>
    private static bool AlwaysLoser(ServerConVars server) => (int)server.Number(LoserState.AlwaysLoserConVar) != 0;

    /// <summary>`CL_FireEvents` for the gesture events: every one due by a tick, in the order they arrived (B437).</summary>
    /// <param name="queued">The waiting events and their fire ticks; the fired ones are removed.</param>
    /// <param name="dueBy">The last tick whose events fire now.</param>
    /// <param name="interval">Seconds per tick; the gesture starts at its fire tick.</param>
    /// <param name="entities">The entity table as the client holds it when they fire.</param>
    /// <param name="alwaysLoser">`tf_always_loser.GetBool()`.</param>
    /// <param name="classes">The class scripts and models, or null.</param>
    /// <param name="gestures">The gesture feed.</param>
    /// <remarks>
    /// **`DoAnimationEvent` runs when the event fires, an interpolation window after it arrives** (`CL_QueueEvent`,
    /// <see cref="FireTick"/>), and reads `GetFlags()`, the latch and `IsLoser()` then — the latest the client has
    /// RECEIVED, since none of them is interpolated. So the timeline fires twice a packet: before applying it, every
    /// event due before its tick, against the packet before (the client fired them on frames between the two); and
    /// after it, every event due on its tick. Both run before that tick's `HandleJumping`, as the engine's events run
    /// before its animation update. An event still waiting when the demo ends never fires.
    /// </remarks>
    private static void FireGestures(
        List<(int Fires, DecodedTempEntity Effect)> queued,
        int dueBy,
        double interval,
        EntityStateTable entities,
        bool alwaysLoser,
        IClassAnimationScripts? classes,
        PlayerGestureFeed gestures)
    {
        int kept = 0;

        for (int next = 0; next < queued.Count; next++)
        {
            (int fires, DecodedTempEntity effect) = queued[next];

            if (fires > dueBy)
            {
                queued[kept++] = (fires, effect);
                continue;
            }

            gestures.Record(
                PlayerGestureFeed.EventClassName, effect, fires * interval, PostureOf(effect, entities, alwaysLoser, classes));
        }

        queued.RemoveRange(kept, queued.Count - kept);
    }
    }

    /// <summary>The tick a temp entity FIRES on — `CL_QueueEvent`, read out of `engine.dll` (`0x1801f9bc0`, B415).</summary>
    /// <param name="arrival">The tick its message arrived on.</param>
    /// <param name="delay">Its own fire delay, the 8-bit hundredths the message carries.</param>
    /// <param name="interval">Seconds per tick; non-positive falls back to TF2's.</param>
    /// <param name="interpolation">`GetClientInterpAmount()` — the WATCHER's, from the viewer's config.</param>
    /// <returns>The first tick at or after the moment it fires.</returns>
    /// <remarks>
    /// <code>
    /// fire_delay = now + delay
    /// if ( cl.m_nMaxClients &gt; 1 || demo playing back )  fire_delay += GetClientInterpAmount()
    /// </code>
    /// **So an effect fires one interpolation window late, when the drawn entities have reached the state it was sent
    /// with.** A server impact lands on the struck player as the client DRAWS it, and a tracer leaves the muzzle where
    /// the gun is drawn. Fired on arrival, every effect ran ~7 ticks ahead of the entities, and a bullet's decal missed a
    /// moving player's hitboxes by the distance they cover in 0.1 s.
    ///
    /// **Rounded DOWN: it fires inside the tick its time falls in.** `CClientState::GetTime` (`0x1800a3020`) is
    /// `tickcount * interval + m_tickRemainder` outside simulation, so the clock `CL_FireEvents` reads runs between
    /// ticks. This rounded up once, on the belief that frames saw whole ticks, and fired every effect a tick late —
    /// f12's scattergun impact at 13850 where TF2 played it at 13849.
    /// </remarks>
    internal static int FireTick(
        int arrival, float delay, double interval, double interpolation = ScenePropTrack.DefaultInterpolation)
    {
        double seconds = interval > 0d ? interval : ScenePropTrack.Tf2TickInterval;

        return arrival + (int)Math.Floor(((delay + interpolation) / seconds) + 1e-6d);
    }

    private sealed partial class Builder
    {
    /// <summary>`C_BaseEntity::Instance( hEntity )->IsPlayer()` — the entity at an index exists and is a player.</summary>
    /// <remarks>
    /// Existence in the table and not visibility: the client's list keeps a dormant entity, and so does this.
    /// </remarks>
    private static bool IsPlayer(EntityStateTable entities, int index) =>
        entities.TryGet(index, out EntityState? entity) &&
        PlayerClass.Equals(entity.ClassName, StringComparison.Ordinal);

    /// <summary>`ToTFPlayer( GetBaseEntity( iPlayer ) )` for a shot, with what the tracer asks of it, or null.</summary>
    private static ShotShooter? Shooter(EntityStateTable entities, int index)
    {
        if (!entities.TryGet(index, out EntityState? player) ||
            !PlayerClass.Equals(player.ClassName, StringComparison.Ordinal))
        {
            return null;
        }

        int? weapon = entities.Resolve(player.ActiveWeaponHandle());
        int? item = weapon is { } held && entities.TryGet(held, out EntityState? holding)
            ? holding.ItemDefinitionIndex()
            : null;

        return new ShotShooter(First(player, TeamProperties) ?? 0, weapon, item);
    }

    /// <summary>What the player named by a gesture event was doing when it fired (B437).</summary>
    /// <param name="effect">The gesture event.</param>
    /// <param name="entities">The entity table.</param>
    /// <param name="alwaysLoser">`tf_always_loser.GetBool()`.</param>
    /// <param name="classes">The class scripts and models, whose crouch walk decides the duck; null for none.</param>
    /// <returns>The context the activity choice is made against.</returns>
    /// <remarks>
    /// **Five of the seven context fields; the feed fills the other two** (B112). `NData` is the event's own
    /// `m_nData`, and `InAirWalk` is `m_bInAirWalk` — the anim state's memory rather than anything on the player —
    /// which `PlayerGestureFeed.Record` reads from the latch it keeps. Each field changes WHICH activity a gesture
    /// resolves to, and an activity that resolves to no sequence draws nothing at all — so a field left unread is a
    /// missing animation rather than a slightly wrong one. `InAirWalk` and `IsLoser` were left false until B112's
    /// residual: every reload begun mid-rocket-jump stood, and a losing scout's air dash was the winner's.
    ///
    /// **The weapon in hand answers two of them**, exactly as the engine asks
    /// (`tf_playeranimstate.cpp:987`): `bIsMinigun` is
    /// `pWpn->GetWeaponID() == TF_WEAPON_MINIGUN` and `bIsSniperRifle` is
    /// `WeaponID_IsSniperRifleOrBow( … )`, which covers the rifle, its decapitation and classic
    /// variants and the bow (`tf_weaponbase.cpp:6328`). A demo does not carry a weapon ID, but it
    /// carries the weapon entity's SERVER CLASS, which is one-to-one with it.
    ///
    /// **`TF_COND_ZOOMED` is condition bit 1** (`tf_shareddefs.h:691`), and the zoom matters as
    /// much as the rifle: an unzoomed sniper fires the ordinary stand activity.
    ///
    /// **`IsLoser` is asked of the live entities as the event arrives** — the double jump asks
    /// `m_Shared.IsLoser()` when it fires (`tf_playeranimstate.cpp:1196`). The game rules entity gives the round
    /// state, the winning team and the match group; the player gives his own team and class, conditions, disguise
    /// team and stun. The rule is <see cref="LoserState.IsLoser"/>, which the HUD asks too.
    /// </remarks>
    private static GestureContext PostureOf(
        DecodedTempEntity effect, EntityStateTable entities, bool alwaysLoser, IClassAnimationScripts? classes)
    {
        int player = 0;

        foreach (DecodedProperty property in effect.State)
        {
            if (string.Equals(
                property.Definition.Property.Name,
                PlayerGestureFeed.PlayerIndexProperty,
                StringComparison.Ordinal))
            {
                player = (int)property.Value.AsInt;
                break;
            }
        }

        if (player <= 0 || !entities.TryGet(player, out EntityState? state))
        {
            return default;
        }

        string? weapon = entities.Resolve(state.ActiveWeaponHandle()) is { } held &&
            entities.TryGet(held, out EntityState? carried)
                ? carried.ClassName
                : null;

        EntityState? rules = entities.OfClass(GameRulesClass).FirstOrDefault();

        return new GestureContext(
            // `bInDuck`, dropped when the model lacks the translated crouch walk (`tf_playeranimstate.cpp:971-975`).
            InDuck: state.Flags() is { } flags &&
                (flags & PlayerActivityState.Ducking) != 0 &&
                ModelCrouchWalks(state, entities, classes, ActivityTableOf(state, rules, alwaysLoser)),
            InSwim: state.WaterLevel() >= PlayerActivityState.WaistDeepWaterLevel,
            IsLoser: IsLoser(state, rules, alwaysLoser),
            IsMinigun: string.Equals(weapon, MinigunClass, StringComparison.Ordinal),
            IsSniperZoomed: IsSniperRifleOrBow(weapon) &&
                state.Conditions().Has(PlayerConditions.Zoomed));
    }
    }

    /// <summary>`m_Shared.IsLoser()` of one player against the game rules, by <see cref="LoserState.IsLoser"/>.</summary>
    /// <param name="state">The player.</param>
    /// <param name="rules">The game rules entity, or null.</param>
    /// <param name="alwaysLoser">`tf_always_loser.GetBool()`.</param>
    /// <returns>Whether the player is a loser.</returns>
    /// <remarks>One place, asked by the gesture context and by the activity table alike (B437).</remarks>
    private static bool IsLoser(EntityState state, EntityState? rules, bool alwaysLoser) =>
        LoserState.IsLoser(
            alwaysLoser,
            SceneGameRules.MatchTypeCompetitive(rules?.Integer(MatchGroupProperty) ?? -1),
            rules?.Integer(RoundStateProperty),
            rules?.Integer(WinningTeamProperty),
            First(state, TeamProperties),
            state.PlayerClass(),
            state.Conditions(),
            state.DisguiseTeam(),
            state.StunIndex(),
            state.StunFlags());

    /// <summary>`CTFPlayerAnimState::ActivityOverride`'s table for one player now, by <see cref="PlayerActivityOverrides.For"/>.</summary>
    private static PlayerActivityOverride ActivityTableOf(EntityState player, EntityState? rules, bool alwaysLoser) =>
        PlayerActivityOverrides.For(
            player.Conditions(),
            IsLoser(player, rules, alwaysLoser),
            player.Integer(CarryingObjectProperty) is > 0);

    /// <summary>
    /// `SelectWeightedSequence( TranslateActivity( ACT_MP_CROUCHWALK ) ) &gt;= 0` — the half of `bInDuck` that is the
    /// model's (B437). True without the install, which leaves the flag alone.
    /// </summary>
    /// <remarks>
    /// The weapon is `GetActiveWeapon()` — the handle resolved through its serial — and its item and the player's
    /// team are what `TranslateActivity` hands the weapon's table and the item's `GetActivityOverride` (`:130-139`).
    /// </remarks>
    private static bool ModelCrouchWalks(
        EntityState player, EntityStateTable entities, IClassAnimationScripts? classes, PlayerActivityOverride table)
    {
        if (classes is null)
        {
            return true;
        }

        EntityState? weapon = entities.Resolve(player.ActiveWeaponHandle()) is { } held &&
            entities.TryGet(held, out EntityState? holding)
                ? holding
                : null;

        return classes.HasCrouchWalk(
            player.PlayerClass(), table, weapon?.ClassName, weapon?.ItemDefinitionIndex(), First(player, TeamProperties) ?? 0);
    }

    private sealed partial class Builder
    {
    /// <summary>`CTFPlayerShared::IsAiming` (`tf_player_shared.cpp:11429-11441`).</summary>
    /// <param name="player">The player.</param>
    /// <param name="weaponClass">The active weapon's server class, or null.</param>
    /// <returns>`TF_COND_AIMING` on anyone but a soldier; for a sniper with the classic rifle, `TF_COND_ZOOMED` instead.</returns>
    private static bool IsAiming(EntityState player, string? weaponClass)
    {
        PlayerConditions conditions = player.Conditions();

        if (player.PlayerClass() == SniperClass && string.Equals(weaponClass, ClassicSniperRifleClass, StringComparison.Ordinal))
        {
            return conditions.Has(PlayerConditions.Zoomed);
        }

        return conditions.Has(PlayerConditions.Aiming) && player.PlayerClass() != SoldierClass;
    }
    }

    /// <summary>`TF_CLASS_SNIPER`.</summary>
    private const int SniperClass = 2;

    /// <summary>`TF_CLASS_SOLDIER`.</summary>
    private const int SoldierClass = 3;

    /// <summary>`TF_WEAPON_SNIPERRIFLE_CLASSIC`'s server class.</summary>
    private const string ClassicSniperRifleClass = "CTFSniperRifleClassic";

    /// <summary>`m_Shared.m_iAirDash`.</summary>
    private const string AirDashProperty = "DT_TFPlayerShared.m_iAirDash";

    /// <summary>The launch era's `m_bAirDash`.</summary>
    private const string LaunchAirDashProperty = "DT_TFPlayerShared.m_bAirDash";

    /// <summary>`m_Shared.m_bCarryingObject`, which the HUD and the activity table both read.</summary>
    private const string CarryingObjectProperty = "DT_TFPlayerShared.m_bCarryingObject";

    /// <summary>The server class of the weapon <c>bIsMinigun</c> tests for.</summary>
    private const string MinigunClass = "CTFMinigun";

    private sealed partial class Builder
    {
    /// <summary>Whether a weapon class is one <c>WeaponID_IsSniperRifleOrBow</c> accepts.</summary>
    /// <param name="weaponClass">The weapon entity's server class, or null.</param>
    /// <returns>Whether it is a sniper rifle or the bow.</returns>
    /// <remarks>
    /// **`tf_weaponbase.cpp:6328` and `:6338`**, by class rather than by weapon ID: the rifle, the
    /// decapitation rifle, the classic rifle and the compound bow. A demo carries the class and not
    /// the ID, and the two are one-to-one.
    /// </remarks>
    private static bool IsSniperRifleOrBow(string? weaponClass) =>
        weaponClass is "CTFSniperRifle"
            or "CTFSniperRifleDecap"
            or "CTFSniperRifleClassic"
            or "CTFCompoundBow";
    }

    /// <summary>`DT_TFPlayer.m_bFlipViewModels`, the player's `cl_flipviewmodels` as the server copied it (B515).</summary>
    private const string FlipViewModelsProperty = "DT_TFPlayer.m_bFlipViewModels";

    private sealed partial class Builder
    {
    private static void RecordViewmodels(
        EntityStateTable entities,
        ModelPrecache precache,
        int protocol,
        int tick,
        Dictionary<int, SceneViewmodel> last,
        TailList<(int Tick, SceneViewmodel Weapon)> into,
        List<int> candidates,
        HashSet<int> touched,
        Dictionary<int, int> weaponOf)
    {
        // **Only entities already known to BE viewmodels, not every entity in the table** (B265).
        // This walked `entities.All` on every packet and asked each one for a viewmodel model
        // index: on `z1800` that is roughly six hundred entities across 14,386 packets — about
        // 8.6 million lookups, of which twenty-two ever answer. Measured before touching it, it was
        // **22.4 seconds of a 30.3-second load: 74% of the time to open a demo.**
        //
        // `RecordProp`'s own doc comment already forbade exactly this — *"Walking the whole entity
        // table every frame would ask several hundred entities to repeat themselves across a
        // hundred thousand frames"* — and it is written a few lines below a loop that did it.
        //
        // **The candidate list is a superset, filtered by the same predicate as before**, so the
        // recorded output is identical rather than approximately so: an index enters when an
        // update shows it carrying a viewmodel index (proportional to what the demo SAID, which is
        // the shape the rest of this loop already has), and an index whose entity has gone or been
        // reused simply fails the test below, exactly as it would have when walking everything.
        // Ascending order, because `All` yields in index order and the recorded list keeps
        // same-tick entries in the order they were sampled.
        for (int at = 0; at < candidates.Count; at++)
        {
            if (!entities.TryGet(candidates[at], out EntityState? entity))
            {
                continue;
            }

            // **Re-sampled only when this packet touched it, or touched the weapon it names**
            // (B265). Everything below — the model path, the item, the econ attribute lists — is
            // derived from those two entities and nothing else, so if the demo said nothing about
            // either, the sample it would produce is the one already recorded. The old code built
            // the whole thing on every packet and then threw it away on `before == weapon`: about
            // 316,000 constructions on `z1800` to detect a few hundred changes, which is the
            // second half of this method's cost after the scan.
            //
            // `RecordProp`'s own comment is the rule being applied: *"A demo states what changed;
            // this records exactly that."*
            //
            // The weapon entity is remembered from the last sample, because it is reached THROUGH
            // the viewmodel — an econ attribute changing on a weapon whose viewmodel was quiet
            // still has to be seen.
            bool sampled = last.ContainsKey(candidates[at]);

            if (sampled &&
                !touched.Contains(candidates[at]) &&
                !(weaponOf.TryGetValue(candidates[at], out int held) && touched.Contains(held)))
            {
                continue;
            }

            // Stryker disable once : a mutant that empties the guard body leaves 'rawIndex'
            // unassigned (CS0165), and Safe Mode then drops every mutation in this method — B410.
            if (entity.ViewmodelModelIndex() is not { } rawIndex)
            {
                continue;
            }

            // **Recorded even when it resolves to nothing, for the same reason a hidden one is.**
            // Model index 0 means "no model", and an unused off hand sends exactly that — all 22 of
            // z1800's do. Skipping those here would be right for a viewmodel that is always empty
            // and wrong for one that is emptied: the last sample would keep saying "watch", and the
            // lookup would answer with it for the rest of the demo. `IsOnScreen` decides instead,
            // in one place, on the latest state.
            string path = precache.Path(ModelPrecache.Unpack(rawIndex, protocol)) ?? string.Empty;

            // **Recorded whether or not it is drawn, and this is deliberate.** Skipping a hidden
            // viewmodel here would leave the last recorded sample for that entity saying "visible",
            // and the lookup walks forward keeping the last match — so a watch put away would carry
            // on being answered for the rest of the demo. The flag has to travel with the sample so
            // that the latest state is the one that wins.
            // **Which weapon the VIEWMODEL says it is holding** (B222). `DT_BaseViewModel` networks
            // `m_hWeapon` (`baseviewmodel_shared.cpp:567`), and that entity's own `m_nModelIndex` is
            // its VIEW model — the `c_` model — as this decoder already records elsewhere. So the
            // pair resolves the weapon the engine's own way, in one hop, from data the demo states
            // outright.
            //
            // What this replaces reconstructed it from the PLAYER instead: the player's
            // `m_hActiveWeapon`, that entity's item definition index, then a lookup in
            // `items_game.txt`. Three hops and a schema to reach something already on the wire, and
            // every hop able to fail on its own. Valve never asks the player; the viewmodel knows.
            //
            // Empty when the viewmodel names no weapon, which is an ordinary state — an off hand
            // holding nothing, or a viewmodel between weapons.
            // **The ITEM, not that entity's model index** — Valve builds the attachment as
            // `pItem->GetPlayerDisplayModel( iClass, team )` (`econ_entity.cpp:1167`), which is
            // `model_player` from `items_game.txt`. Taking the weapon entity's own `m_nModelIndex`
            // was tried on 2026-08-28 and drew no weapon at all: `m_hWeapon` says WHICH weapon and
            // the schema says what it looks like. Both hops are needed and they are different
            // questions.
            int? weaponItem = null;
            string? weaponClass = null;
            EconAttributeWire? weaponEcon = null;

            // Remembered so the gate above can watch it: this is the only entity besides the
            // viewmodel itself whose state reaches the sample.
            weaponOf[candidates[at]] = entity.ViewmodelWeapon() ?? -1;

            if (entity.ViewmodelWeapon() is { } weaponEntity &&
                entities.TryGet(weaponEntity, out EntityState? carried))
            {
                weaponItem = carried.ItemDefinitionIndex();
                weaponClass = carried.ClassName;

                // **The weapon's attributes travel with the viewmodel sample** (B252), read from
                // the same entity `m_hWeapon` already resolved — the festivizer on the gun in your
                // own hands is the same list the world draw reads. Null when both lists are empty,
                // so a bare weapon costs the dedup nothing.
                IReadOnlyList<EconAttributeValue> local =
                    carried.EconAttributes(EconAttributeList.Local);
                IReadOnlyList<EconAttributeValue> forDemos =
                    carried.EconAttributes(EconAttributeList.NetworkedForDemos);

                if (local.Count > 0 || forDemos.Count > 0)
                {
                    long? high = carried.Integer("DT_ScriptCreatedItem.m_iItemIDHigh");
                    long? low = carried.Integer("DT_ScriptCreatedItem.m_iItemIDLow");

                    weaponEcon = new EconAttributeWire(
                        local,
                        forDemos,
                        high is { } h && low is { } l
                            && !(h == uint.MaxValue && l == uint.MaxValue));
                }
            }

            bool seen = last.TryGetValue(entity.EntityIndex, out SceneViewmodel before);

            // **The parity counter is how the engine says "play that again"** — see
            // `ViewmodelAnimation.RestartAt` for the citation. `m_nSequence` cannot express it,
            // because firing the same weapon twice sets the same number twice, and the record
            // equality below would then record nothing at all.
            //
            // An unsent parity means unchanged rather than zero, so the previous value is carried
            // forward: reading a missing property as 0 would fake a restart every time it wrapped
            // back to something else.
            int parity = entity.ViewmodelAnimationParity()
                ?? (seen ? before.AnimationParity : 0);

            int startedAt = ViewmodelAnimation.RestartAt(
                seen ? before.AnimationParity : null,
                parity,
                seen ? before.AnimationStartTick : 0,
                tick);

            // **The owner's handedness, read with the sample** (B515). `m_bFlipViewModels` is the server's copy of the
            // owner's `cl_flipviewmodels` (`tf_gamerules.cpp:10281`); unsent is the engine's false. It is not watched
            // by the gate above: the cvar is `FCVAR_NOT_CONNECTED` (`c_baseviewmodel.cpp:44`), so it cannot change
            // while the owner is connected, and the player's own entity arrives with it before any viewmodel sample.
            int? owner = entity.ViewmodelOwner();
            bool? ownerFlips = owner is { } ownerIndex && entities.TryGet(ownerIndex, out EntityState? holder)
                ? (holder.Integer(FlipViewModelsProperty) ?? 0) != 0
                : null;

            SceneViewmodel weapon = new(
                path,
                entity.ViewmodelSequence() ?? 0,
                entity.ViewmodelPlaybackRate() ?? 1f,
                owner,
                entity.ViewmodelSlot(),
                entity.IsDrawn,
                weaponItem,
                weaponClass,
                parity,
                startedAt,
                weaponEcon,
                ownerFlips);

            // Unchanged since this entity was last sampled, so there is nothing new to record.
            if (seen && before == weapon)
            {
                continue;
            }

            last[entity.EntityIndex] = weapon;
            into.Add((tick, weapon));
        }
    }

    private static string? ModelFor(EntityState state, ModelPrecache precache, int protocol)
    {
        if (PlayerClass.Equals(state.ClassName, StringComparison.Ordinal))
        {
            return string.Empty;
        }

        // **A weapon in the world is its WORLD model, which is a different property.** A carried
        // weapon's m_nModelIndex holds the VIEW model, so reading it here drew the owner's
        // first-person arms as scenery — every weapon the player carried, all resolving to
        // c_soldier_arms.mdl, stacked at the hand on top of the real viewmodel (B160).
        //
        // m_iWorldModelIndex is sent by DT_BaseCombatWeapon (basecombatweapon_shared.cpp:2870) and
        // is what the client draws the world model from (tf_weaponbase.cpp:2144).
        //
        // Preferred rather than exclusive: an entity that is not a weapon does not declare the
        // table at all, and a weapon that somehow sent no world model still has its base index to
        // fall back on, which is no worse than what it had before.
        //
        // **A rope has no model, and its renderable is its material** (B477): `DT_RopeKeyframe` sends no
        // `m_nModelIndex`, and `C_RopeKeyframe::OnDataChanged` draws the material `m_iRopeMaterialModelIndex` names
        // (`c_rope.cpp:1294-1309`). Carried as the track's model path, it is what admits the rope and what loads the
        // material it is drawn with.
        int? index = state.WorldModelIndex() ?? state.ModelIndex() ?? state.RopeMaterialIndex();

        // The engine's own compatibility shim: protocol 20 and below packed indices below -1.
        // See ModelPrecache.Unpack and docs/findings/19-model-indices.md.
        return index is { } rawIndex
            ? precache.Path(ModelPrecache.Unpack(rawIndex, protocol))
            : null;
    }

    /// <summary>The eight <c>localSound</c> slots an entity carries, in order.</summary>
    /// <remarks>
    /// All eight always, rather than only the ones <c>localBits</c> marks used: the bits and the
    /// vectors are separate properties and either can arrive without the other, so keeping the raw
    /// slots lets a reader see that rather than having it decided here.
    /// </remarks>
    private static (float X, float Y, float Z)?[] Positions(EntityState entity)
    {
        (float X, float Y, float Z)?[] slots = new (float X, float Y, float Z)?[8];

        for (int slot = 0; slot < slots.Length; slot++)
        {
            slots[slot] = entity.SoundscapePosition(slot);
        }

        return slots;
    }
    }

    /// <summary>Everything the player had equipped at death, for the corpse's body (B395).</summary>
    /// <param name="tracks">Every track, to find the player's bone-merged children.</param>
    /// <param name="player">The dead player's entity index.</param>
    /// <param name="disguised">Whether the corpse wears its disguise's gear.</param>
    /// <param name="activeWeapon">The entity he was holding, or null if it cannot be read.</param>
    /// <returns>Each equipped item with its kind and whether it was deployed, or null.</returns>
    /// <remarks>
    /// **The same walk as <see cref="Builder.WornAtDeath"/> and deliberately NOT the same filter.** That
    /// one answers "what hangs off the corpse", which the engine takes from the econ wearable list
    /// (`c_tf_player.cpp:10178`), so it drops weapons. This answers "what was the player's body",
    /// which `RecalculatePlayerBodygroups` builds from weapons AND wearables
    /// (`tf_player_shared.cpp:13693-13709`). Two questions, two sets; filtering one list to serve
    /// both is wrong in one direction or the other.
    ///
    /// **The kind is taken from the class name**, the same distinction `WornAtDeath` draws —
    /// `CEconWearable` against `CTFWeaponBase` — because a weapon's own carry state is per-tick and
    /// the class is not.
    ///
    /// **`activeWeapon` is the entity the player held at the death tick**, which the deployed-only
    /// pass needs. Null when the recorder never saw it; those items then contribute nothing, which
    /// is what the engine does for a weapon that is not the active one.
    /// </remarks>
    /// <remarks>
    /// **Internal rather than private so its branches can be tested directly.** It is a pure
    /// function of the tracks, the player and the held weapon — the surrounding `Build` needs a
    /// whole demo, and the interesting cases here (a weapon against a wearable, a disguise
    /// mismatch, a track with no item index) are cheaper and more exact stated as inputs.
    /// </remarks>
    internal static List<SceneCarriedItem>? CarriedAtDeath(
        Dictionary<int, ScenePropTrack> tracks, int? player, bool disguised, int? activeWeapon)
    {
        if (player is not { } owner)
        {
            return null;
        }

        List<SceneCarriedItem>? carried = null;

        foreach (ScenePropTrack track in tracks.Values)
        {
            // Stryker disable all : the guard condition spans several lines, so a mutant that
            // empties the guard body leaves 'definition' unassigned at its use below (CS0165), and
            // Safe Mode then drops every mutation in this method — B410.
            if (track.AttachedTo != owner ||
                !track.BoneMerged ||
                track.OfDisguise != disguised ||
                track.ItemDefinitionIndex is not { } definition)
            {
                continue;
            }

            // Stryker restore all

            bool wearable =
                track.ClassName.Contains("Wearable", StringComparison.Ordinal) ||
                track.ClassName.Contains("PowerupBottle", StringComparison.Ordinal);

            carried ??= [];
            carried.Add(new SceneCarriedItem(
                definition,
                Weapon: !wearable,
                Deployed: activeWeapon is { } held && held == track.EntityIndex));
        }

        return carried;
    }

    private sealed partial class Builder
    {
    /// <summary>What a player was wearing, for the corpse being made of them.</summary>
    /// <param name="tracks">Every prop track so far.</param>
    /// <param name="player">The dying player's entity index, or null when it is unknown.</param>
    /// <param name="disguised">The corpse's <c>m_bWasDisguised</c>.</param>
    /// <returns>The models to hang on the corpse, or null when there are none.</returns>
    /// <remarks>
    /// **`CreateBoneAttachmentsFromWearables` walks the player and skips four kinds** — a viewmodel
    /// wearable, a disguise mismatch, one hidden with `EF_NODRAW`, and one whose drop type makes it
    /// a falling gib instead (`c_tf_player.cpp:10169-10251`). Two of those are applied here:
    /// bone-merged world wearables only, and the disguise pairing.
    ///
    /// **The disguise rule is a pairing, not a filter.** The engine takes disguise wearables when
    /// the corpse's <c>m_bWasDisguised</c> is set and ordinary ones when it is not — a pair of
    /// mirrored `continue`s on `IsDisguiseWearable()`. A spy who died disguised leaves a corpse
    /// wearing the disguise's gear, which is the point of the branch.
    ///
    /// **Not applied, and stated rather than left to be found:** the `EF_NODRAW` skip (a Razorback
    /// that was inactive), the drop-type skip, and the decapitation rule that drops head and misc
    /// slots. Each needs the item schema or per-tick effect flags at a point that has neither.
    /// </remarks>
    private static List<SceneWornItem>? WornAtDeath(
        Dictionary<int, ScenePropTrack> tracks, int? player, bool disguised)
    {
        if (player is not { } owner)
        {
            return null;
        }

        List<SceneWornItem>? worn = null;

        foreach (ScenePropTrack track in tracks.Values)
        {
            if (track.AttachedTo != owner ||
                !track.BoneMerged ||
                track.OfDisguise != disguised ||
                string.IsNullOrEmpty(track.ModelPath))
            {
                continue;
            }

            // **A WEAPON is not a wearable, and the first version of this put all four of a
            // demoman's on his corpse.** `CreateBoneAttachmentsFromWearables` walks
            // `GetWearable( wbl )` (`c_tf_player.cpp:10178`) — the econ wearable list — and a
            // weapon is a `CTFWeaponBase`, which is not in it. Every bone-merged child of a player
            // is not the same set: it also holds the grenade launcher, the sticky launcher, the
            // bottle, and whatever else is holstered.
            //
            // The class name is the distinction the engine draws — `CEconWearable` against
            // `CTFWeaponBase` — and it is on the track, where a weapon's per-tick carry state is
            // not.
            if (!track.ClassName.Contains("Wearable", StringComparison.Ordinal) &&
                !track.ClassName.Contains("PowerupBottle", StringComparison.Ordinal))
            {
                continue;
            }

            worn ??= [];
            worn.Add(new SceneWornItem(track.ModelPath, track.ItemDefinitionIndex));
        }

        return worn;
    }

    /// <summary>Closes a corpse's window at the tick it stopped being drawable.</summary>
    /// <param name="corpses">The corpses recorded so far, by entity slot.</param>
    /// <param name="entityIndex">The slot that left or was destroyed.</param>
    /// <param name="tick">When.</param>
    /// <remarks>
    /// **Both callers reach here for the same reason and neither is the last UPDATE.** A corpse is
    /// spoken about once and then simulated on the client, so the last thing said about it is
    /// almost always its creation — 158 of 159 in `serveme-627619-stv-2026-08-07`. What ends it is
    /// leaving the visible set or being destroyed.
    /// </remarks>
    private static void EndCorpse(
        Dictionary<int, SceneRagdoll> corpses, int entityIndex, int tick)
    {
        if (corpses.TryGetValue(entityIndex, out SceneRagdoll dying))
        {
            corpses[entityIndex] = dying with { LastTick = tick };
        }
    }

    private static void RecordProp(
        DecodedEntity entity,
        EntityStateTable entities,
        ModelPrecache precache,
        Dictionary<int, ScenePropTrack> tracks,
        List<ScenePropTrack> props,
        List<ScenePropTrack> players,
        HashSet<int> mergesItself,
        HashSet<int> combatWeapons,
        int protocol,
        int tick,
        float interval,
        int[] simulationLag,
        int[] animationLag,
        int[] clockGap,
        Dictionary<string, int[]> lagByClass,
        Dictionary<int, SceneRagdoll> corpses,
        TailList<SceneRagdoll> replaced,
        Dictionary<int, int> lastHeld)
    {
        // **A corpse is captured here and never becomes a prop track**, because it has no model
        // index to make one from — `DT_TFRagdoll` is `NOBASE` (`PARITY-AUDIT.md` #4). Recorded on
        // every update rather than on the first, since a ragdoll enters before its origin arrives
        // and the last word is the one that placed it.
        if (entities.TryGet(entity.EntityIndex, out EntityState? corpse) &&
            RagdollClass.Equals(corpse.ClassName, StringComparison.Ordinal))
        {
            // **A slot is REUSED, so an Enter on an occupied one is a different corpse** (B92, in
            // its own words: an entity may change while remaining at its index). Keying only by
            // index collapsed the two into one and undercounted a match's dead by more than half —
            // 87 reported against the 299 `PARITY-AUDIT.md` #4 measured. The serial is what tells
            // them apart, which is the same field `ScenePropTrack.Continues` exists for.
            if (corpses.TryGetValue(entity.EntityIndex, out SceneRagdoll standing) &&
                standing.Serial != corpse.SerialNumber)
            {
                // **Ended where the slot was taken, not where it was last updated.** Both corpses
                // carry the same entity index, so a window that includes the moment of handover puts
                // two props at one index in one frame — and everything downstream keys per-entity
                // caches by that index. The successor's arrival IS the predecessor's end; taking the
                // earlier tick would be inventing a gap, and taking a later one a collision.
                replaced.Add(standing with { LastTick = Math.Min(standing.LastTick, tick - 1) });
            }

            (int? playerClass, int? team, bool gib, bool burning, bool feign, bool disguised) =
                corpse.Ragdoll();

            (float x, float y, float z) = corpse.RagdollOrigin() ?? (0f, 0f, 0f);

            // **The first tick is kept from the earlier record, not re-read.** A corpse is recorded
            // on every update rather than only its first, so taking `tick` unconditionally would
            // walk its arrival forward to the last thing said about it and no corpse would ever be
            // drawn before it stopped being spoken about.
            int born = corpses.TryGetValue(entity.EntityIndex, out SceneRagdoll earlier) &&
                earlier.Serial == corpse.SerialNumber
                ? earlier.FirstTick
                : tick;

            // **The corpse faces the way the PLAYER faced**, and only the player entity knows —
            // `SetAbsAngles( pPlayer->GetRenderAngles() )` (`c_tf_player.cpp:766`). Resolved rather
            // than masked, because the low 11 bits of an invalid handle name a real slot (B231);
            // and the yaw is kept from the earlier record when the player has since gone, since a
            // corpse does not turn to face north the moment its owner disconnects.
            // Kept from the earlier record when the player can no longer be found, since a corpse
            // does not turn to face north the moment its owner disconnects.
            float facing = corpses.TryGetValue(entity.EntityIndex, out SceneRagdoll facedBefore) &&
                facedBefore.Serial == corpse.SerialNumber
                ? facedBefore.Yaw
                : 0f;

            // **Two names for the player, and two encodings** (B319). Newer builds send
            // `m_hPlayer`, a packed ehandle that must be resolved; older ones send
            // `m_iPlayerIndex`, the entity index itself. Measured: the STV demo carries handles in
            // the hundreds of thousands, `z1800` carries 2, 3, 4. Reading either as the other gives
            // a plausible number and the wrong player, so each is decoded as what it is.
            int? deadIndex =
                entities.Resolve(corpse.RagdollPlayerHandle()) ?? corpse.RagdollPlayerIndex();

            if (deadIndex is { } player &&
                entities.TryGet(player, out EntityState? dead) &&
                dead.EyeAngles() is { } looking)
            {
                facing = looking.Yaw;
            }

            // **The cosmetics are captured HERE, at the moment of death, because they are gone
            // afterwards** (B320). That is also exactly when the engine takes them:
            // `CreateBoneAttachmentsFromWearables` runs from inside `CreateTFRagdoll` and walks the
            // PLAYER's wearable list. A corpse lasts seconds and its owner respawns wearing the same
            // hats on a new entity, so reading them later finds either nothing or somebody's living
            // gear.
            //
            // Measured on `serveme-627619-stv-2026-08-07`: 150 of 159 corpses have their player's
            // wearables present on the tick they appear, 531 in total. The nine that do not are
            // players the recorder could not see.
            IReadOnlyList<SceneWornItem>? worn =
                facedBefore.Serial == corpse.SerialNumber && facedBefore.Worn is { } already
                    ? already
                    : WornAtDeath(tracks, deadIndex, disguised);

            // **The body's own set, captured at the same moment and for the same reason** (B395).
            // `m_nBody` is `pPlayer->GetBody()` copied whole (`c_tf_player.cpp:790-793`), and the
            // player's body is built from weapons as well as wearables — so this is a wider walk
            // than `worn`, with the held weapon marked for the deployed-only pass.
            //
            // **The held weapon is read separately from the eye angles above**, whose `dead` is
            // scoped to that pattern — and a corpse whose facing could not be read still has a
            // body worth building.
            // **The tick's own value first, then the last one seen while he was alive.** Measured
            // on `demostf-cp_process_f12-2026-08-07`: the death tick answers for 15 of 204, so
            // without the fallback the deployed-only pass would almost never run.
            int? held = null;

            if (deadIndex is { } holder)
            {
                held = entities.TryGet(holder, out EntityState? carrying)
                    ? entities.Resolve(carrying.ActiveWeaponHandle())
                    : null;

                if (held is null && lastHeld.TryGetValue(holder, out int seen))
                {
                    held = seen;
                }
            }

            IReadOnlyList<SceneCarriedItem>? carried =
                facedBefore.Serial == corpse.SerialNumber && facedBefore.Carried is { } before
                    ? before
                    : CarriedAtDeath(tracks, deadIndex, disguised, held);

            corpses[entity.EntityIndex] = new SceneRagdoll(
                entity.EntityIndex, corpse.SerialNumber, playerClass, team, x, y, z,
                gib, burning, feign, disguised, born, tick, facing, corpse.DamageCustom(),
                deadIndex ?? facedBefore.PlayerIndex, worn, corpse.RagdollOnGround(),
                corpse.RagdollGold(), corpse.RagdollIce(),

                // **The three initial conditions the simulation needs**, carried as they arrived
                // rather than combined here: the kill's impulse, the player's own motion, and which
                // body took the hit. `CreateTFRagdoll` keeps them apart too — the force goes to
                // `RagdollCreate` and the velocity to `SetAbsVelocity` — and folding them together
                // would lose the one that scales by mass.
                corpse.RagdollForce(), corpse.RagdollVelocity(), corpse.RagdollForceBone(),
                carried, corpse.RagdollCloaked());
        }

        if (entity.UpdateType == EntityUpdateType.Delete)
        {
            if (tracks.Remove(entity.EntityIndex, out ScenePropTrack? finished))
            {
                finished.End(tick);
            }

            // **A corpse ends when it is DELETED, not when it was last spoken about**, and the
            // difference is the whole feature. Measured on `serveme-627619-stv-2026-08-07`: 158 of
            // 159 corpses receive exactly ONE update. That is not a decode gap — everything
            // `DT_TFRagdoll` carries is a fact about the moment of death, and the corpse is a
            // client-side simulation afterwards, so the server has nothing more to say. Ending the
            // window at the last update therefore drew each corpse for a single frame, which is
            // indistinguishable from not drawing it at all.
            //
            // The delete is reached here rather than in the block above because `entities.Apply`
            // has already removed the entity by now, so the class can no longer be asked for — the
            // index is all that is left, and it is enough.
            EndCorpse(corpses, entity.EntityIndex, tick);

            return;
        }

        // **A corpse that LEAVES the visible set stops being drawn, and that is the engine's own
        // behaviour rather than a shortcut.** An entity outside the PVS is dormant on the client
        // and nothing dormant is rendered. It matters here because a SourceTV recording almost
        // never DELETES a ragdoll — the server keeps one per player until that player next dies
        // (`UTIL_Remove`, `tf_player.cpp:15602`) — so ending only at deletes let corpses pile up
        // across the match: 36 alive at a quarter through, 42 at half, 56 at three quarters,
        // against a twelve-player roster. Measured, `corpses` probe.
        //
        // **A corpse that re-enters extends its window across the gap**, which is a known and
        // deliberate imprecision: it was not visible to the recorder in between, so the demo says
        // nothing about it, and this viewer's free camera can look where SourceTV's could not
        // (`docs/memory/author-the-specimen-the-corpus-lacks.md#pov-demos-are-pvs-limited`). Two intervals per corpse would say it
        // exactly; one interval says it generously and never invents a corpse that was not there.
        if (entity.UpdateType == EntityUpdateType.Leave)
        {
            EndCorpse(corpses, entity.EntityIndex, tick);
        }

        // **Entity zero is the world and the client never draws it as an entity.**
        // C_BaseEntity::ShouldDraw ends `&& (index != 0)` at c_baseentity.cpp:1450, so the
        // exclusion is by index rather than by anything about the model: CWorld holds model
        // index 1, `maps/<name>.bsp`, which is an ordinary brush model naming submodel zero.
        //
        // It never reached here before instance baselines were applied (B132) — the world states
        // its model once, in its class baseline, and never again — so a track for the entire map
        // appeared the moment that was fixed. Drawn, it would be the world laid over the world.
        if (entity.EntityIndex == 0)
        {
            return;
        }

        if (!entities.TryGet(entity.EntityIndex, out EntityState? state))
        {
            return;
        }

        // **A viewmodel is never scenery, and the engine says so in the demo case specifically.**
        // C_BaseViewModel::ShouldDraw, c_baseviewmodel.cpp:277:
        //
        //     if ( engine->IsHLTV() )
        //     {
        //         return ( HLTVCamera()->GetMode() == OBS_MODE_IN_EYE &&
        //                  HLTVCamera()->GetPrimaryTarget() == GetOwner() );
        //     }
        //
        // In eye, and owned by the player being watched — otherwise not drawn at all. There is no
        // camera from which one of these is part of the world, so it does not belong in the prop
        // list at all; the viewmodel pass resolves its own models through ViewmodelAt.
        //
        // **Left in, it draws the weapon twice.** Measured on movement-test-pov-cp_process: three
        // tracks carrying c_soldier_arms.mdl, a model that exists only to sit in front of a
        // first-person camera. Those instances land at the player's eye alongside the ones the
        // viewmodel pass puts there, which is the "2 sticky launchers overlapping each other" the
        // owner reported — and it happens in SourceTV recordings as well, because the branch above
        // is the HLTV branch.
        //
        // **Keyed on the TABLE rather than on the model path**, which matters: a viewmodel declares
        // DT_BaseViewModel and nothing else does, whereas an arms model is merely the most obvious
        // symptom. Excluding by name would leave every weapon viewmodel behind — those carry the
        // same c_ model as the world weapon and are indistinguishable by path.
        // **A viewmodel is never scenery, and the engine says so in the demo case specifically.**
        // C_BaseViewModel::ShouldDraw, c_baseviewmodel.cpp:277:
        //
        //     if ( engine->IsHLTV() )
        //     {
        //         return ( HLTVCamera()->GetMode() == OBS_MODE_IN_EYE &&
        //                  HLTVCamera()->GetPrimaryTarget() == GetOwner() );
        //     }
        //
        // In eye, and owned by the player being watched — otherwise not drawn at all. There is no
        // camera from which one of these is part of the world, and the viewmodel pass resolves its
        // own models through ViewmodelAt.
        //
        // **Belt and braces rather than the load-bearing guard.** DT_BaseViewModel is
        // BEGIN_NETWORK_TABLE_NOBASE, so a viewmodel sends no origin and no parent and is already
        // dropped by the transform check below. This states the rule where it belongs anyway, so
        // that a later change giving these entities a position does not silently put them in the
        // world.
        if (state.ViewmodelModelIndex() is not null)
        {
            return;
        }

        // **No origin is an answer for an attached entity, not a gap.** A hat, a badge and a
        // carried weapon are attached with FollowEntity, which sets EF_BONEMERGE and then zeroes
        // local origin and angles (shared/baseentity_shared.cpp:2360) — the client matches the
        // child model's bones to the parent's BY NAME and takes the parent's matrices, so the
        // child never has a transform and the engine sends none.
        //
        // Requiring one therefore dropped every cosmetic in every demo: measured on cp_process,
        // all 37 live CTFWearable entities carry a model, an owner, a skin and a team, and no
        // position whatsoever. They are recorded at the origin because that is literally what
        // SetLocalOrigin( vec3_origin ) put there; the owner is what says where to draw them.
        // **Attachment is asked FIRST, and the order is the whole of B173's weapon half.** A
        // networked origin is a LOCAL origin — relative to the parent when there is one — so for an
        // attached entity it says nothing about where the thing is in the world. `FollowEntity`
        // ends with `SetLocalOrigin( vec3_origin )` (baseentity_shared.cpp:2371), so a carried
        // weapon sends an origin, and that origin is exactly (0,0,0).
        //
        // Tested the other way round, that zero satisfied the first branch and `Attachment` was
        // never called at all. Measured in the viewer: 17 weapons in the scene, 17 instanced, 14
        // owned, **0 attached, 14 at the world origin** — every carried weapon piled on the map's
        // origin while the three unowned ones, which send a real position, drew correctly.
        //
        // Cosmetics escaped it only by accident: a `CTFWearable` sends no origin whatsoever, so it
        // fell through to the attachment branch and worked. That is why hats were fixed and weapons
        // were not, and why the comment above — "the owner is what says where to draw them" —
        // described a rule the code did not follow.
        int? attachedTo = null;
        (float X, float Y, float Z) origin;

        // **Both halves, and neither alone is the answer** (B231).
        //
        // `CalcAbsolutePosition` (`c_baseentity.cpp:4387`) tests `EF_BONEMERGE` to choose between
        // riding a parent's SKELETON and concatenating onto its transform. Weapons carry that flag
        // on the wire; wearables do not, because `CEconWearable::Spawn` adds it on the CLIENT for
        // every wearable any client creates (`econ_wearable.cpp:112`, outside the server-only
        // guard) and so it never needs to travel.
        //
        // Measured on a real match: 26 of 26 `CTFWearable` and 3 of 3 `CTFPowerupBottle` send no
        // `m_fEffects` at all, while every weapon sends the flag. Reading only the wire puts every
        // hat and cosmetic on the transform path — which broke the viewer outright — and treating
        // every parent as a merge is the mistake in the other direction, which leaves a
        // `CDynamicProp` hung on a `func_door` searching for a skeleton brushwork does not have.
        bool boneMerged = state.IsBoneMerged || mergesItself.Contains(entity.ClassId);

        // **Resolved through the table, so the handle's SERIAL is checked** (B231).
        // `RecvProxy_IntToEHandle` keeps index and serial and dereferencing compares the serial
        // against the slot's current occupant; masking it away resolves a dangling handle to a
        // real, existing, different entity. Measured: a spawn resupply locker composed onto a door
        // because its parent slot had been reused, landing thousands of units away.
        if (entities.Resolve(state.AttachmentHandle()) is { } owner)
        {
            attachedTo = owner;

            // **A bone-merged follower's own origin is meaningless and a parented one's is not.**
            // `FollowEntity` calls `SetLocalOrigin( vec3_origin )` on the follower
            // (`baseentity_shared.cpp:2371`), so a merged entity genuinely sends zero and the
            // bones place it; everything else sends its OFFSET from the parent, which is the value
            // `MatrixSetColumn( GetLocalOrigin(), 3, matEntityToParent )` needs. Zeroing both
            // discards the second.
            origin = boneMerged ? (0f, 0f, 0f) : state.Origin() ?? (0f, 0f, 0f);
        }
        else if (state.Origin() is { } placed)
        {
            origin = placed;
        }
        else
        {
            return;
        }

        // **A player's model is not on the wire, so it cannot be resolved here.**
        // CTFPlayerClassShared::GetModelName looks it up locally from m_iClass through the class
        // data table, and only m_iszCustomModel travels. A player therefore sends no
        // m_nModelIndex and gets a track with no model rather than no track at all - the poses are
        // what the interpolator needs, and the model is the viewer's to resolve from the install.
        string? model = ModelFor(state, precache, protocol);

        // **Which econ item this is, when it is one.** A weapon's model comes from the item schema
        // rather than from the wire — `pItem->GetPlayerDisplayModel( iClass, team )`,
        // `econ_entity.cpp:1167` — and some weapons network no model index at all. Measured on
        // `cp_fulgur`, every weapon with an owner: a rocket launcher sends both indices, a
        // flamethrower sends the world model, and every `CWeaponMedigun` sends NEITHER, while all
        // of them state their item. 211 is the stock Medi Gun.
        int? item = state.ItemDefinitionIndex();

        // **A null model is not the end of the road for something that says which ITEM it is**
        // (B231). This returned outright, so a medigun — no `m_nModelIndex`, no
        // `m_iWorldModelIndex`, item 211 — produced no track at all, and every medigun on every
        // other player went undrawn. `WeaponModels.For` has resolved exactly this for the viewmodel
        // and the followed player since B222; the weapon entities other players carry were the one
        // caller that never asked.
        if (model is null && item is null)
        {
            return;
        }

        // **A slot is reused, and the SERIAL NUMBER is what identifies the occupant** — the engine's
        // own rule, and the one EntityStateTable already applies to entity state. A rocket that
        // explodes frees its index for the next one, and appending that one's positions to the old
        // track would draw a rocket flying between two unrelated places.
        //
        // This compared MODEL PATHS until 2026-08-16 (B92), which was wrong in both directions. Two
        // consecutive rockets share a model, so the case described above — the one the check existed
        // for — was exactly the case it could not see. And an entity may change model while
        // remaining itself: team_control_point.cpp:569 calls SetModel on every capture, so a point
        // changing hands ended its track and split one object into several.
        if (tracks.TryGetValue(entity.EntityIndex, out ScenePropTrack? track) &&
            !track.Continues(state.SerialNumber))
        {
            track.End(tick);
            tracks.Remove(entity.EntityIndex);
            track = null;
        }

        if (track is null)
        {
            // Empty rather than null for a weapon whose model is not on the wire yet: the track's
            // path is a string, and `Follow` fills it in when a later update names one.
            track = new ScenePropTrack(
                entity.EntityIndex, model ?? string.Empty, state.SerialNumber);
            tracks[entity.EntityIndex] = track;

            // Player tracks are kept apart from Props. They carry poses and no model, so a
            // consumer walking Props to draw models would find one it cannot draw and could only
            // report as a missing asset - which is exactly the false alarm this split avoids.
            // **An ITEM is a model the draw path can still find, so it crosses over** (B231). The
            // reasoning above is right about a player, whose model-less track can never resolve to
            // anything, and wrong about a weapon whose model is merely not on the wire: `items_game`
            // names it, `WeaponModels.For` reads it, and the only thing standing between them was
            // this line putting the track where nothing drawing models ever looks.
            //
            // Narrow deliberately — an item index, not merely "has an owner" — so the false
            // missing-asset alarm the split exists to prevent stays prevented. A track with no
            // model AND no item still has nothing anyone could resolve.
            (string.IsNullOrEmpty(model) && item is null ? players : props).Add(track);
        }

        // Kept current for the same reason the model is: an entity can be created from a baseline
        // that omits its item and be told which one it is on a later update.
        track.ItemDefinitionIndex = item ?? track.ItemDefinitionIndex;
        track.ClassName = state.ClassName ?? track.ClassName;

        // **The wire's half of `IterateAttributes`, recomputed only when this update touched an
        // attribute element** (B234). The accessor walks the whole property bag, so running it on
        // every positional delta would pay for fifty thousand scans a demo; an update that carried
        // no element-scoped property cannot have changed either list.
        foreach (DecodedProperty carried in entity.Properties)
        {
            if (!carried.Definition.ElementScoped)
            {
                continue;
            }

            track.Econ = EconWire(state);

            break;
        }

        // **The flag as the entity holds it, not sticky** (B548). `m_bDisguiseWearable` is a plain networked bool
        // (`tf_item_wearable.h:45`, `SetDisguiseWearable( bool )`), so the client holds what was last received; the
        // accumulated state already keeps it across updates that do not mention it, which is all the old `||` bought.
        track.OfDisguise = state.OfDisguise();

        // Kept current for the same reason: a brush entity states its team on creation and an
        // update that does not mention it must not erase it.
        track.TeamNumber = First(state, TeamProperties) ?? track.TeamNumber;

        // Kept per tick, because a door can turn non-solid and back: what the recorder's movement trace meets (B450).
        if (Collision(state) is { } collision)
        {
            track.Collide(tick, collision);
        }

        // **A parity change means this animation began again, so its clock restarts**
        // (`C_BaseAnimating::OnDataChanged`, `c_baseanimating.cpp:4737`). Everything downstream
        // measures `elapsed = seconds - AnimationStartSeconds`, and leaving that at zero made
        // `elapsed` the whole recording — a one-shot sequence finished before its first frame drew.
        //
        // **A counter rather than a comparison of sequence numbers**, because a cabinet used twice
        // plays `open` twice and only the counter says the second one began
        // (`m_nNewSequenceParity = ( m_nNewSequenceParity + 1 ) & EF_PARITY_MASK`, `:5574`).
        //
        // An entity that never sends the field keeps the clock it was created with, which is right:
        // nothing has told it to restart.
        // **Two signals, because the engine has two modes and reads a different field in each.**
        // `C_BaseAnimating::OnDataChanged` (`c_baseanimating.cpp:5021`) checks
        // `m_bClientSideFrameReset` ONLY when `m_bClientSideAnimation` is set, and resets cycle
        // interpolation on `m_nNewSequenceParity` (`:4737`) regardless. Measured on `cp_fulgur`, the
        // spawn cabinets are client-side animated — they send `m_bClientSideAnimation` 1 and no
        // `DT_ServerAnimationData.m_flCycle` whatsoever — so the toggle is their restart, and a fix
        // built on parity alone did not move them.
        //
        // **The frame reset is a TOGGLE and only its CHANGE means anything.**
        // `CBaseAnimating::ResetClientsideFrame` is `m_bClientSideFrameReset =
        // !(bool)m_bClientSideFrameReset` (`server/baseanimating.cpp:3055`), so reading it as a
        // boolean "should reset" would restart on every update where it happened to be one.
        // **A first sighting is not a restart.** An entity's opening update states whatever value
        // it holds, and treating that as a change would stamp the clock for every prop the moment
        // it appears — wrong for one that has been idling since the map loaded.
        bool restarted = false;

        // **Held until after the keyframe is added, because the engine's reset runs after the latch.**
        // `C_BaseAnimating::PostDataUpdate` calls `BaseClass::PostDataUpdate` FIRST — that is where
        // `OnLatchInterpolatedVariables` appends the entry (`c_baseentity.cpp:2584`) — and only then does
        // the parity block reach `m_iv_flCycle.Reset()` (`c_baseanimating.cpp:4738`). So the reset wipes
        // the entry that has just landed and re-seeds from the value that came with it. Resetting first
        // would seed from the PREVIOUS update's cycle, which is a different animation's.
        bool newSequenceStarted = false;

        if (state.NewSequenceParity() is { } parity)
        {
            bool newSequence = track.LastSequenceParity is not null
                && parity != track.LastSequenceParity;

            restarted |= newSequence;
            newSequenceStarted = newSequence;
            track.LastSequenceParity = parity;
        }

        // **Both are consumed even once the first has fired**, because the stored value must track
        // the wire whether or not anyone acted on it, or the next genuine change is missed.
        //
        // **And the toggle counts only in CLIENT-side mode**, which is the guard Valve puts around
        // it and which a first version of this left out — caught by auditing every EntityState
        // accessor for a production caller and finding `ClientSideAnimation` had none.
        // `c_baseanimating.cpp:5021`:
        //
        //     if ( m_bClientSideAnimation )
        //         if ( m_bClientSideFrameReset != m_bLastClientSideFrameReset )
        //             ResetClientsideFrame();
        //
        // A server-animated entity that toggles the field would otherwise restart on it, which is
        // the same class of mistake as reading the toggle's VALUE instead of its change.
        // **Kept current on every update, as PostDataUpdate joins and leaves the list on every
        // update** (B259). A server-animated entity that is later told to animate itself must start
        // being advanced, and one told to stop must stop.
        if (state.ClientSideAnimation() is { } clientSide)
        {
            track.ClientSideAnimated = clientSide != 0;
        }

        if (state.ClientSideFrameReset() is { } reset)
        {
            restarted |= track.LastFrameReset is not null
                && reset != track.LastFrameReset
                && state.ClientSideAnimation() is not 0 and not null;

            track.LastFrameReset = reset;
        }

        if (restarted)
        {
            track.AnimationStartSeconds = tick * interval;
        }

        // **A discontinuity is a SEPARATE signal from a restart, and it must not be folded into
        // `restarted`** (B346). They gate different things: a restart says the animation began
        // again and creates a transition, while this says the entity JUMPED and destroys the queue.
        // Folding them would make every teleport also restart the animation, which the engine
        // does not do — `IncrementInterpolationFrame` touches no sequence state at all
        // (`baseentity.cpp:8471`).
        //
        // **Kept current whether or not it fired, like both signals above**, since the stored value
        // must track the wire or the next genuine change is missed. And the FIRST value seen is not
        // a change: an entity entering the world already carries whatever parity it had.
        if (state.NoInterpolationParity() is { } noInterp)
        {
            if (track.LastNoInterpolationParity is not null &&
                noInterp != track.LastNoInterpolationParity)
            {
                track.DiscontinuitySeconds = tick * interval;
            }

            track.LastNoInterpolationParity = noInterp;
        }

        // **The chamber's clock, stamped the frame the two tubes first differ** (B348), which is
        // exactly what `OnDataChanged` does (`tf_weapon_grenadelauncher.cpp:626`):
        //
        //     if ( m_bCurrentAndGoalTubeEqual && m_iCurrentTube != m_iGoalTube )
        //         m_flBarrelRotateBeginTime = gpGlobals->curtime;
        //     m_bCurrentAndGoalTubeEqual = ( m_iCurrentTube == m_iGoalTube );
        //
        // **The `m_bCurrentAndGoalTubeEqual` half is the whole guard.** Stamping whenever they
        // differ would restart the animation on every packet that still shows them apart, freezing
        // the chamber a few degrees from where it set off.
        if (state.ChamberTubes() is { } chamber)
        {
            if (track.ChamberWasSettled && chamber.Current != chamber.Goal)
            {
                track.ChamberStartedSeconds = tick * interval;
            }

            track.ChamberWasSettled = chamber.Current == chamber.Goal;
        }

        // **The model, kept current for the same reason and on the engine's own adjacent line.**
        // `C_BaseEntity::PostDataUpdate` (`c_baseentity.cpp:2603`) calls `HierarchySetParent` and
        // then `ValidateModelIndex`, both ABOVE the `DATA_UPDATE_CREATED` test, so both run on
        // every update. This followed the parent and fixed the model at construction.
        //
        // A creating update rarely carries the model, because it is a delta against the class
        // INSTANCE BASELINE — one representative entity's state. Measured on `cp_fulgur`, slot 432,
        // the BLU spawn's windowed door: created from two properties with the baseline's model
        // index 1154 (`resupply_locker.mdl`) and the baseline's origin (3440 -2096 240), which is
        // `prop_locker_blu_5`'s world position out of the map. The next update said 1177 and
        // (2 0 -59), and the track never heard it. Nine other entities took the same identity.
        track.Follow(model);

        // Kept current rather than set once: a wearable can arrive before its owner handle does,
        // and a track stuck on the first answer would draw the hat on whoever wore it last.
        track.AttachedTo = attachedTo;

        // Kept current alongside the parent, because an entity can gain or lose the flag on a
        // later delta and the branch it takes has to follow.
        track.BoneMerged = boneMerged;

        // Ownership regardless of attachment, because the first-person view hides a followed
        // player's weapon by OWNER and a carried weapon that sends an origin is parented to nobody.
        track.OwnedBy = state.Owner();

        // **The weapon state is written into the POSE below rather than here** (B244). The comment
        // this replaces said "kept current … a state fixed at the first delta would freeze whichever
        // weapon happened to be out when the track began" — which had the hazard exactly right and
        // the cure exactly backwards. Keeping a scalar current while parsing does not make it
        // current when READ: by then it holds the last value the whole demo wrote, so the state
        // froze at the demo's end instead of at its beginning.

        // **Which point on the wearer, for the items that hang from one rather than merging.**
        // Kept current for the same reason the wearer is: it can arrive on a later delta than the
        // model, and a track fixed at the first answer would leave the item wherever it started.
        track.AttachmentPoint = state.ParentAttachment();

        // **Every identity field set above, dated** (B548): read at a tick, the scalars answer the demo's end.
        track.Stamp(tick);

        (float pitch, float yaw, float roll) = state.Angles() ?? (0f, 0f, 0f);

        // **A player faces where its EYES point, not where m_angRotation says.** A player's
        // m_angRotation is not networked, so reading it gives zero for every player in every demo
        // - measured across the whole corpus as exactly one distinct yaw, twenty-four players
        // included. What TF2 sends is m_angEyeAngles, as two independent properties
        // (tf_player.cpp:731):
        //
        //   SendPropFloat( SENDINFO_VECTORELEM(m_angEyeAngles, 0), 8, SPROP_CHANGES_OFTEN, -90, 90 )
        //   SendPropAngle( SENDINFO_VECTORELEM(m_angEyeAngles, 1), 10, SPROP_CHANGES_OFTEN )
        //
        // And the eye yaw is what drives the body: the server feeds its animation state from it
        // directly, `m_PlayerAnimState->Update( m_angEyeAngles[YAW], m_angEyeAngles[PITCH] )`
        // (tf_player.cpp:2689). So this is the engine's own source for which way a player model
        // points, not a substitute for a value we could not find.
        //
        // Applied here rather than at the ScenePlayer, deliberately: this pose feeds the same
        // ScenePropTrack a rocket uses, so the eye angles are interpolated by the same spline and
        // the same LoopingLerp that knows 359 to 1 is two degrees. TF2 registers m_angEyeAngles as
        // an interpolated variable of its own (c_tf_player.cpp:3874), so interpolating it is
        // matching the client rather than embellishing it.
        if (state.EyeAngles() is { } eyes)
        {
            (pitch, yaw) = (eyes.Pitch, eyes.Yaw);
        }

        // **The size of a divergence, counted where the two numbers are both in hand** (B273). The
        // engine stamps an interpolation history entry with the entity's simulation time and this
        // project stamps a keyframe with the packet; the difference is what this bucket holds.
        if (state.SimulatedAtTick is { } simulated)
        {
            int lag = Math.Clamp(state.SimulationBaseTick - simulated, -LagZero, LagZero);

            simulationLag[lag + LagZero]++;

            // **Which entities sit in which cluster, because that decides whether the divergence is
            // visible at all.** A constant offset shared by everything is a clock difference and
            // moves nothing on screen; entities disagreeing with EACH OTHER is what a viewer sees.
            if (state.ClassName is { } className)
            {
                if (!lagByClass.TryGetValue(className, out int[]? counts))
                {
                    counts = new int[LagBuckets];
                    lagByClass[className] = counts;
                }

                counts[lag + LagZero]++;
            }
        }
        else
        {
            simulationLag[LagUnknownBucket]++;
        }

        if (state.AnimatedAtTick is { } animated)
        {
            animationLag[Math.Clamp(state.SimulationBaseTick - animated, -LagZero, LagZero)
                + LagZero]++;

            // **The question that decides whether one keyframe can serve both clocks.** The engine
            // keeps a separate interpolation history per variable and stamps each with its own
            // clock; this project keeps ONE keyframe per entity per packet. If the two clocks agree
            // whenever both are sent, that single keyframe is faithful and no split is needed.
            if (state.SimulatedAtTick is { } alsoSimulated)
            {
                clockGap[Math.Clamp(alsoSimulated - animated, -LagZero, LagZero) + LagZero]++;
            }
        }
        else
        {
            animationLag[LagUnknownBucket]++;
        }

        // **Stamped with when the value APPLIED, not with the packet that carried it** (B273).
        // `OnLatchInterpolatedVariables` gives every simulation-latched variable — origin and
        // angles among them — the entity's own `GetSimulationTime()` as its history timestamp
        // (`c_baseentity.cpp:2806`), and this project stamped the packet tick.
        //
        // **The lag is applied to the demo's tick because it is a DIFFERENCE**, taken between two
        // numbers on the server's axis. The two axes are never mixed: the server's tickcount and
        // the demo's command numbering are unrelated, and subtracting one from the other is what
        // made the first attempt at this measurement pure noise.
        //
        // Measured on the 2013 SourceTV foundry recording, this moves `CTFPlayer` samples by four
        // ticks on exactly half of their updates — the two clusters are 50/50 — so the players,
        // which are the fastest things on screen, were being sampled with 60 ms of jitter that no
        // other entity shared.
        track.Add(
            tick,
            new ScenePose
            {
                // **When the animation now playing began**, so `elapsed` downstream is the time
                // since the server stamped it rather than since the recording opened.
                AnimationStartSeconds = track.AnimationStartSeconds,

                // **When it last jumped**, so a sequence change at that moment cuts (B346).
                DiscontinuitySeconds = track.DiscontinuitySeconds,

                // **A minigun's wind-up state, which is what spins its barrel** (B347).
                MinigunState = state.MinigunWeaponState(),

                // **The chamber's two tubes and its clock** (B348), carried together because
                // neither answers anything alone.
                Chamber = state.ChamberTubes() is { } tubes
                    ? (tubes.Current, tubes.Goal, track.ChamberStartedSeconds)
                    : null,
                X = origin.X,
                Y = origin.Y,
                Z = origin.Z,
                Pitch = pitch,
                Yaw = yaw,
                Roll = roll,

                // **Scale defaults to 1, sequence to 0, and both are the engine's own defaults
                // rather than sentinels of ours.** An absent scale is authored size; an absent
                // sequence is sequence 0, which m_nSequence is initialised to
                // (BaseAnimatingOverlay.cpp:104).
                //
                // Sequence was -1 until 2026-08-16, meaning "does not animate". Every drawing
                // consumer clamped it straight back to 0, and the one that compares rather than
                // clamps — InterpolateCycle, where a sequence change is a cut — saw a change that
                // never happened and froze the cycle at that boundary.
                Scale = state.ModelScale() ?? 1f,

                // **The three per-BONE scales, defaulting to 1 like the engine's own members do**
                // (`c_tf_player.cpp:577`). A demo that never sends them is a demo where TF2 would
                // also have used 1, so the default is the engine's rather than a fallback (B312).
                HeadScale = state.BoneScales().Head ?? 1f,
                TorsoScale = state.BoneScales().Torso ?? 1f,
                HandScale = state.BoneScales().Hand ?? 1f,
                Sequence = state.AnimationSequence() ?? 0,

                // The third factor in Valve's advance, c_baseanimating.cpp:5493. Retained and
                // decoded since the whitelist was written, and read by nothing until now.
                PlaybackRate = state.PlaybackRate() ?? 1f,
                Body = state.Body() ?? 0,

                // **The layers this entity sends, which a player never has** (B285).
                // `tf_player.cpp:774` excludes the array from the player's send table, so these
                // belong to everything else that animates — sentries carry two to four, measured
                // on `z1800.dem`, and teleporters, dispensers, sappers and taunt props carry them
                // too. `AccumulateLayers` walks them in `m_nOrder` over the main sequence.
                Layers = state.AnimationLayers(),

                // **Networked, and read by `CalcBoneAdj` to bend one bone** (B288). Decoded since
                // the whitelist was written and applied by nothing until now — a sentry's barrel
                // and a door's hinge are wired to these rather than to an animation.
                BoneControllers = state.BoneControllers(),

                // **The render state, which is what lets anything fade** (B221). All three are on
                // `DT_BaseEntity`, so they arrive for props, brush entities and players alike —
                // measured on real matches at 410 of 1,973 entities not fully opaque, with 118 at
                // `kRenderNone`, which the engine does not draw at all.
                //
                // Defaults applied HERE rather than left null, because `ScenePose` carries values
                // rather than answers: absent alpha is opaque, absent effect is `kRenderFxNone` and
                // absent mode is `kRenderNormal`, and each of those is the ordinary case rather
                // than an unknown.
                RenderAlpha = state.RenderAlpha(),
                RenderColor = state.RenderRgb(),
                RenderFx = state.RenderFx() ?? 0,
                RenderMode = state.RenderMode() ?? 0,

                // **The distance fade's two bounds** (B268). Zero for both is "does not fade",
                // which is the common case and the engine's own first branch, so an entity that
                // never sent them behaves exactly as it did before.
                FadeMinimumDistance = state.FadeMinimumDistance() ?? 0f,
                FadeMaximumDistance = state.FadeMaximumDistance() ?? 0f,

                // **`DT_Sprite`, whose fields were decoded by nothing until B378.** The SCALE is what
                // identifies the class here, exactly as the areaportal's start distance does below: no
                // other table sends `m_flSpriteScale`, so a present one means this entity is a
                // `CSprite` and every prop that is not gets null rather than a record of zeroes.
                //
                // **Each fallback is `CSprite`'s own constructor default rather than a neutral
                // number.** A missing scale read as zero would make the sprite vanish, where
                // `DrawSpriteModel` replaces a non-positive scale with 1 outright; a missing
                // brightness read as zero would draw it black where the engine draws it opaque.
                Sprite = state.SpriteScale() is { } spriteScale
                    ? new SceneSprite(
                        spriteScale,
                        state.SpriteBrightness() ?? SceneSprite.Default.Brightness,
                        state.SpriteFrame() ?? 0f,
                        state.SpriteFramerate() ?? 0f,
                        state.SpriteGlowProxySize() ?? 0f,
                        state.SpriteHdrColourScale() ?? SceneSprite.Default.HdrColourScale,
                        state.SpriteScaleIsWorldSpace() ?? false)
                    : null,

                // **`DT_Beam`, read by nothing until now.** The halo is a model index like the beam's own, so it is
                // named here, where the precache is, rather than by a drawer that has no table to look it up in.
                Beam = state.Beam() is { } beam
                    ? beam with { HaloPath = precache.Path(ModelPrecache.Unpack(beam.HaloIndex, protocol)) }
                    : null,

                // **`DT_SpriteTrail`, whose sampling parameters nothing read until B475** — the trail drew as a plain
                // sprite quad of its own material.
                SpriteTrail = state.SpriteTrail(),

                // **`DT_RopeKeyframe`, a NOBASE table nothing read until B477** — no rope ever reached a track.
                Rope = state.Rope(),

                // **An areaportal window's three, which travel together or not at all** (B358).
                // Only `DT_FuncAreaPortalWindow` sends them, so a present start distance is what
                // identifies the class — and the tuple stays null for every other entity, which is
                // what the scene tests before applying Valve's distance blend.
                PortalWindow = state.PortalFadeStartDistance() is { } portalStart
                    ? (portalStart,
                        state.PortalFadeDistance() ?? portalStart,
                        state.PortalTranslucencyLimit() ?? 0f)
                    : null,

                // **What the entity says its pose parameters are.** Empty for a player, because
                // `tf_player.cpp:769` excludes the array from their send table and the client
                // computes theirs — so this cannot override the ones `PoseValues` derives.
                PoseParameters = state.PoseParameters(),

                // **Compared, not read** (B275). `DoAnimationEvents` restarts its walk when this
                // differs from the value it saw last, which is how a taunt played twice sounds
                // twice while its sequence number never moves.
                ResetEventsParity = state.ResetEventsParity() ?? 0,

                // **Skin defaults to 0 because 0 is a real skin**, the model's first family, and a
                // delta-compressed format only sends what changed from it.
                //
                // This line is the one that was missing. Everything downstream was already in
                // place — ScenePropTrack copies Skin through its clone, with a comment explaining
                // why losing it draws every entity in family zero, and the renderer reads
                // prop.Pose.Skin. The value simply never entered the pose, so it was structurally
                // zero and no assertion could tell that from a demo where zero was correct.
                Skin = state.Skin() ?? 0,
                Cycle = state.Cycle() ?? 0f,

                // **In the POSE because it changes while the entity lives, which is the test the
                // other nine track scalars pass and this one does not** (B244). A weapon is
                // holstered and drawn again as its owner switches, and `ShouldDraw` reduces to
                // `m_iState == WEAPON_IS_ACTIVE` for another player's weapon — so a value read off
                // the track answers with the state at the END of the recording and a medic whose
                // medigun was away at the final tick drew empty-handed for the whole demo.
                //
                // Null rather than 0 for anything that is not a weapon: `m_iState` is declared by
                // `DT_BaseCombatWeapon`, so a wearable never sends it, and 0 is `WEAPON_NOT_CARRIED`
                // — a real state a dropped weapon has. Conflating the two would strip the shield
                // off every demoman.
                // **Absent means the DEFAULT for a weapon, and "not a weapon" for anything else**
                // (B245). `CL_CopyNewEntity` decodes an entering entity from its baseline, so a
                // weapon that has not restated `m_iState` since re-entering the visible set is at
                // `WEAPON_NOT_CARRIED` — a real value, and one `ShouldDraw` refuses.
                //
                // Reading absence as null instead makes it "this is not a weapon", which is the
                // answer that DRAWS, and it is right for a `CTFWearable`: the Mantreads, a
                // demoman's shield and a sniper's Razorback are worn whatever is in the hands. The
                // schema is what separates them, because `m_iState` is declared by
                // `DT_BaseCombatWeapon` and a class that never reaches that table never had it.
                WeaponState = state.WeaponState()
                    ?? (combatWeapons.Contains(entity.ClassId)
                        ? EntityState.WeaponNotCarried
                        : null),

                // EF_NODRAW, or gone from the visible set. A taken health pack is hidden rather
                // than deleted because it respawns, so this is a property of the moment.
                Hidden = !state.IsDrawn,
            },
            appliedAt: state.SimulatedAtTick is { } simulatedAt
                ? tick - (state.SimulationBaseTick - simulatedAt)
                : tick,

            // **The engine's other latch clock** (B274). `GetLastChangeTime` returns
            // `GetAnimTime()` for the cycle and the pose parameters where it returns
            // `GetSimulationTime()` for origin and angles, and a server sets the two at different
            // moments — on the 2013 SourceTV foundry recording they disagree by more than eight
            // ticks on 95.5% of the updates carrying both.
            //
            // Falls back to the packet tick for an entity that sends none, which is every player:
            // TF2's use client-side animation and `SendProxy_AnimTime` asserts they encode no
            // animation time at all.
            animationAppliedAt: state.AnimatedAtTick is { } animatedAt
                ? tick - (state.SimulationBaseTick - animatedAt)
                : tick);

        // **After the latch, because that is where the engine puts it** (B383).
        // `C_BaseAnimating::PostDataUpdate` runs `BaseClass::PostDataUpdate` — the latch — and reaches the
        // parity block last, so `m_iv_flCycle.Reset()` discards the entry that just landed and re-seeds
        // three copies of the value it carried (`c_baseanimating.cpp:4738`, `interpolatedvar.h:740`).
        //
        // **Stamped with the arrival tick, not the animation time**, which is the engine's `curtime`:
        // `Reset` calls `AddToHead( gpGlobals->curtime, m_pValue, false )` where the latch it replaces
        // used `GetAnimTime()`.
        if (newSequenceStarted)
        {
            track.SequenceRestarted(tick);
        }
    }
    }

    /// <summary>Every model that existed at a tick, with the pose it held then.</summary>
    /// <param name="tick">The moment being shown, which may fall between ticks.</param>
    /// <param name="into">Filled with the visible models; cleared first.</param>
    /// <remarks>
    /// **Fills a caller's collection rather than returning a new one.** A viewer asks this on
    /// every frame, and a match carries over a thousand tracks on a busy map — allocating a list
    /// per frame is garbage the renderer does not need to make. Typed as
    /// <see cref="ICollection{T}"/> rather than <c>List</c> so callers keep their own buffer
    /// without this API dictating which type it is (CA1002).
    ///
    /// Tracks are asked individually because each holds its own keyframes; a track that has not
    /// started or has already ended simply answers nothing.
    /// </remarks>
    /// <param name="viewEntity">
    /// <c>render-&gt;GetViewEntity()</c> — who the view is attached to, which is the ONE clause of
    /// <c>ShouldInterpolate</c> a recording cannot answer for itself (`c_baseentity.cpp:3031`). Null
    /// means no view entity, which is what a SourceTV recording drawn from a free camera has.
    /// </param>
    /// <remarks>
    /// **The interpolation list was a SET the caller passed, and that was the divergence** (B385).
    /// The renderer supplied <c>MomentScene.PosedEntities</c> — the entities that reached
    /// <c>SetupBones</c> last frame — and <c>IsVisible()</c> is none of those things: it is
    /// <c>m_hRender != INVALID_CLIENT_RENDER_HANDLE</c> (`c_baseentity.h:691`), set by
    /// <c>UpdateVisibility</c> from <c>ShouldDraw() &amp;&amp; !IsDormant()</c>
    /// (`c_baseentity.cpp:1421`). It is not about bones, not about the frustum, and not about the
    /// previous frame. Three populations were wrong in the same set: a brush entity has no skeleton
    /// so no door was EVER interpolated, the viewmodel pass cleared the set and refilled it with its
    /// own two props, and an entity the frustum rejected dropped off a list the engine keeps it on.
    ///
    /// Every clause but the view entity is answerable here, so it is answered here.
    /// </remarks>
    /// <param name="interpolating">
    /// <c>C_BaseEntity::IsInterpolationEnabled()</c> — false while the client is PAUSED, which
    /// <c>InterpolateServerEntities</c> sets directly (`c_baseentity.cpp:3226`) and
    /// <c>BaseInterpolatePart1</c> honours by calling <c>MoveToLastReceivedPosition</c> and stopping
    /// (`:2845`). A paused frame therefore shows the last stated pose with no delay, which is a
    /// different pose from the one playback shows at the same tick, not the same one frozen (B399).
    /// </param>
    public void PropsAt(
        double tick, ICollection<SceneProp> into, int? viewEntity = null, bool interpolating = true)
    {
        ArgumentNullException.ThrowIfNull(into);

        // **One sample at a time, because the sample below is shared** (B438). The viewer asks from one
        // thread, but a built timeline is a value worth sharing — the corpus suite hands one per demo to
        // tests running in parallel — and two callers rebuilding it at once corrupt the wake queue. The
        // lock makes each call whole; that the answer then depends on nothing but its own arguments is
        // what the sampling below guarantees, and `DemoTimelineSampleOrderTests` holds it to.
        lock (_sampling)
        {
            Sample(tick, into, viewEntity, interpolating);
        }
    }

    /// <summary>Serialises <see cref="PropsAt"/>, whose sample is kept between calls.</summary>
    private readonly Lock _sampling = new();

    /// <summary><see cref="PropsAt"/>'s body, run under its lock.</summary>
    private void Sample(double tick, ICollection<SceneProp> into, int? viewEntity, bool interpolating)
    {
        into.Clear();

        // **The signature stays abstract on purpose.** CA1002 refuses `List<T>` in a public API and
        // is right to; `Collection<T>` would be slower still, since it wraps. Narrowing inside costs
        // one type test, and a caller that passes something else keeps working through the slow
        // path rather than being refused.
        List<SceneProp>? fast = into as List<SceneProp>;

        // **The recorder's team AT THIS TICK**, because a player can switch sides mid-recording and
        // "is this my own team's spawn wall" then changes answer. Null for a SourceTV recording,
        // which has no local player at all — and the engine's `pLocalPlayer &&` guard means that
        // case DRAWS, so an entity of no known relation must come out false rather than true.
        int? recorderTeam = FrameAt((int)Math.Floor(tick))?.RecorderTeam;

        // **Sampling is proportional to what changed, not to what exists** (B259 fix 3, stage C).
        // The engine never walks its entity array per frame: `ProcessInterpolatedList` walks
        // `g_InterpolationList` — *"Interpolate the minimal set of entities that need it"*
        // (`c_baseentity.cpp:3123`) — which an entity joins when an update latches a changed
        // variable (`OnLatchInterpolatedVariables`, `:2832`) and leaves the moment its
        // interpolation has nothing more to do (`bNoMoreChanges`, `:2927`).
        //
        // **Our updates are already on disk, so `NoteChanged` becomes arithmetic**: each track
        // names the next tick at which its own answer can change (`Motion`), those wakes sit in
        // one queue, and a rebuild pays for the boundaries it crossed plus the tracks mid-lerp.
        // Everything else serves the prop it already built.
        //
        // **A seek is the case the engine does not have** (D131): state surviving across frames is
        // wrong the moment the clock jumps backwards, so a rewind — and the rarer team switch,
        // which is baked into every prop as `OfRecordersTeam` — rebuilds everything from nothing.
        // **A pause changes the sampling RULE without moving the clock**, so the incremental path
        // would serve the interpolated props it built while playing — the flag joins the seek and
        // the team switch as a reason to rebuild from nothing.
        //
        // **And so does the view entity** (B438). It is `ShouldInterpolate`'s first clause, an input
        // the caller supplies rather than one the recording states, and a sample kept from a call
        // made from somebody else's eyes answered that call's question: a `kRenderNone` mover asked
        // for from its own eyes held its last position where a cold sample blends it.
        if (!_sampleSynced || tick < _sampledTo || recorderTeam != _sampledTeam ||
            interpolating != _sampledInterpolating || viewEntity != _sampledView)
        {
            ResyncSample(tick, viewEntity, recorderTeam, interpolating);
        }
        else
        {
            AdvanceSample(tick, viewEntity, recorderTeam, interpolating);
        }

        _sampleSynced = true;
        _sampledTo = tick;
        _sampledTeam = recorderTeam;
        _sampledInterpolating = interpolating;
        _sampledView = viewEntity;

        // The list is refilled to nearly the same length every frame, so growing it from empty
        // re-allocates the backing array a dozen times a second for nothing.
        fast?.EnsureCapacity(_props.Count);

        // **The collate is the engine's own closing step** — `BuildRenderablesList` also emits a
        // fresh per-frame list from its maintained structures. This walk touches one reference and
        // one null test per track; the work a track used to cost here (a binary search, an
        // interpolation, a sixteen-field construction) happens only when something changed.
        foreach (ScenePropTrack track in _props)
        {
            // Stryker disable once : a mutant that empties the guard body leaves 'prop'
            // unassigned (CS0165), and Safe Mode then drops every mutation in this method — B410.
            if (track.Live is not { } prop)
            {
                continue;
            }

            // One virtual call saved per prop. `fast` is the same object as `into`; the branch
            // only chooses whether the add can inline.
            if (fast is not null)
            {
                fast.Add(prop);
            }
            else
            {
                into.Add(prop);
            }
        }
    }

    /// <summary>The wake queue: each scheduled track, keyed by the tick to re-derive it at.</summary>
    /// <remarks>
    /// At most one entry per track at any moment: a track is enqueued when it is derived and
    /// dequeued exactly once before being derived again, so the queue cannot accumulate stale
    /// entries — the engine's equivalent invariant is <c>AddToInterpolationList</c> checking
    /// <c>m_InterpolationListEntry</c> before adding.
    /// </remarks>
    private readonly PriorityQueue<ScenePropTrack, double> _wakes = new();

    /// <summary>Tracks whose pose changes continuously right now — <c>g_InterpolationList</c>.</summary>
    private readonly List<ScenePropTrack> _lerping = [];

    private bool _sampleSynced;
    private double _sampledTo;
    private int? _sampledTeam;

    /// <summary>Whether the held samples were built with interpolation on (B399).</summary>
    private bool _sampledInterpolating = true;

    /// <summary>Whose eyes the held samples were built from (B438).</summary>
    private int? _sampledView;

    /// <summary>Rebuilds every track's sample from nothing, at one tick.</summary>
    /// <remarks>
    /// The cold path: the first call, any seek backwards, a recorder team switch, a pause and a new
    /// view entity. It is the old per-frame walk, demoted to the cases that genuinely need one — and
    /// the answer every other path must reproduce exactly (B438).
    /// </remarks>
    private void ResyncSample(
        double tick, int? viewEntity, int? recorderTeam, bool interpolating = true)
    {
        _wakes.Clear();

        foreach (ScenePropTrack track in _lerping)
        {
            track.Lerping = false;
        }

        _lerping.Clear();

        foreach (ScenePropTrack track in _props)
        {
            DeriveSample(track, tick, viewEntity, recorderTeam, interpolating);
        }
    }

    /// <summary>Advances the samples to a later tick, paying only for what changed.</summary>
    /// <remarks>
    /// Two costs and nothing else: every wake whose tick has arrived is re-derived (a boundary
    /// crossed — the engine's update latching), and every track mid-lerp is re-sampled (the
    /// engine's `ProcessInterpolatedList` walk). A parked track is not touched.
    /// </remarks>
    private void AdvanceSample(
        double tick, int? viewEntity, int? recorderTeam, bool interpolating = true)
    {
        // A re-derived track re-enqueues its NEXT wake, which a long forward jump may also have
        // passed — the loop keeps popping until the head is in the future, so every crossed
        // boundary is processed and `Motion` returning a wake strictly beyond the asked tick is
        // what guarantees termination.
        while (_wakes.TryPeek(out ScenePropTrack? due, out double at) && at <= tick)
        {
            _wakes.Dequeue();

            DeriveSample(due, tick, viewEntity, recorderTeam, interpolating);
        }

        foreach (ScenePropTrack track in _lerping)
        {
            // Mid-lerp tracks are blend-sampled by construction: `Motion` only reports a track
            // changing under blended sampling. Hidden is re-read because it is a field of the
            // pose, discrete at the keyframe the lerp is walking away from.
            track.Live = track.At(tick) is { Hidden: false } pose
                ? BuildProp(track, tick, pose, recorderTeam)
                : null;
        }
    }

    /// <summary>Re-derives one track's whole sampling state at a tick.</summary>
    /// <remarks>
    /// **This is the engine's latch, with the decision it takes there** —
    /// <c>OnLatchInterpolatedVariables</c> consults <c>ShouldInterpolate()</c> when an update
    /// arrives (`c_baseentity.cpp:2832`), so whether a track blends or holds is decided at its
    /// wake, from the interpolation set as it stands then, and not revisited per frame. A prop
    /// granted visibility between keyframes therefore joins the lerp at the next keyframe, which
    /// is when the engine would re-latch it.
    ///
    /// **Every input the decision read is a wake, and that is what makes a stepped sample a cold one**
    /// (B438). Its own visibility changes only at its own keyframes, which are wakes already; the view
    /// entity resyncs everything when it changes; and a move child's visibility, which the fourth clause
    /// reads, can change at the child's keyframes — so the parent is looked at again then, or a door whose
    /// grate appeared between the door's updates would hold where a scrub to the same tick blends it.
    /// </remarks>
    private void DeriveSample(
        ScenePropTrack track,
        double tick,
        int? viewEntity,
        int? recorderTeam,
        bool interpolating = true)
    {
        // **The last STATED pose, which is both answers this needs.** `IsVisible()` reports the last
        // `UpdateVisibility`, and that runs on a data update — so the render mode and the draw flag
        // that decide the list are the ones the newest keyframe carried, never an interpolated blend
        // of two. It is also `Held` itself, so a track off the list costs one call rather than two.
        ScenePose? stated = track.Held(tick);

        // **The engine's flag is global and it wins over every per-entity clause** —
        // `IsInterpolationEnabled()` is read before `ShouldInterpolate`'s answer matters at all, so
        // a paused client holds even the entity the view is attached to (B399).
        double revisit = double.PositiveInfinity;

        // **A beam is never drawn where interpolation put it.** `C_Beam::AddEntity` ends with
        // `MoveToLastReceivedPosition()` (`beam_shared.cpp:1075`), which writes the network origin back over whatever
        // `Interpolate` produced, every frame — so a beam stands at the last position it was SENT, not between two.
        bool blend = interpolating && stated?.Beam is null && Interpolates(track, stated, tick, viewEntity, out revisit);

        (bool changing, double nextWake) = track.Motion(tick, blend);

        nextWake = Math.Min(nextWake, revisit);

        ScenePose? sampled = blend ? track.At(tick) : stated;

        // A hidden entity is not drawn but is still tracked: it is coming back.
        track.Live = sampled is { Hidden: false } pose
            ? BuildProp(track, tick, pose, recorderTeam)
            : null;

        if (changing != track.Lerping)
        {
            if (changing)
            {
                _lerping.Add(track);
            }
            else
            {
                _lerping.Remove(track);
            }

            track.Lerping = changing;
        }

        if (!double.IsPositiveInfinity(nextWake))
        {
            _wakes.Enqueue(track, nextWake);
        }
    }

    /// <summary><c>C_BaseEntity::ShouldInterpolate</c>, all four clauses (B385).</summary>
    /// <param name="asked">The track being asked about.</param>
    /// <param name="stated">Its last stated pose, or null when it has none at this tick.</param>
    /// <param name="tick">The moment, for asking a movement child the same question.</param>
    /// <param name="viewEntity">Who the view is attached to, or null when nobody is.</param>
    /// <param name="revisit">
    /// The first tick at which a move child walked here can answer differently — its birth, its next
    /// keyframe or its end — or infinity when the answer read no child (B438).
    /// </param>
    /// <returns>Whether its variables are blended rather than held.</returns>
    /// <remarks>
    /// **The engine's, in its order** (`c_baseentity.cpp:3029`):
    ///
    /// <code>
    /// if ( render-&gt;GetViewEntity() == index ) return true;
    /// if ( index == 0 || !GetModel() )        return false;
    /// if ( IsVisible() )                      return true;   // always interpolate if visible
    /// // if any movement child needs interpolation, we have to interpolate too
    /// C_BaseEntity *pChild = FirstMoveChild();
    /// while( pChild )
    /// {
    ///     if ( pChild-&gt;ShouldInterpolate() )
    ///         return true;
    ///     pChild = pChild-&gt;NextMovePeer();
    /// }
    /// return false;
    /// </code>
    ///
    /// **<c>IsVisible()</c> is leaf-system membership and nothing else** —
    /// <c>m_hRender != INVALID_CLIENT_RENDER_HANDLE</c> (`c_baseentity.h:691`), which
    /// <c>UpdateVisibility</c> sets from <c>ShouldDraw() &amp;&amp; !IsDormant()</c>
    /// (`c_baseentity.cpp:1421`), and <c>ShouldDraw</c> is <c>kRenderNone</c>, then
    /// <c>model != 0 &amp;&amp; !IsEffectActive( EF_NODRAW ) &amp;&amp; index != 0</c> (`:1435`). A
    /// frustum does not appear in it, which is what makes the whole question answerable from a
    /// recording rather than from the renderer.
    ///
    /// **The fourth clause is the only one a door mover can pass.** `cp_fulgur`'s gates hang on
    /// `func_door`s that declare <c>kRenderNone</c>, so the third clause refuses them forever and
    /// without this walk they hold their last stated pose while the grate bolted to them is
    /// interpolated onto a mover that is not moving.
    ///
    /// **Recursive because the engine's is** — the clause calls <c>ShouldInterpolate</c> on the child,
    /// not <c>IsVisible</c> — so a grate on a door on a platform reaches the platform. Iterative here
    /// with its own visited set rather than a call stack, because a demo can state any parent it likes
    /// and decoding must be total: <c>SetParent</c> refuses a cycle, our decode has no such authority,
    /// and a cycle down a recursive walk is a hang rather than a wrong answer.
    /// </remarks>
    private bool Interpolates(
        ScenePropTrack asked, ScenePose? stated, double tick, int? viewEntity, out double revisit)
    {
        revisit = double.PositiveInfinity;

        if (viewEntity == asked.EntityIndex)
        {
            return true;
        }

        if (Visible(asked, stated, tick))
        {
            return true;
        }

        _moveChildren ??= MoveChildren();

        if (!_moveChildren.TryGetValue(asked.EntityIndex, out List<int>? children))
        {
            return false;
        }

        // Allocated only for an entity that HAS children, which on `cp_granary` is 66 of 297 brush
        // entities and none of the props.
        Queue<(int Child, int Parent)> pending = new();

        foreach (int child in children)
        {
            pending.Enqueue((child, asked.EntityIndex));
        }

        HashSet<int> walked = [asked.EntityIndex];

        while (pending.TryDequeue(out (int Child, int Parent) next))
        {
            int child = next.Child;

            // **The occupant alive at the tick, or no child at all** (B548). The map is keyed by slot and holds every
            // parent any occupant ever had, so the question is asked of the entity that exists now; a slot with no
            // live occupant has nothing hanging in it, and its grandchildren are not reached through it.
            if (TrackFor(child, tick) is not { } hanging)
            {
                // Its wake still matters to the parent: the slot's NEXT occupant, whichever is born first after the
                // tick, may hang here. A live child's own death is in its `HeldChangesAfter`, so the two together
                // re-derive the parent when the child vanishes and when the slot is filled again.
                revisit = Math.Min(revisit, NextBirthInSlot(child, tick));

                continue;
            }

            // **The answer holds only until this child's own can change** (B438), which is the parent's to hear
            // about. Taken for a FORMER child too: its next change may make it this parent's child again, between the
            // mover's own wakes — `PropsAt_SteppedAcrossAFormerChildRejoining_MatchesACold` fails without it (B548).
            revisit = Math.Min(revisit, hanging.HeldChangesAfter(tick));

            // **A child of this parent only while it hangs from it** (B548): `FirstMoveChild` holds the parent it has now.
            if (hanging.FactsAt(tick).AttachedTo != next.Parent || !walked.Add(child))
            {
                continue;
            }

            // The view entity counts only as a CURRENT child: the fourth clause asks `ShouldInterpolate` of each entity
            // on `FirstMoveChild`'s list, and that list holds the children the parent has now (`c_baseentity.cpp:3031`).
            if (viewEntity == child)
            {
                return true;
            }

            // The child's own last stated pose — the clause calls `ShouldInterpolate` on the CHILD, so the child's
            // render mode decides, not the parent's.
            if (Visible(hanging, hanging.Held(tick), tick))
            {
                return true;
            }

            if (_moveChildren.TryGetValue(child, out List<int>? theirs))
            {
                foreach (int grandchild in theirs)
                {
                    pending.Enqueue((grandchild, child));
                }
            }
        }

        return false;
    }

    /// <summary><c>IsVisible()</c> — whether an entity holds a client render handle.</summary>
    /// <param name="track">The entity, for its model and its index.</param>
    /// <param name="stated">Its last stated pose, or null when the track states none here.</param>
    /// <param name="tick">The moment, for the model the entity held then (B548).</param>
    /// <returns>Whether <c>UpdateVisibility</c> would have it in the leaf system.</returns>
    /// <remarks>
    /// **<c>ShouldDraw()</c> in the engine's own order** (`c_baseentity.cpp:1435`) — the render mode
    /// first, because the comment there says *"Some rendermodes prevent rendering"* and that is the
    /// clause every invisible mover in the game relies on, then the model, the draw flag and the
    /// index. A track with no stated pose has not arrived: no handle, so not visible.
    ///
    /// **An empty model path is <c>model != 0</c>** — a wearable whose model index has not arrived is
    /// exactly the engine's null model, and the same test <c>EntityModelSet.CanDraw</c> makes one
    /// layer out. <c>Kind</c> is deliberately NOT tested: a kind this project cannot draw is our
    /// limitation, not the engine's, and refusing to interpolate one would hold a pose the engine
    /// blends.
    /// </remarks>
    private static bool Visible(ScenePropTrack track, ScenePose? stated, double tick) =>
        stated is { Hidden: false, RenderMode: not RenderModes.None }
        && track.FactsAt(tick).ModelPath.Length > 0
        && track.EntityIndex != 0;

    /// <summary>Movement children by parent, which is <c>FirstMoveChild</c>/<c>NextMovePeer</c>.</summary>
    /// <returns>Every entity that hangs off another, indexed by what it hangs off.</returns>
    /// <remarks>
    /// **Built once and not maintained, because sampling happens after decoding.** `AttachedTo` is
    /// kept current per delta while the timeline is being built (`m_hMoveParent` follows the engine's
    /// `HierarchySetParent`), and the first caller to reach this is a `PropsAt`, by which time every
    /// delta has been read. A timeline that gained tracks afterwards would need this invalidated, and
    /// none does.
    ///
    /// **Keyed by entity rather than by track, which is the reuse hazard**
    /// (`docs/memory/an-entity-index-does-not-name-a-track.md`). A slot reused by a second prop
    /// parented elsewhere contributes both parents, so the walk considers a child that is no longer
    /// there — which errs toward interpolating a mover, and that direction is never a visible defect.
    /// The alternative, keying on the track, would need the walk to know which occupant is alive at
    /// the tick, and the engine's own list has no such notion: it holds ENTITIES.
    /// </remarks>
    private Dictionary<int, List<int>> MoveChildren()
    {
        Dictionary<int, List<int>> children = [];

        // **Every parent the child EVER had, not its last** (B548): the walk keeps only the child whose parent
        // at the asked tick is the one it came from, so a wearable that changed hands is visited under each.
        // **Players too**: `FirstMoveChild` is a property of every `C_BaseEntity`, and a player hung on a parent is
        // as much its move child as a prop is (B548).
        foreach (ScenePropTrack track in _props.Concat(_playerTracks))
        {
            foreach ((int _, ScenePropFacts facts) in track.FactsHistory)
            {
                // Stryker disable once : a mutant that empties the guard body leaves 'parent'
                // unassigned (CS0165), and Safe Mode then drops every mutation in this method — B410.
                if (facts.AttachedTo is not { } parent || parent == track.EntityIndex)
                {
                    continue;
                }

                if (!children.TryGetValue(parent, out List<int>? hanging))
                {
                    children[parent] = hanging = [];
                }

                if (!hanging.Contains(track.EntityIndex))
                {
                    hanging.Add(track.EntityIndex);
                }
            }
        }

        return children;
    }

    /// <summary>Movement children by parent, or null before the first sampling built it.</summary>
    private Dictionary<int, List<int>>? _moveChildren;

    /// <summary>Builds the prop a track serves until something changes it.</summary>
    private static SceneProp BuildProp(ScenePropTrack track, double tick, in ScenePose pose, int? recorderTeam) =>
        BuildProp(track, track.FactsAt(tick), pose, recorderTeam);

    /// <summary>Builds the prop from the identity the entity held at the moment drawn (B548).</summary>
    private static SceneProp BuildProp(ScenePropTrack track, in ScenePropFacts facts, in ScenePose pose, int? recorderTeam) =>
        new(
            // **The pose as sampled, with no player-animation inputs derived from it** (B258).
            // `Moving` was called here for every prop, and it computes `move_x`, `move_y` and
            // `Speed` — which come from `CBasePlayerAnimState::ComputePoseParam_MoveYaw` and exist
            // nowhere outside `base_playeranimstate.cpp` and `multiplayer_animstate.cpp`. A
            // `C_BaseAnimating` prop has no animation state, so the engine derives none of this
            // for one: a resupply locker does not have legs. And it never ran for a player here
            // anyway — player tracks live in `_playerTracks`, and `PlayerProps` carries the values
            // `PlayersAt` computes onto the player's own `SceneProp`.
            track.EntityIndex, facts.ModelPath, facts.Kind, pose,

            // **From the POSE, because the weapon state is the one of these that changes while an
            // entity lives** (B244). The rest — the parent, the owner, the item, the model — come from
            // `facts`, as the entity held them at the drawn tick (B548): read off the track they were
            // the demo's END values, the same defect B244 fixed for `m_iState`.
            facts.AttachedTo, facts.AttachmentPoint, facts.OwnedBy, pose.WeaponState,
            facts.BoneMerged, facts.ItemDefinitionIndex, facts.ClassName,
            facts.OfDisguise,
            OfRecordersTeam: recorderTeam is { } mine && facts.TeamNumber == mine,
            Econ: facts.Econ,
            ClientSideAnimated: facts.ClientSideAnimated);

    private sealed partial class Builder
    {
    /// <summary>Whether a player is on the opposite team from the recorder.</summary>
    /// <param name="recorderTeam">The recording player's team, or null when it is not known.</param>
    /// <param name="team">The player being asked about.</param>
    /// <returns>Whether a disguise is meant to fool us.</returns>
    /// <remarks>
    /// **`C_TFPlayer::IsEnemyPlayer`, `c_tf_player.cpp:5384`**, which switches on the LOCAL player's
    /// team and answers true only for the opposite one:
    ///
    /// <code>
    ///   case TF_TEAM_RED:  return ( GetTeamNumber() == TF_TEAM_BLUE );
    ///   case TF_TEAM_BLUE: return ( GetTeamNumber() == TF_TEAM_RED );
    ///   default: break;
    ///   return false;
    /// </code>
    ///
    /// **The default matters and is kept.** A recorder who is a spectator, unassigned or unknown is
    /// on NEITHER team and the engine answers false — so a SourceTV recording sees every spy
    /// undisguised, which is what a spectator sees in game.
    ///
    /// **The recorder's team is resolved BEFORE the player loop, not from the list being built.**
    /// A first version searched the players added so far, so everybody with an entity index below
    /// the recorder's answered false regardless of team — measured on `cp_fulgur`, entities 1 and 2
    /// reported friendly while on the opposite side. That is an ordering bug of exactly the kind
    /// this project keeps shipping, and it was caught by reading the output rather than by the
    /// tests, which had no ordering to get wrong.
    ///
    /// **The coaching branch is not implemented**: the engine substitutes the student's team when
    /// `m_hStudent` is set and `m_bIsCoaching`. Neither field is read here, so a coached recording
    /// reports the coach's own team.
    ///
    /// **The fields ARE on the wire, which the note did not used to say.** `m_bIsCoaching` and
    /// `m_hCoach` sit in `DT_TFLocalPlayerExclusive` — sent only to the local player, who in a POV
    /// demo is the recorder, so a coached recording carries exactly what this would need.
    ///
    /// **Measured inert on the corpus rather than assumed so:** `m_bIsCoaching` is 0 on all 33 of
    /// its appearances in `z1800`, with `m_hCoach` at the 2097151 invalid-handle sentinel
    /// throughout. A coaching demo is what would exercise this, and the corpus has none — so the
    /// gap is real and unranked rather than fixed, and the number is written with the dump that
    /// produced it so it can be re-measured when the corpus grows.
    /// </remarks>
    private static bool IsEnemyOfRecorder(int? recorderTeam, int? team) =>
        (recorderTeam, team) switch
        {
            (SceneTeams.Red, SceneTeams.Blu) => true,
            (SceneTeams.Blu, SceneTeams.Red) => true,
            _ => false,
        };
    }

    /// <summary>The STUDENT's team when the recorder is coaching, else null.</summary>
    /// <param name="entities">The entity table, for looking the student up by index.</param>
    /// <param name="recorder">The recording player.</param>
    /// <param name="resource">The player resource, whose team array is preferred where it exists.</param>
    /// <returns>The student's team number, or null when nobody is being coached.</returns>
    /// <remarks>
    /// **`iMyApparentTeam = m_hStudent->GetTeamNumber();`** under
    /// <c>if ( m_bIsCoaching &amp;&amp; m_hStudent )</c> (`c_tf_player.cpp:10136`), and it repeats
    /// where the disguise colour is chosen (`:5394`) under a comment saying *"if we are coaching,
    /// use the team of the student"*.
    ///
    /// **A coach has no team of their own that means anything.** They are on `TEAM_SPECTATOR` while
    /// attached to a player, so the recorder's own team answers "neither", and every player in the
    /// demo then reads as friendly — including the enemies the coach can see.
    ///
    /// **Both fields are `DT_TFLocalPlayerExclusive`**, sent only to the local player. That is
    /// exactly the recorder in a POV demo, so a coached recording carries what this needs and no
    /// other player's copy is ever consulted.
    ///
    /// **Null rather than a fallback when the student cannot be resolved.** A handle naming an
    /// entity the table does not hold is a coach whose student has left, and answering with the
    /// coach's own spectator team is what the caller already does.
    ///
    /// **Measured inert on the corpus** (B313): `m_bIsCoaching` is 0 on all 33 of its appearances
    /// in `z1800` and `m_hCoach` sits at the 2097151 invalid-handle sentinel throughout. There is no
    /// coaching demo here, so this is written from the engine and exercised only by its tests.
    /// </remarks>
    internal static int? Coached(
        EntityStateTable entities, EntityState recorder, EntityState? resource)
    {
        if (recorder.Integer($"{CoachTable}.m_bIsCoaching") is not (int and not 0) ||
            recorder.Integer($"{CoachTable}.m_hStudent") is not { } handle)
        {
            return null;
        }

        // **Through `Resolve`, not by masking, and this nearly went the other way** (B231). A
        // handle is an index AND a serial; masking alone resolves a DANGLING handle to a real,
        // existing, different entity — measured once as a resupply locker composed onto a door.
        // Worse here: the invalid sentinel is 21 bits of ones whose low 11 mask to 2047, a
        // perfectly ordinary-looking slot, so every non-coaching player in the corpus would have
        // resolved to entity 2047 rather than to nothing.
        if (entities.Resolve(handle) is not { } student ||
            !entities.TryGet(student, out EntityState? taught))
        {
            return null;
        }

        return ResourceTeam(resource, student) ?? First(taught, TeamProperties);
    }

    /// <summary>One player's team from the resource entity's array.</summary>
    /// <param name="resource">The <c>CTFPlayerResource</c>, or null when the demo has none.</param>
    /// <param name="entity">The player's ENTITY index, not their slot.</param>
    /// <returns>The team, or null when the resource does not carry one for them.</returns>
    /// <remarks>
    /// **The key is the entity index zero-padded to three digits** — `m_iTeam.003`, confirmed
    /// against a dump of `z1800`, whose keys run `m_iTeam.000` upward. Two things in that sentence
    /// were each got wrong once at the recorder's call site (B313): it passed the SLOT, and it did
    /// not pad.
    ///
    /// **The result was a lookup that could never match, hidden by a `??`.** It returned null every
    /// time and the fallback to the player's own `m_iTeamNum` answered instead — so the code read
    /// as "prefer the resource, fall back to the entity" while only ever doing the second half.
    /// That is `docs/memory/a-fallback-that-makes-sound-hides-itself.md`: a fallback that works
    /// stops anyone noticing the path in front of it is dead.
    ///
    /// **Extracted so the two callers cannot disagree.** The player loop had it right and the
    /// recorder's line had it wrong, sixty lines apart, for as long as both have existed.
    /// </remarks>
    private static int? ResourceTeam(EntityState? resource, int entity) =>
        resource?.Integer(
            $"m_iTeam.{entity.ToString("D3", CultureInfo.InvariantCulture)}");

    /// <summary>The table carrying the coaching pair, sent only to the local player.</summary>
    private const string CoachTable = "DT_TFLocalPlayerExclusive";

    /// <summary>Where everyone was at a tick, or the most recent moment before it.</summary>
    /// <param name="tick">The tick being shown.</param>
    /// <returns>The players, empty before the first recorded frame.</returns>
    /// <remarks>
    /// **The most recent frame rather than an exact match**, because positions arrive with packets
    /// and the server sends no packet on most ticks. Requiring an exact tick would blink the map
    /// empty between updates.
    /// </remarks>
    public IReadOnlyList<ScenePlayer> PlayersAt(int tick) =>
        FrameAt(tick)?.Players ?? [];


    /// <summary>Where everyone was at a moment, with positions interpolated as the client does.</summary>
    /// <param name="tick">The moment being shown, which may fall between ticks.</param>
    /// <param name="into">Filled with the players; cleared first.</param>
    /// <remarks>
    /// **A player is interpolated by exactly the same machinery as a rocket, because in the engine
    /// it is the same code.** <c>m_vecOrigin</c> and <c>m_angRotation</c> are registered on
    /// <c>C_BaseEntity</c> — <c>AddVar(&amp;m_vecOrigin, &amp;m_iv_vecOrigin, LATCH_SIMULATION_VAR)</c>
    /// at <c>c_baseentity.cpp:905</c> — and a player is a <c>C_BaseEntity</c>. There is no separate
    /// player position path to reproduce. TF2 adds exactly one interpolated variable of its own,
    /// <c>m_angEyeAngles</c> (<c>c_tf_player.cpp:3874</c>).
    ///
    /// So the position here comes from the entity's own <see cref="ScenePropTrack"/> — the same
    /// hermite spline, the same time renormalisation — rather than from a second implementation
    /// that would drift from the first.
    ///
    /// **Team, class and health are not interpolated, and that is measured rather than assumed.**
    /// Neither appears in any <c>AddVar</c> call in the client. They are discrete facts: a player
    /// between 125 and 68 health was never on 96, and one changing team was never on a team
    /// between the two.
    /// </remarks>
    /// <param name="interpolating">
    /// False while paused, exactly as for <see cref="PropsAt(double, ICollection{SceneProp}, int?,
    /// bool)"/> — one global flag in the engine (`c_baseentity.cpp:3226`), and a player's position
    /// is registered on <c>C_BaseEntity</c> like any other entity's (B399).
    /// </param>
    /// <param name="viewpoint">
    /// What <c>CDemoPlayer::InterpolateViewpoint</c> handed the local player this frame (<see cref="DemoPlayer"/>), or
    /// null when it set nothing — a SourceTV demo, or a frame before the first view (B56).
    /// </param>
    /// <param name="recorderFeet">
    /// The viewer's anim-state feet for the recorder, carried from frame to frame (B450); null starts fresh feet from
    /// the timeline's, which a single frame cannot tell apart.
    /// </param>
    /// <param name="predictedVelocity">
    /// Prediction's <c>m_vecVelocity</c> for the local player this frame (D205), or null when it did not run — the
    /// networked one stands then.
    /// </param>
    public void PlayersAt(
        double tick,
        ICollection<ScenePlayer> into,
        bool interpolating = true,
        RecordedView? viewpoint = null,
        RecorderFeet? recorderFeet = null,
        (float X, float Y, float Z)? predictedVelocity = null)
    {
        ArgumentNullException.ThrowIfNull(into);

        into.Clear();

        foreach (ScenePlayer player in PlayersAt((int)Math.Floor(tick)))
        {
            into.Add(Sampled(player, tick, interpolating, viewpoint, recorderFeet, predictedVelocity));
        }
    }

    /// <summary>One player at a moment, interpolated as <see cref="PlayersAt(double, ICollection{ScenePlayer}, bool, RecordedView?, RecorderFeet?, ValueTuple{float, float, float}?)"/> does, with no viewpoint.</summary>
    /// <param name="tick">The moment, which may fall between ticks.</param>
    /// <param name="entityIndex">The player.</param>
    /// <returns>The player, or null when the frame at that tick lists no such player.</returns>
    /// <remarks>
    /// **Side-effect free, which the per-frame sampler is not**: the viewer's demo player and prediction keep a cache
    /// keyed on the last tick asked, and a backward scan would reset them (D233). So the recorder of a point-of-view demo
    /// is read on his networked track here.
    /// </remarks>
    public ScenePlayer? PlayerAt(double tick, int entityIndex)
    {
        foreach (ScenePlayer player in PlayersAt((int)Math.Floor(tick)))
        {
            if (player.EntityIndex == entityIndex)
            {
                return Sampled(player, tick, interpolating: true, viewpoint: null, recorderFeet: null, predictedVelocity: null);
            }
        }

        return null;
    }

    /// <summary>One player of the frame under <paramref name="tick"/>, at the moment itself.</summary>
    private ScenePlayer Sampled(
        ScenePlayer player,
        double tick,
        bool interpolating,
        RecordedView? viewpoint,
        RecorderFeet? recorderFeet,
        (float X, float Y, float Z)? predictedVelocity)
    {
        // **A dead player keeps the position recorded for them**, which is where they fell.
        // The entity's own track follows whoever they are spectating, so interpolating it
        // would drag the body across the map to stand inside a living player.
        if (!player.IsAlive)
        {
            return player;
        }

        // **The local player is where the demo player's view put him** (B56): his origin is SetViewOrigin's.
        if (viewpoint is { } local && player.EntityIndex == RecorderEntityIndex)
        {
            return LocalPlayer(player, local, tick, recorderFeet ?? new RecorderFeet(), predictedVelocity);
        }

        // Stryker disable all : the guard condition spans two lines, so a mutant that empties
        // the guard body leaves 'pose' unassigned at its use below (CS0165), and Safe Mode
        // then drops every mutation in this method — B410.
        // The occupant of the slot AT this tick, not its last one: a reused slot holds several tracks.
        if (TrackFor(player.EntityIndex, tick) is not { } track)
        {
            return player;
        }

        // **One pair of samples for the speed and the heading**, where each asked for the same two: they are the
        // same differencing over the same window, and `At` is three of a frame's five track reads per player.
        ScenePose? now = track.At(tick);

        if ((interpolating ? now : track.Held(tick)) is not { } pose)
        {
            return player;
        }

        // Stryker restore all

        ScenePose? was = now is null ? null : track.At(Math.Max(0d, tick - MotionWindowTicks));
        (float moveX, float moveY) = MoveParameters(HeadingOf(now, was), pose.EyeYaw ?? pose.Yaw);

        // **Yaw travels with the position, from the same pose.** Taking one and discarding the
        // other is what left every player facing north the moment they stopped being a dot:
        // the number was decoded and interpolated already, and simply not carried the last few
        // lines.
        return player with
        {
            X = pose.X,
            Y = pose.Y,
            Z = pose.Z,
            Yaw = pose.Yaw,
            Speed = SpeedOf(now, was),
            MoveX = moveX,
            MoveY = moveY,
        };
    }

    /// <summary>The round the game rules were in, or <c>null</c> when the demo does not say.</summary>
    /// <param name="tick">The moment being shown.</param>
    /// <returns><c>m_iRoundState</c>; <c>GR_STATE_TEAM_WIN</c> is 5.</returns>
    /// <remarks>
    /// **Null is "the demo did not say", not a state.** Era demos predate the game rules proxy this
    /// reads, and a consumer that treated absent as any particular state would apply that state's
    /// behaviour to every one of them — which for the spawn walls means blanking all of them for a
    /// whole recording (`RespawnRoomVisibility`).
    /// </remarks>
    public int? RoundStateAt(double tick) => FrameAt((int)Math.Floor(tick))?.RoundState;

    /// <summary>The game rules the HUD tests, at a tick; the defaults before the first frame.</summary>
    /// <param name="tick">The tick.</param>
    /// <returns>The rules.</returns>
    public SceneGameRules RulesAt(int tick) => FrameAt(tick)?.Rules ?? default;

    /// <summary>The server's tick at a demo tick — the last `net_Tick` — or null before one arrived.</summary>
    /// <param name="tick">The demo tick.</param>
    public int? ServerTickAt(int tick) => FrameAt(tick)?.ServerTick;

    /// <summary>Every `team_round_timer` at a tick.</summary>
    /// <param name="tick">The demo tick.</param>
    public IReadOnlyList<SceneRoundTimer> RoundTimersAt(int tick) => FrameAt(tick)?.RoundTimers ?? [];

    /// <summary>Every Engineer building at a tick.</summary>
    /// <param name="tick">The demo tick.</param>
    public IReadOnlyList<SceneBuilding> BuildingsAt(int tick) => FrameAt(tick)?.Buildings ?? [];

    /// <summary>Every `CTFTeam` at a tick.</summary>
    /// <param name="tick">The demo tick.</param>
    public IReadOnlyList<SceneTeam> TeamsAt(int tick) => FrameAt(tick)?.Teams ?? [];

    /// <summary>Every player slot the scoreboard would list at a tick.</summary>
    /// <param name="tick">The demo tick.</param>
    public IReadOnlyList<SceneScoreboardPlayer> ScoreboardPlayersAt(int tick) => FrameAt(tick)?.ScoreboardPlayers ?? [];

    /// <summary>Every dropped weapon and revive marker at a tick.</summary>
    /// <param name="tick">The tick.</param>
    /// <returns>The entities, empty when there is none.</returns>
    public IReadOnlyList<SceneIdEntity> IdEntitiesAt(int tick) => FrameAt(tick)?.IdEntities ?? [];

    /// <summary>The recording player's team at a tick, or <c>null</c> when there is no local player.</summary>
    /// <param name="tick">The moment being asked about.</param>
    /// <returns><see cref="TimelineFrame.RecorderTeam"/> for the frame at or before it.</returns>
    /// <remarks>
    /// **Null means a SourceTV recording**, where the engine's own `pLocalPlayer` is null and every guard on it falls
    /// through — which is how a client-emitted sound such as an explosion's decides whether to ask an item.
    /// </remarks>
    public int? RecorderTeamAt(int tick) => FrameAt(tick)?.RecorderTeam;

    /// <summary>The most recent frame at or before a tick.</summary>
    /// <remarks>
    /// **The server does not send a packet every tick**, so this answers with the last one rather
    /// than requiring an exact match — the same rule <see cref="PlayersAt(int)"/> has always used,
    /// lifted out so the round state and the props answer from the same frame the players do.
    /// </remarks>
    private TimelineFrame? FrameAt(int tick)
    {
        int low = 0;
        int high = _frames.Count - 1;
        int found = -1;

        while (low <= high)
        {
            int middle = low + ((high - low) / 2);

            if (_frames.TickAt(middle) <= tick)
            {
                found = middle;
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return found >= 0 ? _frames[found] : null;
    }

    /// <summary>The interpolation track for one entity, when it has one.</summary>
    /// <param name="entityIndex">The entity's slot.</param>
    /// <returns>Its track, or <c>null</c> when nothing about it was recorded.</returns>
    /// <remarks>
    /// **Exposed so a test can predict what <see cref="PlayersAt(double, ICollection{ScenePlayer}, bool, RecordedView?, RecorderFeet?, ValueTuple{float, float, float}?)"/>
    /// should report.** Asserting a player's yaw against a literal would test the demo rather than
    /// the code; asserting it against the track this reads from tests the plumbing between them,
    /// which is where the number was being dropped.
    /// </remarks>
    public ScenePropTrack? TrackFor(int entityIndex) =>
        _trackByEntity.TryGetValue(entityIndex, out ScenePropTrack? track) ? track : null;

    /// <summary>The track for one entity at one MOMENT, since an index alone does not name one.</summary>
    /// <param name="entityIndex">The entity's slot.</param>
    /// <param name="tick">The moment being asked about.</param>
    /// <returns>The track alive there, or <c>null</c> when no occupant of that slot covers the tick.</returns>
    /// <remarks>
    /// **An edict slot is recycled, so <see cref="TrackFor(int)"/> answers about whichever occupant
    /// was written last** — which is right only for a caller whose tick is after the final one
    /// started. This project seeks, so that caller does not exist:
    /// `demostf-cp_process_f12` carries 1,669 `CTFProjectile_Rocket` tracks over a couple of thousand
    /// slots, and on the 2013 granary match entity 328 owns eight tracks and entity 141 owns several.
    /// Both public forms are kept so the pair is visible together — the one-argument overload states
    /// its own precondition badly and this one states it in its signature.
    ///
    /// **The two ways it has already failed are both silent.** B375: a rocket trail asked
    /// `TrackFor(407)` at tick 51122, got a rocket from later in the match whose `FirstTick` is past
    /// that, and `age` clamped to zero so the replay drew nothing. B370: `jitter` reported "fewer
    /// than three samples" for a door whose chosen track was long dead at the tick asked for. Neither
    /// threw; both produced a fully-formed wrong answer.
    ///
    /// **<see cref="ScenePropTrack.Alive"/> rather than a tick comparison written here** — it is the
    /// bound <see cref="ScenePropTrack.At"/> and <c>Held</c> both apply, so a track this returns is a
    /// track the sampler will answer for. A second reading of the same two ends is how expressions
    /// that must agree stop agreeing.
    ///
    /// Both populations, in the order <c>_trackByEntity</c> is filled from, so this can never see
    /// fewer subjects than <see cref="TrackFor(int)"/> does: a player's track lives in
    /// <see cref="PlayerTracks"/> and a scan of <see cref="Props"/> alone would find nothing for one.
    /// </remarks>
    public ScenePropTrack? TrackFor(int entityIndex, double tick)
    {
        if (_tracksBySlot.TryGetValue(entityIndex, out List<ScenePropTrack>? occupants))
        {
            foreach (ScenePropTrack track in occupants)
            {
                if (track.Alive(tick))
                {
                    return track;
                }
            }
        }

        return null;
    }

    /// <summary>Every track that has ever occupied one slot, in the order they were recorded.</summary>
    /// <param name="entityIndex">The entity's slot.</param>
    /// <returns>Each occupant, props before players; empty when the slot was never used.</returns>
    /// <remarks>
    /// **What a caller needs to SAY when <see cref="TrackFor(int, double)"/> answers null.** The
    /// difference between "the demo records nothing for that entity" and "that index owns seven
    /// tracks and none of them covers the tick you asked about" is the whole diagnosis, and only the
    /// second one tells the reader which tick to ask instead — it is what made B370's doors
    /// tractable. A caller that reports the first when the second is true sends the reader looking
    /// for a decode bug.
    /// </remarks>
    public IReadOnlyList<ScenePropTrack> TracksFor(int entityIndex) =>
        [.. _props.Concat(_playerTracks).Where(track => track.EntityIndex == entityIndex)];

    /// <summary>How fast a track is moving horizontally at a moment.</summary>
    /// <remarks>
    /// **Differenced from the positions, because velocity is networked only to its owner.**
    /// <c>m_vecVelocity[0..2]</c> sit inside <c>DT_LocalPlayerExclusive</c>
    /// (<c>server/player.cpp:8117</c>), sent through <c>SendProxy_SendLocalDataTable</c> — so a
    /// SourceTV recording carries nobody's velocity at all, because SourceTV is not any of the
    /// players, and a point-of-view recording carries only the recorder's.
    ///
    /// That makes differencing the only thing that works generally rather than a workaround: it is
    /// the sole option for every player in an STV demo and for eleven of twelve in a POV one. The
    /// recorder's own velocity IS available in a POV demo and would be exact; using it is a refinement
    /// this does not make yet.
    ///
    /// An animation state needs speed to tell standing from running — <c>MOVING_MINIMUM_SPEED</c>
    /// is 0.5 units a second in <c>base_playeranimstate.h</c>.
    ///
    /// Sampled over a tenth of a second rather than one tick. A single tick is 15 milliseconds and
    /// the positions are interpolated, so differencing two adjacent samples measures the
    /// interpolator's noise as much as the player's motion; a tenth of a second is long enough to
    /// be a speed and short enough to still be this moment's.
    ///
    /// Vertical motion is left out, which is what <c>GetOuterXYSpeed</c> does — a falling player is
    /// not running.
    /// </remarks>
    /// <summary>
    /// The seconds a player's speed and heading are differenced over — this project's choice, not an engine setting, and
    /// a reader of the track that far before the tick, which a window's reach must cover (D220).
    /// </summary>
    internal const double MotionWindowSeconds = 0.1d;

    /// <summary>The motion window in ticks, at the 0.015 s tick it has always been differenced at — as a float, bit for bit.</summary>
    private const double MotionWindowTicks = MotionWindowSeconds / (double)0.015f;

    /// <summary>The horizontal speed between the track's pose now and <see cref="MotionWindowTicks"/> before.</summary>
    private static float SpeedOf(ScenePose? now, ScenePose? was)
    {
        // Stryker disable once : a mutant that empties the guard body leaves the poses unassigned below (CS0165), and Safe
        // Mode then drops every mutation in this method — B410.
        if (now is not { } at || was is not { } before)
        {
            return 0f;
        }

        float across = at.X - before.X;
        float along = at.Y - before.Y;

        return MathF.Sqrt((across * across) + (along * along)) / (float)MotionWindowSeconds;
    }

    /// <summary>Which way a track is travelling, in degrees, or null when it is still.</summary>
    /// <remarks>
    /// Differenced over the same pair as <see cref="SpeedOf"/> and for the same reason: velocity
    /// is inside <c>DT_LocalPlayerExclusive</c>, so a SourceTV demo carries nobody's.
    ///
    /// Null rather than zero when stationary, because zero degrees is due east and a player
    /// standing still is not facing east — it is a different question with no answer, and
    /// answering it anyway makes every idle player run on the spot toward the same corner of the
    /// map.
    /// </remarks>
    private static float? HeadingOf(ScenePose? now, ScenePose? was)
    {
        // Stryker disable once : a mutant that empties the guard body leaves the poses unassigned below (CS0165), and Safe
        // Mode then drops every mutation in this method — B410.
        if (now is not { } at || was is not { } before)
        {
            return null;
        }

        float across = at.X - before.X;
        float along = at.Y - before.Y;

        // Below this the direction is numerical noise in the position rather than movement:
        // MOVING_MINIMUM_SPEED is 0.5 units a second (base_playeranimstate.h), which over a tenth
        // of a second is 0.05 units.
        return ((across * across) + (along * along)) < 0.0025f
            ? null
            : MathF.Atan2(along, across) * (180f / MathF.PI);
    }

    /// <summary>The local player as <c>InterpolateViewpoint</c> left him, animated as the client animates him (B56).</summary>
    /// <remarks>
    /// <c>C_TFPlayer::UpdateClientSideAnimation</c> drives the local player's anim state from <c>EyeAngles()</c>
    /// (<c>c_tf_player.cpp:4279-4284</c>), which is <c>pl.v_angle</c> (<c>:7280-7290</c>,
    /// <c>baseplayer_shared.cpp:303-311</c>) — <c>SetLocalViewAngles</c>' value. <c>EstimateAbsVelocity</c> returns
    /// <c>GetAbsVelocity()</c> for him (<c>c_baseentity.cpp:5852-5858</c>): the last <c>m_vecVelocity</c> received,
    /// stepped, since its <c>AddVar</c> is commented out (<c>:907-912</c>). Zero when never sent, as the entity is
    /// allocated zeroed. The feet advance per frame from that same local yaw (<see cref="RecorderFeet"/>, B450), are the
    /// drawn yaw (<c>m_angRender[YAW] = m_flCurrentFeetYaw</c>, <c>multiplayer_animstate.cpp:1765</c>), and the twist
    /// is measured against them (<c>:1768-1772</c>).
    ///
    /// **Prediction's velocity when it ran** (D205): <c>CPrediction::FinishMove</c> writes <c>SetAbsVelocity</c>
    /// (<c>prediction.cpp:708</c>), and demo playback runs prediction, so that is what the client animates him from.
    /// </remarks>
    private ScenePlayer LocalPlayer(
        ScenePlayer player, RecordedView viewpoint, double tick, RecorderFeet feet,
        (float X, float Y, float Z)? predictedVelocity)
    {
        float eyeYaw = Normalize(viewpoint.LocalAngles.Yaw);
        (float X, float Y, float Z) velocity = predictedVelocity ?? player.Velocity ?? default;

        // `vecVelocity.Length() > 1.0f` (:1709): the three-dimensional length, unlike the XY speed below.
        float length = MathF.Sqrt((velocity.X * velocity.X) + (velocity.Y * velocity.Y) + (velocity.Z * velocity.Z));
        float feetYaw = feet.Advance(tick, eyeYaw, length, player.Yaw, IntervalPerTick);

        // GetOuterXYSpeed, and CalcMovementSpeed's `flSpeed > MOVING_MINIMUM_SPEED` (0.5).
        float speed = MathF.Sqrt((velocity.X * velocity.X) + (velocity.Y * velocity.Y));
        float? heading = speed > MovingMinimumSpeed
            ? MathF.Atan2(velocity.Y, velocity.X) * (180f / MathF.PI)
            : null;

        (float moveX, float moveY) = MoveParameters(heading, eyeYaw);

        return player with
        {
            X = viewpoint.Origin.X,
            Y = viewpoint.Origin.Y,
            Z = viewpoint.Origin.Z,
            Yaw = feetYaw,
            EyeYaw = eyeYaw,
            EyePitch = Normalize(viewpoint.LocalAngles.Pitch),
            AimYaw = FeetYaw.AimYaw(eyeYaw, feetYaw),
            Speed = speed,
            MoveX = moveX,
            MoveY = moveY,

            // One GetAbsVelocity() for every reader, e.g. InvisibilityThink's motion cloak (tf_player_shared.cpp:8020).
            Velocity = predictedVelocity ?? player.Velocity,
        };
    }

    /// <summary><c>MOVING_MINIMUM_SPEED</c> (<c>base_playeranimstate.h</c>): 0.5 units a second.</summary>
    private const float MovingMinimumSpeed = 0.5f;

    /// <summary>The <c>move_x</c> and <c>move_y</c> pose parameters for a moving player.</summary>
    /// <param name="heading">Which way the player is travelling, in degrees, or null when still.</param>
    /// <param name="bodyYaw">Which way the player is facing, in degrees.</param>
    /// <returns>The unit vector of travel in the body's frame, or zero when standing still.</returns>
    /// <remarks>
    /// **Ported from <c>CMultiPlayerAnimState::ComputePoseParam_MoveYaw</c>**
    /// (<c>multiplayer_animstate.cpp:1575</c>):
    ///
    /// <code>
    /// float flYaw = flAngle - m_PoseParameterData.m_flEstimateYaw;
    /// flYaw = AngleNormalize( -flYaw );
    /// flYaw = SnapYawTo( flYaw );
    /// vecCurrentMoveYaw.x =  cos( DEG2RAD( flYaw ) );
    /// vecCurrentMoveYaw.y = -sin( DEG2RAD( flYaw ) );
    /// </code>
    ///
    /// **The snap is Valve's and it is not a rounding convenience.** <c>SnapYawTo</c>
    /// (<c>:1443</c>) forces the direction to the nearest of eight compass points using thresholds
    /// of 23, 67, 113 and 157 degrees, so a player strafing slightly off true still plays the
    /// clean sideways animation rather than a permanent blend of two. Leaving it out makes every
    /// player's legs waver between animations as the differenced heading jitters.
    ///
    /// **<c>m_flEstimateYaw</c> is approximated by the body yaw**, which is what this project has.
    /// The engine tracks a separate estimate that lags the eyes while turning on the spot, so a
    /// player spinning in place will differ slightly here. Recorded rather than hidden; it needs
    /// the rest of the turn-in-place state (B61) to do properly.
    /// </remarks>
    private static (float X, float Y) MoveParameters(float? heading, float bodyYaw)
    {
        if (heading is not { } travel)
        {
            return (0f, 0f);
        }

        // **estimateYaw − eyeYaw, and the order is the whole of a defect that lasted until it was
        // measured.** The engine computes `flYaw = flAngle - m_flEstimateYaw` and then
        // `AngleNormalize( -flYaw )`, so the two negations cancel and what reaches the cosine is
        // the direction of travel minus the way the body faces. This project had it the other way
        // round, which is zero for a player running dead forward — so a measurement of a forward
        // run could not see it — and which swaps strafing left with strafing right.
        float yaw = Normalize(travel - bodyYaw);
        (float sine, float cosine) = MathF.SinCos(yaw * (MathF.PI / 180f));

        float x = cosine;
        float y = -sine;

        // **"push edges out to -1 to 1 box"**, Valve's own comment. A unit vector puts a diagonal
        // at 0.707 on each axis, which is halfway along a nine-way grid's cell, so the corner
        // animations authored for the diagonals were never reached. Dividing by the larger
        // component sends every direction to the edge of the box instead of the circle.
        float scale = MathF.Max(MathF.Abs(x), MathF.Abs(y));

        // Guarded exactly where Valve guards it — `if ( flInvScale != 0.0f )`. The vector is a
        // cosine and a sine, so this only fires on a degenerate value, but dividing by it would
        // give two NaNs that reach the blend grid as a plausible-looking mid-cell.
        if (scale != 0f)
        {
            x /= scale;
            y /= scale;
        }

        // **The speed scaling is NOT applied, and this is the one part of the engine's function
        // left out.** After the push-out it does:
        //
        //     float flMaxSpeed = GetBasePlayer()->GetSequenceGroundSpeed( GetSequence() );
        //     if ( flMaxSpeed > flSpeed ) { x *= flSpeed / flMaxSpeed; y *= flSpeed / flMaxSpeed; }
        //
        // which pulls a player moving slower than their animation was authored for back towards
        // the middle of the grid. flMaxSpeed is the authored ground speed of the CHOSEN SEQUENCE,
        // read from mstudiomovement_t in the model file — and this layer decodes a demo and has
        // never opened a model. Recorded in B101 rather than approximated: a guessed maximum would
        // scale every player by a number with no relationship to what they are playing.
        return (x, y);
    }

    /// <summary>Brings an angle into −180 to 180.</summary>
    /// <param name="degrees">Any angle, including one several turns out.</param>
    /// <returns>The same direction, expressed once.</returns>
    /// <remarks>
    /// **One direction must have one representation, or nothing can compare two of them.** The wire
    /// sends yaw as 0..360 and everything here stores (−180, 180]; a player's facing was measured
    /// held as 220.997 and −139.003 at once, which is the same direction and a full turn apart. Any
    /// comparison or interpolation across that boundary is then wrong by 360 — and the boundary is
    /// due south, where players spend a great deal of time.
    ///
    /// Internal so the invariant can be asserted over the whole circle rather than at the one value
    /// somebody happened to measure. A wrap defect lives at a boundary, and a single example never
    /// sits on one.
    /// </remarks>
    internal static float NormalizeAngle(float degrees) => Normalize(degrees);

    private static float Normalize(float degrees)
    {
        float wrapped = degrees % 360f;

        if (wrapped > 180f)
        {
            wrapped -= 360f;
        }
        else if (wrapped < -180f)
        {
            wrapped += 360f;
        }

        return wrapped;
    }

    // **SnapYawTo was implemented here and is deliberately gone.** It is real engine code
    // (multiplayer_animstate.cpp:1443) and forces a direction to the nearest of eight compass
    // points, but ComputePoseParam_MoveYaw calls it only under `if ( mp_slammoveyaw.GetBool() )` —
    // and that cvar is declared `ConVar mp_slammoveyaw( "mp_slammoveyaw", "0", FCVAR_REPLICATED |
    // FCVAR_DEVELOPMENTONLY, "Force movement yaw along an animation path." )`. Default off, and
    // development-only, so no shipped TF2 client takes that branch.
    //
    // This project applied it unconditionally, with a comment arguing it stopped the legs wavering
    // between animations as the differenced heading jitters. That reasoning is plausible and is not
    // what the engine does: it quantised every direction to eight, so a player running at 30° off
    // their facing animated as though at 45°. Kept in the history rather than in the code, because
    // an unused private method is dead weight and the reason it went belongs with the decision.

    private static int? First(EntityState player, string[] keys)
    {
        foreach (string key in keys)
        {
            if (player.Integer(key) is { } value)
            {
                return value;
            }
        }

        return null;
    }
}
