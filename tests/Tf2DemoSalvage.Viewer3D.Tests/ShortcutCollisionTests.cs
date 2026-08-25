using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

using Tf2DemoSalvage.Viewer3D;

namespace Tf2DemoSalvage.Viewer3D.Tests;

/// <summary>
/// No keyboard shortcut may be claimed twice, because the loser fails silently.
/// </summary>
/// <remarks>
/// **Three instances of this defect have shipped in one file.** B165 was F11, which silently broke
/// full screen for days. Then F12, which meant pressing it produced no screenshot, no log line and
/// no error — the owner diagnosed it from the shape alone: *"if f12 is double bound it wont
/// work"*. Auditing the rest found a third: F8 claimed by both the frame-rate overlay and the
/// reflections toggle.
///
/// **What makes it worth a test rather than care is that the failure is invisible from every side.**
/// A double-bound key does not throw, does not warn, and leaves the menu item looking correct — it
/// simply does nothing when pressed. Nobody notices until they try the feature, and then they
/// suspect the feature.
///
/// Windows Forms, so this fixture is <c>[Apartment(ApartmentState.STA)]</c> and
/// <c>[NonParallelizable]</c> per B178: constructing a form off the STA is what aborted the whole
/// suite three times in five runs.
/// </remarks>
[Apartment(ApartmentState.STA)]
[NonParallelizable]
public sealed class ShortcutCollisionTests
{
    [Test]
    public void Shortcuts_EveryMenuItem_ClaimsAKeyNothingElseHas()
    {
        using MainForm form = new();

        List<(string Item, Keys Key)> claimed = [];

        foreach (ToolStripMenuItem item in Descendants(form.MainMenuStrip))
        {
            if (item.ShortcutKeys != Keys.None)
            {
                claimed.Add((item.Name ?? item.Text ?? "<unnamed>", item.ShortcutKeys));
            }
        }

        // **The control, and it matters more than usual here.** Every assertion below is about
        // ABSENCE — a walk that found no menu items at all would report perfect agreement. The
        // viewer has well over a dozen shortcuts.
        claimed.Count.ShouldBeGreaterThan(5, "the menu walk found almost nothing, so it is not walking the menu");

        List<string> collisions = claimed
            .GroupBy(entry => entry.Key)
            .Where(group => group.Count() > 1)
            .Select(group => $"{group.Key} claimed by {string.Join(" and ", group.Select(entry => entry.Item))}")
            .ToList();

        collisions.ShouldBeEmpty(
            "a key claimed twice does nothing when pressed, and says nothing about why");
    }

    /// <summary>Every menu item under a strip, however deeply nested.</summary>
    /// <remarks>
    /// Recursive because the collisions live at different depths: F12 was on a top-level View item
    /// and the fullbright keys are two levels down in a submenu. A one-level walk would have found
    /// the F8 pair and missed the ones that are fine, which is the wrong half to be sure about.
    /// </remarks>
    private static IEnumerable<ToolStripMenuItem> Descendants(ToolStrip? strip)
    {
        if (strip is null)
        {
            yield break;
        }

        foreach (ToolStripItem item in strip.Items)
        {
            foreach (ToolStripMenuItem found in Descendants(item))
            {
                yield return found;
            }
        }
    }

    private static IEnumerable<ToolStripMenuItem> Descendants(ToolStripItem item)
    {
        if (item is not ToolStripMenuItem menu)
        {
            yield break;
        }

        yield return menu;

        foreach (ToolStripItem child in menu.DropDownItems)
        {
            foreach (ToolStripMenuItem found in Descendants(child))
            {
                yield return found;
            }
        }
    }
}
