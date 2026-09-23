using System;
using System.Collections.Generic;
using System.Numerics;

namespace Tf2DemoSalvage.Scene;

/// <summary>
/// TF2's impact-sound grouping for one shot: `FX_FireBullets` routes every pellet's impact sound through
/// `ImpactSoundGroup` between `StartGroupingSounds` and `EndGroupingSounds` (`tf_fx_shared.cpp:40-99`), which plays a
/// sound only if the same one has not already played within 300 units in that shot. A scattergun blast into a wall is
/// one concrete sound, not ten.
/// </summary>
public sealed class ShotSoundGroup
{
    /// <summary>`300.0f`, squared as the engine compares it.</summary>
    private const float Radius = 300f;

    private readonly List<(string Name, Vector3 At)> _played = [];
    private int _shot = int.MinValue;

    /// <summary>Begins a shot's group, or continues it when the shot is the one already open.</summary>
    /// <param name="shot">The shot's identity; a different one purges the list (`EndGroupingSounds`).</param>
    public void Start(int shot)
    {
        if (shot != _shot)
        {
            _shot = shot;
            _played.Clear();
        }
    }

    /// <summary>Whether this impact sound plays, recording it when it does.</summary>
    /// <param name="name">The surface's bullet-impact sound, as the script names it (compared as `Q_stricmp` does).</param>
    /// <param name="at">Where it lands.</param>
    /// <returns>False when the same sound already played within 300 units in this shot.</returns>
    public bool Allows(string name, Vector3 at)
    {
        ArgumentNullException.ThrowIfNull(name);

        foreach ((string played, Vector3 where) in _played)
        {
            if (Vector3.DistanceSquared(at, where) < Radius * Radius &&
                string.Equals(played, name, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        _played.Add((name, at));
        return true;
    }
}
