using System.Numerics;

using Tf2DemoSalvage.Animation.Animating;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>
/// A collision hull's points, out of IVP's convention and into Source's (B400).
/// </summary>
/// <remarks>
/// **`IvpWorldCollision.ToSource` is the inverse of `IvpTransform.Position`, and it was the forward
/// map applied a second time.** The forward map is read out of `vphysics.dll` —
/// `Source (x, y, z)` becomes `IVP (x, −z, y)` (`FUN_180002cc0`, and a second confirmation in
/// `CPhysicsEnvironment::SetGravity` at `1800150f0`, see
/// <see cref="IvpTransformConformanceTests"/>) — so the inverse sends `IVP (x, y, z)` to
/// `Source (x, z, −y)`. Applying `(x, −z, y)` again instead is a 180° rotation about X: every world
/// hull, every prop hull and every ragdoll hull arrived upside down and back to front, while the
/// whole map's EXTENT stayed plausible.
///
/// **That is B400 — corpses falling through floors.** The measurement that named it: of
/// `cp_process_final`'s 2,083 axis-aligned solid box brushes, **0** had a ledge with their exact
/// bounds as the hulls were read, and **1,964** do under this correction. A brush's convex is built
/// with `NO_SHRINK` for the world (`BuildWorldPhysModel( collisionList[i], NO_SHRINK,
/// VPHYSICS_MERGE )`, `utils/vbsp/ivp.cpp:1531`), so an exact box identity is the right instrument
/// and it needs no tolerance argument.
///
/// **Why a symmetric map hid it for so long, and why the fixtures below are asymmetric.** Both
/// candidate maps are proper rotations, so neither mirrors the world; on a 5CP map, symmetric about
/// a diagonal, the wrong one produces a full-sized world whose extents, plane histograms and
/// contents counts are all correct. The same aliasing showed up in the census itself — "mirror x and
/// z" scored 1,771 on `cp_process_final` purely from the map's own symmetry. So every case here uses
/// a point with no two components equal and none zero.
/// </remarks>
public sealed class IvpHullConventionConformanceTests
{
    private const float Tolerance = 1e-3f;

    /// <remarks>
    /// **One metre on each axis, so the permutation is the whole answer.** `(1, 2, 3)` metres is
    /// `(39.37, 118.11, −78.74)` inches: X passes through, IVP Z becomes Source Y, and IVP Y becomes
    /// Source Z negated. A dropped sign or an exchanged pair cannot land on the same triple.
    /// </remarks>
    [Test]
    public void ToSource_AnIvpPoint_PutsZIntoYAndNegatedYIntoZ()
    {
        Vector3 at = IvpWorldCollision.ToSource(new Vector3(1f, 2f, 3f));

        at.X.ShouldBe(39.37f, Tolerance);
        at.Y.ShouldBe(118.11f, Tolerance);
        at.Z.ShouldBe(-78.74f, Tolerance);
    }

    /// <remarks>
    /// **The two directions must describe one convention, not two readings of it.** This is the
    /// assertion the old code would have failed while every self-consistent measurement passed:
    /// `ToSource` and <see cref="IvpTransform.Position"/> disagreed by a 180° rotation, and nothing
    /// that only ever used one of them could tell.
    ///
    /// The bound is `1e-3` inches because Valve stores `0.0254` and `39.37` as a pair rather than
    /// one and its reciprocal, so a round trip loses two parts in a million by design.
    /// </remarks>
    [Test]
    public void ToSource_OfPosition_ReturnsTheSourcePoint()
    {
        (float X, float Y, float Z) ivp = IvpTransform.Position(13f, -29f, 71f);

        Vector3 back = IvpWorldCollision.ToSource(new Vector3(ivp.X, ivp.Y, ivp.Z));

        back.X.ShouldBe(13f, Tolerance);
        back.Y.ShouldBe(-29f, Tolerance);
        back.Z.ShouldBe(71f, Tolerance);
    }

    /// <remarks>
    /// **The axis that separates the two candidates is IVP's Z, not its Y.** Both the correct
    /// inverse and the forward-map-applied-twice send IVP `+Y` to Source `+Z`, so an up-axis
    /// assertion cannot fail for this defect and is not the test to write — IVP `+Z` is, because the
    /// correct inverse sends it to Source `+Y` and the old reading sent it to `−Y`. That sign is the
    /// whole of B400: a horizontal slab landed on the wrong side of the map's mid-plane, which on a
    /// symmetric map is another slab's place.
    /// </remarks>
    [Test]
    public void ToSource_TheIvpZAxis_IsSourcePositiveY()
    {
        Vector3 across = IvpWorldCollision.ToSource(new Vector3(0f, 0f, 1f));

        across.X.ShouldBe(0f, Tolerance);
        across.Y.ShouldBe(39.37f, Tolerance);
        across.Z.ShouldBe(0f, Tolerance);
    }
}
