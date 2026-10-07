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

    /// <summary>Notes what one snapshot said of one entity.</summary>
    /// <param name="entityIndex">The entity.</param>
    /// <param name="update">What the snapshot said.</param>
    /// <param name="tick">The tick it arrived on.</param>
    public void Observe(int entityIndex, EntityUpdateType update, int tick)
    {
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
