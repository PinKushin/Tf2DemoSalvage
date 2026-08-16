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
                OriginProperty, AnglesProperty, EffectsProperty, ModelIndexProperty,
                OwnerProperty,
            ],
            [AnimatingTable] =
            [
                SequenceProperty, BodyProperty, CycleProperty, PlaybackRateProperty,
                ModelScaleProperty,
            ],
        };

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
    /// <c>INVALID_NETWORKED_EHANDLE_VALUE</c>: <c>(1 &lt;&lt; (MAX_EDICT_BITS + 10)) - 1</c>. Tested
    /// against the WHOLE value, because its low eleven bits are 2047 and would otherwise read as a
    /// real slot.
    /// </summary>
    private const int InvalidHandle = (1 << (EdictBits + 10)) - 1;

    /// <summary>Only things that animate carry the four below.</summary>
    private const string AnimatingTable = "DT_BaseAnimating";

    private const string SequenceProperty = "m_nSequence";

    /// <summary>Which alternative each body part shows, packed into one number.</summary>
    private const string BodyProperty = "m_nBody";
    private const string CycleProperty = "m_flCycle";
    private const string PlaybackRateProperty = "m_flPlaybackRate";
    private const string ModelScaleProperty = "m_flModelScale";

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
    public bool IsDrawn =>
        IsVisible && ((Integer($"{BaseEntityTable}.{EffectsProperty}") ?? 0) & NoDraw) == 0;

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
    public int? LifeState() => Integer("DT_BasePlayer.m_lifeState");

    /// <summary>How far through its animation the entity is, from 0 to 1.</summary>
    /// <returns>The cycle, or <c>null</c> when the entity does not animate.</returns>
    /// <remarks><c>c_baseanimating.cpp:152</c>.</remarks>
    public float? Cycle() => Number($"{AnimatingTable}.{CycleProperty}");

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
