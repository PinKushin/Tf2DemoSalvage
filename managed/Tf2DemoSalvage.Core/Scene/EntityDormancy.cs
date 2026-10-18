using System.Collections.Generic;

using Tf2DemoSalvage.Core.Schema;

namespace Tf2DemoSalvage.Core.Scene;

/// <summary>
/// When each entity was dormant — out of the recorder's PVS but still on the client (B478).
/// </summary>
/// <remarks>
/// **A snapshot says it**: <c>LeavePVS</c> makes the client entity dormant and keeps it, and <c>EnterPVS</c> or a delete
/// ends that (`EntityUpdateType`). A track keeps drawing its last pose through the gap; this is the separate answer the
/// engine's <c>IsDormant()</c> gives, read where a rule asks for it — the rope's <c>DrawModel</c> (`c_rope.cpp:1462-1467`).
/// </remarks>
public sealed class EntityDormancy
{
    private readonly Dictionary<int, List<(int From, int To)>> _spans = [];
    private readonly Dictionary<int, List<int>> _deletes = [];
    private readonly Dictionary<int, int> _serials = [];

    /// <summary>How many ends the entity index has had, for a test or a diagnostic.</summary>
    /// <param name="entityIndex">The entity index.</param>
    /// <returns>The count of recorded deletes.</returns>
    public int DeleteCount(int entityIndex) => _deletes.TryGetValue(entityIndex, out List<int>? deletes) ? deletes.Count : 0;

    /// <summary>The first tick after <paramref name="tick"/> the entity at this index was deleted, or null — the end of its life.</summary>
    /// <param name="entityIndex">The entity index.</param>
    /// <param name="tick">The tick to look after; a delete AT it is not after it (see the conformance test).</param>
    /// <returns>The delete's tick; an index used again after it is another entity.</returns>
    public int? DeletedAfter(int entityIndex, int tick)
    {
        if (_deletes.TryGetValue(entityIndex, out List<int>? deletes))
        {
            foreach (int at in deletes)
            {
                if (at > tick)
                {
                    return at;
                }
            }
        }

        return null;
    }

    /// <summary>Notes what one snapshot said of one entity.</summary>
    /// <param name="entityIndex">The entity.</param>
    /// <param name="update">What the snapshot said.</param>
    /// <param name="tick">The tick it arrived on.</param>
    /// <param name="serial">Its serial number when the snapshot sent one; an enter with a new serial is a new entity in an old slot.</param>
    public void Observe(int entityIndex, EntityUpdateType update, int tick, int serial = -1)
    {
        // **A slot reused without a delete between is still the old entity's end** — the serial says which entity it is.
        if (update == EntityUpdateType.Enter && serial >= 0)
        {
            if (_serials.TryGetValue(entityIndex, out int was) && was != serial)
            {
                Observe(entityIndex, EntityUpdateType.Delete, tick);
            }

            _serials[entityIndex] = serial;
        }

        if (update == EntityUpdateType.Delete)
        {
            if (!_deletes.TryGetValue(entityIndex, out List<int>? deletes))
            {
                _deletes[entityIndex] = deletes = [];
            }

            // Once a tick: a snapshot's delete and the same slot entered under a new serial are one end.
            if (deletes.Count == 0 || deletes[^1] != tick)
            {
                deletes.Add(tick);
            }
        }

        if (update == EntityUpdateType.Leave)
        {
            if (!_spans.TryGetValue(entityIndex, out List<(int From, int To)>? spans))
            {
                _spans[entityIndex] = spans = [];
            }

            if (spans.Count == 0 || spans[^1].To != int.MaxValue)
            {
                spans.Add((tick, int.MaxValue));
            }

            return;
        }

        if (update is EntityUpdateType.Enter or EntityUpdateType.Delete &&
            _spans.TryGetValue(entityIndex, out List<(int From, int To)>? open) &&
            open.Count > 0 && open[^1].To == int.MaxValue)
        {
            open[^1] = (open[^1].From, tick);
        }
    }

    /// <summary>Whether an entity was dormant at a tick.</summary>
    /// <param name="entityIndex">The entity.</param>
    /// <param name="tick">The tick.</param>
    /// <returns>True from its leave up to, not including, its next enter or delete.</returns>
    public bool IsDormant(int entityIndex, int tick)
    {
        if (!_spans.TryGetValue(entityIndex, out List<(int From, int To)>? spans))
        {
            return false;
        }

        foreach ((int from, int to) in spans)
        {
            if (tick >= from && tick < to)
            {
                return true;
            }
        }

        return false;
    }
}
