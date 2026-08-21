using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// The whole chain, on a real demo: held weapon to item index to model path.
/// </summary>
/// <remarks>
/// **Three decoded facts have to line up and only a real demo can check that they do.** The player
/// says which entity is their weapon, the weapon entity says which item it is, and the item schema
/// says which model that item wears. Each piece has its own tests against fixtures its author
/// wrote; this is the one that can disagree with all of them at once.
///
/// It is also the assertion the project's own rule asks for — a component test proves a component
/// answers when called with the values the test chose, and says nothing about whether production
/// calls it or with what. Three no-ops shipped here behind exactly that gap.
/// </remarks>
public sealed class HeldWeaponModelTests
{
    /// <summary>Where the game is, on this machine.</summary>
    private const string SchemaPath =
        "F:/SteamLibrary/steamapps/common/Team Fortress 2/tf/scripts/items/items_game.txt";

    [Test]
    public void WeaponItem_ForEveryPlayerHoldingSomething_ResolvesToAModel()
    {
        string? path = Corpus.FilesWithSchema()
            .FirstOrDefault(file => Path.GetFileName(file).Contains("z1800", StringComparison.Ordinal));

        if (path is null || !File.Exists(SchemaPath))
        {
            Assert.Ignore("the modern demo or the game is not present");
            return;
        }

        ItemSchema schema = ItemSchema.Read(File.ReadAllBytes(SchemaPath));
        DemoTimeline timeline = TimelineCache.For(path);

        int holding = 0;
        int identified = 0;
        int resolved = 0;
        List<string> sample = [];
        List<string> missed = [];

        foreach (int tick in (int[])[2883, 20000, 40000])
        {
            foreach (ScenePlayer player in timeline.PlayersAt(tick))
            {
                if (player.ActiveWeapon is null)
                {
                    continue;
                }

                holding++;

                // The item index when the demo sends one, and the stock item for the weapon's class
                // when it does not — the fallback described on ItemSchema.ModelForClass.
                string? model = player.WeaponItem is { } item
                    ? schema.ModelFor(item, player.PlayerClass ?? 0)
                    : null;

                if (model is { Length: > 0 })
                {
                    identified++;
                }
                else if (player.WeaponClass is { } weaponClass)
                {
                    foreach (string candidate in
                        WeaponScriptName.Candidates(weaponClass, player.PlayerClass))
                    {
                        model = schema.ModelForClass(candidate, player.PlayerClass ?? 0);

                        if (model is { Length: > 0 })
                        {
                            identified++;
                            break;
                        }
                    }
                }

                if (model is not { Length: > 0 })
                {
                    missed.Add(player.WeaponClass ?? "(no class)");
                    continue;
                }

                resolved++;

                if (sample.Count < 10)
                {
                    sample.Add(
                        $"class {player.PlayerClass} '{player.WeaponClass}' " +
                        $"item {player.WeaponItem?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "stock"} -> {model}");
                }
            }
        }

        TestContext.Out.WriteLine(string.Join(Environment.NewLine, sample));
        TestContext.Out.WriteLine($"HOLDING {holding}, IDENTIFIED {identified}, RESOLVED {resolved}");
        TestContext.Out.WriteLine("MISSED: " + string.Join(", ", missed.GroupBy(name => name).Select(g => $"{g.Key} x{g.Count()}")));

        // A positive control before any ratio means anything.
        holding.ShouldBeGreaterThan(0, "nobody was holding a weapon, so nothing was measured");

        // **Every one, not most.** A weapon whose item is unknown draws nothing, and this project's
        // rule is that decoding is total: anything short of everything is a defect here rather than
        // a property of the file.
        identified.ShouldBe(holding, "some held weapons carry no item definition index");
        resolved.ShouldBe(holding, "some items resolve to no model in the schema");
    }
}
