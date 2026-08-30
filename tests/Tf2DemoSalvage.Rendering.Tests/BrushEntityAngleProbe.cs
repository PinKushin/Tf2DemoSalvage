using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// What angles `cp_fulgur`'s brush entities declare, and which of them are doors — a MEASUREMENT.
/// </summary>
/// <remarks>
/// **The owner saw a BLU spawn grate drawn rotated 90 degrees from its correct orientation.** The
/// engine says that cannot be right: `CBaseDoor` asserts its own collision angles are zero —
///
/// <code>
///   Assert( CollisionProp()-&gt;GetCollisionAngles() == vec3_angle );   // doors.cpp:440
/// </code>
///
/// — and a door's direction of travel is a SEPARATE keyfield,
/// <c>DEFINE_KEYFIELD( m_vecMoveDir, FIELD_VECTOR, "movedir" )</c> (`doors.cpp:36`), converted to a
/// vector by `AngleVectors` at spawn (`doors.cpp:262`). So an `angles` key on a `func_door` is a
/// direction, not an orientation, and anything that rotates the brushwork by it is rotating
/// geometry the engine leaves alone.
///
/// **This reports rather than asserts**, because what a community map's entity lump contains is a
/// fact about the map (D38). What it is FOR is telling two explanations apart: an `angles` key of
/// "0 90 0" on the broken grate and "0 0 0" on the working ones says this project is applying a
/// direction as a rotation; identical keys on all of them says the 90 degrees comes from somewhere
/// else entirely and the search moves.
/// </remarks>
[Explicit("Diagnostic: reports the angles a map's brush entities declare.")]
public sealed class BrushEntityAngleProbe
{
    /// <summary>The map the owner saw it on.</summary>
    private const string Fulgur = "cp_fulgur";

    /// <summary>Classes whose brushwork moves, which is where an angle can be a direction.</summary>
    private static readonly string[] Movers =
    [
        "func_door", "func_door_rotating", "func_movelinear", "func_rotating",
        "func_brush", "func_respawnroomvisualizer", "func_respawnroom", "func_areaportal",
    ];

    [Test]
    public void Read_TheMapsBrushEntities_ReportsTheirClassAnglesAndModel()
    {
        IReadOnlyList<BspEntity> entities = BspEntities.ReadFrom(MapCache.Bytes(Fulgur));

        TestContext.Out.WriteLine($"{entities.Count} entities in {Fulgur}");

        // **Every class that owns a brush model**, not just the ones named above: a mapper can put
        // a grate on any of them, and a list written from memory is exactly how a search misses the
        // entity it was looking for.
        List<BspEntity> brushes =
        [
            .. entities.Where(entity =>
                entity.TryGetValue("model", out string model) &&
                model.StartsWith('*')),
        ];

        TestContext.Out.WriteLine($"{brushes.Count} carry a brush model");

        Dictionary<string, int> byClass = [];

        foreach (BspEntity entity in brushes)
        {
            byClass[entity.ClassName] = byClass.GetValueOrDefault(entity.ClassName) + 1;
        }

        TestContext.Out.WriteLine(
            "classes: " + string.Join(
                ", ", byClass.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => $"{pair.Key} x{pair.Value}")));

        foreach (BspEntity entity in brushes)
        {
            string model = entity["model"];
            string angles = Key(entity, "angles");
            string moveDir = Key(entity, "movedir");

            // Only the ones that declare a non-zero angle are interesting; a lump of two hundred
            // brush entities at "0 0 0" is noise that hides the handful that are not.
            if (angles is "0 0 0" or "" && moveDir is "0 0 0" or "")
            {
                continue;
            }

            TestContext.Out.WriteLine(
                $"{model} {entity.ClassName}: angles '{angles}' movedir '{moveDir}' "
                + $"origin '{Key(entity, "origin")}' name '{Key(entity, "targetname")}' "
                + $"team '{Key(entity, "TeamNum")}{Key(entity, "teamnum")}'");
        }

        // **Named separately, because a door is the case the SDK makes a claim about.** Everything
        // above is context; these are the entities `doors.cpp:440` says must not be rotated.
        foreach (BspEntity entity in brushes.Where(
            candidate => Movers.Contains(candidate.ClassName, StringComparer.Ordinal)))
        {
            TestContext.Out.WriteLine(
                $"MOVER {entity["model"]} {entity.ClassName}: "
                + $"angles '{Key(entity, "angles")}' movedir '{Key(entity, "movedir")}' "
                + $"spawnflags '{Key(entity, "spawnflags")}' "
                + $"rendermode '{Key(entity, "rendermode")}' "
                + $"renderamt '{Key(entity, "renderamt")}'");
        }

        // A precondition on the HARNESS rather than a claim about the map: an entity lump that read
        // as empty would make every "no angles found" above a fact about the reader.
        entities.Count.ShouldBeGreaterThan(0, "the entity lump read as empty");
    }

    private static string Key(BspEntity entity, string key) =>
        entity.TryGetValue(key, out string value) ? value : string.Empty;
}
