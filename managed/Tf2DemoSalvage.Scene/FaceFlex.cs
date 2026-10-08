using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene;

/// <summary>
/// The pure halves of a face (B513): a model's controllers out of an actor's global weights, the rules and the delay,
/// and the vertex deltas in buffer order.
/// </summary>
public static class FaceFlex
{
    /// <summary><c>flex_smooth</c>'s decay: <c>ExponentialDecay( 0.8, 0.033, d )</c> (<c>c_baseflex.cpp:1271</c>).</summary>
    private const float DelayDecayTo = 0.8f;

    private const float DelayDecayTime = 0.033f;

    /// <summary>A model's controller values out of the actor's global list, by name.</summary>
    /// <param name="data">The model's tables.</param>
    /// <param name="global">The actor's <c>g_flexweight</c>, keyed by controller name; null for no actor.</param>
    /// <returns>One value per local controller — a name the actor never set reads 0, as a zeroed global slot does.</returns>
    /// <remarks>
    /// <c>RunFlexRules</c> reads <c>src[ pFlexcontroller( i )-&gt;localToGlobal ]</c> and the global list is keyed by
    /// name (<c>AddGlobalFlexController</c>), so a worn item's controller named like its wearer's reads the wearer's
    /// value (<c>CEconEntity::SetupWeights</c>, <c>econ_entity.cpp:1377</c>).
    /// </remarks>
    public static float[] Local(StudioFlexData data, IReadOnlyDictionary<string, float>? global)
    {
        ArgumentNullException.ThrowIfNull(data);

        float[] src = new float[data.Controllers.Count];

        if (global is null)
        {
            return src;
        }

        for (int index = 0; index < src.Length; index++)
        {
            src[index] = global.TryGetValue(data.Controllers[index].Name, out float value) ? value : 0f;
        }

        return src;
    }

    /// <summary><c>C_BaseFlex::RunFlexDelay</c> (<c>c_baseflex.cpp:1265</c>).</summary>
    /// <param name="weights">The descriptor weights this frame.</param>
    /// <param name="delayed">The delayed weights, updated in place.</param>
    /// <param name="delayTime"><c>m_flFlexDelayTime</c>, updated.</param>
    /// <param name="curtime">Now.</param>
    /// <param name="frametime">This frame's length.</param>
    public static void Delay(ReadOnlySpan<float> weights, Span<float> delayed, ref double delayTime, double curtime, double frametime)
    {
        if (delayTime > 0d && delayTime < curtime)
        {
            float d = (float)Math.Clamp(curtime - delayTime, 0d, frametime);
            d = MathF.Exp(MathF.Log(DelayDecayTo) / DelayDecayTime * d);

            for (int index = 0; index < weights.Length && index < delayed.Length; index++)
            {
                delayed[index] = (delayed[index] * d) + (weights[index] * (1f - d));
            }
        }

        delayTime = curtime;
    }

    /// <summary>The per-<c>.vvd</c>-vertex deltas the weights produce, or null when they move nothing.</summary>
    /// <param name="data">The model's tables.</param>
    /// <param name="weights">Descriptor weights.</param>
    /// <param name="delayed">Delayed descriptor weights.</param>
    /// <param name="vertexCount">How many vertices the model's <c>.vvd</c> holds.</param>
    /// <returns>Position deltas then normal deltas, three floats per vertex each, or null.</returns>
    public static (float[] Positions, float[] Normals)? Deltas(
        StudioFlexData data, float[] weights, float[] delayed, int vertexCount)
    {
        ArgumentNullException.ThrowIfNull(data);

        float[] positions = new float[vertexCount * 3];
        float[] normals = new float[vertexCount * 3];

        return StudioFlexRules.Accumulate(data, weights, delayed, positions, normals) > 0 ? (positions, normals) : null;
    }

    /// <summary><c>RunFlexRules</c> over local controller values.</summary>
    /// <param name="data">The model's tables.</param>
    /// <param name="src">From <see cref="Local"/>.</param>
    /// <returns>One weight per descriptor.</returns>
    public static float[] Rules(StudioFlexData data, float[] src)
    {
        ArgumentNullException.ThrowIfNull(data);

        float[] weights = new float[data.Descriptors.Count];
        StudioFlexRules.Run(data, src, weights);
        return weights;
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

/// <summary>
/// One TF2 player's <c>C_BaseFlex</c> state, stepped once per frame: <c>SetupGlobalWeights</c> as it runs for a player
/// (B513).
/// </summary>
/// <remarks>
/// **Each frame, in the engine's order** (<c>c_baseflex.cpp:1189-1262</c>): every controller's <c>m_flexWeight</c>
/// decays by 0.95 and the active <c>FLEXANIMATION</c> tracks write over it (<c>ProcessSceneEvents( true )</c>, <c>:1703</c>,
/// <c>:1950</c>); the global weights are the controllers rescaled (<c>:1222</c>); the active <c>EXPRESSION</c>s blend in
/// (<c>:1882</c>); the blink never fires for a TF player (its toggle is not sent); the visemes add in (<c>:922</c>).
///
/// **A TF player's controllers start at zero in their own range, not at their minimum**: <c>C_TFPlayer::OnNewModel</c> and
/// <c>CleanUpAnimationOnSpawn</c> call <c>ResetFlexWeights</c>, which is <c>SetFlexWeight( i, 0.0f )</c> — normalised
/// against the range, so a −1..1 controller rests at 0.5 (<c>c_tf_player.cpp:5291-5301</c>, <c>c_baseflex.cpp:1632</c>).
///
/// **Frame-rate state is kept as the engine keeps it**: the decay is per call, and a scene event is active while the
/// scene's clock at the PREVIOUS frame was inside it (<c>CChoreoScene::EventThink</c> tests <c>frame_start_time</c>,
/// <c>choreoscene.cpp:2514-2529</c>) while its value is read at this frame's time.
/// </remarks>
public sealed class ActorFace
{
    /// <summary><c>g_CV_PhonemeFilter</c>, "phonemefilter" 0.08 (<c>c_baseflex.cpp:30</c>).</summary>
    private const float PhonemeFilter = 0.08f;

    /// <summary><c>STRONG_CROSSFADE_START</c>, <c>c_baseflex.cpp:654</c>.</summary>
    private const float StrongCrossfadeStart = 0.60f;

    /// <summary><c>WEAK_CROSSFADE_START</c>, <c>c_baseflex.cpp:655</c>.</summary>
    private const float WeakCrossfadeStart = 0.40f;

    private readonly StudioFlexData _data;
    private readonly float[] _flexWeight;
    private readonly Dictionary<double, float> _previousSceneTime = [];
    private readonly Dictionary<double, float> _sceneTime = [];
    private readonly HashSet<int> _callers = [];
    private readonly Dictionary<string, float> _global = new(StringComparer.OrdinalIgnoreCase);
    private readonly FlexSettings?[] _phonemeClasses = new FlexSettings?[3];

    /// <summary>A player's face, reset as <c>OnNewModel</c> leaves it.</summary>
    /// <param name="data">The player model's flex tables.</param>
    /// <param name="modelName">The model's internal name (<c>pszName</c>), which the phoneme files are named from.</param>
    /// <param name="sources">Where expression files come from.</param>
    public ActorFace(StudioFlexData data, string modelName, FaceSources? sources)
    {
        _data = data ?? throw new ArgumentNullException(nameof(data));
        _flexWeight = new float[data.Controllers.Count];

        for (int index = 0; index < _flexWeight.Length; index++)
        {
            Set(index, 0f);
        }

        if (sources is not null)
        {
            // `C_TFPlayer::InitPhonemeMappings`: "<model without extension>/phonemes/phonemes", else plain "phonemes"
            // (c_tf_player.cpp:5268), and its _weak and _strong siblings (c_baseflex.cpp:173).
            string stem = System.IO.Path.ChangeExtension(modelName ?? string.Empty, null).Replace('\\', '/');
            string root = sources.Expression(stem + "/phonemes/phonemes") is not null ? stem + "/phonemes/phonemes" : "phonemes";

            _phonemeClasses[0] = sources.Expression(root);
            _phonemeClasses[1] = sources.Expression(root + "_weak");
            _phonemeClasses[2] = sources.Expression(root + "_strong");
        }
    }

    /// <summary>The demo seconds of the last step, or NaN before the first.</summary>
    public double LastSeconds { get; private set; } = double.NaN;

    /// <summary>This frame's length, as the last step measured it.</summary>
    public double FrameTime { get; private set; }

    /// <summary>The global weights the last step left — <c>g_flexweight</c> by controller name.</summary>
    public IReadOnlyDictionary<string, float> Global => _global;

    /// <summary>The normalised controller value a test can read back, <c>m_flexWeight[ index ]</c>.</summary>
    /// <param name="index">A local controller.</param>
    /// <returns>0 to 1.</returns>
    public float FlexWeight(int index) => _flexWeight[index];

    /// <summary>
    /// One <c>SetupGlobalWeights</c> call — once per drawing entity per frame. The face itself, each bone-merged flex
    /// wearable (<c>CEconEntity::SetupWeights</c>) and a dead player's corpse (<c>C_TFRagdoll::SetupWeights</c>) each
    /// call it, and each call decays the controllers again, so the decay runs once per such draw.
    /// </summary>
    /// <param name="seconds">Demo seconds now.</param>
    /// <param name="caller">The drawing entity; a second call from it in one frame changes nothing.</param>
    /// <param name="scenes">The scenes the actor is in.</param>
    /// <param name="voices">The mouth's voice sources.</param>
    public void Step(double seconds, int caller, IReadOnlyList<FaceScene> scenes, IReadOnlyList<FaceVoice> voices)
    {
        ArgumentNullException.ThrowIfNull(scenes);
        ArgumentNullException.ThrowIfNull(voices);

        if (double.IsNaN(LastSeconds) || seconds < LastSeconds || seconds > LastSeconds)
        {
            // A new frame: the scenes' clocks of the frame before become the previous ones `EventThink` tests.
            FrameTime = double.IsNaN(LastSeconds) ? 0d : seconds - LastSeconds;

            foreach ((double started, float now) in _sceneTime)
            {
                _previousSceneTime[started] = now;
            }

            _sceneTime.Clear();
            _callers.Clear();
            LastSeconds = seconds;
        }

        if (!_callers.Add(caller))
        {
            return;
        }

        List<(FaceScene Scene, float Now, float Previous)> clocks = [];

        foreach (FaceScene scene in scenes)
        {
            float now = scene.Plan.TimeAt((float)(seconds - scene.StartedSeconds));
            // A scene first seen this frame — its first frame, or the first after a seek — has no earlier clock; its
            // own is taken, as the frame before would have been at most a frame behind.
            float previous = _previousSceneTime.TryGetValue(scene.StartedSeconds, out float before) ? before : now;

            _sceneTime[scene.StartedSeconds] = now;
            clocks.Add((scene, now, previous));
        }

        // ProcessSceneEvents( true ): decay, then the flex animations.
        for (int index = 0; index < _flexWeight.Length; index++)
        {
            Set(index, (float)(Get(index) * 0.95d));
        }

        foreach ((FaceScene scene, float now, float previous) in clocks)
        {
            float global = SceneRamp(scene.Plan, now);

            foreach (SceneFlexAnimation animation in scene.Plan.FlexAnimations)
            {
                if (previous < animation.Start || previous > animation.End)
                {
                    continue;
                }

                float weight = global * ChoreoCurve.Intensity(
                    animation.Ramp, now - animation.Start, animation.End - animation.Start, hasEndTime: true);

                AddFlexAnimation(animation, now, weight);
            }
        }

        // The networked weights, rescaled into the global list.
        _global.Clear();

        for (int index = 0; index < _flexWeight.Length; index++)
        {
            _global[_data.Controllers[index].Name] = Get(index);
        }

        // ProcessSceneEvents( false ): the expressions.
        foreach ((FaceScene scene, float now, float previous) in clocks)
        {
            float global = SceneRamp(scene.Plan, now);

            foreach (SceneExpression expression in scene.Plan.Expressions)
            {
                if (previous < expression.Start || previous > expression.End)
                {
                    continue;
                }

                float scale = global * ChoreoCurve.Intensity(
                    expression.Ramp, now - expression.Start, expression.End - expression.Start, hasEndTime: true);

                foreach (SceneExpressionWeight weight in expression.Weights)
                {
                    float s = Math.Clamp(scale * weight.Influence, 0f, 1f);
                    float was = _global.TryGetValue(weight.Controller, out float value) ? value : 0f;

                    _global[weight.Controller] = (was * (1f - s)) + (weight.Weight * s);
                }
            }
        }

        ProcessVisemes(seconds, voices);
    }

    /// <summary>The scene's own ramp at its clock, <c>GetSceneRampIntensity</c>.</summary>
    private static float SceneRamp(SceneTaunt plan, float now) =>
        ChoreoCurve.Intensity(plan.SceneRamp, now, plan.Duration, hasEndTime: true);

    /// <summary><c>C_BaseFlex::AddFlexAnimation</c> (<c>c_baseflex.cpp:1950</c>).</summary>
    /// <remarks>
    /// **A track whose controller the model lacks writes controller 0**: the lookup is
    /// <c>MAX( FindFlexController( name ), LocalFlexController_t( 0 ) )</c> (<c>:1978</c>, <c>:1987</c>) — a −1 becomes 0, and
    /// <c>if ( controller &gt;= 0 )</c> then lets it through.
    /// </remarks>
    private void AddFlexAnimation(SceneFlexAnimation animation, float now, float weight)
    {
        foreach (SceneFlexTrack track in animation.Tracks)
        {
            if (!track.Active)
            {
                continue;
            }

            if (track.Combo)
            {
                for (int side = 0; side < 2; side++)
                {
                    // side 0 is the right_ controller, side 1 the left_ (c_baseflex.cpp:1975-1983).
                    string name = (side == 0 ? "right_" : "left_") + track.Controller;
                    int controller = Math.Max(_data.FindController(name), 0);
                    Blend(controller, ChoreoCurve.TrackIntensity(track, animation.Start, animation.End, now, side), weight);
                }
            }
            else
            {
                int controller = Math.Max(_data.FindController(track.Controller), 0);
                Blend(controller, ChoreoCurve.TrackIntensity(track, animation.Start, animation.End, now, 0), weight);
            }
        }
    }

    private void Blend(int controller, float intensity, float weight)
    {
        if (controller >= _flexWeight.Length)
        {
            return;
        }

        float orig = Get(controller);
        Set(controller, (orig * (1f - weight)) + (intensity * weight));
    }

    /// <summary><c>C_BaseFlex::ProcessVisemes</c> (<c>c_baseflex.cpp:922</c>) over the mouth's sources.</summary>
    private void ProcessVisemes(double seconds, IReadOnlyList<FaceVoice> voices)
    {
        foreach (FaceVoice voice in voices)
        {
            if (voice.IgnorePhonemes)
            {
                continue;
            }

            float length = voice.Sentence.Length;
            float elapsed = (float)(seconds - voice.StartedSeconds);

            if (elapsed >= length + 2.0f)
            {
                continue;
            }

            // phonemedelay is 0 (c_baseflex.cpp:29).
            float t = elapsed;
            float emphasis = voice.Sentence.Intensity(t, length);

            AddVisemesForSentence(emphasis, voice.Sentence, t, PhonemeFilter);
        }
    }

    /// <summary><c>AddVisemesForSentence</c> (<c>c_baseflex.cpp:828</c>) at LOD 0, where <c>phonemesnap</c> 2 crossfades.</summary>
    private void AddVisemesForSentence(float emphasis, Sentence sentence, float t, float dt)
    {
        IReadOnlyList<SentencePhoneme> phonemes = sentence.Phonemes;

        for (int k = 0; k < phonemes.Count; k++)
        {
            SentencePhoneme phoneme = phonemes[k];

            if (t > phoneme.Start && t < phoneme.End && k < phonemes.Count - 1)
            {
                SentencePhoneme next = phonemes[k + 1];

#pragma warning disable S1244 // `next->GetStartTime() == phoneme->GetEndTime()`, the SDK's exact compare.
                dt = next.Start == phoneme.End
                    ? MathF.Max(dt, MathF.Min(next.End - t, phoneme.End - phoneme.Start))
                    : MathF.Max(dt, MathF.Min(next.Start - t, phoneme.End - phoneme.Start));
#pragma warning restore S1244
            }

            float t1 = (phoneme.Start - t) / dt;
            float t2 = (phoneme.End - t) / dt;

            if (t1 < 1.0 && t2 > 0)
            {
                t2 = MathF.Min(t2, 1f);
                t1 = MathF.Max(t1, 0f);

                AddViseme(emphasis, phoneme.Code, t2 - t1);
            }
        }
    }

    /// <summary><c>AddViseme</c>, <c>SetupEmphasisBlend</c> and <c>ComputeBlendedSetting</c> (<c>c_baseflex.cpp:667-817</c>).</summary>
    private void AddViseme(float emphasis, int phoneme, float scale)
    {
        // SetupEmphasisBlend: the normal class is required, the others optional.
        IReadOnlyList<FlexSettingWeight>?[] settings = new IReadOnlyList<FlexSettingWeight>?[3];

        for (int type = 0; type < 3; type++)
        {
            settings[type] = _phonemeClasses[type]?.IndexedSetting(phoneme);
        }

        if (settings[0] is null)
        {
            return;
        }

        float[] amounts = Blended(emphasis, settings[1] is not null, settings[2] is not null);

        for (int type = 0; type < 3; type++)
        {
            // `if ( !info->valid || info->amount == 0.0f ) continue;`
#pragma warning disable S1244
            if (settings[type] is not { } weights || amounts[type] == 0f)
#pragma warning restore S1244
            {
                continue;
            }

            foreach (FlexSettingWeight weight in weights)
            {
                float was = _global.TryGetValue(weight.Controller, out float value) ? value : 0f;
                _global[weight.Controller] = was + (amounts[type] * scale * weight.Weight);
            }
        }
    }

    /// <summary><c>ComputeBlendedSetting</c>: amounts for normal, weak, strong.</summary>
    private static float[] Blended(float emphasis, bool hasWeak, bool hasStrong)
    {
        float[] amount = new float[3];

        if (emphasis > StrongCrossfadeStart)
        {
            if (hasStrong)
            {
                float frac = (1.0f - emphasis) / (1.0f - StrongCrossfadeStart);
                amount[0] = frac * 2.0f * StrongCrossfadeStart;
                amount[2] = 1.0f - frac;
            }
            else
            {
                amount[0] = 2.0f * MathF.Min(emphasis, StrongCrossfadeStart);
            }
        }
        else if (emphasis < WeakCrossfadeStart)
        {
            if (hasWeak)
            {
                float frac = (WeakCrossfadeStart - emphasis) / WeakCrossfadeStart;
                amount[0] = (1.0f - frac) * 2.0f * WeakCrossfadeStart;
                amount[1] = frac;
            }
            else
            {
                amount[0] = 2.0f * MathF.Max(emphasis, WeakCrossfadeStart);
            }
        }
        else
        {
            amount[0] = 2.0f * emphasis;
        }

        return amount;
    }

    /// <summary><c>GetFlexWeight</c>: the stored 0..1 value in the controller's range.</summary>
    private float Get(int index)
    {
        StudioFlexController controller = _data.Controllers[index];

#pragma warning disable S1244 // `max != min`, as the SDK writes it.
        return controller.Max != controller.Min
            ? (_flexWeight[index] * (controller.Max - controller.Min)) + controller.Min
            : _flexWeight[index];
#pragma warning restore S1244
    }

    /// <summary><c>SetFlexWeight</c>: normalised into 0..1 and clamped.</summary>
    private void Set(int index, float value)
    {
        StudioFlexController controller = _data.Controllers[index];

#pragma warning disable S1244
        if (controller.Max != controller.Min)
#pragma warning restore S1244
        {
            value = Math.Clamp((value - controller.Min) / (controller.Max - controller.Min), 0f, 1f);
        }

        _flexWeight[index] = value;
    }

    /// <summary>The scenes' clocks a test can read back.</summary>
    /// <returns>Scene start (demo seconds) to the scene time it was at last step.</returns>
    public IReadOnlyDictionary<double, float> SceneTimes() => _previousSceneTime.ToDictionary(p => p.Key, p => p.Value);
}
