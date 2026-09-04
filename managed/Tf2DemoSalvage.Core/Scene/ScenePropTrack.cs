using System;
using System.Collections.Generic;
using System.Numerics;

namespace Tf2DemoSalvage.Core.Scene;

/// <summary>
/// What kind of thing a model reference names, and therefore how to draw it.
/// </summary>
/// <remarks>
/// **Valve's own <c>modtype_t</c>**, from <c>src/public/model_types.h</c>: <c>mod_bad</c>,
/// <c>mod_brush</c>, <c>mod_sprite</c>, <c>mod_studio</c>. The engine keeps the distinction because
/// the three real kinds come from entirely different places, and
/// <c>C_BaseEntity::IsBrushModel</c> exists to ask which one an entity has.
///
/// **The corpus taught this one rather than the other way round.** A test written to demand
/// <c>models/</c> failed twice: on <c>*3</c> from the 2007 demo, then on
/// <c>sprites/light_glow02_noz.vmt</c> from the 2008 one. One string table carries all three, and a
/// viewer that hands any of them to a <c>.mdl</c> loader draws nothing and reports nothing.
/// </remarks>
public enum SceneModelKind
{
    /// <summary>Nothing recognisable — <c>mod_bad</c>.</summary>
    Unknown = 0,

    /// <summary>An inline BSP submodel, named <c>*N</c>: a door, a lift, a visualiser.</summary>
    Brush,

    /// <summary>A camera-facing sprite, named by its material.</summary>
    Sprite,

    /// <summary>An ordinary <c>.mdl</c> studio model.</summary>
    Studio,
}

/// <summary>
/// Where a model-bearing entity was, and what it was doing, at one moment.
/// </summary>
/// <remarks>
/// A struct because a match produces a great many of these and none of them is shared. Scale
/// defaults to 1 rather than 0 so a pose built from properties the demo never sent is drawn at its
/// authored size instead of vanishing.
/// </remarks>
public readonly record struct ScenePose
{
    /// <summary>World position, east.</summary>
    public float X { get; init; }

    /// <summary>World position, north.</summary>
    public float Y { get; init; }

    /// <summary>World position, up.</summary>
    public float Z { get; init; }

    /// <summary>Rotation about the side axis, in degrees.</summary>
    public float Pitch { get; init; }

    /// <summary>Rotation about the vertical axis, in degrees.</summary>
    public float Yaw { get; init; }

    /// <summary>Rotation about the forward axis, in degrees.</summary>
    public float Roll { get; init; }

    /// <summary>Size relative to the model as authored.</summary>
    public float Scale { get; init; } = 1f;

    /// <summary>TF2's per-BONE head scale — <c>m_flHeadScale</c>.</summary>
    /// <remarks>
    /// **Three separate fields rather than one, because the engine applies three separate passes**
    /// (`c_tf_player.cpp:8815`) and they do different things: the head is scaled where it stands,
    /// the torso is COMPRESSED toward the pelvis without any basis being touched, and the hands are
    /// scaled along with every descendant. Collapsing them would look right only while a demo set
    /// them equal, which is exactly what an ordinary match does by setting all three to 1 (B312).
    /// </remarks>
    public float HeadScale { get; init; } = 1f;

    /// <summary>TF2's per-bone torso scale — <c>m_flTorsoScale</c>.</summary>
    public float TorsoScale { get; init; } = 1f;

    /// <summary>TF2's per-bone hand scale — <c>m_flHandScale</c>.</summary>
    public float HandScale { get; init; } = 1f;

    /// <summary>The alpha byte of <c>m_clrRender</c>; 255 when the entity never said.</summary>
    /// <remarks>
    /// **Not nullable, because the default IS the answer** — an entity nobody has tinted is opaque,
    /// and a delta-compressed format sends only what changed. Feeds
    /// <c>FxBlend.Compute</c>, which is <c>C_BaseEntity::ComputeFxBlend</c> (B221).
    /// </remarks>
    public byte RenderAlpha { get; init; } = 255;

    /// <summary><c>m_nRenderFX</c>, the effect animating the alpha; <c>kRenderFxNone</c> by default.</summary>
    public int RenderFx { get; init; }

    /// <summary><c>m_nRenderMode</c>, the blend mode; <c>kRenderNormal</c> by default.</summary>
    /// <remarks>
    /// **The default is load-bearing.** `ComputeFxBlend`'s last branch answers 255 for
    /// <c>kRenderNormal</c> and the colour's alpha for everything else, so a wrong default here
    /// makes every untouched entity translucent rather than merely mis-tagged. Measured on real
    /// matches: 1,852 of 1,973 entities are <c>kRenderNormal</c>, and 118 are <c>kRenderNone</c> —
    /// which the engine does not draw at all.
    /// </remarks>
    public int RenderMode { get; init; }

    /// <summary>Where this entity starts fading with distance — <c>m_fadeMinDist</c>.</summary>
    /// <remarks>
    /// **Discrete, and not blended between keyframes.** It is a property of the model's placement
    /// rather than a quantity that moves, so a value part-way between two settings names nothing.
    /// Zero with <see cref="FadeMaximumDistance"/> also zero means the entity does not fade, which
    /// is <c>ComputeDistanceFade</c>'s own first branch (B268).
    /// </remarks>
    public float FadeMinimumDistance { get; init; }

    /// <summary>Where this entity becomes invisible — <c>m_fadeMaxDist</c>.</summary>
    public float FadeMaximumDistance { get; init; }

    /// <summary>What the wire says this entity's pose parameters are, normalised.</summary>
    /// <remarks>
    /// **Empty for a player and populated for everything else animating**, which is the send
    /// table's split rather than a rule invented here: <c>CBaseAnimating</c> networks all 24
    /// (<c>server/baseanimating.cpp:243</c>) and <c>tf_player.cpp:769</c> excludes them, because a
    /// player's are computed by <c>CBasePlayerAnimState</c> on the client.
    ///
    /// **Treated as immutable.** <c>ScenePropTrack.At</c> hands the same array through when two keyframes
    /// agree rather than allocating a copy per sampled frame, so a caller that wrote into one would
    /// be writing into the track's stored keyframe.
    /// </remarks>
    public IReadOnlyList<float> PoseParameters { get; init; } = [];

    /// <summary>The counter that re-arms this entity's animation events.</summary>
    /// <remarks>
    /// **Discrete, and compared rather than read** (B275). <c>DoAnimationEvents</c> does not care
    /// what the number IS; it restarts the event walk when it differs from the one it saw last
    /// (<c>c_baseanimating.cpp:3618</c>), which is how a taunt played twice in a row sounds twice
    /// while its sequence number never moves. A value part-way between two counters names nothing,
    /// so it takes the earlier keyframe's like the other discrete fields.
    /// </remarks>
    public int ResetEventsParity { get; init; }

    /// <summary>How fast the animation advances, as a multiple of its authored rate.</summary>
    /// <remarks>
    /// **The third factor in Valve's cycle advance** (<c>c_baseanimating.cpp:5493</c>):
    ///
    /// <code>
    /// float addcycle = flInterval * cyclerate * m_flPlaybackRate;
    /// </code>
    ///
    /// One when the demo never said, which is the engine's default and not a sentinel — a rate of
    /// zero would freeze the animation, which is the wrong reading of "never mentioned".
    ///
    /// Added 2026-08-16. <c>m_flPlaybackRate</c> had been retained and decoded since the whitelist
    /// was written, and nothing outside a unit test ever read it, so anything not playing at rate 1
    /// animated at the wrong speed.
    /// </remarks>
    public float PlaybackRate { get; init; } = 1f;

    /// <summary>The gestures layered over this entity's main sequence, or null for none.</summary>
    /// <remarks>
    /// **For a player these are the only layers there are** (B282). TF2 excludes the whole
    /// <c>m_AnimOverlay</c> array from the player's send table (<c>tf_player.cpp:774</c>), so a
    /// reload, a flinch or an attack reaches a demo as a <c>CTEPlayerAnimEvent</c> temp entity and
    /// nowhere else. <see cref="PlayerGestureFeed"/> turns those into slots and
    /// <c>PlayerProps.Add</c> carries them here.
    ///
    /// **Null rather than empty**, because most entities have none and this is read once per drawn
    /// prop per frame.
    /// </remarks>
    public IReadOnlyList<SceneGesture>? Gestures { get; init; }

    /// <summary>The animation layers this entity sends, in <c>m_nOrder</c>.</summary>
    /// <remarks>
    /// **The other source of layers, and the one a PLAYER never uses** (B285).
    /// <c>tf_player.cpp:774</c> excludes <c>m_AnimOverlay</c> from the player's send table, so
    /// these come from every other animating entity — a sentry's aim, a dispenser, a teleporter.
    /// The engine draws both kinds through the same <c>AccumulateLayers</c>; the difference is
    /// only where they were read from.
    ///
    /// **Empty rather than null**, because unlike <see cref="Gestures"/> this is read for every
    /// prop on every keyframe and the empty list costs nothing.
    /// </remarks>
    public IReadOnlyList<SceneAnimationLayer> Layers { get; init; } = [];

    /// <summary>This entity's bone controller values, normalised, by input index.</summary>
    /// <remarks>
    /// **<c>m_flEncodedController</c>, which is networked and therefore recoverable** —
    /// eleven bits each over nought to one (<c>baseanimating.cpp:248</c>). `CalcBoneAdj`
    /// (<c>bone_setup.cpp:2462</c>) reads them to bend a single bone: a sentry's barrel, a door's
    /// hinge, anything an author wired to a controller rather than to an animation (B288).
    ///
    /// **Indexed by INPUT rather than by controller**, because a model's controllers name which
    /// input drives them and are not stored in input order.
    /// </remarks>
    public IReadOnlyList<float> BoneControllers { get; init; } = [];

    /// <summary>Which animation is playing.</summary>
    /// <remarks>
    /// **Zero when the demo never said, because zero is the engine's default** — <c>m_nSequence</c>
    /// is a plain int initialised to 0 (<c>BaseAnimatingOverlay.cpp:104</c>), and a delta format only
    /// sends what changed from the baseline. "Never mentioned" and "sequence 0" are the same
    /// statement about the wire.
    ///
    /// **This was −1 until 2026-08-16, and the sentinel was doing harm.** Every drawing consumer
    /// immediately undid it — <c>Math.Max(0, pose.Sequence)</c> in two places, and
    /// <c>PropModels.Select</c> opening with <c>sequence &lt; 0 ? 0 : sequence</c> under a comment
    /// saying "a sequence the demo never mentioned is sequence zero, not an error". But
    /// <c>InterpolateCycle</c> does not clamp, it COMPARES: a change of sequence is a cut, so
    /// a first keyframe of −1 followed by an explicit 0 registered a change that never happened and
    /// froze the cycle across that span.
    ///
    /// Invented sentinels for absent values are the hazard the owner's rule names: Valve's data is
    /// values rather than pointers, so absence is always a default and never a third state.
    /// </remarks>
    public int Sequence { get; init; }

    /// <summary>How far through that animation, from 0 to 1.</summary>
    public float Cycle { get; init; }

    /// <summary>Demo time this animation started, for a prop whose cycle restarts.</summary>
    /// <remarks>
    /// **Zero means "measure from demo time", which is what everything except a viewmodel wants.**
    /// A player's cycle advances continuously and is corrected by what the wire sends; a viewmodel's
    /// is restarted outright whenever the server hands it an animation, and
    /// <c>C_BaseViewModel::UpdateAnimationParity</c> does that by setting
    /// <c>m_flAnimTime = curtime</c> alongside <c>SetCycle( 0 )</c>. This is that
    /// <c>m_flAnimTime</c>.
    ///
    /// Kept as a default of zero so nothing else in the scene changes behaviour: subtracting zero
    /// from demo time is the free-running clock every other prop already had.
    /// </remarks>
    public double AnimationStartSeconds { get; init; }

    /// <summary>How fast the entity is moving horizontally, when that was worked out.</summary>
    /// <remarks>
    /// **Only players carry this, and only because nothing else can supply it.** A demo networks
    /// no animation state for a player, so which sequence to play has to be computed - and the
    /// input that decides standing from running is horizontal speed, which the engine reads from
    /// the entity's own velocity. Null for anything whose animation the demo does state.
    /// </remarks>
    public float? Speed { get; init; }

    /// <summary>
    /// A player's <c>m_fFlags</c>, for choosing an activity, or null when nothing said.
    /// </summary>
    /// <remarks>
    /// **Carried on the pose because the choice cannot be made where the flags are known.** A player
    /// becomes a prop before its model has been read, and the activity lookup needs the model — so
    /// the two happen in separate passes and the flags have to travel between them.
    ///
    /// Discrete, like <see cref="Body"/> and <see cref="Hidden"/>: there is no halfway between
    /// crouched and standing, so an interpolated pose takes the earlier keyframe's value rather than
    /// blending toward the next.
    ///
    /// Null when the recording did not say. That used to be documented as "every player but the
    /// recorder in a POV demo, since the send prop is in DT_LocalPlayerExclusive" — which was wrong
    /// in both halves: it is on <c>DT_BasePlayer</c> and reaches every player in the PVS (B103).
    /// </remarks>
    public int? Flags { get; init; }

    /// <summary>The activity suffix the held weapon drives, such as <c>SECONDARY</c>.</summary>
    /// <remarks>
    /// **Travels with the flags and for the same reason**: the weapon is known when the player
    /// becomes a prop, and the activity lookup that needs it happens a pass later, once the model
    /// has been read.
    ///
    /// A string rather than an enumeration because it is pasted onto an activity name —
    /// <c>ACT_MP_RUN_</c> plus this — and the set of suffixes is the game's data rather than this
    /// project's. Null means nothing was resolved, and the lookup then uses the primary forms, which
    /// is the engine's own default.
    /// </remarks>
    public string? Slot { get; init; }

    /// <summary>How long the player has been off the ground, for splitting a jump.</summary>
    /// <remarks>
    /// Travels with the flags, like <see cref="Slot"/>. Discrete in the same sense: it is a clock
    /// reading rather than a position, and interpolating between two keyframes' readings would
    /// invent a moment neither recorded.
    /// </remarks>
    public float? AirborneSeconds { get; init; }

    /// <summary>Whether the player is air-walking: rising fast, and their class allows it.</summary>
    /// <remarks>
    /// Both halves are resolved before this is set — the rise in the timeline, the class in the
    /// viewer, which is the only layer that can open a class script.
    /// </remarks>
    public bool Airwalking { get; init; }

    /// <summary>How far up or down the player is looking, in degrees.</summary>
    /// <remarks>
    /// **Not <see cref="Pitch"/>, and the two must not be confused.** That one rotates the whole
    /// model and stays zero for a player, because a player stands upright however far the eyes are
    /// pitched (<c>tf_player.cpp:2689</c>). This one drives the <c>body_pitch</c> pose parameter,
    /// which aims the torso within the standing body.
    ///
    /// Stored as the eye pitch itself. The negation <c>ComputePoseParam_AimPitch</c> applies
    /// belongs where the parameter is bound, not here — a stored value that is already negated
    /// reads as a bug every time someone compares it against the wire.
    /// </remarks>
    public float? EyePitch { get; init; }

    /// <summary>Where the player is LOOKING, when that differs from where the body is drawn.</summary>
    /// <remarks>
    /// **<see cref="Yaw"/> is the feet and this is the eyes**, and the two part company when a
    /// player turns on the spot. The body is rendered at the feet yaw
    /// (<c>m_angRender[YAW] = m_flCurrentFeetYaw</c>) while the torso twists to face the eyes.
    ///
    /// Kept because <c>ComputePoseParam_MoveYaw</c> reads the EYE yaw —
    /// <c>float flAngle = AngleNormalize( m_flEyeYaw )</c> — so the movement blend must not start
    /// using the feet when the drawn yaw became the feet. Null for anything that is not a player,
    /// where the entity's own rotation is the only yaw there is.
    /// </remarks>
    public float? EyeYaw { get; init; }

    /// <summary>The <c>body_yaw</c> pose parameter: how far the torso is twisted.</summary>
    /// <remarks>
    /// Already negated, as <c>SetPoseParameter( m_iAimYaw, -flAimYaw )</c> negates it. See
    /// <see cref="FeetYaw.AimYaw"/>.
    /// </remarks>
    public float? AimYaw { get; init; }

    /// <summary>How deep in water the player is: 0 dry, 1 feet, 2 waist, 3 eyes.</summary>
    /// <remarks>
    /// Travels with the flags, for the same reason. Waist deep is where the activity changes, so a
    /// player who jumps into water swims instead of falling with their legs tucked.
    /// </remarks>
    public int? WaterLevel { get; init; }

    /// <summary>The <c>move_x</c> pose parameter: how much of the motion is forward.</summary>
    /// <remarks>
    /// **A movement sequence is a blend grid and these are its coordinates.** Without them the
    /// engine's own lookup takes the grid's corner, which is one fixed direction — the legs then
    /// run that way however the body is turned.
    ///
    /// Ported from <c>CMultiPlayerAnimState::ComputePoseParam_MoveYaw</c>
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
    /// so the pair is the unit vector of travel expressed in the body's own frame — <c>(1, 0)</c>
    /// running straight forward, <c>(-1, 0)</c> backpedalling.
    ///
    /// Zero for a player standing still, which is what the engine leaves them at.
    /// </remarks>
    /// <summary>Which alternative each of the model's body parts shows.</summary>
    /// <remarks>
    /// **A capture point's label and a player's drawn weapon are the same mechanism.** A model's
    /// body parts each offer alternatives and this packs one choice per part; the renderer draws
    /// the selected mesh of each part rather than all of them.
    ///
    /// Zero when the entity never sent one, which selects every part's first alternative — what the
    /// engine shows for an entity that never sets it.
    /// </remarks>
    public int Body { get; init; }

    public float MoveX { get; init; }

    /// <summary>The <c>move_y</c> pose parameter: how much of the motion is sideways.</summary>
    public float MoveY { get; init; }

    /// <summary>Which skin family paints this entity, where zero is the model's own.</summary>
    /// <remarks>
    /// **A team colour is a different material rather than a tint.** TF2's player models carry two
    /// skin families and the game picks by team - RED is 0 and BLU is 1. Left at zero for anything
    /// that has no team.
    /// </remarks>
    public int Skin { get; init; }

    /// <summary>Whether the engine was told not to draw this entity at this moment.</summary>
    /// <remarks>
    /// **Part of the pose rather than an end to the track**, because a hidden entity comes back.
    /// A health pack that has been taken sets <c>EF_NODRAW</c> and respawns in the same place a
    /// few seconds later; ending its track would lose everything after the first pickup.
    /// </remarks>
    public bool Hidden { get; init; }

    /// <summary>Whether a weapon is the one in its owner's hands at this moment.</summary>
    /// <remarks>
    /// **<c>m_iState</c>, and it belongs to the MOMENT rather than to the entity** (B244). A player
    /// switches weapons, so the same entity is `WEAPON_IS_ACTIVE` and then
    /// `WEAPON_IS_CARRIED_BY_PLAYER` while remaining itself — which makes it unlike the nine other
    /// facts a track keeps as scalars, every one of which is fixed for an entity's lifetime.
    ///
    /// It was a track scalar until it was measured: those are written while the demo is parsed, so
    /// a reader asking about tick 14000 received the state at the demo's LAST tick. Every medic
    /// whose medigun happened to be holstered at the end drew empty-handed throughout.
    ///
    /// Null means "not a weapon", not "state zero". `m_iState` is declared by
    /// <c>DT_BaseCombatWeapon</c> (<c>basecombatweapon_shared.cpp:2871</c>) so a wearable never
    /// sends it, and 0 is <c>WEAPON_NOT_CARRIED</c> — the real state of a weapon lying on the
    /// floor, which draws.
    /// </remarks>
    public int? WeaponState { get; init; }

    /// <summary>Builds a pose at the world origin, unrotated and unanimated.</summary>
    public ScenePose()
    {
    }
}

/// <summary>One model, as it stands at the tick being drawn.</summary>
/// <param name="EntityIndex">Slot in the entity table, so a viewer can label or pick it.</param>
/// <param name="ModelPath">What to draw, as <c>modelprecache</c> named it.</param>
/// <param name="Kind">Which loader the path belongs to.</param>
/// <param name="Pose">Where it is and what it is doing.</param>
/// <param name="AttachedTo">
/// The entity whose skeleton carries this one, or <c>null</c> when it stands on its own origin.
/// </param>
/// <param name="OwnedBy">
/// Which entity OWNS this one, whatever it hangs from, or <c>null</c>. Separate from
/// <paramref name="AttachedTo"/> because they answer different questions: that one says where the
/// prop is DRAWN, and this says who it belongs to. The engine keys a carried weapon's visibility on
/// the second — `C_BaseCombatWeapon::ShouldDraw` hides a weapon owned by the player whose eyes you
/// are in, because the viewmodel draws it instead.
/// </param>
/// <param name="WeaponState">
/// A combat weapon's carry state — 0 on the ground, 1 carried, 2 active (<c>shareddefs.h:296</c>) —
/// or <c>null</c> when this is not a combat weapon at all. Null is the meaningful case as often as
/// not: <c>m_iState</c> comes from <c>DT_BaseCombatWeapon</c>, so a <c>CTFWearable</c> such as the
/// Mantreads or a demoman's shield never sends it, and those are worn whatever is in the player's
/// hands. See <see cref="ScenePose.WeaponState"/>, which is where it is sampled from: it changes
/// while the entity lives, so it belongs to the moment rather than to the track (B244).
/// </param>
/// <param name="BoneMerged">
/// Whether it rides its parent's SKELETON — <c>EF_BONEMERGE</c>, the second branch of
/// <c>CalcAbsolutePosition</c> — rather than concatenating its own transform onto its parent's.
/// </param>
/// <param name="ItemDefinitionIndex">
/// Which econ item this is, when it is one, so a weapon whose model the wire never carried can
/// still be named. <c>CEconEntity::SetModel</c> resolves
/// <c>pItem-&gt;GetPlayerDisplayModel( iClass, team )</c> — <c>model_player</c> from
/// <c>items_game.txt</c>, <c>econ_entity.cpp:1167</c> — so the networked index is a convenience
/// rather than the source of truth. Measured on `cp_fulgur`: every <c>CWeaponMedigun</c> networks
/// neither <c>m_nModelIndex</c> nor <c>m_iWorldModelIndex</c>, and every one states item 211.
/// </param>
/// <param name="OfDisguise">
/// Whether this cosmetic or weapon belongs to the owner's DISGUISE rather than to the owner —
/// <c>m_bDisguiseWearable</c> and <c>m_bDisguiseWeapon</c>, both networked.
/// </param>
/// <param name="ClassName">
/// Its networked class name, the stock fallback for an item that names no model of its own.
/// </param>
/// <param name="OfRecordersTeam">
/// Whether this entity is on the RECORDER's team — <c>C_FuncRespawnRoomVisualizer::DrawModel</c>
/// hides a spawn wall from the team that spawns behind it (<c>c_func_respawnroom.cpp:47</c>).
/// </param>
/// <param name="Econ">
/// The wire's attribute inputs for <c>CEconItemView::IterateAttributes</c>, or <c>null</c> for an
/// entity that carries none — see <see cref="EconAttributeWire"/> (B234).
/// </param>
/// <param name="FirstPerson">
/// Whether this prop is part of the first-person scene, which selects the display-flag mask its
/// attachments are filtered by (B252).
/// </param>
/// <param name="ClientSideAnimated">
/// Whether the CLIENT advances this entity's cycle — <c>m_bClientSideAnimation</c>, which is
/// membership in the engine's client-side animation list (B259).
/// </param>
/// <param name="DeathSequence">
/// The death animation a CORPSE plays, by label, or null for everything else (B323).
/// <para>
/// **A name rather than an index, because only the model knows which index a label is.** The
/// decision — which of TF2's two death animations, and whether the coin flip kept it — is made
/// where models cannot be opened; <c>EntityModelSet.UpdateClientSideAnimations</c> resolves it with
/// <c>SequenceByLabel</c>, which is the engine's own <c>LookupSequence</c>.
/// </para>
/// </param>
/// <param name="MaterialOverride">
/// One VMT path replacing EVERY material this model has, or null for the ordinary case (B325).
/// <para>
/// **This is the engine's <c>ForcedMaterialOverride</c>, not a skin.** A skin picks another entry
/// from the model's own material table; this ignores the table entirely. TF2 sets it from
/// <c>m_bGoldRagdoll</c> and <c>m_bIceRagdoll</c> on a corpse, and — because the override is per
/// renderable rather than per entity — sets it again on each of that corpse's worn items.
/// </para>
/// </param>
/// <param name="AttachmentPoint">
/// Which of that entity's named attachment points it hangs from, one-based, or <c>null</c> when it
/// is bone-merged instead.
/// <para>
/// **The two are different mechanisms and an item uses one or the other.** A hat shares bone names
/// with the player and takes their matrices outright; a halo, an MvM canteen and a spellbook share
/// no bone name at all — <c>hwn_spellbook_complete.mdl</c> has a single bone called <c>mvm</c> — and
/// hang off a named point on the wearer instead. Without this they fall back to the wearer's
/// transform, which on a player is their feet (RISKS B82).
/// </para>
/// </param>
public sealed record SceneProp(
    int EntityIndex,
    string ModelPath,
    SceneModelKind Kind,
    ScenePose Pose,
    int? AttachedTo = null,
    int? AttachmentPoint = null,

    // **Who it BELONGS to, which is not who it hangs from.** The engine keys a carried weapon's
    // visibility on ownership (C_BaseCombatWeapon::ShouldDraw), and a weapon that sends its own
    // origin is owned by its carrier while being parented to nobody — so AttachedTo cannot answer
    // the question the first-person view has to ask.
    int? OwnedBy = null,

    // **A combat weapon's carry state, null for anything that is not one.** The engine draws a
    // player's holstered weapons not at all and their wearables always, and the two are told apart
    // by whether `DT_BaseCombatWeapon` was declared — see ScenePose.WeaponState, which is sampled
    // per tick because a weapon is holstered and drawn again while the entity goes on being itself.
    //
    // Appended rather than inserted: every parameter here is positional, so putting it beside the
    // other attachment fields would silently re-map every call site that passes OwnedBy by
    // position.
    int? WeaponState = null,

    // **Which of Valve's two attachment branches this entity takes** (B231). `EF_BONEMERGE` rides
    // the parent's SKELETON — `MoveToAimEnt`, c_baseentity.cpp:4389 — and everything else
    // concatenates its own local transform onto the parent's. `AttachedTo` says what it hangs off
    // and cannot say which mechanism, so treating every parent as a bone merge left a prop hung on
    // brushwork looking for a skeleton that does not exist.
    bool BoneMerged = false,

    // **Which econ item this is, and which class it is, so a model-less weapon can still be
    // named.** A weapon's model comes from `items_game.txt` rather than from the wire —
    // `pItem->GetPlayerDisplayModel( iClass, team )`, `econ_entity.cpp:1167` — and measured on
    // `cp_fulgur` every `CWeaponMedigun` networks neither `m_nModelIndex` nor
    // `m_iWorldModelIndex` while stating item 211, the stock Medi Gun. `WeaponPropModels` turns
    // the pair into a path; the item is the answer and the class name is the stock fallback for
    // an item that has no `model_player` of its own.
    //
    // Appended, like OwnedBy and WeaponState above and for the same reason: every parameter here
    // is positional, so inserting one silently re-maps every call site.
    int? ItemDefinitionIndex = null,
    string ClassName = "",

    // **Whether this belongs to a DISGUISE rather than to its owner.**
    // `m_bDisguiseWearable` (`tf_item_wearable.cpp:36`) and `m_bDisguiseWeapon`
    // (`tf_weaponbase.cpp:198`), both networked. The server sends a disguise's gear as its own
    // entities bone-merged to the spy so an ENEMY sees a convincing soldier; who may see it is
    // `DisguiseVisibility`.
    bool OfDisguise = false,

    // **Whether this entity is on the RECORDER's team**, which some entities are drawn or not drawn
    // by. `C_FuncRespawnRoomVisualizer::DrawModel` (`c_func_respawnroom.cpp:47`) returns without
    // drawing when `pLocalPlayer->GetTeamNumber() == GetTeamNumber()`, so a player standing in
    // their own spawn does not see the team wall across their own doorway.
    //
    // **Computed here rather than in the scene, for the same reason `ScenePlayer.IsEnemy` is**: it
    // compares against the local player, and in a recording that is whoever recorded it — which
    // only the timeline knows.
    //
    // **Not `IsEnemy`, and the difference matters.** With no local player at all — a SourceTV
    // recording — the engine's `pLocalPlayer &&` short-circuits and the visualizer DRAWS. "On the
    // recorder's team" is false there, which is the same answer; "is an enemy" would also be false
    // and would give the opposite one.
    bool OfRecordersTeam = false,

    // **The wire's half of `IterateAttributes`, unresolved** (B234). Null is the honest default
    // for everything that is not an econ entity — a door has no attribute lists, not empty ones —
    // and only `DemoTimeline` can fill it, because only the accumulated entity state holds the
    // two lists and the item id. The consumer completes the resolution with the schema's branch 4.
    EconAttributeWire? Econ = null,

    // **Whether this prop is drawn in the first-person view** (B252), which decides the
    // display-flag mask its attachments are filtered by — `kAttachedModelDisplayFlag_ViewModel`
    // against `WorldModel`, the two masks `DrawEconEntityAttachedModels` is called with. False is
    // the true answer everywhere but `ViewmodelScene`'s three construction sites, which are the
    // first-person scene by definition.
    bool FirstPerson = false,

    // **Whether the CLIENT advances this entity's cycle** - `m_bClientSideAnimation`, which is
    // membership in the engine's client-side animation list (B259). `UpdateClientSideAnimations`
    // walks that list rather than every entity, so a prop that did not ask takes its cycle off the
    // wire and the client advances nothing for it.
    //
    // Appended for the reason every parameter above says: they are positional, so inserting one
    // silently re-maps every call site.
    bool ClientSideAnimated = false,

    // **The death animation a CORPSE plays, by label, or null for everything else** (B323). Carried
    // as a NAME rather than an index because only the model knows which index a label is, and the
    // decision is made where models cannot be opened — `RagdollDeath.SequenceFor` reads the damage
    // type and draws the coin, `EntityModelSet.UpdateClientSideAnimations` resolves it with
    // `SequenceByLabel`, which is the engine's own `LookupSequence`.
    //
    // Appended, like every parameter above and for the same reason: they are positional, so
    // inserting one silently re-maps every call site.
    string? DeathSequence = null,

    // **One material replacing all of the model's own, by VMT path** — `ForcedMaterialOverride`.
    // A gold or iced corpse, and each item it wears, because the engine's override is per
    // renderable rather than per entity. Null everywhere else, which is nearly everywhere.
    string? MaterialOverride = null);

/// <summary>
/// One entity's pose over the whole demo, stored as the moments it changed.
/// </summary>
/// <remarks>
/// **Keyframes rather than a pose per tick, and the arithmetic decided it.** A 1,600-second demo
/// is about 106,000 frames and a match carries a few hundred model-bearing entities, so a pose per
/// entity per frame is tens of millions of records — for a scene in which most of them never move.
/// A health pack that sits still all match costs one keyframe.
///
/// It also matches what a demo is. The stream sends only what changed, so the moments recorded
/// here are exactly the moments the demo spoke. Nothing is interpolated between them: a door that
/// opened at tick 900 was shut at 899, and inventing a position halfway would be this project
/// making up data it was not given.
/// </remarks>
public sealed class ScenePropTrack
{
    private readonly List<(int Tick, ScenePose Pose)> _keyframes = [];

    /// <summary>
    /// For each keyframe, the last tick the demo restated that same pose.
    /// </summary>
    /// <remarks>
    /// **Parallel to <see cref="_keyframes"/> rather than folded into it**, so the public
    /// <see cref="Keyframes"/> shape and everything reading it are untouched. Equal to the
    /// keyframe's own tick for a pose stated once, and later for one held.
    ///
    /// This is the interval an interpolation may legitimately run over. A demo states a stationary
    /// entity's pose repeatedly and those repeats are collapsed, so without this the gap between two
    /// stored keyframes reads as the duration of the movement between them — which for a door is
    /// ten seconds of drift instead of a tenth of a second of travel.
    /// </remarks>
    private readonly List<int> _heldUntil = [];

    /// <summary>
    /// For each keyframe, when the entity said the value applied.
    /// </summary>
    /// <remarks>
    /// **The engine's <c>changetime</c>, which is not the packet's tick** (B273).
    /// <c>OnLatchInterpolatedVariables</c> stamps every simulation-latched variable with the
    /// entity's own <c>GetSimulationTime()</c> (<c>c_baseentity.cpp:2806</c>), and this project
    /// used the packet tick for both purposes.
    ///
    /// Carried alongside rather than replacing the keyframe's tick, because the two answer
    /// different questions: the tick orders the list and dates the state changes a pose carries,
    /// while this dates the interpolated quantities inside it. Measured on the 2013 SourceTV
    /// foundry recording, `CTFPlayer` splits exactly 50/50 between a lag of 0 and 4 ticks — so the
    /// players, the fastest things on screen, were sampled with 60 ms of jitter that no other
    /// entity shared.
    /// </remarks>
    private readonly List<int> _appliedAt = [];

    /// <summary>
    /// For each keyframe, when the entity said its ANIMATION applied.
    /// </summary>
    /// <remarks>
    /// **The engine's second latch clock, and it is a second history rather than a second stamp**
    /// (B274). <c>GetLastChangeTime</c> returns <c>GetAnimTime()</c> for <c>LATCH_ANIMATION_VAR</c>
    /// — which for this project is exactly <see cref="ScenePose.Cycle"/> and
    /// <see cref="ScenePose.PoseParameters"/> — where <see cref="_appliedAt"/> serves origin and
    /// angles. Measured on the 2013 SourceTV foundry recording, the two disagree by more than eight
    /// ticks on 95.5% of the updates carrying both, so neither can stand in for the other: an
    /// entity that animates without moving keeps one simulation time while its animation time runs
    /// on.
    ///
    /// **One list, two search keys, and both are non-decreasing** — which is what makes this cheap.
    /// A server stamps both clocks monotonically, so the keyframes are ordered by animation time as
    /// well as by arrival, and the second lookup is another binary search over the same array
    /// rather than a second list to maintain.
    /// </remarks>
    private readonly List<int> _animationAppliedAt = [];

    /// <summary>For each keyframe, the animation time of the last restatement of that pose.</summary>
    private readonly List<int> _animationHeldUntil = [];

    /// <summary>Whether any keyframe carried an animation clock of its own.</summary>
    /// <remarks>
    /// **False for a player and for most props, and that is the fast path.** TF2's players use
    /// client-side animation and <c>SendProxy_AnimTime</c> asserts they encode no animation time,
    /// so their tracks skip the second lookup entirely. It is set only when a keyframe arrives with
    /// an animation time away from its arrival tick — anything else has nothing to correct.
    /// </remarks>
    private bool _hasAnimationClock;

    /// <summary>
    /// How far behind the requested tick a pose is sampled — the engine's <c>cl_interp</c>.
    /// </summary>
    /// <remarks>
    /// **This is the delay a client renders at, not a smoothing fudge.** Drawing the recent past
    /// is what lets a client interpolate at all — at the present moment there is nothing yet to
    /// interpolate toward.
    ///
    /// **It was seven and the engine's answer is eight** (B267). The old derivation was right as
    /// far as it went — 0.1 seconds at 66.67 ticks is 6.67, rounded to seven — and stopped one
    /// term early. <c>C_BaseEntity::GetInterpolationAmount</c> (`c_baseentity.cpp:5920`) returns
    /// <c>TICKS_TO_TIME( TIME_TO_TICKS( GetClientInterpAmount() ) + serverTickMultiple )</c> on the
    /// branch that names demo playback, so the rounding is followed by a whole extra tick. Every
    /// interpolated entity was drawn one tick nearer the present than the engine draws it.
    ///
    /// Derived through <see cref="DelayTicksFor"/> rather than stated, so the reasoning is code
    /// and the tick interval is an input.
    /// </remarks>
    private int InterpolationDelayTicks { get; set; } = DelayTicksFor(Tf2TickInterval);

    /// <summary>TF2's tick interval, and the fallback when a demo states none.</summary>
    /// <remarks>
    /// Every demo in the corpus measures 66.67 ticks a second, era specimens included, so this is
    /// the observed rate rather than an assumed one — and a header that states nothing is a real
    /// case (`docs/memory/a-header-written-last-is-absent.md`).
    /// </remarks>
    public const double Tf2TickInterval = 0.015d;

    /// <summary>TF2's <c>cl_interp</c> default, which is what a demo is replayed at.</summary>
    /// <remarks>
    /// <c>GetClientInterpAmount</c> (`cdll_bounded_cvars.cpp:127`) takes
    /// <c>MAX( cl_interp, cl_interp_ratio / cl_updaterate )</c>; at TF2's defaults that is
    /// <c>MAX( 0.1, 2/66 )</c> = 0.1. A recording does not carry the recorder's own cvars, so this
    /// is the setting a viewer has to assume.
    /// </remarks>
    public const double DefaultInterpolation = 0.1d;

    /// <summary>How many ticks behind the present an entity is drawn.</summary>
    /// <param name="intervalPerTick">The demo's seconds per tick; non-positive falls back to TF2's.</param>
    /// <param name="interpolation">The client's interp amount in seconds.</param>
    /// <returns>The delay in ticks.</returns>
    /// <remarks>
    /// **Valve's arithmetic, term for term** — <c>TIME_TO_TICKS( dt )</c> is
    /// <c>(int)( 0.5f + dt / TICK_INTERVAL )</c> (`shareddefs.h:17`), which rounds to NEAREST
    /// rather than truncating, and <c>GetInterpolationAmount</c> adds <c>serverTickMultiple</c>
    /// afterwards — one, except on a server simulating alternate ticks.
    ///
    /// Kept in TICKS rather than converted back to time because this class indexes keyframes by
    /// tick; the engine's `TICKS_TO_TIME` on the way out is for a caller that wants seconds.
    /// </remarks>
    public static int DelayTicksFor(
        double intervalPerTick, double interpolation = DefaultInterpolation)
    {
        double interval = intervalPerTick > 0d ? intervalPerTick : Tf2TickInterval;

        return (int)(0.5d + (interpolation / interval)) + 1;
    }

    private int _endTick = int.MaxValue;

    /// <summary>Starts a track for one entity.</summary>
    /// <param name="entityIndex">Slot in the entity table.</param>
    /// <param name="modelPath">The model this entity draws as.</param>
    /// <param name="serialNumber">The engine's serial for this occupant of the slot.</param>
    public ScenePropTrack(int entityIndex, string modelPath, int serialNumber = 0)
    {
        EntityIndex = entityIndex;
        ModelPath = modelPath;
        SerialNumber = serialNumber;
    }

    /// <summary>Slot in the entity table.</summary>
    public int EntityIndex { get; }

    /// <summary>The model this entity draws as.</summary>
    /// <remarks>
    /// **Kept current, because the engine re-applies the model on every update** — the same reason
    /// <see cref="AttachedTo"/> is, and the two are set on adjacent lines in
    /// <c>C_BaseEntity::PostDataUpdate</c> (<c>client/c_baseentity.cpp:2603</c>):
    ///
    /// <code>
    ///   HierarchySetParent(m_hNetworkMoveParent);
    ///   MarkMessageReceived();
    ///   // Make sure that the correct model is referenced for this entity
    ///   ValidateModelIndex();
    ///   if ( updateType == DATA_UPDATE_CREATED ) { ... }
    /// </code>
    ///
    /// Both calls sit ABOVE the <c>DATA_UPDATE_CREATED</c> test, so both run every update, and
    /// <c>ValidateModelIndex</c> ends in <c>SetModelByIndex( m_nModelIndex )</c>
    /// (<c>c_baseentity.cpp:2531</c>). This project followed the parent and fixed the model at
    /// construction — half the mechanism.
    ///
    /// **What that cost is not a corner case, because a creating update rarely carries the model.**
    /// An entity is created as a delta against its class's INSTANCE BASELINE, which is one
    /// representative entity's state, so everything the creating update does not mention comes from
    /// whatever entity happened to supply the baseline. Measured on `cp_fulgur`, slot 432 — the BLU
    /// spawn's windowed door:
    ///
    /// <code>
    ///   Enter 432 serial 998 props 2  modelindex 1154 origin (3440 -2096 240)   &lt;- baseline
    ///   Enter 432 serial 998 props 11 modelindex 1177 origin (2 0 -59)          &lt;- the real values
    /// </code>
    ///
    /// 1154 is `resupply_locker.mdl` and `(3440 -2096 240)` is `prop_locker_blu_5`'s world origin
    /// out of the map's entity lump. The door was a resupply cabinet for the rest of the recording,
    /// and nine other entities took that same baseline's identity the same way. The owner's report
    /// was that the spawn gates and the health cabinet do not draw.
    ///
    /// **Not a reason to stop merging the baseline** — `CL_CopyNewEntity` does exactly that, and
    /// the engine simply overwrites the guess on the next update because it re-reads the index.
    /// Removing the merge would trade this defect for B132's.
    ///
    /// Set through <see cref="Follow"/> rather than by a bare setter, so an empty path — an entity
    /// whose model has not arrived yet — cannot erase a name the track already has.
    /// </remarks>
    public string ModelPath { get; private set; }

    /// <summary>Re-applies the entity's model, as <c>ValidateModelIndex</c> does each update.</summary>
    /// <param name="modelPath">What <c>m_nModelIndex</c> now names, or empty when it names nothing.</param>
    /// <remarks>
    /// **An empty path is ignored, and that is the engine's behaviour rather than a convenience.**
    /// <c>SetModelByIndex</c> resolves through <c>modelinfo->GetModel</c>
    /// (<c>c_baseentity.cpp:1778</c>) and a model index of zero is the "no model" placeholder, which
    /// leaves the entity drawing what it already had. Treating it as a change would blank the name
    /// of every entity whose update happened not to mention its model.
    /// </remarks>
    internal void Follow(string? modelPath)
    {
        if (!string.IsNullOrEmpty(modelPath))
        {
            ModelPath = modelPath;
        }
    }

    /// <summary>Which econ item this entity is, when it is one.</summary>
    /// <remarks>
    /// **A weapon's model comes from its ITEM, not from the wire, and for some weapons the wire
    /// carries no model at all.** `CEconEntity::SetModel` resolves
    /// <c>pItem-&gt;GetPlayerDisplayModel( iClass, team )</c> — <c>model_player</c> from
    /// <c>items_game.txt</c> (<c>econ_entity.cpp:1167</c>) — so <c>m_nModelIndex</c> is a
    /// convenience the server sends when it happens to, not the source of truth.
    ///
    /// Measured on `cp_fulgur`, every weapon with an owner. A rocket launcher sends both indices, a
    /// flamethrower sends the world model, and every `CWeaponMedigun` sends NEITHER — while all of
    /// them state their item:
    ///
    /// <code>
    ///   CTFRocketLauncher model  996 worldmodel  426 item   513
    ///   CTFFlameThrower   model none worldmodel  225 item    40
    ///   CWeaponMedigun    model none worldmodel none item   211
    ///   CTFMinigun        model none worldmodel none item 15123
    /// </code>
    ///
    /// **211 is the stock Medi Gun**, so the information was never missing — only unread, and every
    /// medigun on every other player went undrawn for it.
    ///
    /// **Carried rather than resolved here**, because `items_game.txt` belongs to the content layer
    /// and Core must not read it. This is the number; `WeaponModels.For` turns it into a model, and
    /// already did so for the viewmodel and the followed player.
    /// </remarks>
    public int? ItemDefinitionIndex { get; internal set; }

    /// <summary>The entity's networked class name, for the stock-weapon fallback.</summary>
    /// <remarks>
    /// **The item is the answer and this is the backstop.** `WeaponModels.For` prefers the item
    /// because that is what the player actually equipped — preferring the class would draw a stock
    /// rocket launcher for every reskin in the game — and falls back to the class only when the
    /// item names no model. A decorated weapon whose definition inherits its model through a
    /// prefab is the case that needs it.
    /// </remarks>
    public string ClassName { get; internal set; } = string.Empty;

    /// <summary>Whether this belongs to its owner's DISGUISE rather than to its owner.</summary>
    /// <remarks>
    /// `m_bDisguiseWearable` / `m_bDisguiseWeapon`. Kept current rather than set once: the server
    /// creates a disguise's gear when the disguise goes up and the flag arrives with it.
    /// </remarks>
    public bool OfDisguise { get; internal set; }

    /// <summary>Which team this entity belongs to, or null when it declares none.</summary>
    /// <remarks>
    /// **The entity's own team, not a relation to anybody.** Whether it is the RECORDER's team is a
    /// question about a moment — a player can switch sides mid-demo — so the comparison happens in
    /// <c>DemoTimeline.PropsAt</c> against the frame's recorder team rather than being baked in
    /// here.
    ///
    /// Read for the spawn walls, which the team that spawns behind them does not see
    /// (<c>C_FuncRespawnRoomVisualizer::DrawModel</c>, <c>c_func_respawnroom.cpp:47</c>).
    /// </remarks>
    public int? TeamNumber { get; internal set; }

    /// <summary>The parity counter last seen, so a change can be noticed.</summary>
    /// <remarks>
    /// Null until the entity states one. An entity that never sends the field keeps its clock from
    /// creation, which is what a prop that never restarts an animation should do.
    /// </remarks>
    internal int? LastSequenceParity { get; set; }

    /// <summary>The frame-reset toggle last seen, so a flip can be noticed.</summary>
    /// <remarks>
    /// **The restart signal for a CLIENT-side animated entity**, which is what an animated prop is:
    /// measured on `cp_fulgur`, the spawn cabinets send `m_bClientSideAnimation` 1 and no server
    /// cycle whatsoever. `C_BaseAnimating::OnDataChanged` reads this one only in that mode
    /// (`c_baseanimating.cpp:5021`) and `m_nNewSequenceParity` in either.
    /// </remarks>
    internal int? LastFrameReset { get; set; }

    /// <summary>Whether the CLIENT advances this entity's cycle — <c>m_bClientSideAnimation</c>.</summary>
    /// <remarks>
    /// **Membership in the engine's client-side animation list, which is what it decides** (B259).
    /// `C_BaseAnimating::PostDataUpdate` (`c_baseanimating.cpp:4689`) is the whole rule:
    ///
    /// <code>
    /// if ( m_bClientSideAnimation )
    /// {
    ///     SetCycle( m_flOldCycle );
    ///     AddToClientSideAnimationList();
    /// }
    /// else
    /// {
    ///     RemoveFromClientSideAnimationList();
    /// }
    /// </code>
    ///
    /// and `UpdateClientSideAnimations` then walks that list rather than the entity array. So an
    /// entity that did not ask for it takes its cycle off the wire as an ordinary interpolated
    /// value, and the client advances nothing for it at all.
    ///
    /// **It is networked**, `RecvPropInt( RECVINFO( m_bClientSideAnimation ) )` at
    /// `c_baseanimating.cpp:190`, and a real demo is full of it: 3,319 occurrences in one SourceTV
    /// recording. Kept on the track because it is a fact about the entity rather than about a
    /// moment, and read once per rebuild instead of per prop.
    /// </remarks>
    public bool ClientSideAnimated { get; internal set; }

    /// <summary>When the animation now playing was stamped as having begun, in seconds.</summary>
    /// <remarks>
    /// **`C_BaseAnimating` measures its interval from a stamp, never from the start of time** —
    /// <c>flInterval = ( curtime - m_flAnimTime )</c>, <c>c_baseanimating.cpp:5480</c>, re-stamped
    /// on every advance. This project left the equivalent at zero for every prop, so `elapsed` was
    /// the whole recording: a one-shot animation was finished before its first frame drew, and a
    /// looping one had wrapped an arbitrary number of times.
    ///
    /// Measured on `cp_fulgur`: the spawn cabinets are told <c>seq0</c> idle → <c>seq1</c> open →
    /// <c>seq2</c> close, every keyframe carrying cycle <c>0.00</c>. The owner saw them loop for
    /// ever, and after the cycle clamp was corrected, hold open for ever. One cause, two symptoms.
    /// </remarks>
    internal double AnimationStartSeconds { get; set; }

    /// <summary>The engine's serial for this occupant of the slot.</summary>
    /// <remarks>
    /// **An entity is its index AND its serial.** The index is a slot the engine reissues; the
    /// serial is what distinguishes one occupant from the next.
    /// </remarks>
    public int SerialNumber { get; }

    /// <summary>Whether an update in this slot continues this track or starts a new object.</summary>
    /// <param name="serialNumber">The serial of the entity now occupying the slot.</param>
    /// <returns><c>true</c> to keep appending to this track.</returns>
    /// <remarks>
    /// **Identity is the serial number, which is the engine's own rule** — the same one
    /// <c>EntityStateTable</c> applies to entity state.
    ///
    /// **The model path used to decide this, and it was wrong in both directions.** It could not see
    /// two consecutive rockets in one slot, which share a model, so their positions merged into one
    /// track that drew as an object teleporting. And it reported changes that never happened: a
    /// capture point calls <c>SetModel</c> on every capture (<c>team_control_point.cpp:569</c>), so
    /// changing hands ended its track and split one object into several.
    ///
    /// **No fallback, because there is nothing to fall back from.** The serial reaching here comes
    /// from an <c>EntityState</c>, and the state table has already applied the engine's create rule
    /// — a serial is compared only on an enter, and a new occupant gets a new state — so by the time
    /// a track sees it, identity is settled and the value is authoritative. An earlier draft of this
    /// took a nullable serial and treated null as "continue"; that path could never execute, which
    /// makes it dead code wearing the costume of a safety net.
    /// </remarks>
    public bool Continues(int serialNumber) => SerialNumber == serialNumber;

    /// <summary>The entity whose skeleton carries this one, when it has no place of its own.</summary>
    /// <remarks>
    /// **A bone-merged entity has no transform, by design.** A hat, a badge and a carried weapon
    /// are attached with <c>FollowEntity</c>, which sets <c>EF_BONEMERGE</c> and then zeroes local
    /// origin and angles (<c>shared/baseentity_shared.cpp:2360</c>) — the client matches the child
    /// model's bones to the parent's **by name** and uses the parent's matrices outright, so a
    /// position would never be read and is not sent.
    ///
    /// Which is why this is a property of the track rather than of the pose: it does not change
    /// tick to tick, and there is no pose to put it in. A track with an owner keeps its keyframes
    /// for the sequence and skin it does carry, and its position stays at zero because zero is
    /// literally what the engine set.
    ///
    /// Settable rather than fixed at construction: the owner arrives on a later delta than the
    /// model on some entities, and refusing the track until both have landed would lose the
    /// cosmetic for however long that takes.
    /// </remarks>
    public int? AttachedTo { get; internal set; }

    /// <summary>Whether it rides its parent's SKELETON rather than its parent's transform.</summary>
    /// <remarks>
    /// **This is the branch <c>CalcAbsolutePosition</c> takes second, and it is the only thing that
    /// separates the two ways of hanging off something** (<c>c_baseentity.cpp:4387</c>):
    ///
    /// <code>
    ///   if (!m_pMoveParent)                 { abs = local;    return; }
    ///   if ( IsEffectActive(EF_BONEMERGE) ) { MoveToAimEnt(); return; }
    ///   // otherwise concatenate the parent's transform with this entity's local one
    /// </code>
    ///
    /// **This project had only the bone-merged branch and used it for everything with a parent**,
    /// which is right for a hat and wrong for anything hung off brushwork: a `prop_dynamic`
    /// parented to a `func_door` has no skeleton to ride, so it was given origin (0,0,0) and then
    /// dropped for want of one. Measured on `cp_fulgur`, where every gate is an invisible
    /// `func_door` plus a parented grate prop and all six grates sat at the world origin (B231).
    ///
    /// False is the safe default: an entity that never said `EF_BONEMERGE` is placed by the
    /// transform path, which is what the engine does for it.
    /// </remarks>
    public bool BoneMerged { get; internal set; }

    /// <summary>Which entity owns this one, whatever it hangs from.</summary>
    /// <remarks>
    /// Kept apart from <see cref="AttachedTo"/> deliberately: that says where the prop is DRAWN and
    /// this says who it belongs to. The engine keys a carried weapon's visibility on the second.
    /// </remarks>
    public int? OwnedBy { get; internal set; }

    /// <summary>The wire's attribute inputs, or null for an entity that carries none.</summary>
    /// <remarks>
    /// **A track scalar by the B244 test — can it change while the entity stays itself?** — and it
    /// passes where the weapon state failed: an item's applied attributes are fixed at creation.
    /// Recomputed anyway on any update that touches an element-scoped property, because a rule
    /// argued from "it never changes" is one stale delta away from being B244 again.
    /// </remarks>
    public EconAttributeWire? Econ { get; internal set; }

    // **The weapon's carry state used to be a track scalar here, and that was the defect** (B244).
    // It now lives on `ScenePose`, because it is the only one of these that CHANGES while an entity
    // lives: a player switches weapons, so the same entity is active and then merely carried. A
    // scalar is written as the demo is parsed and therefore answers with the recording's LAST tick,
    // so every medic whose medigun happened to be holstered at the end drew empty-handed at every
    // tick of the demo.
    //
    // The rest of the fields on this track are scalars legitimately: a weapon belongs to one
    // player, is one item, is one class, and hangs from one parent. The test for whether a new
    // field belongs here or in the pose is exactly that — can it change without the entity ceasing
    // to be itself?

    /// <summary>Which named point on its wearer this hangs from, one-based, or null.</summary>
    /// <remarks>
    /// **Set only for the items that use an attachment rather than bone merging**, which are the
    /// ones that shared no bone name with their wearer and therefore ended up at the wearer's
    /// origin — a halo, an MvM canteen, a spellbook (RISKS B82). Settable for the same reason as
    /// <see cref="AttachedTo"/>: it can arrive on a later delta than the model.
    /// </remarks>
    public int? AttachmentPoint { get; internal set; }

    /// <summary>Which of Valve's model types this reference names.</summary>
    /// <remarks>
    /// Decided by the reference itself, which is all the string table gives. A leading asterisk is
    /// an inline BSP submodel numbered within the map — <c>*3</c> is the map's fourth. Everything
    /// else is told apart by extension, the way the engine's own loader does.
    /// </remarks>
    /// <remarks>
    /// **An econ item is always a studio model, so a weapon awaiting its item lookup is not of
    /// UNKNOWN kind.** `CEconEntity::SetModel` resolves `model_player` from `items_game.txt`
    /// (`econ_entity.cpp:1167`) and every value it can return is a `.mdl` — so the kind is known
    /// even while the path is not.
    ///
    /// **In the engine this state does not exist at all**: the client has `items_game.txt` and
    /// resolves the model when the entity is created, so a weapon is never model-less. The gap is
    /// this project's layering — Core decodes and must not read game content — and answering
    /// `Unknown` here would export that layering as a fact about the entity. It is not one.
    /// </remarks>
    public SceneModelKind Kind =>
        ModelPath.Length == 0 && ItemDefinitionIndex is not null
            ? SceneModelKind.Studio
            : Classify(ModelPath);

    /// <summary>How many moments the entity actually changed at.</summary>
    public int KeyframeCount => _keyframes.Count;

    /// <summary>Whether this track can ever answer a different pose — B259 fix 3, stage A.</summary>
    /// <remarks>
    /// **A track holds every keyframe the demo ever stated for it.** Nothing is added during
    /// playback: the whole recording is decoded before a frame is drawn, which is the design
    /// decision that lets this project seek where the engine cannot. So a track with a single
    /// keyframe answers that one pose at every tick between its first and its last, and both `At`
    /// and `Held` are a binary search returning a constant.
    ///
    /// **Measured on `tf2-2026-pub-pov-clean`: 677 of 1,165 tracks.** Fifty-eight per cent of the
    /// per-track work of every frame is re-deriving something that was decided when the demo was
    /// read — the map's crates, lights, doors and signs, which are most of what a level contains.
    ///
    /// **This is the engine's dirty flag with the polarity our architecture allows.**
    /// `CClientLeafSystem` is TOLD an entity changed, through `RenderableChanged`, because it
    /// streams and cannot know the future. We can ASK, because the future is already on disk.
    /// </remarks>
    public bool NeverChanges => _keyframes.Count <= 1;

    /// <summary>Whether a tick falls inside this track's life.</summary>
    /// <remarks>
    /// The two ends `IndexAt` already tests, exposed so a caller reusing a cached answer applies the
    /// same bounds rather than a second reading of them. Before the first keyframe an entity does
    /// not exist yet; from `End` it is gone, and a cache that ignored either would keep drawing a
    /// prop the demo had removed — which is exactly the defect the interpolation list shipped with
    /// (`selected` 566 to 850) before its lifetime guard was restored.
    /// </remarks>
    /// <param name="tick">The moment being sampled.</param>
    /// <returns>Whether the track answers a pose at that tick.</returns>
    public bool Alive(double tick) =>
        _keyframes.Count > 0 && tick < _endTick && tick >= _keyframes[0].Tick;

    /// <summary>The prop this track currently answers, kept between rebuilds; null while absent.</summary>
    /// <remarks>
    /// **The engine's entity, in the only sense that survives the translation** (B259 fix 3,
    /// stage C). `C_BaseEntity` lives across frames and is mutated in place by updates; nothing in
    /// the client re-derives an entity per frame. This is the same object identity with the
    /// mutation replaced by replacement — a fresh record is built only when a wake tick or a lerp
    /// says the answer moved, and every quiet frame serves the one already here.
    ///
    /// Null while the track answers nothing: before its first keyframe, from its `End`, and
    /// through a hidden span. Stage A's `Constant` cache was this for single-keyframe tracks; it
    /// generalises to every track and the special case folds in.
    /// </remarks>
    internal SceneProp? Live { get; set; }

    /// <summary>Whether the timeline currently holds this track in its lerp list.</summary>
    /// <remarks>
    /// **Valve's <c>m_InterpolationListEntry != 0xFFFF</c>** (`c_baseentity.h`): the entity itself
    /// carries the marker for whether it is on <c>g_InterpolationList</c>, which is what makes
    /// joining idempotent — <c>AddToInterpolationList</c> checks it and cannot double-add.
    /// </remarks>
    internal bool Lerping { get; set; }

    /// <summary>Whether the pose is mid-lerp at this tick, and when its answer next changes.</summary>
    /// <param name="tick">The moment being sampled.</param>
    /// <param name="blend">Whether this track samples through <see cref="At"/> rather than <see cref="Held"/>.</param>
    /// <returns>
    /// <c>Changing</c>: the sampled pose varies continuously right now, so it must be re-sampled
    /// every rebuild until the next wake. <c>NextWake</c>: the next tick at which the answer can
    /// change shape — the tick to re-derive at — or infinity when nothing is left to happen.
    /// </returns>
    /// <remarks>
    /// **This is <c>NoteChanged</c> computed ahead of time.** The engine is told when an update
    /// changes a variable and marks the entity for interpolation; a demo's future is on disk, so a
    /// track can name every tick where its own <see cref="At"/> changes behaviour: a keyframe
    /// arriving (the causality gate at <c>toTick &gt; tick</c> opening), a lerp window opening
    /// (`heldUntil + delay`) or closing (`keyframe + delay`), and the track's own birth and death.
    ///
    /// **The set is a deliberate SUPERSET, padded one tick past each boundary.** `At` mixes
    /// closed and open comparisons and floors its target, and deriving each edge's exact side is
    /// precisely the arithmetic that would silently drift from the sampler it describes. A wake
    /// that fires a tick early re-derives, finds nothing changed, and schedules the next
    /// candidate — a wasted lookup. A wake that fires a tick late serves a stale pose. The two
    /// costs are not comparable, so every boundary appears at its tick and one past it.
    ///
    /// **<c>Changing</c> errs the same way**: true means only "re-sample every frame", so a
    /// window judged slightly wide re-samples a constant answer, while one judged narrow freezes
    /// a moving prop. It is true from the gate opening to a tick past the window closing.
    /// </remarks>
    internal (bool Changing, double NextWake) Motion(double tick, bool blend)
    {
        if (_keyframes.Count == 0)
        {
            return (false, double.PositiveInfinity);
        }

        int born = _keyframes[0].Tick;

        if (tick < born)
        {
            return (false, born);
        }

        if (tick >= _endTick)
        {
            return (false, double.PositiveInfinity);
        }

        double next = _endTick;

        void Candidate(double at)
        {
            if (at > tick && at < next)
            {
                next = at;
            }
        }

        // The exit from the "nothing had arrived yet" branch, which serves the first pose while
        // the delayed target still sits at or before the first keyframe.
        Candidate(born + InterpolationDelayTicks);
        Candidate(born + InterpolationDelayTicks + 1);

        int index = IndexAt((int)Math.Floor(tick - InterpolationDelayTicks));

        bool changing = false;

        if (index >= 0 && index + 1 < _keyframes.Count)
        {
            int toTick = _keyframes[index + 1].Tick;
            int fromTick = _heldUntil.Count > index ? _heldUntil[index] : _keyframes[index].Tick;

            // The causality gate opening: until the destination keyframe's own tick, `At` holds
            // the earlier pose however far the delayed target has crept into the segment.
            Candidate(toTick);

            // The lerp starting (the restated tick plus the delay) and ending (the destination
            // plus the delay), each padded one past for the floor.
            Candidate(fromTick + InterpolationDelayTicks);
            Candidate(fromTick + InterpolationDelayTicks + 1);
            Candidate(toTick + InterpolationDelayTicks);
            Candidate(toTick + InterpolationDelayTicks + 1);

            // The last term is the "nothing had arrived yet" branch still being in force: while
            // the delayed target sits at or before the first keyframe, `At` serves that keyframe
            // whatever the segment is doing. `>=` and not `>`, per the superset rule above — at
            // exactly `born + delay` the lerp begins within the same tick, and judging the track
            // parked there held it one tick stale.
            changing = blend
                && tick >= toTick
                && tick < toTick + InterpolationDelayTicks + 1
                && tick >= born + InterpolationDelayTicks;
        }

        return (changing, next);
    }

    /// <summary>The moments the demo stated, in order, with nothing added.</summary>
    /// <remarks>
    /// **What the recording said, as opposed to what gets drawn.** Anything reasoning about the
    /// demo itself — a report, an export, a test asking whether a property ever arrived — wants
    /// these rather than <see cref="At(double)"/>, which interpolates.
    ///
    /// It is also the difference between a cheap question and an expensive one: asking a track
    /// about every tick of a long demo is hundreds of thousands of lookups for a few dozen stored
    /// poses.
    /// </remarks>
    public IReadOnlyList<(int Tick, ScenePose Pose)> Keyframes => _keyframes;

    /// <summary>When the value in a keyframe applied, which may precede its arrival.</summary>
    /// <param name="index">Which keyframe, indexed as <see cref="Keyframes"/> is.</param>
    /// <returns>The applied tick, or the keyframe's own when nothing said otherwise.</returns>
    /// <remarks>
    /// **Exposed so the correction can be asserted on real bytes, carried rather than recomputed**
    /// (B243). A test that re-derived this from the demo would be checking its own arithmetic
    /// twice; this is the number the interpolation used.
    /// </remarks>
    public int AppliedAt(int index) =>
        _appliedAt.Count > index ? _appliedAt[index] : _keyframes[index].Tick;

    /// <summary>When the ANIMATION in a keyframe applied, which is the engine's other clock.</summary>
    /// <param name="index">Which keyframe, indexed as <see cref="Keyframes"/> is.</param>
    /// <returns>The applied tick, or the keyframe's own when the entity sent no animation time.</returns>
    /// <remarks>
    /// Carried out for the same reason as <see cref="AppliedAt(int)"/>: so the correction can be
    /// asserted on real bytes without a test re-deriving it and checking its own arithmetic twice.
    /// </remarks>
    public int AnimationAppliedAt(int index) =>
        _animationAppliedAt.Count > index ? _animationAppliedAt[index] : _keyframes[index].Tick;

    /// <summary>The first tick this entity was seen at.</summary>
    public int FirstTick => _keyframes.Count > 0 ? _keyframes[0].Tick : int.MaxValue;

    /// <summary>Works out what a model reference names.</summary>
    /// <param name="modelPath">The reference, as <c>modelprecache</c> carried it.</param>
    /// <returns>The kind, or <see cref="SceneModelKind.Unknown"/> for anything unrecognised.</returns>
    /// <remarks>
    /// **Unknown rather than a default of Studio.** A reference this does not recognise is a fact
    /// about the corpus worth surfacing — both kinds beyond <c>.mdl</c> were found exactly this
    /// way, by something refusing to classify them. Defaulting would have hidden both.
    /// </remarks>
    public static SceneModelKind Classify(string modelPath)
    {
        if (string.IsNullOrEmpty(modelPath))
        {
            return SceneModelKind.Unknown;
        }

        // **Two spellings of one kind.** `*N` is an inline submodel and `maps/<name>.bsp` is
        // submodel zero, the world itself; both are mod_brush and the engine tells them apart by
        // which submodel they name, not by type. The world reference only started arriving here
        // once instance baselines were applied (B132) — CWorld sends its model index once, in its
        // class baseline, and never again — so this looked for a long time like a kind that did
        // not exist. What keeps the world off the screen is C_BaseEntity::ShouldDraw's
        // `index != 0`, at c_baseentity.cpp:1450, not anything about its model.
        if (modelPath.StartsWith('*') ||
            modelPath.EndsWith(".bsp", StringComparison.OrdinalIgnoreCase))
        {
            return SceneModelKind.Brush;
        }

        if (modelPath.EndsWith(".mdl", StringComparison.OrdinalIgnoreCase))
        {
            return SceneModelKind.Studio;
        }

        // **Two extensions for one kind.** ".spr" is the Quake-descended sprite format Source
        // inherited; ".vmt" is a material used as one. Both are mod_sprite to the engine, and the
        // corpus carries both - .vmt on the 2008 demo, .spr on a 2026 one, so this is not an era
        // split and neither can be treated as legacy.
        return modelPath.EndsWith(".vmt", StringComparison.OrdinalIgnoreCase) ||
               modelPath.EndsWith(".spr", StringComparison.OrdinalIgnoreCase)
            ? SceneModelKind.Sprite
            : SceneModelKind.Unknown;
    }

    /// <summary>Records a pose, if it differs from the one before it.</summary>
    /// <param name="tick">When the demo stated it — the packet's own tick.</param>
    /// <param name="pose">The pose.</param>
    /// <param name="appliedAt">When the entity says the value applied, on the same axis.</param>
    /// <remarks>
    /// **Identical means the whole pose, not just the position.** An entity animating on the spot
    /// changes every frame while standing still, and comparing only position would freeze it.
    ///
    /// **Two ticks, and which one is the LIST KEY matters more than it looks** (B273).
    /// <paramref name="tick"/> orders the list, because it is the only one that is monotonic and
    /// because a pose carries more than the interpolated quantities: visibility, render mode, skin
    /// and body all change on their own schedule and must stay in the order the demo stated them.
    /// <paramref name="appliedAt"/> is the entity's own simulation time carried onto the demo's
    /// axis, and it is what the interpolation arithmetic uses — the engine's
    /// <c>GetLastChangeTime</c>, which is what a history entry is stamped with
    /// (<c>c_baseentity.cpp:2806</c>).
    ///
    /// **Keying the list by the applied time was tried first and a corpus test caught it.** An
    /// entity that does not simulate — a prop at rest — keeps one simulation time for minutes, so
    /// every state change it made collapsed onto a single tick and an entity that was hidden and
    /// later shown was never handed back: *"hiding that never ends is deletion wearing a flag"*.
    /// </remarks>
    public void Add(int tick, ScenePose pose, int appliedAt) =>
        Add(tick, pose, appliedAt, animationAppliedAt: tick);

    /// <summary>Records a pose, with an applied time for each of the engine's two latch clocks.</summary>
    /// <param name="tick">When the demo stated it — the packet's own tick.</param>
    /// <param name="pose">The pose.</param>
    /// <param name="appliedAt">When the entity said its POSITION applied, on the demo's axis.</param>
    /// <param name="animationAppliedAt">When it said its ANIMATION applied, on the same axis.</param>
    /// <remarks>
    /// **Two clocks because the engine has two** (B274). <c>OnLatchInterpolatedVariables</c> is
    /// called once per latch group and takes <c>GetLastChangeTime( flags )</c> for each —
    /// <c>GetSimulationTime()</c> for origin and angles, <c>GetAnimTime()</c> for the cycle and the
    /// pose parameters (<c>c_baseentity.cpp:2806</c>). A server sets them at different moments, so
    /// an entity can move without re-stamping its animation and animate without moving.
    /// </remarks>
    public void Add(int tick, ScenePose pose, int appliedAt, int animationAppliedAt)
    {
        // Noticed once per keyframe rather than tested per sample: a track whose every animation
        // time is its arrival tick has nothing for the second lookup to find.
        _hasAnimationClock |= animationAppliedAt != tick;

        if (_keyframes.Count > 0 && _keyframes[^1].Pose == pose)
        {
            // **Dropped from the list but not from the record.** Collapsing a repeat saves the
            // memory it is there to save; forgetting WHEN it was last repeated throws away the only
            // evidence of when the entity started moving, and the interpolation then smears the
            // motion backwards across the whole stationary stretch.
            //
            // Measured: a shutter on cp_process holds at Z 640 and steps to 785. With the repeat
            // simply discarded, tick 100 of a 610-tick hold reported 663.771 — the straight line
            // from the first keyframe to the step. On screen that is a door drifting upward for no
            // reason for ten seconds, and then, on the way back, sinking below its own frame into
            // the floor.
            _heldUntil[^1] = appliedAt;
            _animationHeldUntil[^1] = animationAppliedAt;
            return;
        }

        _keyframes.Add((tick, pose));
        _heldUntil.Add(appliedAt);
        _appliedAt.Add(appliedAt);
        _animationHeldUntil.Add(animationAppliedAt);
        _animationAppliedAt.Add(animationAppliedAt);
    }

    /// <summary>Adds a keyframe stated and applying at the same tick.</summary>
    /// <param name="tick">When the demo stated it, and when it applied.</param>
    /// <param name="pose">The pose.</param>
    /// <remarks>
    /// For callers with no lag to account for — every test that builds a track by hand, and any
    /// entity whose recording never said when it simulated.
    /// </remarks>
    public void Add(int tick, ScenePose pose) => Add(tick, pose, tick);

    /// <summary>Records that the entity ceased to exist.</summary>
    /// <param name="tick">The first tick it was gone.</param>
    /// <remarks>
    /// Without this a picked-up health pack stays on the floor for the rest of the demo, and a
    /// rocket that hit a wall hangs there — a scene that gradually fills with rubbish, which reads
    /// as clutter rather than as a defect.
    /// </remarks>
    public void End(int tick) => _endTick = tick;

    /// <summary>The pose to draw at a moment, interpolated between keyframes.</summary>
    /// <param name="tick">The moment, which may fall between ticks.</param>
    /// <returns>The pose, or <c>null</c> when the entity did not exist then.</returns>
    /// <remarks>
    /// **Parity with the client, which never draws a stored value directly.** Valve's
    /// <c>CInterpolatedVar</c> keeps a history of value-and-changetime entries and calls
    /// <c>Interpolate()</c> for the moment being rendered. Snapping to the last keyframe instead
    /// makes a rocket jump between updates rather than fly — worst on a 33-tick server, where the
    /// updates are twice as far apart.
    ///
    /// **Three quantities, three rules, all Valve's:**
    ///
    /// - Position and scale interpolate linearly.
    /// - Angles interpolate through <em>quaternions</em>, not per component:
    ///   <c>Lerp&lt;QAngle&gt;</c> in <c>mathlib.h</c> converts both to quaternions and slerps.
    ///   That is also what makes 350° to 10° a twenty-degree turn rather than a 340° one.
    /// - The animation cycle uses <c>LoopingLerp</c> from <c>lerp_functions.h</c>: a gap of half a
    ///   cycle or more means the animation wrapped past 1.0, so the lower value is raised by one
    ///   before interpolating and the fractional part taken. Without it a looping model rewinds
    ///   through its whole animation at every loop point.
    ///
    /// **Hermite is deliberately not implemented.** The engine's default is
    /// <c>Lerp_Hermite</c>, which needs three points and the tuning that goes with it; Valve
    /// itself falls back to linear for angles ("Can't do hermite with QAngles, get
    /// discontinuities") and exposes <c>INTERPOLATE_LINEAR_ONLY</c> for the rest. Linear is
    /// therefore a shape the engine also produces, rather than an invention. Recorded as an open
    /// gap rather than quietly called parity.
    ///
    /// Nothing is extrapolated. Before the first keyframe there is nothing, and after the last
    /// the pose holds — a rocket that flew on forever after its final update would be a plausible
    /// trajectory that no part of the demo ever stated.
    ///
    /// **The interpolation is CAUSAL, and getting that wrong put doors through the floor.** A client
    /// draws <c>targettime = now - interp</c> and can only interpolate between entries it has already
    /// received; an update that has not arrived cannot pull anything toward it. This reader holds the
    /// whole demo, so without the delay it could see a keyframe seconds in the future and slide
    /// toward it the entire time — a shutter drifting open on its own for ten seconds, and sinking
    /// below its own frame on the way back (B94). Two rules restore it:
    ///
    /// - the pose is sampled <see cref="InterpolationDelayTicks"/> behind the requested tick, which
    ///   is what <c>cl_interp</c> does;
    /// - a keyframe later than the requested tick is not used, because it has not arrived yet.
    ///
    /// Together those turn a long gap into exactly what a client shows: the old pose held, then the
    /// movement rendered over the interpolation window once the update lands.
    /// </remarks>
    public ScenePose? At(double tick)
    {
        if (AtKeyframe((int)Math.Floor(tick)) is not { } earlier)
        {
            return null;
        }

        // The moment being drawn, one interpolation window behind the moment being asked for.
        double target = tick - InterpolationDelayTicks;

        if (target <= _keyframes[0].Tick)
        {
            // Nothing had been received yet, so the first stated pose is all a client would have.
            return _keyframes[0].Pose;
        }

        int index = IndexAt((int)Math.Floor(target));

        if (index < 0 || index + 1 >= _keyframes.Count)
        {
            return earlier;
        }

        (int statedTick, ScenePose from) = _keyframes[index];
        (int arrivedAt, ScenePose to) = _keyframes[index + 1];

        // **The interpolation runs on when the value APPLIED, not on when the packet arrived**
        // (B273). The engine stamps a simulation-latched variable's history entry with the entity's
        // own `GetSimulationTime()` (`OnLatchInterpolatedVariables`, `c_baseentity.cpp:2806`), and
        // for a player on a SourceTV recording that is up to four ticks from the packet's own tick,
        // on half the updates. The list is still keyed by arrival, which is what keeps the state a
        // pose carries in the order the demo stated it.
        int toTick = _appliedAt.Count > index + 1 ? _appliedAt[index + 1] : arrivedAt;

        // **A keyframe later than the tick being asked for has not arrived yet.** This is the whole
        // of the causality rule: a client at tick 100 cannot be pulled toward an update stated at
        // tick 610, and a reader holding the entire demo can. Holding the earlier pose is what the
        // client shows, and skipping this check is what walked a shutter open over ten seconds.
        if (arrivedAt > tick)
        {
            return from;
        }

        // **The movement starts where the pose was last RESTATED, not where it was first stated.**
        // A stationary entity's repeats are collapsed, so the stored keyframe can be seconds older
        // than the last moment the demo confirmed that pose — and interpolating from the older tick
        // spreads a tenth of a second of travel over the whole hold.
        int fromTick = _heldUntil.Count > index ? _heldUntil[index] : statedTick;

        if (toTick <= fromTick)
        {
            return earlier;
        }

        tick = target;

        float fraction = (float)Math.Clamp((tick - fromTick) / (toTick - fromTick), 0.0, 1.0);

        (float pitch, float yaw, float roll) = SlerpAngles(from, to, fraction);

        // Hermite when a third sample exists, which is the engine's default rather than an extra:
        // the client splines whenever there is an older entry, and falls back to linear only when
        // there is not, or when INTERPOLATE_LINEAR_ONLY is set on the variable.
        ScenePose? previous = index > 0 ? Renormalise(index, toTick - fromTick) : null;

        // **The animation-latched pair take the OTHER clock, which is a second lookup** (B274).
        // `OnLatchInterpolatedVariables` is called once per latch group and stamps each with its own
        // `GetLastChangeTime` — `GetAnimTime()` here, `GetSimulationTime()` above. Measured on the
        // 2013 SourceTV foundry recording, the two disagree by more than eight ticks on 95.5% of the
        // updates carrying both, so sharing one set of neighbours between them is not an
        // approximation of the engine, it is a different answer.
        //
        // Skipped whole for a track that never carried an animation clock, which is every player
        // and most props — `SendProxy_AnimTime` asserts a client-side-animated entity encodes none.
        (ScenePose animationFrom, ScenePose animationTo, ScenePose? animationPrevious,
            float animationFraction) = _hasAnimationClock
            ? AnimationNeighbours(target, from, to, previous, fraction)
            : (from, to, previous, fraction);

        // **A client-side-animated entity's cycle is NOT interpolated, and the engine enforces that
        // structurally rather than with a test** (B276).
        // `C_BaseAnimating::AddBaseAnimatingInterpolatedVars` (`c_baseanimating.cpp:887`):
        //
        //     int flags = LATCH_ANIMATION_VAR;
        //     if ( m_bClientSideAnimation )
        //         flags |= EXCLUDE_AUTO_INTERPOLATE;
        //     AddVar( &m_flCycle, &m_iv_flCycle, flags, true );
        //
        // and `AddVar` appends an EXCLUDE_AUTO_INTERPOLATE variable to the TAIL of the var map,
        // past `m_nInterpolatedEntries` — which is the bound `Interp_Interpolate` loops to, with
        // `Assert( !( watcher->GetType() & EXCLUDE_AUTO_INTERPOLATE ) )` inside the loop
        // (`c_baseentity.cpp:6405` and `:875`). The variable is registered and then deliberately
        // placed where the interpolator cannot reach it.
        //
        // **The client owns that cycle.** It advances it every frame in `FrameAdvance` and treats
        // what the wire says as a correction; blending two corrections produces a third value the
        // engine never held. `EntityModelSet.Simulate` already runs that advance for exactly these
        // entities, so the value this hands it must be the stated one.
        //
        // **This deviation predates the two clocks and was invisible until they arrived.** The
        // cycle used to be blended on the same pair as the position — wrong, but smooth, and the
        // advance on top dominated it. B274 gave it its own clock, its own neighbours and its own
        // fraction, which for a viewmodel is a different pair again, and the error stopped being
        // smooth.
        float cycle = ClientSideAnimated
            ? animationFrom.Cycle
            : InterpolateCycle(
                animationPrevious, animationFrom, animationTo, animationFraction);

        return new ScenePose
        {
            X = Curve(previous?.X, from.X, to.X, fraction),
            Y = Curve(previous?.Y, from.Y, to.Y, fraction),
            Z = Curve(previous?.Z, from.Z, to.Z, fraction),
            Pitch = pitch,
            Yaw = yaw,
            Roll = roll,
            // **Not interpolated, because the engine does not interpolate it** (B277). The whole
            // interpolated list is what `AddVar` registers on the client — origin, angles, eye
            // angles, velocity, view offset, punch, cycle, pose parameters, encoded controllers,
            // flex weights, lean, shift, ragdoll position, the overlay layers — and
            // `m_flModelScale` is not among them. A networked scale change SNAPS.
            //
            // The client does ramp it, by a different mechanism that no recording can trigger:
            // `SetModelScale( scale, change_duration )` (`c_baseanimating.cpp:6140`) creates a
            // `MODELSCALE` data object with a start, a goal and two times, and `UpdateModelScale`
            // lerps between them. That object exists only when game code asks for a duration;
            // receiving a value over the wire assigns the member directly. Blending here would
            // invent a ramp the demo never contained.
            Scale = from.Scale,

            // **Carried, because this rebuilds the pose field by field** (B312) — the second hop
            // these three had to survive, and the completeness test is what found it. Silent in
            // production, since the default of 1 they would fall back to is also a legitimate value.
            HeadScale = from.HeadScale,
            TorsoScale = from.TorsoScale,
            HandScale = from.HandScale,

            Sequence = from.Sequence,
            Cycle = cycle,

            // **Discrete, and blending it would be meaningless.** It is the instant an animation
            // restarted, not a quantity — a value part-way between two restarts names a moment
            // neither animation began at. Taking the earlier keyframe's is the same rule the
            // sequence itself follows two lines up.
            AnimationStartSeconds = from.AnimationStartSeconds,

            // Discrete, so it takes the earlier keyframe's value rather than being blended.
            // Half-hidden is not a state the engine has.
            Hidden = from.Hidden,

            // **Discrete for the same reason, and missing for a whole session.** m_nBody selects
            // which alternative of a body part to draw, so there is no halfway between a capture
            // point's "?" sign and its RED one — and being absent from this list meant the
            // interpolated pose carried the record's default of zero. Every capture point drew "?"
            // while the demo, the model and the packer all measured correct, because the number
            // was rebuilt without it at the last hop before the draw.
            //
            // Second time this exact shape has appeared: a record constructed field by field, one
            // field forgotten, and a default that is also a legitimate value so nothing can report
            // it. ScenePlayer.Yaw was the other.
            Body = from.Body,

            // **And the skin, which is how a team colour is carried.** TF2 paints RED and BLU as
            // two skin families of one model rather than as a tint, so losing this draws every
            // entity in family zero — the RED one — however the demo set it. Discrete like the
            // others: there is no halfway between two materials.
            //
            // Found immediately after Body, in the same list, by asking what ELSE this rebuild
            // forgets rather than waiting for the next symptom to arrive.
            Skin = from.Skin,

            // Fourth field added to this list after being forgotten from it by default — Yaw, Body
            // and Skin were the others. Carried rather than recomputed: a rate that reverted to 1
            // between keyframes would look like an animation that speeds up whenever the viewer
            // scrubs, which is a symptom nobody would trace back to here.
            PlaybackRate = from.PlaybackRate,

            // **Taken from the earlier keyframe, because a gesture is an EVENT with a start rather
            // than a value with a curve** (B282). Interpolating between two slot maps has no
            // meaning: the layer's own cycle comes from how long ago it began, which is already
            // absolute time, so the moment between two keyframes is exactly the moment the earlier
            // one describes.
            //
            // **Empty on every track today**, because a player's gestures reach the renderer
            // through `PlayerProps.Add` rather than through a keyframe. Carried anyway: the pose
            // completeness test is right that a field dropped by this rebuild is invisible in
            // production, and a future entity whose gestures DO come off the wire would lose them
            // here with nothing to say so.
            Gestures = from.Gestures,

            // **From the earlier keyframe, not interpolated between two.** The engine DOES
            // interpolate its layer array (`m_iv_AnimOverlay`), and `CheckForLayerChanges` exists
            // to stop that interpolation crossing a sequence change — so taking the earlier frame
            // is a stated approximation rather than parity, and the visible cost is a layer that
            // steps at the snapshot rate where the engine's slides (B285).
            Layers = from.Layers,

            // **From the earlier keyframe.** The engine interpolates these as ordinary networked
            // floats, so this is a stated approximation for the same reason the layers are: a
            // controller that sweeps steps at the snapshot rate here where the engine's slides.
            BoneControllers = from.BoneControllers,

            // **Discrete, and the newest arrival on this list** (B244). A weapon is in a player's
            // hands or it is not; there is no state part-way between holstered and drawn, and the
            // earlier keyframe's is what a client holds until the next update contradicts it.
            //
            // Unlike the four above, this one was not forgotten here — it was never in the pose at
            // all, and lived on the track as a scalar that answered with the end of the demo.
            WeaponState = from.WeaponState,

            // **Fifth field on this list, and added deliberately rather than after a symptom.** Yaw,
            // Body, Skin and PlaybackRate were each forgotten here first and each defaulted to a
            // legitimate value, so nothing could report the loss. Flags defaulting to null would
            // read as "the recording never said" and quietly stand every crouching player up.
            //
            // Discrete: there is no halfway between crouched and standing.
            Flags = from.Flags,

            // **Sixth, seventh and eighth on this list, and caught by the completeness test rather
            // than by a symptom** (B221). Every one of the five before them was forgotten here
            // first, and every one defaulted to a legitimate value so nothing could report the loss;
            // these three do too — 255 is opaque, 0 is `kRenderFxNone`, 0 is `kRenderNormal`. A
            // rebuild that dropped them would draw every faded entity solid and look correct on all
            // the others.
            //
            // Discrete like the rest: an entity's render mode does not interpolate between
            // keyframes, and neither does the effect driving its alpha. The alpha itself is a byte
            // the demo states, not a curve this project may invent between statements.
            RenderAlpha = from.RenderAlpha,
            RenderFx = from.RenderFx,
            RenderMode = from.RenderMode,

            // Discrete, like the render fields above: a fade band is a property of the placement,
            // and a value part-way between two settings names nothing (B268).
            FadeMinimumDistance = from.FadeMinimumDistance,
            FadeMaximumDistance = from.FadeMaximumDistance,

            // **Interpolated, because the engine puts them in the interpolation list**:
            // `AddVar( m_flPoseParameter, &m_iv_flPoseParameter, LATCH_ANIMATION_VAR, true )`
            // (`c_baseanimating.cpp:890`). A sentry's barrel would otherwise step between updates.
            PoseParameters = BlendPoses(
                animationFrom.PoseParameters, animationTo.PoseParameters, animationFraction),

            // Discrete: a counter part-way between two values names neither.
            ResetEventsParity = from.ResetEventsParity,

            Slot = from.Slot,
            AirborneSeconds = from.AirborneSeconds,
            Airwalking = from.Airwalking,
            EyePitch = from.EyePitch,
            EyeYaw = from.EyeYaw,
            AimYaw = from.AimYaw,
            WaterLevel = from.WaterLevel,
        };
    }

    /// <summary>The last pose stated at or before the sampled moment, with no blending.</summary>
    /// <param name="tick">The tick being drawn, before the interpolation delay is applied.</param>
    /// <returns>That pose, or <c>null</c> when the track has not started.</returns>
    /// <remarks>
    /// **What the engine leaves an entity that is not on `g_InterpolationList`** (B259): its
    /// variables keep whatever they last held, so it stands at its last stated position rather than
    /// being blended toward the next one. Not an extrapolation and not a guess - a pose the demo
    /// really stated.
    ///
    /// **The interpolation DELAY still applies**, which is the part that is easy to drop. `At`
    /// samples `cl_interp` behind the requested tick, so holding the pose at the raw tick instead
    /// would put an ungated entity a tenth of a second AHEAD of every gated one - and the two swap
    /// as things come in and out of view, which reads as jitter rather than as a missing blend.
    /// </remarks>
    public ScenePose? Held(double tick)
    {
        // **The lifetime guard first, and it is `At`'s own** — asked at the RAW tick, because that
        // is when the entity exists rather than when its pose is sampled. Leaving it out was a
        // regression that shipped for one measurement: the prop count went 566 to 850, because
        // tracks that had already ended came back holding their last pose for ever. An entity that
        // is gone is not an entity that stopped interpolating.
        if (AtKeyframe((int)Math.Floor(tick)) is null)
        {
            return null;
        }

        double target = tick - InterpolationDelayTicks;

        if (_keyframes.Count > 0 && target <= _keyframes[0].Tick)
        {
            return _keyframes[0].Pose;
        }

        return AtKeyframe((int)Math.Floor(target));
    }

    /// <summary>Advances the animation cycle, allowing for both wrapping and sequence changes.</summary>
    /// <remarks>
    /// **A sequence change is a cut, not a blend.** Two animations share no timeline, so a cycle
    /// of 0.9 in one and 0.1 in the next are not two points on one curve — and it is exactly the
    /// wrap rule that would otherwise fire on those unrelated numbers. The engine restarts the
    /// variable instead: <c>c_baseanimating.cpp</c> calls <c>m_iv_flCycle.Reset()</c>.
    ///
    /// The same applies one sample further back: a third point from a different animation is not
    /// a tangent, so the spline drops to the two-point form rather than curving through it.
    /// </remarks>
    private static float InterpolateCycle(
        ScenePose? previous, ScenePose from, ScenePose to, float fraction)
    {
        if (from.Sequence != to.Sequence)
        {
            return from.Cycle;
        }

        return previous is { } older && older.Sequence == from.Sequence
            ? LoopingCurve(older.Cycle, from.Cycle, to.Cycle, fraction)
            : LoopingLerp(from.Cycle, to.Cycle, fraction);
    }

    /// <summary>Rebuilds the sample before <paramref name="index"/> at an even spacing.</summary>
    /// <param name="index">Position of the keyframe the interpolation starts from.</param>
    /// <param name="span">Ticks from that keyframe to the one after it.</param>
    /// <returns>The synthetic earlier sample, or <c>null</c> when hermite does not apply.</returns>
    /// <remarks>
    /// **<c>TimeFixup_Hermite</c>, and it is not an optimisation — it is what makes the spline
    /// usable on real data.** A hermite curve assumes its three samples are evenly spaced, and a
    /// demo's are not: the server sends when it sends, and a packet arriving late leaves a gap of
    /// a different size from the one before it. Valve rebuilds the oldest sample rather than
    /// feeding the spline uneven spacing —
    ///
    /// <code>
    /// float frac = dt1 / dt2;
    /// fixup.changetime = start->changetime - dt1;
    /// fixup.value = Lerp( 1-frac, prev->value, start->value );
    /// </code>
    ///
    /// — placing a synthetic sample exactly <c>dt1</c> before the start. Skipping it does not
    /// produce a slightly different curve; it produces one that overshoots whenever the packet
    /// spacing wobbles, which on a real demo is most of the time.
    /// </remarks>
    private ScenePose? Renormalise(int index, int span)
    {
        ScenePose previous = _keyframes[index - 1].Pose;

        // **The gap between the two older samples, measured on the clock the interpolation runs
        // on** (B278). `GetInterpolationInfo` (`interpolatedvar.h:851`) makes the spline conditional
        // on it:
        //
        //     float dt2 = older_change_time - oldest_change_time;
        //     if ( dt2 > 0.0001f )
        //         pInfo->m_bHermite = true;
        //
        // so a third entry sharing a CHANGETIME with the second gives linear, not a spline.
        //
        // **This measured arrivals while everything around it had moved to applied times** — my
        // own B273, which changed what `span` means and left this behind. Two keyframes can now
        // carry one applied time, for an entity that did not re-simulate between two packets, and
        // the arrival gap was positive so the spline ran through a zero-length interval. Measured
        // on a fixture: 74.22 where the engine gives 77.5.
        int gap = AppliedAt(index) - AppliedAt(index - 1);

        if (gap <= 0 || span <= 0)
        {
            return null;
        }

        if (Math.Abs(span - gap) <= 0.0001f)
        {
            // Already evenly spaced, so the stored sample is the one the spline wants.
            return previous;
        }

        ScenePose start = _keyframes[index].Pose;
        float fraction = 1f - ((float)span / gap);

        return new ScenePose
        {
            X = float.Lerp(previous.X, start.X, fraction),
            Y = float.Lerp(previous.Y, start.Y, fraction),
            Z = float.Lerp(previous.Z, start.Z, fraction),
            Scale = float.Lerp(previous.Scale, start.Scale, fraction),

            // **Held at the previous sample rather than blended** (B312). The engine's interpolated
            // set is exactly what `AddVar` registers, and B277 enumerated it: origin, angles, eye
            // angles, velocity, view offset, punch, cycle, pose parameters, encoded controllers,
            // flex weights, viewtarget, lean, shift, IK target, the ragdoll's transform and the
            // overlay layers. These three are not among them — `BuildTransformations` reads them
            // straight off `C_TFPlayer` — so blending two values would invent a ramp no recording
            // contains, which is the same mistake B277 corrected for `m_flModelScale`.
            HeadScale = previous.HeadScale,
            TorsoScale = previous.TorsoScale,
            HandScale = previous.HandScale,

            Sequence = previous.Sequence,
            Cycle = previous.Sequence == start.Sequence
                ? LoopingLerp(previous.Cycle, start.Cycle, fraction)
                : previous.Cycle,
        };
    }

    /// <summary>Hermite through three samples, or linear when there is no third.</summary>
    /// <remarks>
    /// **<c>Lerp_Hermite</c> from <c>lerp_functions.h</c>**, transcribed:
    ///
    /// <code>
    /// T d1 = p1 - p0;
    /// T d2 = p2 - p1;
    /// output  = p1 * (2*tCube-3*tSqr+1);
    /// output += p2 * (-2*tCube+3*tSqr);
    /// output += d1 * (tCube-2*tSqr+t);
    /// output += d2 * (tCube-tSqr);
    /// </code>
    ///
    /// The tangents come from the differences either side, so a curving path bends through its
    /// updates rather than turning a corner at each one.
    /// </remarks>
    private static float Curve(float? p0, float p1, float p2, float t)
    {
        if (p0 is not { } previous)
        {
            return float.Lerp(p1, p2, t);
        }

        float d1 = p1 - previous;
        float d2 = p2 - p1;

        float tSqr = t * t;
        float tCube = t * tSqr;

        return (p1 * ((2f * tCube) - (3f * tSqr) + 1f)) +
               (p2 * ((-2f * tCube) + (3f * tSqr))) +
               (d1 * (tCube - (2f * tSqr) + t)) +
               (d2 * (tCube - tSqr));
    }

    /// <summary>Hermite for a value that wraps at 1, such as an animation cycle.</summary>
    /// <remarks>
    /// **<c>LoopingLerp_Hermite</c>**, and the second half of it is the interesting part. Raising
    /// <c>p1</c> to reach <c>p2</c> can leave it more than half a cycle from <c>p0</c>, so Valve
    /// re-checks that pair afterwards — its own comment gives the case it was written for:
    /// "important for vars that are decreasing from p0-&gt;p1-&gt;p2 where p1 is fixed up relative
    /// to p2, eg p0 = 0.2, p1 = 0.1, p2 = 0.9".
    ///
    /// Note the threshold here is <c>&gt;</c> where <c>LoopingLerp</c> uses <c>&gt;=</c>. That is
    /// Valve's, kept rather than tidied: an exactly-half-cycle step is treated as a wrap by one
    /// and not by the other, and choosing which is right is not this project's call to make.
    ///
    /// **The re-check's <c>else</c> arm is unreachable, and the arithmetic says so rather than the
    /// corpus.** It is reached only after <c>p1</c> has been raised, which puts it in
    /// <c>[1, 2)</c>; it then asks whether <c>p0 &lt; p1</c>. Two cases, and both answer yes:
    ///
    /// * <c>p0</c> was not raised, so it is still in <c>[0, 1)</c> and below <c>p1</c>;
    /// * <c>p0</c> was raised by the first pass, which happens only when <c>p0 &lt; p1</c> — and
    ///   raising both by one preserves that.
    ///
    /// A third case would need the first pass to have raised <c>p1</c>, but then <c>p1 &gt;= 1</c>
    /// and the <c>p1 &lt; p2</c> test guarding this block cannot hold against a <c>p2</c> in
    /// <c>[0, 1)</c>.
    ///
    /// Kept anyway, because this is a transcription: the arm is in Valve's own
    /// <c>LoopingLerp_Hermite</c> and deleting it would make the two harder to compare for no gain
    /// beyond a coverage line. Noted here so the gap is a recorded conclusion rather than an
    /// oversight — <c>docs/memory/an-uncoverable-gap-is-usually-your-reader.md</c> is the warning
    /// this is answering, and the answer survived it.
    /// </remarks>
    private static float LoopingCurve(float p0, float p1, float p2, float t)
    {
        if (Math.Abs(p1 - p0) > 0.5f)
        {
            if (p0 < p1)
            {
                p0 += 1f;
            }
            else
            {
                p1 += 1f;
            }
        }

        if (Math.Abs(p2 - p1) > 0.5f)
        {
            if (p1 < p2)
            {
                p1 += 1f;

                // Valve's re-check: p1 has moved, so it may now be more than half a cycle from p0.
                if (Math.Abs(p1 - p0) > 0.5f)
                {
                    if (p0 < p1)
                    {
                        p0 += 1f;
                    }
                    else
                    {
                        p1 += 1f;
                    }
                }
            }
            else
            {
                p2 += 1f;
            }
        }

        float value = Curve(p0, p1, p2, t);

        value -= (int)value;

        return value < 0f ? value + 1f : value;
    }

    /// <summary>The pose the demo actually stated at or before a tick, with nothing added.</summary>
    /// <param name="tick">The tick to ask about.</param>
    /// <returns>The pose, or <c>null</c> when the entity did not exist then.</returns>
    /// <remarks>
    /// Separate from <see cref="At(double)"/> because "what did the demo say" and "what should be
    /// drawn" are different questions and only one of them is evidence. Anything reasoning about
    /// the recording — a report, a test, an export — wants this one.
    ///
    /// Binary search rather than a scan: a viewer asks for every tracked entity on every frame, so
    /// a linear walk would be the whole cost of drawing.
    /// </remarks>
    public ScenePose? AtKeyframe(int tick)
    {
        int index = IndexAt(tick);

        return index < 0 ? null : _keyframes[index].Pose;
    }

    /// <summary>Position of the last keyframe at or before a tick, or −1 when there is none.</summary>
    /// <remarks>
    /// **The lifetime test is <see cref="Alive"/>, not a second copy of it.** This guard spelled
    /// out the empty, ended and not-yet-born cases inline, which is exactly
    /// <c>!Alive(tick)</c> — two expressions that had to agree and nothing making them.
    ///
    /// **They can drift, and a sabotage run showed what it costs.** Loosening this one alone to
    /// `tick > _endTick` did not merely misjudge the end tick: `Motion` carries its own
    /// end-of-life check, so the two disagreed about whether a track was finished, no further
    /// wake was scheduled, and the stepped sampler froze on a stale pose while a cold timeline
    /// answered correctly. The failure was in the DISAGREEMENT, not in either value.
    ///
    /// `Alive` was written for stage A's constant-track cache and orphaned by stage C, which
    /// routed everything through `At`/`Held` and therefore through this guard — it had a test and
    /// no production caller, which is `docs/memory/a-superseded-type-keeps-its-tests.md`. Making
    /// it the one definition gives it a caller and removes the pair that could disagree.
    /// </remarks>
    private int IndexAt(int tick)
    {
        if (!Alive(tick))
        {
            return -1;
        }

        int low = 0;
        int high = _keyframes.Count - 1;

        while (low < high)
        {
            // Rounded up, so the search moves towards the later keyframe and cannot stall on low.
            // Rounding down hangs rather than answering wrongly: with the two adjacent, the
            // midpoint is low, the branch assigns low to itself and the loop never ends.
            int middle = low + ((high - low + 1) / 2);

            if (_keyframes[middle].Tick <= tick)
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }

        return low;
    }

    /// <summary>The pair of keyframes the ANIMATION clock puts either side of a moment.</summary>
    /// <param name="target">The moment being drawn.</param>
    /// <param name="from">What the simulation clock chose, used when the animation clock cannot.</param>
    /// <param name="to">Its later neighbour.</param>
    /// <param name="previous">The older sample the spline wants, on the simulation clock.</param>
    /// <param name="fraction">How far between the simulation pair.</param>
    /// <returns>The animation pair, its older sample, and how far between them the moment is.</returns>
    /// <remarks>
    /// **Falls back to the simulation pair rather than to nothing**, in the two cases where the
    /// animation clock has no answer: the moment is before the first animation stamp, or past the
    /// last. Holding the caller's pair there gives the behaviour this had before the second clock
    /// existed, which is the right thing for an entity whose animation is not moving.
    /// </remarks>
    private (ScenePose From, ScenePose To, ScenePose? Previous, float Fraction) AnimationNeighbours(
        double target, ScenePose from, ScenePose to, ScenePose? previous, float fraction)
    {
        int index = AnimationIndexAt((int)Math.Floor(target));

        if (index < 0 || index + 1 >= _keyframes.Count)
        {
            return (from, to, previous, fraction);
        }

        int fromTick = _animationHeldUntil[index];
        int toTick = _animationAppliedAt[index + 1];

        if (toTick <= fromTick)
        {
            return (from, to, previous, fraction);
        }

        // **The spline's third sample needs a non-zero older interval on THIS clock too** (B278).
        // `GetInterpolationInfo` splines only when `dt2 = older_change_time - oldest_change_time`
        // exceeds 0.0001, and for an animation-latched variable those changetimes are animation
        // times. An entity that animated at one moment across two packets — which is the ordinary
        // case for anything holding a pose — would otherwise spline through nothing.
        ScenePose? older =
            index > 0 && _animationAppliedAt[index] - _animationAppliedAt[index - 1] > 0
                ? _keyframes[index - 1].Pose
                : null;

        return (
            _keyframes[index].Pose,
            _keyframes[index + 1].Pose,
            older,
            (float)Math.Clamp((target - fromTick) / (toTick - fromTick), 0.0, 1.0));
    }

    /// <summary>The last keyframe whose ANIMATION applied at or before a tick.</summary>
    /// <param name="tick">The moment being drawn.</param>
    /// <returns>Its index, or −1 when the track holds nothing yet.</returns>
    /// <remarks>
    /// **The same search over the same array, on the other clock** (B274). A server stamps
    /// animation time monotonically, so the keyframes are ordered by it as well as by arrival —
    /// which is what lets the second history be a second KEY rather than a second list.
    /// </remarks>
    private int AnimationIndexAt(int tick)
    {
        if (_animationAppliedAt.Count == 0)
        {
            return -1;
        }

        int low = 0;
        int high = _animationAppliedAt.Count - 1;

        while (low < high)
        {
            int middle = low + ((high - low + 1) / 2);

            if (_animationAppliedAt[middle] <= tick)
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }

        return low;
    }

    /// <summary>Which pose parameters of this entity's model wrap, by index.</summary>
    /// <remarks>
    /// **The model supplies these, exactly as it does in the engine.**
    /// <c>C_BaseAnimating::OnNewModel</c> (<c>c_baseanimating.cpp:1130</c>) walks the studio header
    /// and calls <c>m_iv_flPoseParameter.SetLooping( Pose.loop != 0.0f, i )</c> — so the flags live
    /// on the interpolator, set once when the model becomes known, and this is that.
    ///
    /// **Empty until somebody sets it, and empty means nothing loops**, which is the right answer
    /// for the overwhelming majority: of a sentry's two parameters only <c>aim_yaw</c> loops. This
    /// layer cannot open a model, so the scene layer sets it when it resolves one — a frame later
    /// than the entity's first appearance, which is also when the engine's history is empty.
    /// </remarks>
    public IReadOnlyList<bool> PoseParameterLoops { get; set; } = [];

    /// <summary>Interpolates each pose parameter, wrapping the ones the model says wrap.</summary>
    /// <remarks>
    /// **Returns one of the inputs when they agree, rather than allocating a copy.** Interpolation
    /// runs per sampled entity per frame, and the common case by far is two keyframes carrying the
    /// same values — a sentry that has not moved, or an entity whose parameters never change. The
    /// arrays are treated as immutable throughout, which is what makes handing one out safe.
    ///
    /// **A length mismatch takes the earlier keyframe's**, since that is the one the caller would
    /// have got with no interpolation at all. It happens when an entity's model changes under it,
    /// and blending a two-parameter model's values into a five-parameter one would pair values by
    /// position across two unrelated orderings.
    /// </remarks>
    private IReadOnlyList<float> BlendPoses(
        IReadOnlyList<float> from, IReadOnlyList<float> to, float fraction)
    {
        if (from.Count == 0 || from.Count != to.Count)
        {
            return from;
        }

        if (ReferenceEquals(from, to) || Same(from, to))
        {
            return from;
        }

        float[] blended = new float[from.Count];

        for (int index = 0; index < blended.Length; index++)
        {
            blended[index] = index < PoseParameterLoops.Count && PoseParameterLoops[index]
                ? LoopingLerp(from[index], to[index], fraction)
                : from[index] + ((to[index] - from[index]) * fraction);
        }

        return blended;
    }

    /// <summary>Whether two equal-length parameter lists hold the same values.</summary>
    /// <remarks>
    /// Compared BIT for bit, which is the question being asked: not "are these two numbers close"
    /// but "did this entity send anything different between the two keyframes". A tolerance would
    /// be wrong here — a parameter that moved by a hair still moved, and the answer decides whether
    /// an allocation is skipped rather than what any value becomes.
    /// </remarks>
    private static bool Same(IReadOnlyList<float> from, IReadOnlyList<float> to)
    {
        for (int index = 0; index < from.Count; index++)
        {
            if (BitConverter.SingleToInt32Bits(from[index])
                != BitConverter.SingleToInt32Bits(to[index]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Interpolates a normalised value that wraps, allowing for it having passed 1.</summary>
    /// <param name="from">The earlier value, 0..1.</param>
    /// <param name="to">The later one.</param>
    /// <param name="fraction">How far between them.</param>
    /// <returns>The blend, back inside 0..1.</returns>
    /// <remarks>
    /// **Valve's <c>LoopingLerp&lt;float&gt;</c>**, from <c>src/game/client/lerp_functions.h</c>:
    ///
    /// <code>
    /// if ( fabs( flTo - flFrom ) >= 0.5f )
    /// {
    ///     if (flFrom &lt; flTo) flFrom += 1.0f; else flTo += 1.0f;
    /// }
    /// float s = flTo * flPercent + flFrom * (1.0f - flPercent);
    /// s = s - (int)(s);
    /// if (s &lt; 0.0f) s = s + 1.0f;
    /// </code>
    ///
    /// The half-cycle threshold is the whole trick: a large gap means the animation looped rather
    /// than jumped, so the smaller value belongs to the next repetition.
    ///
    /// **The same function serves a looping POSE PARAMETER**, which is the other place the engine
    /// reaches for it: <c>CInterpolatedVarArray::_Interpolate</c> (<c>interpolatedvar.h:1333</c>)
    /// picks it per element from the model's <c>Pose.loop</c> flag. There the 0.5 is half the
    /// parameter's whole range rather than half a cycle — 180 degrees for a sentry's
    /// <c>aim_yaw</c>, crossed every time one tracks a target past due south.
    ///
    /// The comparison is <c>&gt;=</c>, so a gap of exactly half wraps. Both directions are the same
    /// distance there and the engine picks this one, which makes it arbitrary and worth pinning
    /// rather than tidying.
    /// </remarks>
    internal static float LoopingLerp(float from, float to, float fraction)
    {
        if (Math.Abs(to - from) >= 0.5f)
        {
            if (from < to)
            {
                from += 1f;
            }
            else
            {
                to += 1f;
            }
        }

        float value = (to * fraction) + (from * (1f - fraction));

        value -= (int)value;

        return value < 0f ? value + 1f : value;
    }

    /// <summary>Interpolates a rotation the way the engine does — through quaternions.</summary>
    /// <remarks>
    /// **Not component-wise, and Valve is explicit about why.** <c>Lerp&lt;QAngle&gt;</c> in
    /// <c>mathlib.h</c> is specialised to convert both angles to quaternions and slerp between
    /// them, and <c>Lerp_Hermite&lt;QAngle&gt;</c> refuses hermite entirely with the comment
    /// "Can't do hermite with QAngles, get discontinuities".
    ///
    /// The conversion is <c>AngleQuaternion</c> from <c>mathlib_base.cpp</c>, transcribed rather
    /// than rederived: a QAngle is (pitch, yaw, roll) about three different axes in a specific
    /// order, and a plausible-looking reconstruction produces rotations that are wrong only for
    /// some inputs.
    ///
    /// Taking the short way round falls out of this rather than being added: slerp follows the
    /// shorter arc, which is why 350° to 10° passes through zero instead of turning 340° the
    /// other way.
    /// </remarks>
    private static (float Pitch, float Yaw, float Roll) SlerpAngles(
        ScenePose from, ScenePose to, float fraction)
    {
        if (SameBits(from.Pitch, to.Pitch) &&
            SameBits(from.Yaw, to.Yaw) &&
            SameBits(from.Roll, to.Roll))
        {
            // Valve's own first line ("Avoid precision errors"), and not only a shortcut: a round
            // trip through quaternions and back perturbs the angles slightly, so a still prop
            // would jitter.
            //
            // Exact equality is deliberate and Equals says so without a suppression: the question
            // is whether the demo repeated the same stored bits, not whether two computed angles
            // are close. A tolerance here would freeze genuinely slow rotations.
            return (from.Pitch, from.Yaw, from.Roll);
        }

        Quaternion start = ToQuaternion(from.Pitch, from.Yaw, from.Roll);
        Quaternion end = ToQuaternion(to.Pitch, to.Yaw, to.Roll);

        return ToAngles(Quaternion.Slerp(start, end, fraction));
    }

    /// <summary>Whether two angles are the same stored value, bit for bit.</summary>
    /// <remarks>
    /// **A question about storage, not about magnitude**, which is why it is not a tolerance
    /// comparison and why the analyzer's usual advice does not apply (S1244). The demo either
    /// repeated a value or stated a new one; comparing bits says exactly that. A tolerance here
    /// would treat a slow rotation as stillness and freeze it.
    /// </remarks>
    private static bool SameBits(float left, float right) =>
        BitConverter.SingleToInt32Bits(left) == BitConverter.SingleToInt32Bits(right);

    /// <summary><c>AngleQuaternion</c> from <c>mathlib_base.cpp</c>.</summary>
    private static Quaternion ToQuaternion(float pitch, float yaw, float roll)
    {
        (float sinYaw, float cosYaw) = MathF.SinCos(float.DegreesToRadians(yaw) * 0.5f);
        (float sinPitch, float cosPitch) = MathF.SinCos(float.DegreesToRadians(pitch) * 0.5f);
        (float sinRoll, float cosRoll) = MathF.SinCos(float.DegreesToRadians(roll) * 0.5f);

        float sinRollCosPitch = sinRoll * cosPitch;
        float cosRollSinPitch = cosRoll * sinPitch;
        float cosRollCosPitch = cosRoll * cosPitch;
        float sinRollSinPitch = sinRoll * sinPitch;

        return new Quaternion(
            (sinRollCosPitch * cosYaw) - (cosRollSinPitch * sinYaw),
            (cosRollSinPitch * cosYaw) + (sinRollCosPitch * sinYaw),
            (cosRollCosPitch * sinYaw) - (sinRollSinPitch * cosYaw),
            (cosRollCosPitch * cosYaw) + (sinRollSinPitch * sinYaw));
    }

    /// <summary>The inverse, as <c>QuaternionAngles</c> computes it.</summary>
    /// <remarks>
    /// The engine routes this through a matrix, but the direct form is in the same function under
    /// an <c>#else</c> and is the one transcribed here. Its noted singularity near pitch ±90 is
    /// real and shared: at straight up or down, yaw and roll describe the same rotation and the
    /// split between them is arbitrary.
    /// </remarks>
    private static (float Pitch, float Yaw, float Roll) ToAngles(Quaternion q)
    {
        float m11 = (2f * q.W * q.W) + (2f * q.X * q.X) - 1f;
        float m12 = (2f * q.X * q.Y) + (2f * q.W * q.Z);
        float m13 = (2f * q.X * q.Z) - (2f * q.W * q.Y);
        float m23 = (2f * q.Y * q.Z) + (2f * q.W * q.X);
        float m33 = (2f * q.W * q.W) + (2f * q.Z * q.Z) - 1f;

        return (
            float.RadiansToDegrees(MathF.Asin(Math.Clamp(-m13, -1f, 1f))),
            float.RadiansToDegrees(MathF.Atan2(m12, m11)),
            float.RadiansToDegrees(MathF.Atan2(m23, m33)));
    }
}
