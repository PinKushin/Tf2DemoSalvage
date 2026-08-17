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

    /// <summary>How fast the animation advances, as a multiple of its authored rate.</summary>
    /// <remarks>
    /// **The third factor in Valve's cycle advance** (<c>c_baseanimating.cpp:5493</c>):
    ///
    /// <code>
    /// float addcycle = flInterval * cyclerate * m_flPlaybackRate;
    /// </code>
    ///
    /// One when the demo never said, which is the engine's default and not a sentinel — a rate of
    /// zero would freeze the animation, which is the wrong reading of "never mentioned".
    ///
    /// Added 2026-08-16. <c>m_flPlaybackRate</c> had been retained and decoded since the whitelist
    /// was written, and nothing outside a unit test ever read it, so anything not playing at rate 1
    /// animated at the wrong speed.
    /// </remarks>
    public float PlaybackRate { get; init; } = 1f;

    /// <summary>Which animation is playing.</summary>
    /// <remarks>
    /// **Zero when the demo never said, because zero is the engine's default** — <c>m_nSequence</c>
    /// is a plain int initialised to 0 (<c>BaseAnimatingOverlay.cpp:104</c>), and a delta format only
    /// sends what changed from the baseline. "Never mentioned" and "sequence 0" are the same
    /// statement about the wire.
    ///
    /// **This was −1 until 2026-08-16, and the sentinel was doing harm.** Every drawing consumer
    /// immediately undid it — <c>Math.Max(0, pose.Sequence)</c> in two places, and
    /// <c>PropModels.Select</c> opening with <c>sequence &lt; 0 ? 0 : sequence</c> under a comment
    /// saying "a sequence the demo never mentioned is sequence zero, not an error". But
    /// <c>InterpolateCycle</c> does not clamp, it COMPARES: a change of sequence is a cut, so
    /// a first keyframe of −1 followed by an explicit 0 registered a change that never happened and
    /// froze the cycle across that span.
    ///
    /// Invented sentinels for absent values are the hazard the owner's rule names: Valve's data is
    /// values rather than pointers, so absence is always a default and never a third state.
    /// </remarks>
    public int Sequence { get; init; }

    /// <summary>How far through that animation, from 0 to 1.</summary>
    public float Cycle { get; init; }

    /// <summary>How fast the entity is moving horizontally, when that was worked out.</summary>
    /// <remarks>
    /// **Only players carry this, and only because nothing else can supply it.** A demo networks
    /// no animation state for a player, so which sequence to play has to be computed - and the
    /// input that decides standing from running is horizontal speed, which the engine reads from
    /// the entity's own velocity. Null for anything whose animation the demo does state.
    /// </remarks>
    public float? Speed { get; init; }

    /// <summary>
    /// A player's <c>m_fFlags</c>, for choosing an activity, or null when nothing said.
    /// </summary>
    /// <remarks>
    /// **Carried on the pose because the choice cannot be made where the flags are known.** A player
    /// becomes a prop before its model has been read, and the activity lookup needs the model — so
    /// the two happen in separate passes and the flags have to travel between them.
    ///
    /// Discrete, like <see cref="Body"/> and <see cref="Hidden"/>: there is no halfway between
    /// crouched and standing, so an interpolated pose takes the earlier keyframe's value rather than
    /// blending toward the next.
    ///
    /// Null for every player but the recorder in a POV demo, since the send prop is in
    /// <c>DT_LocalPlayerExclusive</c>; a SourceTV recording carries it for all of them.
    /// </remarks>
    public int? Flags { get; init; }

    /// <summary>The <c>move_x</c> pose parameter: how much of the motion is forward.</summary>
    /// <remarks>
    /// **A movement sequence is a blend grid and these are its coordinates.** Without them the
    /// engine's own lookup takes the grid's corner, which is one fixed direction — the legs then
    /// run that way however the body is turned.
    ///
    /// Ported from <c>CMultiPlayerAnimState::ComputePoseParam_MoveYaw</c>
    /// (<c>multiplayer_animstate.cpp:1575</c>):
    ///
    /// <code>
    /// float flYaw = flAngle - m_PoseParameterData.m_flEstimateYaw;
    /// flYaw = AngleNormalize( -flYaw );
    /// flYaw = SnapYawTo( flYaw );
    /// vecCurrentMoveYaw.x =  cos( DEG2RAD( flYaw ) );
    /// vecCurrentMoveYaw.y = -sin( DEG2RAD( flYaw ) );
    /// </code>
    ///
    /// so the pair is the unit vector of travel expressed in the body's own frame — <c>(1, 0)</c>
    /// running straight forward, <c>(-1, 0)</c> backpedalling.
    ///
    /// Zero for a player standing still, which is what the engine leaves them at.
    /// </remarks>
    /// <summary>Which alternative each of the model's body parts shows.</summary>
    /// <remarks>
    /// **A capture point's label and a player's drawn weapon are the same mechanism.** A model's
    /// body parts each offer alternatives and this packs one choice per part; the renderer draws
    /// the selected mesh of each part rather than all of them.
    ///
    /// Zero when the entity never sent one, which selects every part's first alternative — what the
    /// engine shows for an entity that never sets it.
    /// </remarks>
    public int Body { get; init; }

    public float MoveX { get; init; }

    /// <summary>The <c>move_y</c> pose parameter: how much of the motion is sideways.</summary>
    public float MoveY { get; init; }

    /// <summary>Which skin family paints this entity, where zero is the model's own.</summary>
    /// <remarks>
    /// **A team colour is a different material rather than a tint.** TF2's player models carry two
    /// skin families and the game picks by team - RED is 0 and BLU is 1. Left at zero for anything
    /// that has no team.
    /// </remarks>
    public int Skin { get; init; }

    /// <summary>Whether the engine was told not to draw this entity at this moment.</summary>
    /// <remarks>
    /// **Part of the pose rather than an end to the track**, because a hidden entity comes back.
    /// A health pack that has been taken sets <c>EF_NODRAW</c> and respawns in the same place a
    /// few seconds later; ending its track would lose everything after the first pickup.
    /// </remarks>
    public bool Hidden { get; init; }

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
/// <param name="AttachedTo">
/// The entity whose skeleton carries this one, or <c>null</c> when it stands on its own origin.
/// </param>
public readonly record struct SceneProp(
    int EntityIndex,
    string ModelPath,
    SceneModelKind Kind,
    ScenePose Pose,
    int? AttachedTo = null);

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

    /// <summary>
    /// For each keyframe, the last tick the demo restated that same pose.
    /// </summary>
    /// <remarks>
    /// **Parallel to <see cref="_keyframes"/> rather than folded into it**, so the public
    /// <see cref="Keyframes"/> shape and everything reading it are untouched. Equal to the
    /// keyframe's own tick for a pose stated once, and later for one held.
    ///
    /// This is the interval an interpolation may legitimately run over. A demo states a stationary
    /// entity's pose repeatedly and those repeats are collapsed, so without this the gap between two
    /// stored keyframes reads as the duration of the movement between them — which for a door is
    /// ten seconds of drift instead of a tenth of a second of travel.
    /// </remarks>
    private readonly List<int> _heldUntil = [];

    /// <summary>
    /// How far behind the requested tick a pose is sampled — the engine's <c>cl_interp</c>.
    /// </summary>
    /// <remarks>
    /// **This is the delay a client renders at, not a smoothing fudge.** <c>cl_interp</c> defaults
    /// to 0.1 seconds, which at TF2's 66.67 ticks per second is 6.67 ticks; seven is that rounded.
    /// Drawing the recent past is what lets a client interpolate at all — at the present moment
    /// there is nothing yet to interpolate toward.
    ///
    /// **Ticks rather than seconds is a known simplification.** A 33-tick server's 0.1 seconds is
    /// half as many ticks, and this class is not told the tick rate; the honest form takes the
    /// interval from the demo header, which is a change to how tracks are constructed.
    /// </remarks>
    private const int InterpolationDelayTicks = 7;

    private int _endTick = int.MaxValue;

    /// <summary>Starts a track for one entity.</summary>
    /// <param name="entityIndex">Slot in the entity table.</param>
    /// <param name="modelPath">The model this entity draws as.</param>
    /// <param name="serialNumber">The engine's serial for this occupant of the slot.</param>
    public ScenePropTrack(int entityIndex, string modelPath, int serialNumber = 0)
    {
        EntityIndex = entityIndex;
        ModelPath = modelPath;
        SerialNumber = serialNumber;
    }

    /// <summary>Slot in the entity table.</summary>
    public int EntityIndex { get; }

    /// <summary>The model this entity draws as.</summary>
    public string ModelPath { get; }

    /// <summary>The engine's serial for this occupant of the slot.</summary>
    /// <remarks>
    /// **An entity is its index AND its serial.** The index is a slot the engine reissues; the
    /// serial is what distinguishes one occupant from the next.
    /// </remarks>
    public int SerialNumber { get; }

    /// <summary>Whether an update in this slot continues this track or starts a new object.</summary>
    /// <param name="serialNumber">The serial of the entity now occupying the slot.</param>
    /// <returns><c>true</c> to keep appending to this track.</returns>
    /// <remarks>
    /// **Identity is the serial number, which is the engine's own rule** — the same one
    /// <c>EntityStateTable</c> applies to entity state.
    ///
    /// **The model path used to decide this, and it was wrong in both directions.** It could not see
    /// two consecutive rockets in one slot, which share a model, so their positions merged into one
    /// track that drew as an object teleporting. And it reported changes that never happened: a
    /// capture point calls <c>SetModel</c> on every capture (<c>team_control_point.cpp:569</c>), so
    /// changing hands ended its track and split one object into several.
    ///
    /// **No fallback, because there is nothing to fall back from.** The serial reaching here comes
    /// from an <c>EntityState</c>, and the state table has already applied the engine's create rule
    /// — a serial is compared only on an enter, and a new occupant gets a new state — so by the time
    /// a track sees it, identity is settled and the value is authoritative. An earlier draft of this
    /// took a nullable serial and treated null as "continue"; that path could never execute, which
    /// makes it dead code wearing the costume of a safety net.
    /// </remarks>
    public bool Continues(int serialNumber) => SerialNumber == serialNumber;

    /// <summary>The entity whose skeleton carries this one, when it has no place of its own.</summary>
    /// <remarks>
    /// **A bone-merged entity has no transform, by design.** A hat, a badge and a carried weapon
    /// are attached with <c>FollowEntity</c>, which sets <c>EF_BONEMERGE</c> and then zeroes local
    /// origin and angles (<c>shared/baseentity_shared.cpp:2360</c>) — the client matches the child
    /// model's bones to the parent's **by name** and uses the parent's matrices outright, so a
    /// position would never be read and is not sent.
    ///
    /// Which is why this is a property of the track rather than of the pose: it does not change
    /// tick to tick, and there is no pose to put it in. A track with an owner keeps its keyframes
    /// for the sequence and skin it does carry, and its position stays at zero because zero is
    /// literally what the engine set.
    ///
    /// Settable rather than fixed at construction: the owner arrives on a later delta than the
    /// model on some entities, and refusing the track until both have landed would lose the
    /// cosmetic for however long that takes.
    /// </remarks>
    public int? AttachedTo { get; internal set; }

    /// <summary>Which of Valve's model types this reference names.</summary>
    /// <remarks>
    /// Decided by the reference itself, which is all the string table gives. A leading asterisk is
    /// an inline BSP submodel numbered within the map — <c>*3</c> is the map's fourth. Everything
    /// else is told apart by extension, the way the engine's own loader does.
    /// </remarks>
    public SceneModelKind Kind => Classify(ModelPath);

    /// <summary>How many moments the entity actually changed at.</summary>
    public int KeyframeCount => _keyframes.Count;

    /// <summary>The moments the demo stated, in order, with nothing added.</summary>
    /// <remarks>
    /// **What the recording said, as opposed to what gets drawn.** Anything reasoning about the
    /// demo itself — a report, an export, a test asking whether a property ever arrived — wants
    /// these rather than <see cref="At(double)"/>, which interpolates.
    ///
    /// It is also the difference between a cheap question and an expensive one: asking a track
    /// about every tick of a long demo is hundreds of thousands of lookups for a few dozen stored
    /// poses.
    /// </remarks>
    public IReadOnlyList<(int Tick, ScenePose Pose)> Keyframes => _keyframes;

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
            // **Dropped from the list but not from the record.** Collapsing a repeat saves the
            // memory it is there to save; forgetting WHEN it was last repeated throws away the only
            // evidence of when the entity started moving, and the interpolation then smears the
            // motion backwards across the whole stationary stretch.
            //
            // Measured: a shutter on cp_process holds at Z 640 and steps to 785. With the repeat
            // simply discarded, tick 100 of a 610-tick hold reported 663.771 — the straight line
            // from the first keyframe to the step. On screen that is a door drifting upward for no
            // reason for ten seconds, and then, on the way back, sinking below its own frame into
            // the floor.
            _heldUntil[^1] = tick;
            return;
        }

        _keyframes.Add((tick, pose));
        _heldUntil.Add(tick);
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
    ///
    /// **The interpolation is CAUSAL, and getting that wrong put doors through the floor.** A client
    /// draws <c>targettime = now - interp</c> and can only interpolate between entries it has already
    /// received; an update that has not arrived cannot pull anything toward it. This reader holds the
    /// whole demo, so without the delay it could see a keyframe seconds in the future and slide
    /// toward it the entire time — a shutter drifting open on its own for ten seconds, and sinking
    /// below its own frame on the way back (B94). Two rules restore it:
    ///
    /// - the pose is sampled <see cref="InterpolationDelayTicks"/> behind the requested tick, which
    ///   is what <c>cl_interp</c> does;
    /// - a keyframe later than the requested tick is not used, because it has not arrived yet.
    ///
    /// Together those turn a long gap into exactly what a client shows: the old pose held, then the
    /// movement rendered over the interpolation window once the update lands.
    /// </remarks>
    public ScenePose? At(double tick)
    {
        if (AtKeyframe((int)Math.Floor(tick)) is not { } earlier)
        {
            return null;
        }

        // The moment being drawn, one interpolation window behind the moment being asked for.
        double target = tick - InterpolationDelayTicks;

        if (target <= _keyframes[0].Tick)
        {
            // Nothing had been received yet, so the first stated pose is all a client would have.
            return _keyframes[0].Pose;
        }

        int index = IndexAt((int)Math.Floor(target));

        if (index < 0 || index + 1 >= _keyframes.Count)
        {
            return earlier;
        }

        (int statedTick, ScenePose from) = _keyframes[index];
        (int toTick, ScenePose to) = _keyframes[index + 1];

        // **A keyframe later than the tick being asked for has not arrived yet.** This is the whole
        // of the causality rule: a client at tick 100 cannot be pulled toward an update stated at
        // tick 610, and a reader holding the entire demo can. Holding the earlier pose is what the
        // client shows, and skipping this check is what walked a shutter open over ten seconds.
        if (toTick > tick)
        {
            return from;
        }

        // **The movement starts where the pose was last RESTATED, not where it was first stated.**
        // A stationary entity's repeats are collapsed, so the stored keyframe can be seconds older
        // than the last moment the demo confirmed that pose — and interpolating from the older tick
        // spreads a tenth of a second of travel over the whole hold.
        int fromTick = _heldUntil.Count > index ? _heldUntil[index] : statedTick;

        if (toTick <= fromTick)
        {
            return earlier;
        }

        tick = target;

        float fraction = (float)Math.Clamp((tick - fromTick) / (toTick - fromTick), 0.0, 1.0);

        (float pitch, float yaw, float roll) = SlerpAngles(from, to, fraction);

        // Hermite when a third sample exists, which is the engine's default rather than an extra:
        // the client splines whenever there is an older entry, and falls back to linear only when
        // there is not, or when INTERPOLATE_LINEAR_ONLY is set on the variable.
        ScenePose? previous = index > 0 ? Renormalise(index, toTick - fromTick) : null;

        float cycle = InterpolateCycle(previous, from, to, fraction);

        return new ScenePose
        {
            X = Curve(previous?.X, from.X, to.X, fraction),
            Y = Curve(previous?.Y, from.Y, to.Y, fraction),
            Z = Curve(previous?.Z, from.Z, to.Z, fraction),
            Pitch = pitch,
            Yaw = yaw,
            Roll = roll,
            Scale = Curve(previous?.Scale, from.Scale, to.Scale, fraction),

            Sequence = from.Sequence,
            Cycle = cycle,

            // Discrete, so it takes the earlier keyframe's value rather than being blended.
            // Half-hidden is not a state the engine has.
            Hidden = from.Hidden,

            // **Discrete for the same reason, and missing for a whole session.** m_nBody selects
            // which alternative of a body part to draw, so there is no halfway between a capture
            // point's "?" sign and its RED one — and being absent from this list meant the
            // interpolated pose carried the record's default of zero. Every capture point drew "?"
            // while the demo, the model and the packer all measured correct, because the number
            // was rebuilt without it at the last hop before the draw.
            //
            // Second time this exact shape has appeared: a record constructed field by field, one
            // field forgotten, and a default that is also a legitimate value so nothing can report
            // it. ScenePlayer.Yaw was the other.
            Body = from.Body,

            // **And the skin, which is how a team colour is carried.** TF2 paints RED and BLU as
            // two skin families of one model rather than as a tint, so losing this draws every
            // entity in family zero — the RED one — however the demo set it. Discrete like the
            // others: there is no halfway between two materials.
            //
            // Found immediately after Body, in the same list, by asking what ELSE this rebuild
            // forgets rather than waiting for the next symptom to arrive.
            Skin = from.Skin,

            // Fourth field added to this list after being forgotten from it by default — Yaw, Body
            // and Skin were the others. Carried rather than recomputed: a rate that reverted to 1
            // between keyframes would look like an animation that speeds up whenever the viewer
            // scrubs, which is a symptom nobody would trace back to here.
            PlaybackRate = from.PlaybackRate,

            // **Fifth field on this list, and added deliberately rather than after a symptom.** Yaw,
            // Body, Skin and PlaybackRate were each forgotten here first and each defaulted to a
            // legitimate value, so nothing could report the loss. Flags defaulting to null would
            // read as "the recording never said" and quietly stand every crouching player up.
            //
            // Discrete: there is no halfway between crouched and standing.
            Flags = from.Flags,
        };
    }

    /// <summary>Advances the animation cycle, allowing for both wrapping and sequence changes.</summary>
    /// <remarks>
    /// **A sequence change is a cut, not a blend.** Two animations share no timeline, so a cycle
    /// of 0.9 in one and 0.1 in the next are not two points on one curve — and it is exactly the
    /// wrap rule that would otherwise fire on those unrelated numbers. The engine restarts the
    /// variable instead: <c>c_baseanimating.cpp</c> calls <c>m_iv_flCycle.Reset()</c>.
    ///
    /// The same applies one sample further back: a third point from a different animation is not
    /// a tangent, so the spline drops to the two-point form rather than curving through it.
    /// </remarks>
    private static float InterpolateCycle(
        ScenePose? previous, ScenePose from, ScenePose to, float fraction)
    {
        if (from.Sequence != to.Sequence)
        {
            return from.Cycle;
        }

        return previous is { } older && older.Sequence == from.Sequence
            ? LoopingCurve(older.Cycle, from.Cycle, to.Cycle, fraction)
            : LoopingLerp(from.Cycle, to.Cycle, fraction);
    }

    /// <summary>Rebuilds the sample before <paramref name="index"/> at an even spacing.</summary>
    /// <param name="index">Position of the keyframe the interpolation starts from.</param>
    /// <param name="span">Ticks from that keyframe to the one after it.</param>
    /// <returns>The synthetic earlier sample, or <c>null</c> when hermite does not apply.</returns>
    /// <remarks>
    /// **<c>TimeFixup_Hermite</c>, and it is not an optimisation — it is what makes the spline
    /// usable on real data.** A hermite curve assumes its three samples are evenly spaced, and a
    /// demo's are not: the server sends when it sends, and a packet arriving late leaves a gap of
    /// a different size from the one before it. Valve rebuilds the oldest sample rather than
    /// feeding the spline uneven spacing —
    ///
    /// <code>
    /// float frac = dt1 / dt2;
    /// fixup.changetime = start->changetime - dt1;
    /// fixup.value = Lerp( 1-frac, prev->value, start->value );
    /// </code>
    ///
    /// — placing a synthetic sample exactly <c>dt1</c> before the start. Skipping it does not
    /// produce a slightly different curve; it produces one that overshoots whenever the packet
    /// spacing wobbles, which on a real demo is most of the time.
    /// </remarks>
    private ScenePose? Renormalise(int index, int span)
    {
        (int previousTick, ScenePose previous) = _keyframes[index - 1];

        int gap = _keyframes[index].Tick - previousTick;

        if (gap <= 0 || span <= 0)
        {
            return null;
        }

        if (Math.Abs(span - gap) <= 0.0001f)
        {
            // Already evenly spaced, so the stored sample is the one the spline wants.
            return previous;
        }

        ScenePose start = _keyframes[index].Pose;
        float fraction = 1f - ((float)span / gap);

        return new ScenePose
        {
            X = float.Lerp(previous.X, start.X, fraction),
            Y = float.Lerp(previous.Y, start.Y, fraction),
            Z = float.Lerp(previous.Z, start.Z, fraction),
            Scale = float.Lerp(previous.Scale, start.Scale, fraction),
            Sequence = previous.Sequence,
            Cycle = previous.Sequence == start.Sequence
                ? LoopingLerp(previous.Cycle, start.Cycle, fraction)
                : previous.Cycle,
        };
    }

    /// <summary>Hermite through three samples, or linear when there is no third.</summary>
    /// <remarks>
    /// **<c>Lerp_Hermite</c> from <c>lerp_functions.h</c>**, transcribed:
    ///
    /// <code>
    /// T d1 = p1 - p0;
    /// T d2 = p2 - p1;
    /// output  = p1 * (2*tCube-3*tSqr+1);
    /// output += p2 * (-2*tCube+3*tSqr);
    /// output += d1 * (tCube-2*tSqr+t);
    /// output += d2 * (tCube-tSqr);
    /// </code>
    ///
    /// The tangents come from the differences either side, so a curving path bends through its
    /// updates rather than turning a corner at each one.
    /// </remarks>
    private static float Curve(float? p0, float p1, float p2, float t)
    {
        if (p0 is not { } previous)
        {
            return float.Lerp(p1, p2, t);
        }

        float d1 = p1 - previous;
        float d2 = p2 - p1;

        float tSqr = t * t;
        float tCube = t * tSqr;

        return (p1 * ((2f * tCube) - (3f * tSqr) + 1f)) +
               (p2 * ((-2f * tCube) + (3f * tSqr))) +
               (d1 * (tCube - (2f * tSqr) + t)) +
               (d2 * (tCube - tSqr));
    }

    /// <summary>Hermite for a value that wraps at 1, such as an animation cycle.</summary>
    /// <remarks>
    /// **<c>LoopingLerp_Hermite</c>**, and the second half of it is the interesting part. Raising
    /// <c>p1</c> to reach <c>p2</c> can leave it more than half a cycle from <c>p0</c>, so Valve
    /// re-checks that pair afterwards — its own comment gives the case it was written for:
    /// "important for vars that are decreasing from p0-&gt;p1-&gt;p2 where p1 is fixed up relative
    /// to p2, eg p0 = 0.2, p1 = 0.1, p2 = 0.9".
    ///
    /// Note the threshold here is <c>&gt;</c> where <c>LoopingLerp</c> uses <c>&gt;=</c>. That is
    /// Valve's, kept rather than tidied: an exactly-half-cycle step is treated as a wrap by one
    /// and not by the other, and choosing which is right is not this project's call to make.
    /// </remarks>
    private static float LoopingCurve(float p0, float p1, float p2, float t)
    {
        if (Math.Abs(p1 - p0) > 0.5f)
        {
            if (p0 < p1)
            {
                p0 += 1f;
            }
            else
            {
                p1 += 1f;
            }
        }

        if (Math.Abs(p2 - p1) > 0.5f)
        {
            if (p1 < p2)
            {
                p1 += 1f;

                // Valve's re-check: p1 has moved, so it may now be more than half a cycle from p0.
                if (Math.Abs(p1 - p0) > 0.5f)
                {
                    if (p0 < p1)
                    {
                        p0 += 1f;
                    }
                    else
                    {
                        p1 += 1f;
                    }
                }
            }
            else
            {
                p2 += 1f;
            }
        }

        float value = Curve(p0, p1, p2, t);

        value -= (int)value;

        return value < 0f ? value + 1f : value;
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
