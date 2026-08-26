using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;

using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Viewer3D.Tests;

/// <summary>
/// Whether this viewer's default keys survive a real TF2 config being pasted over them.
/// </summary>
/// <remarks>
/// **The denominator is generated from the game rather than written here** (B214), so it cannot go
/// stale: TF2 ships its own defaults in plain text at <c>tf/cfg/config_default.cfg</c>, 64 `bind`
/// lines covering every letter except <c>o</c>.
///
/// ## The rule, and why it is about D69 rather than about taste
///
/// D69's promise is that a person's own config works **wholesale**. That cuts both ways, and the
/// second way had not been thought about: loading their config does not merely add bindings, it
/// **takes keys away from us**. `bind "f" "+inspect"` moves `f` to a command this viewer does not
/// implement, so whatever we had on `f` is simply gone — reported by `ConfigConsole.Unbound()` and
/// then silently absent.
///
/// So a default key is safe in exactly two cases:
///
/// 1. **TF2 binds that key to the same command we do.** Then a pasted config moves our action
///    wherever the player moved theirs, which is the behaviour we want — `SPACE` is `+jump` in both,
///    so a player who rebound jump rebinds our camera switch with it.
/// 2. **TF2 does not bind that key at all.** Then nothing in a pasted config can claim it.
///
/// Anything else loses the action to the paste. There are only six keys in the second category —
/// <c>o</c>, <c>F3</c>, <c>F4</c>, <c>F8</c>, <c>F9</c>, <c>F11</c> — which is why the viewer's own
/// actions sit on <c>CTRL</c> combinations: **TF2 binds no modifier combination anywhere**, so that
/// whole space is unreachable from a real config and therefore safe.
///
/// **Evidence class: read from shipped data.** The parse below is of Valve's file, not of ours.
/// </remarks>
public sealed class DefaultBindingConformanceTests
{
    /// <summary>TF2's shipped bindings, keyed by the key's upper-case name.</summary>
    private static Dictionary<string, string> Shipped()
    {
        // **`GameInstall.Root` is already the `tf` folder**, so this is relative to that. Written
        // `tf/cfg/config_default.cfg` first, which resolved to `tf/tf/cfg/...` and made both tests
        // SKIP — reported as a pass by the summary line and by the count floor, which is the shape
        // `docs/memory/a-skip-is-not-a-pass-or-a-failure.md` exists for.
        string? path = GameInstall.Find(Path.Combine("cfg", "config_default.cfg"));

        if (path is null)
        {
            Assert.Ignore(GameInstall.Missing);
        }

        Dictionary<string, string> binds = [];

        foreach (string line in File.ReadLines(path))
        {
            string trimmed = line.Trim();

            if (!trimmed.StartsWith("bind", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // bind "KEY"<tabs>"COMMAND" — quoted on both sides, whitespace between them varies.
            string[] quoted = trimmed.Split('"', StringSplitOptions.RemoveEmptyEntries)
                .Where(part => !string.IsNullOrWhiteSpace(part.Trim()))
                .Select(part => part.Trim())
                .ToArray();

            if (quoted.Length >= 3)
            {
                binds[quoted[1].ToUpperInvariant()] = quoted[2];
            }
        }

        binds.Count.ShouldBeGreaterThan(50, "the shipped config should carry TF2's whole default set");

        return binds;
    }

    [Test]
    public void Defaults_AgainstTf2sOwnConfig_TakeOnlyKeysThatSurviveAPaste()
    {
        Dictionary<string, string> shipped = Shipped();
        KeyBindings bindings = new();
        List<string> stolen = [];

        foreach ((ViewerAction action, string key) in bindings.All())
        {
            // A modifier combination is unreachable from a Source config, which has no syntax for
            // one. Safe by construction, and the reason the viewer's own actions live there.
            if (key.Contains('+', StringComparison.Ordinal))
            {
                continue;
            }

            if (!shipped.TryGetValue(key.ToUpperInvariant(), out string? theirs))
            {
                // Case 2: TF2 leaves the key alone.
                continue;
            }

            KeyBindings.Commands.TryGetValue(action, out string? ours);

            if (!string.Equals(theirs, ours, StringComparison.OrdinalIgnoreCase))
            {
                // Case 3, the one that loses the action: they bind this key to something else.
                stolen.Add($"{action} on \"{key}\" — TF2 binds it to \"{theirs}\", we mean \"{ours}\"");
            }
        }

        stolen.ShouldBeEmpty(
            "a pasted TF2 config would rebind these keys and silently remove the action:\n  " +
            string.Join("\n  ", stolen));
    }

    [Test]
    public void Defaults_WhereTheyShareTf2sCommand_AreOnTf2sOwnKey()
    {
        // **The other direction, and it is not the same assertion.** The test above lets an action
        // sit on any key TF2 leaves free; this one says that where we DO speak TF2's command, we
        // should start on TF2's key — otherwise a player who never touches a config finds jump,
        // attack and forward somewhere unfamiliar for no reason.
        Dictionary<string, string> shipped = Shipped();
        KeyBindings bindings = new();
        List<string> moved = [];

        foreach ((ViewerAction action, string key) in bindings.All())
        {
            if (!KeyBindings.Commands.TryGetValue(action, out string? ours))
            {
                continue;
            }

            // Which key does TF2 give this command? Not every command of ours is one of theirs.
            string? theirKey = shipped
                .FirstOrDefault(bind =>
                    string.Equals(bind.Value, ours, StringComparison.OrdinalIgnoreCase))
                .Key;

            if (theirKey is null)
            {
                continue;
            }

            if (!string.Equals(theirKey, key, StringComparison.OrdinalIgnoreCase))
            {
                moved.Add($"{action} (\"{ours}\") is on \"{key}\" here and \"{theirKey}\" in TF2");
            }
        }

        moved.ShouldBeEmpty(
            "these speak TF2's command but start on a different key:\n  " + string.Join("\n  ", moved));
    }

    [Test]
    public void Defaults_EveryOne_IsResolvableToAKey()
    {
        // **The check that catches a typo in the table**, which is otherwise invisible: an
        // unresolvable name becomes `Keys.None` and the control simply never fires. Mouse buttons
        // resolve to None deliberately — they are not `Keys` — so they are excluded rather than
        // being made to look like failures.
        KeyBindings bindings = new();

        foreach ((ViewerAction action, string key) in bindings.All())
        {
            if (key.StartsWith("MOUSE", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            KeyNames.Resolve(key).ShouldNotBe(
                Keys.None, $"{action} is bound to \"{key}\", which resolves to nothing");
        }
    }
}
