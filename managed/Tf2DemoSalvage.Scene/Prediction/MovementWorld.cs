using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;

using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Prediction;

/// <summary>A box the recorder's movement meets: a player or a building, world-aligned.</summary>
/// <param name="Min">Its low corner in the world.</param>
/// <param name="Max">Its high corner.</param>
/// <param name="Entity">Its entity index, which the trace reports.</param>
/// <param name="Contents">The team contents a mask must carry for it to block — a player's; 0 for one that always does.</param>
public readonly record struct MovementBox(Vector3 Min, Vector3 Max, int Entity, int Contents);

/// <summary>
/// Everything <c>CTFGameMovement::TracePlayerBBox</c> meets at one packet's tick (D205, B450): the map, the brush entities
/// <c>CTraceFilterObject</c> passes, building boxes, and enemy players.
/// </summary>
/// <remarks>
/// The filter is <c>CTraceFilterObject</c> over <c>CTraceFilterSimple</c> with <c>COLLISION_GROUP_PLAYER_MOVEMENT</c>
/// (<c>tf_gamemovement.cpp:2223-2328</c>, <c>util_shared.cpp:288-314</c>). <c>MASK_PLAYERSOLID</c> carries
/// <c>CONTENTS_MONSTER</c>, <c>CONTENTS_WINDOW</c> and <c>CONTENTS_MOVEABLE</c> (<c>bspflags.h:108</c>), so
/// <c>StandardFilterRules</c> (<c>util_shared.cpp:241</c>) passes every entity; the entity's own <c>ShouldCollide</c> and
/// <see cref="PlayerMovementCollidesWith"/> decide.
///
/// A brush entity with angles is carried with them; <see cref="MapLevel.TraceHull"/> turns the ray into its frame as
/// <c>CM_TransformedBoxTrace</c> does (B450).
/// </remarks>
public sealed class MovementWorld
{
    /// <summary><c>MASK_PLAYERSOLID</c> (<c>bspflags.h:108</c>).</summary>
    public const int MaskPlayerSolid = BspLeafTree.MaskPlayerSolid;

    /// <summary><c>CONTENTS_REDTEAM</c>, <c>CONTENTS_TEAM1</c> (<c>tf_shareddefs.h:61</c>, <c>bspflags.h:46</c>).</summary>
    public const int ContentsRedTeam = 0x800;

    /// <summary><c>CONTENTS_BLUETEAM</c>, <c>CONTENTS_TEAM2</c> (<c>tf_shareddefs.h:62</c>, <c>bspflags.h:47</c>).</summary>
    public const int ContentsBlueTeam = 0x1000;

    private const int TeamRed = 2;
    private const int TeamBlue = 3;
    private const int SolidBsp = 1;
    private const int SolidBbox = 2;
    private const int FsolidNotSolid = 4;
    private const int GroupDebris = 1;
    private const int GroupPushaway = 17;
    private const int StateTeamWin = 5;
    private const float DistEpsilon = 0.03125f;

    private readonly MapLevel _level;
    private readonly List<(SolidBrush Brush, string ClassName, SceneCollision Collision, int? Team)> _brushes;
    private readonly int? _roundState;
    private readonly List<MovementBox> _boxes;
    private readonly Dictionary<int, List<SolidBrush>> _brushesByMask = [];

    private MovementWorld(
        MapLevel level,
        List<(SolidBrush, string, SceneCollision, int?)> brushes,
        int? roundState,
        List<MovementBox> boxes)
    {
        _level = level;
        _brushes = brushes;
        _roundState = roundState;
        _boxes = boxes;
    }

    /// <summary>The world as it stood at a packet's tick.</summary>
    /// <param name="timeline">The demo.</param>
    /// <param name="brushTracks">Its brush entity tracks with their models' head nodes.</param>
    /// <param name="level">The map.</param>
    /// <param name="tick">The packet's tick.</param>
    /// <param name="recorder">The recorder, as networked then.</param>
    /// <returns>The world.</returns>
    public static MovementWorld At(
        DemoTimeline timeline,
        IReadOnlyList<(ScenePropTrack Track, int HeadNode)> brushTracks,
        MapLevel level,
        int tick,
        ScenePlayer recorder)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        ArgumentNullException.ThrowIfNull(brushTracks);

        List<(SolidBrush, string, SceneCollision, int?)> brushes = [];

        foreach ((ScenePropTrack track, int headNode) in brushTracks)
        {
            if (track.Alive(tick) && track.AtKeyframe(tick) is { } pose && track.CollisionAt(tick) is { } collision)
            {
                brushes.Add((
                    new SolidBrush(
                        headNode,
                        new Vector3(pose.X, pose.Y, pose.Z),
                        track.EntityIndex,
                        new Vector3(pose.Pitch, pose.Yaw, pose.Roll)),
                    track.ClassName,
                    collision,
                    track.TeamNumber));
            }
        }

        return new MovementWorld(
            level, brushes, timeline.RoundStateAt(tick), Boxes(timeline.PlayersAt(tick), timeline.BuildingsAt(tick), recorder));
    }

    /// <summary>The brush entity tracks a movement trace can meet: every one whose model is a <c>*N</c> submodel.</summary>
    /// <param name="props">Every prop track.</param>
    /// <param name="models">The map's models.</param>
    /// <returns>Each with its model's head node.</returns>
    public static IReadOnlyList<(ScenePropTrack Track, int HeadNode)> BrushTracks(IReadOnlyList<ScenePropTrack> props, IReadOnlyList<BspModel> models)
    {
        ArgumentNullException.ThrowIfNull(props);
        ArgumentNullException.ThrowIfNull(models);

        List<(ScenePropTrack, int)> found = [];

        foreach (ScenePropTrack track in props)
        {
            if (track.ModelPath.Length > 1 && track.ModelPath[0] == '*' &&
                int.TryParse(track.ModelPath.AsSpan(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int model) &&
                model > 0 && model < models.Count)
            {
                found.Add((track, models[model].HeadNode));
            }
        }

        return found;
    }

    /// <summary><c>UTIL_TraceRay</c> of a player box through this world, with <c>CTraceFilterObject</c>.</summary>
    /// <param name="start">The box's origin at the start.</param>
    /// <param name="end">Its origin at the end.</param>
    /// <param name="mins">Its low corner relative to the origin.</param>
    /// <param name="maxs">Its high corner.</param>
    /// <param name="mask"><c>PlayerSolidMask()</c>.</param>
    /// <returns>The trace; null with no world.</returns>
    public BspTrace? Trace(Vector3 start, Vector3 end, Vector3 mins, Vector3 maxs, int mask)
    {
        if (_level.TraceHull(
                (start.X, start.Y, start.Z), (end.X, end.Y, end.Z), (mins.X, mins.Y, mins.Z), (maxs.X, maxs.Y, maxs.Z), mask,
                BrushesFor(mask))
            is not { } nearest)
        {
            return null;
        }

        foreach (MovementBox box in _boxes)
        {
            if (box.Contents != 0 && (mask & box.Contents) == 0)
            {
                continue;
            }

            if (Sweep(start, end, mins, maxs, box) is { } hit)
            {
                nearest = Nearer(nearest, hit);
            }
        }

        return nearest;
    }

    /// <summary>
    /// The engine's merge of one entity's trace into the running one: a nearer hit, or one that starts solid, becomes the
    /// trace and names its entity. *Interpolated:* <c>CEngineTrace::TraceRay</c>'s loop is engine code the SDK omits.
    /// </summary>
    internal static BspTrace Nearer(BspTrace nearest, BspTrace hit)
    {
        if (hit.Fraction < nearest.Fraction || (hit.StartSolid && !nearest.StartSolid))
        {
            return hit with
            {
                Fraction = MathF.Min(hit.Fraction, nearest.Fraction),
                AllSolid = hit.AllSolid || nearest.AllSolid,
            };
        }

        return nearest with { AllSolid = nearest.AllSolid || hit.AllSolid, StartSolid = nearest.StartSolid || hit.StartSolid };
    }

    private List<SolidBrush> BrushesFor(int mask)
    {
        if (_brushesByMask.TryGetValue(mask, out List<SolidBrush>? cached))
        {
            return cached;
        }

        List<SolidBrush> passing = [];

        foreach ((SolidBrush brush, string className, SceneCollision collision, int? team) in _brushes)
        {
            if (ShouldCollide(className, collision, team, _roundState, mask))
            {
                passing.Add(brush);
            }
        }

        _brushesByMask[mask] = passing;
        return passing;
    }

    /// <summary>Whether a brush entity stops player movement under a mask.</summary>
    /// <param name="className">Its server class.</param>
    /// <param name="collision">Its networked solidity.</param>
    /// <param name="team">Its <c>m_iTeamNum</c>.</param>
    /// <param name="roundState"><c>m_iRoundState</c>; null when the demo does not say.</param>
    /// <param name="mask">The trace's mask.</param>
    /// <returns>True when the trace meets it.</returns>
    internal static bool ShouldCollide(string className, SceneCollision collision, int? team, int? roundState, int mask)
    {
        // A non-solid entity leaves PARTITION_CLIENT_SOLID_EDICTS, and only SOLID_BSP walks the brush model.
        if (collision.SolidType != SolidBsp || (collision.SolidFlags & FsolidNotSolid) != 0)
        {
            return false;
        }

        // C_FuncRespawnRoomVisualizer::ShouldCollide (c_func_respawnroom.cpp:69-97) and C_FuncForceField's
        // (c_func_forcefield.cpp:45-73): off on a team win, never for TEAM_UNASSIGNED, and for player movement only
        // against a mask carrying the wall's own team's contents — an enemy's.
        if (className is "CFuncRespawnRoomVisualizer" or "CFuncForceField")
        {
            int wall = team switch
            {
                TeamBlue => ContentsBlueTeam,
                TeamRed => ContentsRedTeam,
                _ => 0,
            };

            if (roundState == StateTeamWin || team is null or 0 || (wall != 0 && (mask & wall) == 0))
            {
                return false;
            }
        }
        // CBaseEntity::ShouldCollide (baseentity_shared.cpp:619-627) refuses DEBRIS to a mask without CONTENTS_DEBRIS,
        // which MASK_PLAYERSOLID is; the game rules below refuse the same group, so one check stands for both.
        return PlayerMovementCollidesWith(collision.CollisionGroup);
    }

    /// <summary>
    /// <c>CTFGameRules::ShouldCollide( COLLISION_GROUP_PLAYER_MOVEMENT, group )</c> (<c>tf_gamerules.cpp:18000-18116</c>)
    /// over <c>CGameRules::ShouldCollide</c> (<c>gamerules.cpp:676-782</c>), each rule read with the pair sorted low first.
    /// </summary>
    internal static bool PlayerMovementCollidesWith(int group) => group switch
    {
        GroupDebris or 2 => false, // DEBRIS, DEBRIS_TRIGGER sort first and collide with nothing (gamerules.cpp:713)
        3 => false, // INTERACTIVE_DEBRIS (:734)
        10 => false, // IN_VEHICLE (:704)
        11 => false, // WEAPON (tf_gamerules.cpp:18009)
        12 => false, // VEHICLE_CLIP, which only a VEHICLE meets (gamerules.cpp:771)
        13 => false, // PROJECTILE (tf_gamerules.cpp:18016)
        14 => false, // DOOR_BLOCKER, which only an NPC meets (gamerules.cpp:707)
        16 => false, // DISSOLVING, which only NONE meets (:720)
        GroupPushaway => false, // (:685)
        20 => false, // TF_COLLISIONGROUP_GRENADES (tf_gamerules.cpp:18057)
        23 => false, // TFCOLLISION_GROUP_COMBATOBJECT (:18097)
        24 or 27 => false, // TFCOLLISION_GROUP_ROCKETS, ROCKET_BUT_NOT_WITH_OTHER_ROCKETS (:18028)
        26 => false, // TFCOLLISION_GROUP_TANK (:18109)
        _ => true, // PASSABLE_DOOR refuses COLLISION_GROUP_PLAYER alone (:710); RESPAWNROOMS takes movement (:18062)
    };

    /// <summary>The boxes at a tick: every enemy player, and every solid building of either team.</summary>
    /// <param name="players">The players then.</param>
    /// <param name="buildings">The buildings then.</param>
    /// <param name="recorder">The recorder.</param>
    /// <returns>The boxes.</returns>
    /// <remarks>
    /// A player blocks another's movement only through <c>C_TFPlayer::ShouldCollide</c>'s team contents, which
    /// <c>PlayerSolidMask</c> adds for the enemy (<c>tf_gamemovement.cpp:269-283</c>). A building is <c>SOLID_BBOX</c>
    /// with its <c>UTIL_SetSize</c> bounds, world-aligned, and is met whatever its team: <c>CTraceFilterObject</c> hits the
    /// recorder's own (<c>:2234-2255</c>) and <c>CTraceFilterSimple</c> everyone else's, since no rule refuses
    /// <c>TFCOLLISION_GROUP_OBJECT</c> or <c>_SOLIDTOPLAYERMOVEMENT</c> (<c>tf_obj.cpp:3368-3379</c>) to player movement.
    /// </remarks>
    internal static List<MovementBox> Boxes(IReadOnlyList<ScenePlayer> players, IReadOnlyList<SceneBuilding> buildings, ScenePlayer recorder)
    {
        List<MovementBox> boxes = [];

        foreach (ScenePlayer other in players)
        {
            if (other.EntityIndex == recorder.EntityIndex || !other.IsAlive || other.Team is not (TeamRed or TeamBlue) ||
                other.Team == recorder.Team)
            {
                continue;
            }

            // VEC_HULL and VEC_DUCK_HULL (tf_gamerules.cpp:1313-1317).
            float top = ((other.Flags ?? 0) & 2) != 0 ? 62f : 82f;
            Vector3 origin = new(other.X, other.Y, other.Z);

            boxes.Add(new MovementBox(
                origin + new Vector3(-24f, -24f, 0f),
                origin + new Vector3(24f, 24f, top),
                other.EntityIndex,
                other.Team == TeamBlue ? ContentsBlueTeam : ContentsRedTeam));
        }

        foreach (SceneBuilding building in buildings)
        {
            if (building.SolidType != SolidBbox || (building.SolidFlags & FsolidNotSolid) != 0 ||
                building is not { Position: { } at, Mins: { } mins, Maxs: { } maxs })
            {
                continue;
            }

            Vector3 origin = new(at.X, at.Y, at.Z);

            boxes.Add(new MovementBox(
                origin + new Vector3(mins.X, mins.Y, mins.Z), origin + new Vector3(maxs.X, maxs.Y, maxs.Z), building.EntityIndex, 0));
        }

        return boxes;
    }

    /// <summary>
    /// A player box swept against a world-aligned box, as <c>CM_ClipBoxToBrush</c> clips one brush: the box grown by the
    /// player's, the slab test stopping <c>DIST_EPSILON</c> short; from inside, startsolid, and allsolid as well when it
    /// never gets out.
    /// </summary>
    internal static BspTrace? Sweep(Vector3 start, Vector3 end, Vector3 mins, Vector3 maxs, MovementBox box)
    {
        Vector3 min = box.Min - maxs;
        Vector3 max = box.Max - mins;
        Vector3 delta = end - start;
        float enter = -1f;
        float leave = 1f;
        Vector3 normal = Vector3.Zero;
        bool startsOut = false;
        bool getsOut = false;

        for (int axis = 0; axis < 3; axis++)
        {
            // The two planes of this slab: distance outside each at the start and the end.
            foreach ((float startDistance, float sign) in new[] { (start[axis] - max[axis], 1f), (min[axis] - start[axis], -1f) })
            {
                float endDistance = startDistance + (sign * delta[axis]);

                startsOut |= startDistance > 0f;
                getsOut |= endDistance > 0f;

                if (startDistance > 0f && endDistance >= 0f)
                {
                    return null;
                }

                if (startDistance <= 0f && endDistance <= 0f)
                {
                    continue;
                }

                if (startDistance > endDistance)
                {
                    float fraction = (startDistance - DistEpsilon) / (startDistance - endDistance);

                    if (fraction > enter)
                    {
                        enter = fraction;
                        normal = Vector3.Zero;
                        normal[axis] = sign;
                    }
                }
                else
                {
                    leave = MathF.Min(leave, (startDistance + DistEpsilon) / (startDistance - endDistance));
                }
            }
        }

        if (!startsOut)
        {
            return getsOut
                ? new BspTrace(1f, -1, default, AllSolid: false, BrushEntity: box.Entity, StartSolid: true)
                : new BspTrace(0f, -1, default, AllSolid: true, BrushEntity: box.Entity, StartSolid: true);
        }

        return enter < leave && enter > -1f
            ? new BspTrace(MathF.Max(0f, enter), -1, (normal.X, normal.Y, normal.Z), AllSolid: false, BrushEntity: box.Entity)
            : null;
    }
}
