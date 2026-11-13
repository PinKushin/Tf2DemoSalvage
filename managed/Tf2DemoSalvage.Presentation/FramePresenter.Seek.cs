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

    /// <summary>Whether a skip spreads over frames, as the engine's does (B708); false runs it to the end in the call that began it.</summary>
    /// <remarks>
    /// **The engine's main loop drains the window's queue and then runs `CEngine::Frame`** (engine.dll `0x18020d460`, frame at vtable
    /// +0x28, `0x18020e510`), `CDemoPlayer::ReadPacket` ends a read loop at the 100th packet of a skip, and `_Host_RunFrame` renders
    /// after it. So paint and input run between a skip's batches with nothing re-entered. False where nothing draws frames between
    /// calls: the presenter's own tests, and `TF2VIEW_SEEK_THROUGH`, which asks to pass each tick it names.
    /// </remarks>
    public bool SkipsAcrossFrames { get; set; }

    /// <summary>Whether going back shows its tick at once rather than reading from the demo's start.</summary>
    /// <remarks>
    /// **Rewind is an addition above the engine, and the owner's** (D246): TF2's only way back is a reload that reads from the start
    /// (FUN_180073b10). False only where a test or `TF2VIEW_SEEK_THROUGH` asks for that reload.
    /// </remarks>
    public bool RewindsAtOnce { get; set; }

    /// <summary>Whether a skip is under way: the moment shown is on its way to the tick the transport names.</summary>
    public bool Skipping => _skipWalk is not null;

    /// <summary>The skip under way, or null.</summary>
    private SkipWalk? _skipWalk;

    /// <summary>The start of the line a skip's landing writes; the UI suite reads its counts.</summary>
    public const string SkipLandedLine = "skip landed at";

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
        // Asked again while it runs: a new target, or the same one from a repaint. The frame's own step does the work.
        if (_skipWalk is { } walking)
        {
            if (RewindsAtOnce && (int)tick < walking.Target)
            {
                // A rewind, not a new target: what the walk read so far is left undealt, as a rewind deals nothing.
                DropSkip();
            }
            else if (ReferenceEquals(_frame.Timeline, walking.Timeline))
            {
                Retarget(walking, tick);

                return;
            }

            else
            {
                // Another demo: nothing of this walk is its history, and the walk is not its seek.
                DropSkip();
            }
        }

        // Walked: it is under way and will show the tick when it lands, or ran to its landing in this call and already has.
        if (ReplaySkip((int)tick, tick))
        {
            return;
        }

        ReplayModelDecals((int)tick);
        ShowOnly(tick);
    }

    /// <summary>One frame of a skip under way: what the engine's client runs between two renders (B708).</summary>
    /// <param name="frame">What the window knows of the frame.</param>
    /// <returns>Whether a skip took this frame, the one it lands on included: its clock does not move past what was asked for.</returns>
    /// <remarks>
    /// **One batch per tick the frame built up, and at least one**: `_Host_RunFrame` runs `_Host_RunFrame_Client` once per tick it
    /// accumulated (FUN_1801a4570), and once with none while skipping; each runs a read loop `ReadPacket` ends at the 100th packet.
    /// </remarks>
    public bool StepSkip(in ViewFrame frame)
    {
        if (_skipWalk is not { } walk)
        {
            return false;
        }

        _frame = frame;

        // The demo it was walking has gone: a new one starts its own history.
        if (!ReferenceEquals(frame.Timeline, walk.Timeline))
        {
            DropSkip();

            return false;
        }

        // The frame's real length, not the demo's: a paused demo's frame still accumulates host time.
        int batches = DemoSkip.BatchesThisFrame(frame.FrameSeconds, walk.Timeline.IntervalPerTick);
        int read = 0;

        walk.FramesDrawn++;

        for (int batch = 0; batch < batches && _skipWalk is not null; batch++)
        {
            read += walk.Phase == SkipPhase.Frames ? 1 : 0;
            walk.Batches += walk.Phase == SkipPhase.Frames ? 1 : 0;
            walk.MostBatchesInAFrame = Math.Max(walk.MostBatchesInAFrame, read);
            StepWalk(walk, DemoSkip.PacketsPerFrame);
        }

        // Drawn as the frame it is, with every entity: the batch's own show is entities only, and poses nothing it can be seen by.
        // Still held (`HoldSkip`): this pose is at the batch's tick, whose events its batch walked.
        if (_skipWalk is not null)
        {
            ShowOnly(walk.Shown);
        }

        return true;
    }

    /// <summary>Runs a skip under way to its landing, for a caller that needs the tick reached now.</summary>
    public void FinishSkip()
    {
        while (_skipWalk is { } walk)
        {
            StepWalk(walk, int.MaxValue);
        }
    }

    /// <summary>Forgets a skip under way, unlanded and undealt: its demo or its map is going, and nothing of it belongs to what comes next.</summary>
    public void DropSkip()
    {
        if (_skipWalk is null)
        {
            return;
        }

        _skipWalk = null;
        _decals.AbandonReplay();
        _sound.DropSkip();
        ReleaseSkip();
    }

    /// <summary>What holds from the start of a skip to its landing, on its frames and the frames drawn between them (B708).</summary>
    /// <remarks>
    /// **Corpses, because the engine has none to step.** A `C_TFRagdoll` made while skipping records it
    /// (`m_bCreatedWhilePlaybackSkipping`, `c_tf_player.cpp:583`) and makes no ragdoll in a demo (`:1190-1193`), and the physics it
    /// has for an older one steps by the host's frame time, never by demo ticks. Stepping ours here caught up 99 ticks or more a batch.
    /// **Events, because only a batch walks them**: the drawn frame poses the batch's own tick, and `RunSkipFrame` lifts this for its pose.
    /// </remarks>
    private void HoldSkip()
    {
        _models.HoldsCorpses = true;
        _models.WalksEvents = false;
    }

    private void ReleaseSkip()
    {
        _models.HoldsCorpses = false;
        _models.WalksEvents = true;
    }

    /// <summary>One batch of a skip: the decals' ticks it read, then its frame; or what is left of the decals; or the landing.</summary>
    /// <remarks>The batch's ticks before its frame, as `CL_ReadPackets` comes before the render: the tick drawn only ever moves forward.</remarks>
    private void StepWalk(SkipWalk walk, int decalTicks)
    {
        switch (walk.Phase)
        {
            case SkipPhase.Frames when walk.NextThrough() is { } through:
                StepDecalWalk(walk, decalTicks, through);
                RunSkipFrame(walk, through);
                break;

            case SkipPhase.Frames:
                walk.Phase = SkipPhase.Decals;
                break;

            case SkipPhase.Decals when walk.DecalsUnderWay:
                StepDecalWalk(walk, decalTicks, int.MaxValue);
                break;

            default:
                Land(walk);
                break;
        }
    }

    /// <summary>The skip reaches its tick: what its frames made goes to the deck, and the moment is shown with nothing held.</summary>
    private void Land(SkipWalk walk)
    {
        EndSkipFrames(walk);

        long corpses = _models.Corpses.SteppingTicks;

        _skipWalk = null;
        ReleaseSkip();
        ShowOnly(walk.Tick);

        // Stryker disable once all : a mutated interpolated string cannot bind to its handler (CS1620) and Safe Mode drops the method - B410
        _renderLog.LogInformation(
            "{Message}",
            string.Create(
                CultureInfo.InvariantCulture,
                $"{SkipLandedLine} {walk.Target} after {walk.Batches} batches over {walk.FramesDrawn} frames, at most {walk.MostBatchesInAFrame} in a frame; corpses stepped {(corpses - walk.CorpseTicksAtStart) * 1000d / Stopwatch.Frequency:0} ms on the way"));
    }

    /// <summary>A new tick asked for while a skip runs: on from where it is, or from the demo's start for one behind it, as `SkipToTick` reloads.</summary>
    private void Retarget(SkipWalk walk, double tick)
    {
        int target = (int)tick;

        walk.Tick = tick;

        // It took the jump, and the walk's landing is what shows it: the next ordinary move is no second skip.
        _jumpsSkipped = _jumpsNow;

        if (target == walk.Target)
        {
            return;
        }

        _skipShown = target;
        Skip = new FrameSkip(target, (Skip?.Generation ?? 0) + 1, (Skip?.Skips ?? 0) + 1) { Skipped = true };
        walk.Retarget(target, walk.Timeline.FirstTick);
        BeginDecalWalk(walk);
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

        // A skip frame's sound is dealt with the skip, never played (B504); the HUD painted on a frame drawn during it belongs to the
        // batch that frame shows.
        if ((_skipFrameSounds ?? _skipWalk?.LastLive) is { } skipping)
        {
            if (HudSounds.Emit(_skipFrameTick, scriptName, scripts.Entries) is { } dealt)
            {
                skipping.Add(dealt);

                // A batch counts its own at its end; this one's end has passed.
                if (_skipFrameSounds is null)
                {
                    _skipWalk!.Counts.Heard++;
                }
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
    private bool ReplaySkip(int target, double tick)
    {
        if (Replaying || _skipFrameSounds is not null)
        {
            return false;
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

        // Going back behaves as a reload (B676, `FUN_1800eb5d0`): TF2's skip back is one, so every model instance is new and its next draw a
        // first draw; reverse play is ours and follows it, whole ticks being the measure, so a frame behind within its tick holds. The
        // guard above returns only inside a seek's own walk, whose outer call has cleared already and whose poses draw nothing.
        if (target < from)
        {
            _models.LightAverage.Clear();
        }

        // A jump that lands on the tick already shown is still a skip, and its readers must see it.
        if (from != target || skipped)
        {
            Skip = new FrameSkip(target, (Skip?.Generation ?? 0) + 1, (Skip?.Skips ?? 0) + (skipped ? 1 : 0)) { Skipped = skipped };
        }

        // Stryker disable once all : a mutated guard leaves its pattern variables unassigned (CS0165) and Safe Mode drops the method - B410
        if (_frame.Timeline is not { } timeline || !skipped)
        {
            return false;
        }

        // Rewind (D246): the moment is sampled and the decals and HUD rebuild from their own windows, so nothing needs the demo read again.
        // The entities' own built-up state is rebuilt here, from the timeline (B527).
        if (RewindsAtOnce && target < from)
        {
            RebuildEntityState(timeline, target);
            return false;
        }

        bool reads = _sound.Scripts is not null;

        // Nothing to deal and nothing to draw between: the caller shows the moment in the same call.
        if (!reads && !SkipsAcrossFrames)
        {
            return false;
        }

        // Stryker disable once all : a mutated guard leaves its pattern variables unassigned (CS0165) and Safe Mode drops the method - B410
        bool back = from is not { } was || target < was;

        // The tick last on screen, until the first batch has one of its own.
        _skipWalk = new SkipWalk(timeline, back ? null : from, back ? timeline.FirstTick : from!.Value + 1, target, tick)
        {
            ReadsFrames = reads,
            Shown = from ?? timeline.FirstTick,
            CorpseTicksAtStart = _models.Corpses.SteppingTicks,
        };
        HoldSkip();
        BeginDecalWalk(_skipWalk);

        if (!SkipsAcrossFrames)
        {
            FinishSkip();
        }

        return true;
    }

    /// <summary>How many ticks before a rewind's target its entities are posed again: D217's checkpoint span, two seconds.</summary>
    /// <remarks>
    /// **Long enough for every piece of entity state that forgets its past** (B527): a sequence transition fades over its
    /// `fadeouttime` (a fraction of a second on TF2's models), the animation-event walk needs only the last frame's cycle, the
    /// minigun's barrel speed approaches its target by 0.1 a frame (`UpdateBarrelMovement`, a second at most), and the duck jump's
    /// hold is its own landing window. **Not the barrel's ANGLE**, which is the integral of the speed over the whole recording and so
    /// depends on every frame since the minigun appeared — a phase even TF2's reload reproduces only at the frame rate it was played at.
    /// </remarks>
    public const int RewindPreRollTicks = 133;

    /// <summary>
    /// What a rewind owes its entities (B527): every entity's built-up state forgotten, then the <see cref="RewindPreRollTicks"/>
    /// before <paramref name="target"/> posed from the timeline — entities only, events walked and not dealt, no HUD, no demo read.
    /// </summary>
    /// <remarks>
    /// **Forgetting is the reload's half** (B676): TF2's way back reloads, so every entity is new. **Posing the window is the playing
    /// half**: the state each frame builds is built again, tick by tick, from the same moments playing would have shown.
    /// </remarks>
    private void RebuildEntityState(DemoTimeline timeline, int target)
    {
        long began = Stopwatch.GetTimestamp();

        _models.ForgetEntities();

        int first = Math.Max(timeline.FirstTick, target - RewindPreRollTicks);
        bool paused = _models.Paused;

        // The skip's own re-entry guard: a pose shown from here must not be taken for a new jump.
        _skipFrameSounds = [];
        _moment.EntitiesOnly = true;
        _models.SimulatesOnly = true;
        _models.WalksEvents = true;
        _models.Paused = false;

        try
        {
            for (int tick = first; tick < target; tick++)
            {
                ShowOnly(tick);
                _moments.PoseNow(ViewFrustum.Nothing);

                // Walked, so each entity's place in its events moves as playing moves it; not dealt, because they fired before the target.
                _firedEvents.Clear();
                _models.TakeFiredEvents(_firedEvents);
            }
        }
        finally
        {
            _firedEvents.Clear();
            _skipFrameSounds = null;
            _moment.EntitiesOnly = false;
            _models.SimulatesOnly = false;
            _models.WalksEvents = false;
            _models.Paused = paused;
        }

        // Carried from this call (B243): what the rebuild cost, which a rewind waits on.
        // Stryker disable once all : a mutated interpolated string cannot bind to its handler (CS1620) and Safe Mode drops the method - B410
        _renderLog.LogInformation(
            "{Message}",
            string.Create(
                CultureInfo.InvariantCulture,
                $"{RewindRebuiltLine} {target - first} ticks before {target} in {Stopwatch.GetElapsedTime(began).TotalMilliseconds:0.0} ms"));
    }

    /// <summary>The start of the line a rewind's entity rebuild writes, with how many ticks it posed and how long it took.</summary>
    public const string RewindRebuiltLine = "rewind rebuilt entities from";

    /// <summary>One batch of a skip: its HUD, its moment and its pose, keeping what it made.</summary>
    private void RunSkipFrame(SkipWalk walk, int through)
    {
        if (_sound.Scripts is not { } scripts)
        {
            return;
        }

        _seekQueue?.Lock();

        SkipCounts counts = walk.Counts;
        long simulatedBefore = _models.SimulateTicks;

        counts.Hud = _skipHudFrame is not null;
        _moment.EntitiesOnly = true;
        _models.SimulatesOnly = true;
        _models.WalksEvents = true;

        // `CDemoPlayer::IsPlaybackPaused` answers false while skipping whatever the pause flag says (engine.dll
        // FUN_1800729f0), so a skip's frames have a frametime and walk their events; the frame it lands on has the flag's.
        bool paused = _models.Paused;

        _models.Paused = false;

        try
        {
            List<SceneSound> live = [];

            _skipFrameSounds = live;
            _skipFrameTick = through;

            long phase = Stopwatch.GetTimestamp();

            _skipHudFrame?.Invoke(walk.Timeline, through);

            counts.HudTicks += Stopwatch.GetTimestamp() - phase;
            _skipFrameSounds = null;
            counts.Heard += live.Count;

            foreach (SceneSound made in live)
            {
                counts.Events = SequenceHash.Add(counts.Events, made.Name);
            }

            phase = Stopwatch.GetTimestamp();
            ShowOnly(through);
            counts.BuildTicks += Stopwatch.GetTimestamp() - phase;
            counts.SampleTicks += _moments.LastBuild.Sampled;
            counts.DrawListTicks += _moments.LastBuild.Built.DrawList;
            counts.ModelTicks += _moments.LastBuild.Built.Models;
            phase = Stopwatch.GetTimestamp();
            _moments.PoseNow(ViewFrustum.Nothing);
            counts.PoseTicks += Stopwatch.GetTimestamp() - phase;
            _firedEvents.Clear();
            _models.TakeFiredEvents(_firedEvents);

            counts.Walked += _firedEvents.Count;

            foreach (FiredAnimationEvent fired in _firedEvents)
            {
                counts.Steps += fired.Event.Id == SoundPresenter.FootstepEvent ? 1 : 0;
                // Stryker disable once all : a mutated interpolated string cannot bind to its handler (CS1620) and Safe Mode drops the method - B410
                counts.Events = SequenceHash.Add(counts.Events, string.Create(CultureInfo.InvariantCulture, $"{through}:{fired.EntityIndex}:{fired.Event.Id}"));

                if (fired.Event.Id == SoundPresenter.PlaySoundEvent &&
                    EntitySounds.Emit(through, fired.EntityIndex, fired.Event.Options, fired.Origin, scripts.Entries, emitted: true, ClientSoundPhase.Simulate) is { } sound)
                {
                    live.Add(sound);
                    counts.Crossed++;
                }
            }

            walk.Frames.Add(new SkipFrame(through, live));
            walk.LastLive = live;
            walk.Shown = through;

            // The batch's sounds start in this frame, as `CL_DispatchSound` runs as each packet is read (B550).
            _sound.SkipFrameRead(through, walk.ReloadPending);
            walk.ReloadPending = false;
        }
        finally
        {
            _skipFrameSounds = null;
            _moment.EntitiesOnly = false;
            _models.SimulatesOnly = false;
            _models.WalksEvents = false;
            _models.Paused = paused;
            counts.SimulateTicks += _models.SimulateTicks - simulatedBefore;
        }
    }

    /// <summary>A skip's frames are read: deals what they made, and says so.</summary>
    private void EndSkipFrames(SkipWalk walk)
    {
        if (!walk.ReadsFrames)
        {
            return;
        }

        _seekQueue?.Lock();
        _sound.Skipped(new SoundSkip(walk.From, walk.Target, [.. walk.Frames]) { Reloaded = [.. walk.Reloaded] });

        SkipCounts counts = walk.Counts;

        // Stryker disable once all : a mutated interpolated string cannot bind to its handler (CS1620) and Safe Mode drops the method - B410
        _audioLog.LogInformation(
            "{Message}",
            string.Create(
                CultureInfo.InvariantCulture,
                $"skip: to {walk.Target} from {(walk.From is { } from ? from.ToString(CultureInfo.InvariantCulture) : "the start")} in {walk.Frames.Count} frames " +
                $"made {counts.Heard} HUD and {counts.Crossed} animation script sounds (of {counts.Walked} animation events, {counts.Steps} of them footsteps, which only read){(counts.Hud ? string.Empty : " (no HUD yet)")} in {Stopwatch.GetElapsedTime(counts.Began).TotalMilliseconds:0} ms " +
                $"(hud {Ms(counts.HudTicks):0}, build {Ms(counts.BuildTicks):0} (sample {Ms(counts.SampleTicks):0}, drawlist {Ms(counts.DrawListTicks):0}, models {Ms(counts.ModelTicks):0}), pose {Ms(counts.PoseTicks):0} of which simulate {Ms(counts.SimulateTicks):0}; " +
                $"last frame {_moment.Drawn.Count} drawn, {_moment.StaticProps.Count} static; sequence {counts.Events:x16})"));

        static double Ms(long ticks) => ticks * 1000d / Stopwatch.Frequency;
    }

    /// <summary>Starts the decals' replay for where a skip is going, if the jump needs one, in place of any it was running.</summary>
    private void BeginDecalWalk(SkipWalk walk)
    {
        walk.DecalsUnderWay = false;
        _decals.AbandonReplay();

        // Stryker disable once all : a mutated guard leaves its pattern variables unassigned (CS0165) and Safe Mode drops the method - B410
        if (_frame.Timeline is null || _seekDevice is null)
        {
            return;
        }

        long lookingBack = Stopwatch.GetTimestamp();

        // Stryker disable once all : a mutated guard leaves its pattern variables unassigned (CS0165) and Safe Mode drops the method - B410
        if (_decals.ReplayStart(DecalFrameAt(walk.Target), walk.Target) is { } start)
        {
            BeginDecals(walk, start, lookingBack);
        }
    }

    /// <summary>Begins the replay from a tick, its costs counted from nothing.</summary>
    private void BeginDecals(SkipWalk walk, int start, long lookingBack)
    {
        _seekQueue?.Lock();

        SkipCounts counts = walk.Counts;

        counts.LookbackTicks = Stopwatch.GetTimestamp() - lookingBack;
        counts.DecalStart = start;
        counts.DecalTicks = 0;
        counts.BuildingTicks = 0;
        counts.PosingTicks = 0;
        counts.DecalSimulate = 0;
        counts.DecalSetup = 0;
        counts.DecalSkin = 0;
        _decals.BeginReplay(start, walk.Target);
        walk.DecalsUnderWay = true;
    }

    /// <summary>Up to <paramref name="ticks"/> ticks of the decals' replay, none past <paramref name="through"/>.</summary>
    private void StepDecalWalk(SkipWalk walk, int ticks, int through)
    {
        if (!walk.DecalsUnderWay)
        {
            return;
        }

        if (_seekDevice is not { } device)
        {
            walk.DecalsUnderWay = false;
            _decals.AbandonReplay();

            return;
        }

        _seekQueue?.Lock();

        SkipCounts counts = walk.Counts;
        long began = Stopwatch.GetTimestamp();
        (long simulate, long setup, long skin) = (_models.SimulateTicks, _models.SetupTicks, _models.SkinTicks);
        bool holds = _models.HoldsCorpses;
        bool walks = _models.WalksEvents;

        Replaying = true;
        _models.HoldsCorpses = true;
        _moments.PlayersOnly = true;

        // Ours, not the engine's: its poses must not move the animation-event walk (B504).
        _models.WalksEvents = false;

        DecalReplayReport? replayed;

        try
        {
            replayed = _decals.StepReplay(
                ticks,
                through,
                device,
                tick =>
                {
                    long phase = Stopwatch.GetTimestamp();
                    ShowOnly(tick);
                    counts.BuildingTicks += Stopwatch.GetTimestamp() - phase;

                    // Simulated, not drawn: `CModelRender::AddDecal` sets up the struck player's bones itself (`SkinningOf`).
                    phase = Stopwatch.GetTimestamp();
                    _moments.PoseNow(ViewFrustum.Nothing);
                    counts.PosingTicks += Stopwatch.GetTimestamp() - phase;

                    return DecalFrameAt(tick);
                });
        }
        finally
        {
            Replaying = false;
            _models.HoldsCorpses = holds;
            _moments.PlayersOnly = false;
            _models.WalksEvents = walks;
        }

        // Added up step by step: a skip's frames run between these, and their poses are theirs.
        counts.DecalTicks += Stopwatch.GetTimestamp() - began;
        counts.DecalSimulate += _models.SimulateTicks - simulate;
        counts.DecalSetup += _models.SetupTicks - setup;
        counts.DecalSkin += _models.SkinTicks - skin;

        if (replayed is not { } report)
        {
            return;
        }

        walk.DecalsUnderWay = false;

        // Stryker disable once all : a mutated interpolated string cannot bind to its handler (CS1620) and Safe Mode drops the method - B410
        _renderLog.LogInformation(
            "{Message}",
            string.Create(
                CultureInfo.InvariantCulture,
                $"model decals: a seek to {walk.Target} replayed ticks {counts.DecalStart}-{walk.Target - 1}, posing {report.Stepped} of them, in {Ms(counts.DecalTicks):0} ms " +
                $"(lookback {Ms(counts.LookbackTicks):0}, skipped ticks {report.Skipping.TotalMilliseconds:0}, building {Ms(counts.BuildingTicks):0}, " +
                $"posing {Ms(counts.PosingTicks):0} of which simulate {Ms(counts.DecalSimulate):0}, " +
                $"bones {Ms(counts.DecalSetup):0}, skin {Ms(counts.DecalSkin):0}; " +
                $"the rest placing decals); {report.Held} held"));

        static double Ms(long ticks) => ticks * 1000d / Stopwatch.Frequency;
    }

    /// <summary>
    /// A move that is no skip but is more than a second from the last decals: has them replay the ticks between in this call
    /// (<see cref="DecalEffects.Replay"/>), showing and posing each tick they ask for, as a skip's walk does.
    /// </summary>
    private void ReplayModelDecals(int target)
    {
        // Stryker disable once all : a mutated guard leaves its pattern variables unassigned (CS0165) and Safe Mode drops the method - B410
        if (_frame.Timeline is not { } timeline || _seekDevice is null)
        {
            return;
        }

        long lookingBack = Stopwatch.GetTimestamp();

        // Every played frame asks, so the walk is made only for one that needs it.
        // Stryker disable once all : a mutated guard leaves its pattern variables unassigned (CS0165) and Safe Mode drops the method - B410
        if (_decals.ReplayStart(DecalFrameAt(target), target) is not { } start)
        {
            return;
        }

        SkipWalk replay = new(timeline, null, timeline.FirstTick, target, target);

        BeginDecals(replay, start, lookingBack);

        while (replay.DecalsUnderWay)
        {
            StepDecalWalk(replay, int.MaxValue, int.MaxValue);
        }
    }
}
