using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene;

/// <summary>
/// A TF2 player's face at a moment: <c>C_BaseFlex::SetupGlobalWeights</c> and <c>SetupLocalWeights</c> as they run for
/// a player, then the vertex morph (B513).
/// </summary>
/// <remarks>
/// **What a TF2 player's face is driven by, and what it is not.** <c>DT_TFPlayer</c> excludes
/// <c>m_flexWeight</c>, <c>m_blinktoggle</c> and <c>m_viewtarget</c> (<c>tf_player.cpp:782-784</c>), so the
/// networked weights stay zero — rescaled to each controller's MIN (<c>c_baseflex.cpp:1222</c>) — and the blink never
/// starts (<c>:1228</c>). What moves the face is the scene the player is in: each active <c>EXPRESSION</c> blends its
/// <c>.vfe</c> setting into the controllers (<c>AddFlexSetting</c>, <c>:1882-1883</c>), and the rules turn the
/// controllers into flex weights.
///
/// **Not ported, and why each is named:** <c>FLEXANIMATION</c> tracks (they write <c>m_flexWeight</c>, which
/// <c>ProcessSceneEvents( true )</c> decays by 0.95 per FRAME, <c>:1703</c> — frame-rate state a seeking viewer has no
/// closed form for; 1,373 such events in the archive, mostly naming HL2 controllers TF2's models lack); lip sync from
/// <c>SPEAK</c> phonemes (<c>ProcessVisemes</c>, <c>:922</c>, reads the <c>.wav</c>'s phoneme chunk); the eyes'
/// <c>SetViewTarget</c>, which runs after the rules and moves eyeballs, not vertices; and <c>RunFlexDelay</c>, which
/// every shipped player vertex ignores (all speeds 255, measured with the <c>flex</c> probe).
/// </remarks>
public static class FaceFlex
{
    /// <summary>The controller values for a player whose scene is <paramref name="taunt"/>, at scene time.</summary>
    /// <param name="data">The model's flex tables.</param>
    /// <param name="taunt">The scene's plan; null for a player in no scene.</param>
    /// <param name="sceneSeconds">The scene's own clock, already folded by its loop.</param>
    /// <returns>One value per local controller, each in its own range.</returns>
    public static float[] Controllers(StudioFlexData data, SceneTaunt? taunt, float sceneSeconds)
    {
        ArgumentNullException.ThrowIfNull(data);

        float[] src = StudioFlexRules.Resting(data);

        if (taunt is null || taunt.Expressions.Count == 0)
        {
            return src;
        }

        float global = ChoreoCurve.Intensity(taunt.SceneRamp, sceneSeconds, taunt.Duration, hasEndTime: true);

        foreach (SceneExpression expression in taunt.Expressions)
        {
            // `IsTimeInRange`, inclusive at both ends (choreoscene.cpp:2403).
            if (sceneSeconds < expression.Start || sceneSeconds > expression.End)
            {
                continue;
            }

            float scale = global * ChoreoCurve.Intensity(
                expression.Ramp, sceneSeconds - expression.Start, expression.End - expression.Start, hasEndTime: true);

            foreach (SceneExpressionWeight weight in expression.Weights)
            {
                int controller = data.FindController(weight.Controller);

                if (controller < 0)
                {
                    continue;
                }

                float s = Math.Clamp(scale * weight.Influence, 0f, 1f);
                src[controller] = (src[controller] * (1f - s)) + (weight.Weight * s);
            }
        }

        return src;
    }

    /// <summary>The per-<c>.vvd</c>-vertex deltas the controllers produce, or null when they move nothing.</summary>
    /// <param name="data">The model's flex tables.</param>
    /// <param name="src">Controller values, from <see cref="Controllers"/>.</param>
    /// <param name="vertexCount">How many vertices the model's <c>.vvd</c> holds.</param>
    /// <returns>Position deltas then normal deltas, three floats per vertex each, or null.</returns>
    public static (float[] Positions, float[] Normals)? Deltas(StudioFlexData data, float[] src, int vertexCount)
    {
        ArgumentNullException.ThrowIfNull(data);

        float[] weights = new float[data.Descriptors.Count];
        StudioFlexRules.Run(data, src, weights);

        float[] positions = new float[vertexCount * 3];
        float[] normals = new float[vertexCount * 3];

        return StudioFlexRules.Accumulate(data, weights, weights, positions, normals) > 0 ? (positions, normals) : null;
    }

    /// <summary>Spreads per-vertex deltas over the uploaded buffer's order: six floats per buffer vertex.</summary>
    /// <param name="deltas">From <see cref="Deltas"/>.</param>
    /// <param name="cornerVertex">Each corner's <c>.vvd</c> vertex, in the model's corner order.</param>
    /// <param name="packedOrder">Each buffer vertex's corner.</param>
    /// <returns>Position delta then normal delta for every buffer vertex.</returns>
    public static float[] InBufferOrder(
        (float[] Positions, float[] Normals) deltas, IReadOnlyList<int> cornerVertex, IReadOnlyList<int> packedOrder)
    {
        ArgumentNullException.ThrowIfNull(cornerVertex);
        ArgumentNullException.ThrowIfNull(packedOrder);

        float[] stream = new float[packedOrder.Count * 6];

        for (int at = 0; at < packedOrder.Count; at++)
        {
            int corner = packedOrder[at];

            if (corner < 0 || corner >= cornerVertex.Count)
            {
                continue;
            }

            int from = cornerVertex[corner] * 3;

            if (from < 0 || from + 2 >= deltas.Positions.Length)
            {
                continue;
            }

            int to = at * 6;
            stream[to] = deltas.Positions[from];
            stream[to + 1] = deltas.Positions[from + 1];
            stream[to + 2] = deltas.Positions[from + 2];
            stream[to + 3] = deltas.Normals[from];
            stream[to + 4] = deltas.Normals[from + 1];
            stream[to + 5] = deltas.Normals[from + 2];
        }

        return stream;
    }
}
