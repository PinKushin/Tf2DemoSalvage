using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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

        if (model is null || !model.Flex.HasVertexAnimation || model.CornerVertex.Count == 0 ||
            !_packedOrder.TryGetValue(prop.ModelPath, out int[]? order))
        {
            return (null, null);
        }

        tally.Faces++;
        float[]? face;
        long drivingAt = Stopwatch.GetTimestamp();
        IReadOnlyDictionary<string, float>? global = FaceDriving(prop, seconds);
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

                // `FaceFlex.Deltas` taken apart so its two halves are timed apart: the clear, then the accumulation.
                (float[] positions, float[] normals) = _flexScratch;
                Array.Clear(positions);
                Array.Clear(normals);
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
        int[] at = FlexedCornersOf(prop.ModelPath, model, order).At;
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
            // `( pPlayer && pPlayer->IsAlive() ) || !pPlayer` takes the corpse's own face (c_tf_player.cpp:630).
            if (PlayerAlive?.Invoke(dead) == true)
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
    /// Each baked static prop's `.vhv` colours, by entity index — red, green, blue per corner of its model's
    /// geometry, overbright already applied — set when a map is read (B426).
    /// </summary>
    /// <remarks>
    /// `engine.dll` `0x1800f1bd0`: a static prop with baked colours is drawn with its per-placement colour
    /// mesh and no ambient cube and no local lights. An entity found here draws that way; one that is not
    /// is lit per draw.
    /// </remarks>
    public IReadOnlyDictionary<int, float[]> StaticPropColours
    {
        get;
        set
        {
            field = value;
            _bakedByEntity.Clear();
        }
    } = new Dictionary<int, float[]>();

    /// <summary>
    /// The styled and dynamic lights a static prop with a colour mesh draws on top of its colours, by entity and lighting
    /// origin (<see cref="LevelLighting.StaticPlusDynamicLights"/>, B424, B425); null gives it none.
    /// </summary>
    public Func<int, float, float, float, IReadOnlyList<LocalLight>>? StaticPlusDynamicLights { get; set; }

    /// <summary>
    /// The whole lighting state of a static prop drawn without a colour mesh, by entity and lighting origin
    /// (<see cref="LevelLighting.StaticPropFullLightingAt"/>, B514); null leaves it the model path's light.
    /// </summary>
    public Func<int, float, float, float, (PointLighting Lighting, SunLight? Sun)>? StaticPropFullLighting { get; set; }

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
    /// Builds one static prop's light cache handle at its lighting origin, whether to build its static state too
    /// (<see cref="LevelLighting.PrecacheStaticProp"/>); null builds none ahead of the draw.
    /// </summary>
    public Action<int, float, float, float, bool>? StaticPropHandle { get; set; }

    /// <summary>
    /// Every static prop's lighting, built as its level loads (B607): `CStaticPropMgr::PrecacheLighting` (`engine.dll`
    /// `0x180205b20`) builds each prop's light cache handle (`FUN_1801b8350`), and the colour mesh is built with the prop.
    /// </summary>
    /// <remarks>
    /// Run once the static props are packed, since the lighting origin reads the model's `illumposition`. Each prop takes
    /// the draw's own route — <see cref="IlluminationPoint"/> at its pose, the draw's colour-mesh test — so the draw finds
    /// what was built here under the same key. Only what a draw reads is built: the colour mesh, the handle of a prop
    /// whose draw reads one (a colour mesh's flags 6, a bump-mapped model's flags 7), and the static state for flags 7.
    /// </remarks>
    private void PrecacheStaticLighting()
    {
        foreach (SceneProp prop in _staticProps)
        {
            if (!CanDraw(prop) || prop.EntityIndex < PropModels.FirstStaticPropEntityIndex)
            {
                continue;
            }

            ScenePose pose = prop.Pose;
            (float x, float y, float z) = IlluminationPoint(prop, pose);

            bool fullyLit = ResolvedFor(prop).Frames is { } frames && StaticPropVertexLighting.RefusesColourMesh(frames.StudioFlags);

            if (fullyLit || BakedColoursOf(prop, pose, x, y, z) is not null)
            {
                StaticPropHandle?.Invoke(prop.EntityIndex, x, y, z, fullyLit);
            }
        }
    }

    /// <summary>Where a model's light should be sampled, in world space.</summary>
    /// <param name="prop">The prop; its model must have been added.</param>
    /// <param name="pose">Where it stands.</param>
    /// <returns>The point. Public so a probe asks the set itself rather than repeating its rule (`prop-light`).</returns>
    /// <exception cref="ArgumentNullException"><paramref name="prop"/> is null.</exception>
    public (float X, float Y, float Z) IlluminationPoint(SceneProp prop, ScenePose pose)
    {
        ArgumentNullException.ThrowIfNull(prop);

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

        float radians = pose.Yaw * (MathF.PI / 180f);
        (float sine, float cosine) = MathF.SinCos(radians);

        return (
            pose.X + (x * cosine) - (y * sine),
            pose.Y + (x * sine) + (y * cosine),
            pose.Z + z);
    }
}
