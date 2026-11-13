using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Presentation;

/// <summary>Where an entity's sounds are, as the client answers the engine every frame (B724).</summary>
/// <remarks>
/// `C_BaseEntity::GetSoundSpatialization` (`c_baseentity.cpp:1864`): the entity's abs origin, and for a brush model
/// the middle of its bounds added (`(mins + maxs) * 0.5`, `:1891-1899`). A dormant entity answers nothing and its
/// channels keep their origin; here a track out of the PVS holds its last pose, which is the same place.
/// *Interpolated:* the origin is the entity's networked one, so a move-parented entity's sound is at its parent-relative
/// origin rather than its abs origin.
/// </remarks>
internal static class SoundOrigins
{
    /// <summary>The sound origin of an entity standing at a pose.</summary>
    /// <param name="pose">Its pose.</param>
    /// <param name="modelPath">Its model, <c>*N</c> for a brush model.</param>
    /// <param name="brushes">The map's brush models, or null when none were read.</param>
    /// <returns>The pose's origin, a brush model's centre added.</returns>
    public static (float X, float Y, float Z) Of(ScenePose pose, string? modelPath, IReadOnlyList<BspModel>? brushes)
    {
        int index = BspModels.IndexOf(modelPath);

        if (index < 0 || brushes is null || index >= brushes.Count)
        {
            return (pose.X, pose.Y, pose.Z);
        }

        BspModel brush = brushes[index];

        return (
            pose.X + ((brush.Minimum.X + brush.Maximum.X) * 0.5f),
            pose.Y + ((brush.Minimum.Y + brush.Maximum.Y) * 0.5f),
            pose.Z + ((brush.Minimum.Z + brush.Maximum.Z) * 0.5f));
    }

    /// <summary>Where an entity's sounds are at a tick, by its track; null for one the timeline has no track for.</summary>
    /// <param name="timeline">The demo.</param>
    /// <param name="brushes">The map's brush models.</param>
    /// <returns>The lookup.</returns>
    public static Func<int, double, (float X, float Y, float Z)?> For(DemoTimeline timeline, IReadOnlyList<BspModel>? brushes)
    {
        ArgumentNullException.ThrowIfNull(timeline);

        return (entity, tick) =>
            timeline.TrackFor(entity, tick) is { } track && track.At(tick) is { } pose
                ? Of(pose, track.ModelPath, brushes)
                : null;
    }
}
