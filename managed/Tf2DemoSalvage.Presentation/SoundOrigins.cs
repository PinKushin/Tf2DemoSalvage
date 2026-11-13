using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Presentation;

/// <summary>Where an entity's sounds are, and its model's radius, as the client answers the engine every frame (B724).</summary>
/// <remarks>
/// `C_BaseEntity::GetSoundSpatialization` (`c_baseentity.cpp:1864`): the model's radius (`GetModelRadius`), the
/// entity's abs origin, and for a brush model the middle of its bounds added (`(mins + maxs) * 0.5`, `:1891-1899`). A
/// dormant entity answers nothing and its channels keep their origin (<c>For</c>).
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
        if (Brush(modelPath, brushes) is not { } brush)
        {
            return (pose.X, pose.Y, pose.Z);
        }

        return (
            pose.X + ((brush.Minimum.X + brush.Maximum.X) * 0.5f),
            pose.Y + ((brush.Minimum.Y + brush.Maximum.Y) * 0.5f),
            pose.Z + ((brush.Minimum.Z + brush.Maximum.Z) * 0.5f));
    }

    /// <summary>A studio model's radius: the largest magnitude of any component of its hull (engine.dll FUN_1801060b0).</summary>
    /// <param name="hull">The <c>.mdl</c> header's <c>hull_min</c>/<c>hull_max</c>.</param>
    /// <returns>The radius <c>GetModelRadius</c> answers (<c>model_t +0x40</c>, read at 0x1801cc9a9).</returns>
    public static float StudioRadius(StudioBox hull)
    {
        float radius = 0f;

        foreach (float component in (ReadOnlySpan<float>)[hull.MinX, hull.MaxX, hull.MinY, hull.MaxY, hull.MinZ, hull.MaxZ])
        {
            if (MathF.Abs(component) > radius)
            {
                radius = MathF.Abs(component);
            }
        }

        return radius;
    }

    /// <summary>
    /// A brush model's radius: <c>RadiusFromBounds</c> of its bounds widened by 1 each way, as <c>Mod_LoadSubmodels</c>
    /// (engine.dll FUN_180104c80) loads them.
    /// </summary>
    /// <param name="brush">The brush model.</param>
    /// <returns>The length of the corner made of each axis's larger magnitude.</returns>
    public static float BrushRadius(BspModel brush)
    {
        float x = Larger(brush.Minimum.X - 1f, brush.Maximum.X + 1f);
        float y = Larger(brush.Minimum.Y - 1f, brush.Maximum.Y + 1f);
        float z = Larger(brush.Minimum.Z - 1f, brush.Maximum.Z + 1f);

        return MathF.Sqrt((x * x) + (y * y) + (z * z));

        static float Larger(float low, float high) => MathF.Abs(low) <= MathF.Abs(high) ? MathF.Abs(high) : MathF.Abs(low);
    }

    /// <summary>Where an entity's sounds are at a tick and its model's radius; null to keep the channel's own origin.</summary>
    /// <param name="timeline">The demo.</param>
    /// <param name="brushes">The map's brush models.</param>
    /// <param name="read">The level's file reader, for a studio model's header; read once a model.</param>
    /// <returns>The lookup.</returns>
    /// <remarks>
    /// Null for an entity with no track, a dormant one (the client answers false, `c_baseentity.cpp:1873`, so the
    /// channel keeps the origin its message carried), and a parented one: its track holds the parent-relative origin,
    /// not the abs origin the client reports, so the channel's own is the nearer answer (*interpolated*). *Interpolated*
    /// too: that a brush model's <c>model_t</c> radius is its submodel's was not traced.
    /// </remarks>
    public static Func<int, double, (float X, float Y, float Z, float Radius)?> For(
        DemoTimeline timeline, IReadOnlyList<BspModel>? brushes, Func<string, byte[]?> read)
    {
        ArgumentNullException.ThrowIfNull(timeline);

        return For(
            (entity, tick) =>
                timeline.TrackFor(entity, tick) is { } track &&
                !timeline.Dormancy.IsDormant(entity, (int)tick) &&
                track.FactsAt(tick).AttachedTo is null &&
                track.At(tick) is { } pose
                    ? (pose, track.ModelPath)
                    : null,
            brushes,
            read,
            static work => System.Threading.ThreadPool.QueueUserWorkItem(_ => work()));
    }

    /// <summary>The timeline's lookup over any pose lookup, so a test can drive it.</summary>
    /// <param name="poseOf">An entity's pose and model at a tick, or null when the client would answer nothing.</param>
    /// <param name="brushes">The map's brush models.</param>
    /// <param name="read">
    /// The level's file reader, for a studio model's header. Called off the frame, so it must be safe from another thread,
    /// as `SoundCache` already requires of the same reader (a stream per read over immutable archives).
    /// </param>
    /// <param name="offFrame">Where a studio model's header is read: never on the frame, as `SoundCache` reads waves.</param>
    /// <returns>The lookup.</returns>
    /// <remarks>
    /// A studio model's radius is 0 until its header has been read, and the channel asks again while it holds 0
    /// (`ActiveLoops.Follow`), so it takes the radius a frame or so later rather than stalling the frame on the archive.
    /// </remarks>
    internal static Func<int, double, (float X, float Y, float Z, float Radius)?> For(
        Func<int, double, (ScenePose Pose, string ModelPath)?> poseOf,
        IReadOnlyList<BspModel>? brushes,
        Func<string, byte[]?> read,
        Action<Action> offFrame)
    {
        ArgumentNullException.ThrowIfNull(poseOf);
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(offFrame);

        // Every model's radius once a level: the engine computes it at load and only reads it after. Written by the
        // off-frame reads, so concurrent.
        ConcurrentDictionary<string, float> radii = new(StringComparer.OrdinalIgnoreCase);
        ConcurrentDictionary<string, byte> asked = new(StringComparer.OrdinalIgnoreCase);

        return (entity, tick) =>
        {
            if (poseOf(entity, tick) is not { } found)
            {
                return null;
            }

            (float x, float y, float z) = Of(found.Pose, found.ModelPath, brushes);

            return (x, y, z, Radius(found.ModelPath));
        };

        float Radius(string modelPath)
        {
            if (modelPath.Length == 0)
            {
                return 0f;
            }

            if (radii.TryGetValue(modelPath, out float radius))
            {
                return radius;
            }

            // A studio model asked once and still being read: no parse of the path again while it waits.
            if (asked.ContainsKey(modelPath))
            {
                return 0f;
            }

            if (Brush(modelPath, brushes) is { } brush)
            {
                return radii[modelPath] = BrushRadius(brush);
            }

            if (asked.TryAdd(modelPath, 0))
            {
                offFrame(() => radii[modelPath] = Studio(modelPath));
            }

            return 0f;
        }

        // A model the game cannot open has no radius, as a model_t that never loaded answers 0; kept, not retried.
        // **Every exception, on purpose**: this runs on a pool thread with nothing above it, where an unhandled one ends
        // the process (SoundCache's off-frame reads catch everything for the same reason), and the archives can be
        // closed under it by a demo or install change while the read is queued. The answer is the same either way: no
        // radius, so the channel pans as one with none.
        float Studio(string modelPath)
        {
            try
            {
                return read(modelPath) is { } bytes ? StudioRadius(StudioBounds.MovementHull(bytes)) : 0f;
            }
#pragma warning disable CA1031 // a pool thread has no caller to hand the exception to; see the comment above
            catch (Exception)
#pragma warning restore CA1031
            {
                return 0f;
            }
        }
    }

    private static BspModel? Brush(string? modelPath, IReadOnlyList<BspModel>? brushes)
    {
        int index = BspModels.IndexOf(modelPath);

        return index < 0 || brushes is null || index >= brushes.Count ? null : brushes[index];
    }
}
