using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

using Tf2DemoSalvage.Presentation;

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
        // **An exact count, not a floor.** The View menu is where the toggles live, and this is the
        // one assertion that fails if the move dropped one and nothing else noticed. Twelve was
        // measured from the code being moved, not chosen.
        //
        // **Thirteen since 2026-08-27**, when `mat_phong` was added beside `mat_specular` — a real
        // Valve convar this viewer had no equivalent for, which is why B170 could not be tested by
        // the one manipulation that would settle it. The count RISING for a named reason is the
        // outcome this test is for; a fall, or a rise nobody can name, is the defect it guards.
        //
        // **Fourteen since 2026-08-29**: `cl_showpos`, beside `cl_showfps` because Valve's own
        // `CFPSPanel` draws both (D123). Added as an instrument for reading positions off a
        // screenshot, on the owner's direction.
        using MainForm form = new();

        ToolStripMenuItem view = form.MainMenuStrip!.Items.OfType<ToolStripMenuItem>()
            .Single(item => item.Name == MainForm.ViewMenuId);

        view.DropDownItems.Count.ShouldBe(14);
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

    [Test]
    public void Strip_EveryShortcutLabelItPrints_NamesAKeyThatIsActuallyBound()
    {
        // **A menu that names the wrong key is worse than a menu that names none** (B239). The
        // owner: *"f5 is the shortcut for ss's and the menu item saying f12 is actually wrong"* —
        // B214 moved the screenshot to F5 for Valve parity, because F5 is TF2's own screenshot key,
        // and the label was left behind as the literal string "F12".
        //
        // **`ShortcutKeyDisplayString` is a LABEL, not a registration**, which is exactly why it can
        // drift: nothing stops working when it is wrong. It exists here because the screenshot key
        // is bound once in `ProcessCmdKey` and binding it twice makes it do nothing at all — B165
        // on F11, then again on F12 — so the item displays what the form owns.
        //
        // This is written as a tripwire over EVERY item rather than as an assertion about one, so
        // the next label added by hand fails here instead of on someone's screen.
        using MainForm form = new();

        HashSet<string> bound = new(
            new KeyBindings().All().Select(entry => entry.Key),
            StringComparer.OrdinalIgnoreCase);

        List<string> printed =
        [
            .. Descendants(form.MainMenuStrip)
                .Select(item => item.ShortcutKeyDisplayString ?? string.Empty)
                .Where(label => label.Length > 0)
                .Where(label => !bound.Contains(label)),
        ];

        printed.ShouldBeEmpty(
            "a printed shortcut must be a key something is actually bound to; ask "
            + "KeyBindings.KeyFor rather than typing one");
    }

    [Test]
    public void ScreenshotItem_ItsShortcutLabel_IsTheKeyBoundToTheScreenshot()
    {
        // **The control on the tripwire above**, and it is not the same test. A label of "F9" would
        // satisfy that one — F9 is bound, to the surface colours — while still telling the owner to
        // press the wrong key. This names the pairing.
        using MainForm form = new();

        ToolStripMenuItem screenshot = Descendants(form.MainMenuStrip)
            .Single(item => string.Equals(item.Name, MainForm.ScreenshotItemId, StringComparison.Ordinal));

        screenshot.ShortcutKeyDisplayString.ShouldBe(
            new KeyBindings().KeyFor(ViewerAction.Screenshot),
            "the label and the binding are one fact and must have one source");
    }

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
