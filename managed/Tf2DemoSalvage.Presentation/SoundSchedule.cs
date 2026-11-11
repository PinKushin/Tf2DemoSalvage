using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Presentation;

/// <summary>
/// Which sounds should start as playback moves from one tick to another.
/// </summary>
/// <remarks>
/// **A cursor over a tick-ordered list, not a search per frame.** `DemoTimeline.Sounds` is ordered
/// and playback normally moves forward a few ticks at a time, so the common case is walking a few
/// entries. A scan would be the whole list sixty times a second for the life of the demo.
///
/// **Seeking is the interesting case and the reason this is a type rather than a loop.** A scrub
/// from tick 200 to tick 5000 skips four thousand ticks of sounds, and playing them would empty a
/// match's worth of gunfire into one frame — every sound the viewer "missed", at once, none of them
/// belonging to the moment now on screen. So a jump repositions the cursor without yielding
/// anything, and only ordinary advancement plays; what a skip's ticks still start — the engine's
/// static sounds and stops — the caller dispatches itself (B550).
///
/// **Where the line falls is a judgement and it is written down rather than tuned in silence.**
/// <see cref="CatchUpTicks"/> is two seconds at TF2's 66.7 tick, which comfortably covers a stalled
/// frame, a garbage collection or the first frame after a pause, and is far below any deliberate
/// scrub. Nothing measures it; it is a threshold between two behaviours that are each obviously
/// right on their own side of it.
///
/// This type is in `Presentation` because it is the playback decision and has no window in it —
/// D62's split. The viewer owns resolving a name to a file, computing gain from the camera, and the
/// device; none of that is here.
/// </remarks>
public sealed class SoundSchedule
{
    /// <summary>How far playback may advance in one step and still be treated as continuous.</summary>
    /// <remarks>
    /// About two seconds at 66.7 tick. Above this the movement is read as a seek: the cursor is
    /// repositioned and nothing plays, because the sounds in between belong to moments the viewer
    /// passed over rather than watched.
    /// </remarks>
    public const int CatchUpTicks = 133;

    private readonly IReadOnlyList<SceneSound> _sounds;

    /// <summary>The first entry not yet played.</summary>
    private int _cursor;

    /// <summary>Where playback was when it was last asked, so a jump can be recognised.</summary>
    private int _tick;

    /// <summary>Reads a timeline's sounds.</summary>
    /// <param name="sounds">The sounds, in tick order.</param>
    /// <exception cref="ArgumentNullException"><paramref name="sounds"/> is null.</exception>
    public SoundSchedule(IReadOnlyList<SceneSound> sounds)
    {
        ArgumentNullException.ThrowIfNull(sounds);

        _sounds = sounds;
        _tick = int.MinValue;
    }

    /// <summary>Whether the last call was treated as a seek rather than as playback.</summary>
    public bool Jumped { get; private set; }

    /// <summary>Whether the cursor was just placed: the move was a skip, whose ticks still dispatch their static sounds and stops.</summary>
    /// <remarks>
    /// **Not the same question as <see cref="Jumped"/>.** `Jumped` is false on the first call, because nothing is
    /// playing yet; this is true then too, because opening a demo at a tick is `StartPlayback` and a skip from its start.
    /// The caller dispatches what the skipped ticks still start (B550, `SoundPresenter.SkipDispatched`): cp_process starts
    /// six static `)ambient/machine_hum.wav` at tick 4 and restarts them only at round boundaries, so a skip past tick 4
    /// that dispatched nothing would leave the map silent.
    /// </remarks>
    public bool Repositioned { get; private set; }

    /// <summary>Whether moving playback from one tick to another is a seek rather than playback: <see cref="Advance"/>'s rule.</summary>
    /// <param name="previous">Where playback was, or null when it was nowhere.</param>
    /// <param name="tick">Where it is now.</param>
    /// <returns>True for the first move, a move back, or one past <see cref="CatchUpTicks"/>.</returns>
    public static bool IsSkip(int? previous, int tick) =>
        previous is not { } last || tick < last || tick - last > CatchUpTicks;

    /// <summary>Moves playback to a tick and answers what should start.</summary>
    /// <param name="tick">Where playback now is.</param>
    /// <returns>The sounds to start, in the order the server sent them.</returns>
    /// <remarks>
    /// **Half-open on the low side**: a sound exactly at the new tick plays, one at the tick already
    /// played does not. Without that a paused frame re-triggers whatever sits on its tick, every
    /// frame, which is a stutter rather than a sound.
    ///
    /// **The first call never plays anything.** There is no previous position for it to have
    /// advanced from, so it positions the cursor and returns nothing — otherwise opening a demo
    /// fires every sound before tick one, including the map ambience the signon put there.
    /// </remarks>
    /// <param name="skipped">The frame's one skip decision for this move (<see cref="FrameSkip"/>, D221), or null to decide here.</param>
    public IReadOnlyList<SceneSound> Advance(int tick, bool? skipped = null)
    {
        bool first = _tick == int.MinValue;
        int previous = _tick;

        _tick = tick;

        if (skipped ?? IsSkip(first ? null : previous, tick))
        {
            Jumped = !first;
            Repositioned = true;
            _cursor = FirstAtOrAfter(tick);

            return [];
        }

        Jumped = false;
        Repositioned = false;

        List<SceneSound> starting = [];

        while (_cursor < _sounds.Count && _sounds[_cursor].Tick <= tick)
        {
            // **Strictly after the previous tick**, so nothing on the tick just played repeats.
            // The cursor cannot simply be trusted for this: a seek positions it by tick, and the
            // entry it lands on may sit exactly on the tick that was sought to.
            if (_sounds[_cursor].Tick > previous)
            {
                starting.Add(_sounds[_cursor]);
            }

            _cursor++;
        }

        return starting;
    }

    /// <summary>Every sound at or before a tick, in order — what playing from the start to it has started.</summary>
    /// <param name="tick">The tick.</param>
    /// <returns>The sounds, lazily.</returns>
    public IEnumerable<SceneSound> Through(int tick)
    {
        for (int index = 0; index < _sounds.Count && _sounds[index].Tick <= tick; index++)
        {
            yield return _sounds[index];
        }
    }

    /// <summary>Positions the cursor at the first sound at or after a tick.</summary>
    /// <remarks>
    /// Binary search rather than a scan: a seek to the end of a long demo would otherwise walk
    /// every sound in it, and a scrub does that on every drag of the bar.
    /// </remarks>
    private int FirstAtOrAfter(int tick)
    {
        int low = 0;
        int high = _sounds.Count;

        while (low < high)
        {
            int middle = low + ((high - low) / 2);

            if (_sounds[middle].Tick < tick)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }
}
