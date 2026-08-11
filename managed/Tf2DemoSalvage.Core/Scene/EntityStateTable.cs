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

        if (!_entities.TryGetValue(entity.EntityIndex, out EntityState? state) ||
            state.SerialNumber != entity.SerialNumber)
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

        foreach (DecodedProperty property in entity.Properties)
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
