using System;
using System.Collections.Generic;
using System.Numerics;

namespace Tf2DemoSalvage.Core.Scene;

/// <summary>
/// What kind of thing a model reference names, and therefore how to draw it.
/// </summary>
/// <remarks>
/// **Valve's own <c>modtype_t</c>**, from <c>src/public/model_types.h</c>: <c>mod_bad</c>,
/// <c>mod_brush</c>, <c>mod_sprite</c>, <c>mod_studio</c>. The engine keeps the distinction because
/// the three real kinds come from entirely different places, and
/// <c>C_BaseEntity::IsBrushModel</c> exists to ask which one an entity has.
///
/// **The corpus taught this one rather than the other way round.** A test written to demand
/// <c>models/</c> failed twice: on <c>*3</c> from the 2007 demo, then on
/// <c>sprites/light_glow02_noz.vmt</c> from the 2008 one. One string table carries all three, and a
/// viewer that hands any of them to a <c>.mdl</c> loader draws nothing and reports nothing.
/// </remarks>
public enum SceneModelKind
{
    /// <summary>Nothing recognisable — <c>mod_bad</c>.</summary>
    Unknown = 0,

    /// <summary>An inline BSP submodel, named <c>*N</c>: a door, a lift, a visualiser.</summary>
    Brush,

    /// <summary>A camera-facing sprite, named by its material.</summary>
    Sprite,

    /// <summary>An ordinary <c>.mdl</c> studio model.</summary>
    Studio,
}

/// <summary>
/// Where a model-bearing entity was, and what it was doing, at one moment.
/// </summary>
/// <remarks>
/// A struct because a match produces a great many of these and none of them is shared. Scale
/// defaults to 1 rather than 0 so a pose built from properties the demo never sent is drawn at its
/// authored size instead of vanishing.
/// </remarks>
public readonly record struct ScenePose
{
    /// <summary>World position, east.</summary>
    public float X { get; init; }

    /// <summary>World position, north.</summary>
    public float Y { get; init; }

    /// <summary>World position, up.</summary>
    public float Z { get; init; }

    /// <summary>Rotation about the side axis, in degrees.</summary>
    public float Pitch { get; init; }

    /// <summary>Rotation about the vertical axis, in degrees.</summary>
    public float Yaw { get; init; }

    /// <summary>Rotation about the forward axis, in degrees.</summary>
    public float Roll { get; init; }

    /// <summary>Size relative to the model as authored.</summary>
    public float Scale { get; init; } = 1f;

    /// <summary>Which animation is playing, or −1 when the entity does not animate.</summary>
    public int Sequence { get; init; } = -1;

    /// <summary>How far through that animation, from 0 to 1.</summary>
    public float Cycle { get; init; }

    /// <summary>Builds a pose at the world origin, unrotated and unanimated.</summary>
    public ScenePose()
    {
    }
}

/// <summary>One model, as it stands at the tick being drawn.</summary>
/// <param name="EntityIndex">Slot in the entity table, so a viewer can label or pick it.</param>
/// <param name="ModelPath">What to draw, as <c>modelprecache</c> named it.</param>
/// <param name="Kind">Which loader the path belongs to.</param>
/// <param name="Pose">Where it is and what it is doing.</param>
public readonly record struct SceneProp(
    int EntityIndex, string ModelPath, SceneModelKind Kind, ScenePose Pose);

/// <summary>
/// One entity's pose over the whole demo, stored as the moments it changed.
/// </summary>
/// <remarks>
/// **Keyframes rather than a pose per tick, and the arithmetic decided it.** A 1,600-second demo
/// is about 106,000 frames and a match carries a few hundred model-bearing entities, so a pose per
/// entity per frame is tens of millions of records — for a scene in which most of them never move.
/// A health pack that sits still all match costs one keyframe.
///
/// It also matches what a demo is. The stream sends only what changed, so the moments recorded
/// here are exactly the moments the demo spoke. Nothing is interpolated between them: a door that
/// opened at tick 900 was shut at 899, and inventing a position halfway would be this project
/// making up data it was not given.
/// </remarks>
public sealed class ScenePropTrack
{
    private readonly List<(int Tick, ScenePose Pose)> _keyframes = [];

    private int _endTick = int.MaxValue;

    /// <summary>Starts a track for one entity.</summary>
    /// <param name="entityIndex">Slot in the entity table.</param>
    /// <param name="modelPath">The model this entity draws as.</param>
    public ScenePropTrack(int entityIndex, string modelPath)
    {
        EntityIndex = entityIndex;
        ModelPath = modelPath;
    }

    /// <summary>Slot in the entity table.</summary>
    public int EntityIndex { get; }

    /// <summary>The model this entity draws as.</summary>
    public string ModelPath { get; }

    /// <summary>Which of Valve's model types this reference names.</summary>
    /// <remarks>
    /// Decided by the reference itself, which is all the string table gives. A leading asterisk is
    /// an inline BSP submodel numbered within the map — <c>*3</c> is the map's fourth. Everything
    /// else is told apart by extension, the way the engine's own loader does.
    /// </remarks>
    public SceneModelKind Kind => Classify(ModelPath);

    /// <summary>How many moments the entity actually changed at.</summary>
    public int KeyframeCount => _keyframes.Count;

    /// <summary>The first tick this entity was seen at.</summary>
    public int FirstTick => _keyframes.Count > 0 ? _keyframes[0].Tick : int.MaxValue;

    /// <summary>Works out what a model reference names.</summary>
    /// <param name="modelPath">The reference, as <c>modelprecache</c> carried it.</param>
    /// <returns>The kind, or <see cref="SceneModelKind.Unknown"/> for anything unrecognised.</returns>
    /// <remarks>
    /// **Unknown rather than a default of Studio.** A reference this does not recognise is a fact
    /// about the corpus worth surfacing — both kinds beyond <c>.mdl</c> were found exactly this
    /// way, by something refusing to classify them. Defaulting would have hidden both.
    /// </remarks>
    public static SceneModelKind Classify(string modelPath)
    {
        if (string.IsNullOrEmpty(modelPath))
        {
            return SceneModelKind.Unknown;
        }

        if (modelPath.StartsWith('*'))
        {
            return SceneModelKind.Brush;
        }

        if (modelPath.EndsWith(".mdl", StringComparison.OrdinalIgnoreCase))
        {
            return SceneModelKind.Studio;
        }

        // **Two extensions for one kind.** ".spr" is the Quake-descended sprite format Source
        // inherited; ".vmt" is a material used as one. Both are mod_sprite to the engine, and the
        // corpus carries both - .vmt on the 2008 demo, .spr on a 2026 one, so this is not an era
        // split and neither can be treated as legacy.
        return modelPath.EndsWith(".vmt", StringComparison.OrdinalIgnoreCase) ||
               modelPath.EndsWith(".spr", StringComparison.OrdinalIgnoreCase)
            ? SceneModelKind.Sprite
            : SceneModelKind.Unknown;
    }

    /// <summary>Records a pose, if it differs from the one before it.</summary>
    /// <param name="tick">When the demo stated it.</param>
    /// <param name="pose">The pose.</param>
    /// <remarks>
    /// **Identical means the whole pose, not just the position.** An entity animating on the spot
    /// changes every frame while standing still, and comparing only position would freeze it.
    /// </remarks>
    public void Add(int tick, ScenePose pose)
    {
        if (_keyframes.Count > 0 && _keyframes[^1].Pose == pose)
        {
            return;
        }

        _keyframes.Add((tick, pose));
    }

    /// <summary>Records that the entity ceased to exist.</summary>
    /// <param name="tick">The first tick it was gone.</param>
    /// <remarks>
    /// Without this a picked-up health pack stays on the floor for the rest of the demo, and a
    /// rocket that hit a wall hangs there — a scene that gradually fills with rubbish, which reads
    /// as clutter rather than as a defect.
    /// </remarks>
    public void End(int tick) => _endTick = tick;

    /// <summary>The pose to draw at a moment, interpolated between keyframes.</summary>
    /// <param name="tick">The moment, which may fall between ticks.</param>
    /// <returns>The pose, or <c>null</c> when the entity did not exist then.</returns>
    /// <remarks>
    /// **Parity with the client, which never draws a stored value directly.** Valve's
    /// <c>CInterpolatedVar</c> keeps a history of value-and-changetime entries and calls
    /// <c>Interpolate()</c> for the moment being rendered. Snapping to the last keyframe instead
    /// makes a rocket jump between updates rather than fly — worst on a 33-tick server, where the
    /// updates are twice as far apart.
    ///
    /// **Three quantities, three rules, all Valve's:**
    ///
    /// - Position and scale interpolate linearly.
    /// - Angles interpolate through <em>quaternions</em>, not per component:
    ///   <c>Lerp&lt;QAngle&gt;</c> in <c>mathlib.h</c> converts both to quaternions and slerps.
    ///   That is also what makes 350° to 10° a twenty-degree turn rather than a 340° one.
    /// - The animation cycle uses <c>LoopingLerp</c> from <c>lerp_functions.h</c>: a gap of half a
    ///   cycle or more means the animation wrapped past 1.0, so the lower value is raised by one
    ///   before interpolating and the fractional part taken. Without it a looping model rewinds
    ///   through its whole animation at every loop point.
    ///
    /// **Hermite is deliberately not implemented.** The engine's default is
    /// <c>Lerp_Hermite</c>, which needs three points and the tuning that goes with it; Valve
    /// itself falls back to linear for angles ("Can't do hermite with QAngles, get
    /// discontinuities") and exposes <c>INTERPOLATE_LINEAR_ONLY</c> for the rest. Linear is
    /// therefore a shape the engine also produces, rather than an invention. Recorded as an open
    /// gap rather than quietly called parity.
    ///
    /// Nothing is extrapolated. Before the first keyframe there is nothing, and after the last
    /// the pose holds — a rocket that flew on forever after its final update would be a plausible
    /// trajectory that no part of the demo ever stated.
    /// </remarks>
    public ScenePose? At(double tick)
    {
        if (AtKeyframe((int)Math.Floor(tick)) is not { } earlier)
        {
            return null;
        }

        int index = IndexAt((int)Math.Floor(tick));

        if (index < 0 || index + 1 >= _keyframes.Count)
        {
            return earlier;
        }

        (int fromTick, ScenePose from) = _keyframes[index];
        (int toTick, ScenePose to) = _keyframes[index + 1];

        if (toTick <= fromTick)
        {
            return earlier;
        }

        float fraction = (float)Math.Clamp((tick - fromTick) / (toTick - fromTick), 0.0, 1.0);

        (float pitch, float yaw, float roll) = SlerpAngles(from, to, fraction);

        return new ScenePose
        {
            X = float.Lerp(from.X, to.X, fraction),
            Y = float.Lerp(from.Y, to.Y, fraction),
            Z = float.Lerp(from.Z, to.Z, fraction),
            Pitch = pitch,
            Yaw = yaw,
            Roll = roll,
            Scale = float.Lerp(from.Scale, to.Scale, fraction),

            // **A sequence change is a cut, not a blend.** Two animations share no timeline, so a
            // cycle of 0.9 in one and 0.1 in the next are not two points on one curve, and the
            // wrap rule below would fire on unrelated numbers. The engine restarts the variable
            // instead: c_baseanimating.cpp calls m_iv_flCycle.Reset() when the sequence changes.
            Sequence = from.Sequence,
            Cycle = from.Sequence == to.Sequence
                ? LoopingLerp(from.Cycle, to.Cycle, fraction)
                : from.Cycle,
        };
    }

    /// <summary>The pose the demo actually stated at or before a tick, with nothing added.</summary>
    /// <param name="tick">The tick to ask about.</param>
    /// <returns>The pose, or <c>null</c> when the entity did not exist then.</returns>
    /// <remarks>
    /// Separate from <see cref="At(double)"/> because "what did the demo say" and "what should be
    /// drawn" are different questions and only one of them is evidence. Anything reasoning about
    /// the recording — a report, a test, an export — wants this one.
    ///
    /// Binary search rather than a scan: a viewer asks for every tracked entity on every frame, so
    /// a linear walk would be the whole cost of drawing.
    /// </remarks>
    public ScenePose? AtKeyframe(int tick)
    {
        int index = IndexAt(tick);

        return index < 0 ? null : _keyframes[index].Pose;
    }

    /// <summary>Position of the last keyframe at or before a tick, or −1 when there is none.</summary>
    private int IndexAt(int tick)
    {
        if (_keyframes.Count == 0 || tick >= _endTick || tick < _keyframes[0].Tick)
        {
            return -1;
        }

        int low = 0;
        int high = _keyframes.Count - 1;

        while (low < high)
        {
            // Rounded up, so the search moves towards the later keyframe and cannot stall on low.
            // Rounding down hangs rather than answering wrongly: with the two adjacent, the
            // midpoint is low, the branch assigns low to itself and the loop never ends.
            int middle = low + ((high - low + 1) / 2);

            if (_keyframes[middle].Tick <= tick)
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }

        return low;
    }

    /// <summary>Interpolates an animation cycle, allowing for it having wrapped past 1.</summary>
    /// <remarks>
    /// **Valve's <c>LoopingLerp&lt;float&gt;</c>**, from <c>src/game/client/lerp_functions.h</c>:
    ///
    /// <code>
    /// if ( fabs( flTo - flFrom ) >= 0.5f )
    /// {
    ///     if (flFrom &lt; flTo) flFrom += 1.0f; else flTo += 1.0f;
    /// }
    /// float s = flTo * flPercent + flFrom * (1.0f - flPercent);
    /// s = s - (int)(s);
    /// if (s &lt; 0.0f) s = s + 1.0f;
    /// </code>
    ///
    /// The half-cycle threshold is the whole trick: a large gap means the animation looped rather
    /// than jumped, so the smaller value belongs to the next repetition.
    /// </remarks>
    private static float LoopingLerp(float from, float to, float fraction)
    {
        if (Math.Abs(to - from) >= 0.5f)
        {
            if (from < to)
            {
                from += 1f;
            }
            else
            {
                to += 1f;
            }
        }

        float value = (to * fraction) + (from * (1f - fraction));

        value -= (int)value;

        return value < 0f ? value + 1f : value;
    }

    /// <summary>Interpolates a rotation the way the engine does — through quaternions.</summary>
    /// <remarks>
    /// **Not component-wise, and Valve is explicit about why.** <c>Lerp&lt;QAngle&gt;</c> in
    /// <c>mathlib.h</c> is specialised to convert both angles to quaternions and slerp between
    /// them, and <c>Lerp_Hermite&lt;QAngle&gt;</c> refuses hermite entirely with the comment
    /// "Can't do hermite with QAngles, get discontinuities".
    ///
    /// The conversion is <c>AngleQuaternion</c> from <c>mathlib_base.cpp</c>, transcribed rather
    /// than rederived: a QAngle is (pitch, yaw, roll) about three different axes in a specific
    /// order, and a plausible-looking reconstruction produces rotations that are wrong only for
    /// some inputs.
    ///
    /// Taking the short way round falls out of this rather than being added: slerp follows the
    /// shorter arc, which is why 350° to 10° passes through zero instead of turning 340° the
    /// other way.
    /// </remarks>
    private static (float Pitch, float Yaw, float Roll) SlerpAngles(
        ScenePose from, ScenePose to, float fraction)
    {
        if (SameBits(from.Pitch, to.Pitch) &&
            SameBits(from.Yaw, to.Yaw) &&
            SameBits(from.Roll, to.Roll))
        {
            // Valve's own first line ("Avoid precision errors"), and not only a shortcut: a round
            // trip through quaternions and back perturbs the angles slightly, so a still prop
            // would jitter.
            //
            // Exact equality is deliberate and Equals says so without a suppression: the question
            // is whether the demo repeated the same stored bits, not whether two computed angles
            // are close. A tolerance here would freeze genuinely slow rotations.
            return (from.Pitch, from.Yaw, from.Roll);
        }

        Quaternion start = ToQuaternion(from.Pitch, from.Yaw, from.Roll);
        Quaternion end = ToQuaternion(to.Pitch, to.Yaw, to.Roll);

        return ToAngles(Quaternion.Slerp(start, end, fraction));
    }

    /// <summary>Whether two angles are the same stored value, bit for bit.</summary>
    /// <remarks>
    /// **A question about storage, not about magnitude**, which is why it is not a tolerance
    /// comparison and why the analyzer's usual advice does not apply (S1244). The demo either
    /// repeated a value or stated a new one; comparing bits says exactly that. A tolerance here
    /// would treat a slow rotation as stillness and freeze it.
    /// </remarks>
    private static bool SameBits(float left, float right) =>
        BitConverter.SingleToInt32Bits(left) == BitConverter.SingleToInt32Bits(right);

    /// <summary><c>AngleQuaternion</c> from <c>mathlib_base.cpp</c>.</summary>
    private static Quaternion ToQuaternion(float pitch, float yaw, float roll)
    {
        (float sinYaw, float cosYaw) = MathF.SinCos(float.DegreesToRadians(yaw) * 0.5f);
        (float sinPitch, float cosPitch) = MathF.SinCos(float.DegreesToRadians(pitch) * 0.5f);
        (float sinRoll, float cosRoll) = MathF.SinCos(float.DegreesToRadians(roll) * 0.5f);

        float sinRollCosPitch = sinRoll * cosPitch;
        float cosRollSinPitch = cosRoll * sinPitch;
        float cosRollCosPitch = cosRoll * cosPitch;
        float sinRollSinPitch = sinRoll * sinPitch;

        return new Quaternion(
            (sinRollCosPitch * cosYaw) - (cosRollSinPitch * sinYaw),
            (cosRollSinPitch * cosYaw) + (sinRollCosPitch * sinYaw),
            (cosRollCosPitch * sinYaw) - (sinRollSinPitch * cosYaw),
            (cosRollCosPitch * cosYaw) + (sinRollSinPitch * sinYaw));
    }

    /// <summary>The inverse, as <c>QuaternionAngles</c> computes it.</summary>
    /// <remarks>
    /// The engine routes this through a matrix, but the direct form is in the same function under
    /// an <c>#else</c> and is the one transcribed here. Its noted singularity near pitch ±90 is
    /// real and shared: at straight up or down, yaw and roll describe the same rotation and the
    /// split between them is arbitrary.
    /// </remarks>
    private static (float Pitch, float Yaw, float Roll) ToAngles(Quaternion q)
    {
        float m11 = (2f * q.W * q.W) + (2f * q.X * q.X) - 1f;
        float m12 = (2f * q.X * q.Y) + (2f * q.W * q.Z);
        float m13 = (2f * q.X * q.Z) - (2f * q.W * q.Y);
        float m23 = (2f * q.Y * q.Z) + (2f * q.W * q.X);
        float m33 = (2f * q.W * q.W) + (2f * q.Z * q.Z) - 1f;

        return (
            float.RadiansToDegrees(MathF.Asin(Math.Clamp(-m13, -1f, 1f))),
            float.RadiansToDegrees(MathF.Atan2(m12, m11)),
            float.RadiansToDegrees(MathF.Atan2(m23, m33)));
    }
}
