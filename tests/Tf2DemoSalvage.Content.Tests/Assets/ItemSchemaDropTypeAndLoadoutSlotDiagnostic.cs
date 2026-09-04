using System.Collections.Generic;
using System.IO;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// How many real item definitions declare a <c>drop_type</c> of "drop" or "break", and how many
/// occupy the HEAD or MISC loadout slot — measured on the shipped file, not predicted.
/// </summary>
/// <remarks>
/// **A measurement, not a test (D38).** "How many items in a real schema declare X" is a fact
/// about Valve's data, not about this parser, and it changes every time the game updates. Explicit:
/// it reports rather than asserts, the same shape as <see cref="ViewmodelArmsBoneDiagnostic"/>.
/// </remarks>
[Explicit("Diagnostic: counts drop_type and default loadout slot declarations in the shipped schema.")]
public sealed class ItemSchemaDropTypeAndLoadoutSlotDiagnostic
{
    private static string? SchemaPath => GameInstall.Find("scripts/items/items_game.txt");

    [Test]
    public void ReportDropTypeAndLoadoutSlotCounts()
    {
        if (SchemaPath is not { } path)
        {
            Assert.Ignore("the game is not installed");
            return;
        }

        ItemSchema schema = ItemSchema.Read(File.ReadAllBytes(path));

        List<int> definitions = [.. schema.DefinitionIndices];

        // The control: an empty denominator would make every percentage below meaningless without
        // saying so (docs/memory/an-empty-search-needs-a-control.md).
        definitions.ShouldNotBeEmpty("the shipped schema should declare at least one item");

        int none = 0, drop = 0, brk = 0, unrecognizedOrOutOfTable = 0;
        int head = 0, misc = 0, invalid = 0;

        foreach (int index in definitions)
        {
            int dropType = schema.DropType(index);

            if (dropType == ItemSchema.DropTypeDrop)
            {
                drop++;
            }
            else if (dropType == ItemSchema.DropTypeBreak)
            {
                brk++;
            }
            else if (dropType == ItemSchema.DropTypeNone)
            {
                none++;
            }
            else
            {
                unrecognizedOrOutOfTable++;
            }

            int slot = schema.DefaultLoadoutSlot(index);

            if (slot == ItemSchema.LoadoutSlotHead)
            {
                head++;
            }
            else if (slot == ItemSchema.LoadoutSlotMisc)
            {
                misc++;
            }
            else if (slot == ItemSchema.LoadoutSlotInvalid)
            {
                invalid++;
            }
        }

        TestContext.Out.WriteLine($"item definitions: {definitions.Count}");
        TestContext.Out.WriteLine(
            $"drop_type: none/unset {none}, drop {drop}, break {brk}, " +
            $"unrecognized {unrecognizedOrOutOfTable}");
        TestContext.Out.WriteLine(
            $"default loadout slot: HEAD {head}, MISC {misc}, INVALID/unset {invalid}, " +
            $"other named slot {definitions.Count - head - misc - invalid}");
    }
}
