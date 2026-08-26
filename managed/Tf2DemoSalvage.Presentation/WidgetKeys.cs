using System;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Presentation;

/// <summary>The kind of thing that has keyboard focus, named without naming a toolkit.</summary>
/// <remarks>
/// **Deliberately not a WinForms type, and this project cannot make it one.** `Presentation` targets
/// plain `net10.0`, so the compiler refuses a reference to `System.Windows.Forms` — the enforcement
/// is the TFM rather than a convention anyone has to remember (D54, D62, D90, and
/// `docs/memory/a-partial-thin-view-is-worse-than-none.md`).
///
/// **These five kinds exist in every toolkit** — WinForms, WPF, Avalonia, MAUI, GTK, Qt, HTML — which
/// is the point. Porting the front end means writing the ten-line adapter that maps that toolkit's
/// focused control onto one of these, not working the key rules out again.
/// </remarks>
public enum FocusedWidget
{
    /// <summary>Nothing has focus, or nothing that consumes keys.</summary>
    None,

    /// <summary>Somewhere text is typed.</summary>
    Text,

    /// <summary>A slider, scrollbar or spinner — anything with a value along a range.</summary>
    Slider,

    /// <summary>A list, grid or drop-down — anything with a selected row.</summary>
    List,

    /// <summary>A button, checkbox or similar.</summary>
    Button,

    /// <summary>Something focusable that claims no navigation keys.</summary>
    Other,
}

/// <summary>
/// Which keys a focused widget uses itself, and therefore must not lose to a global shortcut.
/// </summary>
/// <remarks>
/// **The B212 family, generalised.** A viewer-wide shortcut is dispatched before any widget sees the
/// key — `ProcessCmdKey` in WinForms, a tunnelling `PreviewKeyDown` elsewhere — so a binding on a key
/// the focused widget needs silently takes it away. The first instance was `Home`, `PageUp` and
/// `PageDown` reaching over the search box; the guard written then asked **what type has focus**, and
/// excused only text.
///
/// **That guard was still wrong, independently of any one key.** D101 lets a person bind anything, so
/// `bind "UPARROW" "+forward"` in someone's config takes the arrow keys away from the playlist —
/// a defect nobody had to introduce, waiting for a user to write one line. The question is not "is
/// this a text box", it is **"does the thing with focus already use this key"**.
///
/// **Text keeps everything, and that is not laziness.** Any printable character is content while
/// somebody is typing, and so are the navigation keys; there is no subset to carve out.
///
/// **Type-ahead is deliberately NOT here.** Real list controls select by typed letters, so a strict
/// reading would have `List` keep every letter — which would stop `w`/`a`/`s`/`d` flying the camera
/// whenever the playlist had focus. That is a bigger behaviour change than this guard is for, and it
/// is a question for the owner rather than a detail to decide in passing.
/// </remarks>
public static class WidgetKeys
{
    /// <summary>Keys any value-along-a-range widget drives itself.</summary>
    /// <remarks>
    /// Left/Right and Up/Down step it, `HOME` and `END` go to the ends, and the page keys jump. True
    /// of a WinForms `TrackBar`, a WPF/Avalonia `Slider`, an HTML `input[type=range]` and a GTK
    /// `Scale` alike — this is a UI convention, not a toolkit's behaviour.
    /// </remarks>
    private static readonly HashSet<string> SliderKeys =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "LEFTARROW", "RIGHTARROW", "UPARROW", "DOWNARROW",
            "HOME", "END", "PGUP", "PGDN",
        };

    /// <summary>Keys any selected-row widget drives itself.</summary>
    /// <remarks>
    /// Up/Down move the selection, `HOME`/`END` go to the first and last row, the page keys jump a
    /// screenful. Left/Right are included because grids and trees use them and a plain list ignores
    /// them harmlessly — the cost of being wrong in that direction is one key doing nothing, against
    /// a key that silently does the wrong thing.
    /// </remarks>
    private static readonly HashSet<string> ListKeys =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "UPARROW", "DOWNARROW", "LEFTARROW", "RIGHTARROW",
            "HOME", "END", "PGUP", "PGDN",
        };

    /// <summary>Whether the focused widget uses this key itself.</summary>
    /// <param name="widget">What has focus.</param>
    /// <param name="keyName">The key's Source name, such as <c>HOME</c> or <c>w</c>.</param>
    /// <returns>True when the widget must be allowed to handle it.</returns>
    /// <remarks>
    /// **A blank name keeps nothing**, so an unmapped key from some future toolkit falls through to
    /// the shortcuts rather than silently disabling them.
    /// </remarks>
    public static bool Keeps(FocusedWidget widget, string? keyName)
    {
        if (string.IsNullOrWhiteSpace(keyName))
        {
            return false;
        }

        return widget switch
        {
            FocusedWidget.Text => true,
            FocusedWidget.Slider => SliderKeys.Contains(keyName.Trim()),
            FocusedWidget.List => ListKeys.Contains(keyName.Trim()),
            _ => false,
        };
    }
}
