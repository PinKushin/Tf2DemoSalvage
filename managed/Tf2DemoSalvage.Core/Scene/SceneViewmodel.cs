namespace Tf2DemoSalvage.Core.Scene;

/// <summary>
/// The weapon model a player sees in their own hands.
/// </summary>
/// <param name="ModelPath">What to draw, as <c>modelprecache</c> named it.</param>
/// <param name="Sequence">Which animation it is playing.</param>
/// <param name="PlaybackRate">How fast, which is the third factor in the cycle advance.</param>
/// <param name="OwnerEntityIndex">
/// Whose it is, or <c>null</c> on a point-of-view recording where the demo does not say.
/// </param>
/// <remarks>
/// **Not a <see cref="SceneProp"/>, because it has nowhere to be.** A viewmodel's table is
/// declared <c>BEGIN_NETWORK_TABLE_NOBASE</c>, so it inherits no origin and no angles at all — the
/// demo names the model and the pose and the client puts it at the camera. Everything else in a
/// scene has a position; a model with none would be a prop every consumer had to special-case.
///
/// **The owner is null on most demos and that is correct rather than missing.** Measured across
/// the corpus: a point-of-view recording carries exactly one viewmodel and never names an owner,
/// because you only ever receive your own. A modern SourceTV recording carries one per player and
/// names every one. See <c>docs/findings/04-entities.md</c>.
/// </remarks>
public readonly record struct SceneViewmodel(
    string ModelPath,
    int Sequence,
    float PlaybackRate,
    int? OwnerEntityIndex);
