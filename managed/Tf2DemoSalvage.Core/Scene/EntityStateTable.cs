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

        bool created = false;

        if (!_entities.TryGetValue(entity.EntityIndex, out EntityState? state) ||
            (statesSerial && state.SerialNumber != entity.SerialNumber))
        {
            created = true;

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
        IReadOnlyList<DecodedProperty> properties = created
            ? _baselines.EffectiveProperties(entity)
            : entity.Properties;

        foreach (DecodedProperty property in properties)
        {
            state.Set(
                $"{property.Definition.OwnerTable}.{property.Definition.Property.Name}",
                property.Value);
        }
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
