using System;
using System.Collections.Generic;
using Tf2DemoSalvage.Core.Schema;

namespace Tf2DemoSalvage.Core.Scene;

/// <summary>
/// One entity's accumulated properties, as every snapshot so far has left them.
/// </summary>
/// <remarks>
/// **The current state of the world is nowhere in the demo.** A <c>svc_PacketEntities</c> snapshot
/// carries only what changed since the last one, so "where is everybody now" exists only as the
/// sum of every update up to this tick. That is what this type holds, and it is the first place
/// in the project where a bug is invisible in the trace: the trace prints each delta correctly
/// while the accumulated view is wrong.
/// </remarks>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Naming", "CA1720:Identifier contains type name",
    Justification = "The accessors are named for the PropertyValueKind they read, the same " +
                    "reasoning already applied to PropertyValue itself. Renaming only here " +
                    "would break the correspondence with the tagged union they unwrap.")]
public sealed class EntityState
{
    /// <summary>Where TF2 sends the recording client's own position.</summary>
    /// <summary>Every networked property this decoder looks for, by the table it lives in.</summary>
    /// <remarks>
    /// **Exposed so the names can be checked against the ones Source actually sends.** A property
    /// name that no send table declares is not an error here — the lookup simply finds nothing and
    /// the value takes its default, which is a legal value for every one of these. That is the same
    /// silence that hid <c>m_nBody</c>, <c>m_nSkin</c> and the player's yaw, one layer further down:
    /// a typo in a string is indistinguishable from an entity that never sent the property.
    ///
    /// Valve declares them in the send tables — <c>SendPropInt( SENDINFO(m_nBody), …)</c> in
    /// <c>server/baseanimating.cpp:237</c> and its neighbours — so a conformance test can read the
    /// engine's list and confirm every name here appears in it.
    /// </remarks>
    internal static IReadOnlyDictionary<string, IReadOnlyList<string>> NetworkedProperties =>
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            [BaseEntityTable] =
            [
                OriginProperty, OriginZProperty, AnglesProperty, EffectsProperty,
                ModelIndexProperty, OwnerProperty, ParentProperty,
            ],
            [LocalOriginTable] = [OriginProperty, OriginZProperty, EyeAnglesPitch, EyeAnglesYaw],
            [NonLocalOriginTable] = [OriginProperty, OriginZProperty, EyeAnglesPitch, EyeAnglesYaw],
            [AnimatingTable] =
            [
                SequenceProperty, BodyProperty, PlaybackRateProperty,
                ModelScaleProperty, SkinProperty,
            ],
            [ServerAnimationTable] = [CycleProperty],
            [BasePlayerTable] = [FlagsProperty, LifeStateProperty],
            [CombatCharacterTable] = [ActiveWeaponProperty],
            [TfPlayerTable] = [WaterLevelProperty],

            // **Fog, which is the first entry here that is not about a thing you can see.** A
            // CFogController has no model and no position that matters; it exists to carry the
            // atmosphere, and it changes during a round as triggers fire. Without these the demo
            // records fog and the viewer draws none.
            [FogControllerTable] =
            [
                FogEnableProperty, FogStartProperty, FogEndProperty,
                FogColourProperty, FogMaxDensityProperty,
            ],
        };

    /// <summary>The atmosphere, networked per tick by <c>CFogController</c>.</summary>
    /// <remarks>
    /// <c>fogcontroller.cpp:78</c>. **The property names are struct PATHS, not field names**, because
    /// <c>SENDINFO_STRUCTELEM( m_fog.start )</c> sends under the expression it was given — the same
    /// trap as <c>docs/memory/wire-names-are-strings.md</c>. A decoder looking for <c>start</c>
    /// finds nothing and reports no fog, which is indistinguishable from a map that has none.
    /// </remarks>
    internal const string FogControllerTable = "DT_FogController";

    // Internal rather than private so FogControllerConformanceTests can compare each one against
    // the SENDINFO_STRUCTELEM that declares it in fogcontroller.cpp. A wire name is a claim about
    // somebody else's schema, and this project has been bitten by unchecked ones before —
    // docs/memory/wire-names-are-strings.md.
    internal const string FogEnableProperty = "m_fog.enable";
    internal const string FogStartProperty = "m_fog.start";
    internal const string FogEndProperty = "m_fog.end";
    internal const string FogColourProperty = "m_fog.colorPrimary";
    internal const string FogMaxDensityProperty = "m_fog.maxdensity";

    private const string LocalOriginTable = "DT_TFLocalPlayerExclusive";

    /// <summary>Where TF2 sends every other player's position.</summary>
    private const string NonLocalOriginTable = "DT_TFNonLocalPlayerExclusive";

    /// <summary>Where every non-player entity sends its position.</summary>
    private const string BaseEntityTable = "DT_BaseEntity";

    private const string OriginProperty = "m_vecOrigin";
    private const string OriginZProperty = "m_vecOrigin[2]";

    /// <summary>Everything that can be drawn at all carries these two.</summary>
    private const string ModelIndexProperty = "m_nModelIndex";

    private const string AnglesProperty = "m_angRotation";

    private const string EffectsProperty = "m_fEffects";

    /// <summary><c>EF_NODRAW</c> from <c>src/public/const.h</c>: "don't draw entity".</summary>
    private const int NoDraw = 0x020;

    /// <summary>Who an entity hangs off, when it is bone-merged rather than placed.</summary>
    private const string OwnerProperty = "m_hOwnerEntity";

    /// <summary>
    /// <c>EF_BONEMERGE</c> from <c>src/public/const.h</c>: "Performs bone merge on client side".
    /// Set by <c>FollowEntity</c>, which is how <c>CBaseCombatWeapon::Equip</c> attaches a weapon.
    /// </summary>
    private const int BoneMerge = 0x001;

    /// <summary>
    /// The parent handle's WIRE name, which is not its member name. <c>DT_BaseEntity</c> declares
    /// <c>m_hMoveParent</c> with <c>SENDINFO_NAME</c> (<c>server/baseentity.cpp:287</c>), so the
    /// stream carries <c>moveparent</c> and a search for the member finds nothing at all.
    /// </summary>
    private const string ParentProperty = "moveparent";

    /// <summary><c>MAX_EDICT_BITS</c> — the low bits of a handle are the entity's slot.</summary>
    private const int EdictBits = 11;

    /// <summary>
    /// <c>NUM_NETWORKED_EHANDLE_SERIAL_NUMBER_BITS</c> — the high part of a handle, which
    /// distinguishes a slot's current occupant from the one that used to be there.
    /// </summary>
    private const int SerialBits = 10;

    /// <summary>
    /// <c>INVALID_NETWORKED_EHANDLE_VALUE</c>:
    /// <c>(1 &lt;&lt; (MAX_EDICT_BITS + NUM_NETWORKED_EHANDLE_SERIAL_NUMBER_BITS)) - 1</c>. Tested
    /// against the WHOLE value, because its low eleven bits are 2047 and would otherwise read as a
    /// real slot.
    /// </summary>
    private const int InvalidHandle = (1 << (EdictBits + SerialBits)) - 1;

    /// <summary>Only things that animate carry the six below.</summary>
    private const string AnimatingTable = "DT_BaseAnimating";

    private const string SequenceProperty = "m_nSequence";

    /// <summary>Which alternative each body part shows, packed into one number.</summary>
    private const string BodyProperty = "m_nBody";
    /// <summary>How far through its animation a SERVER-animated entity is.</summary>
    /// <remarks>
    /// **Not on <c>DT_BaseAnimating</c>, which is where this looked for it and found nothing.**
    /// <c>baseanimating.cpp:223</c> puts it in a table of its own, under a comment that explains
    /// exactly who gets it:
    ///
    /// <code>
    /// // Sendtable for fields we don't want to send to clientside animating entities
    /// BEGIN_SEND_TABLE_NOBASE( CBaseAnimating, DT_ServerAnimationData )
    ///     SendPropFloat (SENDINFO(m_flCycle), ANIMATION_CYCLE_BITS, ...)
    /// END_SEND_TABLE()
    /// </code>
    ///
    /// So a door or a moving platform sends its cycle and a player never does — <c>CTFPlayer</c>
    /// calls <c>UseClientSideAnimation()</c> (<c>tf_player.cpp:949</c>) and the client advances the
    /// cycle itself. A trace agrees: 97 <c>DT_ServerAnimationData.m_flCycle</c> and no
    /// <c>DT_BaseAnimating.m_flCycle</c> at all.
    /// </remarks>
    private const string CycleProperty = "m_flCycle";

    /// <summary>The sub-table carrying what client-animated entities must not receive.</summary>
    private const string ServerAnimationTable = "DT_ServerAnimationData";
    private const string PlaybackRateProperty = "m_flPlaybackRate";
    private const string ModelScaleProperty = "m_flModelScale";

    /// <summary>Which material family the model draws with.</summary>
    /// <remarks>
    /// <c>RecvPropInt(RECVINFO(m_nSkin))</c>, <c>c_baseanimating.cpp:176</c>.
    ///
    /// **TF2 paints teams as two skin families of one model rather than as a tint**, so this is not
    /// decoration — it is which of two authored materials an entity draws with. The capture point
    /// sets it from ownership arithmetic (<c>team_control_point.cpp:569</c>): 0 for RED, 1 for BLU,
    /// 2 for unowned.
    ///
    /// Retained late. Everything downstream existed first — the renderer reads
    /// <c>prop.Pose.Skin</c>, <c>ScenePropTrack</c> copies it through its clone — so the value was
    /// structurally zero for every entity, and zero is a legitimate skin, which is why nothing
    /// reported it.
    /// </remarks>
    private const string SkinProperty = "m_nSkin";

    /// <summary>The ordinary player table, sent to everyone.</summary>
    /// <remarks>
    /// **This was <c>DT_LocalPlayerExclusive</c>, and the citation beside it was right while the
    /// claim was invented.** <c>player.cpp:8183</c> really is where <c>m_fFlags</c> is declared, and
    /// what it says there is:
    ///
    /// <code>
    /// IMPLEMENT_SERVERCLASS_ST( CBasePlayer, DT_BasePlayer )
    ///     ...
    ///     SendPropInt ( SENDINFO(m_fFlags), 0, SPROP_UNSIGNED|SPROP_CHANGES_OFTEN ),
    /// </code>
    ///
    /// No exclusivity — it is sent for every player, and marked <c>SPROP_CHANGES_OFTEN</c> because
    /// they all send it constantly. The old comment's "for the recorder alone in a POV one" was a
    /// guess written in the voice of a measurement.
    ///
    /// The cost was total silence: the qualified key never matched, <c>Flags</c> answered null for
    /// every player in every demo, and the activity state machine took its "nothing said" branch
    /// forever. Nobody crouched or jumped in the viewer, ever. A trace settles it — 119
    /// <c>DT_BasePlayer.m_fFlags</c> and not one occurrence of the name being looked for.
    ///
    /// <see cref="LifeState"/> already read from this table, with a comment saying why. Two
    /// accessors on the same entity disagreed about where a player's own state lives, which is what
    /// a constant is for.
    /// </remarks>
    private const string BasePlayerTable = "DT_BasePlayer";

    /// <summary>The engine flag word, carrying the crouch and ground bits.</summary>
    private const string FlagsProperty = "m_fFlags";

    /// <summary>Where anything that can hold a weapon says which one it is holding.</summary>
    /// <remarks>
    /// <c>IMPLEMENT_SERVERCLASS_ST(CBaseCombatCharacter, DT_BaseCombatCharacter)</c>
    /// (<c>basecombatcharacter.cpp:196</c>), not one of the exclusive tables beside it — so it
    /// arrives for every combat character in the PVS rather than for the recorder alone.
    /// </remarks>
    private const string CombatCharacterTable = "DT_BaseCombatCharacter";

    /// <summary>The handle naming the weapon currently in hand.</summary>
    private const string ActiveWeaponProperty = "m_hActiveWeapon";

    /// <summary>TF2's own player table, for what only a TF2 player sends.</summary>
    private const string TfPlayerTable = "DT_TFPlayer";

    /// <summary>How deep in water the player is.</summary>
    /// <remarks>
    /// <c>SendPropInt( SENDINFO( m_nWaterLevel ), 2, SPROP_UNSIGNED )</c>,
    /// <c>tf_player.cpp:792</c> — two bits, so four levels, and Valve documents them in a comment at
    /// <c>player.cpp:1961</c>: 0 not in water, 1 feet, 2 waist, 3 eyes.
    ///
    /// **Sent on <c>DT_TFPlayer</c> rather than the local-player table**, deliberately and with a
    /// note saying why: "This will create a race condition will the local player, but the data will
    /// be the same so.....". <c>DT_BasePlayer</c> carries its own copy for the local player alone;
    /// this is the one that arrives for everybody.
    /// </remarks>
    private const string WaterLevelProperty = "m_nWaterLevel";

    /// <summary>0 alive, 1 dying, 2 dead; see LIFE_ALIVE in const.h.</summary>
    private const string LifeStateProperty = "m_lifeState";

    private const string EyeAnglesPitch = "m_angEyeAngles[0]";
    private const string EyeAnglesYaw = "m_angEyeAngles[1]";

    private readonly Dictionary<string, long> _lastSet = [];
    private long _sequence;

    private readonly Dictionary<string, PropertyValue> _properties = new(StringComparer.Ordinal);

    internal EntityState(int entityIndex, int classId, int serialNumber, string? className)
    {
        EntityIndex = entityIndex;
        ClassId = classId;
        SerialNumber = serialNumber;
        ClassName = className;
    }

    /// <summary>Slot in the entity table.</summary>
    public int EntityIndex { get; }

    /// <summary>Networked class id.</summary>
    public int ClassId { get; }

    /// <summary>Distinguishes this occupant of the slot from the previous one.</summary>
    public int SerialNumber { get; }

    /// <summary>The class's name, or <c>null</c> when no schema resolved it.</summary>
    public string? ClassName { get; internal set; }

    /// <summary>
    /// Whether the entity is currently in the potentially-visible set.
    /// </summary>
    /// <remarks>
    /// Tracked separately from existence, because <c>Leave</c> and <c>Delete</c> are different
    /// messages meaning different things. An entity that leaves the visible set still exists and
    /// returns with a delta rather than a fresh enter, so its properties must survive — but a
    /// viewer must not draw it while it is gone.
    /// </remarks>
    public bool IsVisible { get; internal set; } = true;

    /// <summary>The atmosphere this entity describes, or null when it is not a fog controller.</summary>
    /// <remarks>
    /// **Null for every entity but one, and that is the point of asking through the state rather
    /// than by class name.** A demo may carry a controller whose <c>m_fog.enable</c> is zero — a map
    /// with fog switched off is a real case, not a missing one — so "no controller" and "a
    /// controller saying no fog" both arrive here as null and draw the same.
    ///
    /// The colour is packed as one 32-bit value, RGBA in the low bytes upward, because
    /// <c>colorPrimary</c> is a <c>color32</c> sent as <c>SendPropInt( …, 32, SPROP_UNSIGNED )</c>.
    /// </remarks>
    public SceneFog? Fog()
    {
        if (Integer($"{FogControllerTable}.{FogEnableProperty}") is not 1 ||
            Number($"{FogControllerTable}.{FogStartProperty}") is not { } start ||
            Number($"{FogControllerTable}.{FogEndProperty}") is not { } end ||
            Integer($"{FogControllerTable}.{FogColourProperty}") is not { } packed)
        {
            return null;
        }

        // **A range of zero would divide by zero in the shader's `1 / (end - start)`.** The engine
        // guards this by never authoring one; guarding it here means a malformed demo draws no fog
        // rather than a screen of NaN.
        if (end <= start)
        {
            return null;
        }

        return new SceneFog(
            start,
            end,
            ((uint)packed & 0xFF) / 255f,
            (((uint)packed >> 8) & 0xFF) / 255f,
            (((uint)packed >> 16) & 0xFF) / 255f,

            // **Absent means 1, not 0.** maxdensity caps the fog; a controller that does not send
            // it wants no cap, and defaulting to zero would switch fog off entirely while
            // reporting that it is on.
            Number($"{FogControllerTable}.{FogMaxDensityProperty}") ?? 1f);
    }

    /// <summary>Every property this entity has ever been sent, keyed <c>Table.Name</c>.</summary>
    public IReadOnlyDictionary<string, PropertyValue> Properties => _properties;

    /// <summary>Reads an integer property.</summary>
    /// <param name="key">Qualified name, e.g. <c>DT_BasePlayer.m_iHealth</c>.</param>
    /// <returns>The value, or <c>null</c> if absent or not an integer.</returns>
    public int? Integer(string key) =>
        _properties.TryGetValue(key, out PropertyValue value) &&
        value.Kind == PropertyValueKind.Int
            ? (int)value.AsInt
            : null;

    /// <summary>Reads a float property.</summary>
    /// <param name="key">Qualified name.</param>
    /// <returns>The value, or <c>null</c> if absent or not a float.</returns>
    public float? Number(string key) =>
        _properties.TryGetValue(key, out PropertyValue value) &&
        value.Kind == PropertyValueKind.Float
            ? value.AsFloat
            : null;

    /// <summary>Whether the entity should be drawn at all right now.</summary>
    /// <remarks>
    /// **A taken health pack is hidden, not destroyed, because it respawns.**
    /// <c>CTFPowerup::SetDisabled</c> calls <c>AddEffects(EF_NODRAW)</c>, and the entity carries on
    /// existing and updating in place. A viewer that ignores the flag leaves a marker on the floor
    /// at every pickup anyone took for the rest of the match.
    ///
    /// <c>EF_NODRAW</c> is <c>0x020</c> in <c>const.h</c>, and it is one bit of a field carrying a
    /// dozen unrelated flags — bone merging, dim light, no shadow. Testing the field for non-zero
    /// would hide entities for reasons that have nothing to do with visibility.
    ///
    /// The visible set matters too: <see cref="IsVisible"/> is false while an entity has left the
    /// PVS, which is a different thing from being deleted and a different thing again from being
    /// told not to draw.
    /// </remarks>
    public bool IsDrawn => IsVisible && (Effects() & NoDraw) == 0;

    /// <summary>The effect flags, from whichever table this entity declares them in.</summary>
    /// <remarks>
    /// **Two tables, because a viewmodel declares its own copy.** <c>DT_BaseViewModel</c> is
    /// <c>BEGIN_NETWORK_TABLE_NOBASE</c> and so inherits no <c>DT_BaseEntity</c> — but NOBASE means
    /// it inherits nothing, not that it can declare nothing, and
    /// <c>baseviewmodel_shared.cpp:565</c> sends <c>m_fEffects</c> at ten bits unsigned.
    ///
    /// **This was written down backwards and cost the off hand.** The comment on
    /// <see cref="ViewModelTable"/> asserted "no origin, no angles, no <c>m_fEffects</c>" — right
    /// about the first two, wrong about the third — and because the lookup was hardcoded to
    /// <c>DT_BaseEntity</c>, a viewmodel answered null, which reads as no flags and therefore as
    /// "draw it". The engine hides the spy's watch with exactly this flag:
    /// <c>CTFWeaponInvis::SetWeaponVisible</c> resolves the viewmodel and calls
    /// <c>vm->AddEffects( EF_NODRAW )</c>. See <c>ViewmodelVisibilityConformanceTests</c>.
    ///
    /// Resolved here rather than at each call site so there is one answer to "is this drawn",
    /// whatever kind of entity is asking. A class declares one of these tables, never both.
    /// </remarks>
    private int Effects() =>
        Integer($"{BaseEntityTable}.{EffectsProperty}") ??
        Integer($"{ViewModelTable}.{EffectsProperty}") ??
        0;

    /// <summary>Which model the entity is, as an index into <c>modelprecache</c>.</summary>
    /// <returns>The index, or <c>null</c> when the entity never sent one.</returns>
    /// <remarks>
    /// **The number is not the model; the string table is.** Valve's client reads exactly this
    /// property and asks <c>modelinfo</c> for the path — <c>c_baseentity.cpp:449</c>. See
    /// <see cref="ModelPrecache"/>, which also carries the unpacking early protocols need.
    ///
    /// Null rather than zero, because zero is a real index that means "no model". An entity that
    /// never sent the property is a different thing from one that sent zero, and collapsing them
    /// hides a decode that missed a property behind a value that looks deliberate.
    /// </remarks>
    public int? ModelIndex() => Integer($"{BaseEntityTable}.{ModelIndexProperty}");

    /// <summary>The table a viewmodel's properties arrive under.</summary>
    /// <remarks>
    /// **Its own table and nothing else.** <c>baseviewmodel_shared.cpp:557</c> declares it
    /// <c>BEGIN_NETWORK_TABLE_NOBASE</c>, so a viewmodel inherits no <c>DT_BaseEntity</c> — no
    /// origin, no angles, and an owner handle under a different name. Every other reader on this
    /// class looks in <c>DT_BaseEntity</c> and would answer null for a viewmodel that is perfectly
    /// well described on the wire.
    ///
    /// **It does send <c>m_fEffects</c>, and this comment used to say it did not.** NOBASE stops it
    /// inheriting a property; it does not stop the table declaring one, and line 565 declares this
    /// one. The mistake was invisible because the reader looked in <c>DT_BaseEntity</c> and got
    /// null, which means "draw it" — see <see cref="Effects"/>.
    /// </remarks>
    private const string ViewModelTable = "DT_BaseViewModel";

    /// <summary>The model a viewmodel is showing, or <c>null</c> when this is not one.</summary>
    /// <remarks>
    /// Separate from <see cref="ModelIndex"/> rather than folded into it, because the two answer
    /// different questions: that one is "where does this entity's own model index live", and
    /// merging them would make every ordinary entity search a table it does not have.
    /// </remarks>
    public int? ViewmodelModelIndex() => Integer($"{ViewModelTable}.{ModelIndexProperty}");

    /// <summary>Which player a viewmodel belongs to, or <c>null</c> when this is not one.</summary>
    /// <remarks>
    /// **<c>m_hOwner</c>, not <c>m_hOwnerEntity</c>** — a different property in a different table
    /// from the one <see cref="Attachment"/> reads, and not gated on <c>EF_BONEMERGE</c>, which a
    /// viewmodel never sets. Masked through <see cref="Slot"/> like every other handle, so an
    /// unset one is nobody rather than entity 2047.
    /// </remarks>
    public int? ViewmodelOwner() => Slot(Integer($"{ViewModelTable}.m_hOwner"));

    /// <summary>Which animation a viewmodel is playing.</summary>
    public int? ViewmodelSequence() => Integer($"{ViewModelTable}.m_nSequence");

    /// <summary>Which of a player's two viewmodels this one is.</summary>
    /// <returns>0 for the weapon in hand, 1 for the off hand, or <c>null</c> when unstated.</returns>
    /// <remarks>
    /// **A player has two of these, and without the slot they are indistinguishable.**
    /// <c>shareddefs.h:325</c> sets <c>MAX_VIEWMODELS 2</c>, and TF2 names the second one outright:
    ///
    /// <code>
    /// CBaseViewModel *CTFPlayer::GetOffHandViewModel()
    /// {
    ///     // off hand model is slot 1
    ///     return GetViewModel( 1 );
    /// }
    /// </code>
    ///
    /// Exactly two things claim it — <c>CTFWeaponInvis::Spawn</c>, the spy's watch, and
    /// <c>tf_weaponbase_grenade</c> — so a recording of a spy carries both at once and a reader
    /// with no slot shows whichever it happened to walk past last.
    ///
    /// **Two values and no more**, because <c>VIEWMODEL_INDEX_BITS</c> is 1 and the property is
    /// <c>SPROP_UNSIGNED</c> (<c>baseviewmodel_shared.h:29</c>, <c>.cpp:563</c>). Measured on the
    /// corpus: every demo back to 2007 declares it at that width, so this is not a modern field.
    /// </remarks>
    public int? ViewmodelSlot() => Integer($"{ViewModelTable}.m_nViewModelIndex");

    /// <summary>Which item in TF2's schema this entity is, when it is an econ item.</summary>
    /// <returns>The definition index, or <c>null</c> for anything that is not one.</returns>
    /// <remarks>
    /// **This is what identifies the weapon a player is holding, and it is the only thing that
    /// can.** The model a player sees in their own hands is a client-side entity the recording
    /// cannot carry (<c>econ_entity.cpp:1153</c>), the held weapon entity sends no model of its
    /// own, and most weapon scripts no longer name one. What the demo does carry is the item's
    /// index into the schema, and <c>items_game.txt</c> turns that into a model.
    ///
    /// **<c>DT_ScriptCreatedItem</c>, which is neither the weapon's table nor the player's.** An
    /// econ item is a <c>CEconItemView</c> held inside the weapon through an attribute manager, so
    /// the property arrives under the item's own table rather than under <c>DT_TFWeaponBase</c> —
    /// a lookup on the weapon's table finds nothing at all.
    ///
    /// **Present from the 2009 build onward**, measured across the corpus: the 2007 and 2008
    /// specimens declare no such property, because the item schema did not exist yet. A demo from
    /// before then answers null here, which is correct — those weapons carry their model in the
    /// weapon script instead.
    /// </remarks>
    public int? ItemDefinitionIndex() => Integer("DT_ScriptCreatedItem.m_iItemDefinitionIndex");

    /// <summary>How fast a viewmodel's animation is playing.</summary>
    /// <remarks>
    /// The third factor in Valve's cycle advance, and the one that was once decoded, retained,
    /// unit-tested and read by nothing — so every animation played at rate 1. Carried here so the
    /// viewmodel cannot repeat that.
    /// </remarks>
    public float? ViewmodelPlaybackRate() => Number($"{ViewModelTable}.m_flPlaybackRate");

    /// <summary>The entity this one hangs off, when it is bone-merged onto another.</summary>
    /// <returns>The owner's entity index, or <c>null</c> when it stands on its own.</returns>
    /// <remarks>
    /// **Bone merging, not parenting, and the difference decides whether there is a position to
    /// find.** A hat or a carried weapon is attached with <c>FollowEntity</c>, which sets
    /// <c>EF_BONEMERGE</c> (<c>0x001</c>, <c>public/const.h:284</c>) and then zeroes local origin
    /// and angles (<c>shared/baseentity_shared.cpp:2360</c>). The client matches the child model's
    /// bones to the parent's **by name** and takes the parent's matrices outright, so the child
    /// never has a transform of its own and the engine sends none.
    ///
    /// Measured on <c>cp_process</c>: all 37 live <c>CTFWearable</c> entities carry an owner, a
    /// model index and a skin, and no origin whatsoever. Looking for a position and giving up when
    /// none arrived is what left every player bare-headed.
    ///
    /// **Ownership is not attachment, and conflating them was measured wrong.** A syringe knows
    /// which medic fired it through the same <c>m_hOwnerEntity</c> a held weapon uses, and it is
    /// emphatically not merged onto him — treating an owner as an attachment claimed 220 syringe
    /// projectiles as worn items on one demo. So the owner handle is read only once something
    /// else has said this entity is attached.
    ///
    /// **What says so differs by entity, so both are read.** A <c>CTFWearable</c> sends
    /// <c>moveparent</c> and no <c>m_fEffects</c> at all; a carried <c>CTFRocketLauncher</c> sends
    /// <c>m_fEffects</c> with <c>EF_BONEMERGE</c> and no parent. Either alone covers half the
    /// problem and looks like it covers all of it, because whichever half is missing simply does
    /// not draw. The wire name for the parent is <c>moveparent</c> rather than the member name,
    /// because <c>DT_BaseEntity</c> declares it with <c>SENDINFO_NAME</c>
    /// (<c>server/baseentity.cpp:287</c>) — searching for <c>m_hMoveParent</c> finds nothing and
    /// reads as "demos do not carry this".
    ///
    /// **A handle is not an entity index.** It is an index in the low bits and a serial number
    /// above it, so that a handle to a slot which has since been reused can be told from one to
    /// its current occupant. <c>RecvProxy_IntToEHandle</c> (<c>client/recvproxy.cpp:90</c>) is
    /// exactly two lines:
    ///
    /// <code>
    /// int iEntity = pData->m_Value.m_Int &amp; ((1 &lt;&lt; MAX_EDICT_BITS) - 1);
    /// int iSerialNum = pData->m_Value.m_Int >> MAX_EDICT_BITS;
    /// </code>
    ///
    /// with the whole value compared against <c>INVALID_NETWORKED_EHANDLE_VALUE</c> FIRST — which
    /// matters, because that constant is 21 bits of ones and its low 11 bits mask to 2047, a
    /// perfectly ordinary-looking slot number. Masking before testing turns "no owner" into
    /// "owned by entity 2047".
    /// </remarks>
    public int? Attachment()
    {
        // The parent is attachment outright - an entity only has one because something set it.
        if (Slot(Integer($"{BaseEntityTable}.{ParentProperty}")) is { } parent)
        {
            return parent;
        }

        // The owner is attachment only for something that also asked to be merged.
        return ((Integer($"{BaseEntityTable}.{EffectsProperty}") ?? 0) & BoneMerge) == 0
            ? null
            : Slot(Integer($"{BaseEntityTable}.{OwnerProperty}"));
    }

    /// <summary>Which of its parent's attachment points this entity hangs from.</summary>
    /// <returns>A one-based attachment number, or <c>null</c> when it hangs from none.</returns>
    /// <remarks>
    /// **The other way an item rides a wearer, and the one that puts things on the floor when it is
    /// missing.** A hat shares bone names with the player and is bone-merged; a halo, an MvM
    /// canteen and a spellbook do not — <c>hwn_spellbook_complete.mdl</c> has one bone called
    /// <c>mvm</c> and no player skeleton has it, so nothing matches and the item falls back to the
    /// wearer's transform, at their feet (RISKS B82).
    ///
    /// **One-based, because the engine stores attachments that way.**
    /// <c>SetupBones_AttachmentHelper</c> ends with <c>PutAttachment( i + 1, world )</c>, so zero
    /// means "not attached" rather than "the first one". Returned as null for zero so a caller
    /// cannot accidentally index a real point with it — the mistake would hang every such item off
    /// a genuine but wrong place, which looks like a placement bug rather than an off-by-one.
    ///
    /// **It names a point on the PARENT, not on the item.** Measured: the spellbook declares no
    /// attachments at all, while a scout declares 29 — <c>head</c>, <c>back_upper</c>,
    /// <c>partyhat</c> and the rest. So this is an index into the wearer's table.
    ///
    /// <c>DT_BaseEntity.m_iParentAttachment</c>, 6 bits unsigned
    /// (<c>NUM_PARENTATTACHMENT_BITS</c>, <c>baseentity_shared.h:41</c>), carried by every demo in
    /// the corpus from the 2007 build onward.
    /// </remarks>
    public int? ParentAttachment() =>
        Integer($"{BaseEntityTable}.m_iParentAttachment") is { } attachment && attachment > 0
            ? attachment
            : null;

    /// <summary>Which entity is the weapon this one is holding, or null when it holds none.</summary>
    /// <returns>The weapon's entity slot.</returns>
    /// <remarks>
    /// **This decides how the whole body animates, not just what is in the hands.**
    /// <c>CTFWeaponBase::ActivityList</c> (<c>tf_weaponbase.cpp:4208</c>) selects an
    /// <c>acttable_t</c> from the weapon's role and every entry maps a bare activity to a suffixed
    /// one — <c>{ ACT_MP_RUN, ACT_MP_RUN_SECONDARY }</c>. So a medic holding a medigun runs with a
    /// different animation from a scout holding a scattergun, and drawing both with the primary
    /// suffix is wrong for a large part of the game.
    ///
    /// Through <see cref="Slot"/> like every other handle, so the invalid value is tested before
    /// the mask rather than after it.
    /// </remarks>
    public int? ActiveWeapon() =>
        Slot(Integer($"{CombatCharacterTable}.{ActiveWeaponProperty}"));

    /// <summary>How deep in water the player is: 0 dry, 1 feet, 2 waist, 3 eyes.</summary>
    /// <returns>The level, or <c>null</c> when the recording never said.</returns>
    /// <remarks>
    /// **Waist deep is where the animation changes.** Both <c>HandleJumping</c> and
    /// <c>HandleSwimming</c> test <c>GetWaterLevel() >= WL_Waist</c> — a player who leaps into water
    /// stops mid-jump and swims rather than falling with their legs tucked.
    /// </remarks>
    public int? WaterLevel() => Integer($"{TfPlayerTable}.{WaterLevelProperty}");

    /// <summary>The entity slot a networked handle names, or null when it names nothing.</summary>
    /// <param name="handle">The raw networked value, or null when the property was never sent.</param>
    /// <returns>The entity index, or <c>null</c> for the invalid handle.</returns>
    /// <remarks>
    /// **The invalid test comes BEFORE the mask, which is Valve's order and not an arrangement of
    /// convenience.** <c>RecvProxy_IntToEHandle</c> (<c>client/recvproxy.cpp:90</c>) compares the
    /// whole word against <c>INVALID_NETWORKED_EHANDLE_VALUE</c> first, and only then takes the low
    /// <c>MAX_EDICT_BITS</c>. Masking first turns the invalid handle into 2047 — a legal index that
    /// names whatever entity occupies that slot — which is how 220 syringe projectiles were claimed
    /// as worn items by their owner.
    ///
    /// Internal so the rule can be asserted directly: the order of two operations is exactly the
    /// kind of thing that reads correctly and behaves wrongly.
    /// </remarks>
    internal static int? Slot(int? handle) =>
        handle is not { } raw || raw == InvalidHandle ? null : raw & ((1 << EdictBits) - 1);

    /// <summary>The invalid networked handle, as the engine defines it.</summary>
    /// <remarks>
    /// <c>(1 &lt;&lt; (MAX_EDICT_BITS + NUM_NETWORKED_EHANDLE_SERIAL_NUMBER_BITS)) - 1</c>, which is
    /// 11 + 10 bits. Exposed so a test can state the value rather than assume −1, which is what it
    /// is NOT.
    /// </remarks>
    internal static int NoHandle => InvalidHandle;

    /// <summary>The engine constants this decoder acts on, by their names in the SDK.</summary>
    /// <remarks>
    /// **Exposed so a conformance test can read the values the code uses, not copies of them.** A
    /// test asserting <c>0x020 == 0x020</c> against <c>const.h</c> proves nothing about this class;
    /// asserting <em>this</em> dictionary does. The names are the engine's, so the test needs no
    /// translation table and a rename here fails there.
    ///
    /// Every one of them is a value whose corruption is silent: a wrong <c>EF_NODRAW</c> bit hides
    /// or shows entities, a wrong <c>EF_BONEMERGE</c> parents a weapon to nothing, and a wrong
    /// <c>MAX_EDICT_BITS</c> masks a handle to the wrong slot — which resolves to a real, existing,
    /// different entity.
    /// </remarks>
    internal static IReadOnlyDictionary<string, int> EngineConstants =>
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["EF_NODRAW"] = NoDraw,
            ["EF_BONEMERGE"] = BoneMerge,
            ["MAX_EDICT_BITS"] = EdictBits,
            ["NUM_NETWORKED_EHANDLE_SERIAL_NUMBER_BITS"] = SerialBits,
        };

    /// <summary>Which way the entity faces.</summary>
    /// <returns>Pitch, yaw and roll in degrees, or <c>null</c> when never sent.</returns>
    /// <remarks>
    /// **A QAngle is (pitch, yaw, roll)**, which is Valve's order and not the (x, y, z) the three
    /// components look like. Reading it positionally turns every prop in the map to face the wrong
    /// way — a picture that cannot be checked without already knowing the map.
    /// </remarks>
    public (float Pitch, float Yaw, float Roll)? Angles() =>
        _properties.TryGetValue($"{BaseEntityTable}.{AnglesProperty}", out PropertyValue angles) &&
        angles.Kind == PropertyValueKind.Vector
            ? angles.AsVector
            : null;

    /// <summary>Which animation the entity is playing.</summary>
    /// <returns>The sequence number, or <c>null</c> when the entity does not animate.</returns>
    /// <remarks>
    /// <c>c_baseanimating.cpp:173</c>. On <c>DT_BaseAnimating</c> rather than
    /// <c>DT_BaseEntity</c>, so only things that animate carry it — and null matters here, because
    /// sequence zero is a real animation, usually the idle one.
    /// </remarks>
    public int? AnimationSequence() => Integer($"{AnimatingTable}.{SequenceProperty}");

    /// <summary>Which alternative each of the model's body parts is showing.</summary>
    /// <returns>The packed body number, or <c>null</c> when the entity never sent one.</returns>
    /// <remarks>
    /// **One number holding a choice per body part.** A model's parts each offer alternatives — a
    /// capture point sign reading A, B or C, a player with a weapon drawn or holstered — and
    /// <c>m_nBody</c> packs one selection per part into a single integer, mixed-radix: part N's
    /// choice is <c>(body / base) % nummodels</c>, where <c>base</c> is that part's place value
    /// (<c>GetBodygroup</c>, <c>shared/animation.cpp:876</c>).
    ///
    /// Null rather than zero when absent, because zero is a real body number meaning "every part
    /// shows its first alternative" and an entity that never sent one is a different thing from an
    /// entity that sent zero — even though both draw the same, which is exactly why collapsing them
    /// would hide a decode that missed the property.
    /// </remarks>
    public int? Body() => Integer($"{AnimatingTable}.{BodyProperty}");

    /// <summary>Which material family the model draws with, when it says.</summary>
    /// <returns><c>m_nSkin</c>, or <c>null</c> when it was never sent.</returns>
    /// <remarks>
    /// **Absent means skin 0**, which is the model's first family and a perfectly ordinary value —
    /// so a caller must not treat null as "unknown, leave it alone". That ambiguity is exactly what
    /// hid this field's absence for a month: the default and the common case are the same number.
    /// </remarks>
    public int? Skin() => Integer($"{AnimatingTable}.{SkinProperty}");

    /// <summary>Whether the entity is alive, when it says.</summary>
    /// <returns><c>m_lifeState</c>, or <c>null</c> when it was never sent.</returns>
    /// <remarks>
    /// **<c>DT_BasePlayer</c>, not the local-player table**, so it is present for every player in
    /// any recording — unlike <c>m_vecVelocity</c>, which only its owner receives.
    ///
    /// Values from <c>const.h</c>: 0 alive, 1 dying, 2 dead, 3 respawnable, 4 discard body.
    ///
    /// **Absent means ALIVE**, because zero is the default and a delta-compressed format only
    /// sends what changed. Reading absence as "unknown, so do not draw" would hide every player
    /// who had not died yet.
    /// </remarks>
    public int? LifeState() => Integer($"{BasePlayerTable}.{LifeStateProperty}");

    /// <summary>The player's engine flags, when they were sent.</summary>
    /// <returns><c>m_fFlags</c>, or <c>null</c> when it was never sent.</returns>
    /// <remarks>
    /// **What a player's animation has to be computed FROM, because none of it is sent.**
    /// <c>tf_player.cpp:771</c> excludes <c>m_nSequence</c> from a player's send table, along with
    /// <c>m_flCycle</c>, <c>m_flPoseParameter</c>, <c>m_flPlaybackRate</c> and <c>m_nBody</c> — the
    /// client computes the whole lot. Measured on a real match: across 13 player entities,
    /// <c>m_nSequence</c> appears zero times while <c>m_fFlags</c> appears on all thirteen.
    ///
    /// **Which recordings carry it, and this is the part worth knowing.** The send prop lives in
    /// <c>DT_LocalPlayerExclusive</c> (<c>player.cpp:8183</c>), so a live client receives it for
    /// itself alone — and a POV demo therefore carries it only for the recorder. A SourceTV
    /// recording carries it for EVERY player, because an HLTV client is sent the full snapshot
    /// rather than a per-client filtered one; the director has to be able to cut to anybody.
    ///
    /// So a caller must handle absence rather than assume: on a POV demo this is null for every
    /// player but one, and the animation falls back to what speed alone can say.
    ///
    /// Bits from <c>const.h</c>: <c>FL_ONGROUND</c> 1, <c>FL_DUCKING</c> 2, <c>FL_ANIMDUCKING</c> 4,
    /// <c>FL_INWATER</c> 512.
    /// </remarks>
    public int? Flags() => Integer($"{BasePlayerTable}.{FlagsProperty}");

    /// <summary>How far through its animation the entity is, from 0 to 1.</summary>
    /// <returns>The cycle, or <c>null</c> when the entity does not animate.</returns>
    /// <remarks><c>c_baseanimating.cpp:152</c>.</remarks>
    public float? Cycle() => Number($"{ServerAnimationTable}.{CycleProperty}");

    /// <summary>How fast the animation runs, where 1 is its authored speed.</summary>
    /// <returns>The rate, or <c>null</c> when never sent.</returns>
    /// <remarks><c>c_baseanimating.cpp:186</c>.</remarks>
    public float? PlaybackRate() => Number($"{AnimatingTable}.{PlaybackRateProperty}");

    /// <summary>How much larger or smaller than authored the model is drawn.</summary>
    /// <returns>The scale, or <c>null</c> when never sent.</returns>
    /// <remarks>
    /// **Null rather than 1, and the caller supplies the default.** Answering zero for an absent
    /// property would draw the model at no size at all, which reads as a renderer that dropped it
    /// rather than as a property that never arrived.
    /// </remarks>
    public float? ModelScale() => Number($"{AnimatingTable}.{ModelScaleProperty}");

    /// <summary>The entity's world position, if it has sent one.</summary>
    /// <returns>The position, or <c>null</c> when no origin has arrived.</returns>
    /// <remarks>
    /// **Two tables carry this and which one is used depends on who is recording.** TF2 sends the
    /// recording client's position through <c>DT_TFLocalPlayerExclusive</c> and everyone else's
    /// through <c>DT_TFNonLocalPlayerExclusive</c>, so a reader that knows only one silently loses
    /// either the recorder or the entire rest of the server — with no error, because the other
    /// players are simply absent rather than wrong.
    ///
    /// **It is not safe to branch on recording mode, and the corpus is emphatic about it.** The
    /// obvious rule — point-of-view demos use the local table, SourceTV demos the non-local one —
    /// is false. Measured over the corpus: the 2013 SourceTV demo is 21 non-local against 2 local,
    /// while a modern demos.tf SourceTV recording is 12 local and 0 non-local. Most recordings are
    /// all-or-nothing one way or the other, and which way is not predictable from the mode. Both
    /// tables are always read.
    ///
    /// **The shape of the value changed between eras.** At protocol 11 <c>m_vecOrigin</c> is a
    /// full three-component vector. By 2013 Valve had split it into a two-component horizontal
    /// vector plus a separate scalar <c>m_vecOrigin[2]</c>, so height can delta independently of
    /// horizontal movement — a player running on flat ground then sends no Z at all. Both shapes
    /// are read, because a reader that knows only the modern one finds *no* position on a
    /// launch-era demo rather than a wrong one, which is invisible until players are counted.
    ///
    /// **Players are the special case, not the rule.** Projectiles, buildings and pickups send
    /// their position through <c>DT_BaseEntity</c>, which is checked last so a player's own
    /// exclusive tables win where both are present.
    ///
    /// Null rather than zero when absent: the world origin is a real place near the middle of
    /// every map, so returning it for "not known" invents a position and calls it data.
    /// </remarks>
    public (float X, float Y, float Z)? Origin()
    {
        // Most recently written first: see Sequence. A fixed order returns whichever table happens
        // to be listed first even when the other one was updated a thousand ticks later.
        string[] tables = [LocalOriginTable, NonLocalOriginTable, BaseEntityTable];

        System.Array.Sort(
            tables,
            (left, right) => Sequence($"{right}.{OriginProperty}")
                .CompareTo(Sequence($"{left}.{OriginProperty}")));

        foreach (string table in tables)
        {
            if (!_properties.TryGetValue($"{table}.{OriginProperty}", out PropertyValue origin))
            {
                continue;
            }

            // The launch-era shape: one three-component vector, position complete in itself.
            if (origin.Kind == PropertyValueKind.Vector)
            {
                return origin.AsVector;
            }

            // The modern shape: horizontal position here, height in its own property.
            if (origin.Kind == PropertyValueKind.VectorXY)
            {
                (float x, float y) = origin.AsVectorXY;
                return (x, y, Number($"{table}.{OriginZProperty}") ?? 0f);
            }
        }

        return null;
    }

    /// <summary>The entity's view angles, if the demo carries them for it.</summary>
    /// <returns>Pitch and yaw, or <c>null</c> when neither table sent them.</returns>
    /// <remarks>
    /// **A point-of-view demo does not contain the recorder's own eye angles.** They are not sent
    /// as entity properties to the client that already knows them, so for that one player the
    /// angles come from <c>dem_usercmd</c> and <c>democmdinfo_t</c> instead — which is where the
    /// user command work pays off for the viewer. SourceTV recordings have the opposite shape:
    /// every player is non-local, so every player's angles are here and there are no user
    /// commands at all.
    ///
    /// Pitch and yaw are sent independently, and a player who is turning without looking up or
    /// down sends only yaw. Roll is never sent for players.
    /// </remarks>
    public (float Pitch, float Yaw)? EyeAngles()
    {
        foreach (string table in (string[])[NonLocalOriginTable, LocalOriginTable])
        {
            float? pitch = Number($"{table}.{EyeAnglesPitch}");
            float? yaw = Number($"{table}.{EyeAnglesYaw}");

            if (pitch is not null || yaw is not null)
            {
                return (pitch ?? 0f, yaw ?? 0f);
            }
        }

        return null;
    }

    internal void Set(string key, PropertyValue value)
    {
        _properties[key] = value;
        _lastSet[key] = ++_sequence;
    }

    /// <summary>When a key was last written, as a monotonic counter.</summary>
    /// <remarks>
    /// **Which table spoke most recently is the answer, not which table is listed first.** TF2
    /// sends the recording client's position through one exclusive table and everyone else's
    /// through another, and an entity can hold a value in both — one of them stale. A fixed
    /// preference order then returns the stale one forever, and the player stands still while the
    /// demo plays around them.
    ///
    /// This was invisible for as long as every delta wiped the entity: only the table just written
    /// existed, so any order picked it. Fixing the wipe made the stale value permanent and froze
    /// every player on a SourceTV demo — a regression the owner saw in the viewer before the suite
    /// was re-run.
    /// </remarks>
    private long Sequence(string key) => _lastSet.TryGetValue(key, out long at) ? at : -1;
}
