using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;

using Tf2DemoSalvage.Content.Bsp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene;

public sealed partial class EntityModelSet
{
    /// <summary>Reports a viewmodel whose posed VERTICES stop covering any space.</summary>
    /// <param name="modelPath">Which model.</param>
    /// <param name="bones">Its skinning matrices this frame.</param>
    /// <param name="body">The entity's <c>m_nBody</c>, so hidden bodygroups are not measured.</param>
    /// <remarks>
    /// **The real measurement, replacing a proxy that had no established meaning** (B222). The first
    /// attempt measured the span of the skinning matrices' translation columns — but a skinning
    /// matrix is <c>Concatenate(boneToWorld, poseToBone)</c>, so its translation is not the bone's
    /// world position and the span of those columns is a mixture of placement and bind offset. It
    /// correlated with the defect by timing and could not be interpreted.
    ///
    /// This applies the matrices to the model's own corners exactly as the GPU will, and measures
    /// the box they land in. That is the quantity the symptom is about: a model covering no space
    /// draws nothing, whatever its matrices look like individually.
    ///
    /// **Sampled every sixteenth corner**, because this runs per frame for the viewmodel rather than
    /// once per model. A collapse is a property of the whole set, so a sixteenth of it is ample —
    /// and the alternative is walking twenty thousand corners a frame to answer a yes/no.
    ///
    /// **Transition-logged**, like every other instrument this hunt produced: the event lasts 60 ms
    /// and a sampled line cannot see it. See `docs/memory/instrument-bugs-outnumber-decoder-bugs.md#log-the-event-not-a-sample-of-it`.
    /// </remarks>
    private void ReportPosedSize(string modelPath, IReadOnlyList<float[]> bones, int body)
    {
        if (!_raw.TryGetValue(modelPath, out IReadOnlyList<PropVertex>? corners) ||
            corners.Count == 0)
        {
            return;
        }

        // **Hidden bodygroups are excluded, and leaving them in made this instrument lie** (B222).
        // The first version measured every corner, so a StatTrak module — a bodygroup the entity is
        // NOT showing, hanging off `c_weapon_stattrack`, which is the one bone that does not merge
        // onto the arms — drifted with the weapon's own animation and inflated the measured span
        // from 27 units to 97 on a 28-unit model. That read as the weapon being torn apart when
        // nothing drawn had moved at all.
        //
        // A measurement of geometry the renderer discards is a measurement of nothing, and this one
        // produced a confident finding before it was caught.
        IReadOnlyList<(int Base, int Count)>? parts =
            _frames.TryGetValue(modelPath, out PropModels.ModelFrames? frames)
                ? frames.BodyParts
                : null;

        float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;

        // **Where each BONE's own vertices end up, so the outlier can be named rather than
        // inferred** (B222). The span alone says the model is stretched; it cannot say which bone is
        // dragging it. Accumulated per bone over the vertices that are weighted mostly to it — the
        // one whose centroid sits far from the others is the bone that is not following the rest,
        // and that is the last hop between "the weapon is the wrong shape" and a cause.
        Span<float> boneX = stackalloc float[MaximumReportedBones];
        Span<float> boneY = stackalloc float[MaximumReportedBones];
        Span<float> boneZ = stackalloc float[MaximumReportedBones];
        Span<int> boneCount = stackalloc int[MaximumReportedBones];

        boneX.Clear();
        boneY.Clear();
        boneZ.Clear();
        boneCount.Clear();

        for (int at = 0; at < corners.Count; at += 16)
        {
            PropVertex corner = corners[at];

            float total = corner.Weights.First + corner.Weights.Second + corner.Weights.Third;

            if (total <= 0f || !Shows(parts, corner.BodyPart, corner.BodyModel, body))
            {
                continue;
            }

            Span<byte> which = [corner.Bones.First, corner.Bones.Second, corner.Bones.Third];
            Span<float> howMuch =
                [corner.Weights.First, corner.Weights.Second, corner.Weights.Third];

            float x = 0f, y = 0f, z = 0f;

            for (int slot = 0; slot < 3; slot++)
            {
                if (howMuch[slot] <= 0f || which[slot] >= bones.Count)
                {
                    continue;
                }

                float[] m = bones[which[slot]];
                float share = howMuch[slot] / total;

                x += share * ((m[0] * corner.X) + (m[1] * corner.Y) + (m[2] * corner.Z) + m[3]);
                y += share * ((m[4] * corner.X) + (m[5] * corner.Y) + (m[6] * corner.Z) + m[7]);
                z += share * ((m[8] * corner.X) + (m[9] * corner.Y) + (m[10] * corner.Z) + m[11]);
            }

            minX = MathF.Min(minX, x);
            minY = MathF.Min(minY, y);
            minZ = MathF.Min(minZ, z);
            maxX = MathF.Max(maxX, x);
            maxY = MathF.Max(maxY, y);
            maxZ = MathF.Max(maxZ, z);

            // Attributed to the bone carrying the most of this vertex, which is the one that
            // decides where it lands.
            int dominant = corner.Bones.First;
            float heaviest = corner.Weights.First;

            if (corner.Weights.Second > heaviest)
            {
                dominant = corner.Bones.Second;
                heaviest = corner.Weights.Second;
            }

            if (corner.Weights.Third > heaviest)
            {
                dominant = corner.Bones.Third;
            }

            if (dominant < MaximumReportedBones)
            {
                boneX[dominant] += x;
                boneY[dominant] += y;
                boneZ[dominant] += z;
                boneCount[dominant]++;
            }
        }

        if (maxX < minX)
        {
            return;
        }

        float size = MathF.Max(maxX - minX, MathF.Max(maxY - minY, maxZ - minZ));

        // A weapon is tens of units long. Under one unit it is not on screen as anything.
        string band = size < 1f ? "NO SIZE" : "drawable";

        (float X, float Y, float Z) centre =
            ((minX + maxX) / 2f, (minY + maxY) / 2f, (minZ + maxZ) / 2f);

        // **Where it is relative to the HANDS, from the same posed vertices.** Size alone answered
        // its question — every viewmodel stays a healthy 28 to 65 units across, including while the
        // weapon is invisible — so what is left is placement, and this is the only measurement of it
        // that means anything: both centres come from real corners under the real matrices, taken in
        // the SAME frame. An earlier attempt compared two centres three seconds apart and reported a
        // 4,400-unit error that did not exist (B222).
        if (modelPath.Contains("_arms", StringComparison.OrdinalIgnoreCase))
        {
            _armsCentre = centre;
        }

        // **Where the ARMS are relative to the EYE, which nothing has measured.** Every placement
        // number so far has been the weapon's distance from the arms, and the arms reported only
        // "arms" — so a pose that carries the whole viewmodel behind the near plane keeps its size,
        // keeps its shape, keeps the weapon correctly attached to it, and blanks both models with
        // every instrument silent. That is the shape of the reported defect: sticky-specific (the
        // charge is what changes the arms' sequence), and both models vanishing together (the weapon
        // merges onto the arms, so it goes wherever they go).
        string place = "arms";

        if (modelPath.Contains("_arms", StringComparison.OrdinalIgnoreCase) &&
            ViewmodelEye is { } eye)
        {
            float ex = centre.X - eye.X;
            float ey = centre.Y - eye.Y;
            float ez = centre.Z - eye.Z;

            float fromEye = MathF.Sqrt((ex * ex) + (ey * ey) + (ez * ez));

            place = $"{(int)(fromEye / 5f) * 5}-{((int)(fromEye / 5f) * 5) + 5}u from eye";
        }

        if (_armsCentre is { } hands &&
            !modelPath.Contains("_arms", StringComparison.OrdinalIgnoreCase))
        {
            float dx = centre.X - hands.X;
            float dy = centre.Y - hands.Y;
            float dz = centre.Z - hands.Z;

            // **Bucketed to five units, not thresholded at a hundred.** The first version asked
            // "is it further than 100 units from the hands" and answered no throughout, which is
            // true and useless: a weapon fifty units from the eye is entirely off screen and reads
            // as "with the hands". A weapon sits within tens of units of the eye, so the bucket has
            // to be of that order for movement to appear at all. Same mistake as the one-second
            // sample and the hundred-unit threshold before it — a resolution chosen without asking
            // what size of effect had to be visible.
            float away = MathF.Sqrt((dx * dx) + (dy * dy) + (dz * dz));

            place = $"{(int)(away / 5f) * 5}-{((int)(away / 5f) * 5) + 5}u from hands";
        }

        _posedSize.TryGetValue(modelPath, out string? was);

        if ($"{band}/{place}" == was)
        {
            return;
        }

        _posedSize[modelPath] = $"{band}/{place}";

        // **The per-bone centroids, printed when the model is stretched.** A sticky launcher is 28
        // units long; past 40 it is being pulled apart, and this says by which bone. Their names
        // come from the `bone merge` line, which prints `name[index]`.
        string spread = string.Empty;

        // 35 rather than 40, so the ARMS report too. Their normal span is around 28 and they have
        // been measured at 49 to 55 — which was read as ordinary variation all evening and may be
        // the same stretch, less obvious only because far fewer of their vertices ride the bone
        // that is out of place.
        if (size > 35f)
        {
            List<string> where = [];

            for (int bone = 0; bone < MaximumReportedBones; bone++)
            {
                if (boneCount[bone] == 0)
                {
                    continue;
                }

                where.Add(
                    $"[{bone}] {boneCount[bone]}v at ({boneX[bone] / boneCount[bone]:0.#}, " +
                    $"{boneY[bone] / boneCount[bone]:0.#}, {boneZ[bone] / boneCount[bone]:0.#})");
            }

            spread = $" bones: {string.Join("; ", where)}";
        }

        _props.LogDebug(
            "{Message}",
            $"{System.IO.Path.GetFileNameWithoutExtension(modelPath)} posed size changed: " +
            $"{was ?? "(first)"} -> {band}/{place}, {size:0.##} units across, " +
            $"at ({centre.X:0.#}, {centre.Y:0.#}, {centre.Z:0.#}){spread}");
    }

    /// <summary>How many bones the stretch report will attribute vertices to.</summary>
    /// <remarks>
    /// A viewmodel weapon has a handful — the sticky launcher has five. Bounded so the accumulators
    /// can sit on the stack and a player-sized skeleton cannot be walked here by accident.
    /// </remarks>
    private const int MaximumReportedBones = 16;

    /// <summary>Whether a corner's body part is the alternative this entity shows.</summary>
    /// <remarks>
    /// <c>GetBodygroup</c>, <c>shared/animation.cpp:876</c> — a part's choice is the body number
    /// divided by that part's base, modulo how many alternatives it has. Same arithmetic the
    /// renderer applies to batches; applied here so a diagnostic measures what is DRAWN.
    /// </remarks>
    private static bool Shows(
        IReadOnlyList<(int Base, int Count)>? parts, int bodyPart, int bodyModel, int body)
    {
        if (parts is not { Count: > 0 } || bodyPart < 0 || bodyPart >= parts.Count)
        {
            return bodyModel == 0;
        }

        (int place, int count) = parts[bodyPart];

        return place <= 0 || count <= 0
            ? bodyModel == 0
            : bodyModel == (body / place) % count;
    }

    /// <summary>Whether each viewmodel's posed vertices last covered any space, and where.</summary>
    private readonly Dictionary<string, string> _posedSize = [];

    /// <summary>Where the first-person eye is, so a viewmodel can be measured against it (B222).</summary>
    /// <remarks>
    /// Set by the caller that knows the camera, before the viewmodel props are instanced. Null for
    /// every world pass, which is what keeps this out of the two hundred props a frame.
    /// </remarks>
    public (float X, float Y, float Z)? ViewmodelEye { get; set; }

    /// <summary>The view a viewmodel is drawn through, for <c>FormatViewModelAttachment</c>.</summary>
    /// <remarks>
    /// **Set by the caller that knows the camera, beside <see cref="ViewmodelEye"/>**, and null for
    /// every world pass — which is what keeps the correction off the two hundred props a frame that
    /// must not receive it. `SetupBones_AttachmentHelper` calls the correction unconditionally and
    /// relies on the base implementation being an empty body; null here is that empty body.
    ///
    /// Carries the whole of what `FormatViewModelAttachment` reads out of `CViewSetup` and the main
    /// view vectors, so the correction cannot derive any of it a second way.
    /// </remarks>
    public ViewmodelProjection? ViewmodelProjection { get; set; }

    /// <summary>Where the view is, for the distance fade; null leaves everything unfaded.</summary>
    /// <remarks>
    /// **Set from the same `MomentInfo` the frustum came from** (B268), so the fade and the cull
    /// measure from one camera. Null when no eye has been established — a frame before a demo is
    /// open — and the fade then answers 255 rather than measuring from the map origin, which
    /// would fade out everything far from (0,0,0).
    /// </remarks>
    public (float X, float Y, float Z)? ViewOrigin { get; set; }

    /// <summary>The main view a static prop's screen width is measured through (B432); null measures none.</summary>
    public ScreenFadeView? ScreenView { get; set; }

    /// <summary>The level screen fade, from <c>worldspawn</c> when a map is read (B432); the default is disabled.</summary>
    public ScreenFadeRange LevelScreenFade { get; set; }

    /// <summary>Where the arms were posed this frame, as the weapon's reference.</summary>
    /// <remarks>
    /// The props are built arms first, so this is set before any weapon consults it. Null until the
    /// first arms model is posed, which is the state where a weapon has nothing to be measured
    /// against and is reported as being with the hands rather than guessed at.
    /// </remarks>
    private (float X, float Y, float Z)? _armsCentre;

    /// <summary>Measures a skinned model with its pose applied on the processor.</summary>
    private void ReportPosedExtents(
        string modelPath, IReadOnlyList<float[]> bones, string? label = null)
    {
        if (!_raw.TryGetValue(modelPath, out IReadOnlyList<PropVertex>? corners))
        {
            return;
        }

        float minimumX = float.MaxValue, minimumY = float.MaxValue, minimumZ = float.MaxValue;
        float maximumX = float.MinValue, maximumY = float.MinValue, maximumZ = float.MinValue;
        int weighted = 0;

        foreach (PropVertex corner in corners)
        {
            float total = corner.Weights.First + corner.Weights.Second + corner.Weights.Third;

            if (total <= 0f)
            {
                continue;
            }

            weighted++;

            Span<byte> which = [corner.Bones.First, corner.Bones.Second, corner.Bones.Third];
            Span<float> howMuch =
                [corner.Weights.First, corner.Weights.Second, corner.Weights.Third];

            float x = 0f, y = 0f, z = 0f;

            for (int slot = 0; slot < 3; slot++)
            {
                if (howMuch[slot] <= 0f || which[slot] >= bones.Count)
                {
                    continue;
                }

                float[] matrix = bones[which[slot]];
                float share = howMuch[slot] / total;

                x += share * ((matrix[0] * corner.X) + (matrix[1] * corner.Y) + (matrix[2] * corner.Z) + matrix[3]);
                y += share * ((matrix[4] * corner.X) + (matrix[5] * corner.Y) + (matrix[6] * corner.Z) + matrix[7]);
                z += share * ((matrix[8] * corner.X) + (matrix[9] * corner.Y) + (matrix[10] * corner.Z) + matrix[11]);
            }

            minimumX = MathF.Min(minimumX, x);
            minimumY = MathF.Min(minimumY, y);
            minimumZ = MathF.Min(minimumZ, z);
            maximumX = MathF.Max(maximumX, x);
            maximumY = MathF.Max(maximumY, y);
            maximumZ = MathF.Max(maximumZ, z);
        }

        // **All three ranges, because which axis is "up" is a property of the model.** A hat is a
        // few inches tall wherever it is, so its SIZE says nothing about whether it is on a head;
        // where it sits does. Reporting only the z range assumed z was up and this bind pose is
        // Y-up - a player model measures 84 along Y and bip_head rests at (0, 75, -1) - so a hat
        // correctly on the head reads as "z from -16 to -2" and looks like a hat on the floor.
        //
        // That mistake cost a full round of investigation, and it is the same one as measuring at
        // a tick the demo does not contain: an instrument answering confidently about the wrong
        // quantity.
        // **The root bone's own matrix, because extents cannot separate a rotation from a move.**
        // Two poses of one model came out as the same three ranges with the axes cycled, which is
        // a basis change rather than a different pose — but the extents alone cannot say whether
        // the translation came with it. The matrix can: its last column is where the root sits and
        // its first three are what it does to the axes.
        string root = bones.Count > 0 && bones[0] is { Length: >= 12 } first
            ? $"root [{first[0]:0.##} {first[1]:0.##} {first[2]:0.##} | " +
              $"{first[4]:0.##} {first[5]:0.##} {first[6]:0.##} | " +
              $"{first[8]:0.##} {first[9]:0.##} {first[10]:0.##}] " +
              $"at ({first[3]:0.#}, {first[7]:0.#}, {first[11]:0.#})"
            : "root none";

        // **Debug, and this one had the largest volume of the lot** — 338 lines in four minutes,
        // because it fires the first time each model, each worn item and each corner comparison is
        // posed, and models keep first appearing throughout a match. Each line is a disk flush
        // (B191), and this call also does real work to produce it: extents over every corner, plus
        // a second full skeleton built for the CORNER comparison.
        _props.LogDebug(
            "{Message}",
            $"posed {label ?? modelPath}: {weighted} of {corners.Count} corners weighted, " +
            $"{bones.Count} bones, x {minimumX:0.#}..{maximumX:0.#} " +
            $"y {minimumY:0.#}..{maximumY:0.#} z {minimumZ:0.#}..{maximumZ:0.#}, {root}");
    }
}
