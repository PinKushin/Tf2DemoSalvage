using System;

namespace Tf2DemoSalvage.Presentation;

/// <summary>What the opening countdown wants done this frame.</summary>
public enum OpeningStep
{
    /// <summary>Keep waiting.</summary>
    Nothing,

    /// <summary>Seek to the opening tick and apply the launch options.</summary>
    ApplyOpeningState,

    /// <summary>Write the capture and close.</summary>
    Capture,

    /// <summary>No demo ever arrived — say so and close, writing nothing.</summary>
    /// <remarks>
    /// **A capture that waits for a condition must be able to give up, or a run that loses its demo
    /// hangs for ever** (B401). The first version of that wait did exactly that: a command line
    /// whose demo path never reached the window sat drawing an empty viewport at 299 fps until an
    /// outer timeout killed it, which is a worse automation failure than the junk picture it
    /// replaced. **Writing nothing is the point** — an empty PNG is what B196, B387 and B401 all
    /// cost, because it reads as "the viewer drew nothing" rather than "the viewer had nothing".
    /// </remarks>
    GiveUp,
}

/// <summary>The countdown from a window opening to a capture being taken.</summary>
/// <param name="shotPath">Where to write a capture, or null when none was asked for.</param>
/// <param name="openingFrames">Frames to wait before capturing.</param>
/// <param name="settleFrames">Frames into the wait at which the opening state is applied.</param>
/// <param name="patienceFrames">
/// How many frames a capture run waits for a demo before giving up and writing nothing (B401).
/// </param>
/// <remarks>
/// **Three fields and a countdown inside `MainForm`** (B188, D90) — `_shotDelay`, `_shotPath` and
/// `_openingDone`. None of it needed a window, and none of it could be exercised without launching
/// the viewer with `--shot` and looking for a file afterwards.
///
/// **The split is `FramePacer`'s: the decision is here, the act is the caller's.** Seeking, capturing
/// and closing are all things a window does; when to do them is arithmetic.
///
/// **Why a countdown at all, rather than doing it on the first frame.** The world has not settled,
/// the textures upload on a later frame, and a seek into a scene that is not ready then latches
/// itself done. The first version applied the opening state immediately and produced exactly that.
///
/// **And why it restarts on a demo rather than on the window.** A demo opened from the playlist
/// arrives long after the frame the countdown fired on, so the opening state was simply lost. The
/// wait is measured from the demo now, which keeps the original reasoning and fixes the case it
/// missed.
/// </remarks>
public sealed class OpeningSequence(
    string? shotPath, int openingFrames, int settleFrames, int patienceFrames)
{
    private readonly int _openingFrames = openingFrames > 0
        ? openingFrames
        : throw new ArgumentOutOfRangeException(nameof(openingFrames));

    private int _framesLeft = openingFrames;

    /// <summary>Frames spent waiting for a demo that has not arrived.</summary>
    /// <remarks>
    /// **Separate from <c>_framesLeft</c>, which `Restart` resets.** The patience has to survive a
    /// restart or a viewer that keeps restarting its countdown never runs out of it.
    /// </remarks>
    private int _waited;

    private string? _shotPath = shotPath;

    /// <summary>Whether there is nothing left for this sequence to ask for.</summary>
    /// <remarks>
    /// **Both halves must be done**: the opening state applied, and any capture taken. A viewer
    /// nobody asked a capture from still has to stop counting, or every frame for the rest of the
    /// session runs a countdown that can never do anything.
    /// </remarks>
    public bool Finished => _shotPath is null && Applied;

    /// <summary>Whether the caller has confirmed it applied the opening state.</summary>
    /// <remarks>
    /// **The caller's to declare, because applying can fail.** With no demo open there is nothing to
    /// seek to, and a sequence that marked itself applied would count a refusal as a success and
    /// never offer again.
    /// </remarks>
    public bool Applied { get; private set; }

    /// <summary>Records that the opening state was actually applied.</summary>
    /// <remarks>
    /// **The settle wait is measured from HERE, not from the window opening**, so the countdown
    /// restarts. The wait exists so the map, its textures and the entity models are in place before
    /// the shutter; measured from the window it expired long before a demo had even been read
    /// (B401).
    /// </remarks>
    public void MarkApplied()
    {
        Applied = true;

        Restart();
    }

    /// <summary>Starts the wait again, measured from now.</summary>
    public void Restart() => _framesLeft = _openingFrames;

    /// <summary>Takes the capture path, once.</summary>
    /// <returns>The path, or null when there is none or it has already been taken.</returns>
    /// <remarks>
    /// **Taken rather than read**, because the capture closes the window: a second one is a race
    /// rather than a duplicate file.
    /// </remarks>
    public string? TakeShotPath()
    {
        string? path = _shotPath;

        _shotPath = null;

        return path;
    }

    /// <summary>Advances one frame and says what to do.</summary>
    /// <returns>The step for this frame.</returns>
    /// <remarks>
    /// **The capture cannot come before the opening state, and for a while it did** (B401). The
    /// countdown began at the first frame and ran on a FRAME budget: 45 frames at the ~300 fps an
    /// empty viewport draws at is a seventh of a second, where opening a demo and its map takes
    /// twenty seconds. So `--shot` photographed an empty viewport and closed, and the log said
    /// `no map` in the corner of a 12 KB picture. `Restart` on a loaded demo could not save it —
    /// by then the shutter had already fired.
    ///
    /// **So the two halves are ordered by a CONDITION rather than sharing one clock.** Until the
    /// window confirms the opening state took, this asks for it — every frame past the settle
    /// point, because a demo can finish loading on any frame and a single-frame offer is a race.
    /// Only afterwards does the capture countdown run, and `MarkApplied` restarts it so the wait
    /// that exists for textures and models is measured from the seek that needs them.
    /// </remarks>
    public OpeningStep Advance()
    {
        if (Finished)
        {
            return OpeningStep.Nothing;
        }

        if (!Applied)
        {
            // **Past the settle point, keep asking.** The window refuses while there is no demo,
            // and says so by not calling `MarkApplied`.
            _framesLeft--;

            // **But a wait on a condition needs a way out**, or a run whose demo never arrives
            // draws an empty viewport for ever. Only when a capture was asked for: a person who
            // opened the viewer to look at it is not waiting on anything.
            if (_shotPath is not null && ++_waited > patienceFrames)
            {
                return OpeningStep.GiveUp;
            }

            return _framesLeft > _openingFrames - settleFrames
                ? OpeningStep.Nothing
                : OpeningStep.ApplyOpeningState;
        }

        return _framesLeft-- > 0 || _shotPath is null
            ? OpeningStep.Nothing
            : OpeningStep.Capture;
    }
}
