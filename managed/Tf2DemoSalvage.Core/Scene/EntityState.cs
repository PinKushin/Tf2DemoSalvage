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
    private const string LocalOriginTable = "DT_TFLocalPlayerExclusive";

    /// <summary>Where TF2 sends every other player's position.</summary>
    private const string NonLocalOriginTable = "DT_TFNonLocalPlayerExclusive";

    /// <summary>Where every non-player entity sends its position.</summary>
    private const string BaseEntityTable = "DT_BaseEntity";

    private const string OriginProperty = "m_vecOrigin";
    private const string OriginZProperty = "m_vecOrigin[2]";
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
