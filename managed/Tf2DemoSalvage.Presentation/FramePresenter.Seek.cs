using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;

using Microsoft.Extensions.Logging;

using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Render;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Presentation;

/// <summary>What a seek rebuilds before the moment it lands on is shown (D214, thin view slice 5).</summary>
/// <remarks>
/// **Moved out of `MainForm` whole**, where it was `ReplaySkip`, `ReplayModelDecals`, the flag both set and the HUD
/// sound's skip test: the same frames, the same flags held around them, the same two log lines. The window calls
/// <see cref="ShowMoment"/> once and is handed back to once — to show a tick (its own `ShowOnly`, which reads the
/// camera and the settings). A skip frame of the HUD is this presenter's own, since the HUD is.
/// </remarks>
public sealed partial class FramePresenter
{
    /// <summary>Shows a tick as the window shows it, and answers what the window knows of the frame afterwards.</summary>
    private Func<double, ViewFrame> _show = static _ => default;

    /// <summary>One skip frame of the HUD, or null while there is no HUD yet.</summary>
    private Action<DemoTimeline, int>? _skipHudFrame;

    /// <summary>The device a seek's decals are placed on, or null before there is one.</summary>
    private IDecalUpload? _seekDevice;

    /// <summary>The animation events a skip's frame crossed; reused.</summary>
    private readonly List<FiredAnimationEvent> _firedEvents = [];

    /// <summary>True while a seek replays the ticks it skipped, so nothing heard is emitted and nothing re-enters.</summary>
    public bool Replaying { get; private set; }

    /// <summary>Shows a moment: what a seek to it rebuilds first — the skip's frames, then the decals' — and then the moment.</summary>
    /// <param name="frame">What the window knows of the frame.</param>
    /// <param name="tick">The moment to show, which may fall between ticks.</param>
    /// <param name="device">The device, or null before there is one.</param>
    /// <param name="show">Shows a tick as the window shows it, and answers what the window knows afterwards.</param>
    /// <param name="jumps">How many times the transport has jumped — a move is a skip only when this changed (D221).</param>
    /// <exception cref="ArgumentNullException"><paramref name="show"/> is null.</exception>
    public void ShowMoment(in ViewFrame frame, double tick, Device3D? device, Func<double, ViewFrame> show, int jumps) =>
        ShowMomentOver(frame, tick, device, show, HasHud ? SkipHudFrame : null, jumps, device?.Queue);

    /// <summary>
    /// The render thread a replay drains before it poses (B581): a seek's replays pose the ticks they cross, rewriting the
    /// bones, flex streams and instance lists the frame in flight is drawing. Null over a stand-in device.
    /// </summary>
    private QueuedRenderThread? _seekQueue;

    /// <summary>The same, over any device (D214).</summary>
    /// <param name="frame">What the window knows of the frame.</param>
    /// <param name="tick">The moment to show.</param>
    /// <param name="device">The device, or null.</param>
    /// <param name="show">Shows a tick, and answers what the window knows afterwards.</param>
    /// <param name="skipHudFrame">One skip frame of the HUD, or null.</param>
    /// <param name="jumps">How many times the transport has jumped.</param>
    /// <param name="queue">The render thread a replay drains before it poses, or null.</param>
    internal void ShowMomentOver(
        in ViewFrame frame,
        double tick,
        IDecalUpload? device,
        Func<double, ViewFrame> show,
        Action<DemoTimeline, int>? skipHudFrame,
        int jumps = 0,
        QueuedRenderThread? queue = null)
    {
        ArgumentNullException.ThrowIfNull(show);

        _seekQueue = queue;

        _jumpsNow = jumps;
        _frame = frame;
        _show = show;
        _skipHudFrame = skipHudFrame;
        _seekDevice = device;

        // `gpGlobals->frametime`, which is all `C_BaseAnimating::Simulate` asks before walking events (B541). Set before
        // the moment is shown because its pose follows, and reads it.
        _models.Paused = frame.DemoFrameSeconds == 0d;

        ShowMomentNow(tick);
    }

    /// <summary>`MainForm.ShowMoment`, which the decals' replay re-enters for each tick it poses.</summary>
    private void ShowMomentNow(double tick)
    {
        ReplaySkip((int)tick);
        ReplayModelDecals((int)tick);
        ShowOnly(tick);
    }

    private void ShowOnly(double tick) => _frame = _show(tick);

    /// <summary>
    /// A sound the HUD asks for by script name: dealt with the skip when a skip frame is running, played otherwise —
    /// and neither while the HUD is handling a seek's replayed events, which fired before the tick landed on.
    /// </summary>
    /// <param name="tick">The transport's tick.</param>
    /// <param name="scriptName">The script the HUD element asked for.</param>
    /// <param name="hudReplaying">Whether the HUD is handling a seek's replayed events.</param>
    public void HudSound(int tick, string scriptName, bool hudReplaying)
    {
        // Stryker disable once all : a mutated guard leaves its pattern variables unassigned (CS0165) and Safe Mode drops the method - B410
        if (hudReplaying || _sound.Scripts is not { } scripts)
        {
            return;
        }

        // A skip frame's sound is dealt with the skip, never played (B504).
        if (_skipFrameSounds is { } skipping)
        {
            if (HudSounds.Emit(_skipFrameTick, scriptName, scripts.Entries) is { } dealt)
            {
                skipping.Add(dealt);
            }

            return;
        }

        if (HudSounds.Emit(tick, scriptName, scripts.Entries) is { } sound)
        {
            _sound.Emit(sound);
        }
    }

    /// <summary>The whole tick <see cref="ShowMoment"/> last showed, or null before the first.</summary>
    private int? _skipShown;

    /// <summary>The demo <see cref="_skipShown"/> belongs to.</summary>
    private DemoTimeline? _skipTimeline;

    /// <summary>The transport's jump count this <see cref="ShowMoment"/> was given.</summary>
    private int _jumpsNow;

    /// <summary>The jump count the last skip decision consumed.</summary>
    private int _jumpsSkipped;

    /// <summary>The last move <see cref="ShowMoment"/> made, and whether it was a skip — the one decision each reader takes once.</summary>
    public FrameSkip? Skip { get; private set; }

    /// <summary>The HUD sounds of the skip frame being run, or null outside a skip.</summary>
    private List<SceneSound>? _skipFrameSounds;

    /// <summary>The last tick of the skip frame being run.</summary>
    private int _skipFrameTick;

    /// <summary>
    /// What `demo_gototick` makes on its way to a tick (B504): a frame per <see cref="DemoSkip.PacketsPerFrame"/> packets,
    /// each hearing its batch's game events in the HUD and crossing its animation events, whose script sounds deal.
    /// </summary>
    /// <remarks>
    /// **The engine renders while it skips** — `CDemoPlayer::ReadPacket` (engine.dll FUN_180072ee0) caps a read loop at 99
    /// packets, and `_Host_RunFrame` (FUN_1801a4570) renders after it with no skip test, only prediction off. A skip back
    /// reloads the demo and reads from its start (FUN_180073b10), and the HUD with it. Only the sounds that DEAL are kept:
    /// the HUD's and `AE_CL_PLAYSOUND`'s; footsteps and physics read the deck and change nothing in it. The sounds go to
    /// <see cref="SoundPresenter.Skipped"/>, which deals them with the skip's temp entities, frame by frame, unplayed.
    /// **Interpolated:** one frame per 99 ticks — see <see cref="DemoSkip.Frames"/>.
    /// </remarks>
    private void ReplaySkip(int target)
    {
        if (Replaying || _skipFrameSounds is not null)
        {
            return;
        }

        // A new demo starts its own history: its first move is a skip, not a move from the last demo's tick.
        if (!ReferenceEquals(_frame.Timeline, _skipTimeline))
        {
            _skipTimeline = _frame.Timeline;
            _skipShown = null;
        }

        int? from = _skipShown;

        _skipShown = target;

        // **The frame's one skip decision** (D221): the particles and the sound read this, rather than each keeping a
        // cursor of its own that a different call cadence could leave disagreeing. **A skip is a JUMP, not a distance**:
        // the engine skips only on an explicit `demo_gototick`, and a played frame — whose demo time `Host_FilterTime`
        // (engine.dll FUN_18019ffe0) caps at 0.1 s times the timescale — never is, however many ticks it covers.
        bool skipped = from is null || _jumpsNow != _jumpsSkipped;

        _jumpsSkipped = _jumpsNow;

        // A jump that lands on the tick already shown is still a skip, and its readers must see it.
        if (from != target || skipped)
        {
            Skip = new FrameSkip(target, (Skip?.Generation ?? 0) + 1, (Skip?.Skips ?? 0) + (skipped ? 1 : 0)) { Skipped = skipped };
        }

        // Stryker disable once all : a mutated guard leaves its pattern variables unassigned (CS0165) and Safe Mode drops the method - B410
        if (_frame.Timeline is not { } timeline || !skipped || _sound.Scripts is not { } scripts)
        {
            return;
        }

        _seekQueue?.Lock();

        long began = Stopwatch.GetTimestamp();
        // Stryker disable once all : a mutated guard leaves its pattern variables unassigned (CS0165) and Safe Mode drops the method - B410
        bool back = from is not { } was || target < was;
        int first = back ? timeline.FirstTick : from!.Value + 1;
        IReadOnlyList<int> throughs = DemoSkip.Frames(first, Math.Max(first, target));
        List<SkipFrame> frames = new(throughs.Count);
        bool hud = _skipHudFrame is not null;
        int heard = 0;
        int crossed = 0;

        // Every event the frames crossed, the instrument's control: none at all would mean no walk, not no sound.
        int walked = 0;
        int steps = 0;

        // What the frames made, in order — every HUD sound and every crossed event — so two builds compare exactly.
        ulong events = SequenceHash.Empty;
        long hudTicks = 0;
        long buildTicks = 0;
        long poseTicks = 0;
        long simulatedBefore = _models.SimulateTicks;
        long sampleTicks = 0;
        long drawListTicks = 0;
        long modelTicks = 0;

        _models.HoldsCorpses = true;
        _moment.EntitiesOnly = true;
        _models.SimulatesOnly = true;

        // `CDemoPlayer::IsPlaybackPaused` answers false while skipping whatever the pause flag says (engine.dll
        // FUN_1800729f0), so a skip's frames have a frametime and walk their events; the frame it lands on has the flag's.
        bool paused = _models.Paused;

        _models.Paused = false;

        try
        {
            foreach (int through in throughs)
            {
                List<SceneSound> live = [];

                _skipFrameSounds = live;
                _skipFrameTick = through;

                long phase = Stopwatch.GetTimestamp();

                if (hud)
                {
                    _skipHudFrame?.Invoke(timeline, through);
                }

                hudTicks += Stopwatch.GetTimestamp() - phase;
                _skipFrameSounds = null;
                heard += live.Count;

                foreach (SceneSound made in live)
                {
                    events = SequenceHash.Add(events, made.Name);
                }

                phase = Stopwatch.GetTimestamp();
                ShowOnly(through);
                buildTicks += Stopwatch.GetTimestamp() - phase;
                sampleTicks += _moments.LastBuild.Sampled;
                drawListTicks += _moments.LastBuild.Built.DrawList;
                modelTicks += _moments.LastBuild.Built.Models;
                phase = Stopwatch.GetTimestamp();
                _moments.PoseNow(ViewFrustum.Nothing);
                poseTicks += Stopwatch.GetTimestamp() - phase;
                _firedEvents.Clear();
                _models.TakeFiredEvents(_firedEvents);

                walked += _firedEvents.Count;

                foreach (FiredAnimationEvent fired in _firedEvents)
                {
                    steps += fired.Event.Id == SoundPresenter.FootstepEvent ? 1 : 0;
                    // Stryker disable once all : a mutated interpolated string cannot bind to its handler (CS1620) and Safe Mode drops the method - B410
                    events = SequenceHash.Add(events, string.Create(CultureInfo.InvariantCulture, $"{through}:{fired.EntityIndex}:{fired.Event.Id}"));

                    if (fired.Event.Id == SoundPresenter.PlaySoundEvent &&
                        EntitySounds.Emit(through, fired.EntityIndex, fired.Event.Options, fired.Origin, scripts.Entries, emitted: true, ClientSoundPhase.Simulate) is { } sound)
                    {
                        live.Add(sound);
                        crossed++;
                    }
                }

                frames.Add(new SkipFrame(through, live));
            }
        }
        finally
        {
            _skipFrameSounds = null;
            _models.HoldsCorpses = false;
            _moment.EntitiesOnly = false;
            _models.SimulatesOnly = false;
            _models.Paused = paused;
        }

        _sound.Skipped(new SoundSkip(from, target, frames));

        // Stryker disable once all : a mutated interpolated string cannot bind to its handler (CS1620) and Safe Mode drops the method - B410
        _audioLog.LogInformation(
            "{Message}",
            string.Create(
                CultureInfo.InvariantCulture,
                $"skip: to {target} from {(back ? "the start" : from!.Value.ToString(CultureInfo.InvariantCulture))} in {frames.Count} frames " +
                $"made {heard} HUD and {crossed} animation script sounds (of {walked} animation events, {steps} of them footsteps, which only read){(hud ? string.Empty : " (no HUD yet)")} in {Stopwatch.GetElapsedTime(began).TotalMilliseconds:0} ms " +
                $"(hud {Ms(hudTicks):0}, build {Ms(buildTicks):0} (sample {Ms(sampleTicks):0}, drawlist {Ms(drawListTicks):0}, models {Ms(modelTicks):0}), pose {Ms(poseTicks):0} of which simulate {Ms(_models.SimulateTicks - simulatedBefore):0}; " +
                $"last frame {_moment.Drawn.Count} drawn, {_moment.StaticProps.Count} static; sequence {events:x16})"));

        static double Ms(long ticks) => ticks * 1000d / Stopwatch.Frequency;
    }

    /// <summary>
    /// After a seek, has the decals replay the ticks it skipped (<see cref="DecalEffects.Replay"/>), showing and posing
    /// each tick they ask for — what TF2 does, since it cannot seek at all (`demo_gototick`).
    /// </summary>
    /// <remarks>Which ticks, and what lands on them, is the presenter's; this holds the scene still around it and times it.</remarks>
    private void ReplayModelDecals(int target)
    {
        // Stryker disable once all : a mutated guard leaves its pattern variables unassigned (CS0165) and Safe Mode drops the method - B410
        if (Replaying || _frame.Timeline is null || _seekDevice is not { } device)
        {
            return;
        }

        long lookingBack = Stopwatch.GetTimestamp();

        // Stryker disable once all : a mutated guard leaves its pattern variables unassigned (CS0165) and Safe Mode drops the method - B410
        if (_decals.ReplayStart(DecalFrameAt(target), target) is not { } start)
        {
            return;
        }

        _seekQueue?.Lock();

        TimeSpan lookback = Stopwatch.GetElapsedTime(lookingBack);
        TimeSpan building = TimeSpan.Zero;
        TimeSpan posing = TimeSpan.Zero;
        (long simulate, long setup, long skin) was = (_models.SimulateTicks, _models.SetupTicks, _models.SkinTicks);

        Replaying = true;
        _models.HoldsCorpses = true;
        _moments.PlayersOnly = true;

        // Ours, not the engine's: its poses must not move the animation-event walk (B504).
        _models.WalksEvents = false;

        long began = Stopwatch.GetTimestamp();
        DecalReplayReport replayed;

        try
        {
            replayed = _decals.Replay(
                start,
                target,
                device,
                tick =>
                {
                    long phase = Stopwatch.GetTimestamp();
                    ShowMomentNow(tick);
                    building += Stopwatch.GetElapsedTime(phase);

                    // Simulated, not drawn: `CModelRender::AddDecal` sets up the struck player's bones itself (`SkinningOf`).
                    phase = Stopwatch.GetTimestamp();
                    _moments.PoseNow(ViewFrustum.Nothing);
                    posing += Stopwatch.GetElapsedTime(phase);

                    return DecalFrameAt(tick);
                });
        }
        finally
        {
            Replaying = false;
            _models.HoldsCorpses = false;
            _moments.PlayersOnly = false;
            _models.WalksEvents = true;
        }

        // Stryker disable once all : a mutated interpolated string cannot bind to its handler (CS1620) and Safe Mode drops the method - B410
        _renderLog.LogInformation(
            "{Message}",
            string.Create(
                CultureInfo.InvariantCulture,
                $"model decals: a seek to {target} replayed ticks {start}-{target - 1}, posing {replayed.Stepped} of them, in {Stopwatch.GetElapsedTime(began).TotalMilliseconds:0} ms " +
                $"(lookback {lookback.TotalMilliseconds:0}, skipped ticks {replayed.Skipping.TotalMilliseconds:0}, building {building.TotalMilliseconds:0}, " +
                $"posing {posing.TotalMilliseconds:0} of which simulate {Milliseconds(_models.SimulateTicks - was.simulate):0}, " +
                $"bones {Milliseconds(_models.SetupTicks - was.setup):0}, skin {Milliseconds(_models.SkinTicks - was.skin):0}; " +
                $"the rest placing decals); {replayed.Held} held"));

        static double Milliseconds(long ticks) => ticks * 1000d / Stopwatch.Frequency;
    }
}
