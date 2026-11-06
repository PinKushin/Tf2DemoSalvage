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
}

/// <summary>One `CCaptureZone` (`DT_CaptureZone`, func_capture_zone.cpp).</summary>
/// <param name="EntityIndex">The entity index.</param>
/// <param name="Team">`GetTeamNumber()`.</param>
/// <param name="Disabled">`m_bDisabled`.</param>
/// <param name="Center">`WorldSpaceCenter()`.</param>
public readonly record struct SceneCaptureZone(int EntityIndex, int Team, bool Disabled, (float X, float Y, float Z) Center);

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
    internal static SceneFlags? Read(EntityStateTable entities, SceneFlags? last)
    {
        List<SceneFlag>? flags = null;
        List<SceneCaptureZone>? zones = null;

        foreach (EntityState flag in entities.OfClass("CCaptureFlag"))
        {
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
                flag.Number("DT_CaptureFlag.m_flTimeToSetPoisonous") ?? 0f));
        }

        foreach (EntityState zone in entities.OfClass("CCaptureZone"))
        {
            (zones ??= []).Add(new SceneCaptureZone(
                zone.EntityIndex,
                zone.Integer("DT_BaseEntity.m_iTeamNum") ?? 0,
                zone.Integer("DT_CaptureZone.m_bDisabled") is > 0,
                Center(entities, zone)));
        }

        if (flags is null && zones is null)
        {
            return null;
        }

        SceneFlags read = new(flags ?? [], zones ?? []);

        return last is not null && last.Equals(read) ? last : read;
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
    private static (float X, float Y, float Z) Center(EntityStateTable entities, EntityState entity)
    {
        (float X, float Y, float Z) origin = entities.Resolve(entity.Integer("DT_BaseEntity.moveparent")) is { } parent
            && entities.TryGet(parent, out EntityState? carrier)
            ? carrier.Origin() ?? default
            : entity.Origin() ?? default;
        (float X, float Y, float Z) mins = entity.Vector("DT_CollisionProperty.m_vecMins") ?? default;
        (float X, float Y, float Z) maxs = entity.Vector("DT_CollisionProperty.m_vecMaxs") ?? default;

        return (origin.X + ((mins.X + maxs.X) / 2f), origin.Y + ((mins.Y + maxs.Y) / 2f), origin.Z + ((mins.Z + maxs.Z) / 2f));
    }
}
