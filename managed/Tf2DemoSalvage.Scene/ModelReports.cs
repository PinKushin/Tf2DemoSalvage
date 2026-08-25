using System;
using System.Collections.Generic;

using Microsoft.Extensions.Logging;

using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene;

/// <summary>
/// What the draw loop says about each model, once, so a question can be answered from a log.
/// </summary>
/// <remarks>
/// **Split out of the draw loop on 2026-08-24** (B181). It was about eighty of that loop's lines
/// and none of them draw anything — but deleting them was never the answer either. Every line here
/// exists because something was once diagnosable from the picture and from nothing in the log, and
/// this project's standing position is that a failure-only log reads clean while everything falls
/// back.
///
/// **What each one is deduped on is the whole design and differs per line**, which is why they are
/// together here rather than scattered: once per MODEL for a fact about the model, once per ENTITY
/// for a fact about where one stands, and on a change of more than a unit for a brush entity that
/// moves. Getting that wrong is how a per-frame line printed 1,280 times a second (B163) and how a
/// once-per-model line let a bright control point silence a dark one for ever.
/// </remarks>
public sealed class ModelReports
{
    private readonly ILogger _render;

    /// <summary>Creates a reporter.</summary>
    /// <param name="render">Where the lines go, under <c>render</c> (D83).</param>
    /// <exception cref="ArgumentNullException"><paramref name="render"/> is null.</exception>
    public ModelReports(ILogger render)
    {
        ArgumentNullException.ThrowIfNull(render);

        _render = render;
    }

    /// <summary>The height each brush entity was last reported at, so movement can be logged.</summary>
    private readonly Dictionary<int, float> _brushHeight = [];

    /// <summary>Entities whose sampled light has been reported, one line each.</summary>
    private readonly HashSet<int> _reportedLight = [];

    /// <summary>Models whose baked frame selection has been reported.</summary>
    private readonly HashSet<string> _reportedFrames = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Where a brush entity actually lands, every time it moves.</summary>
    /// <param name="prop">The prop.</param>
    /// <param name="seconds">Demo time, so the trace reads against the demo's own keyframes.</param>
    /// <exception cref="ArgumentNullException"><paramref name="prop"/> is null.</exception>
    /// <remarks>
    /// **The one thing the BSP and the demo cannot answer between them (B94).** The map says
    /// submodel 80 spans −64 to 80 about its own origin and the demo says that origin rests at 640
    /// and rises to 785, so the shutter should occupy 576..720 closed. Whether it does is a fact
    /// about the transform.
    ///
    /// **Every movement, not the first sighting.** Reporting once per entity was enough to find
    /// where the gates are and useless for finding out what one DOES: a shutter that sinks below
    /// its frame does so over a handful of frames, and the one line already written came from long
    /// before. Logged on a change of more than a unit, so a stationary door stays silent.
    /// </remarks>
    public void BrushMoved(SceneProp prop, double seconds)
    {
        if (prop.Kind != SceneModelKind.Brush)
        {
            return;
        }

        ScenePose pose = prop.Pose;

        if (_brushHeight.TryGetValue(prop.EntityIndex, out float lastZ) &&
            Math.Abs(lastZ - pose.Z) <= 1f)
        {
            return;
        }

        _brushHeight[prop.EntityIndex] = pose.Z;

        _render.LogInformation(
            "{Message}",
            $"brush {prop.ModelPath} #{prop.EntityIndex} at " +
            $"({pose.X:0},{pose.Y:0},{pose.Z:0.##}) seconds {seconds:0.###}");
    }

    /// <summary>What light one entity was drawn with, once per entity.</summary>
    /// <param name="prop">The prop.</param>
    /// <param name="lit">What the lighting sampler returned.</param>
    /// <param name="skin">Which skin family it drew with.</param>
    /// <exception cref="ArgumentNullException"><paramref name="prop"/> is null.</exception>
    /// <remarks>
    /// **Per INSTANCE rather than per model, because a per-model line cannot see the defect it was
    /// written for.** Five capture points sharing <c>cap_point_base.mdl</c> collapse to one report,
    /// and a bright one reporting first silences a dark one for ever — while the observation being
    /// chased was that ONE control point is dark and its neighbours are fine. That shape rules out a
    /// missing lighting term, since an absent term darkens every instance equally, so the question
    /// is what THIS instance sampled.
    /// </remarks>
    public void Lit(SceneProp prop, ModelLight lit, int skin)
    {
        if (!_reportedLight.Add(prop.EntityIndex))
        {
            return;
        }

        ScenePose pose = prop.Pose;

        _render.LogInformation(
            "{Message}",
            $"lit {System.IO.Path.GetFileName(prop.ModelPath)} #{prop.EntityIndex} " +
            $"at ({pose.X:0},{pose.Y:0},{pose.Z:0}) sampled ({lit.X:0},{lit.Y:0},{lit.Z:0}) " +
            $"skin {skin} " +

            // **Which lighting model, said out loud.** A brush entity has no cube, so a luminance
            // printed for it would be a number about nothing. Saying "lightmapped" is what lets
            // this line answer "why is that door flat" without a second run.
            (lit.Light is { } cube
                ? $"luminance {AmbientCube.Luminance(cube):0.####}"
                : "lightmapped"));
    }

    /// <summary>Which baked frame a model's sequence and cycle selected, once per model.</summary>
    /// <param name="prop">The prop.</param>
    /// <param name="frame">The baked frame chosen.</param>
    /// <param name="frames">How many baked frames the model has.</param>
    /// <param name="blend">How far toward the next one.</param>
    /// <exception cref="ArgumentNullException"><paramref name="prop"/> is null.</exception>
    public void Animating(SceneProp prop, int frame, int frames, float blend)
    {
        if (!_reportedFrames.Add(prop.ModelPath))
        {
            return;
        }

        ScenePose pose = prop.Pose;

        _render.LogInformation(
            "{Message}",
            $"animating {prop.ModelPath}: sequence {pose.Sequence} cycle {pose.Cycle:0.###} " +
            $"-> baked frame {frame} of {frames} " +
            $"blend {blend:0.###} yaw {pose.Yaw:0.##} at ({pose.X:0},{pose.Y:0},{pose.Z:0})");
    }

    /// <summary>Whether a model has already had a named report of some kind.</summary>
    /// <param name="key">The model path, with a suffix naming which report.</param>
    /// <returns>Whether this is the first time it has been asked.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is null.</exception>
    /// <remarks>
    /// Shared with the skinned-model and posed-extents lines, which are keyed by
    /// <c>path + "#skin"</c> and <c>path + "#worn"</c>. One set rather than three, because the only
    /// thing that distinguishes them IS the suffix.
    /// </remarks>
    public bool FirstTime(string key)
    {
        ArgumentNullException.ThrowIfNull(key);

        return _reportedFrames.Add(key);
    }

    /// <summary>Reports a line under the render category.</summary>
    /// <param name="message">The line.</param>
    /// <exception cref="ArgumentNullException"><paramref name="message"/> is null.</exception>
    public void Say(string message)
    {
        ArgumentNullException.ThrowIfNull(message);

        _render.LogInformation("{Message}", message);
    }
}
