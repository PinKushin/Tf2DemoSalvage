using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using Microsoft.Extensions.Logging;
using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene;

public sealed partial class EntityModelSet
{
    /// <summary>Per model with flexes, the face it wears in no scene — null when that moves nothing (B513).</summary>
    private readonly Dictionary<string, float[]?> _restingFace = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>How many instances were given a moving face, since the set was made (B513).</summary>
    /// <remarks>Counted where the stream is attached to the instance, so it reports what the draw USES (B243).</remarks>
    public int FlexedInstances { get; private set; }

    /// <summary>The face a player wears now: the scene's expressions through the rules, in buffer order (B513).</summary>
    /// <remarks>
    /// **The scene is the VCD gesture's**, the one this viewer tracks per player; it ends when the scene stops
    /// playing back, because <c>C_SceneEntity::StopPlayback</c> clears the actor's scene events. A scene running
    /// alongside another on the same actor is not tracked (B513 remainder).
    /// </remarks>
    /// <returns>The face, or null, and the slots of it a flex can move (<see cref="ModelInstance.FlexRange"/>).</returns>
    private (float[]? Face, FlexSlots? Range) FlexFor(SceneProp prop, PropModels.ModelFrames? model, double seconds)
    {
        FlexTally tally = _flexTally;
        tally.Calls++;
        _faceGlobal = null;

        if (model is null || !model.Flex.HasVertexAnimation || model.CornerVertex.Count == 0 ||
            !_packedOrder.TryGetValue(prop.ModelPath, out int[]? order))
        {
            return (null, null);
        }

        tally.Faces++;
        float[]? face;
        long drivingAt = Stopwatch.GetTimestamp();
        IReadOnlyDictionary<string, float>? global = FaceDriving(prop, seconds);
        _faceGlobal = global;
        long drivenAt = Stopwatch.GetTimestamp();
        tally.Driving += drivenAt - drivingAt;

        if (global is not null)
        {
            float[] local = FaceFlex.Local(model.Flex, global);
            long localAt = Stopwatch.GetTimestamp();
            tally.Local += localAt - drivenAt;
            float[] weights = FaceFlex.Rules(model.Flex, local);
            long rulesAt = Stopwatch.GetTimestamp();
            tally.Rules += rulesAt - localAt;
            // A corpse runs its player's whole SetupWeights, so the delay is the player's own (`m_flFlexDelayedWeight`).
            float[] delayed = Delayed(prop.FaceOf ?? prop.EntityIndex, weights, seconds);
            long delayedAt = Stopwatch.GetTimestamp();
            tally.Delayed += delayedAt - rulesAt;

            // The same weights draw the same face: an idle loop's expression holds still for seconds at a time.
            // **Per pose half** (B581 rung 3): the stream is rewritten in place, and the frame in flight draws the other
            // half's, so this half's last face is two frames old — a still face still converges in two.
            Dictionary<int, (float[] Weights, float[] Delayed, float[]? Face, float[]? Stream, FlexedCorners? Corners)> lastFaces = _lastFace[PoseHalf];

            bool same = lastFaces.TryGetValue(prop.EntityIndex, out (float[] Weights, float[] Delayed, float[]? Face, float[]? Stream, FlexedCorners? Corners) last) &&
                last.Weights.AsSpan().SequenceEqual(weights) && last.Delayed.AsSpan().SequenceEqual(delayed);
            long probedAt = Stopwatch.GetTimestamp();
            tally.Probe += probedAt - delayedAt;

            if (same)
            {
                face = last.Face;
                tally.Hits++;
            }
            else
            {
                tally.Misses++;

                // Kept apart from the face, which is null while nothing moves: a face that stops and starts again would
                // otherwise lose its stream at every stop and allocate a new one at every start.
                //
                // **Only the slots a flex can move are written** (finding 92): the stream starts at zero and nothing else
                // writes it, so the rest stays zero — which holds only for the model whose slots wrote it, so a stream
                // left by another model (a class change) is not reused.
                FlexedCorners corners = FlexedCornersOf(prop.ModelPath, model, order);
                long cornersAt = Stopwatch.GetTimestamp();
                tally.Corners += cornersAt - probedAt;

                // **Written into arrays this set keeps, not new ones per frame** (finding 80): a talking face changes every
                // frame, and three .vvd-sized arrays a face a frame were 2 GB of large-object garbage in 20 s of f12. The
                // deltas are scratch; the stream is the entity's own, rewritten in place (the renderer uploads its flexed range per draw).
                // Grown only: faces of different classes alternate, and an exact size would reallocate at every switch.
                if (_flexScratch.Positions.Length < model.VertexCount * 3)
                {
                    _flexScratch = (new float[model.VertexCount * 3], new float[model.VertexCount * 3]);
                }

                float[] stream = last.Stream is { } kept && ReferenceEquals(last.Corners, corners) ? kept : new float[order.Length * 6];

                // `FaceFlex.Deltas` taken apart so its two halves are timed apart: the clear, then the accumulation. Only
                // the offsets the slot write reads are cleared: nothing else is read for this model
                // (`FaceFlex.ClearMoved`, docs/measurements/2026-10-30-flex-for-cost.md).
                (float[] positions, float[] normals) = _flexScratch;
                FaceFlex.ClearMoved(corners, positions, normals);
                long clearedAt = Stopwatch.GetTimestamp();
                tally.Clear += clearedAt - cornersAt;

                int applied = StudioFlexRules.Accumulate(model.Flex, weights, delayed, positions, normals);
                long accumulatedAt = Stopwatch.GetTimestamp();
                tally.Deltas += accumulatedAt - clearedAt;
                tally.Vertices += applied;

                face = applied > 0 ? FaceFlex.IntoFlexedSlots((positions, normals), corners, stream) : null;
                long slottedAt = Stopwatch.GetTimestamp();
                tally.Slots += slottedAt - accumulatedAt;

                lastFaces[prop.EntityIndex] = (weights, (float[])delayed.Clone(), face, stream, corners);
                tally.Delayed += Stopwatch.GetTimestamp() - slottedAt;
            }
        }
        else
        {
            face = RestingFaceOf(prop, model, order);
        }

        if (face is null)
        {
            return (null, null);
        }

        FlexedInstances++;

        // **The first to the last slot a flex of this model names** (B597): every slot outside is zero in every face of
        // it — a driven face writes only the flexed slots, and a resting face's deltas exist only at vertices a flex moves.
        long rangeAt = Stopwatch.GetTimestamp();
        ImmutableArray<int> at = FlexedCornersOf(prop.ModelPath, model, order).At;
        tally.Corners += Stopwatch.GetTimestamp() - rangeAt;

        return (face, at.Length > 0 ? new FlexSlots(at[0], at[^1] - at[0] + 1) : null);
    }

    /// <summary>The face of a prop no player drives, kept per model and kind; timed and counted as the resting part.</summary>
    private float[]? RestingFaceOf(SceneProp prop, PropModels.ModelFrames model, int[] order)
    {
        long restingAt = Stopwatch.GetTimestamp();
        _flexTally.RestingFaces++;

        if (!_restingFace.TryGetValue(RestingKey(prop), out float[]? face))
        {
            // **No face of its own**: a bone-merged item whose parent is not a player is a C_BaseFlex at its zeroed
            // controllers (each at its minimum, `c_baseflex.cpp:1222`); anything else is a C_BaseAnimating, whose
            // `SetupWeights` is empty, so it draws with the weights `LockFlexWeights` zeroed (studiorender
            // 0x1800584b0) — which still moves a flex whose ramp is not zero at zero (B513). A corpse whose player is
            // alive again is the first kind: `C_TFRagdoll::SetupWeights` takes its own `BaseClass` branch
            // (`c_tf_player.cpp:630-633`), a C_BaseFlex nothing ever set.
            float[] weights = IsCBaseFlex(prop)
                ? FaceFlex.Rules(model.Flex, StudioFlexRules.Resting(model.Flex))
                : new float[model.Flex.Descriptors.Count];
            face = FaceFlex.Deltas(model.Flex, weights, weights, model.VertexCount) is { } deltas
                ? FaceFlex.InBufferOrder(deltas, model.CornerVertex, order)
                : null;
            _restingFace[RestingKey(prop)] = face;
        }

        _flexTally.Resting += Stopwatch.GetTimestamp() - restingAt;
        return face;
    }

    private static string RestingKey(SceneProp prop) => (IsCBaseFlex(prop) ? "1:" : "0:") + prop.ModelPath;

    /// <summary>Whether a prop with no player face behind it is still a <c>C_BaseFlex</c>: a worn item or a corpse.</summary>
    private static bool IsCBaseFlex(SceneProp prop) => prop.AttachedTo is not null || prop.FaceOf is not null;

    /// <summary>What the studio render writes into this model's eye and teeth materials this frame (B725).</summary>
    /// <param name="prop">The entity.</param>
    /// <param name="model">Its model.</param>
    /// <param name="bones">Its built bones, bone-to-world.</param>
    /// <returns>Each eyeball's values, and the first mouth's; null where the model has none.</returns>
    /// <remarks>
    /// **The view target**: a player's is <c>C_TFPlayer::UpdateLookAt</c>'s (<see cref="LookTargetOf"/>), a corpse's or a
    /// worn item's <c>m_viewtarget</c>, never set on the client and only zeroed in a debug build (`c_baseflex.cpp:129-131`)
    /// — (0, 0, 0) here; both through <c>C_BaseFlex::SetViewTarget</c>'s clamp in the <c>eyes</c> attachment's frame, with
    /// the actor's <c>eyes_updown</c>/<c>eyes_rightleft</c> global weights (<see cref="_faceGlobal"/>). A C_BaseAnimating
    /// never sets one and reads the render context's last, <see cref="ViewTarget"/>, in this pass's draw order (B725 5d).
    ///
    /// **The mouth only for a model with a Teeth material** (<see cref="PropModels.ModelFrames.HasTeeth"/>): only a teeth
    /// material reads <c>$forward</c> and <c>$illumfactor</c>, and the bind hands it only to one flagged
    /// <c>$clientShader MouthShader</c> (<see cref="EyeMaterial.MouthShader"/>, studiorender's own gate, B725 5e), so computing
    /// it for every teeth model is a cost bound, not a second gate. Its weight is the descriptor weight the face drew with this
    /// frame (<see cref="FlexFor"/>), the resting rules for a C_BaseFlex no scene drives (kept per model), and zero for
    /// anything else (<c>LockFlexWeights</c>).
    ///
    /// **Written into the entity's own <see cref="StudioFace"/>, one per pose half** (B581 rung 3): the frame in flight reads
    /// the other half's, and nothing is allocated per frame once an entity has drawn.
    /// </remarks>
    private StudioFace? FaceShading(SceneProp prop, PropModels.ModelFrames? model, Animation.Animating.BoneAccessor? bones)
    {
        if (model is null || bones is null)
        {
            return null;
        }

        // **The render context's one view target** (B725 5d): set by a C_BaseFlex that reaches `SetViewTarget`, eyes or none,
        // and read by every eye drawn after it, its own included.
        if (SetsViewTarget(prop, model))
        {
            ViewTarget.Target = ViewTargetOf(prop, model, bones);
        }

        (float X, float Y, float Z) target = ViewTarget.Target;
        bool mouthed = model.HasTeeth && model.Mouths.Count > 0;

        if (model.Eyeballs.Count == 0 && !mouthed)
        {
            return null;
        }

        Dictionary<int, StudioFace> faces = _faceShading[PoseHalf];

        if (!faces.TryGetValue(prop.EntityIndex, out StudioFace? face) || face.Eyes.Length != model.Eyeballs.Count)
        {
            face = new StudioFace(new EyeShading[model.Eyeballs.Count]);
            faces[prop.EntityIndex] = face;
        }

        if (model.Eyeballs.Count > 0)
        {
            StudioEyes.FillEyes(face, model, bones, target);
        }

        face.Mouth = null;

        if (mouthed && model.Mouths[0].Bone is >= 0 and var mouthBone && mouthBone < bones.Count)
        {
            float[] weights = LastFaceWeights(PoseHalf, prop.EntityIndex)?.Weights ??
                (IsCBaseFlex(prop) ? RestingWeightsOf(prop.ModelPath, model) : []);

            face.Mouth = StudioEyes.Mouth(model.Mouths[0], bones.Bone(mouthBone), weights);
        }

        return face;
    }

    /// <summary>Each entity's face values, reused, one set per pose half (<see cref="FaceShading"/>).</summary>
    private readonly Dictionary<int, StudioFace>[] _faceShading = [[], []];

    /// <summary>A model's descriptor weights at its resting controllers, for a C_BaseFlex no scene drives, kept per model.</summary>
    private readonly Dictionary<string, float[]> _restingWeights = new(StringComparer.OrdinalIgnoreCase);

    private float[] RestingWeightsOf(string modelPath, PropModels.ModelFrames model)
    {
        if (!_restingWeights.TryGetValue(modelPath, out float[]? weights))
        {
            weights = FaceFlex.Rules(model.Flex, StudioFlexRules.Resting(model.Flex));
            _restingWeights[modelPath] = weights;
        }

        return weights;
    }

    /// <summary>
    /// The actor's global flex weights <see cref="FlexFor"/> drove this entity's face with, this draw — null when it drove
    /// none. Read by <see cref="ViewTargetOf"/> for the eye deflection, so the face is stepped once.
    /// </summary>
    private IReadOnlyDictionary<string, float>? _faceGlobal;

    /// <summary>
    /// The living players of this pass by index, as <see cref="StudioEyes.LookAt"/> takes them: gathered and sorted once, at
    /// the pass's first looker (<see cref="_lookableGathered"/>, cleared at the start of <c>Instances</c>).
    /// </summary>
    private readonly List<(int Index, (float X, float Y, float Z) Origin, (float X, float Y, float Z) Eyes)> _lookable = [];

    /// <summary>Whether <see cref="_lookable"/> holds this pass's players yet.</summary>
    private bool _lookableGathered;

    /// <summary>
    /// Whether a prop is a player as <c>UTIL_PlayerByIndex</c> answers one: a class model drawn as itself, and — where the
    /// scene says who the players are — one of them (<see cref="PlayerAlive"/> null for anything else). The same gate
    /// <see cref="FaceDriving"/> keeps; a map prop wearing a class model is neither a looker nor looked at.
    /// </summary>
    private bool IsScenePlayer(SceneProp prop) =>
        IsPlayerModel(prop) && prop.AttachedTo is null && (PlayerAlive is null || PlayerAlive(prop.EntityIndex) is not null);

    /// <summary>The pass's living players, gathered once (see <see cref="_lookable"/>).</summary>
    private List<(int Index, (float X, float Y, float Z) Origin, (float X, float Y, float Z) Eyes)> LookablePlayers()
    {
        if (_lookableGathered)
        {
            return _lookable;
        }

        _lookable.Clear();

        foreach (SceneProp other in _frameProps.Values)
        {
            // `if ( !pEnt->IsAlive() ) continue;` (`c_tf_player.cpp:6229`); without a scene to ask, every one is taken alive.
            if (IsScenePlayer(other) && PlayerAlive?.Invoke(other.EntityIndex) != false)
            {
                // `EyePosition()` is the origin plus `m_vecViewOffset`, which only the local player has.
                (float X, float Y, float Z) origin = (other.Pose.X, other.Pose.Y, other.Pose.Z);
                float eyeHeight = LocalPlayerView is { } local && local.Index == other.EntityIndex ? local.EyeHeight : 0f;
                _lookable.Add((other.EntityIndex, origin, (origin.X, origin.Y, origin.Z + eyeHeight)));
            }
        }

        // **A dormant player is still `UTIL_PlayerByIndex`'s** (B725): out of the PVS and drawn by nothing, at his last origin
        // with his last life state, and looked at at that origin — a non-local player's `EyePosition()`.
        // Indexed, so the gathering allocates no enumerator (B725 review: the look-at allocates nothing).
        for (int at = 0; at < (DormantPlayers?.Count ?? 0); at++)
        {
            SceneDormantPlayer dormant = DormantPlayers![at];

            // A sample that still holds him as a prop while his frame calls him dormant lists him once (review).
            if (dormant.IsAlive && !_frameProps.ContainsKey(dormant.EntityIndex))
            {
                (float X, float Y, float Z) origin = (dormant.X, dormant.Y, dormant.Z);
                _lookable.Add((dormant.EntityIndex, origin, origin));
            }
        }

        _lookable.Sort(static (a, b) => a.Index.CompareTo(b.Index));
        _lookableGathered = true;

        return _lookable;
    }

    /// <summary><c>C_TFPlayer::UpdateLookAt</c> (`c_tf_player.cpp:6210-6300`) for a drawn player.</summary>
    /// <remarks>
    /// **Its angles are (0, 0, 0).** `GetLocalAngles()` is `m_angRotation`, which a player's send table excludes
    /// (`tf_player.cpp:773`, `SendPropExclude( "DT_BaseEntity", "m_angRotation" )`); `C_TFPlayer::OnDataChanged` sets the
    /// network angles to the local ones it already has (`c_tf_player.cpp:4415`), and only the LOCAL player's are set, from
    /// the view (`c_baseplayer.cpp:883`). So a non-local player looks along world +x, 512 units, unless a living player is
    /// within 300 units on that side. **Another player's `EyePosition()` is its origin**: `m_vecViewOffset` is in
    /// `DT_LocalPlayerExclusive` (`c_baseplayer.cpp:223-229`) and only game movement sets it, on the predicted player.
    /// **The POV recorder is the local player** (<see cref="LocalPlayerView"/>): its angles are the view's
    /// (`c_baseplayer.cpp:865-883`), and it is looked at at its eye. A dormant player, whom `UTIL_PlayerByIndex` still
    /// returns, is found among <see cref="DormantPlayers"/>.
    /// </remarks>
    private (float X, float Y, float Z) LookTargetOf(SceneProp prop) =>
        LookTargetFrom((prop.Pose.X, prop.Pose.Y, prop.Pose.Z), prop.EntityIndex);

    /// <summary><see cref="LookTargetOf"/> for player <paramref name="index"/> standing at <paramref name="origin"/>.</summary>
    private (float X, float Y, float Z) LookTargetFrom((float X, float Y, float Z) origin, int index) =>
        StudioEyes.LookAt(
            origin,
            LocalPlayerView is { } local && local.Index == index ? (local.Pitch, local.Yaw, 0f) : (0f, 0f, 0f),
            LookablePlayers(),
            index);

    /// <summary>
    /// The POV recorder — the local player during playback — as <c>UpdateLookAt</c> reads it: its index, its local angles
    /// (<c>engine->GetViewAngles</c>, set on the local player alone in <c>C_BasePlayer::PostDataUpdate</c>,
    /// <c>c_baseplayer.cpp:865-883</c>) and its <c>m_vecViewOffset[2]</c>; null for SourceTV, whose local player is no one drawn.
    /// </summary>
    public (int Index, float Pitch, float Yaw, float EyeHeight)? LocalPlayerView { get; set; }

    /// <summary>
    /// A player's <c>GetAbsOrigin()</c> by index, dead or alive, or null where the scene does not know him: where a corpse
    /// whose player is dead runs that player's <c>UpdateLookAt</c> from (B725 review 1).
    /// </summary>
    public Func<int, (float X, float Y, float Z)?>? PlayerOrigin { get; set; }

    /// <summary>The players out of the PVS this moment, which the eyes' look-at still finds by index (B725); null is none.</summary>
    public IReadOnlyList<SceneDormantPlayer>? DormantPlayers { get; set; }

    /// <summary>
    /// Whether this draw sets the render context's view target: a C_BaseFlex — a player, a worn item, a corpse — whose model
    /// has flex controllers. A C_BaseAnimating (a map prop, one wearing a class model included) never calls
    /// <c>SetViewTarget</c>; nor does a C_BaseFlex without controllers, as <c>SetupGlobalWeights</c> returns false before
    /// <c>SetupLocalWeights</c> (<c>c_baseflex.cpp:1198-1207</c>). Either reads the last target set.
    /// </summary>
    /// <remarks>
    /// **A bone-merged worn item on a flexing C_BaseFlex sets none** (review 1): <c>CEconEntity::SetupWeights</c> runs the
    /// parent's <c>SetupGlobalWeights</c> and its own rules and returns with <c>SetViewTarget</c> commented out
    /// (<c>econ_entity.cpp:1361-1392</c>); it falls to <c>C_BaseFlex::SetupWeights</c> (:1397), which sets its own, only when
    /// not bone-merged, not on a C_BaseFlex, or the parent's <c>SetupGlobalWeights</c> returns false — a parent model without
    /// controllers (<c>c_baseflex.cpp:1198-1207</c>).
    /// </remarks>
    private bool SetsViewTarget(SceneProp prop, PropModels.ModelFrames model)
    {
        if (model.Flex.Controllers.Count == 0 || !(IsScenePlayer(prop) || IsCBaseFlex(prop)) || IsUnplacedDeadPlayersCorpse(prop))
        {
            return false;
        }

        if (prop.BoneMerged && prop.AttachedTo is { } parentIndex && _frameProps.TryGetValue(parentIndex, out SceneProp? parent)
            && (IsScenePlayer(parent) || IsCBaseFlex(parent))
            && FindMdl(parent.ModelPath) is { Flex.Controllers.Count: > 0 })
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// The player behind a corpse as <c>C_TFRagdoll::SetupWeights</c> finds him (<c>c_tf_player.cpp:626-637</c>): his index,
    /// <c>IsAlive()</c>, and where he stands; null where <c>GetPlayer()</c> has no one (<c>!pPlayer</c>) or the prop is no
    /// corpse. <c>GetPlayer</c> is <c>return m_hPlayer;</c> (<c>c_tf_player.h:1049</c>), so a DORMANT player is found, by
    /// his last life state and origin (<see cref="DormantPlayers"/>). With <see cref="PlayerAlive"/> unset every corpse's
    /// player is taken for dead, placed nowhere.
    /// </summary>
    private (int Index, bool Alive, (float X, float Y, float Z)? Origin)? CorpsePlayer(SceneProp prop)
    {
        if (prop.FaceOf is not { } player)
        {
            return null;
        }

        if (PlayerAlive is null)
        {
            return (player, false, PlayerOrigin?.Invoke(player));
        }

        if (PlayerAlive(player) is { } alive)
        {
            return (player, alive, PlayerOrigin?.Invoke(player));
        }

        for (int at = 0; at < (DormantPlayers?.Count ?? 0); at++)
        {
            SceneDormantPlayer dormant = DormantPlayers![at];

            if (dormant.EntityIndex == player)
            {
                return (player, dormant.IsAlive, (dormant.X, dormant.Y, dormant.Z));
            }
        }

        return null;
    }

    /// <summary>
    /// A corpse whose player is dead and placed: <c>pPlayer->SetupWeights</c> (<c>c_tf_player.cpp:634-637</c>) sets the
    /// player's target, his <c>UpdateLookAt</c> from his origin — which <c>ClientThink</c> keeps running while he is dead or
    /// dormant (<c>c_tf_player.cpp:5948-5955</c>; no dormant gate in <c>client_thinklist.cpp:186-324</c>). Null for anything
    /// else, and for a dead player with no origin, whom no look-at can run from (not reachable from <c>MomentScene</c>,
    /// which fills <see cref="PlayerAlive"/> and <see cref="PlayerOrigin"/> from one <c>ScenePlayer</c>).
    /// </summary>
    private (int Index, (float X, float Y, float Z) Origin)? DeadPlayerOf(SceneProp prop) =>
        CorpsePlayer(prop) is { Alive: false, Origin: { } origin } dead ? (dead.Index, origin) : null;

    /// <summary>A corpse whose player is dead but placed nowhere: it sets no target (see <see cref="DeadPlayerOf"/>).</summary>
    private bool IsUnplacedDeadPlayersCorpse(SceneProp prop) => CorpsePlayer(prop) is { Alive: false, Origin: null };

    /// <summary>The view target a C_BaseFlex's <c>SetViewTarget</c> hands the render (see <see cref="FaceShading"/>).</summary>
    /// <remarks>
    /// *Interpolated* for a corpse whose player is dead: the clamp is taken in the corpse's own <c>eyes</c> attachment, where
    /// the engine takes the dead player's (<c>GetAttachment</c> on the player, <c>c_baseflex.cpp:574-583</c>), a model this
    /// viewer does not pose once he is dead.
    /// </remarks>
    private (float X, float Y, float Z) ViewTargetOf(
        SceneProp prop, PropModels.ModelFrames model, Animation.Animating.BoneAccessor bones)
    {
        (float X, float Y, float Z) target = (0f, 0f, 0f);

        if (IsScenePlayer(prop))
        {
            target = LookTargetOf(prop);
        }
        else if (DeadPlayerOf(prop) is { } player)
        {
            target = LookTargetFrom(player.Origin, player.Index);
        }

        // `eyeAng.x/y = g_flexweight[ pflex->localToGlobal ]` only where the model has the controller (`c_baseflex.cpp:596-606`).
        return StudioEyes.ClampedTarget(
            target, model, bones, EyeController(model, "eyes_updown"), EyeController(model, "eyes_rightleft"));
    }

    /// <summary>
    /// An eye controller's global weight: the actor's, as <see cref="FlexFor"/> drove the face with it; a C_BaseFlex no scene
    /// drives at the controller's minimum (<see cref="StudioEyes.RestingController"/>); 0 where the model lacks it.
    /// </summary>
    private float EyeController(PropModels.ModelFrames model, string name)
    {
        if (_faceGlobal is null)
        {
            return StudioEyes.RestingController(model, name);
        }

        foreach (StudioFlexController controller in model.Flex.Controllers)
        {
            if (string.Equals(controller.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return _faceGlobal.GetValueOrDefault(controller.Name);
            }
        }

        return 0f;
    }

    /// <summary>
    /// Whether an entity is a player and alive now (<c>C_TFRagdoll::SetupWeights</c> asks <c>IsAlive()</c>): null for an
    /// entity that is not a player — a class model placed by a map is a prop, not a face. Unset, every class model is
    /// taken for a player and every corpse's player for dead.
    /// </summary>
    public Func<int, bool?>? PlayerAlive { get; set; }

    /// <summary>Each TF player's face, by entity index, with the model it was reset for.</summary>
    private readonly Dictionary<int, (string Model, ActorFace Face)> _faces = [];

    /// <summary>Each drawing entity's <c>m_flFlexDelayedWeight</c> and <c>m_flFlexDelayTime</c>.</summary>
    private readonly Dictionary<int, (float[] Delayed, double Time)> _delays = [];

    /// <summary>Each drawing entity's last weights and the face they made, reused while they hold; one set per pose half.</summary>
    private readonly Dictionary<int, (float[] Weights, float[] Delayed, float[]? Face, float[]? Stream, FlexedCorners? Corners)>[] _lastFace = [[], []];

    /// <summary>The weights and delayed weights a pose half's last rebuilt face of an entity used, as FlexFor stored them.</summary>
    /// <remarks>For the differential that holds FlexFor to its reference (docs/measurements/2026-10-30-flex-for-cost-after.md).</remarks>
    internal (float[] Weights, float[] Delayed)? LastFaceWeights(int half, int entity) =>
        _lastFace[half].TryGetValue(entity, out (float[] Weights, float[] Delayed, float[]? Face, float[]? Stream, FlexedCorners? Corners) last)
            ? (last.Weights, last.Delayed)
            : null;

    /// <summary>Per model, the buffer slots its flexes can move (finding 92).</summary>
    private readonly Dictionary<string, FlexedCorners> _flexedCorners = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The buffer slots any flex of a model can move, out of how many it has, once a face of it was driven; null before.
    /// </summary>
    /// <param name="modelPath">The model.</param>
    /// <returns>The slots (ascending) and the model's buffer vertex count, or null.</returns>
    public (FlexedCorners Corners, int Slots)? FlexedSlotsOf(string modelPath) =>
        _flexedCorners.TryGetValue(modelPath, out FlexedCorners? corners) && _packedOrder.TryGetValue(modelPath, out int[]? order)
            ? (corners, order.Length)
            : null;

    private FlexedCorners FlexedCornersOf(string modelPath, PropModels.ModelFrames model, int[] order)
    {
        if (!_flexedCorners.TryGetValue(modelPath, out FlexedCorners? corners))
        {
            corners = FaceFlex.FlexedCorners(model.Flex, model.CornerVertex, order);
            _flexedCorners[modelPath] = corners;
        }

        return corners;
    }

    /// <summary>What drives faces: every scene and voice in the recording (B513); null drives none.</summary>
    public FaceSources? Faces { get; set; }

    /// <summary>
    /// The global weights a prop's rules read, or null for a prop with no TF player behind it: a player's own face; a
    /// bone-merged item on a player reads the player's (<c>CEconEntity::SetupWeights</c>, <c>econ_entity.cpp:1377</c>); a
    /// corpse reads its dead player's (<c>C_TFRagdoll::SetupWeights</c>, <c>c_tf_player.cpp:636</c>).
    /// </summary>
    private IReadOnlyDictionary<string, float>? FaceDriving(SceneProp prop, double seconds)
    {
        int actor;

        // Which branch drove it, for the counters: 0 a corpse, 1 a worn item, 2 the player's own.
        int kind;

        if (prop.FaceOf is { } dead)
        {
            // `( pPlayer && pPlayer->IsAlive() ) || !pPlayer` takes the corpse's own face (c_tf_player.cpp:630): the same
            // branch the view target takes (CorpsePlayer), a dormant player found by his handle (B725 review 2).
            if (CorpsePlayer(prop) is not { Alive: false })
            {
                return null;
            }

            actor = dead;
            kind = 0;

            // A player who died before the first frame drawn has no face yet; the corpse wears their class model, so
            // the face is that model's (only when absent: a living player's own draw keeps it current).
            if (!_faces.ContainsKey(actor))
            {
                EnsureFace(actor, prop.ModelPath, seconds);
            }
        }
        else if (prop.BoneMerged && prop.AttachedTo is { } parent && _faces.ContainsKey(parent))
        {
            actor = parent;
            kind = 1;
        }
        else if (IsPlayerModel(prop) && (PlayerAlive is null || PlayerAlive(prop.EntityIndex) is not null))
        {
            actor = prop.EntityIndex;
            kind = 2;
            EnsureFace(actor, prop.ModelPath, seconds);
        }
        else
        {
            return null;
        }

        if (!_faces.TryGetValue(actor, out (string Model, ActorFace Face) face))
        {
            return null;
        }

        // The arguments taken into locals, in the order C# evaluates them, so each is timed apart.
        long scenesAt = Stopwatch.GetTimestamp();
        IReadOnlyList<FaceScene> scenes = Faces?.Scenes(actor, seconds) ?? GestureScenes(prop);
        long voicesAt = Stopwatch.GetTimestamp();
        IReadOnlyList<FaceVoice> voices = Faces?.Voices(actor, seconds) ?? [];
        long stepAt = Stopwatch.GetTimestamp();

        face.Face.Step(seconds, prop.EntityIndex, scenes, voices);

        FlexTally tally = _flexTally;
        tally.Scenes += voicesAt - scenesAt;
        tally.Voices += stepAt - voicesAt;
        tally.Step += Stopwatch.GetTimestamp() - stepAt;

        switch (kind)
        {
            case 0:
                tally.Corpse++;
                break;
            case 1:
                tally.Worn++;
                break;
            default:
                tally.Own++;
                break;
        }

        return face.Face.Global;
    }

    /// <summary>
    /// Every player's frame start, drawn or not: the engine interpolates and latches each client-animated entity every
    /// frame (`InterpolateServerEntities`, `UpdateClientSideAnimations`, `cdll_client_int.cpp:2156`, `:2189`) and each
    /// scene thinks every frame (`C_SceneEntity::ClientThink`), whatever is on screen — so a face that returns to view is
    /// current. Only `SetupGlobalWeights` — the decay, the flex animations, the expressions — waits for a draw.
    /// </summary>
    private void BeginFaces(IReadOnlyList<SceneProp> props, double seconds)
    {
        foreach (SceneProp prop in props)
        {
            if (IsPlayerModel(prop) && (PlayerAlive is null || PlayerAlive(prop.EntityIndex) is not null))
            {
                EnsureFace(prop.EntityIndex, prop.ModelPath, seconds);

                if (_faces.TryGetValue(prop.EntityIndex, out (string Model, ActorFace Face) face))
                {
                    face.Face.BeginFrame(seconds, Faces?.Scenes(prop.EntityIndex, seconds) ?? GestureScenes(prop));
                }
            }
        }
    }

    /// <summary>A player's face for a model: kept while the model holds, reset when it changes or the clock runs back.</summary>
    private void EnsureFace(int actor, string modelPath, double seconds)
    {
        if (!_frames.TryGetValue(modelPath, out PropModels.ModelFrames? own))
        {
            return;
        }

        if (!_faces.TryGetValue(actor, out (string Model, ActorFace Face) known) ||
            !string.Equals(known.Model, modelPath, StringComparison.OrdinalIgnoreCase) ||
            seconds < known.Face.LastSeconds)
        {
            // `C_TFPlayer::OnNewModel` resets the face; a seek back starts it again too (interpolated: the engine
            // never plays a demo backwards).
            _faces[actor] = (modelPath, new ActorFace(
                own.Flex, own.Flex.ModelName, Faces,
                Faces?.InterpolationSeconds ?? ScenePropTrack.DelayTicksFor(IntervalPerTick) * (double)IntervalPerTick,
                seconds));
            _delays.Remove(actor);
        }
    }

    /// <summary>A TF class model, <c>models/player/&lt;class&gt;.mdl</c> — the one shape a <c>C_TFPlayer</c> draws.</summary>
    private static bool IsPlayerModel(SceneProp prop) =>
        !prop.BoneMerged && prop.FaceOf is null &&
        prop.ModelPath.StartsWith("models/player/", StringComparison.OrdinalIgnoreCase) &&
        prop.ModelPath.IndexOf('/', "models/player/".Length) < 0;

    /// <summary>Without a recording's sources, the gesture slot's scenes — the scene a test hands a prop.</summary>
    private static List<FaceScene> GestureScenes(SceneProp prop) =>
        [.. (prop.Pose.Gestures ?? []).Where(g => g.Taunt is not null)
            .Select(g => new FaceScene(g.Taunt!, g.StartedSeconds, g.StoppedSeconds))];

    /// <summary><c>RunFlexDelay</c> on the drawing entity's own delayed weights.</summary>
    private float[] Delayed(int entity, float[] weights, double seconds)
    {
        if (!_delays.TryGetValue(entity, out (float[] Delayed, double Time) state) ||
            state.Delayed.Length != weights.Length || seconds < state.Time)
        {
            state = (new float[weights.Length], 0d);
        }

        // A second draw in one frame blends nothing: the time it left is now (`flFlexDelayTime < curtime` fails).
        double time = state.Time;
        FaceFlex.Delay(weights, state.Delayed, ref time, seconds, seconds - state.Time);

        _delays[entity] = (state.Delayed, time);
        return state.Delayed;
    }

    /// <summary>Each baked static prop's colours in its model buffer's order, built once per entity.</summary>
    private readonly Dictionary<int, float[]?> _bakedByEntity = [];

    /// <summary>
    /// `mat_bumpmap` and `mat_phong` as the models were last loaded under (B720). A change reloads every model in TF2, so a
    /// model's bump flag is decided again (`ModelFrames.StudioFlagsUnder`), and the engine's restore
    /// (`engine.dll` `FUN_1800f7b40`) restores the static props' colour data: the colours are built again here.
    /// </summary>
    public MaterialSwitches MaterialSwitches
    {
        get;
        set
        {
            if (field == value)
            {
                return;
            }

            field = value;
            _bakedByEntity.Clear();
            _decalLights.Clear();
            _colourMeshOrigins.Clear();
            RestoreStaticPropColours();
        }
    } = MaterialSwitches.Default;

    /// <summary>
    /// Each baked static prop's `.vhv` colours, by entity index — red, green, blue per corner of its model's
    /// geometry, overbright already applied — set when a map is read (B426).
    /// </summary>
    /// <remarks>
    /// `engine.dll` `0x1800f1bd0`: a static prop with baked colours is drawn with its per-placement colour
    /// mesh plus only what its handle's flags 6 hold (B669). An entity found here draws that way unless its model is refused one
    /// (<see cref="StaticPropVertexLighting.RefusesColourMesh"/>); one that is not takes a CPU bake, or its handle's flags 7 (B674).
    /// </remarks>
    public IReadOnlyDictionary<int, float[]> StaticPropColours
    {
        get;
        set
        {
            field = value;
            _bakedByEntity.Clear();
            _decalLights.Clear();
            _colourMeshOrigins.Clear();
            _lateLitVersion = -1;
        }
    } = new Dictionary<int, float[]>();

    /// <summary>
    /// The styled and dynamic lights a static prop with a colour mesh draws on top of its colours, by entity and lighting
    /// origin (<see cref="LevelLighting.StaticPlusDynamicLights"/>, B424, B425); null gives it none.
    /// </summary>
    public Func<int, float, float, float, PointLighting>? StaticPlusDynamicLights { get; set; }

    /// <summary>
    /// The whole lighting state of a static prop drawn without a colour mesh, by entity and lighting origin
    /// (<see cref="LevelLighting.StaticPropFullLightingAt"/>, B514); null gives it none. It never takes the model path's light (B674).
    /// </summary>
    public Func<int, float, float, float, (PointLighting Lighting, SunLight? Sun)>? StaticPropFullLighting { get; set; }

    /// <summary>The model decal pool the device draws from, read to light only a static prop that holds a decal (B642).</summary>
    public ModelDecals? Decals
    {
        get;
        set
        {
            field = value;

            // A new pool may share the old one's version; light it on the next step whatever it says (B642 review 3).
            _lateLitVersion = -1;
        }
    }

    /// <summary>Each decal-holding colour-mesh prop's last decal light, kept while it is unchanged.</summary>
    private readonly Dictionary<int, DecalLighting> _decalLights = [];

    /// <summary>A colour-mesh static prop's decal light, or null when it holds no decal (B642).</summary>
    /// <param name="entity">The prop's entity index.</param>
    /// <param name="x">Its lighting origin.</param>
    /// <param name="y">Its lighting origin.</param>
    /// <param name="z">Its lighting origin.</param>
    /// <returns>The full state at the origin, the instance kept from the last frame where nothing changed.</returns>
    /// <remarks>
    /// **The static state its colours carry, added back**: `engine.dll` lights a colour-mesh prop's decals with its draw
    /// state plus the record's static state at `record + 0x88` (`0x1800f1bd0`, `0x1800eb5e0`), the full state, flags 7.
    /// **Only for a prop that holds a decal**, as the engine's gate `record + 0x10 != 0` is: a prop with none pays no second
    /// ranking. The pool is read as this frame's model pass sees it; a decal the replay places after the pass is lit by
    /// <see cref="LightDecalHolders"/>, which the decal step calls, so the first draw is right even on a paused frame.
    /// ponytail: one ranking over static, styled and dynamic lights; the engine lists the record's lights first and appends
    /// the draw's without ranking again, which differs only past four lights at one prop (B654).
    /// </remarks>
    private DecalLighting? DecalLightOf(int entity, float x, float y, float z) =>
        Decals?.Holds(DecalReplay.KeyOf(entity)) == true ? DecalLightAt(entity, x, y, z) : null;

    /// <summary>The full state at a colour-mesh prop's lighting origin, the kept instance where nothing changed.</summary>
    private DecalLighting? DecalLightAt(int entity, float x, float y, float z)
    {
        if (StaticPropFullLighting is not { } fullLighting)
        {
            return null;
        }

        (PointLighting lighting, SunLight? sun) = fullLighting(entity, x, y, z);

        if (_decalLights.TryGetValue(entity, out DecalLighting? kept) &&
            kept.Cube == lighting.Cube && kept.Sun == sun && ReferenceEquals(kept.Locals, lighting.Locals))
        {
            return kept;
        }

        DecalLighting made = new(lighting.Cube, sun, lighting.Locals);
        _decalLights[entity] = made;

        return made;
    }

    /// <summary>Each colour-mesh static prop's lighting origin, recorded at load, for a decal lit after the pose (B642).</summary>
    private readonly Dictionary<int, (float X, float Y, float Z)> _colourMeshOrigins = [];

    /// <summary>The pool version <see cref="LightDecalHolders"/> last lit for.</summary>
    private int _lateLitVersion = -1;

    /// <summary>Decal lights built by <see cref="LightDecalHolders"/>, ever: the control that late holders are lit.</summary>
    public int LateDecalLights { get; private set; }

    /// <summary>Times <see cref="LightDecalHolders"/> found the pool changed and lit it, ever: the control that the step calls it.</summary>
    public int DecalLightPasses { get; private set; }

    /// <summary>
    /// Lights every colour-mesh static prop holding a decal, into the pool's late lights, after the decal step (B642).
    /// </summary>
    /// <remarks>
    /// **The replay places a static prop's decals after this frame's pose** (`DecalEffects.StepOver`): on a map's first
    /// frame, after a seek, whenever a bullet lands. That pose saw no holder and a paused viewer poses once per build, so
    /// without this the first draw — and every draw of a paused frame — would light the decal with the prop's own flags-6
    /// state. `engine.dll` has no such window: the record's static state is built with the colour mesh (`0x1800eac60`) and
    /// read at the draw. Runs only when the pool changed, and lights only holders, so a frame with no new decal costs a
    /// compare.
    /// </remarks>
    public void LightDecalHolders()
    {
        // **The static props' own version, not the pool's** (B642 review 3): a player's decal, heal, über, class change or
        // cloak changes the pool and none of these lights.
        if (Decals is not { } pool || pool.StaticPropVersion == _lateLitVersion)
        {
            return;
        }

        _lateLitVersion = pool.StaticPropVersion;
        DecalLightPasses++;

        // Reused lists: a pass allocates nothing of its own (B642 review 3).
        pool.StaticPropHolders(_lateHolderKeys);
        _lateLightBuffer.Clear();

        foreach (int key in _lateHolderKeys)
        {
            int entity = PropModels.FirstStaticPropEntityIndex + DecalReplay.StaticPropOf(key);

            if (_colourMeshOrigins.TryGetValue(entity, out (float X, float Y, float Z) origin) &&
                DecalLightAt(entity, origin.X, origin.Y, origin.Z) is { } light)
            {
                _lateLightBuffer.Add(new(key, light));
                LateDecalLights++;
            }
        }

        pool.SetLateLights(_lateLightBuffer);
    }

    /// <summary><see cref="LightDecalHolders"/>'s holder keys, reused.</summary>
    private readonly List<int> _lateHolderKeys = [];

    /// <summary><see cref="LightDecalHolders"/>'s lights, reused.</summary>
    private readonly List<KeyValuePair<int, DecalLighting>> _lateLightBuffer = [];

    /// <summary>
    /// A static prop handle's static lighting state at its lighting origin (<see cref="LevelLighting.StaticPropLightingAt"/>),
    /// which an unbaked <c>$staticprop</c> model's CPU colour mesh is lit with (B429); null lights none on the CPU.
    /// </summary>
    public Func<float, float, float, (PointLighting Lighting, SunLight? Sun)>? StaticPropLighting
    {
        get;
        set
        {
            field = value;
            _bakedByEntity.Clear();
        }
    }

    /// <summary>
    /// An unbaked static prop's colour mesh lit on the CPU, per corner of its model's geometry (B429): `engine.dll`
    /// `FUN_1800f36e0` when the `.vhv` is absent, for a model compiled <c>$staticprop</c> (`FUN_1800eac60`).
    /// </summary>
    /// <remarks>
    /// Built once and never rebuilt. The engine rebuilds it when a listed one-frame style changes (`FUN_1801bb8b0`), but
    /// it builds it from the handle's STATIC state (`FUN_1801ba590` flags 1), which holds no styled light — so the
    /// rebuild reproduces these colours, as it reproduces a `.vhv` prop's by reloading the same file.
    /// </remarks>
    private bool TryLightOnCpu(SceneProp prop, ScenePose pose, float x, float y, float z, out float[] byCorner)
    {
        byCorner = [];

        if (StaticPropLighting is not { } lightingAt ||
            prop.EntityIndex < PropModels.FirstStaticPropEntityIndex ||
            !_frames.TryGetValue(prop.ModelPath, out PropModels.ModelFrames? frames) ||
            !StaticPropVertexLighting.Lights(frames.StudioFlags) ||
            !_raw.TryGetValue(prop.ModelPath, out IReadOnlyList<PropVertex>? corners))
        {
            return false;
        }

        (PointLighting lighting, SunLight? sun) = lightingAt(x, y, z);

        byCorner = StaticPropVertexLighting.Colours(
            corners,
            new PropTransform(pose.X, pose.Y, pose.Z, pose.Pitch, pose.Yaw, pose.Roll, 1f),
            lighting,
            sun,
            StaticPropVertexLighting.ConstantDot(frames.StudioFlags,frames.ConstantDirectionalLightDot));

        return true;
    }

    /// <summary>A baked static prop's colours in its model buffer's order, or null when it has none.</summary>
    /// <remarks>
    /// Null also when the colours do not cover the model's corners — a geometry this set packed differently
    /// from the one the colours were read against — which is refused, said once, and falls to full lighting.
    /// </remarks>
    private float[]? BakedColoursOf(SceneProp prop, ScenePose pose, float x, float y, float z)
    {
        if (_bakedByEntity.TryGetValue(prop.EntityIndex, out float[]? built))
        {
            return built;
        }

        long buildAt = System.Diagnostics.Stopwatch.GetTimestamp();
        built = BuildBakedColours(prop, pose, x, y, z);

        // Every answer kept is timed, a refused mesh too (a CPU bake that could not fill, geometry it does not cover); only a
        // mesh built is counted, since a static prop's "no colour mesh" is kept as well and is not a build. A CPU bake's
        // static state (`LevelLighting.StaticStateBuildTicks`) is built inside this clock: the two nest.
        if (_bakedByEntity.ContainsKey(prop.EntityIndex))
        {
            BakedBuildTicks += System.Diagnostics.Stopwatch.GetTimestamp() - buildAt;

            if (built is not null)
            {
                BakedBuilds++;
            }
        }

        return built;
    }

    /// <summary><see cref="BakedColoursOf"/>'s miss: builds and caches, except when nothing lights the prop.</summary>
    private float[]? BuildBakedColours(SceneProp prop, ScenePose pose, float x, float y, float z)
    {
        float[]? built;

        if (StaticPropColours.TryGetValue(prop.EntityIndex, out float[]? byCorner))
        {
            // A `.vhv` mesh `FUN_1800f1550` never wrote takes the CPU bake (B436); with none, the prop is lit per draw.
            if (Array.Exists(byCorner, float.IsNaN))
            {
                byCorner = (float[])byCorner.Clone();

                if (!PropModels.FillUnfilled(byCorner, TryLightOnCpu(prop, pose, x, y, z, out float[] cpu) ? cpu : null))
                {
                    _bakedByEntity[prop.EntityIndex] = null;
                    return null;
                }
            }
        }
        else if (!TryLightOnCpu(prop, pose, x, y, z, out byCorner))
        {
            // A static prop has one answer a level — no colour mesh — so it is kept, as a mesh is; an entity's index is
            // reused by the next entity, so its answer is not. Kept only while what it depended on stands: the model's
            // flags and packed geometry are the level's, and the two sources a refusal can wait on — `StaticPropLighting`
            // and `StaticPropColours` — drop every kept answer when set, so the next draw builds it (B607). In production
            // both are set before the static props are packed (`LevelSystems.Load`, after `ClearMap`'s shutdown).
            if (prop.EntityIndex >= PropModels.FirstStaticPropEntityIndex)
            {
                _bakedByEntity[prop.EntityIndex] = null;
            }

            return null;
        }

        built = null;

        if (_packedOrder.TryGetValue(prop.ModelPath, out int[]? order) &&
            _raw.TryGetValue(prop.ModelPath, out IReadOnlyList<PropVertex>? corners) &&
            byCorner.Length == corners.Count * 3)
        {
            built = new float[order.Length * 3];

            for (int at = 0; at < order.Length; at++)
            {
                Array.Copy(byCorner, order[at] * 3, built, at * 3, 3);
            }
        }
        else
        {
            _render.LogWarning(
                "{Message}",
                $"baked colours for {prop.ModelPath} (entity {prop.EntityIndex}) do not cover its packed geometry; lit per draw instead");
        }

        _bakedByEntity[prop.EntityIndex] = built;
        return built;
    }

    /// <summary>
    /// Builds one static prop's light cache handle and its static state at its lighting origin
    /// (<see cref="LevelLighting.PrecacheStaticProp"/>); null builds none ahead of the draw.
    /// </summary>
    public Action<int, float, float, float, LightBounds?>? StaticPropHandle { get; set; }

    /// <summary>
    /// Every static prop's lighting, built as its level loads (B607): `CStaticPropMgr::PrecacheLighting` (`engine.dll`
    /// `0x180205b20`) builds each prop's light cache handle (`FUN_1801b8350`), and the colour mesh is built with the prop.
    /// </summary>
    /// <remarks>
    /// Run once the static props are packed, since the lighting origin reads the model's `illumposition`. Each prop takes
    /// the draw's own route — <see cref="IlluminationPoint"/> at its pose, the draw's colour-mesh test — so the draw finds
    /// what was built here under the same key. Every prop gets its handle, as `0x180205b20` makes one for each, and every
    /// draw reads it (a colour mesh's flags 6, flags 7 otherwise, B674), with the colour mesh and the static state, which flags 7
    /// reads and a colour-mesh prop's decal light reads (B642). The engine fills that decal state, `record + 0x88`, when
    /// it builds the colour mesh (`engine.dll` `0x1800eac60`), not per draw.
    /// </remarks>
    private void PrecacheStaticLighting()
    {
        // The origins are recorded now: a decal step that ran since the map's colours cleared them found none and marked the
        // pool lit, so light it again on the next step (B642 review 3 follow-up).
        _lateLitVersion = -1;

        foreach (SceneProp prop in _staticProps)
        {
            if (!CanDraw(prop) || prop.EntityIndex < PropModels.FirstStaticPropEntityIndex)
            {
                continue;
            }

            ScenePose pose = prop.Pose;
            (float x, float y, float z) = IlluminationPoint(prop, pose);

            // The colour mesh first, so a CPU bake's static state is built inside `BakedBuildTicks`, as its remark says.
            BuildColourMesh(prop, pose, x, y, z);

            // `FUN_180205b20` hands `FUN_1801b8350` the record's world bounds, `+0x9c` and `+0xa8`.
            (float minX, float minY, float minZ, float maxX, float maxY, float maxZ) = WorldBoxFor(prop);

            StaticPropHandle?.Invoke(prop.EntityIndex, x, y, z, new LightBounds((minX, minY, minZ), (maxX, maxY, maxZ)));
        }
    }

    /// <summary>
    /// Every static prop's colour mesh built again, as the engine's restore does after a <c>mat_bumpmap</c> or
    /// <c>mat_phong</c> change (B720): `engine.dll` `FUN_1800f7b40` calls `CModelRender` +0x78, which restores the static
    /// props' colour data, and leaves their light cache handles alone.
    /// </summary>
    private void RestoreStaticPropColours()
    {
        _lateLitVersion = -1;

        foreach (SceneProp prop in _staticProps)
        {
            if (!CanDraw(prop) || prop.EntityIndex < PropModels.FirstStaticPropEntityIndex)
            {
                continue;
            }

            ScenePose pose = prop.Pose;
            (float x, float y, float z) = IlluminationPoint(prop, pose);

            BuildColourMesh(prop, pose, x, y, z);
        }
    }

    /// <summary>A static prop's colour mesh under the current switches, and where its decals are lit if it has one (B642).</summary>
    /// <remarks>
    /// A prop refused one builds none it would never draw; its decals take its own light, so it records no decal origin.
    /// </remarks>
    private void BuildColourMesh(SceneProp prop, ScenePose pose, float x, float y, float z)
    {
        bool fullyLit = ResolvedFor(prop).Frames is { } frames
            && StaticPropVertexLighting.RefusesColourMesh(frames.StudioFlagsUnder(MaterialSwitches));

        if (!fullyLit && BakedColoursOf(prop, pose, x, y, z) is not null)
        {
            _colourMeshOrigins[prop.EntityIndex] = (x, y, z);
        }
    }

    /// <summary>
    /// <see cref="AnimatingEntity.AimEntNow"/> for an aligned merge — not merged without fast-cull, or not posed here, is null —
    /// with the follow's builds it made, for the caller to count where they belong (B243, B69 review H): the draw's lighting in
    /// <see cref="BuildsDrawing"/>, an effect's <see cref="AbsOrigin"/> as a query from outside the pass. The one owner of the
    /// question for both (B69 review).
    /// </summary>
    private float[]? AimEntOf(SceneProp prop, out int built)
    {
        built = 0;

        if (prop is not { BoneMerged: true, BoneMergeFastCull: false } ||
            !_entities.TryGetValue(prop.EntityIndex, out AnimatingEntity? animating))
        {
            return null;
        }

        int builds = SkeletonPose.PoseBuilds;
        float[]? aimed = animating.AimEntNow(_simulatedSeconds);

        built = SkeletonPose.PoseBuilds - builds;

        return aimed;
    }

    /// <summary><see cref="AimEntOf"/> for the draw's lighting, its builds counted in <see cref="BuildsDrawing"/> (B243).</summary>
    private float[]? AimEntDrawn(SceneProp prop)
    {
        float[]? aimed = AimEntOf(prop, out int built);

        BuildsDrawing += built;

        return aimed;
    }

    /// <summary>Where a model's light should be sampled, in world space.</summary>
    /// <param name="prop">The prop; its model must have been added.</param>
    /// <param name="pose">Where it stands.</param>
    /// <returns>The point. Public so a probe asks the set itself rather than repeating its rule (`prop-light`).</returns>
    /// <exception cref="ArgumentNullException"><paramref name="prop"/> is null.</exception>
    public (float X, float Y, float Z) IlluminationPoint(SceneProp prop, ScenePose pose)
    {
        ArgumentNullException.ThrowIfNull(prop);

        // **An owned econ item is lit at its OWNER's `WorldSpaceCenter`** (B69 f): "Correct the ambient lighting position to
        // match our owner entity" — `pInfo->pLightingOrigin = &GetOwnerEntity()->WorldSpaceCenter()`
        // (`CEconEntity::OnInternalDrawModel`, econ_entity.cpp:1949-1962). The owner's centre is his collision box's middle
        // at his abs origin (a player's hull, `PlayerHull`), so no bone of the item or the owner is read.
        if (prop is { ItemDefinitionIndex: not null, OwnedBy: { } owner } && ParentOf(owner) is { } holder)
        {
            Vector3 centre = WorldSpaceCenter.Of(Absolute(holder, AnimatingEntity.MaximumFollowDepth), holder.Collision);

            return (centre.X, centre.Y, centre.Z);
        }

        // **An aligned merge that is not an owned econ item is lit at its own aim-ent origin** (B69): only
        // `CEconEntity::OnInternalDrawModel` moves the lighting origin (above); anything else is lit at its render origin, which
        // `MoveToAimEnt` made the aim-ent's (c_baseentity.cpp:4294-4301) — a corpse's worn gib, a sapper on its building. At
        // draw only. **This frame's aim-ent, not the last setup's** (B69 f): `GetAimEntOrigin` as `CalcAimEntPositions` asks it
        // before the draw, setting the FOLLOW's bones up and never the item's own (`AnimatingEntity.AimEntNow`) — the item's own
        // setup here, ahead of the draw's, rolled its mask (AttachmentQueryTests).
        if (!_simulating && AimEntDrawn(prop) is { } aimed)
        {
            (float ix, float iy, float iz) = _frames.TryGetValue(prop.ModelPath, out PropModels.ModelFrames? own)
                ? own.Illumination
                : (0f, 0f, 0f);

            return (
                (aimed[0] * ix) + (aimed[1] * iy) + (aimed[2] * iz) + aimed[3],
                (aimed[4] * ix) + (aimed[5] * iy) + (aimed[6] * iz) + aimed[7],
                (aimed[8] * ix) + (aimed[9] * iy) + (aimed[10] * iz) + aimed[11]);
        }

        // **A bone-merged item is lit where its WEARER is, for every quantity at once** (B189; the
        // outside audit's finding 6). Its own pose is (0,0,0) by construction — `FollowEntity`
        // zeroes it and the client takes the parent's bones outright — so its own point is the map
        // origin, whose leaf is usually solid and lightless. This used to be patched downstream:
        // an override in the draw loop replaced the cube, the lamps and the reflection origin with
        // wearer-point samples but NOT the sun, so every cosmetic's direct light was a sky ray
        // traced from the map origin; and the override went past `ModelLighting.For`'s cache, so
        // it re-traced per item per frame. Answering the wearer's point HERE sends all four
        // quantities through the one sampler and its exact-point cache — one place or it drifts.
        if (prop is { AttachedTo: { } wearer, BoneMerged: true } &&
            _lightPoints.TryGetValue(wearer, out (float X, float Y, float Z) worn))
        {
            return worn;
        }

        // A flagged static prop's lump point replaces the illumination point outright (B427).
        if (prop.LightingOrigin is { } origin)
        {
            return origin;
        }

        // **A move child's pose is LOCAL, so it is lit where the chain puts it** (B551). The wire sends
        // `GetLocalOrigin()` (`server/baseentity.cpp:199`) and the engine samples light at the absolute place
        // `CalcAbsolutePosition` composes (`c_baseentity.cpp:4401`); lighting a `func_door`'s grate at its offset lit it
        // from wherever that offset happens to fall in the map.
        if (prop is { AttachedTo: not null, BoneMerged: false })
        {
            pose = Absolute(prop, AnimatingEntity.MaximumFollowDepth);
        }

        if (!_frames.TryGetValue(prop.ModelPath, out PropModels.ModelFrames? entry))
        {
            return (pose.X, pose.Y, pose.Z);
        }

        (float x, float y, float z) = entry.Illumination;

        if (x == 0f && y == 0f && z == 0f)
        {
            return (pose.X, pose.Y, pose.Z);
        }

        // The whole AngleMatrix of the placement (B682): logged from the running engine, `2pipe_straight_64` at angles (0 270 -90) with
        // its offset (0 0 0.5) is lit half a unit along +x, not above itself.
        (Vector3 forward, Vector3 left, Vector3 up) = StaticPropCollision.AngleMatrix(pose.Pitch, pose.Yaw, pose.Roll);

        return (
            pose.X + (x * forward.X) + (y * left.X) + (z * up.X),
            pose.Y + (x * forward.Y) + (y * left.Y) + (z * up.Y),
            pose.Z + (x * forward.Z) + (y * left.Z) + (z * up.Z));
    }
}
