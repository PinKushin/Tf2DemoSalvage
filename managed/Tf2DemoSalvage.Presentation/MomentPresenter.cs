using System;
using System.Collections.Generic;
using System.Diagnostics;

using Microsoft.Extensions.Logging;

using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Presentation;

/// <summary>What a window knows about a moment that the timeline does not.</summary>
/// <param name="CurrentTick">Where the transport says playback is.</param>
/// <param name="FirstPerson">Whether the view is through a player's eyes.</param>
/// <param name="Followed">The entity being spectated, or null.</param>
/// <param name="Eye">That player's camera, or null when nobody's eyes are available.</param>
/// <param name="ViewmodelFieldOfView">The user's viewmodel field of view, in degrees.</param>
/// <param name="DrawViewmodel">
/// Whether the weapon in hand is drawn — Valve's <c>r_drawviewmodel</c> (B166). A setting like the
/// field of view beside it, defaulted true because Valve ships it at <c>"1"</c>.
/// </param>
/// <remarks>
/// **Five values, and every one of them is genuinely the window's.** The camera mode is a UI state,
/// the transport tick is a control's position, the eye needs the viewport's aspect, and the field of
/// view is a setting. Everything else a moment needs — who is where, at what tick rate — belongs to
/// the recording and is read from <see cref="IMomentSource"/>.
/// </remarks>
public readonly record struct MomentView(
    int CurrentTick,
    bool FirstPerson,
    int? Followed,
    FreeCamera? Eye,
    float ViewmodelFieldOfView,
    bool DrawViewmodel = true);

/// <summary>Samples a moment from the demo and hands it to the scene.</summary>
/// <remarks>
/// **This was `MainForm.ShowMoment` and the two buffers it filled** (B188, D90). The owner's
/// question is what found it — *"does the view need to hold them to pass them on?"* — and the answer
/// was no: the window held a `DemoTimeline`, a `List&lt;ScenePlayer&gt;` and a `List&lt;SceneProp&gt;`
/// only because the sampling happened there.
///
/// **It had no tests and could not have had any.** `DemoTimeline`'s constructor is private and
/// `Build` takes the bytes of a real file, so anything sampling one directly needs a demo shipped
/// into the test project and, here, a window to hold it. `IMomentSource` is what changes that.
///
/// **One path from "which moment" to "what is drawn".** Scrubbing and playing both come through
/// here, so the two cannot disagree about what a tick looks like — which they did once, when
/// playback and the scrub bar each built the scene their own way.
/// </remarks>
public sealed class MomentPresenter
{
    private readonly MomentScene _moment;
    private readonly FrameLedger _ledger;
    private readonly ILogger _render;

    /// <summary>The players at a moment, refilled each time rather than reallocated.</summary>
    private readonly List<ScenePlayer> _players = [];

    /// <summary>The props at a moment, refilled each time.</summary>
    private readonly List<SceneProp> _props = [];

    /// <summary>Wires a presenter to the scene it fills.</summary>
    /// <param name="moment">The scene being assembled.</param>
    /// <param name="ledger">Where the per-second frame counters live.</param>
    /// <param name="render">The render log, for slow-step reports.</param>
    /// <exception cref="ArgumentNullException">A collaborator is null.</exception>
    public MomentPresenter(MomentScene moment, FrameLedger ledger, ILogger render)
    {
        ArgumentNullException.ThrowIfNull(moment);
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(render);

        _moment = moment;
        _ledger = ledger;
        _render = render;
    }

    /// <summary>The demo being shown, or null when none is open.</summary>
    /// <remarks>
    /// **Set when a demo opens, beside `SpectatorView.Eyes` and `MomentScene.Viewmodels`** — the
    /// two sources this one joins. `DemoSystems.Open` sets all three, so a demo that fails to decode
    /// clears every one of them rather than leaving one behind pointing at the previous recording.
    /// </remarks>
    public IMomentSource? Source { get; set; }

    /// <summary>Where the players' appearance comes from, or null when nothing can build one.</summary>
    /// <remarks>
    /// **This was `MainForm.EnsureWeaponRoles`** (B188, D90): one line in the window, called every
    /// frame, reaching for the timeline and the game install. It is a property rather than a
    /// constructor argument because the appearance depends on two things with different lifetimes —
    /// a demo, and an install located later — so there is no moment at which a finished one could be
    /// handed over.
    /// </remarks>
    public IAppearanceSource? Appearances { get; set; }

    /// <summary>The tick interval used by the last <see cref="Show"/>, for tests.</summary>
    /// <remarks>
    /// **Exposed because the alternative was worse.** The interval reaches `MomentScene` inside a
    /// `MomentInfo` that nothing hands back, so asserting it would otherwise need a fake scene —
    /// a second implementation of a large class, to observe one float.
    /// </remarks>
    public float LastInterval { get; private set; }

    /// <summary>Shows the moment at a tick.</summary>
    /// <param name="tick">The moment, which may fall between ticks.</param>
    /// <param name="view">What the window knows and the recording does not.</param>
    /// <remarks>
    /// **Silent with no source**, because the render loop calls this without asking whether a demo
    /// is open — a resize or a repaint arrives before anything is loaded.
    /// </remarks>
    public void Show(double tick, in MomentView view)
    {
        if (Source is not { } source)
        {
            return;
        }

        // **Before the sampling and outside both timers, which is where it was and where it
        // belongs.** It is free after the first call, but the FIRST reads the weapon scripts out of
        // the archives and each one costs an ICE decryption — so counting it as sampling reports one
        // enormous `sampling` spike for work that is not sampling, and counting it as posing moves
        // the same lie one column along.
        //
        // **The return value IS the wiring.** The version of this that wrote into
        // `MomentScene.Appearance` as a side effect is the one that shipped B193 — every weapon
        // suffix answering null and every player animating in the generic primary pose, with the
        // suite green.
        if (Appearances is { } appearances)
        {
            _moment.Appearance = appearances.Ensure(_moment.Appearance);
        }

        // **Timed because three untimed steps once hid 129 ms of a 133 ms pose** (B191). The column
        // moved out of the window with the sampling it measures.
        long sampledAt = Stopwatch.GetTimestamp();

        source.PlayersAt(tick, _players);
        source.PropsAt(tick, _props);

        long sampleTicks = Stopwatch.GetTimestamp() - sampledAt;

        _ledger.Sampled(sampleTicks);

        LastInterval = source.IntervalPerTick;

        MomentPhases phases = _moment.Build(
            _players,
            _props,
            new MomentInfo(
                tick,
                view.CurrentTick,
                view.FirstPerson,
                view.Followed,
                view.Eye,
                LastInterval,
                view.ViewmodelFieldOfView,
                view.DrawViewmodel,

                // From the recording, not from the window: the round is something the demo knows
                // and the viewer cannot derive.
                source.RoundStateAt(tick)));

        _ledger.Posed(phases.Pose);

        StallReport.Moment(phases, sampleTicks, playerTicks: 0, _render);
    }
}
