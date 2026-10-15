using System;
using System.IO;

namespace Tf2DemoSalvage.Presentation;

/// <summary>What a <see cref="ViewerSession"/> asks of the window that hosts it: controls, and nothing decided (D214).</summary>
/// <remarks>
/// **Every member is a control read or a control write.** The session decides what a frame, a key, a capture or a
/// startup means; the window says how big its viewport is, shows a status, ticks a menu item, repaints, closes. A
/// second front end (the owner's reason for the seam is in <c>ViewerMenu</c>) implements this and
/// <see cref="IPlaybackView"/> and has a viewer.
/// </remarks>
public interface IViewerWindow
{
    /// <summary>Runs something on the UI thread and waits for its answer.</summary>
    /// <typeparam name="T">What the work answers.</typeparam>
    /// <param name="work">The work.</param>
    /// <returns>Its answer.</returns>
    public T OnUi<T>(Func<T> work);

    /// <summary>The status line; null shows as empty.</summary>
    public string? Status { get; set; }

    /// <summary>Shows what a load is doing over the viewport.</summary>
    /// <param name="stage">The stage's name.</param>
    /// <param name="fraction">How far through it, or null when that is not known.</param>
    public void ReportLoading(string stage, double? fraction);

    /// <summary>Takes the loading display away.</summary>
    public void HideLoading();

    /// <summary>The viewport's client size, in pixels.</summary>
    public (int Width, int Height) Viewport { get; }

    /// <summary>Whether keyboard focus is inside the window.</summary>
    public bool HasFocus { get; }

    /// <summary>Whether the window is full screen.</summary>
    public bool IsFullScreen { get; }

    /// <summary>Asks for the viewport to be painted again.</summary>
    public void Redraw();

    /// <summary>Closes the window.</summary>
    /// <param name="posted">Whether to post the close behind the messages already queued rather than close now.</param>
    public void Close(bool posted);

    /// <summary>Leaves full screen.</summary>
    public void LeaveFullScreen();

    /// <summary>Ticks or clears the surface-colours item; the window reports the change back as any click does.</summary>
    /// <param name="ticked">Whether it is ticked.</param>
    public void ShowSurfaceColours(bool ticked);

    /// <summary>Ticks or clears the reflections item.</summary>
    /// <param name="ticked">Whether it is ticked.</param>
    public void ShowSpecular(bool ticked);

    /// <summary>Ticks the lighting item for a <c>mat_fullbright</c> value and clears the others.</summary>
    /// <param name="mode">The value: 0, 1 or 2.</param>
    public void ShowFullbright(int mode);

    /// <summary>Ticks the full-screen mode item that is chosen.</summary>
    /// <param name="exclusive">Whether exclusive is the one chosen.</param>
    public void ShowFullScreenExclusive(bool exclusive);

    /// <summary>Ticks the texture-quality item that is chosen.</summary>
    /// <param name="quality">Its level name.</param>
    public void ShowTextureQuality(string quality);

    /// <summary>Shows a playlist of this many rows; the rows themselves are asked for one at a time as they are drawn.</summary>
    /// <param name="rows">How many rows the list has now.</param>
    public void ShowPlaylist(int rows);

    /// <summary>Shows the keys the menu's items answer to.</summary>
    /// <param name="bindings">The bindings in force.</param>
    public void ShowBindings(KeyBindings bindings);
}

/// <summary>What the idle loop asks of the message queue and the thread it runs on.</summary>
/// <remarks>
/// The loop's decisions — when a frame is due, when to sleep, when to give the thread back — are
/// <see cref="ViewerSession.Idle"/>'s. Looking at the queue and sleeping are the platform's, and are these four.
/// </remarks>
public interface IIdleHost
{
    /// <summary>Whether a message arrives within a wait.</summary>
    /// <param name="seconds">How long to wait.</param>
    /// <returns>Whether one did.</returns>
    public bool ArrivesWithin(double seconds);

    /// <summary>The message waiting at the head of the queue, or null when it is empty.</summary>
    /// <returns>Its number.</returns>
    public uint? Waiting();

    /// <summary>Sleeps this thread.</summary>
    /// <param name="milliseconds">For how long.</param>
    public void Sleep(int milliseconds);

    /// <summary>Gives the rest of this thread's time slice away.</summary>
    public void Yield();
}

/// <summary>The process a session runs in: its two streams, its exit code, its environment and its clocks.</summary>
/// <param name="Out">Standard output, where <c>--measure</c> prints.</param>
/// <param name="Error">Standard error, where a headless run that cannot go on says why.</param>
/// <param name="SetExitCode">Sets the process's exit code.</param>
/// <param name="Environment">Reads an environment variable.</param>
/// <param name="Clock">Makes a stopped clock.</param>
/// <remarks>
/// **A record so a test can stand in for the process.** A headless run's whole result is what it printed and what it
/// exited with, and neither can be asserted while they are <see cref="Console"/> and <see cref="System.Environment"/>.
/// </remarks>
public sealed record ViewerHost(
    TextWriter Out,
    TextWriter Error,
    Action<int> SetExitCode,
    Func<string, string?> Environment,
    Func<IElapsedTime> Clock)
{
    /// <summary>The real process.</summary>
    public static ViewerHost Process { get; } = new(
        Console.Out,
        Console.Error,
        static code => System.Environment.ExitCode = code,
        System.Environment.GetEnvironmentVariable,
        static () => new StopwatchTime());
}

/// <summary>One key press, in terms no toolkit owns.</summary>
/// <param name="Name">The key's Source name with no modifiers: <c>SPACE</c>, <c>F12</c>, <c>w</c>.</param>
/// <param name="ControlOrAlt">Whether Control or Alt is held with it.</param>
/// <param name="Focus">What kind of control has the keyboard.</param>
/// <param name="IsChord">Whether a binding's key string names exactly this press, modifiers included.</param>
/// <param name="IsEscape">Whether it is Escape alone.</param>
/// <param name="Described">The press as the toolkit prints it, for the log.</param>
/// <param name="IsRepeat">Whether it is the toolkit's auto-repeat of a key already down, which runs no bind.</param>
public readonly record struct KeyPress(
    string Name,
    bool ControlOrAlt,
    FocusedWidget Focus,
    Func<string?, bool> IsChord,
    bool IsEscape,
    string Described,
    bool IsRepeat = false);
