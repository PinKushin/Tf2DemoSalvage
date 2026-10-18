using System;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Core.Scene;

/// <summary>Whether a window answers a moment as the whole timeline does — the seek proof, for a caller outside Core.</summary>
/// <remarks>
/// **The same walk the probe and the checkpoint tests compare** (<see cref="TimelineAnswers"/>), so the viewer's own check
/// (D217 step 4, the window cache) cannot disagree with them about what was compared. Answer COUNTS are compared too: a
/// window with an answer the whole has not, or one fewer, differs whatever the shared ones say.
/// </remarks>
public sealed class WindowEquivalence
{
    private readonly IEnumerator<(string Question, object? Answer)> _expected;

    private readonly List<(string Question, object? Answer)> _actual;

    private readonly List<string> _differing = [];

    private int _index;

    private WindowEquivalence(DemoTimeline whole, List<(string, object?)> actual, double tick, WindowSettings? settings)
    {
        _expected = TimelineAnswers.Each(whole, tick, settings).GetEnumerator();
        _actual = actual;
    }

    /// <summary>The questions whose answers differ so far.</summary>
    public IReadOnlyList<string> Differing => _differing;

    /// <summary>How many values were compared so far.</summary>
    public long Values { get; private set; }

    /// <summary>Whether every question has been compared.</summary>
    public bool Done { get; private set; }

    /// <summary>The questions whose answers differ between two timelines at one moment, all at once.</summary>
    /// <param name="whole">The whole timeline.</param>
    /// <param name="window">A window covering the moment.</param>
    /// <param name="tick">The moment.</param>
    /// <param name="settings">The settings the window was walked under.</param>
    /// <returns>The differing questions, empty when every answer is the same; and how many values were compared.</returns>
    /// <exception cref="ArgumentNullException">A timeline is null.</exception>
    public static (IReadOnlyList<string> Differing, long Values) Compare(
        DemoTimeline whole, DemoTimeline window, double tick, WindowSettings? settings = null)
    {
        WindowEquivalence comparing = Start(whole, window, tick, settings);

        while (!comparing.Step())
        {
            // Every question in turn.
        }

        return (comparing.Differing, comparing.Values);
    }

    /// <summary>
    /// A comparison to make one question at a time: the window's answers all at once (a window is the caller's alone, so a
    /// worker may ask it), the whole timeline's one per <see cref="Step"/> on the thread that owns it.
    /// </summary>
    /// <param name="whole">The whole timeline.</param>
    /// <param name="window">A window covering the moment.</param>
    /// <param name="tick">The moment.</param>
    /// <param name="settings">The settings the window was walked under.</param>
    /// <returns>The comparison, not yet stepped.</returns>
    /// <exception cref="ArgumentNullException">A timeline is null.</exception>
    public static WindowEquivalence Start(DemoTimeline whole, DemoTimeline window, double tick, WindowSettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(whole);
        ArgumentNullException.ThrowIfNull(window);

        return new WindowEquivalence(whole, TimelineAnswers.At(window, tick, settings), tick, settings);
    }

    /// <summary>Compares the next question.</summary>
    /// <returns>True once every question has been compared, counts included.</returns>
    public bool Step()
    {
        if (Done)
        {
            return true;
        }

        if (!_expected.MoveNext())
        {
            if (_index != _actual.Count)
            {
                _differing.Add($"answer count {_actual.Count} against {_index}");
            }

            _expected.Dispose();
            Done = true;
            return true;
        }

        (string question, object? answer) = _expected.Current;
        (string hash, long count) = TimelineFingerprint.OfAnswer(answer);
        Values += count;

        if (_index >= _actual.Count || _actual[_index].Question != question || TimelineFingerprint.OfAnswer(_actual[_index].Answer).Hash != hash)
        {
            _differing.Add(question);
        }

        _index++;
        return false;
    }
}
