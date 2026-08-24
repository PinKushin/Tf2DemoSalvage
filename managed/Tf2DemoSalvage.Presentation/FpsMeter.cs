using System;
using System.Globalization;

namespace Tf2DemoSalvage.Presentation;

/// <summary>
/// TF2's frame rate meter: <c>cl_showfps</c>, reproduced from the panel that draws it.
/// </summary>
/// <remarks>
/// **A copy of <c>CFPSPanel</c> from <c>src/game/client/vgui_fpspanel.cpp</c>**, which is published
/// in `source-sdk-2013`. Every constant here — the smoothing weight, the colour thresholds, the two
/// format strings, the truncation — is literal in that file, and
/// <c>FpsMeterConformanceTests</c> pins each one against its citation.
///
/// **Why copy rather than invent.** The owner asked for this to tell three stutters apart:
/// *"i have no idea what fps we are rendering at and cant tell stutter in the demo from stutter in
/// the decode, from stutter in fps"*. An instrument answering that must be above suspicion, and the
/// cheapest way to earn that is to behave exactly like the one he already reads in TF2. A meter of
/// our own would have to be learned, and its quirks would be indistinguishable from the stutter it
/// is measuring.
///
/// **It holds no clock.** The caller passes the frame's duration, because the viewer already
/// measures it — <c>FlyCamera</c> restarts a stopwatch every frame, and that is the same number the
/// camera flies by. A meter with its own clock would be a second opinion about the frame rate, and
/// two disagreeing measurements of the thing under investigation is the last thing B163 needs.
/// </remarks>
public sealed class FpsMeter
{
    /// <summary>Off; the panel is hidden.</summary>
    public const int Hidden = 0;

    /// <summary>One over the frame time, unsmoothed.</summary>
    public const int Instantaneous = 1;

    /// <summary>A moving average, with the worst and best single frame beside it.</summary>
    public const int Smoothed = 2;

    /// <summary>How much of each new frame enters the average.</summary>
    /// <remarks><c>const float NewWeight = 0.1f;</c> — `vgui_fpspanel.cpp`.</remarks>
    public const float SmoothingWeight = 0.1f;

    /// <summary>At or above this many frames a second the meter is green.</summary>
    /// <remarks>
    /// <c>nFPSThreshold1</c> for <c>GetDXSupportLevel() >= 95</c>. This viewer is Direct3D 11 and
    /// has no setting that makes it claim otherwise, so the lower pairs Valve keeps for DX9 and
    /// below (30/25 and 20/15) are not reproduced.
    /// </remarks>
    public const int GreenAt = 60;

    /// <summary>At or above this many frames a second the meter is yellow.</summary>
    /// <remarks><c>nFPSThreshold2</c> for the same hardware level.</remarks>
    public const int YellowAt = 50;

    /// <summary>The average, or negative when it is unseeded.</summary>
    /// <remarks>
    /// **Single precision on purpose.** Valve's is a `float`, so it accumulates single-precision
    /// error over a long run; accumulating in `double` would drift away from the number TF2 shows
    /// on the same machine — slowly, invisibly, and in a way that would only ever be noticed as a
    /// parity claim that turned out to be approximate.
    /// </remarks>
    private float _average = Unseeded;

    /// <summary>The unseeded marker, which is what <c>InitAverages</c> writes.</summary>
    private const float Unseeded = -1f;

    /// <summary>The worst and best single frame since the pair was last seeded.</summary>
    private int _low = -1;
    private int _high = -1;

    /// <summary>Whether a previous frame has been seen, so a duration can be believed.</summary>
    /// <remarks>
    /// Valve's <c>m_lastRealTime != -1.0f</c>. The frame on which the meter is switched on has no
    /// previous time to subtract, and its apparent duration is however long the meter was off.
    /// </remarks>
    private bool _seen;

    /// <summary>The mode behind it, so a transition into being shown can be noticed.</summary>
    private int _mode;

    /// <summary>Which meter to draw: 0 hidden, 1 instantaneous, 2 smoothed.</summary>
    /// <remarks>
    /// **Setting it to zero and back is what resets the watermarks**, matching <c>ShouldDraw</c>,
    /// which calls <c>InitAverages</c> only on the transition from hidden to shown. Any other value
    /// is treated as hidden, as a ConVar's unhandled integer would be.
    /// </remarks>
    public int Mode
    {
        get => _mode;

        set
        {
            if (_mode == Hidden && value != Hidden)
            {
                InitAverages();
            }

            _mode = value;
        }
    }

    /// <summary>The current average, exposed so its precision can be asserted.</summary>
    public float Average => _average;

    /// <summary>Forgets the average and both watermarks.</summary>
    private void InitAverages()
    {
        _average = Unseeded;
        _low = -1;
        _high = -1;
        _seen = false;
    }

    /// <summary>Takes one frame's duration and reports what the meter should show.</summary>
    /// <param name="frameSeconds">How long the frame took, in seconds.</param>
    /// <returns>What to draw, or null when there is nothing to draw.</returns>
    /// <remarks>
    /// Null covers three cases Valve also declines to draw: the meter is off, the frame had no
    /// measurable duration, and this is the first frame since it was shown.
    /// </remarks>
    public FpsReading? Sample(double frameSeconds)
    {
        if (_mode is not (Instantaneous or Smoothed))
        {
            return null;
        }

        // `if ( cl_showfps.GetInt() && realFrameTime > 0.0 )`, with the assignment of
        // `m_lastRealTime` that follows it outside the guard — so a frame of no duration still
        // counts as having been seen.
        if (frameSeconds <= 0d)
        {
            _seen = true;
            return null;
        }

        if (!_seen)
        {
            _seen = true;
            return null;
        }

        float frame = 1f / (float)frameSeconds;

        if (_mode == Instantaneous)
        {
            // `m_AverageFPS = -1;` in the else branch, every paint. It is why returning to mode two
            // re-seeds, watermarks included.
            _average = Unseeded;

            return new FpsReading((int)frame, _low, _high, frameSeconds * 1000d, Smoothed: false);
        }

        if (_average < 0f)
        {
            _average = frame;
            _high = (int)_average;
            _low = (int)_average;
        }
        else
        {
            _average *= 1f - SmoothingWeight;
            _average += frame * SmoothingWeight;
        }

        // The pair tracks the INSTANTANEOUS rate, so it can bracket a number the average never
        // reaches — which is the point of showing all three.
        int whole = (int)frame;

        if (whole < _low)
        {
            _low = whole;
        }

        if (whole > _high)
        {
            _high = whole;
        }

        return new FpsReading((int)_average, _low, _high, frameSeconds * 1000d, Smoothed: true);
    }

    /// <summary>The colour a rate is drawn in.</summary>
    /// <param name="fps">The rate being drawn.</param>
    /// <returns>Red, green and blue.</returns>
    /// <remarks>
    /// <c>GetFPSColor</c>. The yellow is (255, 255, 0) rather than an amber of someone's choosing
    /// because that branch sets only the green channel and leaves red at 255.
    /// </remarks>
    public static (byte Red, byte Green, byte Blue) ColourFor(int fps) => fps switch
    {
        >= GreenAt => (0, 255, 0),
        >= YellowAt => (255, 255, 0),
        _ => (255, 0, 0),
    };
}
