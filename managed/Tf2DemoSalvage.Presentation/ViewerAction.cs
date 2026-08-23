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

    /// <summary>Fly faster while held.</summary>
    FlyFast,
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
            [ViewerAction.SwitchCameraMode] = "Space",
            [ViewerAction.ResetCamera] = "F",
            [ViewerAction.CycleTargetForward] = "MouseLeft",
            [ViewerAction.CycleTargetReverse] = "MouseRight",
            [ViewerAction.PlayPause] = "K",
            [ViewerAction.FlyForward] = "W",
            [ViewerAction.FlyBack] = "S",
            [ViewerAction.FlyLeft] = "A",
            [ViewerAction.FlyRight] = "D",
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
            [ViewerAction.FlyFast] = "Shift",
        };

    /// <summary>The key bound to an action.</summary>
    /// <param name="action">The action.</param>
    /// <returns>The key's name.</returns>
    public string KeyFor(ViewerAction action) =>
        _bound.TryGetValue(action, out string? key) ? key : string.Empty;

    /// <summary>Every action a key performs.</summary>
    /// <param name="key">The key's name, compared without case.</param>
    /// <returns>The actions, empty when the key is bound to nothing.</returns>
    /// <remarks>
    /// **Several actions may share a key, and that is not a mistake to reject.** `Space` is both
    /// "switch camera mode" and "fly up" by default, exactly as TF2's jump is both jump and the
    /// spectator's mode switch — which of them applies depends on what the viewer is doing, and
    /// that is the caller's decision rather than the table's.
    ///
    /// Returning every match rather than the first means the caller can make that decision. A table
    /// that silently picked one would make the collision look like a lost binding.
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
