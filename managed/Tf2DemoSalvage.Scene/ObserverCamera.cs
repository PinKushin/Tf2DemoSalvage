using System;
using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Core.Primitives;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene.Prediction;

using static Tf2DemoSalvage.Core.Scene.ObserverModes;

namespace Tf2DemoSalvage.Scene;

/// <summary>What kind of entity an observer target is, which decides <c>GetChaseCamViewOffset</c>.</summary>
public enum ObserverSubjectKind
{
    /// <summary>A player: the view vectors, scaled by its model.</summary>
    Player,

    /// <summary>A full-size building: 64 up (<c>C_TFPlayer::GetChaseCamViewOffset</c>, <c>c_tf_player.cpp:8968</c>).</summary>
    Building,

    /// <summary>A mini building: 40 up.</summary>
    MiniBuilding,

    /// <summary>Anything else: <c>VEC_DEAD_VIEWHEIGHT</c>, "assume it's the players ragdoll".</summary>
    Other,
}

/// <summary>A player's corpse as the death view sees it.</summary>
/// <param name="DeathAnimation">
/// <c>C_TFRagdoll::IsDeathAnim</c>: set once in <c>CreateTFRagdoll</c> (<c>c_tf_player.cpp:852</c>) and never cleared.
/// </param>
/// <param name="Root">
/// <c>IRagdoll::GetRagdollOrigin</c>, the first physics object's position (<c>ragdoll.cpp:177</c>); null until the
/// corpse is simulated, which is <c>GetRepresentativeRagdoll</c> returning null.
/// </param>
/// <param name="FirstGib">
/// <c>m_hFirstGib</c>'s mass centre in the world (<c>GetObserverCamOrigin</c>, <c>c_tf_player.cpp:6817-6838</c>): a gibbed
/// player's first piece, a decapitated one's head; null when there is none or it has faded.
/// </param>
/// <param name="HeadGib"><c>m_hHeadGib</c>, the head the death cam views from; null for every other death.</param>
/// <param name="Body">
/// <c>m_hRagdoll->GetAbsOrigin()</c>: the root once simulated (<c>RagdollMoved</c>, <c>c_baseanimating.cpp:4608</c>), else
/// where it was made; null when the ragdoll entity is gone and only its pieces are left.
/// </param>
public readonly record struct ObserverRagdoll(
    bool DeathAnimation, Vector3? Root, Vector3? FirstGib = null, ObserverHeadGib? HeadGib = null, Vector3? Body = null);

/// <summary>A decapitated player's head, as <c>CalcDeathCamView</c> reads it (B746).</summary>
/// <param name="MassCentre">Its physics object's mass centre in the world.</param>
/// <param name="Origin">Its <c>GetAbsOrigin()</c>.</param>
/// <param name="Angles">Its <c>GetAbsAngles()</c>.</param>
public readonly record struct ObserverHeadGib(Vector3 MassCentre, Vector3 Origin, (float Pitch, float Yaw, float Roll) Angles);

/// <summary>Which corpse is a player's, as the death view reads it (B745).</summary>
public static class ObserverRagdolls
{
    /// <summary>
    /// A player's corpse at a tick: <c>m_hRagdoll</c>, whether it plays a death animation, its root, and his first and head
    /// gibs.
    /// </summary>
    /// <param name="corpses">The recording's corpses, in the order their props are numbered.</param>
    /// <param name="roots">The simulated corpses' roots, by prop entity index (<c>CorpsePhysics.Roots</c>).</param>
    /// <param name="player">The dead player's entity index.</param>
    /// <param name="tick">The tick.</param>
    /// <param name="massCentres">The simulated gibs' mass centres, by prop entity index (<c>CorpsePhysics.MassCentres</c>).</param>
    /// <param name="placements">The simulated gibs' origins and angles (<c>CorpsePhysics.Placements</c>).</param>
    /// <returns>The corpse, or null when the player has neither a ragdoll nor a piece at the tick.</returns>
    /// <remarks>
    /// The player's latest corpse. *Interpolated:* the engine follows his networked <c>m_hRagdoll</c>; a player has one
    /// corpse at a time while dead. A gibbed corpse makes no <c>IRagdoll</c>, so it has no root, and a corpse not yet
    /// simulated has none either (<c>GetRepresentativeRagdoll</c> returning null). Past the ragdoll entity's end only its
    /// pieces are left: they are client entities of their own.
    ///
    /// **The first gib is the first piece made, and only that one** (B746): <c>CreatePlayerGibs</c> keeps the first
    /// <c>BreakModelCreateSingle</c> in <c>m_hFirstGib</c> (<c>c_tf_player.cpp:7506</c>), and a handle to a faded piece is
    /// null rather than the next piece. A whole gib's first is piece 0; a decapitation makes one piece, its head. A death
    /// that threw nothing because a feign death's pieces still held the handle (<c>:1070</c>, <c>:1102</c>) leaves the
    /// handles on those pieces. *Interpolated:* a gib not yet simulated reads as none, where the engine would read its
    /// render origin.
    /// </remarks>
    public static ObserverRagdoll? Of(
        IReadOnlyList<SceneRagdoll> corpses,
        IReadOnlyDictionary<int, Vector3> roots,
        int player,
        int tick,
        IReadOnlyDictionary<int, Vector3>? massCentres = null,
        IReadOnlyDictionary<int, (Vector3 Origin, (float Pitch, float Yaw, float Roll) Angles)>? placements = null)
    {
        ArgumentNullException.ThrowIfNull(corpses);
        ArgumentNullException.ThrowIfNull(roots);

        int found = -1;

        for (int i = 0; i < corpses.Count; i++)
        {
            SceneRagdoll corpse = corpses[i];

            if (corpse.PlayerIndex == player && corpse.FirstTick <= tick && (found < 0 || corpse.FirstTick >= corpses[found].FirstTick))
            {
                found = i;
            }
        }

        if (found < 0)
        {
            return null;
        }

        SceneRagdoll dead = corpses[found];
        (SceneRagdoll Maker, int Index, Vector3 Centre)? first = massCentres is null ? null : FirstGibOf(corpses, found, player, massCentres);
        bool present = tick <= dead.LastTick;

        if (!present && first is null)
        {
            return null;
        }

        ObserverHeadGib? head = first is { } piece && RagdollProps.ViewsFromItsHead(piece.Maker) &&
            placements?.TryGetValue(piece.Index, out (Vector3 Origin, (float Pitch, float Yaw, float Roll) Angles) placed) == true
                ? new ObserverHeadGib(piece.Centre, placed.Origin, placed.Angles)
                : null;

        Vector3? root = present && !dead.Gib && roots.TryGetValue(RagdollProps.FirstCorpseEntityIndex + found, out Vector3 at) ? at : null;

        return new ObserverRagdoll(
            present && RagdollDeath.SequenceFor(dead) is not null,
            root,
            first?.Centre,
            head,
            present ? root ?? new Vector3(dead.X, dead.Y, dead.Z) : null);
    }

    /// <summary>The piece a player's <c>m_hFirstGib</c> holds: his latest corpse's, or a feign death's it left in place.</summary>
    private static (SceneRagdoll Maker, int Index, Vector3 Centre)? FirstGibOf(
        IReadOnlyList<SceneRagdoll> corpses, int found, int player, IReadOnlyDictionary<int, Vector3> massCentres)
    {
        // Walked back through the run of feign deaths, as `RagdollProps` walks it for the guard: a feign death that threw
        // holds the handle, faded or not; one that threw nothing left it. A death that threw nothing because a feign death's
        // piece held the handle shows no piece of its own, and that piece, present now, was alive then.
        for (int at = found; ;)
        {
            SceneRagdoll dead = corpses[at];
            bool threw = RagdollProps.Throws(dead);

            if (threw && FirstGib(dead, massCentres) is { } own)
            {
                return (dead, own, massCentres[own]);
            }

            if ((threw && dead.FeignDeath) || RagdollProps.Before(corpses, at, player) is not { } earlier ||
                !corpses[earlier].FeignDeath)
            {
                return null;
            }

            at = earlier;
        }
    }

    /// <summary>The prop index of a corpse's <c>m_hFirstGib</c> while it is simulated, or null.</summary>
    private static int? FirstGib(SceneRagdoll dead, IReadOnlyDictionary<int, Vector3> massCentres)
    {
        if (dead.Gib)
        {
            int piece = RagdollProps.GibEntityIndex(dead, 0);

            return massCentres.ContainsKey(piece) ? piece : null;
        }

        if (!RagdollProps.IsDecapitation(dead))
        {
            return null;
        }

        // The head's place in the break list is the model's, so the block is walked: it holds that one piece.
        for (int piece = 0; piece < RagdollProps.MaximumGibsPerCorpse; piece++)
        {
            int index = RagdollProps.GibEntityIndex(dead, piece);

            if (massCentres.ContainsKey(index))
            {
                return index;
            }
        }

        return null;
    }
}

/// <summary>One entity as the observer views read it.</summary>
/// <param name="EntityIndex">Its index, for the trace filters.</param>
/// <param name="Origin"><c>GetAbsOrigin()</c>, which is also its <c>GetRenderOrigin()</c> here.</param>
/// <param name="Kind">What it is.</param>
/// <param name="IsAlive">Its <c>IsAlive()</c>.</param>
/// <param name="Ducking"><c>FL_DUCKING</c>.</param>
/// <param name="ModelScale"><c>GetModelScale()</c>.</param>
/// <param name="EyeAngles"><c>EyeAngles()</c>: the recorder's <c>pl.v_angle</c>, anyone else's <c>m_angEyeAngles</c>.</param>
/// <param name="ViewOffsetZ">
/// <c>GetViewOffset().z</c>. **Zero for anyone but the recorder**: <c>m_vecViewOffset</c> is received only in
/// <c>DT_LocalPlayerExclusive</c> (<c>c_baseplayer.cpp:219-273</c>) and <c>C_BaseEntity::Clear</c> zeroes it, so another
/// player's <c>EyePosition()</c> is his feet.
/// </param>
/// <param name="Ragdoll">Its corpse, when it has one (<c>m_hRagdoll</c>).</param>
public readonly record struct ObserverSubject(
    int EntityIndex,
    Vector3 Origin,
    ObserverSubjectKind Kind,
    bool IsAlive,
    bool Ducking,
    float ModelScale,
    (float Pitch, float Yaw, float Roll) EyeAngles,
    float ViewOffsetZ = 0f,
    ObserverRagdoll? Ragdoll = null);

/// <summary>What one frame's observer view is computed from.</summary>
/// <param name="Mode">The recorder's <c>m_iObserverMode</c>.</param>
/// <param name="CurTime"><c>gpGlobals->curtime</c>.</param>
/// <param name="FrameTime"><c>gpGlobals->frametime</c>: zero on a frame that is not a step (paused, a re-projection).</param>
/// <param name="Self">The recorder, the local player.</param>
/// <param name="Target">His <c>m_hObserverTarget</c>, or null.</param>
/// <param name="ViewAngles"><c>engine->GetViewAngles()</c>, which during playback are the demo's recorded view angles.</param>
/// <param name="DeathTime">His <c>m_flDeathTime</c>.</param>
/// <param name="FreezeSince">
/// The curtime of the data update that put him into <c>OBS_MODE_FREEZECAM</c> this time, which is when
/// <c>PostDataUpdate</c> stamps the freeze cam (<c>c_baseplayer.cpp:885-891</c>); null outside it.
/// </param>
/// <param name="Sweep">
/// <c>UTIL_TraceHull</c> with <c>WALL_MIN</c>/<c>WALL_MAX</c> and <c>MASK_SOLID</c>: start, end, the entity it passes
/// over and whether players are passed over too (<c>CTraceFilterNoNPCsOrPlayer</c>), answering the fraction reached.
/// Null traces nothing.
/// </param>
public readonly record struct ObserverInput(
    int Mode,
    double CurTime,
    float FrameTime,
    ObserverSubject Self,
    ObserverSubject? Target,
    (float Pitch, float Yaw, float Roll) ViewAngles,
    float DeathTime,
    double? FreezeSince,
    Func<Vector3, Vector3, int, bool, float>? Sweep);

/// <summary>The view an observer mode built.</summary>
/// <param name="Origin">The eye.</param>
/// <param name="Angles">Where it looks.</param>
/// <param name="FreezeFrameStarted">Whether this frame called <c>view->FreezeFrame( spec_freeze_time )</c>.</param>
/// <param name="FreezeFrameEnded">Whether this frame's data left the freeze cam, which calls <c>FreezeFrame( 0 )</c>.</param>
public readonly record struct ObserverView(
    Vector3 Origin,
    (float Pitch, float Yaw, float Roll) Angles,
    bool FreezeFrameStarted = false,
    bool FreezeFrameEnded = false);

/// <summary>
/// A point-of-view recorder's observer camera: <c>C_BasePlayer::CalcObserverView</c> (<c>baseplayer_shared.cpp:1689</c>)
/// and the views it dispatches to, with the state they keep on the player between frames (B745).
/// </summary>
/// <remarks>
/// **Ported, not approximated.** Each mode is its engine function line for line: <c>C_TFPlayer::CalcDeathCamView</c>
/// (<c>c_tf_player.cpp:7177</c>), <c>C_BasePlayer::CalcFreezeCamView</c> (<c>c_baseplayer.cpp:1652</c>),
/// <c>CalcChaseCamView</c> (<c>:1487</c>), <c>CalcInEyeCamView</c> (<c>:1732</c>) and <c>CalcRoamingView</c>
/// (<c>:1612</c>). The state is <c>m_flObserverChaseDistance</c>, the freeze-cam stamp of <c>PostDataUpdate</c>, and
/// <c>CViewRender::m_View</c>, the last frame's view, which <c>SetUpViews</c> hands to <c>CalcView</c> as its starting
/// <c>eyeOrigin</c> and <c>eyeAngles</c> (<c>view.cpp:628-1775</c>).
///
/// *Not built:* a train's or a boss's longer chase; <c>spec_track</c>; the punch angle in eye.
/// </remarks>
public sealed class ObserverCamera
{
    /// <summary><c>spec_freeze_traveltime</c>, TF2's 0.4 (<c>c_baseplayer.cpp:112-115</c>).</summary>
    public const float FreezeTravelTime = 0.4f;

    /// <summary><c>spec_freeze_time</c>, TF2's 4.0.</summary>
    public const float FreezeTime = 4f;

    /// <summary><c>spec_freeze_distance_min</c>.</summary>
    public const float FreezeDistanceMin = 96f;

    /// <summary><c>spec_freeze_distance_max</c>.</summary>
    public const float FreezeDistanceMax = 200f;

    /// <summary><c>CHASE_CAM_DISTANCE_MIN</c> (<c>c_baseplayer.h:61</c>).</summary>
    public const float ChaseDistanceMin = 16f;

    /// <summary><c>CHASE_CAM_DISTANCE_MAX</c>.</summary>
    public const float ChaseDistanceMax = 96f;

    /// <summary><c>TF_DEATH_ANIMATION_TIME</c> (<c>tf_shareddefs.h:1728</c>).</summary>
    public const float DeathAnimationTime = 2f;

    /// <summary><c>Vector(0,0,6)</c>: the eye over a decapitated head's mass centre (<c>c_tf_player.cpp:7208</c>).</summary>
    private const float HeadGibEyeHeight = 6f;

    /// <summary>How fast the chase distance grows, a second.</summary>
    private const float ChaseGrowth = 48f;

    /// <summary><c>VEC_VIEW</c> (<c>tf_gamerules.cpp:1306</c>).</summary>
    private const float ViewHeight = PlayerEye.GenericViewHeight;

    /// <summary><c>VEC_DUCK_VIEW</c>.</summary>
    private const float DuckViewHeight = PlayerEye.DuckViewHeight;

    /// <summary><c>VEC_HULL_MAX</c>'s height.</summary>
    private const float HullHeight = SpectatorView.PlayerHullHeight;

    /// <summary><c>VEC_DEAD_VIEWHEIGHT</c>.</summary>
    private const float DeadViewHeight = PlayerEye.DeadChaseTarget;

    /// <summary><c>FLT_EPSILON</c>, which <c>VectorNormalize</c> adds to the length.</summary>
    private const float FloatEpsilon = 1.192092896e-07f;

    private readonly UniformRandomStream _random;

    /// <summary><c>m_flObserverChaseDistance</c>.</summary>
    private float _chaseDistance;

    /// <summary>The freeze cam this stamp belongs to, by its <see cref="ObserverInput.FreezeSince"/>.</summary>
    private double? _freezeStamped;

    /// <summary><c>m_vecFreezeFrameStart</c>.</summary>
    private Vector3 _freezeStart;

    /// <summary><c>m_flFreezeFrameStartTime</c>.</summary>
    private double _freezeStartTime;

    /// <summary><c>m_flFreezeFrameDistance</c>.</summary>
    private float _freezeDistance;

    /// <summary><c>m_flFreezeZOffset</c>.</summary>
    private float _freezeZOffset;

    /// <summary><c>m_bSentFreezeFrame</c>.</summary>
    private bool _sentFreezeFrame;

    /// <summary>A camera over a random stream of its own.</summary>
    /// <param name="random">
    /// Where <c>RandomFloat</c> draws come from. *Interpolated:* the engine draws from the client's global stream, whose
    /// state every other caller moves, so which draw a freeze cam gets cannot be recovered; the distribution is Valve's.
    /// </param>
    public ObserverCamera(UniformRandomStream random)
    {
        ArgumentNullException.ThrowIfNull(random);

        _random = random;
    }

    /// <summary><c>CViewRender::m_View</c>'s origin: the last frame's view, which the next starts from.</summary>
    public Vector3 LastOrigin { get; private set; }

    /// <summary><c>m_View</c>'s angles.</summary>
    public (float Pitch, float Yaw, float Roll) LastAngles { get; private set; }

    /// <summary>Records a frame's final view, as <c>SetUpViews</c> leaves it in <c>m_View</c>, whichever camera made it.</summary>
    /// <param name="origin">The eye.</param>
    /// <param name="angles">Where it looked.</param>
    public void Remember(Vector3 origin, (float Pitch, float Yaw, float Roll) angles)
    {
        LastOrigin = origin;
        LastAngles = angles;
    }

    /// <summary>One frame of <c>CalcObserverView</c>, after <c>PostDataUpdate</c>'s freeze-cam bookkeeping.</summary>
    /// <param name="input">The frame.</param>
    /// <returns>The view, which is also remembered as <see cref="LastOrigin"/>.</returns>
    public ObserverView Calc(in ObserverInput input)
    {
        bool ended = Stamp(input);

        ObserverView view = input.Mode switch
        {
            DeathCam => DeathCamView(input),
            Roaming or Fixed => RoamingView(input),
            InEye => InEyeView(input),
            PointOfInterest or Chase => ChaseView(input),
            FreezeCam => FreezeCamView(input),

            // No `default:` in the engine: the view is left as it came in.
            _ => new ObserverView(LastOrigin, LastAngles),
        };

        Remember(view.Origin, view.Angles);

        return view with { FreezeFrameEnded = ended };
    }

    /// <summary>
    /// <c>PostDataUpdate</c>'s exit edge for a recorder who is no longer observing at all: a freeze cam left for play.
    /// </summary>
    /// <returns>Whether a freeze cam was left, which calls <c>view->FreezeFrame( 0 )</c> (<c>c_baseplayer.cpp:920-935</c>).</returns>
    public bool Unobserve()
    {
        bool wasFreezing = _freezeStamped is not null;
        _freezeStamped = null;

        return wasFreezing;
    }

    /// <summary><c>PostDataUpdate</c>'s two edges (<c>c_baseplayer.cpp:885-935</c>).</summary>
    /// <returns>Whether the freeze cam was left, which calls <c>view->FreezeFrame( 0 )</c>.</returns>
    private bool Stamp(in ObserverInput input)
    {
        // Which freeze cam a stamp belongs to is a question of identity, so the bits are compared, not a range.
        if (input.Mode == FreezeCam && input.FreezeSince is { } since &&
            (_freezeStamped is not { } stamped || BitConverter.DoubleToInt64Bits(stamped) != BitConverter.DoubleToInt64Bits(since)))
        {
            _freezeStamped = since;
            _freezeStart = LastOrigin;
            _freezeStartTime = since;
            _freezeDistance = _random.RandomFloat(FreezeDistanceMin, FreezeDistanceMax);
            _freezeZOffset = _random.RandomFloat(-30f, 20f);
            _sentFreezeFrame = false;

            return false;
        }

        if (input.Mode != FreezeCam && _freezeStamped is not null)
        {
            _freezeStamped = null;

            return true;
        }

        return false;
    }

    /// <summary><c>C_TFPlayer::CalcDeathCamView</c> (<c>c_tf_player.cpp:7177</c>).</summary>
    private ObserverView DeathCamView(in ObserverInput input)
    {
        ObserverSubject self = input.Self;
        ObserverSubject? killer = input.Target;

        // Swing to face our killer within half the death anim time.
        float interpolation = (float)((input.CurTime - input.DeathTime) / (DeathAnimationTime * 0.5));
        interpolation = SimpleSpline(Math.Clamp(interpolation, 0f, 1f));

        float minimum = ChaseDistanceMin;
        float maximum = ChaseDistanceMax;

        if (killer is { } animating && animating.Kind != ObserverSubjectKind.Other)
        {
            float scaleSquared = animating.ModelScale * animating.ModelScale;
            minimum *= scaleSquared;
            maximum *= scaleSquared;
        }

        _chaseDistance = Math.Clamp(_chaseDistance + (input.FrameTime * ChaseGrowth), minimum, maximum);

        (float Pitch, float Yaw, float Roll) forward = self.EyeAngles;
        (float Pitch, float Yaw, float Roll) angles = forward;
        Vector3 origin = DeathViewPosition(self);

        if (self.Ragdoll is { HeadGib: { } head } body)
        {
            // View from our decapitated head, turning to face our ragdoll. The head is hidden where it is emitted. With the
            // ragdoll entity gone the engine takes the head's own origin as the direction, not a difference (`:7218-7219`).
            Vector3 headEye = head.MassCentre + new Vector3(0f, 0f, HeadGibEyeHeight);
            Vector3 toBody = body.Body is { } ragdoll ? ragdoll - headEye : head.Origin;
            (float Pitch, float Yaw, float Roll) atBody = AngleVectors.Angles(toBody.X, toBody.Y, toBody.Z);

            return new ObserverView(headEye, ValveQuaternion.InterpolateAngles(head.Angles, atBody, interpolation));
        }

        if (killer is { } other && other.EntityIndex != self.EntityIndex)
        {
            Vector3 toKiller = EyePosition(other, input) - origin;
            (float Pitch, float Yaw, float Roll) atKiller = AngleVectors.Angles(toKiller.X, toKiller.Y, toKiller.Z);
            angles = ValveQuaternion.InterpolateAngles(forward, atKiller, interpolation);
        }

        Vector3 direction = Normalized(Forward(angles));
        Vector3 eye = origin - (_chaseDistance * direction);

        float reached = Trace(input, origin, eye, self.EntityIndex, passPlayers: false);

        if (reached < 1f)
        {
            eye = origin + ((eye - origin) * reached);
            _chaseDistance = (origin - eye).Length();
        }

        return new ObserverView(eye, angles);
    }

    /// <summary><c>C_BasePlayer::CalcFreezeCamView</c> (<c>c_baseplayer.cpp:1652</c>).</summary>
    private ObserverView FreezeCamView(in ObserverInput input)
    {
        if (input.Target is not { } target)
        {
            return DeathCamView(input);
        }

        // Zoom towards our target.
        float elapsed = (float)(input.CurTime - _freezeStartTime);
        float blend = SimpleSpline(Math.Clamp(elapsed / FreezeTravelTime, 0f, 1f));

        Vector3 desired = ObserverCamOrigin(target) + ChaseCamViewOffset(target);
        Vector3 aimAt = desired;

        if (target.IsAlive)
        {
            // Look at their chest, not their head.
            aimAt.Z -= HullHeight * Scale(target) * 0.5f;
        }
        else
        {
            // Look over ragdoll, not through.
            aimAt.Z += DeadViewHeight * Scale(target);
        }

        // Figure out a view position in front of the target.
        Vector3 eyeOnPlane = LastOrigin with { Z = aimAt.Z };
        Vector3 toTarget = Normalized(aimAt - eyeOnPlane);

        // Stop a few units away from the target, and shift up to be at the same height.
        Vector3 standAt = aimAt - (toTarget * _freezeDistance);
        standAt.Z = EyePosition(target, input).Z + _freezeZOffset;

        // Now trace out from the target, so that we're put in front of any walls.
        float reached = Trace(input, aimAt, standAt, target.EntityIndex, passPlayers: false);

        if (reached < 1f)
        {
            // Aim close freezecams at the target's eyes, and move the view up out of their chin.
            standAt = aimAt + ((standAt - aimAt) * reached);
            aimAt = desired;
            standAt.Z += MathF.Abs(aimAt.Z - standAt.Z) * 0.85f;
            standAt = aimAt + ((standAt - aimAt) * Trace(input, aimAt, standAt, target.EntityIndex, passPlayers: false));
        }

        // Look directly at the target.
        Vector3 look = Normalized(aimAt - standAt);
        (float Pitch, float Yaw, float Roll) angles = AngleVectors.Angles(look.X, look.Y, look.Z);

        Vector3 eye = Vector3.Lerp(_freezeStart, standAt, blend);
        bool started = false;

        if (elapsed >= FreezeTravelTime && !_sentFreezeFrame)
        {
            _sentFreezeFrame = true;
            started = true;
        }

        return new ObserverView(eye, angles, started);
    }

    /// <summary><c>C_BasePlayer::CalcChaseCamView</c> (<c>c_baseplayer.cpp:1487</c>).</summary>
    private ObserverView ChaseView(in ObserverInput input)
    {
        if (input.Target is not { } target)
        {
            // Just copy a safe in-map position.
            return new ObserverView(EyePosition(input.Self, input), input.Self.EyeAngles);
        }

        Vector3 origin = ObserverCamOrigin(target) + ChaseCamViewOffset(target);

        (float Pitch, float Yaw, float Roll) angles = input.Mode == InEye ? LastAngles : input.ViewAngles;

        _chaseDistance += input.FrameTime * ChaseGrowth;

        float minimum = ChaseDistanceMin;
        float maximum = ChaseDistanceMax;

        if (target.Kind != ObserverSubjectKind.Other)
        {
            float scaleSquared = target.ModelScale * target.ModelScale;
            minimum *= scaleSquared;
            maximum *= scaleSquared;
            _chaseDistance = maximum;
        }

        _chaseDistance = Math.Clamp(_chaseDistance, minimum, maximum);

        Vector3 point = origin - (_chaseDistance * Normalized(Forward(angles)));

        float reached = Trace(input, origin, point, target.EntityIndex, passPlayers: true);

        if (reached < 1f)
        {
            point = origin + ((point - origin) * reached);

            // Valve measures from the eyeOrigin it was handed, last frame's view, not the point it just clipped.
            _chaseDistance = (origin - LastOrigin).Length();
        }

        return new ObserverView(point, angles);
    }

    /// <summary><c>C_BasePlayer::CalcInEyeCamView</c> (<c>c_baseplayer.cpp:1732</c>), outside HLTV.</summary>
    private ObserverView InEyeView(in ObserverInput input)
    {
        if (input.Target is not { } target)
        {
            return new ObserverView(EyePosition(input.Self, input), input.Self.EyeAngles);
        }

        if (!target.IsAlive)
        {
            // If dead, show from 3rd person.
            return ChaseView(input);
        }

        _chaseDistance = 0f;

        // `eyeOrigin += GetViewOffset()`: the recorder's own offset on the target's origin, Valve's "hack hack".
        return new ObserverView(target.Origin + new Vector3(0f, 0f, input.Self.ViewOffsetZ), target.EyeAngles);
    }

    /// <summary><c>C_BasePlayer::CalcRoamingView</c> (<c>c_baseplayer.cpp:1612</c>); a playing demo smooths nothing.</summary>
    private ObserverView RoamingView(in ObserverInput input)
    {
        ObserverSubject target = input.Target ?? input.Self;

        _chaseDistance = 0f;

        return new ObserverView(EyePosition(target, input), target.EyeAngles);
    }

    /// <summary><c>EyePosition()</c>: <c>GetAbsOrigin() + GetViewOffset()</c>, and the local player in chase is the view.</summary>
    private Vector3 EyePosition(in ObserverSubject subject, in ObserverInput input) =>
        subject.EntityIndex == input.Self.EntityIndex && input.Mode is Chase or PointOfInterest
            ? LastOrigin
            : subject.Origin + new Vector3(0f, 0f, subject.ViewOffsetZ);

    /// <summary><c>C_TFPlayer::GetDeathViewPosition</c> (<c>c_tf_player.cpp:7149</c>).</summary>
    private static Vector3 DeathViewPosition(in ObserverSubject subject)
    {
        Vector3 origin = subject.Origin + new Vector3(0f, 0f, subject.ViewOffsetZ);

        if (subject.Ragdoll is { } ragdoll)
        {
            if (ragdoll.DeathAnimation)
            {
                origin.Z += DeadViewHeight * subject.ModelScale * 4f;
            }
            else if (ragdoll.Root is { } root)
            {
                // Look over ragdoll, not through.
                origin = root with { Z = root.Z + (DeadViewHeight * subject.ModelScale) };
            }
        }

        return origin;
    }

    /// <summary>
    /// <c>C_TFPlayer::GetObserverCamOrigin()</c> (<c>c_tf_player.cpp:6817-6838</c>): a dead TF player's first gib, else his
    /// death view; anyone alive, the render origin.
    /// </summary>
    private static Vector3 ObserverCamOrigin(in ObserverSubject subject)
    {
        if (subject.Kind == ObserverSubjectKind.Player && !subject.IsAlive)
        {
            return subject.Ragdoll?.FirstGib ?? DeathViewPosition(subject);
        }

        return subject.Origin;
    }

    /// <summary><c>C_TFPlayer::GetChaseCamViewOffset</c> and the base it falls to (<c>c_baseplayer.cpp:1461</c>).</summary>
    private static Vector3 ChaseCamViewOffset(in ObserverSubject target) => target.Kind switch
    {
        ObserverSubjectKind.Building => new Vector3(0f, 0f, 64f),
        ObserverSubjectKind.MiniBuilding => new Vector3(0f, 0f, 40f),
        ObserverSubjectKind.Player when !target.IsAlive => new Vector3(0f, 0f, DeadViewHeight * target.ModelScale),
        ObserverSubjectKind.Player when target.Ducking => new Vector3(0f, 0f, DuckViewHeight * target.ModelScale),
        ObserverSubjectKind.Player => new Vector3(0f, 0f, ViewHeight * target.ModelScale),
        _ => new Vector3(0f, 0f, DeadViewHeight),
    };

    /// <summary>The scale the <c>_SCALED</c> view vectors use, or one for something that is not animating.</summary>
    private static float Scale(in ObserverSubject subject) =>
        subject.Kind == ObserverSubjectKind.Other ? 1f : subject.ModelScale;

    private static float Trace(in ObserverInput input, Vector3 start, Vector3 end, int ignore, bool passPlayers) =>
        input.Sweep is { } sweep ? Math.Clamp(sweep(start, end, ignore, passPlayers), 0f, 1f) : 1f;

    private static Vector3 Forward((float Pitch, float Yaw, float Roll) angles)
    {
        (float x, float y, float z) = AngleVectors.Forward(angles.Pitch, angles.Yaw);

        return new Vector3(x, y, z);
    }

    /// <summary><c>VectorNormalize</c> (<c>mathlib_base.cpp:66</c>): divides by the length plus <c>FLT_EPSILON</c>.</summary>
    private static Vector3 Normalized(Vector3 vector) => vector * (1f / (vector.Length() + FloatEpsilon));

    private static float SimpleSpline(float value) => TfGameMovement.SimpleSpline(value);
}
