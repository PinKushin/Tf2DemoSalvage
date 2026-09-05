using System;
using System.Collections.Generic;
using System.Globalization;
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

                // The model scale's pre-2013 wire name, which four of the six era specimens send
                // INSTEAD of the modern one. Listed because this decoder genuinely looks for it,
                // which is what this dictionary claims to enumerate (B271).
                LegacyModelScaleProperty,

                // Eleven bits each over nought to one, and read by `CalcBoneAdj` to bend an
                // individual bone — a sentry's barrel, a door's hinge (B288). Listed by its bare
                // name because the values arrive as an ARRAY, `m_flEncodedController.000` upward.
                EncodedControllerProperty,
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

    /// <summary>The tick offset an entity's simulation time is sent as.</summary>
    /// <remarks>
    /// Named a "time" and sent as eight bits of tick offset — see
    /// <see cref="NoteTickEncodedTimes(int)"/>, which is the only thing entitled to read it.
    /// </remarks>
    private const string SimulationTimeProperty = "m_flSimulationTime";

    /// <summary>The table an entity's animation timestamp arrives in.</summary>
    /// <remarks>
    /// Named for the ordering the engine requires of it, and it has to be spelled out because three
    /// other tables also declare an <c>m_flAnimTime</c> — <c>DT_TFPlayer</c>,
    /// <c>DT_LocalWeaponData</c> and <c>DT_LocalActiveWeaponData</c>. Confirmed against the
    /// flattened <c>CObjectSentrygun</c> on a real demo rather than chosen from the list.
    /// </remarks>
    private const string AnimTimeTable = "DT_AnimTimeMustBeFirst";

    /// <summary>The tick offset an entity's animation time is sent as.</summary>
    private const string AnimTimeProperty = "m_flAnimTime";

    /// <summary><c>MAX_EDICT_BITS</c> — the low bits of a handle are the entity's slot.</summary>
    private const int EdictBits = 11;

    /// <summary>How many low bits of a handle name the slot — <c>MAX_EDICT_BITS</c>.</summary>
    /// <remarks>
    /// Exposed so `EntityStateTable.Resolve` can read the SERIAL above them without keeping its own
    /// copy of the split. Two copies of a bit width are two chances to disagree, and a wrong one
    /// masks a handle to the wrong slot — which resolves to a real, existing, different entity.
    /// </remarks>
    internal const int EdictBitCount = EdictBits;

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

    /// <summary>What TF2 called the model scale before 2013.</summary>
    /// <remarks>
    /// Kept by the engine as a second receiver into the same member "for demo compatibility only"
    /// (<c>c_baseanimating.cpp:181</c>) — which makes it this project's business rather than
    /// legacy clutter, since half the corpus predates the rename.
    /// </remarks>
    private const string LegacyModelScaleProperty = "m_flModelWidthScale";

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

    /// <summary>Which soundscape this player is standing in, or <c>null</c> when unsent.</summary>
    /// <returns>The index into the client's soundscape list, or <c>null</c>.</returns>
    /// <remarks>
    /// **`m_audio.soundscapeIndex`, and it is deliberately player-exclusive on the wire.** The recv
    /// table is `RecvPropInt( RECVINFO( m_audio.soundscapeIndex ) )`
    /// (<c>c_baseplayer.cpp:212</c>), inside `DT_Local`, which reaches the wire through
    /// `SendPropDataTable( "localdata", 0, DT_LocalPlayerExclusive, SendProxy_SendLocalDataTable )`
    /// — and that proxy is one line, `pRecipients->SetOnly( objectID - 1 )`.
    ///
    /// So only the client who OWNS the entity is sent it. A point-of-view recording therefore
    /// carries the recorder's soundscape; a SourceTV recording owns no player and should carry
    /// nobody's. Null is the ordinary answer rather than a fault, and which demos actually carry it
    /// is measured by `SoundscapeWireProbe` rather than assumed from this reasoning (B173).
    ///
    /// **-1 is the engine's "no soundscape" and is passed through as a value.** `CEnvSoundscape`
    /// initialises `m_soundscapeIndex` to -1 (<c>soundscape.cpp:105</c>), so a player who has not
    /// entered one carries it — which is a different fact from the property never arriving, and
    /// collapsing the two would hide exactly the question this is being asked to answer.
    /// </remarks>
    public int? SoundscapeIndex() => Integer($"{LocalDataTable}.m_audio.soundscapeIndex");

    /// <summary>Where the current soundscape's positioned sounds are, one per slot.</summary>
    /// <param name="slot">0 to 7, the <c>position</c> a soundscape script names.</param>
    /// <returns>The position, or <c>null</c> when that slot was not sent.</returns>
    /// <remarks>
    /// **`NUM_AUDIO_LOCAL_SOUNDS` is 8** (<c>playernet_vars.h:16</c>), and each slot is sent as its
    /// own vector rather than as an array — eight separate `RecvPropVector` entries. A soundscape's
    /// `"position" "3"` means slot three of these, which is how one soundscape scatters its loops
    /// across a whole map: `Gorge.Inside` places seven copies of two machine hums at slots 0 to 6.
    ///
    /// `m_audio.localBits` says which slots are valid; a caller wanting that should read it rather
    /// than inferring from null, since an unsent slot and a slot deliberately at the origin are
    /// different things.
    /// </remarks>
    public (float X, float Y, float Z)? SoundscapePosition(int slot)
    {
        if (slot is < 0 or >= AudioLocalSounds)
        {
            return null;
        }

        return _properties.TryGetValue(
                $"{LocalDataTable}.m_audio.localSound[{slot}]", out PropertyValue position) &&
            position.Kind == PropertyValueKind.Vector
            ? position.AsVector
            : null;
    }

    /// <summary><c>NUM_AUDIO_LOCAL_SOUNDS</c>, <c>playernet_vars.h:16</c>.</summary>
    private const int AudioLocalSounds = 8;

    /// <summary>Which of the eight position slots carry a real value.</summary>
    /// <remarks><c>m_audio.localBits</c> — "if bits 0,1,2,3 are set then position 0,1,2,3 are valid/used".</remarks>
    public int? SoundscapePositionBits() => Integer($"{LocalDataTable}.m_audio.localBits");

    /// <summary>The <c>env_soundscape</c> that set this player's soundscape.</summary>
    /// <remarks><c>m_audio.entIndex</c> — "the entity setting the soundscape".</remarks>
    public int? SoundscapeEntity() => Integer($"{LocalDataTable}.m_audio.entIndex");

    /// <summary>The table a player's own private state arrives under.</summary>
    /// <remarks>
    /// Sent only to the client that IS this player, by `SendProxy_SendLocalDataTable`'s
    /// `SetOnly( objectID - 1 )`. Everything read through it is therefore present in a POV recording
    /// and absent from a SourceTV one.
    /// </remarks>
    private const string LocalDataTable = "DT_Local";

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

    /// <summary>What the player is watching through; see OBS_MODE_NONE in shareddefs.h.</summary>
    private const string ObserverModeProperty = "m_iObserverMode";

    /// <summary>The entity's colour and alpha, as a packed <c>color32</c>.</summary>
    private const string RenderColorProperty = "m_clrRender";

    /// <summary>Which <c>kRenderFx_*</c> effect animates the alpha.</summary>
    private const string RenderFxProperty = "m_nRenderFX";

    /// <summary>Which <c>kRender*</c> blend mode the entity draws with.</summary>
    private const string RenderModeProperty = "m_nRenderMode";

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

    /// <summary>The econ attributes this entity carries in one of its two networked lists.</summary>
    /// <param name="list">Which list — local overrides, or the networked-for-demos fallback.</param>
    /// <returns>The attributes, in element order. Empty when the entity carries none.</returns>
    /// <remarks>
    /// **Read from PATH-shaped keys, because the flat <c>Table.Prop</c> key is lossy for exactly
    /// this data** (B234). Every element of <c>m_Attributes</c> references the same
    /// <c>DT_ScriptCreatedAttribute</c>, so twenty elements share one flat name and the two lists
    /// share it too. Properties under a repeated sub-table are therefore stored under their dotted
    /// path — <c>…m_AttributeList.m_Attributes.001.m_iRawValue32</c> — and this walks them.
    ///
    /// **The length prop is honoured per group.** <c>SendPropUtlVectorDataTable</c> networks the
    /// vector's size through <c>lengthproxy.lengthprop20</c>, and the engine resizes to it before
    /// reading elements — an element at or past the length is a stale slot from before the vector
    /// shrank, and reporting it would resurrect a removed attribute.
    ///
    /// **Two value spellings, one field.** Modern demos send the float's raw bits as an int under
    /// <c>m_iRawValue32</c> (<c>SENDINFO_NAME(m_flValue, m_iRawValue32)</c>); era demos send a
    /// genuine float under <c>m_flValue</c> (*"for demo compatibility only"*,
    /// <c>econ_item_view.cpp:74</c>).
    /// </remarks>
    public IReadOnlyList<EconAttributeValue> EconAttributes(EconAttributeList list)
    {
        string marker = list == EconAttributeList.Local
            ? ".m_AttributeList.m_Attributes."
            : ".m_NetworkedDynamicAttributesForDemos.m_Attributes.";

        // Group by everything before the ordinal, so a player's own list and a carried item's list
        // — both legitimately named `m_AttributeList` — stay separate vectors with separate lengths.
        Dictionary<string, SortedDictionary<int, (int? Definition, int? Bits)>> groups = [];
        Dictionary<string, int> lengths = [];

        foreach ((string key, PropertyValue value) in _properties)
        {
            // Keys are stored without a leading dot; the marker carries one so that
            // `m_AttributeList` cannot match inside `…ForDemos`. Normalise by prefixing.
            string dotted = "." + key;

            int at = dotted.IndexOf(marker, StringComparison.Ordinal);
            if (at < 0)
            {
                continue;
            }

            string group = dotted[..at];
            string tail = dotted[(at + marker.Length)..];

            if (tail.StartsWith("lengthproxy.", StringComparison.Ordinal))
            {
                lengths[group] = (int)value.AsInt;
                continue;
            }

            int dot = tail.IndexOf('.', StringComparison.Ordinal);
            if (dot <= 0 ||
                !int.TryParse(tail[..dot], NumberStyles.None, CultureInfo.InvariantCulture,
                    out int element))
            {
                continue;
            }

            string property = tail[(dot + 1)..];

            if (!groups.TryGetValue(
                group, out SortedDictionary<int, (int? Definition, int? Bits)>? elements))
            {
                elements = [];
                groups[group] = elements;
            }

            (int? definition, int? held) = elements.TryGetValue(
                element, out (int? Definition, int? Bits) existing) ? existing : (null, null);

            if (string.Equals(property, "m_iAttributeDefinitionIndex", StringComparison.Ordinal))
            {
                definition = (int)value.AsInt;
            }
            else if (string.Equals(property, "m_iRawValue32", StringComparison.Ordinal))
            {
                held = unchecked((int)value.AsInt);
            }
            else if (string.Equals(property, "m_flValue", StringComparison.Ordinal))
            {
                // The era spelling is a genuine float; the union it fills is the same 32 bits.
                held = BitConverter.SingleToInt32Bits(value.AsFloat);
            }

            elements[element] = (definition, held);
        }

        List<EconAttributeValue> found = [];

        foreach ((string group, SortedDictionary<int, (int? Definition, int? Bits)> elements)
            in groups)
        {
            int? length = lengths.TryGetValue(group, out int declared) ? declared : null;

            foreach ((int element, (int? definition, int? bits)) in elements)
            {
                // `element >= length` is a LIFTED comparison: with no length ever declared it is
                // false and the element is kept, which is the defensive reading — a vector whose
                // size never arrived is reported whole rather than empty.
                if (element >= length || definition is not { } index || bits is not { } raw)
                {
                    continue;
                }

                found.Add(new EconAttributeValue(index, raw));
            }
        }

        return found;
    }

    /// <summary>The animation layers this entity is playing, from <c>m_AnimOverlay</c>.</summary>
    /// <returns>Its layers in <c>m_nOrder</c>, or empty.</returns>
    /// <remarks>
    /// **A player has none of these and that is not a gap** — <c>tf_player.cpp:774</c> excludes
    /// <c>overlay_vars</c> from the player's send table, so a player's layers arrive as
    /// <c>CTEPlayerAnimEvent</c> temp entities instead (B282). What DOES send them is every other
    /// animating entity: measured on <c>z1800.dem</c>, sentries carry two, three and four layers,
    /// and teleporters, dispensers, sappers and taunt props carry them too.
    ///
    /// **Read from PATH-shaped keys**, for the same reason <see cref="EconAttributes"/> is: fifteen
    /// elements share one flat name, so a value keyed by <c>Table.Prop</c> would be whichever
    /// element arrived last. The vector's own <c>lengthproxy</c> bounds it — an element at or past
    /// the length is a stale slot from before the vector shrank.
    ///
    /// **<c>m_nOrder</c> is the layer's position and its identity.** <c>AccumulateLayers</c> sorts
    /// by it and skips anything at or past <c>MAX_OVERLAYS</c>
    /// (<c>c_baseanimatingoverlay.cpp:307</c>), which is how the engine marks a slot unused — so an
    /// order of fifteen is not layer fifteen, it is no layer at all.
    /// </remarks>
    public IReadOnlyList<SceneAnimationLayer> AnimationLayers()
    {
        const string marker = ".m_AnimOverlay.";

        SortedDictionary<int, (int? Sequence, float? Cycle, float? Weight, int? Order)> slots = [];

        int? length = null;

        foreach ((string key, PropertyValue value) in _properties)
        {
            string dotted = "." + key;

            int at = dotted.IndexOf(marker, StringComparison.Ordinal);

            if (at < 0)
            {
                continue;
            }

            string tail = dotted[(at + marker.Length)..];

            if (tail.StartsWith("lengthproxy.", StringComparison.Ordinal))
            {
                length = (int)value.AsInt;
                continue;
            }

            int dot = tail.IndexOf('.', StringComparison.Ordinal);

            if (dot <= 0 ||
                !int.TryParse(
                    tail[..dot], NumberStyles.None, CultureInfo.InvariantCulture, out int element))
            {
                continue;
            }

            (int? sequence, float? cycle, float? weight, int? order) =
                slots.TryGetValue(element, out (int? Sequence, float? Cycle, float? Weight, int? Order) held)
                    ? held
                    : (null, null, null, null);

            switch (tail[(dot + 1)..])
            {
                case "m_nSequence": sequence = (int)value.AsInt; break;
                case "m_flCycle": cycle = value.AsFloat; break;
                case "m_flWeight": weight = value.AsFloat; break;
                case "m_nOrder": order = (int)value.AsInt; break;
                default: break;
            }

            slots[element] = (sequence, cycle, weight, order);
        }

        List<SceneAnimationLayer> layers = [];

        foreach ((int element, (int? sequence, float? cycle, float? weight, int? order)) in slots)
        {
            // The lifted comparison, as in EconAttributes: with no length declared this is false
            // and the element is kept, which is the defensive reading.
            if (element >= length)
            {
                continue;
            }

            // `if (m_AnimOverlay[i].m_nOrder < MAX_OVERLAYS)` — anything else is an unused slot.
            if (order is not { } position || position >= MaximumOverlays || sequence is not { } plays)
            {
                continue;
            }

            layers.Add(new SceneAnimationLayer(
                position, plays, cycle ?? 0f, weight ?? 0f));
        }

        layers.Sort((first, second) => first.Order.CompareTo(second.Order));

        return layers;
    }

    /// <summary><c>MAX_OVERLAYS</c>, <c>c_baseanimatingoverlay.h:46</c>.</summary>
    private const int MaximumOverlays = 15;

    /// <summary>The sub-table an animating entity's pose parameters arrive under.</summary>
    public const string PoseParameterTable = "m_flPoseParameter";

    /// <summary>Every pose parameter this entity sent, normalised, in the model's own order.</summary>
    /// <returns>Empty when the entity sends none, which is what a player does.</returns>
    /// <remarks>
    /// **<c>CBaseAnimating</c> networks all 24** (<c>server/baseanimating.cpp:243</c>):
    /// <c>SendPropArray3( SENDINFO_ARRAY3(m_flPoseParameter), SendPropFloat( …,
    /// ANIMATION_POSEPARAMETER_BITS, 0, 0.0f, 1.0f ) )</c> — so the wire value is NORMALISED to
    /// 0..1 and is stored that way, which is the range the blend grid wants
    /// (<c>C_BaseAnimating::GetPoseParameters</c>, <c>c_baseanimating.cpp:1401</c>).
    ///
    /// **Empty for a player, and that is the send table's doing rather than a special case here.**
    /// <c>tf_player.cpp:769</c> is <c>SendPropExclude( "DT_BaseAnimating", "m_flPoseParameter" )</c>,
    /// so a player's flattened class carries none of the elements at all and the client computes
    /// them in <c>CBasePlayerAnimState</c> instead. Returning 24 zeroes would be indistinguishable
    /// from a real entity aimed at the bottom of every range, and would override that computation.
    ///
    /// **The length follows the highest index SENT, not the number of keys present.** A delta names
    /// only what changed, so an entity can reach us having sent element 3 and not element 1 —
    /// packing the present ones would hand element 3's value to the blend grid under element 1's
    /// name. The engine's array is a fixed 24 with every unsent slot holding its last value; an
    /// unsent slot here reads as zero, which is what the engine leaves an unset parameter at
    /// (<c>c_baseanimating.cpp:1134</c>, <c>SetPoseParameter( hdr, i, 0.0 )</c> on a new model).
    /// </remarks>
    public IReadOnlyList<float> PoseParameters()
    {
        int highest = -1;

        foreach ((string key, PropertyValue value) in _properties)
        {
            if (IndexOfPoseParameter(key) is { } index && value.Kind == PropertyValueKind.Float)
            {
                highest = Math.Max(highest, index);
            }
        }

        if (highest < 0)
        {
            return [];
        }

        float[] values = new float[highest + 1];

        foreach ((string key, PropertyValue value) in _properties)
        {
            if (IndexOfPoseParameter(key) is { } index && value.Kind == PropertyValueKind.Float)
            {
                values[index] = value.AsFloat;
            }
        }

        return values;
    }

    /// <summary>Which element of the pose parameter array a property key names, if any.</summary>
    /// <remarks>
    /// Keys are the demo's own: the array is a sub-table named <c>m_flPoseParameter</c> whose
    /// children are <c>000</c> through <c>023</c>. Matched with a suffix test rather than a prefix
    /// one because the table can arrive qualified or bare depending on where it was declared, and
    /// the ordinal is the part that identifies it either way.
    /// </remarks>
    private static int? IndexOfPoseParameter(string key)
    {
        const int OrdinalLength = 3;

        if (key.Length < PoseParameterTable.Length + 1 + OrdinalLength)
        {
            return null;
        }

        int dot = key.Length - OrdinalLength - 1;

        if (key[dot] != '.' ||
            !key.AsSpan(0, dot).EndsWith(PoseParameterTable, StringComparison.Ordinal))
        {
            return null;
        }

        return int.TryParse(
            key.AsSpan(dot + 1), NumberStyles.None, CultureInfo.InvariantCulture, out int index)
            ? index
            : null;
    }

    /// <summary>The property an entity's bone controller values arrive under.</summary>
    /// <remarks>
    /// <c>SendPropArray3( SENDINFO_ARRAY3(m_flEncodedController), SendPropFloat( …, 11,
    /// SPROP_ROUNDDOWN, 0.0f, 1.0f ) )</c> (<c>baseanimating.cpp:248</c>) — eleven bits each over
    /// nought to one, so the decoded value is ALREADY the normalised input <c>CalcBoneAdj</c>
    /// wants and needs no rescaling.
    /// </remarks>
    private const string EncodedControllerProperty = "m_flEncodedController";

    /// <summary>This entity's bone controller values, normalised, by input index.</summary>
    /// <returns>One value per input the demo mentioned, or empty.</returns>
    /// <remarks>
    /// **Networked, and therefore recoverable from a demo** — which is worth saying because most of
    /// what drives a player's animation is not (see <c>tf_player.cpp:774</c>). `CalcBoneAdj`
    /// (<c>bone_setup.cpp:2462</c>) reads these to bend individual bones: a sentry's barrel, a
    /// door's hinge, anything a model author wired to a controller rather than to an animation.
    ///
    /// **Indexed by INPUT, not by controller.** A controller names which input drives it through
    /// <c>inputfield</c>, and the model's controller list is not in input order — so this returns
    /// the raw input array and the model decides which entry it reads.
    /// </remarks>
    public IReadOnlyList<float> BoneControllers()
    {
        int highest = -1;

        foreach ((string key, PropertyValue value) in _properties)
        {
            if (IndexOfController(key) is { } index && value.Kind == PropertyValueKind.Float)
            {
                highest = Math.Max(highest, index);
            }
        }

        if (highest < 0)
        {
            return [];
        }

        float[] values = new float[highest + 1];

        foreach ((string key, PropertyValue value) in _properties)
        {
            if (IndexOfController(key) is { } index && value.Kind == PropertyValueKind.Float)
            {
                values[index] = value.AsFloat;
            }
        }

        return values;
    }

    /// <summary>The input index a controller key names, or null when it is not one.</summary>
    /// <param name="key">The flat property name.</param>
    /// <returns>The index, or null.</returns>
    private static int? IndexOfController(string key)
    {
        const int OrdinalLength = 3;

        if (key.Length < EncodedControllerProperty.Length + 1 + OrdinalLength)
        {
            return null;
        }

        int dot = key.Length - OrdinalLength - 1;

        if (key[dot] != '.' ||
            !key.AsSpan(0, dot).EndsWith(EncodedControllerProperty, StringComparison.Ordinal))
        {
            return null;
        }

        return int.TryParse(
            key.AsSpan(dot + 1), NumberStyles.None, CultureInfo.InvariantCulture, out int index)
            ? index
            : null;
    }

    /// <summary>Where the offset in <c>m_flSimulationTime</c> is measured from.</summary>
    /// <param name="tick">The tick the packet carrying it was sent on.</param>
    /// <param name="entityIndex">The entity's slot, which shifts the base.</param>
    /// <returns>The tick the eight-bit offset counts up from.</returns>
    /// <remarks>
    /// **<c>CGlobalVarsBase::GetNetworkBase</c>, <c>public/globalvars_base.h:95</c>:**
    ///
    /// <code>
    /// int nEntityMod = nEntity % nTimestampRandomizeWindow;          // 32
    /// int nBaseTick  = nTimestampNetworkingBase *                    // 100
    ///                  (int)( ( nTick - nEntityMod ) / nTimestampNetworkingBase );
    /// </code>
    ///
    /// **The entity index is in there on purpose**, and Valve's comment beside the field says why:
    /// it "prevents them from getting lockstepped", so that every entity's eight-bit offset does not
    /// wrap on the same tick. A version using a plain <c>tick / 100</c> agrees for entity 0 and for
    /// every entity most of the time — it goes wrong only within <c>entindex % 32</c> ticks of a
    /// hundred-tick boundary, and then by a whole base.
    /// </remarks>
    public static int NetworkBase(int tick, int entityIndex)
    {
        const int TimestampNetworkingBase = 100;
        const int TimestampRandomizeWindow = 32;

        return TimestampNetworkingBase
            * ((tick - (entityIndex % TimestampRandomizeWindow)) / TimestampNetworkingBase);
    }

    /// <summary>Converts this packet's simulation-time offset into a tick and stores it.</summary>
    /// <param name="tick">The tick the packet carrying the value was sent on.</param>
    /// <remarks>
    /// **<c>m_flSimulationTime</c> is not a time and cannot be read as one.** It is sent as eight
    /// unsigned bits with <c>SPROP_ENCODED_AGAINST_TICKCOUNT</c>
    /// (<c>server/baseentity.cpp:265</c>), holding an offset from
    /// <see cref="NetworkBase(int, int)"/> — so its meaning depends on the tick it arrived on, and
    /// a decoder that stores the raw number has stored a number about nothing.
    ///
    /// This is <c>RecvProxy_SimulationTime</c> (<c>client/c_baseentity.cpp:344</c>):
    ///
    /// <code>
    /// t = tickbase + addt;
    /// while (t &lt; gpGlobals->tickcount - 127) t += 256;
    /// while (t > gpGlobals->tickcount + 127) t -= 256;
    /// pEntity->m_flSimulationTime = ( t * TICK_INTERVAL );
    /// </code>
    ///
    /// **The re-centring is what makes eight bits able to name a tick**, and it is not defensive:
    /// the offset wraps every 256 ticks, so without it a value sent just across a base boundary
    /// decodes 256 ticks — nearly four seconds — away from where it belongs.
    ///
    /// Stored in TICKS rather than seconds because that is what this project indexes keyframes by;
    /// the engine's final multiply by <c>TICK_INTERVAL</c> is for a caller that wants a time.
    ///
    /// **Converted AT RECEIPT and stored, never recomputed later**, which is the whole reason this
    /// is a method that writes rather than a property that reads. The offset only means something
    /// against the tick of the packet that carried it, and this decoder RETAINS properties across
    /// packets — so an offset read three packets later is decoded against the wrong base and gives
    /// a plausible tick that is up to 128 out. Measured while getting this wrong: half the values
    /// landed at each end of a ±8 clamp, which is noise wearing a distribution (B273).
    ///
    /// The engine has no equivalent hazard because <c>RecvProxy_SimulationTime</c> runs during
    /// decode and stores a time; the raw offset never survives the packet.
    /// </remarks>
    public void NoteTickEncodedTimes(int tick)
    {
        SimulatedAtTick = TickFromOffset(
            Integer($"{BaseEntityTable}.{SimulationTimeProperty}"), tick);

        AnimatedAtTick = TickFromOffset(
            Integer($"{AnimTimeTable}.{AnimTimeProperty}"), tick);

        SimulationBaseTick = tick;
    }

    /// <summary>Turns one eight-bit tick offset into the tick it names.</summary>
    /// <param name="offset">What the wire carried, or null when nothing did.</param>
    /// <param name="tick">The server tick of the packet that carried it.</param>
    /// <returns>The tick, or null when the entity said nothing.</returns>
    /// <remarks>
    /// **One routine for both, because the engine's two receive proxies are byte-identical** —
    /// <c>RecvProxy_AnimTime</c> (<c>c_baseentity.cpp:316</c>) and
    /// <c>RecvProxy_SimulationTime</c> (<c>:344</c>) differ only in which member they assign. The
    /// SEND proxies do differ, in the guard deciding whether a value is encodable at all
    /// (<c>ticknumber >= tickbase - 100</c> for animation against <c>>= tickbase</c> for
    /// simulation), and a decoder never runs those.
    ///
    /// **Null stays null and never falls back to the other clock.** A resting prop simulates
    /// without animating, and a player using client-side animation sends no meaningful animation
    /// time at all — <c>SendProxy_AnimTime</c> asserts <c>!IsUsingClientSideAnimation()</c>.
    /// Substituting one for the other would be a plausible number from the wrong source.
    /// </remarks>
    private int? TickFromOffset(int? offset, int tick)
    {
        if (offset is not { } addt)
        {
            return null;
        }

        const int Window = 256;
        const int Half = 127;

        int stamped = NetworkBase(tick, EntityIndex) + addt;

        while (stamped < tick - Half)
        {
            stamped += Window;
        }

        while (stamped > tick + Half)
        {
            stamped -= Window;
        }

        return stamped;
    }

    /// <summary>The server tick <see cref="SimulatedAtTick"/> was decoded against.</summary>
    /// <remarks>
    /// **Kept beside the answer rather than recomputed by whoever wants the difference** (B243).
    /// The lag between an entity's simulation and the packet carrying it is only meaningful against
    /// the tick that decoded it, and a caller re-deriving that tick from a demo command would get
    /// the demo's numbering rather than the server's — the exact mistake that made this histogram
    /// noise on its first run.
    /// </remarks>
    public int SimulationBaseTick { get; private set; }

    /// <summary>The tick this entity last simulated on, or null before any packet said.</summary>
    /// <remarks>
    /// **What the engine timestamps an interpolation history entry with**, for anything latched as
    /// a simulation variable — origin and angles among them.
    /// <c>C_BaseEntity::GetLastChangeTime</c> returns <c>GetSimulationTime()</c> for those, and
    /// <c>OnLatchInterpolatedVariables</c> hands it to every watcher
    /// (<c>c_baseentity.cpp:2806</c>). This project stamps keyframes with the packet tick instead;
    /// whether that diverges is what this exists to measure.
    /// </remarks>
    public int? SimulatedAtTick { get; private set; }

    /// <summary>The tick this entity's animation was last stamped at, or null.</summary>
    /// <remarks>
    /// **The other of the engine's two latch clocks.** <c>GetLastChangeTime</c> returns this for
    /// <c>LATCH_ANIMATION_VAR</c> — the pose parameters, the bone controllers, the flexes and the
    /// animation overlay layers — where <see cref="SimulatedAtTick"/> serves the simulation ones.
    /// Two clocks because a server sets them at different moments: an entity can move without
    /// re-stamping its animation, and animate without moving.
    ///
    /// Null for a player, and that is the send proxy's own rule rather than an accident: TF2's
    /// players use client-side animation and <c>SendProxy_AnimTime</c> asserts they are not
    /// encoding one.
    /// </remarks>
    public int? AnimatedAtTick { get; private set; }

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
    ///
    /// **`kRenderNone` is deliberately NOT tested here, and that is the whole of B240's second
    /// half.** It belongs to `ShouldDraw` (`c_baseentity.cpp:1447`) — but this property decides
    /// whether an entity is in the scene AT ALL, and those are different questions. Valve's
    /// `ShouldDraw` stops an entity being DRAWN; `CalcAbsolutePosition` still composes a child onto
    /// its parent's transform without asking whether the parent renders (`:4350`).
    ///
    /// Putting the test here removed the invisible `func_door`s from the scene entirely, and every
    /// setup gate's grate props are PARENTED to one — so they lost the transform they hang off and
    /// the gates vanished completely. That is the same trap `7135d319` recorded one layer down, and
    /// it was walked into one layer up within the hour.
    ///
    /// The render mode is applied where drawing is decided: `EntityModelSet.Instances`.
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

    /// <summary>The model a weapon shows in the world, or <c>null</c> when this is not a weapon.</summary>
    /// <remarks>
    /// **A carried weapon's <c>m_nModelIndex</c> is its VIEW model, not its world model**, and that
    /// is the whole of B160. The two are separate networked properties and always have been:
    ///
    /// <code>
    /// m_iViewModelIndex  = CBaseEntity::PrecacheModel( GetViewModel() );
    /// m_iWorldModelIndex = CBaseEntity::PrecacheModel( GetWorldModel() );
    ///   (basecombatweapon_shared.cpp:290-299)
    ///
    /// SendPropModelIndex( SENDINFO(m_iWorldModelIndex) ),
    ///   (basecombatweapon_shared.cpp:2870)
    /// </code>
    ///
    /// and the client draws the world one from exactly this handle —
    /// <c>modelinfo->GetModel( m_iWorldModelIndex )</c>, <c>tf_weaponbase.cpp:2144</c>.
    ///
    /// **Measured, and the symptom was nothing like the cause.** On
    /// <c>movement-test-pov-cp_process</c>, reading <c>m_nModelIndex</c> gave every one of the
    /// soldier's three weapons the same model:
    ///
    /// <code>
    /// entity 228 CTFRocketLauncher   -> models/weapons/c_models/c_soldier_arms.mdl
    /// entity 376 CTFShotgun_Soldier  -> models/weapons/c_models/c_soldier_arms.mdl
    /// entity 380 CTFShovel           -> models/weapons/c_models/c_soldier_arms.mdl
    /// </code>
    ///
    /// Three different weapons drawing one pair of first-person arms, in the world, at the owner's
    /// hand. That is the "2 sticky launchers overlapping each other" the owner reported, and it is
    /// why it happened in SourceTV recordings as well as point-of-view ones: nothing about it is a
    /// camera or a visibility rule.
    ///
    /// Null rather than falling back here — the caller decides, because "this entity is not a
    /// weapon" and "this weapon sent no world model" want different answers and merging them is how
    /// the wrong index got read in the first place.
    /// </remarks>
    public int? WorldModelIndex() => Integer($"{WeaponTable}.m_iWorldModelIndex");

    /// <summary>A weapon on the ground.</summary>
    /// <remarks><c>#define WEAPON_NOT_CARRIED 0</c>, <c>game/shared/shareddefs.h:296</c>.</remarks>
    public const int WeaponNotCarried = 0;

    /// <summary>A weapon a player is carrying but not holding.</summary>
    /// <remarks><c>#define WEAPON_IS_CARRIED_BY_PLAYER 1</c>, <c>shareddefs.h:297</c>.</remarks>
    public const int WeaponCarried = 1;

    /// <summary>The weapon a player currently has out.</summary>
    /// <remarks>
    /// <c>#define WEAPON_IS_ACTIVE 2</c> — *"This client is carrying this weapon and it's the
    /// currently held weapon"*, <c>shareddefs.h:298</c>.
    /// </remarks>
    public const int WeaponActive = 2;

    /// <summary>Whether a weapon is on the ground, carried, or held — or null if not a weapon.</summary>
    /// <remarks>
    /// **The one property the engine's visibility rule turns on for everybody but you.**
    /// <c>C_BaseCombatWeapon::ShouldDraw</c> (<c>c_basecombatweapon.cpp:399</c>) reduces, for a
    /// weapon owned by another player, to a single line:
    ///
    /// <code>
    /// if ( pOwner->IsPlayer() )
    /// {
    ///     // Show it if it's active...
    ///     return bIsActive;
    /// }
    /// </code>
    ///
    /// where <c>bIsActive = ( m_iState == WEAPON_IS_ACTIVE )</c>. So a player's other two weapons
    /// are carried and not drawn, and without this every player wears all three at once in the same
    /// hand.
    ///
    /// **Sent on the same table and the very next line to the world model index we already read** —
    /// <c>SendPropInt( SENDINFO(m_iState), 8, SPROP_UNSIGNED )</c>,
    /// <c>basecombatweapon_shared.cpp:2871</c>, against <c>m_iWorldModelIndex</c> at 2870. It was
    /// simply never decoded.
    ///
    /// Null rather than a default, because "not a weapon" and "a weapon that sent no state" are
    /// different answers and the caller decides — the same reason
    /// <see cref="WorldModelIndex"/> refuses to fall back.
    /// </remarks>
    public int? WeaponState() => Integer($"{WeaponTable}.m_iState");

    /// <summary>The table a weapon's own properties arrive under.</summary>
    private const string WeaponTable = "DT_BaseCombatWeapon";

    /// <summary>Where a TF cosmetic declares whether it belongs to a disguise.</summary>
    /// <remarks>
    /// `m_bDisguiseWearable` is on `CTFWearable`'s own table, not on the econ base — a disguise is
    /// a TF concept and the econ layer knows nothing about it.
    /// </remarks>
    private const string WearableTable = "DT_TFWearable";

    /// <summary>Where a TF weapon declares the same.</summary>
    private const string TfWeaponTable = "DT_TFWeaponBase";

    /// <summary>Which player a viewmodel belongs to, or <c>null</c> when this is not one.</summary>
    /// <remarks>
    /// **<c>m_hOwner</c>, not <c>m_hOwnerEntity</c>** — a different property in a different table
    /// from the one <see cref="Attachment"/> reads, and not gated on <c>EF_BONEMERGE</c>, which a
    /// viewmodel never sets. Masked through <see cref="Slot"/> like every other handle, so an
    /// unset one is nobody rather than entity 2047.
    /// </remarks>
    public int? ViewmodelOwner() => Slot(Integer($"{ViewModelTable}.m_hOwner"));

    /// <summary>Which weapon entity this viewmodel is showing — <c>m_hWeapon</c>.</summary>
    /// <returns>The weapon's entity index, or <c>null</c> when it names none.</returns>
    /// <remarks>
    /// **The viewmodel says which weapon it is, and this project was asking the PLAYER instead**
    /// (B222). `DT_BaseViewModel` networks `SendPropEHandle( SENDINFO( m_hWeapon ) )`
    /// (`baseviewmodel_shared.cpp:567`) — the engine's own answer, sent per viewmodel, for exactly
    /// the question "what is in this hand".
    ///
    /// What replaced it was a reconstruction: read the PLAYER's `m_hActiveWeapon`, take that
    /// entity's item definition index, and look the model up in `items_game.txt`. Three hops and a
    /// schema, to arrive at something the demo states outright — and each hop can fail on its own.
    /// Valve never does this: `C_BaseViewModel::m_hWeapon` is set when the weapon is drawn and the
    /// attachment model is built from it.
    ///
    /// **The weapon entity's own model index is the VIEW model**, which this decoder already knows
    /// and records at <see cref="ModelIndexProperty"/>'s remarks — so the pair
    /// <c>m_hWeapon</c> → that entity's <c>m_nModelIndex</c> resolves the `c_` model with no schema
    /// lookup at all.
    /// </remarks>
    public int? ViewmodelWeapon() => Slot(Integer($"{ViewModelTable}.m_hWeapon"));

    /// <summary>Which animation a viewmodel is playing.</summary>
    public int? ViewmodelSequence() => Integer($"{ViewModelTable}.m_nSequence");

    /// <summary>The counter that says an animation restarted — <c>m_nAnimationParity</c>.</summary>
    /// <remarks>
    /// **The sequence number cannot say "play that again", so the engine flips this instead.**
    /// <c>SendViewModelMatchingSequence</c> (<c>baseviewmodel_shared.cpp:363</c>) bumps it every time
    /// the server hands the viewmodel an animation, including the one already playing, and
    /// <c>C_BaseViewModel::UpdateAnimationParity</c> (<c>c_baseviewmodel.cpp:467</c>) restarts the
    /// cycle on any difference. See <see cref="ViewmodelAnimation.RestartAt"/>.
    /// </remarks>
    public int? ViewmodelAnimationParity() =>
        Integer($"{ViewModelTable}.m_nAnimationParity");

    /// <summary>The counter that says a sequence changed — <c>m_nNewSequenceParity</c>.</summary>
    /// <remarks>
    /// **<c>DT_BaseAnimating</c>'s equivalent, and it is not the same field as
    /// <c>m_nAnimationParity</c>.** <c>C_BaseAnimating</c> uses this one to drive sequence
    /// TRANSITIONS — <c>m_SequenceTransitioner.CheckForSequenceChange</c>
    /// (<c>c_baseanimating.cpp:1831</c>) — and to reset cycle interpolation at <c>:4738</c>. The
    /// viewmodel carries both because it is a <c>C_BaseAnimating</c> that also has viewmodel rules.
    ///
    /// Decoded and not yet acted on: this viewer does not blend between sequences, so there is no
    /// transitioner for it to feed. Recorded here rather than left out so the gap is visible.
    /// </remarks>
    public int? ViewmodelNewSequenceParity() =>
        Integer($"{ViewModelTable}.m_nNewSequenceParity");

    /// <summary>The counter that says THIS entity's animation restarted.</summary>
    /// <remarks>
    /// **The same field on <c>DT_BaseAnimating</c>, which every animated prop has and which nothing
    /// asked for until now.** <c>ViewmodelNewSequenceParity</c> above reads the viewmodel's copy and
    /// its remarks admit it is "decoded and not yet acted on"; this one is acted on, because it is
    /// what tells a spawn cabinet its `open` began.
    ///
    /// <c>C_BaseAnimating::OnDataChanged</c>, <c>c_baseanimating.cpp:4737</c>:
    ///
    /// <code>
    ///   // reset prev cycle if new sequence
    ///   if (m_nNewSequenceParity != m_nPrevNewSequenceParity)
    ///   {
    ///       ...
    ///       m_iv_flCycle.Reset();
    ///   }
    /// </code>
    ///
    /// **A counter rather than a comparison of sequence numbers, and that difference is the point.**
    /// <c>m_nNewSequenceParity = ( m_nNewSequenceParity + 1 ) &amp; EF_PARITY_MASK</c>
    /// (<c>c_baseanimating.cpp:5574</c>) — a cabinet used twice plays the SAME sequence twice, and
    /// only the counter says the second one began.
    /// </remarks>
    public int? NewSequenceParity() =>
        Integer($"{AnimatingTable}.m_nNewSequenceParity");

    /// <summary>The counter that says this entity JUMPED — <c>m_ubInterpolationFrame</c>.</summary>
    /// <remarks>
    /// **A discontinuity, and its own declaration says so**: `void IncrementInterpolationFrame();
    /// // Call this to cause a discontinuity (teleport)` (<c>baseentity.h:878</c>). It is on
    /// <c>DT_BaseEntity</c> rather than <c>DT_BaseAnimating</c>, because a teleport is a fact about
    /// the entity and not about its animation.
    ///
    /// **It is the second half of the guard that clears a sequence transition**
    /// (<c>sequence_Transitioner.cpp:41</c>):
    ///
    /// <code>
    ///   if ((seqdesc.flags &amp; STUDIO_SNAP) || !bInterpolate )
    ///       m_animationQueue.RemoveAll();
    /// </code>
    ///
    /// where `bInterpolate` is `!IsNoInterpolationFrame()` (<c>c_baseanimating.cpp:1832</c>) and
    /// that is `m_ubOldInterpolationFrame != m_ubInterpolationFrame` (<c>c_baseentity.h:2166</c>).
    ///
    /// **Like <see cref="NewSequenceParity"/> it is a COUNTER, so the value means nothing and only
    /// the change does.** Two teleports in consecutive snapshots read 1 then 2, and it wraps —
    /// `(m_ubInterpolationFrame + 1) % NOINTERP_PARITY_MAX` (<c>baseentity.cpp:8473</c>) — so zero
    /// is an ordinary value that a reader must not mistake for absence.
    ///
    /// **Measured on the wire** rather than assumed: `tf2-2026-pub-pov-cheater` sends it 13,261
    /// times across all four values — 12,830 zero, then 102, 149 and 180 of one, two and three.
    /// </remarks>
    public int? NoInterpolationParity() =>
        Integer($"{BaseEntityTable}.m_ubInterpolationFrame");

    /// <summary>The five condition bitfields, read as <c>CTFPlayerShared::InCond</c> does.</summary>
    /// <remarks>
    /// **All five, because 31 of `DT_TFPlayerShared`'s 66 fields live past the first.**
    /// `CConditionVars` (`tf_player_shared.cpp:1041`) picks the variable by the condition's range,
    /// so a reader that took only `m_nPlayerCond` would answer correctly for conditions 0..31 and
    /// silently wrongly for every one after — including most of what TF has added since 2007.
    ///
    /// Absent reads as zero rather than null: a player who sends no condition field is in no
    /// condition, which is the same thing the engine's zero-initialised member means.
    /// </remarks>
    public PlayerConditions Conditions() => new(
        Integer($"{PlayerSharedTable}.m_nPlayerCond") ?? 0,
        Integer($"{PlayerSharedTable}.m_nPlayerCondEx") ?? 0,
        Integer($"{PlayerSharedTable}.m_nPlayerCondEx2") ?? 0,
        Integer($"{PlayerSharedTable}.m_nPlayerCondEx3") ?? 0,
        Integer($"{PlayerSharedTable}.m_nPlayerCondEx4") ?? 0);

    /// <summary>Which class a disguised spy appears to be — <c>m_nDisguiseClass</c>.</summary>
    public int? DisguiseClass() => Integer($"{PlayerSharedTable}.m_nDisguiseClass");

    /// <summary>Which team a disguised spy appears to be on — <c>m_nDisguiseTeam</c>.</summary>
    public int? DisguiseTeam() => Integer($"{PlayerSharedTable}.m_nDisguiseTeam");

    /// <summary>Whose mask a spy disguised AS a spy wears — <c>m_nMaskClass</c>.</summary>
    /// <remarks>
    /// Read in exactly one branch: <c>GetDisguiseMask</c> (<c>tf_player_shared.h:375</c>) supplies
    /// it to `GetSkin`'s enemy mask offset when the disguise class is itself a spy.
    /// </remarks>
    public int? DisguiseMaskClass() => Integer($"{PlayerSharedTable}.m_nMaskClass");

    /// <summary>Whether this cosmetic or weapon belongs to its owner's DISGUISE.</summary>
    /// <remarks>
    /// **Two fields, one question.** A wearable declares <c>m_bDisguiseWearable</c> on
    /// <c>DT_TFWearable</c> (<c>tf_item_wearable.cpp:36</c>) and a weapon declares
    /// <c>m_bDisguiseWeapon</c> on <c>DT_TFWeaponBase</c> (<c>tf_weaponbase.cpp:198</c>); an entity
    /// declares one or the other, never both, so asking for either and taking what answers is the
    /// same question rather than a guess between two.
    ///
    /// The server creates a disguise's gear as its own entities bone-merged to the spy, so an ENEMY
    /// sees a convincing soldier — and this flag is the only thing that separates them from the
    /// spy's own. Without it, soldier hats and a rocket launcher draw on a spy's skeleton.
    /// </remarks>
    public bool OfDisguise() =>
        (Integer($"{WearableTable}.m_bDisguiseWearable")
            ?? Integer($"{TfWeaponTable}.m_bDisguiseWeapon")) is not (null or 0);

    /// <summary>Where a TF player's shared state lives on the wire.</summary>
    /// <remarks>
    /// **A table this project read NOTHING from until now** — `docs/WIRE-COVERAGE.md` reported it
    /// at 0 of 66 declared properties, and it carries conditions, disguises, cloak, charge and
    /// stuns.
    /// </remarks>
    private const string PlayerSharedTable = "DT_TFPlayerShared";

    /// <summary>Whether the CLIENT advances this entity's cycle rather than the server.</summary>
    /// <remarks>
    /// **<c>m_bClientSideAnimation</c>, and it selects between two different restart signals.**
    /// <c>C_BaseAnimating::OnDataChanged</c> (<c>c_baseanimating.cpp:5021</c>):
    ///
    /// <code>
    ///   // Only need to think if animating client side
    ///   if ( m_bClientSideAnimation )
    ///   {
    ///       // Check to see if we should reset our frame
    ///       if ( m_bClientSideFrameReset != m_bLastClientSideFrameReset )
    ///       {
    ///           ResetClientsideFrame();
    ///       }
    ///   }
    /// </code>
    ///
    /// Measured on `cp_fulgur`: the spawn cabinets send <c>1</c> here and send
    /// <c>DT_ServerAnimationData.m_flCycle</c> NEVER — the server states no cycle for them at all
    /// because the client is expected to run it.
    /// </remarks>
    public int? ClientSideAnimation() =>
        Integer($"{AnimatingTable}.m_bClientSideAnimation");

    /// <summary>The toggle that says a client-side animation should start over.</summary>
    /// <remarks>
    /// **A TOGGLE, not a counter, and that is the whole of how it is read.**
    /// <c>CBaseAnimating::ResetClientsideFrame</c> (<c>server/baseanimating.cpp:3055</c>):
    ///
    /// <code>
    ///   void CBaseAnimating::ResetClientsideFrame( void )
    ///   {
    ///       // (Valve's own to-do marker elided so the analyzer does not read it as ours:
    ///       //  "Once we can chain MSG_ENTITY messages, use one of them")
    ///       m_bClientSideFrameReset = !(bool)m_bClientSideFrameReset;
    ///   }
    /// </code>
    ///
    /// so the VALUE means nothing and only a CHANGE does — the client compares it against
    /// <c>m_bLastClientSideFrameReset</c>. Reading it as a boolean "should reset" would restart the
    /// animation on every update where it happened to be one, and never where it was zero.
    ///
    /// **This is the restart signal for a prop, where <see cref="NewSequenceParity"/> is the one for
    /// a server-animated entity.** Measured on the same cabinets: 274 of these against 300 parities,
    /// and the two do not have to coincide.
    /// </remarks>
    public int? ClientSideFrameReset() =>
        Integer($"{AnimatingTable}.m_bClientSideFrameReset");


    /// <summary>The counter that re-arms an ENTITY's animation events.</summary>
    /// <returns><c>m_nResetEventsParity</c>, or null when the entity never sent one.</returns>
    /// <remarks>
    /// <c>c_baseanimating.cpp:3618</c>: <c>bool resetEvents = m_nResetEventsParity !=
    /// m_nPrevResetEventsParity;</c>, and <c>DoAnimationEvents</c> treats a change exactly as it
    /// treats a sequence change — the walk restarts at cycle zero. It is what lets a taunt played
    /// twice in a row sound twice, since the sequence number never moves between them (B275).
    /// </remarks>
    public int? ResetEventsParity() =>
        Integer($"{AnimatingTable}.m_nResetEventsParity");

    /// <summary>The counter that re-arms animation events — <c>m_nResetEventsParity</c>.</summary>
    /// <remarks>
    /// <c>c_baseanimating.cpp:3618</c>: <c>bool resetEvents = m_nResetEventsParity !=
    /// m_nPrevResetEventsParity;</c>, which lets an animation's events fire again when it replays.
    /// The viewmodel's own copy; <see cref="ResetEventsParity"/> is the one every other entity
    /// sends.
    /// </remarks>
    public int? ViewmodelResetEventsParity() =>
        Integer($"{ViewModelTable}.m_nResetEventsParity");

    /// <summary>The counter that fires a muzzle flash — <c>m_nMuzzleFlashParity</c>.</summary>
    /// <remarks>
    /// Two bits, <c>EF_MUZZLEFLASH_BITS</c> (<c>const.h:305</c>), bumped by
    /// <c>C_BaseAnimating::DoMuzzleFlash</c> (<c>c_baseanimating.cpp:6284</c>). Decoded and not yet
    /// acted on: nothing here draws a muzzle flash.
    /// </remarks>
    public int? ViewmodelMuzzleFlashParity() =>
        Integer($"{ViewModelTable}.m_nMuzzleFlashParity");

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
    /// <summary>Which entity OWNS this one, whatever it is attached to.</summary>
    /// <returns>The owner's entity slot, or null when it has none.</returns>
    /// <remarks>
    /// **Ownership and attachment are different questions, and <see cref="Attachment"/> answers the
    /// other one.** That method reports where an entity is DRAWN — a parent outright, or an owner
    /// when the entity also asked to be bone-merged — because an owner alone says nothing about
    /// position. This one reports who it BELONGS to, which is what the engine keys visibility on.
    ///
    /// <c>C_BaseCombatWeapon::ShouldDraw</c> is the case that needs it:
    ///
    /// <code>
    /// C_BaseCombatCharacter *pOwner = GetOwner();
    /// if ( !pOwner ) return true;                  // unowned, always drawn
    /// if ( pOwner == pLocalPlayer ) {
    ///     if ( !bIsActive ) return false;          // only ever the active weapon
    ///     ...
    ///     return false;                            // first person: the viewmodel draws it
    /// }
    /// </code>
    ///
    /// A carried weapon that sends its own origin is owned by its carrier and parented to nobody,
    /// so asking `Attachment` whether it belongs to the player answers null and it is drawn in the
    /// first-person view alongside the viewmodel — two sticky launchers overlapping, which is what
    /// this was found as.
    /// </remarks>
    public int? Owner() => Slot(Integer($"{BaseEntityTable}.{OwnerProperty}"));

    /// <summary>Whether the WIRE says this entity rides its parent's skeleton.</summary>
    /// <remarks>
    /// <c>EF_BONEMERGE</c>, the branch <c>C_BaseEntity::CalcAbsolutePosition</c> takes second
    /// (<c>c_baseentity.cpp:4387</c>).
    ///
    /// **This is only half the answer and must not be used alone** (B231). Measured on a real
    /// match: every weapon carries the flag, and **26 of 26 `CTFWearable` entities carry no
    /// <c>m_fEffects</c> at all** — because `CEconWearable::Spawn` adds it on the CLIENT, for every
    /// wearable any client creates, so it never needs to travel. The other half is
    /// <c>SchemaClasses.BoneMergesItself</c>, which derives the same thing from the class the way
    /// the engine does; `DemoTimeline` combines them.
    ///
    /// Using this alone sent every hat, cosmetic and powerup bottle down the transform path and
    /// broke the viewer outright.
    /// </remarks>
    public bool IsBoneMerged =>
        (Effects() & BoneMerge) != 0;

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

    /// <summary>The raw handle this entity hangs off, serial and all.</summary>
    /// <returns>The handle as the wire carried it, or <c>null</c> when it names nothing.</returns>
    /// <remarks>
    /// **<see cref="Attachment"/> answers the same question with the serial thrown away**, which is
    /// safe only while no slot is ever reused — and slots are reused constantly (B231). This hands
    /// the whole value to <c>EntityStateTable.Resolve</c>, which compares the serial against the
    /// slot's current occupant exactly as dereferencing a <c>CBaseHandle</c> does.
    ///
    /// The same two sources, in the same order: a move parent outright, or an owner for something
    /// that also asked to be bone-merged.
    /// </remarks>
    public int? AttachmentHandle()
    {
        if (Integer($"{BaseEntityTable}.{ParentProperty}") is { } parent &&
            parent != InvalidHandle)
        {
            return parent;
        }

        return ((Integer($"{BaseEntityTable}.{EffectsProperty}") ?? 0) & BoneMerge) == 0
            ? null
            : Integer($"{BaseEntityTable}.{OwnerProperty}");
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

    /// <summary>What the player is watching through, when it says.</summary>
    /// <returns><c>m_iObserverMode</c>, or <c>null</c> when it was never sent.</returns>
    /// <remarks>
    /// **<c>DT_BasePlayer</c> proper, not <c>DT_LocalPlayerExclusive</c>** — verified in
    /// <c>player.cpp:8184</c>, where it sits between <c>m_fFlags</c> and <c>m_hObserverTarget</c>
    /// and above the <c>"localdata"</c> table. So it arrives for every player in any recording,
    /// which is what makes it usable on a SourceTV demo as well as a point-of-view one.
    ///
    /// Three bits unsigned, which is exactly enough for the eight values <c>shareddefs.h:492</c>
    /// defines — nothing the enum can hold is unrepresentable on the wire.
    ///
    /// **Absent means <c>OBS_MODE_NONE</c>**, because zero is the default and a delta-compressed
    /// format only sends what changed. A recording that never mentions the field is a recording of
    /// someone who never observed, not an unknown — the same rule as <see cref="LifeState"/>, and
    /// the reason a caller must not treat null as "refuse to answer".
    ///
    /// The companion field <c>m_hObserverTarget</c> is deliberately NOT read: it is an EHandle, and
    /// masking one down to its index turns "nobody" into entity 2047, which is a legal index. See
    /// <c>UnimplementedGameplayEntityConformanceTests</c>, which still records that gap.
    /// </remarks>
    public int? ObserverMode() => Integer($"{BasePlayerTable}.{ObserverModeProperty}");

    /// <summary>The entity's render colour and alpha, when it says.</summary>
    /// <returns><c>m_clrRender</c> as a packed <c>color32</c>, or <c>null</c> when never sent.</returns>
    /// <remarks>
    /// **A <c>color32</c> squeezed into a 32-bit int** — <c>SendPropInt(SENDINFO(m_clrRender), 32,
    /// SPROP_UNSIGNED)</c> (<c>baseentity.cpp:279</c>). The struct is <c>byte r, g, b, a</c>
    /// (<c>tier0/basetypes.h:248</c>), so on a little-endian machine the red is the LOW byte and the
    /// alpha the high one. Getting that round the wrong way tints every entity and leaves the alpha
    /// reading as a colour channel, which looks like a lighting fault rather than a decode one.
    ///
    /// **Absent means opaque white**, <c>255,255,255,255</c>, which is what an entity that never
    /// mentions the field is: unmodulated and fully solid. `RenderAlpha` applies that default so a
    /// caller does not have to.
    /// </remarks>
    public int? RenderColor() => Integer($"{BaseEntityTable}.{RenderColorProperty}");

    /// <summary>The alpha byte of <see cref="RenderColor"/>, defaulting to opaque.</summary>
    /// <remarks>
    /// **Not nullable, because the default IS the answer.** A delta-compressed format sends only
    /// what changed, and an entity nobody has tinted is opaque — treating absence as unknown would
    /// make every ordinary entity a special case at the call site
    /// (`docs/memory/sentinels-conflate-unknown-with-answer.md`).
    /// </remarks>
    public byte RenderAlpha() =>
        RenderColor() is { } packed ? (byte)((packed >> 24) & 0xFF) : (byte)255;

    /// <summary>Which effect animates the entity's alpha, when it says.</summary>
    /// <returns><c>m_nRenderFX</c>, or <c>null</c> when it was never sent.</returns>
    /// <remarks>
    /// Eight bits unsigned (<c>baseentity.cpp:276</c>). **Absent means <c>kRenderFxNone</c>**, which
    /// is zero and by far the common case — almost nothing in a match pulses or strobes.
    /// </remarks>
    public int? RenderFx() => Integer($"{BaseEntityTable}.{RenderFxProperty}");

    /// <summary>The distance at which this entity starts fading out.</summary>
    /// <returns><c>m_fadeMinDist</c>, or <c>null</c> when it was never sent.</returns>
    /// <remarks>
    /// **Absent means no fade, and so does zero** — <c>ComputeDistanceFade</c>'s first branch is
    /// <c>(flMinDist &lt;= 0) &amp;&amp; (flMaxDist &lt;= 0)</c>. A NEGATIVE value is meaningful and
    /// common: it means "start fading 400 units before the maximum", and 28 entities in the 2013
    /// foundry demo send exactly <c>-1</c>.
    /// </remarks>
    public float? FadeMinimumDistance() => Number($"{AnimatingTable}.m_fadeMinDist");

    /// <summary>The distance beyond which this entity is invisible.</summary>
    /// <returns><c>m_fadeMaxDist</c>, or <c>null</c> when it was never sent.</returns>
    public float? FadeMaximumDistance() => Number($"{AnimatingTable}.m_fadeMaxDist");

    /// <summary>Which blend mode the entity draws with, when it says.</summary>
    /// <returns><c>m_nRenderMode</c>, or <c>null</c> when it was never sent.</returns>
    /// <remarks>
    /// Eight bits unsigned (<c>baseentity.cpp:277</c>). **Absent means <c>kRenderNormal</c>**, and
    /// that default is load-bearing rather than incidental: `ComputeFxBlend`'s default branch
    /// answers 255 for <c>kRenderNormal</c> and the colour's alpha for anything else, so reading
    /// absence as some other mode would make every untouched entity translucent.
    /// </remarks>
    public int? RenderMode() => Integer($"{BaseEntityTable}.{RenderModeProperty}");

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
    ///
    /// **Two wire names, one value, and the engine declares both receivers**
    /// (<c>game/client/c_baseanimating.cpp:180</c>):
    ///
    /// <code>
    /// RecvPropFloat(RECVINFO(m_flModelScale)),
    /// RecvPropFloat(RECVINFO_NAME(m_flModelScale, m_flModelWidthScale)), // for demo compatibility only
    /// </code>
    ///
    /// <c>RECVINFO_NAME</c> takes the WIRE name second, so <c>m_flModelWidthScale</c> is what TF2
    /// called this property before 2013 — and Valve's comment names demos as the reason the old
    /// receiver is still there. **The corpus splits exactly on that line**: the 2007, 2008, 2009 and
    /// 2011 era specimens declare only <c>m_flModelWidthScale</c>, while the 2013 build and z1800
    /// declare only <c>m_flModelScale</c>. Reading one name meant every entity in every pre-2013
    /// demo drew at the caller's default of 1 (B271) — including a mini-sentry, which is 0.75.
    ///
    /// The modern name is preferred when both arrive, matching the order the receivers are declared
    /// in. No corpus demo sends both.
    /// </remarks>
    public float? ModelScale() =>
        Number($"{AnimatingTable}.{ModelScaleProperty}")
        ?? Number($"{AnimatingTable}.{LegacyModelScaleProperty}");

    /// <summary>TF2's three per-BONE scales, distinct from the whole-model one above.</summary>
    /// <returns>Head, torso and hand scale; each null when the demo did not send it.</returns>
    /// <remarks>
    /// **`RecvPropFloat` on `DT_TFPlayer`** (`c_tf_player.cpp:539`), and
    /// `C_TFPlayer::BuildTransformations` runs all three unconditionally at its end
    /// (`c_tf_player.cpp:8815`) — the call is not gated, the VALUE is what makes it a no-op.
    ///
    /// **Nothing read these until B312, and no measurement could have found them.** Each defaults
    /// to 1, and a field that multiplies a scale and defaults to one draws an identical picture when
    /// ignored — so every rendering comparison agreed and every count matched. That is exactly what
    /// happened to `m_flPlaybackRate`, decoded and retained and unit-tested while every animation
    /// played at rate 1.
    ///
    /// **On `DT_TFPlayer` rather than a local/non-local exclusive**, so they arrive for every
    /// player rather than only for the recorder — unlike the origin, which splits.
    /// </remarks>
    /// <summary>Everything a corpse says about itself — <c>DT_TFRagdoll</c>.</summary>
    /// <returns>The fields, each null when this entity is not a ragdoll or did not send one.</returns>
    /// <remarks>
    /// **`DT_TFRagdoll` is `NOBASE`** — `IMPLEMENT_CLIENTCLASS_DT_NOBASE( C_TFRagdoll, DT_TFRagdoll,
    /// CTFRagdoll )` — so it inherits nothing and carries no `m_nModelIndex`, no `m_nSkin`, no
    /// `m_nBody` and no `m_angRotation`. **That is why every corpse in every demo is invisible
    /// here**: a prop path asks for a model index, gets none, and correctly draws nothing. 299
    /// `CTFRagdoll` entities in one match, all decoded, none described
    /// (`docs/PARITY-AUDIT.md` #4).
    ///
    /// **The client BUILDS the missing fields in `CreateTFRagdoll`** (`c_tf_player.cpp:691`), which
    /// is why that function is forty branches long — the model from the class, the skin from the
    /// team, the body off the player. Everything it needs is on this table, which is what makes the
    /// gap reproducible rather than lost.
    ///
    /// **Read here rather than resolved here.** Turning a class number into `models/player/spy.mdl`
    /// needs the game install, which is the Scene layer's job and not Core's — the same split a
    /// player already uses.
    /// </remarks>
    public (int? Class, int? Team, bool Gib, bool Burning, bool FeignDeath, bool WasDisguised)
        Ragdoll() =>
        (Integer($"{RagdollTable}.m_iClass"),
         Integer($"{RagdollTable}.m_iTeam"),
         Integer($"{RagdollTable}.m_bGib") is int and not 0,
         Integer($"{RagdollTable}.m_bBurning") is int and not 0,
         Integer($"{RagdollTable}.m_bFeignDeath") is int and not 0,
         Integer($"{RagdollTable}.m_bWasDisguised") is int and not 0);

    /// <summary>Whether a corpse was on the ground when it was made — <c>m_bOnGround</c>.</summary>
    /// <returns>True when the flag is set.</returns>
    /// <remarks>
    /// **It vetoes the death animation, which is its only use in `CreateTFRagdoll`:**
    /// <c>if ( !m_bOnGround &amp;&amp; bPlayDeathAnim &amp;&amp; !bPlayDeathInAir ) bPlayDeathAnim = false;</c>
    /// (`c_tf_player.cpp:838-839`), under Valve's own comment *"Don't play most death anims in the
    /// air (headshot, etc)"* — a body already falling should not stand up to be shot again.
    /// </remarks>
    public bool RagdollOnGround() => Integer($"{RagdollTable}.m_bOnGround") is int and not 0;

    /// <summary>Whether this corpse turned to gold — <c>m_bGoldRagdoll</c>.</summary>
    /// <returns>True when the flag is set.</returns>
    /// <remarks>
    /// A Saxxy or Golden Wrench kill. **Absent from older tables**: the 2014 era specimen's
    /// `DT_TFRagdoll` carries no such property at all, so this reads false there — which is what
    /// that era's client saw too, since the field did not exist to be sent.
    /// </remarks>
    public bool RagdollGold() => Integer($"{RagdollTable}.m_bGoldRagdoll") is int and not 0;

    /// <summary>Whether this corpse froze — <c>m_bIceRagdoll</c>.</summary>
    /// <returns>True when the flag is set.</returns>
    /// <remarks>A Spy-cicle backstab. Absent from older tables, like the gold flag beside it.</remarks>
    public bool RagdollIce() => Integer($"{RagdollTable}.m_bIceRagdoll") is int and not 0;

    /// <summary>Where a corpse came to rest — <c>m_vecRagdollOrigin</c>.</summary>
    /// <returns>The position, or null when this entity sent none.</returns>
    /// <remarks>
    /// **Its own property, because the table has no <c>m_vecOrigin</c> to inherit.** A reader
    /// falling back to <see cref="Origin"/> for a ragdoll finds nothing and places the corpse at
    /// the world origin, which is the plausible-wrong answer this format specialises in.
    /// </remarks>
    public (float X, float Y, float Z)? RagdollOrigin()
    {
        if (!_properties.TryGetValue($"{RagdollTable}.m_vecRagdollOrigin", out PropertyValue at))
        {
            return null;
        }

        // **A whole three-component vector, unlike a player's**, whose horizontal pair and height
        // travel separately in the modern shape. `SendPropVector( SENDINFO( m_vecRagdollOrigin ) )`
        // has no `[2]` companion, so the split that catches a player reader does not apply — and a
        // reader that assumed it did would read a height that was never sent.
        return at.Kind switch
        {
            PropertyValueKind.Vector => at.AsVector,
            PropertyValueKind.VectorXY =>
                (at.AsVectorXY.X, at.AsVectorXY.Y, Number($"{RagdollTable}.m_vecRagdollOrigin[2]") ?? 0f),
            _ => null,
        };
    }

    /// <summary>How this corpse was made — <c>m_iDamageCustom</c>.</summary>
    /// <returns>A <c>TF_DMG_CUSTOM_*</c> ordinal, or null when this entity sent none.</returns>
    /// <remarks>
    /// **The whole death-animation question turns on this one integer**, and it excludes far more
    /// than it admits. `CTFPlayerShared::GetSequenceForDeath` is a `switch` on it with two cases and
    /// no default (`tf_player_shared.cpp:13425-13456`): headshot, decapitation and their taunt
    /// variants get `primary_death_headshot`, backstab gets `primary_death_backstab`, and **every
    /// other death returns -1** — no animation at all, straight to physics.
    ///
    /// So the "25% of deaths play an animation" reading is wrong by a wide margin: 25% of the
    /// ELIGIBLE deaths do, and eligibility is only these.
    /// </remarks>
    public int? DamageCustom() => Integer($"{RagdollTable}.m_iDamageCustom");

    /// <summary>Which player this corpse was — <c>m_hPlayer</c>.</summary>
    /// <returns>The raw handle, or null when this entity sent none.</returns>
    /// <remarks>
    /// **The corpse's ANGLES come from here and from nowhere else.** `DT_TFRagdoll` is `NOBASE`, so
    /// there is no `m_angRotation` on it; the client turns the corpse to face the way the player was
    /// facing — <c>SetAbsAngles( pPlayer-&gt;GetRenderAngles() )</c> (`c_tf_player.cpp:766`, and again
    /// at `:775` for the local-player branch). Without it every body in a match faces the same way.
    ///
    /// **Raw, because a handle is not an index** (B231). The low 11 bits of
    /// `INVALID_EHANDLE_INDEX` mask to 2047, which is a legitimate slot — so the caller resolves it
    /// through <c>EntityStateTable.Resolve</c> rather than masking, and gets null for a player who
    /// has since left rather than whoever now occupies 2047.
    /// </remarks>
    public int? RagdollPlayerHandle() => Integer($"{RagdollTable}.m_hPlayer");

    /// <summary>
    /// Which player this corpse was, under the name older builds send — <c>m_iPlayerIndex</c>.
    /// </summary>
    /// <returns>The player's entity index directly, or null when this entity sent none.</returns>
    /// <remarks>
    /// **TF2 renamed this field, and the two are not the same KIND of value** (B319). Measured on
    /// the corpus with the CLI's trace:
    ///
    /// | demo | field | values |
    /// |---|---|---|
    /// | `serveme-627619-stv-2026-08-07` | <c>m_hPlayer</c> | 24587, 174093, 311301, … |
    /// | `z1800` | <c>m_iPlayerIndex</c> | 2, 3, 4, 5, … |
    ///
    /// The first is a packed ehandle needing <c>EntityStateTable.Resolve</c>; the second is a player
    /// entity index used as it stands. Reading either one as the other gives a plausible number and
    /// the wrong player — index 24587 does not exist, and handle 3 resolves to whatever occupies
    /// slot 3 with serial 0.
    ///
    /// **`m_iPlayerIndex` is not in the published SDK at all**, not even as a `RECVINFO_NAME`
    /// alias, so nothing but a demo can date it. That is this project's premise working as intended
    /// (`docs/memory/the-demo-dates-its-own-fields.md`): the schema travels with the file, and the
    /// SDK is one build's snapshot.
    /// </remarks>
    public int? RagdollPlayerIndex() => Integer($"{RagdollTable}.m_iPlayerIndex");

    /// <summary>The table a corpse's own fields live on.</summary>
    private const string RagdollTable = "DT_TFRagdoll";

    public (float? Head, float? Torso, float? Hand) BoneScales() =>
        (Number($"{TfPlayerTable}.m_flHeadScale"),
         Number($"{TfPlayerTable}.m_flTorsoScale"),
         Number($"{TfPlayerTable}.m_flHandScale"));

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

    /// <summary>Forgets every networked property, for an entity being decoded from a baseline.</summary>
    /// <remarks>
    /// **`CL_CopyNewEntity` decodes an entering entity FROM ITS BASELINE**, which is a starting
    /// point rather than an overlay — the engine's own assert strings name three separate paths
    /// (`CL_CopyNewEntity: GetClassBaseline(%d) failed.`, `CL_CopyExistingEntity: missing client
    /// entity %d.`, `CL_PreserveExistingEntity`), and only the middle one is a delta against what
    /// the client already holds.
    ///
    /// Without this an entity that leaves and re-enters the potentially visible set keeps every
    /// value the baseline and the update both omit. Measured: a `CTFBonesaw` last stated
    /// `m_iState 2` at tick 8060 was still ACTIVE six thousand ticks and eight PVS transitions
    /// later, so its owner drew a medigun and a melee weapon in the same hand (B245).
    ///
    /// **The sequence counter is deliberately NOT reset.** It orders writes so the most recent
    /// table wins for a value two exclusive tables both carry, and restarting it would make a
    /// later write look older than one that survived in another key.
    /// </remarks>
    internal void Forget()
    {
        _properties.Clear();
        _lastSet.Clear();
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
