using System;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Core.Scene;

/// <summary>What a model's studio header says that `C_BaseAnimating::OnNewModel` and the interpolator read (B870).</summary>
/// <param name="PoseParameterLoops">
/// One entry per pose parameter the model declares, in its order, true where `Pose.loop != 0.0f`. Its COUNT is a fact of
/// its own: `m_iv_flPoseParameter.SetMaxCount( hdr->GetNumPoseParameters() )` (`c_baseanimating.cpp:1124`).
/// </param>
/// <param name="StaticProp">Whether the header carries `STUDIOHDR_FLAGS_STATIC_PROP` (`c_baseanimating.cpp:4745`).</param>
/// <param name="SequenceLoops">
/// Whether a sequence loops as the model plays it — `IsSequenceLooping` (`c_baseanimating.cpp:4471-4472`); null when the
/// model cannot say, which is the engine's no-model answer, nothing loops.
/// </param>
public sealed record ModelFacts(IReadOnlyList<bool> PoseParameterLoops, bool StaticProp, Func<int, bool>? SequenceLoops);

/// <summary>Answers <see cref="ModelFacts"/> for a model path, which only the game's own files can (B870).</summary>
/// <remarks>
/// **Handed to the build because the engine reads these when the recording SETS the model** — `OnNewModel` runs inside the
/// update that changed it (`c_baseentity.cpp:2609`, `:1782-1791`) — and Core cannot open a model. The install implements
/// it, as it does <see cref="IClassAnimationScripts"/>.
/// </remarks>
public interface IModelFacts
{
    /// <summary>The model's facts, or null when the install has none for it — the file is absent, unreadable or not a
    /// studio model. The build tells the two kinds of null apart by path (B870): a brush model or a sprite has no studio
    /// header, which is the engine's early return before `SetMaxCount` (`c_baseanimating.cpp:1042-1043`); a `.mdl` without
    /// facts is unknown (B873).</summary>
    /// <param name="modelPath">The model, as the precache table names it.</param>
    /// <returns>The facts, or null.</returns>
    public ModelFacts? FactsOf(string modelPath);
}
