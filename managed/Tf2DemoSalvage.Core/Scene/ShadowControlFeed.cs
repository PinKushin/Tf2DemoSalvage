using System.Collections.Generic;

using Tf2DemoSalvage.Core.Schema;

namespace Tf2DemoSalvage.Core.Scene;

/// <summary>What a <c>shadow_control</c> told the client shadow manager — the four setters its data change calls.</summary>
/// <param name="Direction"><c>SetShadowDirection</c>'s argument, unnormalised as sent.</param>
/// <param name="Color"><c>SetShadowColor</c>'s red, green and blue.</param>
/// <param name="Distance"><c>SetShadowDistance</c>: the cast distance.</param>
/// <param name="Disabled"><c>SetShadowsDisabled</c>.</param>
public readonly record struct SceneShadowControl(
    (float X, float Y, float Z) Direction, (byte Red, byte Green, byte Blue) Color, float Distance, bool Disabled);

/// <summary>Which <c>shadow_control</c> values the client holds, tick by tick (B702, finding 107).</summary>
/// <remarks>
/// **Every data change writes all four globals** — <c>C_ShadowControl::OnDataChanged</c> (`c_shadowcontrol.cpp:50-57`) — and
/// **nothing undoes them**: the class has no destructor, so a deleted controller's values stay. Before any controller the
/// client has its own: direction (0.1, 0.1, −1), distance 50 (`clientshadowmgr.cpp:1287-1289`), and a colour from the
/// level's ambient light (`:1513-1526`), which is the caller's to supply — <see cref="At"/> answers null then.
///
/// **The colour is <c>m_shadowColor</c> as a 32-bit int**, red in the low byte: <c>SendPropInt( SENDINFO(m_shadowColor),
/// 32, SPROP_UNSIGNED )</c> copies the <c>color32</c>'s memory (`shadowcontrol.cpp:63-68`).
/// </remarks>
public sealed class ShadowControlFeed
{
    /// <summary>The controller's server class.</summary>
    public const string ClassName = "CShadowControl";

    /// <summary><c>DT_ShadowControl</c> (`c_shadowcontrol.cpp:37-42`).</summary>
    public const string Table = "DT_ShadowControl";

    /// <summary>The four fields the receive table declares.</summary>
    internal static readonly string[] Properties = ["m_shadowDirection", "m_shadowColor", "m_flShadowMaxDist", "m_bDisableShadows"];

    private readonly List<(int Tick, SceneShadowControl Control)> _samples = [];

    private SceneShadowControl? _current;

    /// <summary>Every change, in tick order.</summary>
    public IReadOnlyList<(int Tick, SceneShadowControl Control)> Samples => _samples;

    /// <summary>Applies one snapshot entry, as <c>OnDataChanged</c> would.</summary>
    /// <param name="entity">The entity's state after the update, or null.</param>
    /// <param name="update">What the snapshot said.</param>
    public void Observe(EntityState? entity, EntityUpdateType update)
    {
        if (update is EntityUpdateType.Delete or EntityUpdateType.Leave || entity?.ClassName != ClassName)
        {
            return;
        }

        int colour = entity.Integer($"{Table}.{Properties[1]}") ?? 0;

        _current = new SceneShadowControl(
            entity.Vector($"{Table}.{Properties[0]}") ?? (0f, 0f, 0f),
            ((byte)colour, (byte)(colour >> 8), (byte)(colour >> 16)),
            entity.Number($"{Table}.{Properties[2]}") ?? 0f,
            entity.Integer($"{Table}.{Properties[3]}") is 1);
    }

    /// <summary>Records the globals after a packet, on change.</summary>
    /// <param name="tick">The packet's tick.</param>
    public void Sample(int tick)
    {
        if (_current is { } current && (_samples.Count == 0 || _samples[^1].Control != current))
        {
            _samples.Add((tick, current));
        }
    }

    /// <summary>The controller's values at a tick.</summary>
    /// <param name="tick">The tick being drawn.</param>
    /// <returns>The last sample at or before the tick; null before the first, when the client's own defaults stand.</returns>
    public SceneShadowControl? At(int tick)
    {
        SceneShadowControl? found = null;

        foreach ((int at, SceneShadowControl control) in _samples)
        {
            if (at > tick)
            {
                break;
            }

            found = control;
        }

        return found;
    }
}
