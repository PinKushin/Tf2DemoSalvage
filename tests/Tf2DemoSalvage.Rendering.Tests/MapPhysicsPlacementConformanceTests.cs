using System.Linq;
using System.Numerics;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Animation.Animating;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// The map's baked collision, where a real map puts it — the output level of B400.
/// </summary>
/// <remarks>
/// **`IvpHullConventionConformanceTests` pins the transform; nothing asserted that the LOADED world
/// matches the map.** That gap is B400 exactly: `IvpWorldCollision.ToSource` applied
/// `IvpTransform.Position` a second time instead of inverting it, rotating every hull 180° about X,
/// and the whole suite stayed green because both directions of the round trip were wrong together
/// — the fixtures spelled the same wrong map out by hand. A test on the arithmetic alone cannot see
/// that; only one that reads a real `.bsp` through the production loader can
/// (`docs/memory/output-level-assertion-or-it-is-not-done.md`).
///
/// **The oracle needs no tolerance and no model of anything.** vbsp builds the WORLD's convexes
/// with `NO_SHRINK` — `BuildWorldPhysModel( collisionList[i], NO_SHRINK, VPHYSICS_MERGE )`
/// (`utils/vbsp/ivp.cpp:1531`), the `VPHYSICS_SHRINK 0.5` applying only to brush ENTITY models — so
/// a solid brush's convex occupies the brush exactly, and a point in the middle of a plain floor
/// slab is inside some ledge or the reader is wrong.
///
/// **Why a named brush on a named map rather than a census.** A percentage over every brush is the
/// instrument that FOUND the defect and it costs seconds to run; what a suite needs afterwards is
/// the smallest thing that fails when the fault returns. This point was measured under the defect
/// and no ledge of 33,789 contained it, with the broadphase bypassed.
/// </remarks>
public sealed class MapPhysicsPlacementConformanceTests
{
    /// <summary>The map the B400 corpse fell through.</summary>
    private const string Map = "cp_process_final";

    /// <summary>
    /// The middle of the floor slab under that corpse — `LUMP_BRUSHES` brush 451, contents
    /// `CONTENTS_SOLID`, six sides, no displacement, x[−2888, −2688] y[−2304, −2104] z[576, 704].
    /// </summary>
    /// <remarks>
    /// **The brush's own middle, not the corpse's resting place.** A point on the surface is a
    /// boundary case and a point where a body rests depends on the simulation; the centre of a
    /// 200 × 200 × 128 slab is unambiguously interior, so "some ledge contains it" is a statement
    /// about the geometry alone.
    /// </remarks>
    private static readonly Vector3 InsideTheFloor = new(-2788f, -2204f, 640f);

    [Test]
    public void Ledges_AtAPointInsideASolidWorldBrush_ContainIt()
    {
        if (!MapCache.Exists(Map))
        {
            Assert.Ignore($"{Map} is not installed");
        }

        MapLevel level = MapLevel.Read(MapCache.Bytes(Map), NullLogger.Instance);

        level.Physics.Ledges.Count.ShouldBeGreaterThan(
            0, "the control: this map's physics lump reads at all");

        // **The broadphase is deliberately bypassed.** It is a grid of bounding spheres, and a
        // hull in the wrong place takes its sphere with it — so a query through the broadphase
        // would answer "nothing here" for a wrong reason as readily as a right one. Walking every
        // ledge asks only about the geometry.
        bool inside = level.Physics.Ledges.Any(ledge =>
            ledge.Planes.Count > 0 &&
            ledge.Planes.All(face =>
                Vector3.Dot(face.Normal, InsideTheFloor) - face.Distance <= 0f));

        inside.ShouldBeTrue(
            $"no ledge of {level.Physics.Ledges.Count} contains ({InsideTheFloor.X}, " +
            $"{InsideTheFloor.Y}, {InsideTheFloor.Z}), which is inside brush 451 — a plain solid " +
            "floor slab. The world is compiled NO_SHRINK, so its convex occupies the brush.");
    }

    // **A whole-map bounds check was written here and DELETED, and the reason belongs in the
    // file.** "Every ledge has a vertex inside the world's extent" reads like a second, scaling
    // statement of the same fact. It cannot fail for this defect: B400 was a ROTATION, so the
    // wrongly-placed world occupied very nearly the same extent as the right one — that is the
    // whole reason nine measurements missed it. A test that passes under the fault it names is
    // worse than no test, because it makes the suite look like it covers this
    // (`docs/memory/most-of-a-decoder-is-untested.md`).
    //
    // **The measurement that DOES scale needs the brush lump**: of `cp_process_final`'s 2,083
    // axis-aligned solid box brushes, 0 had a ledge with their exact bounds under the defect and
    // 1,964 do now. That lives in `corpse-drop`, where it costs seconds and is read by a person,
    // rather than here (D126: a question about one map at one moment is a probe, not a test).
}
