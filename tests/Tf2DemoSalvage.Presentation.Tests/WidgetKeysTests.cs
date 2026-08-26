namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>Which keys a focused widget keeps for itself, so a shortcut cannot take them.</summary>
/// <remarks>
/// **B212 generalised.** A viewer-wide shortcut is dispatched before any widget sees the key, so a
/// binding on a key the focused widget needs silently removes it. The first guard asked *what type
/// has focus* and excused text alone; this asks **does the focused thing use this key**, which is the
/// question that was always meant.
///
/// **These tests name no toolkit**, deliberately — the type under test is in `Presentation`, which
/// targets `net10.0` and cannot reference one. That is what makes the rules portable when the front
/// end changes; only the ten-line `MainForm.FocusKind` adapter would be rewritten.
/// </remarks>
public sealed class WidgetKeysTests
{
    [Test]
    public void Keeps_ASliderAndHome_IsTrue()
    {
        // The case that prompted the widening. `HOME` on a slider means "minimum" in every toolkit
        // there is, and a speed-reset bound to it would have reached over both transport sliders.
        WidgetKeys.Keeps(FocusedWidget.Slider, "HOME").ShouldBeTrue();
        WidgetKeys.Keeps(FocusedWidget.Slider, "END").ShouldBeTrue();
        WidgetKeys.Keeps(FocusedWidget.Slider, "LEFTARROW").ShouldBeTrue();
    }

    [Test]
    public void Keeps_ASliderAndAFlightKey_IsFalse()
    {
        // **The control, and without it this whole guard could be "keep everything".** A slider does
        // not use `w`, so flying must still work while one has focus — otherwise the fix for a
        // stolen Home would be a camera that stops whenever the user clicks the speed slider.
        WidgetKeys.Keeps(FocusedWidget.Slider, "w").ShouldBeFalse();
        WidgetKeys.Keeps(FocusedWidget.Slider, "SPACE").ShouldBeFalse();
        WidgetKeys.Keeps(FocusedWidget.Slider, "F5").ShouldBeFalse();
    }

    [Test]
    public void Keeps_Text_IsEveryKeyIncludingNavigation()
    {
        // Any printable character is content while somebody is typing, and so is every navigation
        // key — there is no subset to carve out, which is why this arm is not a set.
        foreach (string key in new[] { "w", "SPACE", "HOME", "F5", "UPARROW", "'", "1" })
        {
            WidgetKeys.Keeps(FocusedWidget.Text, key).ShouldBeTrue($"typing needs {key}");
        }
    }

    [Test]
    public void Keeps_AList_TakesTheNavigationKeysAndNothingElse()
    {
        WidgetKeys.Keeps(FocusedWidget.List, "UPARROW").ShouldBeTrue();
        WidgetKeys.Keeps(FocusedWidget.List, "PGDN").ShouldBeTrue();
        WidgetKeys.Keeps(FocusedWidget.List, "HOME").ShouldBeTrue();

        // **Type-ahead is deliberately absent.** A real list selects by typed letters, so a strict
        // reading would keep every letter — and that would stop w/a/s/d flying whenever the playlist
        // had focus. Recorded as a decision rather than left ambiguous; see `WidgetKeys`.
        WidgetKeys.Keeps(FocusedWidget.List, "w").ShouldBeFalse();
    }

    [Test]
    public void Keeps_AButtonOrNothingFocused_KeepsNothing()
    {
        // A button uses Space and Enter to activate, but those reach it through the toolkit's own
        // dispatch rather than through this guard, and claiming HOME for one would be inventing a
        // convention no toolkit has.
        WidgetKeys.Keeps(FocusedWidget.Button, "HOME").ShouldBeFalse();
        WidgetKeys.Keeps(FocusedWidget.None, "HOME").ShouldBeFalse();
        WidgetKeys.Keeps(FocusedWidget.Other, "HOME").ShouldBeFalse();
    }

    [Test]
    public void Keeps_AnUnnamedKey_KeepsNothingRatherThanEverything()
    {
        // An unmapped key from some future toolkit falls through to the shortcuts. The alternative —
        // treating "I do not know" as "the widget keeps it" — would disable shortcuts silently, and
        // a shortcut that does nothing is far harder to notice than one that fires.
        WidgetKeys.Keeps(FocusedWidget.Slider, null).ShouldBeFalse();
        WidgetKeys.Keeps(FocusedWidget.Slider, "  ").ShouldBeFalse();
    }

    [Test]
    public void Keeps_AKeyNameInAnyCase_IsTheSameAnswer()
    {
        // Names come from config files people type by hand, and Source's own are inconsistently
        // cased — `bind "HOME"` and `bind "home"` are one binding in the game.
        //
        // **Asserted as a VALUE, not as an equality between two calls.** Written first as
        // `Keeps("home").ShouldBe(Keeps("HOME"))`, which is true whenever the two agree — including
        // when both are false. Removing HOME from the set to check this file's sensitivity reddened
        // the other test and left this one green, which is the "wrong condition" case in
        // `CLAUDE.md`: an input for which correct and broken predict the same observation. Fixing
        // the assertion rather than the input would not have helped; the pair was the problem.
        WidgetKeys.Keeps(FocusedWidget.Slider, "home").ShouldBeTrue();
        WidgetKeys.Keeps(FocusedWidget.Slider, "HoMe").ShouldBeTrue();
    }
}
