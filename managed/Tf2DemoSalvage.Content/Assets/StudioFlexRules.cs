using System;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Content.Assets;

/// <summary>
/// From controller values to vertex deltas: <c>CStudioHdr::RunFlexRules</c> (<c>studio.cpp:1407</c>), then each mesh
/// flex's weight and its vertices' share of it (B513).
/// </summary>
/// <remarks>
/// **The controller values are indexed LOCALLY.** The engine indexes <c>src</c> by
/// <c>pFlexcontroller( i )-&gt;localToGlobal</c>, and its global list is keyed by name
/// (<c>C_BaseFlex::AddGlobalFlexController</c>), so for one model a local index and its global slot name the same
/// controller; only two local controllers sharing a name would differ, which no shipped model does.
///
/// **The vertex weight is the published GPU morph's** (<c>morphaccumulate_ps30.fxc</c>): speed lerps delayed to
/// current, side lerps the descriptor to its stereo partner. The CPU path (<c>R_StudioFlexVerts</c>,
/// <c>studiorender.dll</c>) is closed; the target ramp below is its <c>RampFlexWeight</c> as the target fields
/// (<c>studio.h:1149-1152</c>) define it — **interpolated, not read in disassembly**. With TF2's player targets of
/// 0/1/10/11 (measured, <c>flex</c> probe) it is the identity on [0, 1] and zero below.
/// </remarks>
public static class StudioFlexRules
{
    /// <summary><c>STUDIO_FLEX_STACK</c>, <c>studio.cpp:18</c>.</summary>
    public const int StackSize = 32;

    // studio.h:3035-3055.
    private const int Const = 1;
    private const int Fetch1 = 2;
    private const int Fetch2 = 3;
    private const int Add = 4;
    private const int Sub = 5;
    private const int Mul = 6;
    private const int Div = 7;
    private const int Neg = 8;
    private const int Max = 13;
    private const int Min = 14;
    private const int TwoWay0 = 15;
    private const int TwoWay1 = 16;
    private const int NWay = 17;
    private const int Combo = 18;
    private const int Dominate = 19;
    private const int DmeLowerEyelid = 20;
    private const int DmeUpperEyelid = 21;

    /// <summary><c>CStudioHdr::RunFlexRules</c>, op for op.</summary>
    /// <param name="data">The model's tables.</param>
    /// <param name="src">Controller values in their own ranges (not 0-1), one per local controller.</param>
    /// <param name="dest">One weight per descriptor; zeroed first, as the engine does.</param>
    /// <remarks>
    /// **A failed precondition turns its op into a no-op and the rule carries on** — the SDK's <c>CHECK</c> macro
    /// breaks out of the switch, not out of the loop (<c>studio.cpp:1444</c>). The stack is zeroed per rule, so a
    /// combo of zero values reads a zero rather than a stale slot.
    /// </remarks>
    public static void Run(StudioFlexData data, ReadOnlySpan<float> src, Span<float> dest)
    {
        ArgumentNullException.ThrowIfNull(data);

        int descriptors = data.Descriptors.Count;
        int controllers = data.Controllers.Count;

        dest[..Math.Min(descriptors, dest.Length)].Clear();

        Span<float> stack = stackalloc float[StackSize];

        foreach (StudioFlexRule rule in data.Rules)
        {
            if (rule.Flex < 0 || rule.Flex >= descriptors || rule.Flex >= dest.Length)
            {
                continue;
            }

            stack.Clear();
            int k = 0;

            foreach (StudioFlexOp op in rule.Ops)
            {
                k = Step(data, op, stack, k, src, dest, controllers, descriptors);
            }

            dest[rule.Flex] = stack[0];
        }
    }

    private static bool Controller(int index, int controllers) => index >= 0 && index < controllers;

    private static float Source(ReadOnlySpan<float> src, int index) => index < src.Length ? src[index] : 0f;

    /// <summary>One op; returns the new stack pointer.</summary>
    private static int Step(
        StudioFlexData data,
        StudioFlexOp op,
        Span<float> stack,
        int k,
        ReadOnlySpan<float> src,
        Span<float> dest,
        int controllers,
        int descriptors)
    {
        switch (op.Op)
        {
            case Add when k >= 2: stack[k - 2] += stack[k - 1]; return k - 1;
            case Sub when k >= 2: stack[k - 2] -= stack[k - 1]; return k - 1;
            case Mul when k >= 2: stack[k - 2] *= stack[k - 1]; return k - 1;
            case Div when k >= 2:
                // `stack[k-1] > 0.0001` compares against a DOUBLE literal.
                stack[k - 2] = stack[k - 1] > 0.0001d ? stack[k - 2] / stack[k - 1] : 0f;
                return k - 1;
            case Neg when k >= 1: stack[k - 1] = -stack[k - 1]; return k;
            case Max when k >= 2: stack[k - 2] = MathF.Max(stack[k - 2], stack[k - 1]); return k - 1;
            case Min when k >= 2: stack[k - 2] = MathF.Min(stack[k - 2], stack[k - 1]); return k - 1;
            case Const when k <= StackSize - 1: stack[k] = op.Value; return k + 1;
            case Fetch1 when k <= StackSize - 1 && Controller(op.Index, controllers):
                stack[k] = Source(src, op.Index);
                return k + 1;
            case Fetch2 when k <= StackSize - 1 && op.Index >= 0 && op.Index < descriptors:
                stack[k] = dest[op.Index];
                return k + 1;
            case Combo:
                return ComboOp(op.Index, stack, k, controllers);
            case Dominate:
                return DominateOp(op.Index, stack, k, controllers);
            case TwoWay0 when k <= StackSize - 1 && Controller(op.Index, controllers):
                stack[k] = RemapValClamped(Source(src, op.Index), -1f, 0f, 1f, 0f);
                return k + 1;
            case TwoWay1 when k <= StackSize - 1 && Controller(op.Index, controllers):
                stack[k] = RemapValClamped(Source(src, op.Index), 0f, 1f, 0f, 1f);
                return k + 1;
            case NWay:
                return NWayOp(op.Index, stack, k, src, controllers);
            case DmeLowerEyelid:
            case DmeUpperEyelid:
                return Eyelid(data, op, stack, k, src, controllers);
            default:
                return k;
        }
    }

    private static int ComboOp(int m, Span<float> stack, int k, int controllers)
    {
        if (!Controller(m, controllers) || k < m || (m == 0 && k > StackSize - 1))
        {
            return k;
        }

        int km = k - m;

        for (int at = km + 1; at < k; at++)
        {
            stack[km] *= stack[at];
        }

        return k - m + 1;
    }

    private static int DominateOp(int m, Span<float> stack, int k, int controllers)
    {
        if (!Controller(m, controllers) || k < m + 1)
        {
            return k;
        }

        int km = k - m;
        float dv = stack[km];

        for (int at = km + 1; at < k; at++)
        {
            dv *= stack[at];
        }

        stack[km - 1] *= 1f - dv;

        return k - m;
    }

    private static int NWayOp(int index, Span<float> stack, int k, ReadOnlySpan<float> src, int controllers)
    {
        if (k < 5 || !Controller(index, controllers))
        {
            return k;
        }

        int valueController = (int)stack[k - 1];

        if (!Controller(valueController, controllers))
        {
            return k;
        }

        float value = Source(src, valueController);
        (float x, float y, float z, float w) = (stack[k - 5], stack[k - 4], stack[k - 3], stack[k - 2]);

        if (value <= x || value >= w)
        {
            value = 0f;
        }
        else if (value < y)
        {
            value = RemapValClamped(value, x, y, 0f, 1f);
        }
        else if (value > z)
        {
            value = RemapValClamped(value, z, w, 1f, 0f);
        }
        else
        {
            value = 1f;
        }

        stack[k - 5] = value * Source(src, index);

        return k - 4;
    }

    private static int Eyelid(
        StudioFlexData data, StudioFlexOp op, Span<float> stack, int k, ReadOnlySpan<float> src, int controllers)
    {
        if (k < 3 ||
            !Controller(op.Index, controllers) ||
            !Controller((int)stack[k - 1], controllers) ||
            !Controller((int)stack[k - 2], controllers) ||
            !Controller((int)stack[k - 3], controllers))
        {
            return k;
        }

        float closeLidV = Normalised(data, src, op.Index, 0f, 1f);
        float closeLid = Normalised(data, src, (int)stack[k - 1], 0f, 1f);

        int eyeUpDown = (int)stack[k - 3];
        float upDown = eyeUpDown >= 0 ? Normalised(data, src, eyeUpDown, -1f, 1f) : 0f;

        // `stack[ k - 2 ]` is validated and never read: TF2 stacks the blink controller there.
        if (op.Op == DmeLowerEyelid)
        {
            stack[k - 3] = upDown > 0f ? (1f - upDown) * (1f - closeLidV) * closeLid : (1f - closeLidV) * closeLid;
        }
        else
        {
            stack[k - 3] = upDown < 0f ? (1f + upDown) * closeLidV * closeLid : closeLidV * closeLid;
        }

        return k - 2;
    }

    /// <summary><c>RemapValClamped( src[ c ], c-&gt;min, c-&gt;max, low, high )</c>.</summary>
    private static float Normalised(StudioFlexData data, ReadOnlySpan<float> src, int controller, float low, float high)
    {
        StudioFlexController range = data.Controllers[controller];

        return RemapValClamped(Source(src, controller), range.Min, range.Max, low, high);
    }

    /// <summary><c>RemapValClamped</c>, <c>mathlib.h</c>.</summary>
    public static float RemapValClamped(float value, float a, float b, float c, float d)
    {
        // S1244 is a false positive here: the engine's own guard is this exact comparison (`mathlib.h:621`).
#pragma warning disable S1244
        if (a == b)
#pragma warning restore S1244
        {
            return value >= b ? d : c;
        }

        float fraction = Math.Clamp((value - a) / (b - a), 0f, 1f);

        return c + ((d - c) * fraction);
    }

    /// <summary>A mesh flex's target ramp: off outside (target0, target3), rising to target1, flat, falling from target2.</summary>
    /// <param name="flex">The flex.</param>
    /// <param name="weight">The descriptor's weight.</param>
    /// <returns>Its effective weight.</returns>
    public static float Ramp(StudioMeshFlex flex, float weight)
    {
        ArgumentNullException.ThrowIfNull(flex);

        if (weight <= flex.Target0 || weight >= flex.Target3)
        {
            return 0f;
        }

        if (weight < flex.Target1)
        {
            return (weight - flex.Target0) / (flex.Target1 - flex.Target0);
        }

        return weight > flex.Target2 ? (flex.Target3 - weight) / (flex.Target3 - flex.Target2) : 1f;
    }

    /// <summary>Adds every flex's deltas to per-vertex position and normal deltas.</summary>
    /// <param name="data">The model's tables.</param>
    /// <param name="weights">Descriptor weights now (<c>RunFlexRules</c>'s output).</param>
    /// <param name="delayed">The delayed weights; pass <paramref name="weights"/> for a model with no smoothing.</param>
    /// <param name="positions">Three floats per <c>.vvd</c> vertex, added to.</param>
    /// <param name="normals">Three floats per <c>.vvd</c> vertex, added to.</param>
    /// <returns>How many vertex animations contributed.</returns>
    public static int Accumulate(
        StudioFlexData data,
        ReadOnlySpan<float> weights,
        ReadOnlySpan<float> delayed,
        Span<float> positions,
        Span<float> normals)
    {
        ArgumentNullException.ThrowIfNull(data);

        int applied = 0;

        foreach (StudioMeshFlex flex in data.Flexes)
        {
            float w1 = Ramp(flex, At(weights, flex.FlexDesc));
            float w2 = Ramp(flex, At(delayed, flex.FlexDesc));
            float w3 = w1;
            float w4 = w2;

            // A non-stereo flex names pair 0; descriptor 0 is a left half and never anyone's partner.
            if (flex.FlexPair != 0)
            {
                w3 = Ramp(flex, At(weights, flex.FlexPair));
                w4 = Ramp(flex, At(delayed, flex.FlexPair));
            }

            // A ramped weight is never negative, so "at most zero" is "off".
            if (w1 <= 0f && w2 <= 0f && w3 <= 0f && w4 <= 0f)
            {
                continue;
            }

            foreach (StudioVertAnim anim in flex.Vertices)
            {
                int at = anim.Vertex * 3;

                if (at < 0 || at + 2 >= positions.Length || at + 2 >= normals.Length)
                {
                    continue;
                }

                float speed = anim.Speed / 255f;
                float side = anim.Side / 255f;
                float weight = Lerp(Lerp(w2, w1, speed), Lerp(w4, w3, speed), side);

                positions[at] += anim.Delta.X * weight;
                positions[at + 1] += anim.Delta.Y * weight;
                positions[at + 2] += anim.Delta.Z * weight;
                normals[at] += anim.NormalDelta.X * weight;
                normals[at + 1] += anim.NormalDelta.Y * weight;
                normals[at + 2] += anim.NormalDelta.Z * weight;
                applied++;
            }
        }

        return applied;
    }

    private static float At(ReadOnlySpan<float> values, int index) =>
        index >= 0 && index < values.Length ? values[index] : 0f;

    /// <summary>HLSL <c>lerp( a, b, t )</c>.</summary>
    private static float Lerp(float a, float b, float t) => a + ((b - a) * t);

    /// <summary>The controller values a model starts from before any scene: the unsent weight 0 rescaled.</summary>
    /// <param name="data">The model's tables.</param>
    /// <returns>Each controller's <c>min</c> (<c>c_baseflex.cpp:1222</c> with <c>m_flexWeight</c> zero).</returns>
    public static float[] Resting(StudioFlexData data)
    {
        ArgumentNullException.ThrowIfNull(data);

        IReadOnlyList<StudioFlexController> controllers = data.Controllers;
        float[] values = new float[controllers.Count];

        for (int index = 0; index < values.Length; index++)
        {
            values[index] = controllers[index].Min;
        }

        return values;
    }
}
