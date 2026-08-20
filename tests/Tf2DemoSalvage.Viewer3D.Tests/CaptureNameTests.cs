using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Viewer3D;

namespace Tf2DemoSalvage.Viewer3D.Tests;

/// <summary>
/// Naming a screenshot so two of them cannot be the same file.
/// </summary>
/// <remarks>
/// **Two captures in the same second overwrote each other**, measured 2026-08-20 while capturing
/// the map view and the first-person view to compare them: both landed in
/// <c>shot-20260820-000241.png</c> and only the second survived. The log said so —
///
/// <code>
/// 00:02:41.286 [render] wrote shot-20260820-000241.png, 984x551, 305 KB
/// 00:02:41.614 [render] wrote shot-20260820-000241.png, 984x551, 416 KB
/// </code>
///
/// — which is the only reason it was noticed at all, and the reason that log line had been added
/// an hour earlier. Without it the run reports success, one file exists, and nothing says which
/// view it holds.
///
/// **A second is not a long time for a person pressing a key twice**, and it is no time at all for
/// automation: the capture that found this was driven by a UI test and the two presses were 328
/// milliseconds apart.
/// </remarks>
public sealed class CaptureNameTests
{
    [Test]
    public void CaptureName_TwoCapturesInTheSameSecond_AreDifferentFiles()
    {
        // The defect, stated directly. 328 ms apart is what the run that found it actually did.
        DateTime first = new(2026, 8, 20, 0, 2, 41, 286, DateTimeKind.Local);
        DateTime second = new(2026, 8, 20, 0, 2, 41, 614, DateTimeKind.Local);

        MainForm.CaptureName(first).ShouldNotBe(MainForm.CaptureName(second));
    }

    [Test]
    public void CaptureName_TwoCapturesAMillisecondApart_AreStillDifferent()
    {
        // The resolution actually claimed, rather than a comfortable margin. A name that only
        // separated tenths would pass the test above and fail here, and automation is quick
        // enough to reach it.
        DateTime first = new(2026, 8, 20, 0, 2, 41, 286, DateTimeKind.Local);

        MainForm.CaptureName(first).ShouldNotBe(MainForm.CaptureName(first.AddMilliseconds(1)));
    }

    [Test]
    public void CaptureName_SortedByName_IsSortedByTime()
    {
        // **Retention depends on this.** FileRetention.Keep decides which captures to delete by
        // ORDINAL NAME ORDER, so a stamp whose text order disagreed with its time order would keep
        // the wrong ones — and it would do it silently, since the count would still be right.
        //
        // The times below cross a second, a minute and an hour boundary, which is where a
        // hand-rolled format usually breaks.
        List<DateTime> times =
        [
            new(2026, 8, 20, 0, 2, 41, 286, DateTimeKind.Local),
            new(2026, 8, 20, 0, 2, 41, 614, DateTimeKind.Local),
            new(2026, 8, 20, 0, 2, 42, 1, DateTimeKind.Local),
            new(2026, 8, 20, 0, 3, 0, 0, DateTimeKind.Local),
            new(2026, 8, 20, 1, 0, 0, 0, DateTimeKind.Local),
        ];

        List<string> names = [.. times.Select(MainForm.CaptureName)];

        names.OrderBy(name => name, StringComparer.Ordinal).ShouldBe(names);
    }

    [Test]
    public void CaptureName_StillMatchesWhatRetentionSweeps()
    {
        // The prune pattern is `shot-*.png`, and a name that stopped matching it would never be
        // deleted — which is how the 233 screenshots that started all this accumulated.
        string name = MainForm.CaptureName(new DateTime(2026, 8, 20, 0, 2, 41, 286, DateTimeKind.Local));

        name.ShouldStartWith("shot-");
        name.ShouldEndWith(".png");
    }
}
