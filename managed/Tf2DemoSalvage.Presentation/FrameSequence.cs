using System;
using System.Collections.Generic;
using System.Diagnostics;

using Tf2DemoSalvage.Render;

namespace Tf2DemoSalvage.Presentation;

/// <summary>The work one frame is made of, as the frame sequencer sees it.</summary>
/// <remarks>
/// **Events out, methods in**, matching <see cref="IPlaybackView"/>. Each member is one stage of
/// Valve's client frame; none of them decides when it runs.
///
/// **Named for Valve's stages rather than for our methods**, so the parity claim is checkable by
/// reading. `Simulate` is `IGameSystem::UpdateAllSystems`, `PlaceCamera` is `CalcView` plus
/// `ComputeCameraVariables`, `UpdateListener` is `engine->SetAudioState`, and `ProjectWorld` is
/// `SetupVis`.
/// </remarks>
public interface IFrameSteps
{
    /// <summary>Advance the world to the moment this frame shows.</summary>
    /// <remarks>Valve: `IGameSystem::UpdateAllSystems( frametime )`, `cdll_client_int.cpp:1308`.</remarks>
    public void Simulate();

    /// <summary>Work out where the eye is and hand the camera to the GPU.</summary>
    /// <remarks>Valve: `ComputeCameraVariables( viewEye.origin, viewEye.angles, ... )`, `view.cpp:779`.</remarks>
    public void PlaceCamera();

    /// <summary>Put the ears where the eye is, and play what is due.</summary>
    /// <remarks>Valve: `engine->SetAudioState( audioState )`, `view.cpp:796`.</remarks>
    public void UpdateListener();

    /// <summary>Rebuild the projected world, if anything invalidated it.</summary>
    /// <remarks>Valve: `SetupVis( viewRender, visFlags, pCustomVisibility )`, `viewrender.cpp:1415`.</remarks>
    public void ProjectWorld();

    /// <summary>Take a screenshot if one was asked for.</summary>
    /// <remarks>
    /// Ours, not Valve's — there is no automatic capture in the engine's frame.
    ///
    /// **Named `TakeShot` rather than `Capture` because the view is a `Control`**, and
    /// `Control.Capture` is WinForms' mouse capture. Implementing the obvious name would have hidden
    /// a base member that decides where mouse input goes — the compiler refuses it (CS0108), and
    /// `new` would have silenced the refusal rather than the hazard.
    /// </remarks>
    public void TakeShot();

    /// <summary>Build the overlay quads for this frame.</summary>
    /// <returns>The quads, in draw order.</returns>
    public IReadOnlyList<HudQuad> BuildOverlay();

    /// <summary>Draw the frame.</summary>
    /// <param name="overlay">The quads from <see cref="BuildOverlay"/>.</param>
    public void Draw(IReadOnlyList<HudQuad> overlay);
}

/// <summary>Runs one frame's stages in the engine's order, and times each of them.</summary>
/// <remarks>
/// **This was the body of <c>MainForm.RenderFrame</c>** (B188, D90), and moving it is not
/// housekeeping — **the order was wrong, and it was wrong because it lived somewhere no test could
/// reach** (B203). A window cannot be asked what order it does things in; this can.
///
/// **Valve computes the camera and the listener from the same eye, in adjacent statements**
/// (`game/client/view.cpp:778-796`):
///
/// <code>
/// ComputeCameraVariables( viewEye.origin, viewEye.angles, &amp;g_vecVForward, ... );
///
/// // set up the hearing origin...
/// AudioState_t audioState;
/// audioState.m_Origin = viewEye.origin;
/// audioState.m_Angles = viewEye.angles;
/// engine->SetAudioState( audioState );
/// </code>
///
/// and simulates strictly before rendering: `UpdateAllSystems` is called from
/// `CHLClient::HudUpdate` (`cdll_client_int.cpp:1308`), which runs before the view is built at all.
///
/// **We did neither.** Sound ran first, so the listener sat at the previous frame's eye; and the
/// world advanced *after* the camera was uploaded, so every drawn entity was one tick ahead of the
/// view looking at it — and the viewmodel, whose camera is rebuilt during the advance, was a tick
/// ahead of the world it is drawn over.
///
/// **Each phase is named where it is timed, not by position.** The previous shape passed eight
/// timestamps positionally into `FramePhases.Between`, so its parameter names encoded the order a
/// second time: reordering the stages without reordering that argument list would have relabelled
/// every column and reported the fix as a regression somewhere else.
/// </remarks>
public static class FrameSequence
{
    /// <summary>Run one frame.</summary>
    /// <param name="steps">The stages to run.</param>
    /// <returns>How long each stage took.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="steps"/> is null.</exception>
    public static FramePhases Run(IFrameSteps steps)
    {
        ArgumentNullException.ThrowIfNull(steps);

        long start = Stopwatch.GetTimestamp();

        long advance = Time(steps.Simulate);
        long camera = Time(steps.PlaceCamera);
        long sound = Time(steps.UpdateListener);
        long project = Time(steps.ProjectWorld);
        long capture = Time(steps.TakeShot);

        long hudAt = Stopwatch.GetTimestamp();
        IReadOnlyList<HudQuad> overlay = steps.BuildOverlay();
        long hud = Stopwatch.GetTimestamp() - hudAt;

        long draw = Time(() => steps.Draw(overlay));

        return new FramePhases(
            Sound: sound,
            Camera: camera,
            Project: project,
            Advance: advance,
            Capture: capture,
            Hud: hud,
            Draw: draw,
            Total: Stopwatch.GetTimestamp() - start);
    }

    /// <summary>How long a stage took, in stopwatch ticks.</summary>
    private static long Time(Action stage)
    {
        long at = Stopwatch.GetTimestamp();
        stage();
        return Stopwatch.GetTimestamp() - at;
    }
}
