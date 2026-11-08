using System;
using System.Collections.Generic;
using System.Numerics;

namespace Tf2DemoSalvage.Scene.ProjectedShadows;

/// <summary>
/// One render-to-texture shadow's geometry — <c>CClientShadowMgr::BuildRenderToTextureShadow</c>
/// (`clientshadowmgr.cpp:2464-2565`, finding 107): its axes, size, origin, falloff, matrices and extra clip planes.
/// </summary>
/// <param name="XAxis">The shadow's x axis in the world, y × dir.</param>
/// <param name="YAxis">Its y axis, the box's longest side projected off the shadow direction.</param>
/// <param name="Direction">The direction it is cast, unit length.</param>
/// <param name="Origin">Where shadow space starts: the box's face furthest back along the direction.</param>
/// <param name="Size">Its width and height in world units, two texels' worth wider at each edge (`m_WorldSize`).</param>
/// <param name="FalloffStart">The depth at which it starts to fade (`ProjectShadow`'s falloff offset).</param>
/// <param name="MaxHeight">The depth past which it draws nothing: the cast distance plus <paramref name="FalloffStart"/>.</param>
/// <param name="ClipPlanes">Its extra clip planes (`ComputeExtraClipPlanes`), each a normal and a distance.</param>
public sealed record ShadowProjection(
    Vector3 XAxis,
    Vector3 YAxis,
    Vector3 Direction,
    Vector3 Origin,
    Vector2 Size,
    float FalloffStart,
    float MaxHeight,
    IReadOnlyList<(Vector3 Normal, float Distance)> ClipPlanes)
{
    /// <summary>`TEXEL_SIZE_PER_CASTER_SIZE` (`:679`).</summary>
    public const float TexelsPerCasterUnit = 2f;

    /// <summary>`MAX_FALLOFF_AMOUNT` (`:680`), the falloff amount every render-to-texture shadow is projected with.</summary>
    public const float MaxFalloffAmount = 240f;

    /// <summary>Builds a caster's shadow — <c>BuildRenderToTextureShadow</c>.</summary>
    /// <param name="renderOrigin">The caster's <c>GetRenderOrigin()</c>.</param>
    /// <param name="renderAngles">Its <c>GetRenderAngles()</c>, pitch, yaw and roll in degrees.</param>
    /// <param name="mins">Its shadow render bounds' lower corner, in its own frame.</param>
    /// <param name="maxs">Their upper corner.</param>
    /// <param name="direction">The shadow direction (<c>GetShadowDirection</c>), any length.</param>
    /// <param name="castDistance">The cast distance (<c>GetShadowDistance</c>).</param>
    /// <returns>The shadow.</returns>
    public static ShadowProjection ForRenderToTexture(
        Vector3 renderOrigin,
        (float Pitch, float Yaw, float Roll) renderAngles,
        Vector3 mins,
        Vector3 maxs,
        Vector3 direction,
        float castDistance)
    {
        (Vector3 forward, Vector3 left, Vector3 up) = Basis(renderAngles);
        Vector3[] axes = [forward, left, up];
        Vector3 dir = Vector3.Normalize(direction);

        // "Project the shadow casting direction into the space of the object" (:2485-2488).
        Vector3 localDir = new(Vector3.Dot(forward, dir), Vector3.Dot(left, dir), Vector3.Dot(up, dir));
        Vector3 boxSize = maxs - mins;

        // The longest side once the direction is projected out (:2494-2506); strictly greater, so a tie keeps the first.
        Vector3 yAxis = Vector3.Zero;
        float longest = 0f;

        for (int i = 0; i < 3; i++)
        {
            Vector3 test = (axes[i] - (dir * Vector3.Dot(dir, axes[i]))) * Component(boxSize, i);
            float lengthSquared = test.LengthSquared();

            if (lengthSquared > longest)
            {
                longest = lengthSquared;
                yAxis = test;
            }
        }

        yAxis = Normalize(yAxis);
        Vector3 xAxis = Vector3.Cross(yAxis, dir);

        Vector2 size = new(
            (boxSize.X * MathF.Abs(Vector3.Dot(forward, xAxis))) + (boxSize.Y * MathF.Abs(Vector3.Dot(left, xAxis))) + (boxSize.Z * MathF.Abs(Vector3.Dot(up, xAxis))),
            (boxSize.X * MathF.Abs(Vector3.Dot(forward, yAxis))) + (boxSize.Y * MathF.Abs(Vector3.Dot(left, yAxis))) + (boxSize.Z * MathF.Abs(Vector3.Dot(up, yAxis))));
        size += new Vector2(2f * TexelsPerCasterUnit);

        float falloffStart = LocalShadowOrigin(mins, maxs, localDir, backupFactor: 1f, out Vector3 local);
        Vector3 origin = renderOrigin + (local.X * forward) + (local.Y * left) + (local.Z * up);

        return new ShadowProjection(
            xAxis, yAxis, dir, origin, size, falloffStart, castDistance + falloffStart, ExtraClipPlanes(renderOrigin, axes, mins, maxs, localDir));
    }

    /// <summary>
    /// Builds a blobby caster's shadow — <c>BuildOrthoShadow</c> (`clientshadowmgr.cpp:2284-2383`), what a
    /// <c>SHADOWS_SIMPLE</c> shadow is projected with.
    /// </summary>
    /// <param name="renderOrigin">The caster's <c>GetRenderOrigin()</c>.</param>
    /// <param name="renderAngles">Its <c>GetRenderAngles()</c>.</param>
    /// <param name="mins">Its shadow render bounds' lower corner, in its own frame.</param>
    /// <param name="maxs">Their upper corner.</param>
    /// <param name="direction">The shadow direction, any length.</param>
    /// <param name="castDistance">The cast distance.</param>
    /// <returns>The shadow, with no extra clip planes — "Removing this for now since it seems to mess up the blobby shadows"
    /// (`:2382-2384`).</returns>
    /// <remarks>
    /// **The two axes are the caster's own most perpendicular to the direction, projected off it** (`SortAbsVectorComponents`,
    /// `:2077-2106`), so they need not be perpendicular to each other, and the world-to-shadow matrix is a general inverse
    /// (`BuildGeneralWorldToShadowMatrix`, `:1954-1969`). The size is 10 wider each way and at least 10; the origin backs
    /// off twice as far (`ComputeLocalShadowOrigin`'s backup factor 2) and is snapped to half a unit.
    /// </remarks>
    public static ShadowProjection ForOrtho(
        Vector3 renderOrigin,
        (float Pitch, float Yaw, float Roll) renderAngles,
        Vector3 mins,
        Vector3 maxs,
        Vector3 direction,
        float castDistance)
    {
        (Vector3 forward, Vector3 left, Vector3 up) = Basis(renderAngles);
        Vector3[] axes = [forward, left, up];
        Vector3 dir = Vector3.Normalize(direction);
        Vector3 localDir = new(Vector3.Dot(forward, dir), Vector3.Dot(left, dir), Vector3.Dot(up, dir));

        // `SortAbsVectorComponents`: the largest component last, "always choose something right-handed".
        float ax = MathF.Abs(localDir.X);
        float ay = MathF.Abs(localDir.Y);
        float az = MathF.Abs(localDir.Z);
        int largest = ax > ay ? 0 : 1;

        if (az > (largest == 0 ? ax : ay))
        {
            largest = 2;
        }

        (int first, int second, int third) = largest switch
        {
            0 => (1, 2, 0),
            1 => (2, 0, 1),
            _ => (0, 1, 2),
        };

        Vector3 xAxis = Normalize(axes[first] - (dir * Vector3.Dot(dir, axes[first])));
        Vector3 yAxis = Normalize(axes[second] - (dir * Vector3.Dot(dir, axes[second])));
        Vector3 boxSize = maxs - mins;

        Vector2 size = new(
            (Component(boxSize, first) * MathF.Abs(Vector3.Dot(axes[first], xAxis))) +
            (Component(boxSize, third) * MathF.Abs(Vector3.Dot(axes[third], xAxis))),
            (Component(boxSize, second) * MathF.Abs(Vector3.Dot(axes[second], yAxis))) +
            (Component(boxSize, third) * MathF.Abs(Vector3.Dot(axes[third], yAxis))));

        // "Bloat a bit, since the shadow wants to extend outside the model a bit", then "Clamp the minimum size".
        size = Vector2.Max(size + new Vector2(10f), new Vector2(10f));

        float falloffStart = LocalShadowOrigin(mins, maxs, localDir, backupFactor: 2f, out Vector3 local);
        Vector3 origin = renderOrigin + (local.X * forward) + (local.Y * left) + (local.Z * up);

        // "FUNKY: A trick to reduce annoying texelization artifacts!?" — truncated to half a unit, toward zero.
        const float Snap = 1f / TexelsPerCasterUnit;
        origin = new Vector3((int)(origin.X / Snap) * Snap, (int)(origin.Y / Snap) * Snap, (int)(origin.Z / Snap) * Snap);

        float determinant = Vector3.Dot(xAxis, Vector3.Cross(yAxis, dir));

        return new ShadowProjection(xAxis, yAxis, dir, origin, size, falloffStart, castDistance + falloffStart, [])
        {
            GeneralRows = (
                Vector3.Cross(yAxis, dir) / determinant,
                Vector3.Cross(dir, xAxis) / determinant,
                Vector3.Cross(xAxis, yAxis) / determinant),
        };
    }

    /// <summary>
    /// The rows of <c>MatrixInverseGeneral</c> of the basis (x, y, dir) for a shadow whose axes are not perpendicular
    /// (<see cref="ForOrtho"/>); null for an orthonormal one, whose inverse is its transpose.
    /// </summary>
    public (Vector3 X, Vector3 Y, Vector3 Depth)? GeneralRows { get; init; }

    /// <summary>
    /// The centre of the sphere `ProjectShadow` stores for the receiver test: half the max height down the direction from
    /// the origin (`FUN_180127de0`, `+0xe0`).
    /// </summary>
    public Vector3 SphereCentre => Origin + (Direction * (MaxHeight * 0.5f));

    /// <summary>Its radius: half the max height plus half the size's diagonal (`+0x8c`).</summary>
    public float SphereRadius => (MaxHeight * 0.5f) + (Size.Length() * 0.5f);

    /// <summary>A point in shadow space — <c>m_WorldToShadow</c> (`BuildOrthoWorldToShadowMatrix`, `:2126-2151`).</summary>
    /// <param name="world">The point in the world.</param>
    /// <returns>Its x and y along the shadow's axes and its depth along the direction, from <see cref="Origin"/>.</returns>
    public Vector3 WorldToShadow(Vector3 world)
    {
        Vector3 offset = world - Origin;

        return GeneralRows is { } rows
            ? new Vector3(Vector3.Dot(rows.X, offset), Vector3.Dot(rows.Y, offset), Vector3.Dot(rows.Depth, offset))
            : new Vector3(Vector3.Dot(XAxis, offset), Vector3.Dot(YAxis, offset), Vector3.Dot(Direction, offset));
    }

    /// <summary>A point in shadow texture space — <c>BuildWorldToTextureMatrix</c> (`:2112-2122`), what `ProjectShadow` is handed.</summary>
    /// <param name="world">The point in the world.</param>
    /// <returns>u and v (0 to 1 across the shadow) and the depth, unscaled.</returns>
    public Vector3 WorldToTexture(Vector3 world)
    {
        Vector3 shadow = WorldToShadow(world);
        return new Vector3((shadow.X / Size.X) + 0.5f, (shadow.Y / Size.Y) + 0.5f, shadow.Z);
    }

    /// <summary>Source's <c>AngleVectors</c> with the right vector negated, as `:2475-2476` does: forward, left and up.</summary>
    /// <param name="angles">Pitch, yaw and roll in degrees.</param>
    /// <returns>The three axes.</returns>
    public static (Vector3 Forward, Vector3 Left, Vector3 Up) Basis((float Pitch, float Yaw, float Roll) angles)
    {
        (float sp, float cp) = MathF.SinCos(angles.Pitch * (MathF.PI / 180f));
        (float sy, float cy) = MathF.SinCos(angles.Yaw * (MathF.PI / 180f));
        (float sr, float cr) = MathF.SinCos(angles.Roll * (MathF.PI / 180f));

        Vector3 forward = new(cp * cy, cp * sy, -sp);
        Vector3 right = new((-1f * sr * sp * cy) + (-1f * cr * -sy), (-1f * sr * sp * sy) + (-1f * cr * cy), -1f * sr * cp);
        Vector3 up = new((cr * sp * cy) + (-sr * -sy), (cr * sp * sy) + (-sr * cy), cr * cp);

        return (forward, -right, up);
    }

    /// <summary>`ComputeLocalShadowOrigin` (`:2022-2071`): the shadow origin in the caster's frame, and where the falloff starts.</summary>
    private static float LocalShadowOrigin(Vector3 mins, Vector3 maxs, Vector3 localDir, float backupFactor, out Vector3 origin)
    {
        Vector3 centroid = (mins + maxs) * 0.5f;
        float radius = (maxs - mins).Length() * 0.5f;

        float minDist = -Vector3.Dot(centroid, localDir);

        for (int i = 0; i < 3; i++)
        {
            float d = Component(localDir, i);
            minDist += d > 0f ? d * Component(mins, i) : d * Component(maxs, i);
        }

        minDist *= backupFactor;
        origin = centroid + (minDist * localDir);

        return radius - minDist;
    }

    /// <summary>`ComputeExtraClipPlanes` (`:2171-2214`), without the rendering clip plane no TF2 entity enables.</summary>
    private static (Vector3 Normal, float Distance)[] ExtraClipPlanes(
        Vector3 renderOrigin, Vector3[] axes, Vector3 mins, Vector3 maxs, Vector3 localDir)
    {
        Vector3 corner = renderOrigin;
        float[] sign = new float[3];

        for (int i = 0; i < 3; i++)
        {
            if (Component(localDir, i) < 0f)
            {
                corner += Component(maxs, i) * axes[i];
                sign[i] = 1f;
            }
            else
            {
                corner += Component(mins, i) * axes[i];
                sign[i] = -1f;
            }
        }

        (Vector3 Normal, float Distance)[] planes = new (Vector3, float)[3];

        for (int i = 0; i < 3; i++)
        {
            Vector3 normal = axes[i] * sign[i];
            planes[i] = (normal, Vector3.Dot(normal, corner));
        }

        return planes;
    }

    /// <summary>`VectorNormalize`: a zero vector stays zero.</summary>
    private static Vector3 Normalize(Vector3 v)
    {
        float length = v.Length();
        return length > 0f ? v / length : v;
    }

    private static float Component(Vector3 v, int i) => i switch
    {
        0 => v.X,
        1 => v.Y,
        _ => v.Z,
    };
}
