using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Scene;

/// <summary>What the studio render writes into an eye material before its mesh draws (B725).</summary>
/// <param name="Origin"><c>$eyeorigin</c>: the bone's transform of the eye's <c>org</c>.</param>
/// <param name="Up"><c>$eyeup</c>: the eye's own <c>up</c>, in its bone's space — the engine does not rotate it.</param>
/// <param name="IrisU"><c>$irisu</c>, <c>eyeballstate_t.mat[0]</c>: the iris's planar projection, x y z then the offset.</param>
/// <param name="IrisV"><c>$irisv</c>, <c>mat[1]</c>.</param>
public readonly record struct EyeShading(
    (float X, float Y, float Z) Origin,
    (float X, float Y, float Z) Up,
    (float X, float Y, float Z, float W) IrisU,
    (float X, float Y, float Z, float W) IrisV);

/// <summary>What it writes into a teeth material: <c>$forward</c> and <c>$illumfactor</c> (B725).</summary>
/// <param name="Forward">The mouth's forward, world space, negated as the engine sends it.</param>
/// <param name="IllumFactor">The mouth's flex weight, clamped to [0, 1].</param>
public readonly record struct MouthShading((float X, float Y, float Z) Forward, float IllumFactor);

/// <summary>The studio render's eye and mouth setup, and the client's view target, ported (B725).</summary>
/// <remarks>
/// **Read** from TF2's x64 <c>studiorender.dll</c> (disassembled; the studio render is closed): <c>R_StudioEyeballPosition</c>
/// at <c>0x18001e1b0</c>, the eyeball mesh's material setup at <c>0x18000f580</c> and <c>R_MouthSetupVertexShader</c> at
/// <c>0x18001df10</c>; from published source, <c>C_TFPlayer::UpdateLookAt</c> (<c>c_tf_player.cpp:6210-6300</c>) and
/// <c>C_BaseFlex::SetViewTarget</c> (<c>c_baseflex.cpp:549-652</c>). The derivation is <c>docs/RISKS.md</c> B725.
///
/// **The render config at its defaults**: <c>r_eyeshift_*</c> and <c>r_eyesize</c> 0, <c>r_eyemove</c> on, which the
/// engine's own cvars default to; this viewer exposes none of them.
/// </remarks>
public static class StudioEyes
{
    /// <summary>The normalise's guard, <c>rsqrt( |v|² + 1e-10 )</c> (<c>0x180080410</c>).</summary>
    private const float NormaliseEpsilon = 1e-10f;

    /// <summary>How far ahead a player with nobody to look at looks (<c>c_tf_player.cpp:6254</c>).</summary>
    public const float LookAhead = 512f;

    /// <summary>How near another player must be to be looked at (<c>c_tf_player.cpp:6237</c>).</summary>
    public const float LookAtRange = 300f;

    /// <summary>The nearest a view target may be in the eyes' frame (<c>c_baseflex.cpp:586-589</c>).</summary>
    private const float NearestTarget = 6f;

    /// <summary><c>R_StudioEyeballPosition</c> and the eyeball mesh's material setup.</summary>
    /// <param name="eye">The eyeball.</param>
    /// <param name="bone">Its bone's bone-to-world, twelve floats, row-major 3×4.</param>
    /// <param name="viewTarget">Where the model looks, world space (<c>SetEyeViewTarget</c>).</param>
    /// <returns>The four material values.</returns>
    public static EyeShading Shading(StudioEyeball eye, ReadOnlySpan<float> bone, (float X, float Y, float Z) viewTarget)
    {
        if (bone.Length < 12)
        {
            throw new ArgumentException("A bone matrix is twelve floats.", nameof(bone));
        }

        // `VectorTransform( org + sign( org ) * eyeshift, bone )` with no shift is the `$eyeorigin` the draw sets too.
        (float X, float Y, float Z) origin = Transform(eye.Origin, bone);
        (float X, float Y, float Z) up = Rotate(eye.Up, bone);

        // `r_eyemove` on: straight at the target.
        (float X, float Y, float Z) forward = Normalise(Sub(viewTarget, origin));
        (float X, float Y, float Z) right = Normalise(Cross(forward, up));

        // `peyeball->zoffset + dz`, where dz is zoffset: twice it.
        forward = Normalise(Add(forward, Scale(right, eye.ZOffset + eye.ZOffset)));
        right = Normalise(Cross(forward, up));
        up = Normalise(Cross(right, forward));

        // `scale = 1 / iris_scale + r_eyesize` in double, back to float, inverted where positive, then negated.
        float scale = (float)(1.0 / eye.IrisScale);

        if (scale > 0f)
        {
            scale = 1f / scale;
        }

        scale = -scale;

        (float X, float Y, float Z) u = Scale(right, scale);
        (float X, float Y, float Z) v = Scale(up, scale);

        return new EyeShading(
            origin,
            eye.Up,
            (u.X, u.Y, u.Z, 0.5f - Dot(origin, u)),
            (v.X, v.Y, v.Z, 0.5f - Dot(origin, v)));
    }

    /// <summary><c>R_MouthSetupVertexShader</c>: the first mouth's forward and flex weight.</summary>
    /// <param name="mouth">The model's first mouth.</param>
    /// <param name="bone">Its bone's bone-to-world.</param>
    /// <param name="descriptorWeights">The model's flex descriptor weights (<c>m_pFlexWeights</c>), or empty.</param>
    /// <returns>The two values.</returns>
    public static MouthShading Mouth(StudioMouth mouth, ReadOnlySpan<float> bone, ReadOnlySpan<float> descriptorWeights)
    {
        if (bone.Length < 12)
        {
            throw new ArgumentException("A bone matrix is twelve floats.", nameof(bone));
        }

        float weight = mouth.FlexDescriptor >= 0 && mouth.FlexDescriptor < descriptorWeights.Length
            ? descriptorWeights[mouth.FlexDescriptor]
            : 0f;

        return new MouthShading(Scale(Rotate(mouth.Forward, bone), -1f), Math.Clamp(weight, 0f, 1f));
    }

    /// <summary><c>C_TFPlayer::UpdateLookAt</c>: the first living other player near and ahead, else ahead.</summary>
    /// <param name="origin">The player's <c>GetAbsOrigin()</c>.</param>
    /// <param name="angles">The player's <c>GetLocalAngles()</c>, pitch, yaw, roll in degrees.</param>
    /// <param name="players">
    /// Every living player in index order (<c>UTIL_PlayerByIndex( 1..maxClients )</c>): its index, origin and
    /// <c>EyePosition()</c>. It may include the looker, which is skipped by <paramref name="self"/>.
    /// </param>
    /// <param name="self">The looker's index: <c>if ( pEnt == this ) continue;</c> (<c>c_tf_player.cpp:6232</c>).</param>
    /// <returns><c>m_viewtarget</c>.</returns>
    /// <remarks><c>tf_clientsideeye_lookats</c> is on by default (<c>c_tf_player.cpp:190</c>).</remarks>
    public static (float X, float Y, float Z) LookAt(
        (float X, float Y, float Z) origin,
        (float Pitch, float Yaw, float Roll) angles,
        IReadOnlyList<(int Index, (float X, float Y, float Z) Origin, (float X, float Y, float Z) Eyes)> players,
        int self)
    {
        ArgumentNullException.ThrowIfNull(players);

        (float X, float Y, float Z) forward = AngleForward(angles.Pitch, angles.Yaw);

        for (int index = 0; index < players.Count; index++)
        {
            (int Index, (float X, float Y, float Z) Origin, (float X, float Y, float Z) Eyes) other = players[index];

            if (other.Index == self)
            {
                continue;
            }

            (float X, float Y, float Z) toward = Sub(other.Origin, origin);
            float length = MathF.Sqrt(Dot(toward, toward));

            if (length > LookAtRange)
            {
                continue;
            }

            // `VectorNormalize`, which leaves a zero vector zero.
            if (length > 0f && Dot(forward, Scale(toward, 1f / length)) < 0f)
            {
                continue;
            }

            return other.Eyes;
        }

        return Add(origin, Scale(forward, LookAhead));
    }

    /// <summary><c>C_BaseFlex::SetViewTarget</c>: the target clamped in the <c>eyes</c> attachment's frame.</summary>
    /// <param name="target"><c>m_viewtarget</c>.</param>
    /// <param name="eyes">The <c>eyes</c> attachment's world matrix, row-major 3×4, or empty for a model without one.</param>
    /// <param name="upDown"><c>eyes_updown</c>'s global weight, degrees, or 0.</param>
    /// <param name="rightLeft"><c>eyes_rightleft</c>'s, or 0.</param>
    /// <param name="maxEyeDeflection">The model's <c>MaxEyeDeflection()</c>.</param>
    /// <returns>What <c>SetViewTarget</c> hands the model render.</returns>
    public static (float X, float Y, float Z) ViewTarget(
        (float X, float Y, float Z) target, ReadOnlySpan<float> eyes, float upDown, float rightLeft, float maxEyeDeflection)
    {
        if (eyes.Length < 12)
        {
            return target;
        }

        (float X, float Y, float Z) local = InverseTransform(target, eyes);

        if (local.X < NearestTarget)
        {
            local.X = NearestTarget;
        }

        float distance = MathF.Sqrt(Dot(local, local));
        local = Normalise(local);

        (float X, float Y, float Z) deflect = AngleForward(upDown, rightLeft);
        deflect.X = 0f;
        local = Normalise(Add(local, Scale(deflect, local.X * local.X)));

        if (local.X < maxEyeDeflection)
        {
            local.X = 0f;

            if ((local.Y * local.Y) + (local.Z * local.Z) > 0f)
            {
                float d = MathF.Sqrt((1f - (maxEyeDeflection * maxEyeDeflection)) / ((local.Y * local.Y) + (local.Z * local.Z)));
                local = (maxEyeDeflection, local.Y * d, local.Z * d);
            }
            else
            {
                local.X = 1f;
            }
        }

        return Transform(Scale(local, distance), eyes);
    }

    /// <summary>Each eyeball's values for one draw, written into <paramref name="face"/> (<see cref="Shading"/> per eye).</summary>
    /// <param name="face">The face to write; its <see cref="StudioFace.Eyes"/> is the model's eyeball count long.</param>
    /// <param name="model">The model.</param>
    /// <param name="bones">Its bone-to-world.</param>
    /// <param name="target">The view target the render was handed.</param>
    public static void FillEyes(StudioFace face, PropModels.ModelFrames model, Animation.Animating.BoneAccessor bones, (float X, float Y, float Z) target)
    {
        ArgumentNullException.ThrowIfNull(face);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(bones);

        for (int index = 0; index < face.Eyes.Length && index < model.Eyeballs.Count; index++)
        {
            StudioEyeball eye = model.Eyeballs[index];

            face.Eyes[index] = eye.Bone >= 0 && eye.Bone < bones.Count ? Shading(eye, bones.Bone(eye.Bone), target) : default;
        }
    }

    /// <summary>
    /// <c>C_BaseFlex::SetViewTarget</c> for a model: the target clamped in its <c>eyes</c> attachment's frame, or unchanged
    /// for a model without one (<c>m_iEyeAttachment = LookupAttachment( "eyes" )</c>, <c>c_baseanimating.cpp:1154</c>).
    /// </summary>
    /// <param name="target"><c>m_viewtarget</c>.</param>
    /// <param name="model">The model.</param>
    /// <param name="bones">Its bone-to-world.</param>
    /// <param name="upDown"><c>eyes_updown</c>'s global weight.</param>
    /// <param name="rightLeft"><c>eyes_rightleft</c>'s.</param>
    /// <returns>What <c>SetViewTarget</c> hands the render.</returns>
    public static (float X, float Y, float Z) ClampedTarget(
        (float X, float Y, float Z) target,
        PropModels.ModelFrames model,
        Animation.Animating.BoneAccessor bones,
        float upDown,
        float rightLeft)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(bones);

        foreach (StudioAttachment attachment in model.Attachments ?? [])
        {
            if (string.Equals(attachment.Name, "eyes", StringComparison.OrdinalIgnoreCase))
            {
                if (attachment.Bone < 0 || attachment.Bone >= bones.Count || attachment.Local.Count < 12)
                {
                    return target;
                }

                Span<float> world = stackalloc float[12];
                Concatenate(bones.Bone(attachment.Bone), attachment.Local, world);

                return ViewTarget(target, world, upDown, rightLeft, model.MaxEyeDeflection);
            }
        }

        return target;
    }

    /// <summary>
    /// A controller's global weight for a C_BaseFlex no scene drives: its networked (or never set) <c>m_flexWeight</c> of 0
    /// rescaled to the controller's range — its minimum (<c>c_baseflex.cpp:1220-1222</c>); 0 where the model lacks it.
    /// </summary>
    /// <param name="model">The model.</param>
    /// <param name="name">The controller.</param>
    /// <returns>The weight.</returns>
    public static float RestingController(PropModels.ModelFrames model, string name)
    {
        ArgumentNullException.ThrowIfNull(model);

        foreach (StudioFlexController controller in model.Flex.Controllers)
        {
            if (string.Equals(controller.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return controller.Min;
            }
        }

        return 0f;
    }

    /// <summary><c>ConcatTransforms( bone, local )</c>: an attachment's world matrix, row-major 3×4.</summary>
    /// <param name="bone">The bone's bone-to-world.</param>
    /// <param name="local">The attachment's matrix in the bone's space.</param>
    /// <param name="into">Twelve floats.</param>
    public static void Concatenate(ReadOnlySpan<float> bone, IReadOnlyList<float> local, Span<float> into)
    {
        ArgumentNullException.ThrowIfNull(local);

        for (int row = 0; row < 3; row++)
        {
            for (int column = 0; column < 4; column++)
            {
                float value = (bone[row * 4] * local[column]) + (bone[(row * 4) + 1] * local[4 + column]) +
                    (bone[(row * 4) + 2] * local[8 + column]);

                into[(row * 4) + column] = column == 3 ? value + bone[(row * 4) + 3] : value;
            }
        }
    }

    /// <summary><c>AngleVectors</c>' forward for a pitch and yaw in degrees.</summary>
    private static (float X, float Y, float Z) AngleForward(float pitch, float yaw)
    {
        float p = pitch * (MathF.PI / 180f);
        float y = yaw * (MathF.PI / 180f);

        return (MathF.Cos(p) * MathF.Cos(y), MathF.Cos(p) * MathF.Sin(y), -MathF.Sin(p));
    }

    /// <summary>
    /// <c>VectorNormalize</c> on x86, which the studio render and the client both use: <c>v · rsqrt( |v|² + 1e-10 )</c>
    /// (<c>vector.h:2239-2252</c>; the studio render's own copy at <c>0x18001e2c3</c>).
    /// </summary>
    private static (float X, float Y, float Z) Normalise((float X, float Y, float Z) v) =>
        Scale(v, 1f / MathF.Sqrt(Dot(v, v) + NormaliseEpsilon));

    private static (float X, float Y, float Z) Transform((float X, float Y, float Z) v, ReadOnlySpan<float> m) => (
        (v.X * m[0]) + (v.Y * m[1]) + (v.Z * m[2]) + m[3],
        (v.X * m[4]) + (v.Y * m[5]) + (v.Z * m[6]) + m[7],
        (v.X * m[8]) + (v.Y * m[9]) + (v.Z * m[10]) + m[11]);

    private static (float X, float Y, float Z) Rotate((float X, float Y, float Z) v, ReadOnlySpan<float> m) => (
        (v.X * m[0]) + (v.Y * m[1]) + (v.Z * m[2]),
        (v.X * m[4]) + (v.Y * m[5]) + (v.Z * m[6]),
        (v.X * m[8]) + (v.Y * m[9]) + (v.Z * m[10]));

    /// <summary><c>VectorITransform</c>: the translation removed, then the rotation's transpose.</summary>
    private static (float X, float Y, float Z) InverseTransform((float X, float Y, float Z) v, ReadOnlySpan<float> m)
    {
        float x = v.X - m[3];
        float y = v.Y - m[7];
        float z = v.Z - m[11];

        return (
            (x * m[0]) + (y * m[4]) + (z * m[8]),
            (x * m[1]) + (y * m[5]) + (z * m[9]),
            (x * m[2]) + (y * m[6]) + (z * m[10]));
    }

    private static (float X, float Y, float Z) Add((float X, float Y, float Z) a, (float X, float Y, float Z) b) =>
        (a.X + b.X, a.Y + b.Y, a.Z + b.Z);

    private static (float X, float Y, float Z) Sub((float X, float Y, float Z) a, (float X, float Y, float Z) b) =>
        (a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    private static (float X, float Y, float Z) Scale((float X, float Y, float Z) a, float s) => (a.X * s, a.Y * s, a.Z * s);

    private static float Dot((float X, float Y, float Z) a, (float X, float Y, float Z) b) =>
        (a.X * b.X) + (a.Y * b.Y) + (a.Z * b.Z);

    private static (float X, float Y, float Z) Cross((float X, float Y, float Z) a, (float X, float Y, float Z) b) => (
        (a.Y * b.Z) - (a.Z * b.Y),
        (a.Z * b.X) - (a.X * b.Z),
        (a.X * b.Y) - (a.Y * b.X));
}
