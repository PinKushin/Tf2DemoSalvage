using System;
using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Scene;

/// <summary>Where a line met a static prop.</summary>
/// <param name="Fraction">How far along the line.</param>
/// <param name="Normal">The struck face's outward normal.</param>
/// <param name="SurfaceProp">The prop model's own `$surfaceprop`, as a surface index — `trace.surface.surfaceProps`.</param>
public readonly record struct StaticPropHit(float Fraction, Vector3 Normal, int SurfaceProp);

/// <summary>The map's solid static props as a line trace meets them.</summary>
/// <remarks>
/// engine.dll `ClipRayToCollideable` (`0x18018f510`): a `SOLID_VPHYSICS` (6) collideable is traced against its vcollide's first
/// solid (`physcollision` slot `0xf8`), placed at the prop's origin and angles. When the hit is on a studio model, the surface
/// becomes `**studio**` with `GetSurfaceIndex( studiohdr->pszSurfaceProp() )` (`studiohdr+0x134`), the MDL's `$surfaceprop`, not the
/// `.phy`'s. A static prop has `CONTENTS_SOLID` and `CTraceFilterSimple` passes it, so bullets meet props as they meet brushes.
/// A line against a convex ledge is the plane clip over its triangles, which `TraceBox` answers for a zero-extent ray.
/// *Not carried:* `SOLID_BBOX` (2) props, a swept box against a prop, and decals on a prop.
/// </remarks>
public sealed class StaticPropCollision
{
    /// <summary>`SOLID_VPHYSICS`.</summary>
    private const int SolidVphysics = 6;

    private readonly Prop[] _props;

    private StaticPropCollision(Prop[] props) => _props = props;

    /// <summary>No props, for a map with none or without an install to read their models from.</summary>
    public static StaticPropCollision Empty { get; } = new([]);

    /// <summary>How many props a line can meet.</summary>
    public int Count => _props.Length;

    /// <summary>Builds the traceable hulls of every solid prop.</summary>
    /// <param name="props">The map's static props.</param>
    /// <param name="collide">A model's first solid, or null when it has none.</param>
    /// <param name="surfaceProp">A model's `$surfaceprop` as a surface index, −1 for none.</param>
    /// <returns>The props.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public static StaticPropCollision From(
        IReadOnlyList<BspStaticProp> props, Func<string, IvpStaticPropCollide?> collide, Func<string, int> surfaceProp)
    {
        ArgumentNullException.ThrowIfNull(props);
        ArgumentNullException.ThrowIfNull(collide);
        ArgumentNullException.ThrowIfNull(surfaceProp);

        List<Prop> made = [];

        foreach (BspStaticProp prop in props)
        {
            if (prop.Solid != SolidVphysics || collide(prop.Model) is not { } found)
            {
                continue;
            }

            (Vector3 forward, Vector3 left, Vector3 up) = AngleMatrix(prop.Pitch, prop.Yaw, prop.Roll);
            Vector3 origin = new(prop.X, prop.Y, prop.Z);
            List<Hull> hulls = [];

            Collect(found.Surface.Root, point =>
            {
                (float x, float y, float z) = IvpTransform.SourcePosition(point.X, point.Y, point.Z);
                return origin + (forward * x) + (left * y) + (up * z);
            }, hulls);

            if (hulls.Count == 0)
            {
                continue;
            }

            Vector3 min = hulls[0].Min;
            Vector3 max = hulls[0].Max;

            foreach (Hull hull in hulls)
            {
                min = Vector3.Min(min, hull.Min);
                max = Vector3.Max(max, hull.Max);
            }

            made.Add(new Prop(min, max, [.. hulls], surfaceProp(prop.Model)));
        }

        return new StaticPropCollision([.. made]);
    }

    /// <summary>The nearest prop a line meets, or null when it meets none.</summary>
    /// <param name="from">The line's start.</param>
    /// <param name="to">Its end.</param>
    /// <returns>The hit.</returns>
    public StaticPropHit? Trace((float X, float Y, float Z) from, (float X, float Y, float Z) to) => Trace(from, to, 0f);

    /// <summary>The nearest prop a box swept along a line meets, or null when it meets none.</summary>
    /// <param name="from">The box centre's start.</param>
    /// <param name="to">Its end.</param>
    /// <param name="halfExtent">Half the box's width on every axis; zero for a line.</param>
    /// <returns>The hit.</returns>
    /// <remarks>
    /// **The box against a convex hull is the line against their Minkowski sum**: each face plane pushed out by the box's support
    /// along its normal, `h·(|nx|+|ny|+|nz|)`, and the hull's own axis-aligned bounds pushed out by `h` as six bevel planes — the
    /// planes a BSP brush carries for the same reason. *Interpolated:* vphysics' `TraceBox` sweeps exactly; the edge-against-edge
    /// bevels that would make this exact are not added, so a box can stop a hair early where a hull edge meets a box edge.
    /// </remarks>
    public StaticPropHit? Trace((float X, float Y, float Z) from, (float X, float Y, float Z) to, float halfExtent)
    {
        Vector3 start = new(from.X, from.Y, from.Z);
        Vector3 delta = new Vector3(to.X, to.Y, to.Z) - start;
        Vector3 grow = new(MathF.Abs(halfExtent));
        StaticPropHit? nearest = null;

        foreach (Prop prop in _props)
        {
            if (Box(start, delta, prop.Min - grow, prop.Max + grow) is not { } reaches || reaches > (nearest?.Fraction ?? 1f))
            {
                continue;
            }

            foreach (Hull hull in prop.Hulls)
            {
                if (Clip(start, delta, hull, grow.X) is { } hit && hit.Fraction < (nearest?.Fraction ?? 1f))
                {
                    nearest = new StaticPropHit(hit.Fraction, hit.Normal, prop.SurfaceProp);
                }
            }
        }

        return nearest;
    }

    /// <summary>`AngleMatrix` (`mathlib_base.cpp`): the local axes' directions in the world.</summary>
    private static (Vector3 Forward, Vector3 Left, Vector3 Up) AngleMatrix(float pitch, float yaw, float roll)
    {
        (float sp, float cp) = MathF.SinCos(pitch * (MathF.PI / 180f));
        (float sy, float cy) = MathF.SinCos(yaw * (MathF.PI / 180f));
        (float sr, float cr) = MathF.SinCos(roll * (MathF.PI / 180f));

        return (
            new Vector3(cp * cy, cp * sy, -sp),
            new Vector3((sr * sp * cy) - (cr * sy), (sr * sp * sy) + (cr * cy), sr * cp),
            new Vector3((cr * sp * cy) + (sr * sy), (cr * sp * sy) - (sr * cy), cr * cp));
    }

    /// <summary>Every terminal ledge of a tree, as world-space planes — an inner node's ledge only bounds its children.</summary>
    private static void Collect(PhysicsLedgeTreeNode node, Func<Vector3, Vector3> place, List<Hull> into)
    {
        if (!node.IsTerminal)
        {
            if (node.Left is { } left)
            {
                Collect(left, place, into);
            }

            if (node.Right is { } right)
            {
                Collect(right, place, into);
            }

            return;
        }

        if (node.Ledge is not { } ledge || ledge.Points.Count == 0)
        {
            return;
        }

        Vector3[] points = new Vector3[ledge.Points.Count];
        Vector3 centre = Vector3.Zero;

        for (int index = 0; index < points.Length; index++)
        {
            points[index] = place(ledge.Points[index]);
            centre += points[index];
        }

        centre /= points.Length;

        List<Plane> planes = [];

        foreach ((int a, int b, int c) in ledge.Triangles)
        {
            Vector3 normal = Vector3.Cross(points[b] - points[a], points[c] - points[a]);

            if (normal.LengthSquared() == 0f)
            {
                continue;
            }

            normal = Vector3.Normalize(normal);

            // Outward: away from the ledge's own middle, whichever way the file winds it.
            if (Vector3.Dot(normal, centre - points[a]) > 0f)
            {
                normal = -normal;
            }

            planes.Add(new Plane(normal, Vector3.Dot(normal, points[a])));
        }

        Vector3 min = points[0];
        Vector3 max = points[0];

        foreach (Vector3 point in points)
        {
            min = Vector3.Min(min, point);
            max = Vector3.Max(max, point);
        }

        into.Add(new Hull(min, max, [.. planes]));
    }

    /// <summary>The six axis bevels: ±x, ±y, ±z.</summary>
    private static readonly Vector3[] Axes =
    [
        Vector3.UnitX, -Vector3.UnitX, Vector3.UnitY, -Vector3.UnitY, Vector3.UnitZ, -Vector3.UnitZ,
    ];

    /// <summary>A line against one convex hull grown by a box: the latest entry before the earliest exit.</summary>
    private static (float Fraction, Vector3 Normal)? Clip(Vector3 start, Vector3 delta, Hull hull, float halfExtent)
    {
        float enter = 0f;
        float leave = 1f;
        Vector3 normal = Vector3.Zero;
        int faces = hull.Planes.Length;
        int bevels = halfExtent > 0f ? Axes.Length : 0;

        for (int index = 0; index < faces + bevels; index++)
        {
            Plane plane;

            if (index < faces)
            {
                Plane face = hull.Planes[index];
                Vector3 n = face.Normal;
                plane = new Plane(n, face.D + (halfExtent * (MathF.Abs(n.X) + MathF.Abs(n.Y) + MathF.Abs(n.Z))));
            }
            else
            {
                Vector3 axis = Axes[index - faces];
                float reach = Vector3.Dot(axis, Vector3.Dot(axis, Vector3.One) > 0f ? hull.Max : hull.Min);
                plane = new Plane(axis, reach + halfExtent);
            }

            float distance = Vector3.Dot(plane.Normal, start) - plane.D;
            float along = Vector3.Dot(plane.Normal, delta);

            if (along == 0f)
            {
                if (distance > 0f)
                {
                    return null;
                }

                continue;
            }

            float at = -distance / along;

            if (along < 0f)
            {
                if (at > enter)
                {
                    enter = at;
                    normal = plane.Normal;
                }
            }
            else if (at < leave)
            {
                leave = at;
            }

            if (enter > leave)
            {
                return null;
            }
        }

        return (enter, normal);
    }

    /// <summary>Where a line enters a box, or null when it misses it.</summary>
    private static float? Box(Vector3 start, Vector3 delta, Vector3 min, Vector3 max)
    {
        float enter = 0f;
        float leave = 1f;

        for (int axis = 0; axis < 3; axis++)
        {
            float origin = start[axis];
            float step = delta[axis];

            if (step == 0f)
            {
                if (origin < min[axis] || origin > max[axis])
                {
                    return null;
                }

                continue;
            }

            float first = (min[axis] - origin) / step;
            float second = (max[axis] - origin) / step;

            enter = MathF.Max(enter, MathF.Min(first, second));
            leave = MathF.Min(leave, MathF.Max(first, second));
        }

        return enter <= leave ? enter : null;
    }

    private readonly record struct Hull(Vector3 Min, Vector3 Max, Plane[] Planes);

    private readonly record struct Prop(Vector3 Min, Vector3 Max, Hull[] Hulls, int SurfaceProp);
}
