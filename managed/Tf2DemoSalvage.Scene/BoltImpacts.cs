using System;
using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene;

/// <summary>An arrow a bolt left standing: a temp model with no physics and a fixed life.</summary>
/// <param name="Index">Its dispatch's index in the feed.</param>
/// <param name="Tick">The tick it appears.</param>
/// <param name="Model">Its model.</param>
/// <param name="At">Where it stands: the impact, set back along the shot.</param>
/// <param name="Pitch">`VectorAngles( dir )`'s pitch.</param>
/// <param name="Yaw">`VectorAngles( dir )`'s yaw.</param>
/// <param name="Skin">`m_nColor`, which picks the team's skin.</param>
/// <param name="Scale">`SetModelScale`.</param>
/// <param name="Life">Seconds until `die`.</param>
public readonly record struct StuckArrow(
    int Index, int Tick, string Model, (float X, float Y, float Z) At, float Pitch, float Yaw, int Skin, float Scale, float Life);

/// <summary>A crossbow bolt's impact on the world — `StickyBoltCallbackTF` (B415).</summary>
/// <remarks>
/// <code>
/// // c_tf_stickybolt.cpp:106 — StickRagdollNowTF( m_vOrigin, m_vNormal, … )
/// UTIL_TraceLine( origin − dir · 16, origin + dir · 64, MASK_SOLID_BRUSHONLY, … )
/// if ( tr.surface.flags &amp; SURF_SKY ) return
/// … pin the struck ragdoll's bone …
/// UTIL_ImpactTrace( &amp;tr, 0 )                          // nothing on a fraction of 1
/// CreateCrossbowBoltTF( origin, dir, m_fFlags, m_nColor )  // the arrow, a 30-second temp model
/// </code>
/// **The impact is a client trace against brushes**, so its surface is the traced texinfo's, as a hitscan bullet's is,
/// and no player is judged. The arrow itself is <see cref="Stuck"/>. *Not built:* the ragdoll pin.
/// </remarks>
public static class BoltImpacts
{
    /// <summary>`g_pszArrowModels`, in `arrow_models` order (`tf_shareddefs.cpp:1142`) — every one precached with the weapons.</summary>
    public static IReadOnlyList<string> ArrowModels { get; } =
    [
        "models/weapons/w_models/w_arrow.mdl",
        "models/weapons/w_models/w_repair_claw.mdl",
        "models/weapons/w_models/w_baseball.mdl",
        "models/weapons/w_models/w_arrow_xmas.mdl",
        "models/weapons/w_models/w_syringe_proj.mdl",
        "models/workshop/weapons/c_models/c_crusaders_crossbow/c_crusaders_crossbow_xmas_proj.mdl",
        "models/weapons/w_models/w_breadmonster/w_breadmonster.mdl",
        "models/weapons/c_models/c_grapple_proj/c_grapple_proj.mdl",
        "models/workshop_partner/weapons/c_models/c_sd_cleaver/c_sd_cleaver.mdl",
    ];

    /// <summary>`CreateCrossbowBoltTF`'s table (`c_tf_stickybolt.cpp:42`): a projectile type's model, set-back, scale and life.</summary>
    /// <param name="projectile">`m_fFlags`, a `ProjectileType_t`.</param>
    /// <returns>The arrow's model and placement, or null for a type the switch does not list (its `default` returns).</returns>
    public static (string Model, float Back, float Scale, float Life)? ArrowFor(int projectile) => projectile switch
    {
        16 => (ArrowModels[2], 5f, 1f, 30f),  // TF_PROJECTILE_STICKY_BALL: MODEL_SNOWBALL
        8 => (ArrowModels[0], 5f, 1f, 30f),   // TF_PROJECTILE_ARROW
        18 => (ArrowModels[1], -2f, 1f, 30f), // TF_PROJECTILE_BUILDING_REPAIR_BOLT
        19 => (ArrowModels[3], 5f, 1f, 30f),  // TF_PROJECTILE_FESTIVE_ARROW
        11 => (ArrowModels[4], 0f, 3f, 30f),  // TF_PROJECTILE_HEALING_BOLT: MODEL_SYRINGE
        23 => (ArrowModels[5], 5f, 2.5f, 30f), // TF_PROJECTILE_FESTIVE_HEALING_BOLT
        28 or 24 or 25 => (ArrowModels[6], 5f, 2.5f, 8f), // BREAD_MONSTER, BREADMONSTER_JARATE, _MADMILK
        26 => (ArrowModels[7], 0f, 1f, 0.1f), // TF_PROJECTILE_GRAPPLINGHOOK
        _ => null,
    };

    /// <summary>Every arrow a bolt leaves standing — `CreateCrossbowBoltTF`'s temp model (`c_tf_stickybolt.cpp:94`).</summary>
    /// <param name="dispatches">The dispatch feed, in tick order.</param>
    /// <param name="name">A dispatch's name by its index in the <c>EffectDispatch</c> table.</param>
    /// <param name="hitsSky">Whether the trace from <c>origin − dir · 16</c> to <c>origin + dir · 64</c> struck sky.</param>
    /// <returns>The arrows, in tick order.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// <code>
    /// if ( tr.surface.flags &amp; SURF_SKY ) return             // before anything; a miss does NOT return
    /// SpawnTempModel( model, origin − dir · offset, VectorAngles( dir ), 0 velocity, life, FTENT_NONE )
    /// SetModelScale( scale );  m_nSkin = m_nColor
    /// </code>
    /// **FTENT_NONE**: no gravity, no fade — the model stands until `die` and is gone.
    /// </remarks>
    public static IReadOnlyList<StuckArrow> Stuck(
        IReadOnlyList<SceneEffectDispatch> dispatches,
        Func<int, string?> name,
        Func<(float X, float Y, float Z), (float X, float Y, float Z), bool> hitsSky)
    {
        ArgumentNullException.ThrowIfNull(dispatches);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(hitsSky);

        List<StuckArrow> arrows = [];

        for (int index = 0; index < dispatches.Count; index++)
        {
            SceneEffectDispatch dispatch = dispatches[index];

            if (!string.Equals(name(dispatch.Name), BoltEffect, StringComparison.Ordinal) ||
                ArrowFor(dispatch.Flags) is not { } arrow)
            {
                continue;
            }

            Vector3 origin = new(dispatch.Origin.X, dispatch.Origin.Y, dispatch.Origin.Z);
            Vector3 direction = new(dispatch.Normal.X, dispatch.Normal.Y, dispatch.Normal.Z);
            Vector3 start = origin - (direction * 16f);
            Vector3 reach = origin + (direction * 64f);

            if (hitsSky((start.X, start.Y, start.Z), (reach.X, reach.Y, reach.Z)))
            {
                continue;
            }

            Vector3 at = origin - (direction * arrow.Back);

            // `VectorAngles( forward, angles )`: yaw from x and y, pitch the negated elevation, no roll.
            float yaw = direction.X == 0f && direction.Y == 0f ? 0f : float.RadiansToDegrees(MathF.Atan2(direction.Y, direction.X));
            float pitch = float.RadiansToDegrees(
                MathF.Atan2(-direction.Z, MathF.Sqrt((direction.X * direction.X) + (direction.Y * direction.Y))));

            arrows.Add(new StuckArrow(index, dispatch.Tick, arrow.Model, (at.X, at.Y, at.Z), pitch, yaw, dispatch.Colour, arrow.Scale, arrow.Life));
        }

        return arrows;
    }

    /// <summary>Where the arrows' entity indices start: past the corpses' gibs, free of every networked index.</summary>
    public const int FirstArrowEntityIndex = 7168;

    /// <summary>How many arrows can be told apart at once; an index is reused after this many.</summary>
    private const int ArrowSlots = 1024;

    /// <summary>Adds every arrow standing at a tick to a frame's props.</summary>
    /// <param name="arrows">The demo's arrows, in tick order.</param>
    /// <param name="tick">The tick drawn.</param>
    /// <param name="intervalPerTick">Seconds per tick.</param>
    /// <param name="into">The frame's props.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// Standing from its tick until `die = curtime + life`, and gone after — `FTENT_NONE` has no fade.
    /// ponytail: a scan over every arrow per frame, and indices reused every 1,024; an interval index if a demo ever
    /// carries tens of thousands.
    /// </remarks>
    public static void Fill(IReadOnlyList<StuckArrow> arrows, double tick, float intervalPerTick, ICollection<SceneProp> into)
    {
        ArgumentNullException.ThrowIfNull(arrows);
        ArgumentNullException.ThrowIfNull(into);

        foreach (StuckArrow arrow in arrows)
        {
            double age = (tick - arrow.Tick) * intervalPerTick;

            if (age < 0d || age >= arrow.Life)
            {
                continue;
            }

            into.Add(new SceneProp(
                FirstArrowEntityIndex + (arrow.Index % ArrowSlots),
                arrow.Model,
                SceneModelKind.Studio,
                new ScenePose
                {
                    X = arrow.At.X,
                    Y = arrow.At.Y,
                    Z = arrow.At.Z,
                    Pitch = arrow.Pitch,
                    Yaw = arrow.Yaw,
                    Scale = arrow.Scale,
                    Skin = arrow.Skin,
                },
                FirstTick: arrow.Tick));
        }
    }

    /// <summary>The effect name `CTFProjectile_Arrow` dispatches.</summary>
    public const string BoltEffect = "TFBoltImpact";

    /// <summary>Where a bolt's `Shot` numbers start, below every server impact's `−1 − index`.</summary>
    private const int FirstShot = -1_000_000;

    /// <summary>Every bolt's impact on the world's brushes.</summary>
    /// <param name="dispatches">The dispatch feed, in tick order.</param>
    /// <param name="name">A dispatch's name by its index in the <c>EffectDispatch</c> table.</param>
    /// <param name="trace">The world trace against brushes.</param>
    /// <param name="into">Added to, in tick order.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public static void From(
        IReadOnlyList<SceneEffectDispatch> dispatches,
        Func<int, string?> name,
        Func<(float X, float Y, float Z), (float X, float Y, float Z), BspTrace> trace,
        ICollection<ShotImpact> into)
    {
        ArgumentNullException.ThrowIfNull(dispatches);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(trace);
        ArgumentNullException.ThrowIfNull(into);

        for (int index = 0; index < dispatches.Count; index++)
        {
            SceneEffectDispatch dispatch = dispatches[index];

            if (!string.Equals(name(dispatch.Name), BoltEffect, StringComparison.Ordinal))
            {
                continue;
            }

            Vector3 origin = new(dispatch.Origin.X, dispatch.Origin.Y, dispatch.Origin.Z);
            Vector3 direction = new(dispatch.Normal.X, dispatch.Normal.Y, dispatch.Normal.Z);
            Vector3 start = origin - (direction * 16f);
            Vector3 reach = origin + (direction * 64f);
            BspTrace hit = trace((start.X, start.Y, start.Z), (reach.X, reach.Y, reach.Z));

            if (hit.Fraction >= 1f)
            {
                continue;
            }

            Vector3 end = start + ((reach - start) * hit.Fraction);

            into.Add(new ShotImpact(
                FirstShot - index, 0, dispatch.Tick, -1, 0, (start.X, start.Y, start.Z), (end.X, end.Y, end.Z),
                (reach.X, reach.Y, reach.Z), hit.Texinfo, hit.Normal, BrushOnly: true));
        }
    }
}
