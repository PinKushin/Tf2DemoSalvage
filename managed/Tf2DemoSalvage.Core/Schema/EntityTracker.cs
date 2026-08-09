using System;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Core.Schema;

/// <summary>
/// Carries entity property values forward across snapshots.
/// </summary>
/// <remarks>
/// **A decoded snapshot answers "what changed this tick"; this answers "what is true now".**
/// Entity updates are deltas — a snapshot names only the properties that moved — so reading one
/// in isolation cannot say where a player is, only that they moved. Every use past a trace needs
/// the second question answered: a 2D viewer is exactly a query for every player's position at
/// an arbitrary tick.
///
/// State is keyed by owning table *and* property name. Bare names collide: real demos carry both
/// <c>DT_TFLocalPlayerExclusive.m_vecOrigin</c> and
/// <c>DT_TFNonLocalPlayerExclusive.m_vecOrigin</c>, holding different values for the same
/// player, and keying on the short name would silently merge them.
///
/// **Known gap: instance baselines are not implemented.** Source sends an entering entity as a
/// delta against its class's baseline rather than against nothing, so properties left at their
/// baseline value are absent from the wire and are therefore absent here. That makes this
/// complete for anything that only reads properties the demo actually transmits — positions,
/// health, angles — and incomplete for anything that must distinguish "unset" from "at its
/// default". Recorded rather than worked around, because guessing a default would produce
/// plausible wrong values, which is the failure mode this project keeps meeting.
/// </remarks>
public sealed class EntityTracker
{
    /// <summary>What is known about one entity slot.</summary>
    private sealed class Slot
    {
        public required int SerialNumber { get; set; }

        public required bool Visible { get; set; }

        public Dictionary<string, PropertyValue> Values { get; } =
            new(StringComparer.Ordinal);
    }

    private readonly Dictionary<int, Slot> _slots = [];

    /// <summary>Entities currently visible, in ascending slot order.</summary>
    /// <remarks>
    /// Visibility is not existence. An entity that leaves the potentially visible set is still
    /// alive on the server and will resume delta updates against the state it left with, so it
    /// keeps its values here and only drops out of this list.
    /// </remarks>
    public IReadOnlyList<int> ActiveEntities
    {
        get
        {
            List<int> active = [];
            foreach ((int index, Slot slot) in _slots)
            {
                if (slot.Visible)
                {
                    active.Add(index);
                }
            }

            active.Sort();
            return active;
        }
    }

    /// <summary>Everything known about an entity, or <c>null</c> if it is not tracked.</summary>
    /// <param name="entityIndex">Slot in the entity table.</param>
    /// <returns>The entity's properties, keyed <c>Table.Property</c>.</returns>
    public IReadOnlyDictionary<string, PropertyValue>? State(int entityIndex) =>
        _slots.TryGetValue(entityIndex, out Slot? slot) ? slot.Values : null;

    /// <summary>Applies one snapshot's worth of decoded entities.</summary>
    /// <param name="entities">The entities a snapshot described.</param>
    /// <exception cref="ArgumentNullException"><paramref name="entities"/> is <c>null</c>.</exception>
    public void Apply(IReadOnlyList<DecodedEntity> entities)
    {
        ArgumentNullException.ThrowIfNull(entities);

        foreach (DecodedEntity entity in entities)
        {
            if (entity.UpdateType == EntityUpdateType.Delete)
            {
                _slots.Remove(entity.EntityIndex);
                continue;
            }

            Slot slot = SlotFor(entity);

            if (entity.UpdateType == EntityUpdateType.Leave)
            {
                // Values are kept deliberately - see ActiveEntities.
                slot.Visible = false;
                continue;
            }

            slot.Visible = true;

            foreach (DecodedProperty property in entity.Properties)
            {
                slot.Values[Key(property)] = property.Value;
            }
        }
    }

    /// <summary>
    /// Finds the slot for an entity, discarding the previous occupant if the slot was reused.
    /// </summary>
    /// <remarks>
    /// Slots are recycled, and the serial number is the only thing distinguishing a new occupant
    /// from the entity that held the slot before. Merging into the old values would leave a dead
    /// player's properties visible on a live one — values that are real and plausible and wrong.
    /// </remarks>
    private Slot SlotFor(DecodedEntity entity)
    {
        if (_slots.TryGetValue(entity.EntityIndex, out Slot? existing))
        {
            if (existing.SerialNumber == entity.SerialNumber)
            {
                return existing;
            }

            _slots.Remove(entity.EntityIndex);
        }

        Slot created = new() { SerialNumber = entity.SerialNumber, Visible = true };
        _slots[entity.EntityIndex] = created;
        return created;
    }

    private static string Key(DecodedProperty property) =>
        string.Concat(property.Definition.OwnerTable, ".", property.Definition.Property.Name);
}
