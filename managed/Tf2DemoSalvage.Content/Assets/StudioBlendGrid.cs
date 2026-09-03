using System;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Content.Assets;

/// <summary>One of a model's pose parameters: a named input to its blend grids.</summary>
/// <param name="Name">What drives it, such as <c>move_x</c>.</param>
/// <param name="Start">The value the parameter takes at the low end.</param>
/// <param name="End">The value it takes at the high end.</param>
/// <param name="Loop">The range it wraps over, or zero when it does not wrap.</param>
/// <remarks>
/// <c>mstudioposeparamdesc_t</c> — name, flags, start, end, loop — listed at
/// <c>numlocalposeparameters</c>/<c>localposeparamindex</c>, which sit at 300 and 304 in
/// <c>studiohdr_t</c> (counted from <c>numbodyparts</c> at 232 through the attachment, node, flex,
/// ik-chain and mouth pairs).
///
/// The flags field is not read: <c>studio.h</c> annotates it <c>// ????</c>, which is Valve's own
/// note that they no longer knew either.
/// </remarks>
public readonly record struct StudioPoseParameter(
    string Name, float Start, float End, float Loop);

/// <summary>
/// The grid of animations a sequence blends between, and the parameters that pick a point in it.
/// </summary>
/// <remarks>
/// **A sequence is not an animation.** It names a <c>groupsize[0]</c> by <c>groupsize[1]</c> grid
/// of them and the engine interpolates between the nearest few, using two pose parameters as the
/// coordinates. For a health pack or a door the grid is one by one and the distinction is
/// invisible; for a player it is the difference between running forwards and running sideways.
///
/// **Taking the corner is what a player looked like without this.** A nine-way movement blend has
/// its corner at one extreme direction, so the legs ran that way whatever the body was doing — the
/// owner's "the model faces right, but the feet and legs bend 180 degrees the wrong way".
/// </remarks>
public sealed class StudioBlendGrid
{
    private readonly int[] _animations;

    /// <summary>Builds a grid from a sequence description.</summary>
    /// <param name="groupX">
    /// <c>groupsize[0]</c>.
    /// </param>
    /// <param name="groupY">
    /// <c>groupsize[1]</c>.
    /// </param>
    /// <param name="animations">The animation indices, <paramref name="groupX"/> across.</param>
    /// <param name="parameterX">
    /// <c>paramindex[0]</c>, which pose parameter drives the first axis, or −1.
    /// </param>
    /// <param name="parameterY">
    /// <c>paramindex[1]</c>.
    /// </param>
    /// <param name="startX">
    /// <c>paramstart[0]</c>.
    /// </param>
    /// <param name="endX">
    /// <c>paramend[0]</c>.
    /// </param>
    /// <param name="startY">
    /// <c>paramstart[1]</c>.
    /// </param>
    /// <param name="endY">
    /// <c>paramend[1]</c>.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="animations"/> is null.</exception>
    public StudioBlendGrid(
        int groupX,
        int groupY,
        IReadOnlyList<int> animations,
        int parameterX,
        int parameterY,
        float startX,
        float endX,
        float startY,
        float endY)
    {
        ArgumentNullException.ThrowIfNull(animations);

        GroupX = Math.Max(1, groupX);
        GroupY = Math.Max(1, groupY);
        ParameterX = parameterX;
        ParameterY = parameterY;
        StartX = startX;
        EndX = endX;
        StartY = startY;
        EndY = endY;

        _animations = [.. animations];
    }

    /// <summary>How many animations wide the grid is.</summary>
    public int GroupX { get; }

    /// <summary>How many animations tall it is.</summary>
    public int GroupY { get; }

    /// <summary>Which pose parameter drives the first axis, or −1 for none.</summary>
    public int ParameterX { get; }

    /// <summary>Which pose parameter drives the second axis, or −1 for none.</summary>
    public int ParameterY { get; }

    /// <summary>Where this sequence starts using the first parameter's range.</summary>
    public float StartX { get; }

    /// <summary>Where it stops.</summary>
    public float EndX { get; }

    /// <summary>Where this sequence starts using the second parameter's range.</summary>
    public float StartY { get; }

    /// <summary>Where it stops.</summary>
    public float EndY { get; }

    /// <summary>Whether the grid holds more than one animation.</summary>
    public bool Blends => _animations.Length > 1;

    /// <summary>The animation at one cell, with both coordinates clamped into the grid.</summary>
    /// <param name="x">Column.</param>
    /// <param name="y">Row.</param>
    /// <returns>The local animation index.</returns>
    /// <remarks>
    /// **Valve clamps rather than wraps or asserts**, in <c>mstudioseqdesc_t::anim</c>:
    ///
    /// <code>
    /// if ( x >= groupsize[0] ) x = groupsize[0] - 1;
    /// if ( y >= groupsize[1] ) y = groupsize[ 1 ] - 1;
    /// int offset = y * groupsize[0] + x;
    /// </code>
    ///
    /// which matters because the blend arithmetic reaches <c>i0 + 1</c> and <c>i1 + 1</c> at the
    /// top of each axis by design.
    /// </remarks>
    public int Animation(int x, int y)
    {
        int column = Math.Clamp(x, 0, GroupX - 1);
        int row = Math.Clamp(y, 0, GroupY - 1);
        int offset = (row * GroupX) + column;

        return offset >= 0 && offset < _animations.Length ? _animations[offset] : 0;
    }

    /// <summary>Puts a pose parameter's value into the 0-to-1 form the engine stores.</summary>
    /// <param name="parameter">The parameter being set, which supplies the range.</param>
    /// <param name="value">The value in the parameter's own units.</param>
    /// <returns>The same value from zero to one, clamped.</returns>
    /// <remarks>
    /// **<c>m_flPoseParameter</c> does not hold what the caller set.** <c>SetPoseParameter</c>
    /// passes through <c>Studio_SetPoseParameter</c> (<c>bone_setup.cpp:5099</c>), which wraps a
    /// looping parameter, divides by the range and clamps:
    ///
    /// <code>
    /// ctlValue = (flValue - PoseParam.start) / (PoseParam.end - PoseParam.start);
    /// if (ctlValue &lt; 0) ctlValue = 0;
    /// if (ctlValue &gt; 1) ctlValue = 1;
    /// </code>
    ///
    /// and it is <c>ctlValue</c> that is stored and later read by <see cref="Locate"/>. Handing
    /// <see cref="Locate"/> a raw value instead lands mid-grid for anything whose range does not
    /// happen to be zero to one — <c>move_x</c> runs −1 to 1, so a player standing still came out
    /// at the bottom of the grid rather than its middle.
    /// </remarks>
    public static float Normalize(StudioPoseParameter parameter, float value)
    {
        float shifted = value;

        if (parameter.Loop != 0f)
        {
            float wrap = ((parameter.Start + parameter.End) / 2f) + (parameter.Loop / 2f);
            float shift = parameter.Loop - wrap;

            shifted -= parameter.Loop * MathF.Floor((shifted + shift) / parameter.Loop);
        }

        float span = parameter.End - parameter.Start;

        return MathF.Abs(span) < float.Epsilon
            ? 0f
            : Math.Clamp((shifted - parameter.Start) / span, 0f, 1f);
    }

    /// <summary>Turns a pose parameter's value into a cell and a fraction along one axis.</summary>
    /// <param name="axis">Zero for the first axis, one for the second.</param>
    /// <param name="parameters">Every pose parameter the model declares.</param>
    /// <param name="values">The current value of each, in the same order.</param>
    /// <param name="masterPose">
    /// The owning group's map from its own pose parameter indices to shared ones, as
    /// <c>virtualgroup_t::masterPose</c>. A sequence's <c>paramindex</c> is local to its group, so
    /// without this the shared list is indexed with a number that belongs to a different list.
    /// </param>
    /// <returns>The lower cell index and how far past it the value sits, from zero to one.</returns>
    /// <remarks>
    /// **Ported from <c>Studio_LocalPoseParameter</c>** (<c>bone_setup.cpp:1682</c>). The value is
    /// wrapped when the parameter loops, mapped from the parameter's own range into the part of it
    /// this sequence covers, clamped, and then split into a cell and a remainder:
    ///
    /// <code>
    /// index = (int)(flSetting * (seqdesc.groupsize[iLocalIndex] - 1));
    /// if (index == seqdesc.groupsize[iLocalIndex] - 1) index = seqdesc.groupsize[iLocalIndex] - 2;
    /// flSetting = flSetting * (seqdesc.groupsize[iLocalIndex] - 1) - index;
    /// </code>
    ///
    /// The step back at the top end is what lets the caller always read <c>index + 1</c>.
    ///
    /// **The <c>posekeyindex</c> branch is not implemented**, and that is stated rather than
    /// hidden: a sequence with explicit pose keys spaces its grid unevenly, and Valve searches the
    /// key list instead of dividing. TF2's movement blends do not use it, so this takes the even
    /// branch — a model that did would blend at slightly wrong proportions rather than break.
    /// </remarks>
    public (int Index, float Setting) Locate(
        int axis,
        IReadOnlyList<StudioPoseParameter> parameters,
        IReadOnlyList<float> values,
        IReadOnlyList<int> masterPose)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(masterPose);

        int local = axis == 0 ? ParameterX : ParameterY;
        int group = axis == 0 ? GroupX : GroupY;

        // **The index stored in the sequence is local to the group that owns it, and translating it
        // is not optional.** <c>CStudioHdr::GetSharedPoseParameter</c> reads
        // <c>pGroup->masterPose[iLocalPose]</c> for exactly this. Skipping the translation is what
        // made every player run backwards: a player model declares two pose parameters and its
        // animation model's run sequence asks for index 5, which fell off the end of the list and
        // returned cell zero on both axes — the backward corner of the blend grid.
        //
        // Valve's own comment on the bounds check is worth keeping: returning the local index when
        // it is out of range "is not correct, this should return -1 because otherwise it's just
        // some random unrelated index".
        int which = local >= 0 && local < masterPose.Count ? masterPose[local] : -1;

        if (which < 0 || which >= parameters.Count || which >= values.Count)
        {
            return (0, 0f);
        }

        StudioPoseParameter pose = parameters[which];
        float value = values[which];

        if (pose.Loop != 0f)
        {
            float wrap = ((pose.Start + pose.End) / 2f) + (pose.Loop / 2f);
            float shift = pose.Loop - wrap;

            value -= pose.Loop * MathF.Floor((value + shift) / pose.Loop);
        }

        float span = pose.End - pose.Start;

        // **Guarded where Valve divides outright.** A parameter whose range is empty, or a
        // sequence covering none of it, is degenerate rather than impossible - and the quotient is
        // an infinity that reaches the grid index as a huge number and then clamps to a real cell,
        // so the model would pose from the wrong animation rather than fail.
        if (MathF.Abs(span) < float.Epsilon)
        {
            return (0, 0f);
        }

        float localStart = ((axis == 0 ? StartX : StartY) - pose.Start) / span;
        float localEnd = ((axis == 0 ? EndX : EndY) - pose.Start) / span;

        if (MathF.Abs(localEnd - localStart) < float.Epsilon)
        {
            return (0, 0f);
        }

        float setting = Math.Clamp((value - localStart) / (localEnd - localStart), 0f, 1f);
        int index = 0;

        if (group > 2)
        {
            index = (int)(setting * (group - 1));

            if (index == group - 1)
            {
                index = group - 2;
            }

            setting = (setting * (group - 1)) - index;
        }

        return (index, setting);
    }

    /// <summary>The three animations a point in the grid blends, and their weights.</summary>
    /// <param name="x">Lower cell on the first axis.</param>
    /// <param name="y">Lower cell on the second axis.</param>
    /// <param name="settingX">How far past <paramref name="x"/>, from zero to one.</param>
    /// <param name="settingY">How far past <paramref name="y"/>.</param>
    /// <returns>Three animation indices and the weight of each.</returns>
    /// <remarks>
    /// **Ported from <c>Calc3WayBlendIndices</c>** (<c>bone_setup.cpp:1840</c>). Three rather than
    /// four because the engine bisects each grid square into two triangles and blends the corners
    /// of whichever one the point falls in — <c>anim_3wayblend</c> defaults to <c>"1"</c>
    /// (<c>bone_setup.cpp:1838</c>), so this IS the path the game takes, not a simplification of
    /// the four-corner one.
    ///
    /// Which diagonal splits the square alternates with the cell, <c>((i0 + i1) &amp; 0x1) == 0</c>,
    /// so that neighbouring squares agree along their shared edge.
    /// </remarks>
    public (int[] Animations, float[] Weights) ThreeWay(
        int x, int y, float settingX, float settingY)
    {
        bool even = ((x + y) & 0x1) == 0;

        int x1, y1, x2, y2, x3, y3;
        float[] weights = new float[3];

        if (even)
        {
            // The diagonal runs from top left to bottom right.
            if (settingX > settingY)
            {
                (x1, y1, x2, y2, x3, y3) = (0, 0, 1, 0, 1, 1);
                weights[0] = 1f - settingX;
                weights[1] = settingX - settingY;
            }
            else
            {
                (x1, y1, x2, y2, x3, y3) = (1, 1, 0, 1, 0, 0);
                weights[0] = settingX;
                weights[1] = settingY - settingX;
            }
        }
        else if (settingX + settingY > 1f)
        {
            // Bottom left to top right, upper triangle.
            (x1, y1, x2, y2, x3, y3) = (1, 0, 1, 1, 0, 1);
            weights[0] = 1f - settingY;
            weights[1] = settingX - 1f + settingY;
        }
        else
        {
            (x1, y1, x2, y2, x3, y3) = (0, 1, 0, 0, 1, 0);
            weights[0] = settingY;
            weights[1] = 1f - settingX - settingY;
        }

        int[] animations =
        [
            Animation(x + x1, y + y1),
            Animation(x + x2, y + y2),
            Animation(x + x3, y + y3),
        ];

        // Valve clamps the diagonal term to zero and gives the remainder to the third corner, so
        // the three always sum to one however the arithmetic rounded.
        if (weights[1] < 0.001f)
        {
            weights[1] = 0f;
        }

        weights[2] = 1f - weights[0] - weights[1];

        return (animations, weights);
    }
}

/// <summary>Mixing two poses of one skeleton, as the engine mixes a blend grid's corners.</summary>
/// <remarks>
/// **This is <c>BlendBones</c>** (<c>bone_setup.cpp:1531</c>), which is not a slerp: Valve aligns
/// the two quaternions and then interpolates them componentwise and normalises —
/// <c>QuaternionBlendNoAlign</c> (<c>mathlib_base.cpp:1563</c>) is
///
/// <code>
/// sclp = 1.0f - t;  sclq = t;
/// for (i = 0; i &lt; 4; i++) qt[i] = sclp * p[i] + sclq * q[i];
/// QuaternionNormalize( qt );
/// </code>
///
/// A normalised lerp rather than a spherical one. The difference is a slight easing near the
/// middle of a wide arc, and matching the engine matters more than the theory: a blend of two
/// adjacent movement animations is a narrow arc where the two agree closely anyway.
/// </remarks>
public static class StudioPoseBlend
{
    /// <summary>Blends one pose toward another.</summary>
    /// <param name="bones">The skeleton both poses belong to.</param>
    /// <param name="first">The pose at weight <c>1 - s</c>.</param>
    /// <param name="second">The pose at weight <paramref name="s"/>.</param>
    /// <param name="s">How far toward <paramref name="second"/>, from zero to one.</param>
    /// <returns>A pose naming every bone, so it can be blended again.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// **Every bone is named in the result, including the ones neither animation moved.** An
    /// animation lists only the bones it touches and the rest fall back to their rest pose, so
    /// blending the LISTS would mix a moved bone against nothing and quietly weight it wrongly.
    /// Valve blends full arrays; expanding to full arrays here is the same thing.
    /// </remarks>
    public static IReadOnlyList<StudioBonePose> Blend(
        IReadOnlyList<StudioBone> bones,
        IReadOnlyList<StudioBonePose> first,
        IReadOnlyList<StudioBonePose> second,
        float s) =>
        Blend(bones, first, second, s, additive: false);

    /// <summary>Blends one pose toward another, knowing whether they are differences.</summary>
    /// <param name="bones">The skeleton both poses belong to.</param>
    /// <param name="first">The pose at weight <c>1 - s</c>.</param>
    /// <param name="second">The pose at weight <paramref name="s"/>.</param>
    /// <param name="s">How far toward <paramref name="second"/>, from zero to one.</param>
    /// <param name="additive">
    /// Whether the two are DELTAS rather than poses, which changes what an absent bone means.
    /// </param>
    /// <returns>A pose naming every bone, so it can be blended again.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// **What an unlisted bone means is the whole of this, and getting it wrong twists a body**
    /// (B284). For an ordinary animation an absent bone sits at its REST pose, which is what
    /// <c>CalcVirtualAnimation</c> seeds (<c>bone_setup.cpp:940</c>). For a DELTA it sits at
    /// identity and zero — no difference — which is the other half of the same branch:
    ///
    /// <code>
    ///   if (animdesc.flags &amp; STUDIO_DELTA) { q[i].Init( 0,0,0,1 ); pos[i].Init( 0,0,0 ); }
    ///   else                                { q[i] = pSeqbone[j].quat; pos[i] = pSeqbone[j].pos; }
    /// </code>
    ///
    /// **Measured:** `jumpland_primary` animates twelve bones of seventy-eight. Expanded against
    /// the rest pose it became a seventy-six-bone difference, sixty-four of whose entries were a
    /// whole bone transform masquerading as a delta — added to the base, that threw a landing
    /// player's arms over its head. The reload layer hid the same fault behind more animated bones.
    /// </remarks>
    public static IReadOnlyList<StudioBonePose> Blend(
        IReadOnlyList<StudioBone> bones,
        IReadOnlyList<StudioBonePose> first,
        IReadOnlyList<StudioBonePose> second,
        float s,
        bool additive)
    {
        ArgumentNullException.ThrowIfNull(bones);
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);

        StudioBonePose[] left = additive
            ? ExpandIdentity(bones, first)
            : Expand(bones, first);

        StudioBonePose[] right = additive
            ? ExpandIdentity(bones, second)
            : Expand(bones, second);

        float weightFirst = 1f - s;

        for (int bone = 0; bone < left.Length; bone++)
        {
            (float X, float Y, float Z, float W) p = left[bone].Rotation;
            (float X, float Y, float Z, float W) q = Align(p, right[bone].Rotation);

            (float X, float Y, float Z) a = left[bone].Position;
            (float X, float Y, float Z) b = right[bone].Position;

            left[bone] = new StudioBonePose(
                bone,
                ((a.X * weightFirst) + (b.X * s),
                 (a.Y * weightFirst) + (b.Y * s),
                 (a.Z * weightFirst) + (b.Z * s)),
                Normalize((
                    (p.X * weightFirst) + (q.X * s),
                    (p.Y * weightFirst) + (q.Y * s),
                    (p.Z * weightFirst) + (q.Z * s),
                    (p.W * weightFirst) + (q.W * s))));
        }

        return left;
    }

    /// <summary>Layers a gesture over a base pose additively, the way a delta sequence composites.</summary>
    /// <param name="bones">The skeleton both poses belong to.</param>
    /// <param name="basePose">The pose being layered onto — the main body, already resolved.</param>
    /// <param name="deltaPose">The gesture's own pose, from a sequence whose flags carry <c>STUDIO_DELTA</c>.</param>
    /// <param name="boneWeights">
    /// The gesture sequence's own per-bone weight list, from <see cref="StudioGestureWeights"/>,
    /// indexed the same way <paramref name="deltaPose"/>'s bones are.
    /// </param>
    /// <param name="layerWeight">How strongly the gesture is blended in, from zero to one.</param>
    /// <param name="post">
    /// Whether the sequence's flags carry <c>STUDIO_POST</c>. Every jump-land and flinch gesture
    /// measured on the shipped player models carries it (B112); this is a parameter rather than an
    /// assumption because the flag genuinely varies by sequence and changes the composition order.
    /// </param>
    /// <returns>A pose naming every bone, so it can be layered again.</returns>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    /// <remarks>
    /// **A different primitive from <c>Blend</c>, not a variant of it.** <c>SlerpBones</c>
    /// (<c>bone_setup.cpp:1373</c>) takes an entirely separate branch for <c>STUDIO_DELTA</c>: the
    /// gesture's rotation is not interpolated toward, it is composed ON TOP of the base rotation as
    /// a scaled quaternion product, and position is a simple scaled add rather than a lerp:
    ///
    /// <code>
    /// s2 = layerWeight * seqdesc.weight( i );      // per-bone strength
    /// if ( STUDIO_POST ) QuaternionMA( q1[i], s2, q2[i], q1[i] );   // base ⊗ scale(delta, s2)
    /// else               QuaternionSM( s2, q2[i], q1[i], q1[i] );  // scale(delta, s2) ⊗ base
    /// pos1[i] += pos2[i] * s2;
    /// </code>
    ///
    /// <c>QuaternionScale(q, t)</c> is a partial ROTATION — the sin/asin form in
    /// <c>mathlib_base.cpp:1757</c>, equivalent to slerping from identity toward <c>q</c> by
    /// <c>t</c> — not a naive per-component multiply, which would not stay a unit quaternion.
    ///
    /// **A bone the gesture's own animation track never mentions defaults to IDENTITY, not the
    /// rest pose.** Confirmed in the runtime's own per-bone decode
    /// (<c>bone_setup.cpp:599</c>): <c>else if (animdesc.flags &amp; STUDIO_DELTA) { boneQuat.Init(
    /// 0, 0, 0, 1 ); bonePos.Init( 0, 0, 0 ); }</c>, with the rest-pose fallback reserved for the
    /// non-delta <c>else</c> beneath it. An identity delta composed at any weight leaves the base
    /// pose bone exactly as it was — which is the only default that means "this gesture does not
    /// touch this bone," where the rest pose would instead drag it toward wherever the skeleton
    /// happens to bind.
    /// </remarks>
    public static IReadOnlyList<StudioBonePose> Layer(
        IReadOnlyList<StudioBone> bones,
        IReadOnlyList<StudioBonePose> basePose,
        IReadOnlyList<StudioBonePose> deltaPose,
        IReadOnlyList<float> boneWeights,
        float layerWeight,
        bool post)
    {
        ArgumentNullException.ThrowIfNull(bones);
        ArgumentNullException.ThrowIfNull(basePose);
        ArgumentNullException.ThrowIfNull(deltaPose);
        ArgumentNullException.ThrowIfNull(boneWeights);

        StudioBonePose[] result = Expand(bones, basePose);
        StudioBonePose[] delta = ExpandIdentity(bones, deltaPose);

        float clampedLayer = Math.Clamp(layerWeight, 0f, 1f);

        for (int bone = 0; bone < result.Length; bone++)
        {
            float strength = clampedLayer *
                (bone < boneWeights.Count ? boneWeights[bone] : 0f);

            // `if (s2 <= 0.0f) continue` — the gesture leaves this bone exactly as the base pose
            // had it, not blended toward anything.
            if (strength <= 0f)
            {
                continue;
            }

            (float X, float Y, float Z, float W) p = result[bone].Rotation;
            (float X, float Y, float Z, float W) q = delta[bone].Rotation;

            (float X, float Y, float Z, float W) scaled = QuaternionScale(q, strength);

            (float X, float Y, float Z, float W) composed = post
                ? Normalize(QuaternionMultiply(p, scaled))
                : Normalize(QuaternionMultiply(scaled, p));

            (float X, float Y, float Z) basePosition = result[bone].Position;
            (float X, float Y, float Z) deltaPosition = delta[bone].Position;

            result[bone] = new StudioBonePose(
                bone,
                (basePosition.X + (deltaPosition.X * strength),
                 basePosition.Y + (deltaPosition.Y * strength),
                 basePosition.Z + (deltaPosition.Z * strength)),
                composed);
        }

        return result;
    }

    /// <summary>Scales a rotation's ANGLE by a fraction, keeping its axis.</summary>
    /// <remarks>
    /// **<c>QuaternionScale</c>**, <c>mathlib_base.cpp:1757</c>. Valve's own comment gives the
    /// intent the fast form below computes: "slerp in 'reverse order' so that p doesn't get
    /// realigned" — <c>QuaternionSlerp( p, identity, 1 - |t|, q )</c>, sign of <c>w</c> preserved
    /// for <c>t &lt; 0</c>. The form actually run reaches the same answer through
    /// <c>sin(asin(sinom) * t) / sinom</c> rather than a general slerp, which is why it is ported
    /// verbatim instead of expressed as a call to a slerp this project does not otherwise have.
    ///
    /// At <c>t = 0</c> this is the identity quaternion; at <c>t = 1</c> it is (numerically very
    /// close to) <paramref name="q"/> itself — the division by <c>sinom + FLT_EPSILON</c> rather
    /// than <c>sinom</c> alone means it does not return bit-for-bit identical output, and that
    /// epsilon is Valve's, not this port's.
    /// </remarks>
    private static (float X, float Y, float Z, float W) QuaternionScale(
        (float X, float Y, float Z, float W) q, float t)
    {
        // **C's FLT_EPSILON, not .NET's `float.Epsilon`.** The two names collide and mean
        // different things: .NET's is the smallest positive denormal, about 1.4e-45, which does
        // nothing to guard a division — the numeric-decoding lesson already on file here is
        // exactly this trap under a different constant (`Four_Epsilons`, B-series). C's
        // `FLT_EPSILON` is 2^-23, about 1.1920929e-7, which is what actually keeps `sinom + eps`
        // away from zero at single-precision.
        const float fltEpsilon = 1.1920929E-07f;

        float sinom = MathF.Min(MathF.Sqrt((q.X * q.X) + (q.Y * q.Y) + (q.Z * q.Z)), 1f);
        float sinsom = MathF.Sin(MathF.Asin(sinom) * t);

        float scale = sinsom / (sinom + fltEpsilon);

        float r = MathF.Max(0f, 1f - (sinsom * sinsom));
        float w = MathF.Sqrt(r);

        return (q.X * scale, q.Y * scale, q.Z * scale, q.W < 0f ? -w : w);
    }

    /// <summary>The Hamilton product of two rotations, aligned first.</summary>
    /// <remarks>
    /// **<c>QuaternionMult</c>**, <c>mathlib_base.cpp:1837</c>. It aligns its SECOND argument to
    /// its first internally — <c>QuaternionAlign( p, q, q2 )</c> — before the product, which is
    /// why <see cref="Align"/> is not called again by <see cref="Layer"/> around this: doing so
    /// would align twice against a result the second alignment cannot see is already aligned.
    /// </remarks>
    private static (float X, float Y, float Z, float W) QuaternionMultiply(
        (float X, float Y, float Z, float W) p, (float X, float Y, float Z, float W) q)
    {
        (float X, float Y, float Z, float W) aligned = Align(p, q);

        return (
            (p.X * aligned.W) + (p.Y * aligned.Z) - (p.Z * aligned.Y) + (p.W * aligned.X),
            (-p.X * aligned.Z) + (p.Y * aligned.W) + (p.Z * aligned.X) + (p.W * aligned.Y),
            (p.X * aligned.Y) - (p.Y * aligned.X) + (p.Z * aligned.W) + (p.W * aligned.Z),
            (-p.X * aligned.X) - (p.Y * aligned.Y) - (p.Z * aligned.Z) + (p.W * aligned.W));
    }

    /// <summary>Fills in the bones a delta animation did not mention, as IDENTITY rather than rest.</summary>
    /// <remarks>
    /// The other half of why <see cref="Layer"/> cannot reuse <see cref="Expand"/> as it stands:
    /// confirmed at <c>bone_setup.cpp:599</c>, a bone a <c>STUDIO_DELTA</c> animation's own track
    /// does not reach defaults to <c>(0,0,0,1)</c> and zero position — composing that at any
    /// strength is a no-op, which is the only default that means "untouched" for an additive
    /// layer. The ordinary <see cref="Expand"/> fills with the rest pose instead, which is correct
    /// for the absolute animations <c>Blend</c> interpolates between and wrong here.
    /// </remarks>
    private static StudioBonePose[] ExpandIdentity(
        IReadOnlyList<StudioBone> bones, IReadOnlyList<StudioBonePose> pose)
    {
        StudioBonePose[] full = new StudioBonePose[bones.Count];

        for (int bone = 0; bone < full.Length; bone++)
        {
            full[bone] = new StudioBonePose(bone, (0f, 0f, 0f), (0f, 0f, 0f, 1f));
        }

        foreach (StudioBonePose moved in pose)
        {
            if (moved.Bone >= 0 && moved.Bone < full.Length)
            {
                full[moved.Bone] = moved with { Bone = moved.Bone };
            }
        }

        return full;
    }

    /// <summary>Fills in the bones an animation did not mention, from the skeleton's rest pose.</summary>
    private static StudioBonePose[] Expand(
        IReadOnlyList<StudioBone> bones, IReadOnlyList<StudioBonePose> pose)
    {
        StudioBonePose[] full = new StudioBonePose[bones.Count];

        for (int bone = 0; bone < bones.Count; bone++)
        {
            full[bone] = new StudioBonePose(bone, bones[bone].Position, bones[bone].Rotation);
        }

        foreach (StudioBonePose moved in pose)
        {
            if (moved.Bone >= 0 && moved.Bone < full.Length)
            {
                full[moved.Bone] = moved with { Bone = moved.Bone };
            }
        }

        return full;
    }

    /// <summary>Negates one quaternion when the two point opposite ways round.</summary>
    /// <remarks>
    /// **<c>QuaternionAlign</c>** (<c>mathlib_base.cpp:1509</c>), and it is what keeps a blend from
    /// taking the long way round. A rotation has two representations, <c>q</c> and <c>-q</c>;
    /// interpolating between the two that happen to be written oppositely swings the bone almost
    /// the whole way around instead of the short distance between them. Valve compares the summed
    /// squared differences both ways rather than using a dot product, with a note wondering
    /// whether a dot product would do — so the roundabout form is deliberate rather than
    /// transcribed carelessly.
    /// </remarks>
    private static (float X, float Y, float Z, float W) Align(
        (float X, float Y, float Z, float W) p, (float X, float Y, float Z, float W) q)
    {
        float apart =
            ((p.X - q.X) * (p.X - q.X)) + ((p.Y - q.Y) * (p.Y - q.Y)) +
            ((p.Z - q.Z) * (p.Z - q.Z)) + ((p.W - q.W) * (p.W - q.W));

        float together =
            ((p.X + q.X) * (p.X + q.X)) + ((p.Y + q.Y) * (p.Y + q.Y)) +
            ((p.Z + q.Z) * (p.Z + q.Z)) + ((p.W + q.W) * (p.W + q.W));

        return apart > together ? (-q.X, -q.Y, -q.Z, -q.W) : q;
    }

    /// <summary>Scales a quaternion back to unit length.</summary>
    /// <remarks>
    /// A zero-length result is left alone rather than divided by: it cannot arise from two unit
    /// quaternions that have been aligned, and dividing would send every vertex weighted to that
    /// bone to NaN, which loses the whole model rather than one joint.
    /// </remarks>
    private static (float X, float Y, float Z, float W) Normalize(
        (float X, float Y, float Z, float W) q)
    {
        float length = MathF.Sqrt((q.X * q.X) + (q.Y * q.Y) + (q.Z * q.Z) + (q.W * q.W));

        return length > 0f ? (q.X / length, q.Y / length, q.Z / length, q.W / length) : q;
    }
}
