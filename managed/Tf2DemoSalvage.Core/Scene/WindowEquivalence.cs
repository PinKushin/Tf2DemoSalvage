using System;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Core.Scene;

/// <summary>Whether a window answers a moment as the whole timeline does — the seek proof, for a caller outside Core.</summary>
/// <remarks>
/// **The same walk the probe and the checkpoint tests compare** (<see cref="TimelineAnswers"/>), so the viewer's own check
/// (D217 step 4, the window cache) cannot disagree with them about what was compared.
/// </remarks>
public static class WindowEquivalence
{
    /// <summary>The questions whose answers differ between two timelines at one moment.</summary>
    /// <param name="whole">The whole timeline.</param>
    /// <param name="window">A window covering the moment.</param>
    /// <param name="tick">The moment.</param>
    /// <param name="settings">The settings the window was walked under.</param>
    /// <returns>The differing questions, empty when every answer is the same; and how many values were compared.</returns>
    /// <exception cref="ArgumentNullException">A timeline is null.</exception>
    public static (IReadOnlyList<string> Differing, long Values) Compare(
        DemoTimeline whole, DemoTimeline window, double tick, WindowSettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(whole);
        ArgumentNullException.ThrowIfNull(window);

        List<(string Question, object? Answer)> expected = TimelineAnswers.At(whole, tick, settings);
        List<(string Question, object? Answer)> actual = TimelineAnswers.At(window, tick, settings);
        // Counted first: a window with an answer the whole has not, or one fewer, differs whatever the shared ones say.
        List<string> differing = expected.Count == actual.Count ? [] : [$"answer count {actual.Count} against {expected.Count}"];
        long values = 0;

        for (int index = 0; index < expected.Count; index++)
        {
            (string hash, long count) = TimelineFingerprint.OfAnswer(expected[index].Answer);
            values += count;

            if (index >= actual.Count || TimelineFingerprint.OfAnswer(actual[index].Answer).Hash != hash)
            {
                differing.Add(expected[index].Question);
            }
        }

        return (differing, values);
    }
}
