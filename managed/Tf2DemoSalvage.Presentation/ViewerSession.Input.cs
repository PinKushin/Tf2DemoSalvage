using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using Tf2DemoSalvage.Logging;
using Tf2DemoSalvage.Render;

namespace Tf2DemoSalvage.Presentation;

public sealed partial class ViewerSession
{
    /// <summary>Which key performs which action (D68).</summary>
    /// <remarks>
    /// **Actions are bound, not keys, which is how TF2 works.** Its spectator HUD prints
    /// `[%jump%]` beside "Switch Camera Mode" and substitutes the player's own binding — nothing in
    /// the game hardcodes Space. Defaults follow TF2 where TF2 has an equivalent, and the table is
    /// meant to be loaded from settings so a user can rebind exactly as they would in the game.
    /// </remarks>
    private KeyBindings _bindings = new();

    /// <summary>Where a drag started, in viewport pixels.</summary>
    private (int X, int Y)? _dragFrom;

    /// <summary>Runs the player's own TF2 configs over the shipped defaults.</summary>
    /// <remarks>
    /// **Their controls, not ours, and that is the requirement in one line (D69).** Someone running
    /// mastercomfig has already decided every one of these once; being asked to decide again in a
    /// second, different settings file is the friction this exists to remove.
    ///
    /// **Failure here costs the player's bindings and nothing else.** The defaults are a complete,
    /// working set of controls, so a missing install, an unreadable VPK or a config full of things
    /// this viewer has never heard of all end the same way: the viewer starts. That is why the catch
    /// is broad rather than narrow — but it is logged with its message, because the alternative is a
    /// player whose config silently does nothing and no way to find out why.
    ///
    /// **The two counts are logged together deliberately.** "13 of 78 binds" and "0 of 0" say
    /// different things; "loaded" says neither.
    /// </remarks>
    private void LoadUserConfig()
    {
        // **Assigned only when something was actually read**, which is what the old body did by
        // returning early. `LoadFrom` answers null rather than handing back its own defaults, so an
        // unreadable config cannot quietly replace the bindings this form already has.
        if (_console.LoadFrom(_maps.GameFolder(), _loggers, _configLog, ProgramCustomFolder) is { } loaded)
        {
            _bindings = loaded;
        }
    }

    /// <summary>The user's controls, running as the engine would run them.</summary>
    /// <remarks>
    /// **Held state, because a keystroke is not a duration** (B97). Keys are pushed in on the key
    /// down and key up messages, so the frame loop can ask what is down right now instead of
    /// inferring it from how fast Windows repeats.
    ///
    /// **A console rather than a `HashSet&lt;Keys&gt;` (D69).** A TF2 config is a program: it binds
    /// keys to aliases, and those aliases redefine each other as they run. A set of held keys can
    /// answer "is W down" but not "what does W currently mean", and in a null-cancelling movement
    /// script — which is what most competitive configs are — those are different questions.
    ///
    /// **This is the single source of truth for the controls**; <see cref="_bindings"/> is a
    /// projection of it for the settings screen to display.
    /// </remarks>
    private readonly ConfigConsole _console = ConfigConsole.WithDefaults();

    /// <summary>Forgets every held key, so nothing is left pressed.</summary>
    /// <remarks>
    /// **A key released while the window is not focused never sends its key up here**, so without
    /// this the camera would fly on for ever after an alt-tab — the classic held-key leak. Called on
    /// deactivation and on leaving the free view.
    /// </remarks>
    /// <remarks>
    /// **Rebuilt rather than cleared**, because a console holds more than held keys: it holds the
    /// alias table a config defined, and clearing that would silently unbind the user's controls on
    /// the first alt-tab. Reloading is not an option either — the configs may have come from
    /// anywhere. So the buttons are released one by one, which is what the engine's own
    /// empty-argument `KeyUp` path does.
    /// </remarks>
    public void ReleaseHeldKeys()
    {
        _console.ReleaseEverything();
        _boundKeysDown.Clear();
    }

    /// <summary>Keys whose press a bind took, until they come up: their auto-repeats are the bind's too (B547).</summary>
    private readonly HashSet<string> _boundKeysDown = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Presses a key into the console, remembering it when the bind took it.</summary>
    private bool PressIntoConsole(string name)
    {
        if (!_console.KeyDown(name))
        {
            return false;
        }

        _boundKeysDown.Add(name);
        return true;
    }

    /// <summary>Performs an action a bound key or button asked for.</summary>
    /// <remarks>
    /// **Only the instant actions arrive here.** Flight is held rather than triggered and is read
    /// per frame by <see cref="FlyCamera"/>; the console tells the two apart by
    /// <see cref="ConfigConsole.HeldActions"/> rather than by the command's spelling, because TF2
    /// writes the camera switch as `+jump` even though nothing about it is continuous.
    ///
    /// **Camera mode and reset are still handled in <see cref="KeyPressed"/> by comparing keys.** Every key is pressed
    /// into the console since B547, so these one-shots arrive here too and are ignored; moving their handling here is
    /// the remaining half of B145, and would let an alias reach them.
    /// </remarks>
    private void OnConsoleAction(object? sender, ViewerActionEventArgs e)
    {
        switch (e.Action)
        {
            case ViewerAction.CycleTargetForward:
                _camera.CycleTarget(reverse: false);
                break;

            case ViewerAction.CycleTargetReverse:
                _camera.CycleTarget(reverse: true);
                break;

            default:
                break;
        }
    }

    /// <summary>Decides what a key press does: which bound action it is, and whether the focused control keeps it.</summary>
    /// <param name="key">The press.</param>
    /// <returns>Whether the press was taken; false leaves it to the control that has the keyboard.</returns>
    /// <remarks>
    /// **`ProcessCmdKey` in the window until slice 9 of D214**, where every branch below compared a toolkit key code.
    /// The order is unchanged; what the window still does is translate a key into a <see cref="KeyPress"/>.
    /// </remarks>
    public bool KeyPressed(in KeyPress key)
    {
        // **Nothing is a shortcut while somebody is typing** (B212). `ProcessCmdKey` runs before any
        // control sees a key and returning true consumes it, so every binding below was reaching
        // over the search box.
        //
        // **`Space` is the DEFAULT bind for switch-camera-mode**, so typing `cp process` toggled
        // first person instead of inserting a space — and in the free camera the flight keys took
        // `w`, `a`, `s` and `d` as well. Neither needed an unusual configuration to hit; the shipped
        // defaults are enough.
        //
        // This is a guard on the binds rather than a binding of its own: no key is named here, so it
        // adds nothing to un-hardcode later (D101).
        // **Widened 2026-08-26 from "is it a text box" to "does the focused thing use this key".**
        // The old guard excused text alone, which was enough for the search box and wrong in general:
        // D101 lets a person bind anything, so `bind "UPARROW" "+forward"` in someone's config takes
        // the arrow keys away from the playlist, and binding `HOME` to a speed reset takes it from
        // both sliders. Nobody had to introduce that defect — it was waiting for one line of config.
        //
        // The decision is `WidgetKeys.Keeps`, in `Presentation`, which knows nothing about WinForms;
        // this line and `FocusKind` are the whole of the toolkit-specific part.
        // **`CTRL`/`ALT` is passed separately because the name is the BARE key.** `NameOf` masks the
        // modifiers off, so `Ctrl+R` and `r` arrive here spelled identically — and the playlist keeps
        // `r` for type-ahead. Without this the list would swallow `Ctrl+R` and reset-camera would
        // stop working whenever it had focus. `SHIFT` is not a command modifier: it types a capital.
        bool kept = WidgetKeys.Keeps(key.Focus, key.Name, key.ControlOrAlt);

        // **Which control was thought to have the keyboard, said for every press** (2026-10-13). A slider that
        // held the focus did not move on Home, and "the binding took it" and "the slider got it and ignored
        // it" read the same from outside.
        if (_renderLog.IsEnabled(LogLevel.Debug))
        {
            // Stryker disable once all : a mutated interpolated string cannot bind to its handler (CS1620) and Safe Mode drops the method - B410
            _renderLog.LogDebug(
                "{Message}",
                string.Create(CultureInfo.InvariantCulture, $"key {key.Described}: focus is {key.Focus}, {(kept ? "left to it" : "offered to the bindings")}"));
        }

        if (kept)
        {
            return false;
        }

        // **Page DOWN descends through the map**, taking the roofs off first, and page up brings
        // them back. The obvious reading of the key is the one to follow: the first version had it
        // inverted, and pressing page down 166 times did nothing because the cut was already at
        // zero and the log said so.
        // **Not while a control that uses these keys has focus** (B212). `ProcessCmdKey` runs before
        // any control sees a key and returning true consumes it, so this stole Home, Page Up and
        // Page Down from everything on the form: Home in the search box changed the map's height cut
        // instead of moving the caret, and the scrub bar, the playlist and the speed slider all lost
        // their standard navigation.
        //
        // Found because a UI test pressed Home on the focused speed slider and nothing happened —
        // the same shape as B165's F11 and the F12 double-bind, which is now three times in this
        // file. **A form-level shortcut must not take a key a focused control already means
        // something by.**
        // **The height cut was here, on hardcoded Home, Page Up and Page Down** (B213, D101). It is
        // gone, and both reasons matter.
        //
        // **It never worked.** Its shader comment said "the cut is on depth, which is height" — an
        // equivalence that holds ONLY under the orthographic top-down projection D98 deleted. Under
        // a perspective camera `pos.z` is distance from the eye, so it cut away whatever was nearest
        // rather than whatever was highest. The owner: *"that was a ortho thing that should be
        // ripped out and never worked in the first place"*.
        //
        // **And the keys were hardcoded, which is now forbidden outright** (D101): *"no hard coded
        // controls ever"*, *"everything gets to be customized so runs through the config"*. Three
        // literal `Keys.` comparisons ahead of every control on the form, stealing Home from the
        // search box and Page Up from the playlist — found because a test pressed Home on a focused
        // slider and nothing happened (B212).

        // **Flying the camera.** W and S run along the way it is looking, A and D strafe, and
        // Space and Control lift and drop it along the world's up axis rather than the camera's —
        // which is what every editor does, because rising along a pitched view drifts sideways and
        // feels broken.
        //
        // **The key is only RECORDED here; the movement happens per frame** (B97). Moving on the
        // message meant Windows' auto-repeat set the speed: nothing for the repeat delay, then fixed
        // jumps at the repeat rate, and never two directions at once because auto-repeat reports
        // only the last key held. See FreeFlight.
        // **Every key goes to its bind, as the engine's `Key_Event` sends it** (B547). Only flight keys in the free camera
        // did, so a keyboard `toggle cl_showpos` or a scoreboard alias did nothing while the same bind on MOUSE3 worked
        // (`PointerDown` always pressed). Three refinements, each the engine's or Windows':
        // - an auto-repeat runs nothing, since Source binds the press; but the key stays the bind's while it is held;
        // - CTRL or ALT with a key is not that key, because Source's `bind` has no modifier syntax - the viewer's own
        //   chords live there; the modifier keys themselves are plain keys (`bind CTRL +duck`);
        // - a bind that did something of the console's own takes the key, so a menu shortcut on the same key does not
        //   also fire: the user's config wins a collision (D101). A one-shot of the viewer's own does not, and goes on
        //   to its route below.
        bool chorded = key.ControlOrAlt && key.Name is not ("CTRL" or "ALT");

        // The free camera's flight keys used to be pressed here alone, behind `ConfigConsole.IsHeldKey`; a held press is
        // one of the things that takes the key, so they need nothing of their own.
        if (!chorded && (key.IsRepeat ? _boundKeysDown.Contains(key.Name) : PressIntoConsole(key.Name)))
        {
            return true;
        }

        // **Bound actions rather than hardcoded keys (D68).** TF2 does the same: its spectator HUD
        // prints `[%jump%]` beside "Switch Camera Mode" and substitutes whatever the player bound,
        // so nothing in the game hardcodes Space — it hardcodes the action. These resolve through
        // `_bindings`, which a settings file can override.
        //
        // **Switch camera mode defaults to Space**, which is what TF2 binds it to.
        if (key.IsChord(_bindings.KeyFor(ViewerAction.SwitchCameraMode)))
        {
            return _camera.CycleCameraMode();
        }

        // **Source's own `firstperson` and `thirdperson`, so a config reaches them by name.** They
        // ask for a mode outright where `SwitchCameraMode` cycles, which is the same pair of routes
        // the engine offers: the spectator HUD cycles with `+jump`, and the commands exist for
        // anyone who wants to land on one directly.
        if (key.IsChord(_bindings.KeyFor(ViewerAction.FirstPersonView)))
        {
            return _camera.EnterFirstPerson();
        }

        if (key.IsChord(_bindings.KeyFor(ViewerAction.ThirdPersonView)))
        {
            return _camera.EnterThirdPerson();
        }

        // **The first binding added through B214's mechanism rather than as a literal** (B216). It
        // was agreed weeks ago and deliberately not built — *"do not hard code home, no new hard
        // codes"* — because there was nothing to add it to.
        if (key.IsChord(_bindings.KeyFor(ViewerAction.ResetSpeed)))
        {
            _transport.ResetSpeed();
            return true;
        }

        if (key.IsChord(_bindings.KeyFor(ViewerAction.ResetCamera)))
        {
            return _camera.ResetCamera();
        }

        // **Through the binding table, and its default key MOVED** (B214, D101). This was
        // `Keys.F12` — which TF2 gives to `replay_togglereplaytips` and Steam's overlay claims for
        // its own capture — while our F5 was a debug view. TF2's screenshot key is F5, so the two
        // were swapped against the game. `ViewerAction.Screenshot` speaks Valve's `screenshot`
        // command, so a config that rebinds screenshots moves this with it.
        if (key.IsChord(_bindings.KeyFor(ViewerAction.Screenshot)))
        {
            CaptureViewportToFile();
            return true;
        }

        // **Logged before the guard, so a key that ARRIVED is distinguishable from one that never
        // did.** Full screen has twice been reported as impossible to leave, and the two states
        // look identical from outside: the key reaching this method and being ignored, and the key
        // going to whichever window took the foreground. Only a line written here separates them.
        if (key.IsEscape || key.IsChord(_bindings.KeyFor(ViewerAction.FullScreen)))
        {
            // Stryker disable once all : a mutated interpolated string cannot bind to its handler (CS1620) and Safe Mode drops the method - B410
            _renderLog.LogInformation(
                "{Message}",
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{key.Described} reached the form; full screen is {_window.IsFullScreen}"));
        }

        // **Escape stays a literal, and this is the ONE exception D101 gets** (B214). Every other
        // key in this window now resolves through `KeyBindings`; this one is the way out of full
        // screen, so a config that rebound it away would leave a user with no menu, no title bar and
        // no key that closes either. TF2 binds `ESCAPE` to `escape` for the same structural reason —
        // it is the escape hatch, not a control.
        //
        // Stated rather than left as an oversight, because an unexplained literal in this method is
        // exactly what B212 and B214 were both about.
        if (key.IsEscape && _window.IsFullScreen)
        {
            _window.LeaveFullScreen();
            return true;
        }

        return false;
    }
}
