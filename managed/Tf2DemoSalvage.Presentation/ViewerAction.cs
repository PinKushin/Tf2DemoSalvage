using System;
using System.Collections.Generic;
using System.Linq;

namespace Tf2DemoSalvage.Presentation;

/// <summary>Something the user can ask the viewer to do, independent of which key says so.</summary>
/// <remarks>
/// **Actions are bound, not keys — which is how TF2 itself works.** Its spectator HUD carries
/// strings like
///
/// <code>
/// "TF_Spectator_SwitchCamModeKey"   "[%jump%]"
/// "TF_Spectator_CycleTargetFwdKey"  "[%attack%]"
/// </code>
///
/// — the label names the *action* and the engine substitutes whatever the player has actually
/// bound. Nothing in the game hardcodes "Space"; it hardcodes `+jump` and asks the binding table
/// what that is today.
///
/// This enum is the same idea. The presenter deals in actions and never sees a key; the view owns
/// the mapping and can be rebound without a presenter changing.
/// </remarks>
public enum ViewerAction
{
    /// <summary>Switch which camera the viewport draws through.</summary>
    /// <remarks>TF2 binds this to <c>+jump</c>, and the default here matches.</remarks>
    SwitchCameraMode,

    /// <summary>Put the camera back where it starts — above the map, looking down.</summary>
    ResetCamera,

    /// <summary>Follow the next player.</summary>
    /// <remarks>TF2's <c>+attack</c>.</remarks>
    CycleTargetForward,

    /// <summary>Follow the previous player.</summary>
    /// <remarks>TF2's <c>+attack2</c>.</remarks>
    CycleTargetReverse,

    /// <summary>Start or stop playback.</summary>
    PlayPause,

    /// <summary>Fly the free camera forward.</summary>
    FlyForward,

    /// <summary>Fly the free camera backward.</summary>
    FlyBack,

    /// <summary>Fly the free camera left.</summary>
    FlyLeft,

    /// <summary>Fly the free camera right.</summary>
    FlyRight,

    /// <summary>Fly the free camera up.</summary>
    FlyUp,

    /// <summary>Fly the free camera down.</summary>
    FlyDown,

    /// <summary>Fly SLOWER while held — Source's <c>+speed</c> is the walk key.</summary>
    /// <remarks>
    /// **Named `FlyFast` until 2026-08-26, and it made the camera four times faster** (B215). The
    /// name was the bug's other half: `IN_SPEED` divides the move factor by two in both
    /// `FullObserverMove` and `FullNoClipMove`, so a person pasting their own config (D69) bound the
    /// key they use for careful positioning and got a sprint.
    /// </remarks>
    FlyWalk,
}

/// <summary>
/// Which key performs which action, and the defaults when the user has not said.
/// </summary>
/// <remarks>
/// **Keys are named as strings here, deliberately.** This project cannot reference
/// <c>System.Windows.Forms.Keys</c> — that is the whole point of the boundary — so a binding is a
/// name, and the view turns the name into whatever its toolkit calls that key. It also means the
/// bindings survive in a settings file as text a person can read and edit, which is what TF2's own
/// <c>config.cfg</c> does.
///
/// **The defaults follow TF2 where TF2 has an equivalent**, read from its shipped
/// <c>tf_english.txt</c> rather than from memory: camera mode on jump, target cycling on the two
/// attack buttons. Flight uses WASD because TF2 has no flying camera to copy and WASD is what every
/// editor uses.
/// </remarks>
public sealed class KeyBindings
{
    private readonly Dictionary<ViewerAction, string> _bound;

    /// <summary>Builds a binding table, filling anything unbound from the defaults.</summary>
    /// <param name="bound">Bindings to apply, or null for all defaults.</param>
    /// <exception cref="ArgumentNullException">A supplied binding names a null key.</exception>
    public KeyBindings(IReadOnlyDictionary<ViewerAction, string>? bound = null)
    {
        _bound = new Dictionary<ViewerAction, string>(Defaults);

        if (bound is null)
        {
            return;
        }

        foreach ((ViewerAction action, string key) in bound)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);

            _bound[action] = key;
        }
    }

    /// <summary>What each action is bound to when the user has not said otherwise.</summary>
    /// <remarks>
    /// **`Space` for the camera mode, because that is what TF2 does** — its spectator HUD prints
    /// `[%jump%]` beside "Switch Camera Mode", and jump is Space unless rebound. Anyone who has
    /// spectated in TF2 will press it without being told, which is the whole argument for matching
    /// a convention rather than inventing one.
    /// </remarks>
    public static IReadOnlyDictionary<ViewerAction, string> Defaults { get; } =
        new Dictionary<ViewerAction, string>
        {
            [ViewerAction.SwitchCameraMode] = "SPACE",
            [ViewerAction.ResetCamera] = "f",
            [ViewerAction.CycleTargetForward] = "MOUSE1",
            [ViewerAction.CycleTargetReverse] = "MOUSE2",
            [ViewerAction.PlayPause] = "k",
            [ViewerAction.FlyForward] = "w",
            [ViewerAction.FlyBack] = "s",
            [ViewerAction.FlyLeft] = "a",
            [ViewerAction.FlyRight] = "d",
            // **Vertical is its own pair of keys in Source too, and NOT jump.** `in_main.cpp` builds
            // the roaming camera's vertical from dedicated commands:
            //
            //     cmd->upmove += cl_upspeed.GetFloat() * KeyState (&in_up);
            //     cmd->upmove -= cl_upspeed.GetFloat() * KeyState (&in_down);
            //
            // `in_up`/`in_down` are `+moveup`/`+movedown` — separate from `+jump`, which is what
            // cycles the observer mode. So Valve has no collision here to reproduce.
            //
            // **TF2 does bind them, and this is a deliberate divergence rather than an oversight.**
            // `tf/cfg/config_default.cfg`:
            //
            //     bind "'"   "+moveup"
            //     bind "/"   "+movedown"
            //
            // Apostrophe and forward-slash — Quake-era defaults, nowhere near WASD.
            //
            // **Owner's call, overruling a proposal to use E and Q instead:** *"we keep the same
            // defaults then, but allow them to be changed just like tf2. them being all the way
            // over there is why i never used them im pretty sure though"*.
            //
            // The argument for matching even an awkward default is that a TF2 player's own config
            // translates, and rebinding is the escape hatch — which is exactly how the game handles
            // it. Picking more comfortable keys would make this viewer's controls a third thing to
            // learn, and the discomfort is the game's to own rather than ours to paper over.
            //
            // **It is worth stating what the first attempt got wrong**, because it looked right:
            // putting `FlyUp` on Space alongside `SwitchCameraMode` collided, `ProcessCmdKey`
            // checks flight keys first and swallowed the press, and three UI tests failed by TIMING
            // OUT on a key that did nothing — with Windows dinging on each unhandled press.
            [ViewerAction.FlyUp] = "'",
            [ViewerAction.FlyDown] = "/",
            [ViewerAction.FlyWalk] = "SHIFT",
        };

    /// <summary>The Source command each action answers to.</summary>
    /// <remarks>
    /// **These are TF2's own command names, so a pasted config resolves without translation (D69).**
    /// `bind "w" "+forward"` out of somebody's `autoexec.cfg` or a mastercomfig VPK names an action
    /// this table already knows, and that is the whole requirement — a translation layer between
    /// their vocabulary and ours would mean the paste does not work.
    ///
    /// **`+jump` for the camera mode is not a liberty**, it is what the game does: TF2's spectator
    /// HUD prints `[%jump%]` beside "Switch Camera Mode".
    ///
    /// **Two actions have no Source equivalent** — resetting the camera and play/pause are things
    /// TF2 has no concept of — so they take names in the same style rather than borrowing an
    /// unrelated command. A config that binds them is ours to read; a TF2 config simply will not
    /// mention them, and the defaults stand.
    /// </remarks>
    public static IReadOnlyDictionary<ViewerAction, string> Commands { get; } =
        new Dictionary<ViewerAction, string>
        {
            [ViewerAction.SwitchCameraMode] = "+jump",
            [ViewerAction.CycleTargetForward] = "+attack",
            [ViewerAction.CycleTargetReverse] = "+attack2",
            [ViewerAction.FlyForward] = "+forward",
            [ViewerAction.FlyBack] = "+back",
            [ViewerAction.FlyLeft] = "+moveleft",
            [ViewerAction.FlyRight] = "+moveright",
            [ViewerAction.FlyUp] = "+moveup",
            [ViewerAction.FlyDown] = "+movedown",
            [ViewerAction.FlyWalk] = "+speed",

            // Ours, because TF2 has nothing that means these.
            [ViewerAction.ResetCamera] = "resetcamera",
            [ViewerAction.PlayPause] = "playpause",
        };

    /// <summary>The action a Source command names, or null when nothing here answers to it.</summary>
    /// <param name="command">The command, such as <c>+forward</c>.</param>
    /// <returns>The action, or null.</returns>
    /// <remarks>
    /// **Null rather than an exception, and this is the "ignoring is the feature" rule.** A real
    /// config is hundreds of commands this viewer does not implement; every one of them arrives
    /// here and has to be waved through.
    /// </remarks>
    public static ViewerAction? ActionOf(string? command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return null;
        }

        foreach ((ViewerAction action, string named) in Commands)
        {
            if (string.Equals(named, command.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return action;
            }
        }

        return null;
    }

    /// <summary>Applies every bind in a Source config, leaving unknown commands alone.</summary>
    /// <param name="text">The config's text.</param>
    /// <returns>How many binds named an action this viewer implements.</returns>
    /// <remarks>
    /// **Returns a count so a caller can say something true.** "Loaded your config" is a claim, and
    /// a config whose every line was ignored deserves to be reported differently from one that
    /// rebound eight controls — otherwise a misspelt path and a working load look identical.
    /// </remarks>
    public int ApplySourceConfig(string? text) => ApplySourceConfigs([text ?? string.Empty]);

    /// <summary>Applies several configs together, as the engine executes them in turn.</summary>
    /// <param name="texts">Config contents, in the order they would be executed.</param>
    /// <returns>How many binds resolved to an action this viewer implements.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="texts"/> is null.</exception>
    /// <remarks>
    /// **Several, because a bind and the alias that gives it meaning live in different files.** The
    /// owner's `config.cfg` binds `w` to `+mfwd` and his `autoexec.cfg` defines what `+mfwd` does —
    /// so reading either alone finds nothing. Aliases are gathered from every file first, then the
    /// binds applied against the whole set.
    /// </remarks>
    public int ApplySourceConfigs(IEnumerable<string> texts)
    {
        ArgumentNullException.ThrowIfNull(texts);

        List<string> all = [.. texts];
        Dictionary<string, string> aliases = new(StringComparer.OrdinalIgnoreCase);

        foreach (string text in all)
        {
            foreach ((string name, string body) in SourceConfig.ReadAliases(text))
            {
                aliases[name] = body;
            }
        }

        int applied = 0;

        foreach (string text in all)
        {
            foreach ((string key, string command) in SourceConfig.ReadBinds(text))
            {
                if (Resolve(command, aliases, depth: 0) is not { } action)
                {
                    continue;
                }

                // An `unbind` arrives as an empty command and cannot name an action, so it is
                // already skipped — a key the user unbound keeps this viewer's default rather than
                // becoming unreachable, since a TF2 config unbinding a movement key says nothing
                // about what a demo viewer should do with it.
                Bind(action, key);
                applied++;
            }
        }

        return applied;
    }

    /// <summary>The action a command names, following aliases.</summary>
    /// <remarks>
    /// **Depth-limited because aliases can define each other, including circularly.** A null-cancel
    /// script routinely redefines an alias from inside another one, and a config that loops would
    /// otherwise hang the viewer at startup — the worst place to discover it.
    ///
    /// **The FIRST recognised command in a body wins.** `+mfwd` expands to
    /// `-back; +forward; alias checkfwd +forward`; `-back` is a release command this viewer has no
    /// action for, `+forward` is the one that matters, and the trailing `alias` clause is a nested
    /// definition rather than an invocation.
    /// </remarks>
    private static ViewerAction? Resolve(
        string command, IReadOnlyDictionary<string, string> aliases, int depth)
    {
        if (ActionOf(command) is { } direct)
        {
            return direct;
        }

        if (depth >= 8 || !aliases.TryGetValue(command.Trim(), out string? body))
        {
            return null;
        }

        foreach (string inner in SourceConfig.Body(body))
        {
            if (Resolve(inner, aliases, depth + 1) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>The key bound to an action.</summary>
    /// <param name="action">The action.</param>
    /// <returns>The key's name.</returns>
    public string KeyFor(ViewerAction action) =>
        _bound.TryGetValue(action, out string? key) ? key : string.Empty;

    /// <summary>Every action a key performs.</summary>
    /// <param name="key">The key's name, compared without case.</param>
    /// <returns>The actions, empty when the key is bound to nothing.</returns>
    /// <remarks>
    /// **Several actions may share a key, and that is not a mistake to reject.** Which of them
    /// applies depends on what the viewer is doing, and that is the caller's decision rather than
    /// the table's. Returning every match rather than the first means the caller can make it; a
    /// table that silently picked one would make the collision look like a lost binding.
    ///
    /// **The example this used to give was out of date, and it misled a test into existence**
    /// (2026-08-26). It said `Space` is both "switch camera mode" and "fly up" by default, *"exactly
    /// as TF2's jump is both jump and the spectator's mode switch"* — a good analogy for a binding
    /// that no longer exists. `FlyUp` moved to `'` when these were made to follow TF2's `+moveup`
    /// (D69), and **no key in <see cref="Defaults"/> now carries two actions at all.**
    ///
    /// The capability is still real and still needed, because a user's own config may bind whatever
    /// it likes to one key — which is the whole premise of D69. It is simply not exercised by the
    /// shipped defaults, so a test wanting a shared key has to build one.
    /// </remarks>
    public IReadOnlyList<ViewerAction> ActionsFor(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        return [.. _bound
            .Where(pair => string.Equals(pair.Value, key, StringComparison.OrdinalIgnoreCase))
            .Select(pair => pair.Key)
            .OrderBy(action => action)];
    }

    /// <summary>Binds an action to a key, replacing whatever it had.</summary>
    /// <param name="action">The action.</param>
    /// <param name="key">The key's name.</param>
    /// <exception cref="ArgumentException">The key is empty.</exception>
    public void Bind(ViewerAction action, string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        _bound[action] = key;
    }

    /// <summary>Every binding, for writing to a settings file.</summary>
    /// <remarks>
    /// Ordered by action so a written file has a stable shape — a settings file that reorders
    /// itself between runs is one nobody can diff.
    /// </remarks>
    public IReadOnlyList<(ViewerAction Action, string Key)> All() =>
        [.. _bound.OrderBy(pair => pair.Key).Select(pair => (pair.Key, pair.Value))];
}
