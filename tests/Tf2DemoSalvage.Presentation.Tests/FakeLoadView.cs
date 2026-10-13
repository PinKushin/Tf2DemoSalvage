using System;
using System.Collections.Generic;
using System.Linq;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>A window for <see cref="LoadPresenter"/> that writes down what it was asked, in order, and how (D214).</summary>
/// <remarks>
/// **Two facts beside each name, because they are what a moved load could get wrong and still finish**: whether the
/// call came through <see cref="OnUi{T}"/> — the old code's marshal to the window's thread — and whether the map read's
/// barrier was up when it arrived. A window that is asked for the same things in the same order from the wrong side of
/// either draws a half-replaced world.
/// </remarks>
internal sealed class FakeLoadView : ILoadView
{
    /// <summary>The decode's stage; its progress reports are posted and arrive when they arrive, so the order ignores them.</summary>
    public const string Decoding = "Decoding the demo";

    [ThreadStatic]
    private static int marshalled;

    private readonly List<(string Name, bool OnUi, bool Reading)> _calls = [];
    private string _status = "No demo loaded.";

    /// <summary>Whether the map read's barrier is up right now: the presenter's own answer.</summary>
    public Func<bool> Reading { get; set; } = static () => false;

    /// <summary>Told each call's name as it arrives, for a test that reads something else at that moment.</summary>
    public Action<string>? Asked { get; set; }

    /// <summary>What the window answers for autoplay.</summary>
    public (bool Play, string Reason) AutoPlay { get; set; } = (false, "test");

    /// <summary>Every call but the decode's posted progress, in order.</summary>
    public IReadOnlyList<(string Name, bool OnUi, bool Reading)> Calls
    {
        get
        {
            lock (_calls)
            {
                return [.. _calls];
            }
        }
    }

    /// <summary>The names alone.</summary>
    public IReadOnlyList<string> Names => [.. Calls.Select(static call => call.Name)];

    public string Status
    {
        get => _status;
        set
        {
            _status = value;
            Note("Status: " + value);
        }
    }

    public T OnUi<T>(Func<T> work)
    {
        ArgumentNullException.ThrowIfNull(work);

        marshalled++;

        try
        {
            return work();
        }
        finally
        {
            marshalled--;
        }
    }

    public void ReportLoading(string stage, double? fraction)
    {
        // The load's own first report is written down; the decode's progress after it is posted, and is not.
        if (stage != Decoding || Names.Count(static name => name == "Report: " + Decoding) < Loads)
        {
            Note("Report: " + stage);
        }
    }

    /// <summary>How many loads the test has started: each makes one decode report of its own.</summary>
    public int Loads { get; set; } = 1;

    public void HideLoading() => Note(nameof(HideLoading));

    public void ClearWorld() => Note(nameof(ClearWorld));

    public void MapReading() => Note(nameof(MapReading));

    public void MapRead() => Note(nameof(MapRead));

    public void DemoOpening() => Note(nameof(DemoOpening));

    public void DemoOpened() => Note(nameof(DemoOpened));

    public void DemoShown() => Note(nameof(DemoShown));

    private void Note(string name)
    {
        lock (_calls)
        {
            _calls.Add((name, marshalled > 0, Reading()));
        }

        Asked?.Invoke(name);
    }
}
