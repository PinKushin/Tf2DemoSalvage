using System.Collections.Generic;

using Tf2DemoSalvage.Core.Schema;

namespace Tf2DemoSalvage.Core.Scene;

/// <summary>The mapmaker's auto-exposure and bloom overrides in force, <c>viewpostprocess.cpp</c>'s globals (B514).</summary>
/// <param name="UseMin"><c>g_bUseCustomAutoExposureMin</c>.</param>
/// <param name="Min"><c>g_flCustomAutoExposureMin</c>.</param>
/// <param name="UseMax"><c>g_bUseCustomAutoExposureMax</c>.</param>
/// <param name="Max"><c>g_flCustomAutoExposureMax</c>.</param>
/// <param name="UseBloom"><c>g_bUseCustomBloomScale</c>.</param>
/// <param name="Bloom"><c>g_flCustomBloomScale</c>.</param>
/// <remarks>The default — every flag false — is the client before any controller: the cvars decide.</remarks>
public readonly record struct SceneTonemap(bool UseMin, float Min, bool UseMax, float Max, bool UseBloom, float Bloom);

/// <summary>Which <c>env_tonemap_controller</c>'s values the client holds, tick by tick (B514).</summary>
/// <remarks>
/// **The globals are written by whichever controller's data changed LAST** — <c>C_EnvTonemapController::OnDataChanged</c>
/// copies all seven fields and claims <c>g_hTonemapControllerInUse</c> (`c_env_tonemap_controller.cpp:83-96`) — and
/// **the destructor of the controller in use clears the three flags but not the values** (`:70-78`). So a round restart
/// that deletes the controller a <c>logic_auto</c> configured and creates a fresh one with all zeros hands exposure back
/// to <c>mat_autoexposure_min/max</c>: f12 (cp_process_f12) carries one controller at 0.5-0.7 from tick 1 and six
/// all-zero ones after it (`entity-census`).
///
/// A snapshot naming the entity stands for <c>OnDataChanged</c>; an entity whose update carried no field the client
/// tracks would also run it, and that cannot be told apart on the wire — both write the same values.
/// </remarks>
public sealed class TonemapFeed
{
    /// <summary>The controller's server class.</summary>
    public const string ClassName = "CEnvTonemapController";

    /// <summary><c>DT_EnvTonemapController</c> (`c_env_tonemap_controller.cpp:43-51`).</summary>
    public const string Table = "DT_EnvTonemapController";

    /// <summary>The seven fields the receive table declares, of which the six read here.</summary>
    internal static readonly string[] Properties =
    [
        "m_bUseCustomAutoExposureMin", "m_bUseCustomAutoExposureMax", "m_bUseCustomBloomScale",
        "m_flCustomAutoExposureMin", "m_flCustomAutoExposureMax", "m_flCustomBloomScale",
    ];

    private readonly List<(int Tick, SceneTonemap Tonemap)> _samples = [];

    private SceneTonemap _current;

    private int? _inUse;

    /// <summary>Every change, in tick order.</summary>
    public IReadOnlyList<(int Tick, SceneTonemap Tonemap)> Samples => _samples;

    /// <summary>Applies one snapshot entry to the globals, as the client's entity callbacks would.</summary>
    /// <param name="entityIndex">The entity.</param>
    /// <param name="entity">Its state after the update, or null.</param>
    /// <param name="update">What the snapshot said.</param>
    public void Observe(int entityIndex, EntityState? entity, EntityUpdateType update)
    {
        if (update == EntityUpdateType.Delete)
        {
            if (_inUse == entityIndex)
            {
                _current = _current with { UseMin = false, UseMax = false, UseBloom = false };
                _inUse = null;
            }

            return;
        }

        if (update == EntityUpdateType.Leave || entity?.ClassName != ClassName)
        {
            return;
        }

        _current = new SceneTonemap(
            entity.Integer($"{Table}.{Properties[0]}") is 1,
            entity.Number($"{Table}.{Properties[3]}") ?? 0f,
            entity.Integer($"{Table}.{Properties[1]}") is 1,
            entity.Number($"{Table}.{Properties[4]}") ?? 0f,
            entity.Integer($"{Table}.{Properties[2]}") is 1,
            entity.Number($"{Table}.{Properties[5]}") ?? 0f);
        _inUse = entityIndex;
    }

    /// <summary>Records the globals after a packet, on change.</summary>
    /// <param name="tick">The packet's tick.</param>
    public void Sample(int tick)
    {
        if (_samples.Count == 0 ? _current != default : _samples[^1].Tonemap != _current)
        {
            _samples.Add((tick, _current));
        }
    }

    /// <summary>The overrides at a tick; the default (none) before the first sample.</summary>
    /// <param name="tick">The tick being drawn.</param>
    /// <returns>The last sample at or before the tick.</returns>
    public SceneTonemap At(int tick)
    {
        SceneTonemap found = default;

        foreach ((int at, SceneTonemap tonemap) in _samples)
        {
            if (at > tick)
            {
                break;
            }

            found = tonemap;
        }

        return found;
    }
}
