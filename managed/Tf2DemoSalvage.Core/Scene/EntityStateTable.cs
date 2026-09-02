using System.Collections.Generic;
using System.Linq;
using System.Diagnostics.CodeAnalysis;
using Tf2DemoSalvage.Core.Schema;

namespace Tf2DemoSalvage.Core.Scene;

/// <summary>
/// Accumulates entity snapshots into the current state of the world.
/// </summary>
/// <remarks>
/// Deliberately separate from <see cref="EntityDecoder"/>. That class answers "what did this
/// snapshot say", which is a question about bits; this one answers "what is true now", which is a
/// question about the sum of every snapshot so far. Keeping them apart is what lets the decoder
/// stay stateless per message while the viewer gets a world to draw.
/// </remarks>
public sealed class EntityStateTable
{
    private readonly Dictionary<int, EntityState> _entities = [];
    private readonly Dictionary<int, string> _classNames = [];
    private readonly IEntityBaselines _baselines;

    /// <summary>Creates an accumulator that reads entities against their class baselines.</summary>
    /// <param name="baselines">
    /// Resolves an entity's full state from what its snapshot said. Pass the
    /// <see cref="EntityDecoder"/> that produced the snapshots, or
    /// <see cref="EntityBaselines.None"/> when there is no schema to resolve against.
    /// </param>
    /// <exception cref="System.ArgumentNullException"><paramref name="baselines"/> is null.</exception>
    public EntityStateTable(IEntityBaselines baselines)
    {
        System.ArgumentNullException.ThrowIfNull(baselines);

        _baselines = baselines;
    }

    /// <summary>Dereferences an entity handle, checking its serial as the engine does.</summary>
    /// <param name="handle">The handle as the wire carried it, or <c>null</c> when absent.</param>
    /// <returns>The entity's slot, or <c>null</c> when the handle names nothing live.</returns>
    /// <remarks>
    /// **A handle is an index AND a serial, and dropping the serial is not a simplification**
    /// (B231). <c>RecvProxy_IntToEHandle</c> (<c>client/recvproxy.cpp:80</c>) keeps both —
    /// <c>pEHandle-&gt;Init( iEntity, iSerialNum )</c> — and dereferencing one compares the serial
    /// against the slot's current occupant. That comparison is the whole point: entity slots are
    /// reused, so a handle taken before a slot changed hands must resolve to NOTHING rather than to
    /// whoever moved in.
    ///
    /// **Masking alone resolves a dangling handle to a real, existing, different entity**, which is
    /// silent and plausible. Measured on `cp_fulgur`: a spawn resupply locker was composed onto
    /// entity 434 — a door — because its parent handle pointed at a slot that had been reused, and
    /// the locker then appeared thousands of units from where it belongs.
    ///
    /// The invalid sentinel is tested BEFORE the mask, for the reason
    /// <see cref="EntityState.Slot"/> records: it is 21 bits of ones and its low 11 mask to 2047, a
    /// perfectly ordinary-looking slot.
    ///
    /// **On the table rather than on <see cref="EntityState"/>**, because resolving needs the slot's
    /// CURRENT occupant and an entity cannot see its neighbours. That is the same split the engine
    /// has: the handle is a value, and `cl_entitylist` is what turns it into an entity.
    /// </remarks>
    public int? Resolve(int? handle)
    {
        if (EntityState.Slot(handle) is not { } slot || handle is not { } raw)
        {
            return null;
        }

        // Absent, and a slot nothing occupies, are the same answer: nothing to point at.
        return _entities.TryGetValue(slot, out EntityState? occupant)
            && occupant.SerialNumber == raw >> EntityState.EdictBitCount
                ? slot
                : null;
    }

    /// <summary>Names a networked class, so accumulated entities can report it.</summary>
    /// <param name="classId">The id entity snapshots carry.</param>
    /// <param name="className">The server class name, e.g. <c>CTFPlayer</c>.</param>
    public void SetClassName(int classId, string className)
    {
        _classNames[classId] = className;

        foreach (EntityState state in _entities.Values)
        {
            if (state.ClassId == classId)
            {
                state.ClassName = className;
            }
        }
    }

    /// <summary>The tick of the packet being applied.</summary>
    /// <remarks>
    /// **Ambient rather than a parameter, which is what the engine does too** (B273).
    /// <c>m_flSimulationTime</c> carries <c>SPROP_ENCODED_AGAINST_TICKCOUNT</c> — eight bits of
    /// offset from a base derived from the packet's own tick — so it cannot be decoded without
    /// knowing when it arrived, and this table retains properties across packets by design.
    /// <c>RecvProxy_SimulationTime</c> reads <c>gpGlobals->tickcount</c> the same way rather than
    /// being handed it.
    ///
    /// Zero is a legitimate starting value and means the base is zero, which is right for a caller
    /// that has not started reading a demo.
    /// </remarks>
    public int PacketTick { get; set; }

    /// <summary>Applies one snapshot's view of an entity.</summary>
    /// <param name="entity">The entity as a snapshot described it.</param>
    /// <exception cref="System.ArgumentNullException"><paramref name="entity"/> is null.</exception>
    public void Apply(DecodedEntity entity)
    {
        System.ArgumentNullException.ThrowIfNull(entity);

        if (entity.UpdateType == EntityUpdateType.Delete)
        {
            _entities.Remove(entity.EntityIndex);
            return;
        }

        // **Only an Enter states a serial number.** It travels with an entity's creation and
        // nowhere else, so EntityDecoder passes zero for a Delta because there is nothing on the
        // wire to read. Comparing that zero against the stored serial makes every delta look like
        // a new occupant, and the table then throws away everything it has accumulated.
        //
        // The symptom was a demo showing team colours the moment it opened and losing them as soon
        // as it was scrubbed: position survives because deltas usually resend an origin, and team
        // does not because it is sent once and never again. Found by the owner scrubbing, not by
        // the suite - the existing test for this hands its delta the same serial as its enter,
        // which is a value the decoder never produces, so correct and broken agreed on it.
        bool statesSerial = entity.UpdateType == EntityUpdateType.Enter;

        if (!_entities.TryGetValue(entity.EntityIndex, out EntityState? state) ||
            (statesSerial && state.SerialNumber != entity.SerialNumber))
        {
            // A different serial number in the same slot is a different entity. Merging into the
            // old one leaves the newcomer holding whichever properties it has not happened to
            // resend - a player who is on the previous occupant's team until they next change it,
            // which is a wrong answer that looks entirely reasonable.
            state = new EntityState(
                entity.EntityIndex,
                entity.ClassId,
                entity.SerialNumber,
                _classNames.GetValueOrDefault(entity.ClassId));

            _entities[entity.EntityIndex] = state;
        }

        // Leave is not Delete: the entity has left the potentially-visible set and still exists,
        // so its properties stay and only its visibility changes.
        state.IsVisible = entity.UpdateType != EntityUpdateType.Leave;

        // **The entity's state, not the snapshot's bits, and the two differ on every Enter.** An
        // entity entering the visible set is a delta against its class's instance baseline and
        // omits everything equal to it, so `entity.Properties` is what the wire carried rather
        // than what the entity is. The engine merges the baseline in CL_CopyNewEntity before the
        // entity exists at all; this table skipped that step and accumulated the difference.
        //
        // For most entities the difference is nothing visible: a player sends an origin, a health
        // and a team every time, so the baseline adds only values that arrive again seconds later.
        // For an entity whose whole state IS its baseline the difference is everything - a fog
        // controller enters once at tick 1 with fifteen properties, none of them on the wire, and
        // is never mentioned again. It reached this table with its class name and nothing else,
        // on every demo in the corpus, and stayed that way for the life of the file (B132).
        // **Only for an entity being CREATED, which is what the paragraph above actually
        // describes** (B231). `CL_CopyNewEntity` runs "before the entity exists at all" — and an
        // entity re-entering the potentially visible set already exists. Merging the baseline on
        // every `Enter` overwrote everything it had accumulated with class defaults.
        //
        // Measured on `cp_fulgur`, one spawn-door prop across the whole recording:
        //
        //     tick  9781: Enter, serial 91, 11 properties, moveparent 1587610
        //     tick  9860: Leave
        //     tick 14180: Enter, serial 91,  0 properties
        //     tick 14635: Leave
        //     tick 15059: Enter, serial 91,  0 properties
        //
        // Same serial, so the same entity, so `created` is false above and the state was rightly
        // kept — and then the baseline put `moveparent` back to its no-parent default and the gate
        // came off its door. The owner described it exactly: *"the things are showing up at tick 0,
        // but immedietly dissapearing when you hit play"*.
        //
        // **An `Enter` carrying ZERO properties is unreadable any other way.** As "rebuild from
        // baseline" it would mean the server discarding everything it knows about a live, unchanged
        // entity; as "this is visible again, nothing has changed" it is what a delta-compressed
        // protocol should send.
        //
        // **This is not a door bug.** Every entity that leaves and re-enters the PVS was losing
        // team, skin, parent, render mode and anything else sent once — which on a point-of-view
        // recording is most of the map, repeatedly.
        // **Every Enter decodes from a BASELINE, and a baseline is a starting point rather than an
        // overlay** (B245). The engine keeps three paths and `engine.dll` still carries their names:
        // `CL_CopyNewEntity: GetClassBaseline(%d) failed.` for an entity entering the visible set,
        // `CL_CopyExistingEntity: missing client entity %d.` for an ordinary delta, and
        // `CL_PreserveExistingEntity` for one that did not change. Only the middle one is a delta
        // against what the client already holds.
        //
        // **So an Enter forgets first.** Anything the baseline and the update both omit is at its
        // default, not at whatever this reader last accumulated — which is the rule
        // `docs/memory/sentinels-conflate-unknown-with-answer.md` states for the wire generally.
        //
        // Measured cost of not doing it: a `CTFBonesaw` last stated `m_iState 2` at tick 8060 was
        // still ACTIVE six thousand ticks and eight PVS transitions later, because its class has no
        // instance baseline at all — 68 of 363 do — and every `ENTER` carried the owner, the move
        // parent, the world model and the whole attribute list while saying nothing about the
        // state. Its owner drew a medigun and a melee weapon in the same hand.
        //
        // **The paragraphs above are B231 and they were RIGHT about what they measured.** Rebuilding
        // a door from the CLASS baseline does take the gate off its door — `CDynamicProp`'s
        // baseline declares `moveparent = 2097151`, which is `0x1FFFFF`, the invalid-handle
        // sentinel. What has changed since is that `EffectiveProperties` prefers the entity's OWN
        // stored baseline and falls back to the class one only when the snapshot named no slot for
        // it, so the door is described against itself and keeps its parent.
        IReadOnlyList<DecodedProperty> properties = entity.Properties;

        if (entity.UpdateType == EntityUpdateType.Enter)
        {
            properties = _baselines.EffectiveProperties(entity);
            state.Forget();
        }

        foreach (DecodedProperty property in properties)
        {
            // **Element-scoped properties key by PATH, because their flat name collides by
            // construction** (B234). Every element of a `SendPropUtlVectorDataTable` references the
            // same sub-table, so twenty attributes flatten to one `Table.Prop` name and the last
            // write wins — 50,447 properties in one demo sharing two keys, nineteen twentieths of
            // them silently discarded. The path — `…m_AttributeList.m_Attributes.001.m_iRawValue32`
            // — is the identity the wire actually has.
            //
            // Everything else keeps the flat key, deliberately: every accessor in this file's
            // consumers spells `DT_Table.m_Prop`, and no non-element key collides.
            state.Set(
                property.Definition.ElementScoped && property.Definition.Path.Length > 0
                    ? property.Definition.Path
                    : $"{property.Definition.OwnerTable}.{property.Definition.Property.Name}",
                property.Value);
        }

        // **After the loop and before anything can read it**, which is where the engine's receive
        // proxy sits. A tick-encoded offset that survives into the next packet is a number about
        // the wrong base, and this is the only moment the right one is in hand.
        state.NoteSimulationTick(PacketTick);
    }

    /// <summary>Looks up an entity's accumulated state.</summary>
    /// <param name="entityIndex">Slot in the entity table.</param>
    /// <param name="state">The state, when the slot is occupied.</param>
    /// <returns>Whether an entity occupies that slot.</returns>
    public bool TryGet(int entityIndex, [NotNullWhen(true)] out EntityState? state) =>
        _entities.TryGetValue(entityIndex, out state);

    /// <summary>Every entity currently held, in no particular order.</summary>
    public IEnumerable<EntityState> All => _entities.Values;

    /// <summary>Every entity of one class.</summary>
    /// <param name="className">Server class name to match exactly.</param>
    /// <returns>Matching entities.</returns>
    public IEnumerable<EntityState> OfClass(string className) =>
        _entities.Values.Where(
            state => string.Equals(state.ClassName, className, System.StringComparison.Ordinal));
}
