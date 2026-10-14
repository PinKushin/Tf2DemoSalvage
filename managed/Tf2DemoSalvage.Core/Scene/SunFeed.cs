using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Core.Schema;

namespace Tf2DemoSalvage.Core.Scene;

/// <summary>One <c>env_sun</c> as <c>C_Sun</c> holds it after receive (B378).</summary>
/// <param name="Entity">The entity, whose two overlays each own a pixel-visibility query.</param>
/// <param name="Direction"><c>m_vDirection</c>, pointing AT the sun.</param>
/// <param name="On"><c>m_bOn</c>: both overlays activated, or both deactivated.</param>
/// <param name="Size"><c>m_nSize</c>, the core's size.</param>
/// <param name="OverlaySize"><c>m_nOverlaySize</c>, the outer glow's.</param>
/// <param name="Render"><c>m_clrRender</c>'s red, green and blue.</param>
/// <param name="Overlay"><c>m_clrOverlay</c>'s red, green and blue; all zero means "the core's".</param>
/// <param name="Material">The core's material, the model path <c>m_nMaterial</c> names; null when unresolved.</param>
/// <param name="OverlayMaterial">The outer glow's, from <c>m_nOverlayMaterial</c>.</param>
/// <param name="HdrColorScale"><c>HDRColorScale</c>, which <c>CGlowOverlay::Draw</c> writes to <c>$hdrcolorscale</c>.</param>
public readonly record struct SceneSun(
    int Entity,
    (float X, float Y, float Z) Direction,
    bool On,
    int Size,
    int OverlaySize,
    (byte Red, byte Green, byte Blue) Render,
    (byte Red, byte Green, byte Blue) Overlay,
    string? Material,
    string? OverlayMaterial,
    float HdrColorScale);

/// <summary>Every <c>env_sun</c> the client holds, tick by tick (B378).</summary>
/// <remarks>
/// **A feed rather than a prop track, because a sun has no model and no position.** <c>DT_Sun</c> is NOBASE
/// (`c_sun.cpp:21-33`, `sun.cpp:54-64`): no origin, no model index, a direction and two material indices. It is
/// <c>FL_EDICT_ALWAYS</c> (`sun.cpp:198-201`), like the tonemap controller this is shaped after.
///
/// **Colours are the engine's packing, red in the HIGH byte.** <c>SendProxy_Color32ToInt</c> writes
/// <c>r &lt;&lt; 24 | g &lt;&lt; 16 | b &lt;&lt; 8 | a</c> (`sendproxy.cpp:18-22`) and <c>RecvProxy_IntToColor32</c> reads it back
/// that way (`recvproxy.cpp:15-24`). Measured: koth_lakeside_final's sun sends <c>m_clrRender = 3838491391</c>,
/// <c>0xE4CABAFF</c>, for the map's <c>rendercolor "228 202 186"</c>. **Not <see cref="EntityState.RenderRgb"/>'s
/// packing, and both are right**: <c>DT_BaseEntity</c> names no proxy (`baseentity.cpp:279`), so it is a raw copy of the
/// struct with red LOW (`dt_send.cpp:157-160`; lakeside's sprites send <c>0x7D248CFF</c> for 255 140 36 at 125).
/// </remarks>
public sealed class SunFeed
{
    /// <summary>The sun's server class.</summary>
    public const string ClassName = "CSun";

    /// <summary><c>DT_Sun</c>.</summary>
    public const string Table = "DT_Sun";

    private readonly Dictionary<int, (EntityState State, int Material, int OverlayMaterial)> _suns = [];

    private readonly List<(int Tick, SceneSun[] Suns)> _samples = [];

    private bool _changed;

    /// <summary>Every change, in tick order.</summary>
    public IReadOnlyList<(int Tick, SceneSun[] Suns)> Samples => _samples;

    /// <summary>Applies one snapshot entry.</summary>
    /// <param name="entityIndex">The entity.</param>
    /// <param name="entity">Its state after the update, or null.</param>
    /// <param name="update">What the snapshot said.</param>
    /// <remarks>
    /// A leave is not a destruction: the overlays live in <c>g_GlowOverlaySystem</c>'s list and only
    /// <c>m_bOn</c> or the destructor takes one out (`glow_overlay.cpp:116-119`, `c_sun.cpp:127-137`).
    /// </remarks>
    public void Observe(int entityIndex, EntityState? entity, EntityUpdateType update)
    {
        if (update == EntityUpdateType.Delete)
        {
            _changed |= _suns.Remove(entityIndex);

            return;
        }

        if (update == EntityUpdateType.Leave || entity?.ClassName != ClassName)
        {
            return;
        }

        _suns[entityIndex] = (entity, entity.Integer($"{Table}.m_nMaterial") ?? 0, entity.Integer($"{Table}.m_nOverlayMaterial") ?? 0);
        _changed = true;
    }

    /// <summary>Records the suns after a packet, on change.</summary>
    /// <param name="tick">The packet's tick.</param>
    /// <param name="modelPath">A model index to its precached path — <c>modelinfo-&gt;GetModelName</c>.</param>
    public void Sample(int tick, Func<int, string?> modelPath)
    {
        ArgumentNullException.ThrowIfNull(modelPath);

        if (!_changed)
        {
            return;
        }

        _changed = false;

        SceneSun[] suns = [.. _suns.OrderBy(pair => pair.Key).Select(pair => Read(pair.Key, pair.Value.State, pair.Value.Material, pair.Value.OverlayMaterial, modelPath))];

        if (_samples.Count == 0 ? suns.Length > 0 : !_samples[^1].Suns.SequenceEqual(suns))
        {
            _samples.Add((tick, suns));
        }
    }

    /// <summary>The suns at a tick; none before the first sample.</summary>
    /// <param name="tick">The tick being drawn.</param>
    /// <returns>The last sample at or before the tick.</returns>
    public IReadOnlyList<SceneSun> At(int tick)
    {
        IReadOnlyList<SceneSun> found = [];

        foreach ((int at, SceneSun[] suns) in _samples)
        {
            if (at > tick)
            {
                break;
            }

            found = suns;
        }

        return found;
    }

    /// <summary><c>RecvProxy_IntToColor32</c>'s three colour bytes.</summary>
    /// <param name="packed">The 32-bit field as decoded.</param>
    /// <returns>Red from the high byte, then green and blue.</returns>
    /// <remarks>Absent is black, the client's zeroed member — not white: the server always sends it.</remarks>
    public static (byte Red, byte Green, byte Blue) Color32(int? packed) =>
        packed is { } value
            ? ((byte)((uint)value >> 24), (byte)((value >> 16) & 0xFF), (byte)((value >> 8) & 0xFF))
            : ((byte)0, (byte)0, (byte)0);

    private static SceneSun Read(int index, EntityState state, int material, int overlayMaterial, Func<int, string?> modelPath) =>
        new(
            index,
            // An absent field is the client's zeroed member; `CSun` sends every one on entering.
            state.Vector($"{Table}.m_vDirection") ?? (0f, 0f, 0f),
            state.Integer($"{Table}.m_bOn") is 1,
            state.Integer($"{Table}.m_nSize") ?? 0,
            state.Integer($"{Table}.m_nOverlaySize") ?? 0,
            Color32(state.Integer($"{Table}.m_clrRender")),
            Color32(state.Integer($"{Table}.m_clrOverlay")),
            material != 0 ? modelPath(material) : null,
            overlayMaterial != 0 ? modelPath(overlayMaterial) : null,
            state.Number($"{Table}.HDRColorScale") ?? 1f);
}
