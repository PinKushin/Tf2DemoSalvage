using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// Every keyvalue on the gates a TF2 player unquestionably sees — a MEASUREMENT.
/// </summary>
/// <remarks>
/// **B231's contradiction, stated as an experiment.** The measurement says every `func_door` on
/// `cp_fulgur` carries `rendermode 10`; `const.h:363` says that is `kRenderNone`, *"Don't render."*;
/// `C_BaseEntity::ShouldDraw` refuses it in its first line and `C_BaseDoor` does not override
/// `ShouldDraw` at all. And yet the setup gates are visible in the game, and hiding those entities
/// in this viewer removed the gates from the map.
///
/// Three of those four cannot all be true, so this prints EVERY key on the entities in question
/// rather than the handful a previous probe chose to look at. A probe that reports only the fields
/// somebody already suspected cannot find the field nobody thought of — which is how the first
/// reading of this got as far as a merge.
///
/// Explicit, and it asserts nothing about the map: what a map declares is a fact about the map
/// (D38).
/// </remarks>
[Explicit("Diagnostic: dumps every keyvalue on the setup gates and their neighbours.")]
public sealed class SetupGateEntityProbe
{
    /// <summary>The map the owner saw it on.</summary>
    private const string Fulgur = "cp_fulgur";

    [Test]
    public void Read_TheSetupGates_ReportsEveryKeyvalueTheyCarry()
    {
        IReadOnlyList<BspEntity> entities = BspEntities.ReadFrom(MapCache.Bytes(Fulgur));

        // **Named entities, because a setup gate says what it is.** The earlier probe grouped by
        // class and printed four chosen keys; this follows the names the map itself uses.
        List<BspEntity> gates =
        [
            .. entities.Where(entity =>
                Key(entity, "targetname").Contains("gate", StringComparison.OrdinalIgnoreCase)
                || Key(entity, "targetname").Contains("door", StringComparison.OrdinalIgnoreCase)),
        ];

        TestContext.Out.WriteLine($"{gates.Count} entities whose name mentions a gate or door");

        foreach (BspEntity gate in gates.Take(6))
        {
            TestContext.Out.WriteLine(
                $"--- {Key(gate, "targetname")} ({gate.ClassName}) model {Key(gate, "model")}");

            foreach ((string key, string value) in gate.Values.OrderBy(
                pair => pair.Key, StringComparer.Ordinal))
            {
                TestContext.Out.WriteLine($"      {key} = {value}");
            }
        }

        // **Everything PARENTED to a gate, which is the mechanism a hidden mover implies.** If the
        // door is invisible and something visible rides on it, that something names the door in its
        // `parentname` — and this project decodes `moveparent` for wearables but has never been
        // asked whether a brush entity follows one.
        HashSet<string> names =
        [
            .. gates.Select(gate => Key(gate, "targetname"))
                .Where(name => name.Length > 0),
        ];

        List<BspEntity> riders =
        [
            .. entities.Where(entity => names.Contains(Key(entity, "parentname"))),
        ];

        TestContext.Out.WriteLine($"{riders.Count} entities are parented to one of those");

        foreach (BspEntity rider in riders.Take(10))
        {
            TestContext.Out.WriteLine(
                $"    RIDER {rider.ClassName} model {Key(rider, "model")} "
                + $"parent '{Key(rider, "parentname")}' rendermode '{Key(rider, "rendermode")}'");
        }

        // **How many entities declare a parent at all**, because the answer changes what "we do not
        // implement parenting for brushwork" costs.
        int parented = entities.Count(entity => Key(entity, "parentname").Length > 0);

        TestContext.Out.WriteLine($"{parented} of {entities.Count} entities declare a parentname");

        // A precondition on the HARNESS, not a claim about the map.
        entities.Count.ShouldBeGreaterThan(0, "the entity lump read as empty");
    }

    private static string Key(BspEntity entity, string key) =>
        entity.TryGetValue(key, out string value) ? value : string.Empty;
}
