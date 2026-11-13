using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Presentation;

/// <summary>Where an entity's sounds are, as the client answers the engine every frame (B724).</summary>
/// <remarks>
/// `C_BaseEntity::GetSoundSpatialization` (`c_baseentity.cpp:1864`): the entity's abs origin, and for a brush model
/// the middle of its bounds added (`(mins + maxs) * 0.5`, `:1891-1899`). A dormant entity answers nothing and its
/// channels keep their origin (<see cref="For"/>).
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

    /// <summary>Where an entity's sounds are at a tick, by its track; null to keep the channel's own origin.</summary>
    /// <param name="timeline">The demo.</param>
    /// <param name="brushes">The map's brush models.</param>
    /// <returns>The lookup.</returns>
    /// <remarks>
    /// Null for an entity with no track, a dormant one (the client answers false, `c_baseentity.cpp:1873`, so the
    /// channel keeps the origin its message carried), and a parented one: its track holds the parent-relative origin,
    /// not the abs origin the client reports, so the channel's own is the nearer answer (*interpolated*).
    /// </remarks>
    public static Func<int, double, (float X, float Y, float Z)?> For(DemoTimeline timeline, IReadOnlyList<BspModel>? brushes)
    {
        ArgumentNullException.ThrowIfNull(timeline);

        return (entity, tick) =>
            timeline.TrackFor(entity, tick) is { } track &&
            !timeline.Dormancy.IsDormant(entity, (int)tick) &&
            track.FactsAt(tick).AttachedTo is null &&
            track.At(tick) is { } pose
                ? Of(pose, track.ModelPath, brushes)
                : null;
    }
}
