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
}

/// <summary>The countdown from a window opening to a capture being taken.</summary>
/// <param name="shotPath">Where to write a capture, or null when none was asked for.</param>
/// <param name="openingFrames">Frames to wait before capturing.</param>
/// <param name="settleFrames">Frames into the wait at which the opening state is applied.</param>
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
public sealed class OpeningSequence(string? shotPath, int openingFrames, int settleFrames)
{
    private readonly int _openingFrames = openingFrames > 0
        ? openingFrames
        : throw new ArgumentOutOfRangeException(nameof(openingFrames));

    private int _framesLeft = openingFrames;

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
    public void MarkApplied() => Applied = true;

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
    public OpeningStep Advance()
    {
        if (Finished)
        {
            return OpeningStep.Nothing;
        }

        if (_framesLeft-- > 0)
        {
            return _framesLeft == _openingFrames - settleFrames
                ? OpeningStep.ApplyOpeningState
                : OpeningStep.Nothing;
        }

        return _shotPath is null ? OpeningStep.Nothing : OpeningStep.Capture;
    }
}
