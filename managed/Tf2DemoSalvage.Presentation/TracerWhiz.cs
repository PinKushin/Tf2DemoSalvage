using System;
using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Audio;
using Tf2DemoSalvage.Core.Primitives;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Presentation;

/// <summary>
/// A tracer's near-miss whiz — `FX_TracerSound( vecStart, vecEnd, TRACER_TYPE_DEFAULT )`, which `ParticleTracerCallback`
/// runs for every TF tracer because `FireBullet` asks for the whiz (`fx_tracer.cpp:173`, `tf_player_shared.cpp:10605`).
/// </summary>
/// <remarks>
/// <code>
/// listener = MainViewOrigin(), lowered along LISTENER_HEIGHT (24) to where the bullet's ray passes it
/// if ( curtime &lt; m_nextWhizTime ) return;                       // one whiz per 0.1 s, for everything
/// if ( CalcDistanceSqrToLineSegment( listener, start, end ) &gt;= 24² ) return;
/// EmitSound( "Bullets.DefaultNearmiss" ) at start, CHAN_STATIC;  m_nextWhizTime = curtime + 0.1
/// </code>
/// </remarks>
public static class TracerWhiz
{
    /// <summary>The script played.</summary>
    public const string Sound = "Bullets.DefaultNearmiss";

    /// <summary>`g_BulletWhiz`'s wait before the next whiz, `RandomFloat( 0.1, 0.1 )`.</summary>
    public const double CooldownSeconds = 0.1d;

    /// <summary>`flWhizDist` for the default tracer.</summary>
    private const float Distance = 24f;

    /// <summary>`LISTENER_HEIGHT`.</summary>
    private const float ListenerHeight = 24f;

    /// <summary>The whiz itself, emitted from the tracer's start (`EmitSound( SOUND_FROM_WORLD, CHAN_STATIC, … &amp;start )`).</summary>
    /// <param name="tick">When the tracer is made.</param>
    /// <param name="at">The tracer's start.</param>
    /// <param name="scripts">Every soundscript entry the game loaded, by name.</param>
    /// <returns>The sound, or null when the script is not loaded.</returns>
    public static SceneSound? SoundAt(int tick, (float X, float Y, float Z) at, IReadOnlyDictionary<string, SoundScriptEntry> scripts)
    {
        ArgumentNullException.ThrowIfNull(scripts);

        if (!scripts.TryGetValue(Sound, out SoundScriptEntry entry) || entry.Waves.Count == 0)
        {
            return null;
        }

        UniformRandomStream random = new();
        random.SetSeed(ImpactSounds.SeedFor(tick));
        return ExplosionSounds.FromWorldAt(entry, random, tick, at);
    }

    /// <summary>Whether a tracer from <paramref name="start"/> to <paramref name="end"/> passes close enough to whiz.</summary>
    /// <param name="start">The tracer's start.</param>
    /// <param name="end">Its end.</param>
    /// <param name="eye">`MainViewOrigin()`.</param>
    /// <returns>True when it passes within 24 units of the listener.</returns>
    public static bool Hears(Vector3 start, Vector3 end, Vector3 eye)
    {
        // `IntersectRayWithRay( bullet, listener, s, t )`: where along the listener's downward segment the bullet's line
        // passes nearest, clamped to it.
        Vector3 bullet = end - start;
        Vector3 down = new(0f, 0f, -ListenerHeight);
        Vector3 across = start - eye;

        float a = Vector3.Dot(bullet, bullet);
        float b = Vector3.Dot(bullet, down);
        float c = Vector3.Dot(down, down);
        float d = Vector3.Dot(bullet, across);
        float e = Vector3.Dot(down, across);
        float denominator = (a * c) - (b * b);

        float t = denominator > 1e-6f ? ((a * e) - (b * d)) / denominator : 0f;
        Vector3 listener = eye + (down * Math.Clamp(t, 0f, 1f));

        // `CalcDistanceSqrToLineSegment( listener, start, end )`.
        float along = a > 0f ? Math.Clamp(Vector3.Dot(listener - start, bullet) / a, 0f, 1f) : 0f;

        return Vector3.DistanceSquared(listener, start + (bullet * along)) < Distance * Distance;
    }
}
