using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene;

/// <summary>Everything a scene rebuild is TOLD, rather than reaching back for.</summary>
/// <param name="Tick">The moment being drawn, fractional so animation advances between ticks.</param>
/// <param name="CurrentTick">The whole tick the transport sits on, which entity lookups key off.</param>
/// <param name="FirstPerson">Whether the view is through a player's eyes.</param>
/// <param name="Followed">Which entity that is, when one is followed.</param>
/// <param name="EyeCamera">Where that eye is, or null when there is none to draw from.</param>
/// <param name="IntervalPerTick">Seconds per tick, from the demo rather than an assumed 66.67.</param>
/// <param name="ViewmodelFieldOfView">The first-person weapon's own FOV, which is not the world's.</param>
/// <remarks>
/// **This is <c>SetupRenderInfo_t</c>'s shape, and the shape is the point.** Valve's renderables-list
/// builder takes one:
///
/// <code>
/// struct SetupRenderInfo_t
/// {
///     WorldListInfo_t *m_pWorldListInfo;
///     CClientRenderablesList *m_pRenderList;
///     Vector m_vecRenderOrigin;
///     Vector m_vecRenderForward;
///     int m_nRenderFrame;
///     ...
/// };
///
/// virtual void BuildRenderablesList( const SetupRenderInfo_t &amp;info ) = 0;
/// </code>
///
/// — <c>clientleafsystem.h:75</c> and <c>:169</c>. The builder is **told** where the camera is and
/// which frame it is; it does not hold a pointer to the window and ask. <c>MainForm.ShowMoment</c>
/// did the opposite: it read the camera mode, the followed entity, the transport's tick and the
/// settings straight off the form, and that coupling is the whole reason a scene rebuild needed a
/// window at all (B188, D90).
///
/// **What is deliberately NOT here: the render list.** Valve passes one in because its leaf system
/// is stateless between frames; ours keeps the packed set and the instance lists alive across
/// frames, because packing an MDL is expensive and the set stops growing seconds into playback.
/// Stated because it is a real difference rather than an oversight.
///
/// **Nor the followed player's arms and weapon**, which were fields here for one commit. They came
/// from the window computing two lookups to fill them in — a shim in everything but name. The scene
/// already holds the roster this moment sampled, so it finds the followed player and asks
/// <see cref="IPlayerAppearance.Hands"/> and <see cref="WeaponModels"/> itself. **A parameter that
/// exists because the CALLER happened to know the answer is the coupling this record was created to
/// remove.**
/// </remarks>
public readonly record struct MomentInfo(
    double Tick,
    int CurrentTick,
    bool FirstPerson,
    int? Followed,
    FreeCamera? EyeCamera,
    float IntervalPerTick,
    float ViewmodelFieldOfView)
{
    /// <summary>How far into the demo this moment is, in seconds.</summary>
    /// <remarks>
    /// **Demo time, from the demo's own tick interval rather than an assumed 66.67.** The cycle of
    /// an animation is advanced by elapsed time, the way the client advances it in
    /// <c>C_BaseAnimating::FrameAdvance</c> — the server never sends one, so a viewer replaying only
    /// what was networked leaves every health pack frozen on its first frame.
    /// </remarks>
    public double Seconds =>
        Tick * (IntervalPerTick > 0f ? IntervalPerTick : PlaybackClock.DefaultIntervalPerTick);
}
