using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene;
using Tf2DemoSalvage.SdkReference;

// Namespaced away from `Tf2DemoSalvage.Corpus.Tests.*`, where `Corpus` binds to the namespace rather than to the
// helper class — the same reason `DisguiseDrawProbe` beside it gives.
namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// On a real recording, a player holding an item whose `anim_slot` overrides the script is DRAWN with the item's
/// table (B105).
/// </summary>
/// <remarks>
/// **The assertion on the output, which is the only one that fails when the wiring does.** The unit tests prove the
/// role is right when asked with the item; this walks the call `MomentScene` makes — `PlayerProps.Add` with the
/// appearance `DemoAppearance.Ensure` builds off the installed game — and reads the <c>Slot</c> it put on each
/// player's pose, so an item dropped anywhere between the demo and the pose shows here and nowhere else.
///
/// **The specimens come from the census, not from a walk of the corpus** (`anim-slot` probe, 2026-09-29). z1800 holds
/// the widest spread measured — sixteen overriding items across 45,638 player-ticks, from the demoman's stock
/// launchers to an engineer's Necro Smasher — and the 2011 koth_viaduct pair is the same session from both points of
/// view, a demoman on stock launchers in each. All three are gcor, so the gate runs this.
///
/// **Each expectation was read by hand from the shipped `items_game.txt` and switched through `ActivityList`**
/// (tf_weaponbase.cpp:4226-4276) — the code under test does not produce its own answers. Only items whose slot
/// CHANGES the role are listed: an item whose slot repeats its script's type would pass with the override removed.
/// </remarks>
public sealed class CorpusItemAnimSlotTests
{
    /// <summary>Every item the census found overriding its script, and the table it names.</summary>
    private static readonly Dictionary<int, string> Expected = new()
    {
        // weapon_grenade_launcher / weapon_quadball: "anim_slot" "secondary", over the launcher script's primary.
        [19] = "SECONDARY", [206] = "SECONDARY", [1151] = "SECONDARY",

        // weapon_stickybomb_launcher, and the jumper and quickiebomb's own: "primary", over the script's secondary.
        [20] = "PRIMARY", [207] = "PRIMARY", [904] = "PRIMARY", [971] = "PRIMARY", [265] = "PRIMARY", [1150] = "PRIMARY",

        // weapon_melee_allclass: "MELEE_ALLCLASS", over whichever class melee it is translated to.
        [264] = "MELEEALLCLASS", [939] = "MELEEALLCLASS", [1013] = "MELEEALLCLASS", [1123] = "MELEEALLCLASS",

        // weapon_sword (via weapon_eyelander) "item1", and the Sharp Dresser's own "ITEM1", over a melee script.
        [132] = "ITEM1", [638] = "ITEM1",

        // weapon_force_a_nature: "item2", over the scattergun's primary.
        [45] = "ITEM2",
    };

    /// <summary>Every tenth frame: the question is whether the item reaches the pose, not how often it is held.</summary>
    private const int Stride = 10;

    [TestCase("tf2-2011-build4604-pov-koth_viaduct")]
    [TestCase("tf2-2011-build4604-stv-koth_viaduct")]
    [TestCase("z1800")]
    public void Add_APlayerHoldingAnItemWhoseSlotOverridesTheScript_IsDrawnWithTheItemsTable(string demo)
    {
        string path = Corpus.Demo(demo);
        string root = GameInstall.Require();
        DemoTimeline timeline = TimelineCache.For(path);

        IPlayerAppearance appearance = DemoAppearance.Ensure(
            DemoAppearance.None, timeline, GameContent.Open(root, NullLoggerFactory.Instance), NullLogger.Instance);

        int drawn = 0;
        Dictionary<int, int> held = [];
        List<string> wrong = [];
        List<SceneProp> props = [];

        foreach (IReadOnlyList<ScenePlayer> players in timeline.Frames.Where((_, index) => index % Stride == 0)
            .Select(frame => frame.Players))
        {
            props.Clear();
            PlayerProps.Add(players, props, appearance, NoBodygroups.Instance);

            foreach (SceneProp prop in props)
            {
                ScenePlayer holder = players.First(player => player.EntityIndex == prop.EntityIndex);
                drawn++;

                if (holder.WeaponItem is not { } item || !Expected.TryGetValue(item, out string? role))
                {
                    continue;
                }

                held[item] = held.GetValueOrDefault(item) + 1;

                if (!string.Equals(prop.Pose.Slot, role, StringComparison.Ordinal) && wrong.Count < 10)
                {
                    wrong.Add(
                        $"player {holder.EntityIndex.ToString(CultureInfo.InvariantCulture)} class " +
                        $"{holder.PlayerClass?.ToString(CultureInfo.InvariantCulture) ?? "?"} {holder.WeaponClass} " +
                        $"item {item.ToString(CultureInfo.InvariantCulture)}: drawn {prop.Pose.Slot ?? "(null)"}, " +
                        $"the item's table is {role}");
                }
            }
        }

        TestContext.Out.WriteLine(
            $"{demo}: {drawn.ToString(CultureInfo.InvariantCulture)} drawn players sampled, holding an overriding item: " +
            string.Join(", ", held.OrderBy(pair => pair.Key).Select(pair =>
                $"{pair.Key.ToString(CultureInfo.InvariantCulture)} x{pair.Value.ToString(CultureInfo.InvariantCulture)}")));

        // The control: a specimen with nobody holding such an item would pass while measuring nothing.
        held.Values.Sum().ShouldBeGreaterThan(0, "the census found players holding these items in this recording");
        wrong.ShouldBeEmpty();
    }
}
