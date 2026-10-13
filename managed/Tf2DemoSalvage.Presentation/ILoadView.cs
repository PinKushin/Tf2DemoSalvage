using System;

namespace Tf2DemoSalvage.Presentation;

/// <summary>What a load asks of the window it fills: its thread, its words, its device and its camera (D214).</summary>
/// <remarks>
/// **Each member is a place the load reached into the window when it lived there**, called at that same moment by
/// <see cref="LoadPresenter"/> [no engine counterpart: the engine's loader and its window are one program]. The view
/// keeps the dialog, the progress display and what only a window has; it decides nothing about a load's order.
///
/// **No member takes or returns a domain type.** The window asks the presenter for the demo and the map when one of
/// these runs, so implementing this adds nothing to what the view reaches.
/// </remarks>
public interface ILoadView
{
    /// <summary>Runs something on the window's thread and waits for its answer; on that thread already, runs it here.</summary>
    /// <typeparam name="T">What the work answers.</typeparam>
    /// <param name="work">The work.</param>
    /// <returns>The work's answer.</returns>
    public T OnUi<T>(Func<T> work);

    /// <summary>The status line.</summary>
    public string Status { get; set; }

    /// <summary>Whether a newly opened demo starts playing, and the reason the log gives (`--autoplay`, or the variable).</summary>
    public (bool Play, string Reason) AutoPlay { get; }

    /// <summary>Shows what the load is doing over the viewport.</summary>
    /// <param name="stage">The stage's words.</param>
    /// <param name="fraction">How far through, or null where the stage cannot say.</param>
    public void ReportLoading(string stage, double? fraction);

    /// <summary>Takes the progress display down: no load is in flight.</summary>
    public void HideLoading();

    /// <summary>Drops the device's world. UI thread.</summary>
    public void ClearWorld();

    /// <summary>A map was read and the presenter holds it: project it.</summary>
    public void MapRead();

    /// <summary>The demo's systems are open: `--speed`. The camera is told next, by the load itself.</summary>
    public void DemoOpened();

    /// <summary>The demo and its map are in place: restart whatever waits on a settled scene.</summary>
    public void DemoShown();
}
