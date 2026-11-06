using System;
using System.Collections.Generic;
using System.Linq;

namespace Tf2DemoSalvage.Core.Scene;

/// <summary>One `CCaptureFlag` (`DT_CaptureFlag`, entity_capture_flag.cpp), as the flag HUD reads it.</summary>
/// <param name="EntityIndex">The entity index.</param>
/// <param name="Team">`GetTeamNumber()`.</param>
/// <param name="Status">`m_nFlagStatus`: 0 home, 1 stolen, 2 dropped.</param>
/// <param name="Disabled">`m_bDisabled`.</param>
/// <param name="VisibleWhenDisabled">`m_bVisibleWhenDisabled`.</param>
/// <param name="Type">`m_nType`, `ETFFlagType`.</param>
/// <param name="HudIcon">`m_szHudIcon`.</param>
/// <param name="Center">`WorldSpaceCenter()`: the origin plus the middle of its collision box.</param>
/// <param name="Carrier">The player whose `m_hItem` is this flag, or 0.</param>
/// <param name="PoisonTime">`m_flTimeToSetPoisonous`.</param>
public readonly record struct SceneFlag(
    int EntityIndex,
    int Team,
    int Status,
    bool Disabled,
    bool VisibleWhenDisabled,
    int Type,
    string HudIcon,
    (float X, float Y, float Z) Center,
    int Carrier,
    float PoisonTime)
{
    /// <summary>`IsStolen()`.</summary>
    public bool IsStolen => Status == 1;

    /// <summary>`IsDropped()`.</summary>
    public bool IsDropped => Status == 2;

    /// <summary>`IsHome()`.</summary>
    public bool IsHome => Status == 0;

    /// <summary>`IsDormant()`: out of the client's PVS and still held.</summary>
    public bool Dormant { get; init; }

    /// <summary>The entity's serial number, which a `CHandle` checks with its index.</summary>
    public int SerialNumber { get; init; }

    /// <summary>`GetAbsOrigin()`, or the move parent's when carried (tf_hud_robot_destruction_status.cpp:899) — no bounds.</summary>
    public (float X, float Y, float Z) Origin { get; init; }

    /// <summary>`GetPrevOwner()->GetTeamNumber()`, or null when the previous owner is no entity.</summary>
    public int? PrevOwnerTeam { get; init; }

    /// <summary>`GetPointValue()`: `m_nPointValue`, robot destruction's points the flag carries.</summary>
    public int PointValue { get; init; }

    /// <summary>`m_flResetTime`.</summary>
    public float ResetTime { get; init; }

    /// <summary>`m_flNeutralTime`.</summary>
    public float NeutralTime { get; init; }

    /// <summary>`m_flMaxResetTime`.</summary>
    public float MaxResetTime { get; init; }

    /// <summary>`m_hPrevOwner`'s entity index, or 0.</summary>
    public int PrevOwner { get; init; }

    /// <summary>`GetReturnProgress` (entity_capture_flag.cpp:2583): `1 - (max(reset, neutral) - curtime) / max reset`.</summary>
    /// <param name="curTime">`gpGlobals->curtime`.</param>
    /// <returns>The progress; a zero max reset divides as C's floats do.</returns>
    public float ReturnProgress(float curTime) => (float)(1.0 - ((MathF.Max(ResetTime, NeutralTime) - curTime) / MaxResetTime));
}

/// <summary>One `CCaptureZone` (`DT_CaptureZone`, func_capture_zone.cpp).</summary>
/// <param name="EntityIndex">The entity index.</param>
/// <param name="Team">`GetTeamNumber()`.</param>
/// <param name="Disabled">`m_bDisabled`.</param>
/// <param name="Center">`WorldSpaceCenter()`.</param>
public readonly record struct SceneCaptureZone(int EntityIndex, int Team, bool Disabled, (float X, float Y, float Z) Center)
{
    /// <summary>`BoundingRadius()`: `CCollisionProperty::m_flRadius`, half the bounds' diagonal (collisionproperty.cpp:707).</summary>
    public float BoundingRadius { get; init; }

    /// <summary>`IsDormant()`: out of the client's PVS and still held.</summary>
    public bool Dormant { get; init; }
}

/// <summary>The capture flags and capture zones at one tick, in entity order as `ICaptureFlagAutoList` holds them by creation.</summary>
/// <param name="Flags">The flags.</param>
/// <param name="Zones">The capture zones.</param>
public sealed record SceneFlags(IReadOnlyList<SceneFlag> Flags, IReadOnlyList<SceneCaptureZone> Zones)
{
    /// <inheritdoc/>
    public bool Equals(SceneFlags? other) => other is not null && Flags.SequenceEqual(other.Flags) && Zones.SequenceEqual(other.Zones);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Flags.Count, Zones.Count);

    /// <summary>Reads the frame's flags and zones, sharing the last frame's when unchanged; null with neither.</summary>
    /// <param name="entities">The frame's entities.</param>
    /// <param name="last">The last frame's, or null.</param>
    /// <returns>The flags, or null.</returns>
    /// <param name="isDormant">Whether an entity index is dormant this frame; null treats none as dormant.</param>
    internal static SceneFlags? Read(EntityStateTable entities, SceneFlags? last, Func<int, bool>? isDormant = null)
    {
        List<SceneFlag>? flags = null;
        List<SceneCaptureZone>? zones = null;

        foreach (EntityState flag in entities.OfClass("CCaptureFlag"))
        {
            int? prevOwner = entities.Resolve(flag.Integer("DT_CaptureFlag.m_hPrevOwner"));

            (flags ??= []).Add(new SceneFlag(
                flag.EntityIndex,
                flag.Integer("DT_BaseEntity.m_iTeamNum") ?? 0,
                flag.Integer("DT_CaptureFlag.m_nFlagStatus") ?? 0,
                flag.Integer("DT_CaptureFlag.m_bDisabled") is > 0,
                flag.Integer("DT_CaptureFlag.m_bVisibleWhenDisabled") is > 0,
                flag.Integer("DT_CaptureFlag.m_nType") ?? 0,
                flag.Text("DT_CaptureFlag.m_szHudIcon") ?? string.Empty,
                Center(entities, flag),
                CarrierOf(entities, flag.EntityIndex),
                flag.Number("DT_CaptureFlag.m_flTimeToSetPoisonous") ?? 0f)
            {
                PointValue = flag.Integer("DT_CaptureFlag.m_nPointValue") ?? 0,
                ResetTime = flag.Number("DT_CaptureFlag.m_flResetTime") ?? 0f,
                NeutralTime = flag.Number("DT_CaptureFlag.m_flNeutralTime") ?? 0f,
                MaxResetTime = flag.Number("DT_CaptureFlag.m_flMaxResetTime") ?? 0f,
                PrevOwner = prevOwner ?? 0,
                PrevOwnerTeam = prevOwner is { } prev && entities.TryGet(prev, out EntityState? owner)
                    ? owner.Integer("DT_BaseEntity.m_iTeamNum") ?? 0
                    : null,
                Dormant = isDormant?.Invoke(flag.EntityIndex) ?? false,
                SerialNumber = flag.SerialNumber,
                Origin = AbsOrigin(entities, flag),
            });
        }

        foreach (EntityState zone in entities.OfClass("CCaptureZone"))
        {
            (zones ??= []).Add(new SceneCaptureZone(
                zone.EntityIndex,
                zone.Integer("DT_BaseEntity.m_iTeamNum") ?? 0,
                zone.Integer("DT_CaptureZone.m_bDisabled") is > 0,
                Center(entities, zone))
            {
                BoundingRadius = Radius(zone),
                Dormant = isDormant?.Invoke(zone.EntityIndex) ?? false,
            });
        }

        if (flags is null && zones is null)
        {
            return null;
        }

        SceneFlags read = new(flags ?? [], zones ?? []);

        return last is not null && last.Equals(read) ? last : read;
    }

    /// <summary>`m_flRadius = ( m_vecMaxs - m_vecMins ).Length() * 0.5f` (collisionproperty.cpp:705-707).</summary>
    private static float Radius(EntityState entity)
    {
        (float X, float Y, float Z) mins = entity.Vector("DT_CollisionProperty.m_vecMins") ?? default;
        (float X, float Y, float Z) maxs = entity.Vector("DT_CollisionProperty.m_vecMaxs") ?? default;
        float x = maxs.X - mins.X;
        float y = maxs.Y - mins.Y;
        float z = maxs.Z - mins.Z;

        return MathF.Sqrt((x * x) + (y * y) + (z * z)) * 0.5f;
    }

    private static int CarrierOf(EntityStateTable entities, int flag)
    {
        foreach (EntityState player in entities.OfClass("CTFPlayer"))
        {
            if (EntityState.Slot(player.Integer("DT_TFPlayer.m_hItem")) == flag)
            {
                return player.EntityIndex;
            }
        }

        return 0;
    }

    /// <remarks>
    /// **A carried flag is parented to its carrier** (`CCaptureFlag::PickUp` sets it to the player's "flag" attachment), so its
    /// networked origin is local to that. **Interpolated:** the carrier's origin stands for the attachment, which needs the
    /// carrier's bones; the compass turns by the horizontal direction alone, which that offset barely moves.
    /// </remarks>
    /// <summary>A carried entity at its move parent's origin, else its own.</summary>
    private static (float X, float Y, float Z) AbsOrigin(EntityStateTable entities, EntityState entity) =>
        entities.Resolve(entity.Integer("DT_BaseEntity.moveparent")) is { } parent && entities.TryGet(parent, out EntityState? carrier)
            ? carrier.Origin() ?? default
            : entity.Origin() ?? default;

    private static (float X, float Y, float Z) Center(EntityStateTable entities, EntityState entity)
    {
        (float X, float Y, float Z) origin = AbsOrigin(entities, entity);
        (float X, float Y, float Z) mins = entity.Vector("DT_CollisionProperty.m_vecMins") ?? default;
        (float X, float Y, float Z) maxs = entity.Vector("DT_CollisionProperty.m_vecMaxs") ?? default;
        (float X, float Y, float Z) local = ((mins.X + maxs.X) / 2f, (mins.Y + maxs.Y) / 2f, (mins.Z + maxs.Z) / 2f);

        // `CollisionToWorldSpace` (collisionproperty.h): bounds defined in entity space — neither `SOLID_NONE` (0) nor
        // `SOLID_BBOX` (2), and no `FSOLID_FORCE_WORLD_ALIGNED` (0x40) — turn with the entity, so a zone placed by a rotated
        // instance has its bounds about an origin elsewhere (rd_asteroid's BLU zone, yaw 180).
        int solidType = entity.Integer("DT_CollisionProperty.m_nSolidType") ?? 0;
        bool entitySpace = solidType is not 0 and not 2 && ((entity.Integer("DT_CollisionProperty.m_usSolidFlags") ?? 0) & 0x40) == 0;
        (float X, float Y, float Z) turned = entitySpace
            && entities.Resolve(entity.Integer("DT_BaseEntity.moveparent")) is null
            && entity.Angles() is { } angles
            ? Rotate(local, angles)
            : local;

        return (origin.X + turned.X, origin.Y + turned.Y, origin.Z + turned.Z);
    }

    /// <summary>`AngleMatrix` (mathlib_base.cpp) applied to a vector: `VectorRotate`.</summary>
    private static (float X, float Y, float Z) Rotate((float X, float Y, float Z) v, (float Pitch, float Yaw, float Roll) angles)
    {
        const float toRadians = MathF.PI / 180f;
        (float sp, float cp) = MathF.SinCos(angles.Pitch * toRadians);
        (float sy, float cy) = MathF.SinCos(angles.Yaw * toRadians);
        (float sr, float cr) = MathF.SinCos(angles.Roll * toRadians);

        return (
            (cp * cy * v.X) + (((sr * sp * cy) - (cr * sy)) * v.Y) + (((cr * sp * cy) + (sr * sy)) * v.Z),
            (cp * sy * v.X) + (((sr * sp * sy) + (cr * cy)) * v.Y) + (((cr * sp * sy) - (sr * cy)) * v.Z),
            (-sp * v.X) + (sr * cp * v.Y) + (cr * cp * v.Z));
    }
}
