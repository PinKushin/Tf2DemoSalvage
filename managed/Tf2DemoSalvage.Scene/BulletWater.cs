using System;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Scene;

/// <summary>`CTFPlayer::FireBullet`'s water test and `ImpactWaterTrace` for the client's own bullets (B415).</summary>
/// <remarks>
/// <code>
/// if ( !( GetPointContents( trace.startpos ) &amp; ( CONTENTS_WATER | CONTENTS_SLIME ) ) &amp;&amp;
///      ( GetPointContents( trace.endpos ) &amp; ( CONTENTS_WATER | CONTENTS_SLIME ) ) )
///     ImpactWaterTrace( trace, vecStart );     // a splash, and nothing else
/// else
///     // regular impact effects
/// </code>
/// `tf_player_shared.cpp:10513`. `ImpactWaterTrace` traces the same line against water as well and dispatches
/// `tf_gunshotsplash` (`tf_gunshotsplash_minigun` for a minigun) where it stops, which `TFSplashCallbackHelper` draws as the
/// particle system `water_bulletsplash01` (`_minigun`) at that point (`tf_fx_impacts.cpp:141`). `tf_impactwatertimeenable`
/// is "0", so no splash is rate-limited. *Interpolated:* the point the line enters water is found by halving the segment
/// on the leaves' contents, not by tracing the water brush's plane; it lands within a hundredth of a unit.
/// </remarks>
public static class BulletWater
{
    /// <summary>`TF_WEAPON_MINIGUN`.</summary>
    public const int MinigunId = 18;

    private const int Wet = 0x20 | 0x10;

    /// <summary>The splash's particle system — `tf_gunshotsplash`.</summary>
    public const string SplashSystem = "water_bulletsplash01";

    /// <summary>The minigun's — `tf_gunshotsplash_minigun`.</summary>
    public const string MinigunSplashSystem = "water_bulletsplash01_minigun";

    /// <summary>Both splashes' particle systems, loaded with the map.</summary>
    public static IReadOnlyList<string> Systems { get; } = [SplashSystem, MinigunSplashSystem];

    /// <summary>Marks each bullet that entered water with where it did and which splash it makes.</summary>
    /// <param name="impacts">The client's bullets, in fire order.</param>
    /// <param name="weaponOf">An impact's shooting weapon id.</param>
    /// <param name="contents">A point's contents on the world.</param>
    /// <returns>The impacts, those that entered water marked.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public static IReadOnlyList<ShotImpact> Mark(
        IReadOnlyList<ShotImpact> impacts, Func<ShotImpact, int> weaponOf, Func<float, float, float, int> contents)
    {
        ArgumentNullException.ThrowIfNull(impacts);
        ArgumentNullException.ThrowIfNull(weaponOf);
        ArgumentNullException.ThrowIfNull(contents);

        List<ShotImpact> marked = new(impacts.Count);

        foreach (ShotImpact impact in impacts)
        {
            (float X, float Y, float Z) start = impact.Start;
            (float X, float Y, float Z) end = impact.End;

            if (impact.Shot < 0 ||
                (contents(start.X, start.Y, start.Z) & Wet) != 0 ||
                (contents(end.X, end.Y, end.Z) & Wet) == 0)
            {
                marked.Add(impact);
                continue;
            }

            marked.Add(impact with
            {
                WaterEntry = Entry(start, end, contents),
                Splash = weaponOf(impact) == MinigunId ? MinigunSplashSystem : SplashSystem,
            });
        }

        return marked;
    }

    /// <summary>Where a dry-to-wet segment first meets water, halved until the step is under a hundredth of a unit.</summary>
    private static (float X, float Y, float Z) Entry(
        (float X, float Y, float Z) dry, (float X, float Y, float Z) wet, Func<float, float, float, int> contents)
    {
        for (int step = 0; step < 32; step++)
        {
            (float X, float Y, float Z) middle = ((dry.X + wet.X) * 0.5f, (dry.Y + wet.Y) * 0.5f, (dry.Z + wet.Z) * 0.5f);

            if ((contents(middle.X, middle.Y, middle.Z) & Wet) != 0)
            {
                wet = middle;
            }
            else
            {
                dry = middle;
            }

            float dx = wet.X - dry.X;
            float dy = wet.Y - dry.Y;
            float dz = wet.Z - dry.Z;

            if ((dx * dx) + (dy * dy) + (dz * dz) < 0.0001f)
            {
                break;
            }
        }

        return wet;
    }
}
