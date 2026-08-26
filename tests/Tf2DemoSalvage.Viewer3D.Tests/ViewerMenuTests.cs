using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

namespace Tf2DemoSalvage.Viewer3D.Tests;

/// <summary>The menu still contains everything it is supposed to contain.</summary>
/// <remarks>
/// **Written for the move that created <see cref="ViewerMenu"/>** (B188, D90): 363 lines left
/// `MainForm`'s constructor in one go, and nothing in the suite could have noticed an item that
/// failed to arrive.
///
/// **Every existing menu test walks whatever items exist**, which is the shape that cannot see a
/// deletion. `ShortcutCollisionTests` checks that no two items claim one key — one fewer item is one
/// fewer to check, and it still passes. `DebugMenuWiringTests` addresses six items by name and says
/// nothing about a seventh going missing. The UI suite presses F11 and invokes the screenshot item,
/// so it covers two of twenty.
///
/// **So the denominator is generated rather than written.** Every <c>*ItemId</c> and <c>*MenuId</c>
/// constant on <see cref="MainForm"/> names something that must be reachable in the strip, and the
/// list is read by reflection — so a constant added without an item fails here, and an item deleted
/// fails here, without anyone remembering to update a count. That is the arrangement
/// <c>SdkCoverageTests</c> uses for the same reason: a hand-written denominator goes stale, and a
/// stale denominator reads as full coverage.
/// </remarks>
[Apartment(ApartmentState.STA)]
[NonParallelizable]
public sealed class ViewerMenuTests
{
    [Test]
    public void Strip_AfterTheMove_ContainsAnItemForEveryPublishedIdentifier()
    {
        using MainForm form = new();

        HashSet<string> present = Descendants(form.MainMenuStrip)
            .Select(item => item.Name ?? string.Empty)
            .ToHashSet(StringComparer.Ordinal);

        List<string> missing = PublishedIdentifiers()
            .Where(id => !present.Contains(id))
            .ToList();

        missing.ShouldBeEmpty(
            "every published menu identifier must name an item that is actually in the strip");
    }

    [Test]
    public void Strip_AfterTheMove_StillHasBothTopLevelMenusInOrder()
    {
        // File then View, which is the order every Windows application uses and the order the
        // accessibility tree reports. A move that rebuilt the strip could reorder them silently.
        using MainForm form = new();

        form.MainMenuStrip!.Items.OfType<ToolStripMenuItem>()
            .Select(item => item.Name)
            .ShouldBe([MainForm.FileMenuId, MainForm.ViewMenuId]);
    }

    [Test]
    public void ViewMenu_AfterTheMove_HasEveryItemItHadBefore()
    {
        // **An exact count, not a floor.** The View menu is where all twelve toggles live, and this
        // is the one assertion that fails if the move dropped one and nothing else noticed. Twelve
        // was measured from the code being moved, not chosen.
        using MainForm form = new();

        ToolStripMenuItem view = form.MainMenuStrip!.Items.OfType<ToolStripMenuItem>()
            .Single(item => item.Name == MainForm.ViewMenuId);

        view.DropDownItems.Count.ShouldBe(12);
    }

    [Test]
    public void DebugMenu_AfterTheMove_HasOneItemPerDebugMode()
    {
        // **The denominator is `DebugModes`' own field count**, so a seventh mode added to the
        // record without a menu item fails here. B210 was the other half of this: a mode that HAD an
        // item and could still not be reached, because the handler's switch had fewer arms than the
        // list had entries.
        using MainForm form = new();

        ToolStripMenuItem debug = Descendants(form.MainMenuStrip)
            .Single(item => item.Name == MainForm.DebugMenuItemId);

        int modes = typeof(DebugModes)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Count(property => property.PropertyType == typeof(bool) && property.Name != nameof(DebugModes.Any));

        debug.DropDownItems.Count.ShouldBe(modes);
    }

    /// <summary>Every menu identifier the form publishes for tests and automation.</summary>
    private static IEnumerable<string> PublishedIdentifiers() =>
        typeof(MainForm)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field =>
                field is { IsLiteral: true, IsInitOnly: false }
                && field.FieldType == typeof(string)
                && (field.Name.EndsWith("ItemId", StringComparison.Ordinal)
                    || field.Name.EndsWith("MenuId", StringComparison.Ordinal)))
            .Select(field => (string)field.GetRawConstantValue()!);

    private static IEnumerable<ToolStripMenuItem> Descendants(MenuStrip? strip)
    {
        if (strip is null)
        {
            yield break;
        }

        Stack<ToolStripItem> pending = new(strip.Items.OfType<ToolStripItem>());

        while (pending.Count > 0)
        {
            if (pending.Pop() is not ToolStripMenuItem item)
            {
                continue;
            }

            yield return item;

            foreach (ToolStripItem child in item.DropDownItems)
            {
                pending.Push(child);
            }
        }
    }
}
