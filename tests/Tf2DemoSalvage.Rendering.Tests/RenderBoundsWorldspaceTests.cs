using System;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// Placing a renderable's box the way <c>DefaultRenderBoundsWorldspace</c> does.
/// </summary>
/// <remarks>
/// **Two branches, and the first one is the whole reason this exists.** A bone-merged entity is
/// culled by its PARENT's box bloated by its own reach; everything else by its own bounds at its
/// render origin and angles. `clientleafsystem.cpp:342`.
///
/// **Written against the SDK rather than from memory**, at the owner's instruction — every number
/// below is arithmetic on Valve's lines, done before the code was run.
/// </remarks>
public sealed class RenderBoundsWorldspaceTests
{
    /// <summary>That an unrotated model is placed by a plain add, as Valve's fast path does.</summary>
    /// <remarks>
    /// `if (angles == vec3_angle) { VectorAdd( mins, origin, absMins ); VectorAdd( maxs, origin,
    /// absMaxs ); }` — no matrix is built at all. A crate spanning ±20 at (100, 200, 8) is
    /// 80..120 by 180..220 by −12..28.
    /// </remarks>
    [Test]
    public void Placed_WithNoRotation_IsTheBoxPlusTheOrigin()
    {
        StudioBox crate = new(-20f, -20f, -20f, 20f, 20f, 20f);

        WorldSpaceBounds.Placed(crate, (100f, 200f, 8f), (0f, 0f, 0f))
            .ShouldBe((80f, 180f, -12f, 120f, 220f, 28f));
    }

    /// <summary>That a rotated model goes through TransformAABB about its own origin.</summary>
    /// <remarks>
    /// **The control for the fast path**: same box, same origin, ninety degrees of yaw. A cube is
    /// unchanged in extent by a quarter turn, so what this pins is that the ORIGIN still lands where
    /// it should — a rotation applied about the world origin instead would fling the box across the
    /// map.
    /// </remarks>
    [Test]
    public void Placed_WithYaw_KeepsTheBoxAboutItsOwnOrigin()
    {
        StudioBox crate = new(-20f, -20f, -20f, 20f, 20f, 20f);

        (float MinX, float MinY, float MinZ, float MaxX, float MaxY, float MaxZ) box =
            WorldSpaceBounds.Placed(crate, (100f, 200f, 8f), (0f, 90f, 0f));

        box.MinX.ShouldBe(80f, 0.01);
        box.MaxX.ShouldBe(120f, 0.01);
        box.MinY.ShouldBe(180f, 0.01);
        box.MaxY.ShouldBe(220f, 0.01);
    }

    /// <summary>That an off-centre box rotates about the origin rather than about itself.</summary>
    /// <remarks>
    /// **The case a cube cannot test.** A box spanning 100..300 in X, yawed ninety degrees about an
    /// origin of (0,0,0), lands at 100..300 in Y. A reimplementation that rotated the box about its
    /// own centre would leave it in X.
    /// </remarks>
    [Test]
    public void Placed_ForAnOffCentreBoxWithYaw_SwingsItAboutTheOrigin()
    {
        StudioBox arm = new(100f, -10f, -10f, 300f, 10f, 10f);

        (float MinX, float MinY, float MinZ, float MaxX, float MaxY, float MaxZ) box =
            WorldSpaceBounds.Placed(arm, (0f, 0f, 0f), (0f, 90f, 0f));

        box.MinY.ShouldBe(100f, 0.01);
        box.MaxY.ShouldBe(300f, 0.01);
        box.MinX.ShouldBe(-10f, 0.01);
        box.MaxX.ShouldBe(10f, 0.01);
    }

    /// <summary>That a bone-merged item takes its wearer's box, grown by its own reach.</summary>
    /// <remarks>
    /// **Valve's arithmetic, worked out by hand first.**
    ///
    /// <code>
    /// float radius = pEnt->GetLocalOrigin().Length();
    /// float flBloatSize = MAX( vAddMins.Length(), vAddMaxs.Length() );
    /// flBloatSize = MAX(flBloatSize, radius);
    /// absMins -= Vector( flBloatSize, flBloatSize, flBloatSize );
    /// </code>
    ///
    /// A hat spanning ±3 in X and Y and 0..6 in Z, merged at a local origin of zero. Its mins corner
    /// is (−3,−3,0), length 4.2426; its maxs corner is (3,3,6), length 7.3485. The bloat is the
    /// larger, 7.3485, applied to every face of the wearer's box.
    ///
    /// **Lengths, not extents** — the trap. Reading `vAddMaxs.Length()` as "6" instead of 7.3485
    /// under-grows the box, which is the exact failure the rule exists to prevent.
    /// </remarks>
    [Test]
    public void Following_ForAMergedItem_BloatsTheWearersBoxByItsCornerLength()
    {
        (float, float, float, float, float, float) wearer = (-16f, -16f, 0f, 16f, 16f, 83f);

        StudioBox hat = new(-3f, -3f, 0f, 3f, 3f, 6f);

        (float MinX, float MinY, float MinZ, float MaxX, float MaxY, float MaxZ) box =
            WorldSpaceBounds.Following(wearer, hat, (0f, 0f, 0f));

        float bloat = MathF.Sqrt((3f * 3f) + (3f * 3f) + (6f * 6f));

        bloat.ShouldBe(7.3485f, 0.001);

        box.MinX.ShouldBe(-16f - bloat, 0.001);
        box.MinZ.ShouldBe(0f - bloat, 0.001);
        box.MaxZ.ShouldBe(83f + bloat, 0.001);
    }

    /// <summary>That a far local origin grows the box further still.</summary>
    /// <remarks>
    /// Valve's own comment on the line: *"if our origin is actually farther away than that, expand
    /// again"*. Same hat, but sitting 40 units from its parent's origin — 40 beats 7.35, so the
    /// bloat is 40.
    /// </remarks>
    [Test]
    public void Following_WhenTheLocalOriginIsFurtherThanTheBox_UsesTheOrigin()
    {
        (float, float, float, float, float, float) wearer = (-16f, -16f, 0f, 16f, 16f, 83f);

        StudioBox hat = new(-3f, -3f, 0f, 3f, 3f, 6f);

        WorldSpaceBounds.Following(wearer, hat, (0f, 0f, 40f)).MaxZ
            .ShouldBe(83f + 40f, 0.001);
    }

    /// <summary>That the merged box always contains the wearer's, which is the safety property.</summary>
    /// <remarks>
    /// **What the rule buys, stated as an invariant.** Whatever the item's own bounds, the result
    /// encloses the parent — so an item can never be culled while its wearer is drawn. That is the
    /// stunstick bug Valve's comment names: a weapon in a hand near the screen edge, culled on its
    /// own box while the soldier holding it stayed visible.
    /// </remarks>
    [Test]
    public void Following_ForAnyItem_EnclosesTheWearer()
    {
        (float MinX, float MinY, float MinZ, float MaxX, float MaxY, float MaxZ) wearer =
            (-16f, -16f, 0f, 16f, 16f, 83f);

        (float MinX, float MinY, float MinZ, float MaxX, float MaxY, float MaxZ) box =
            WorldSpaceBounds.Following(wearer, new StudioBox(0f, 0f, 0f, 0f, 0f, 0f), (0f, 0f, 0f));

        box.MinX.ShouldBeLessThanOrEqualTo(wearer.MinX);
        box.MinY.ShouldBeLessThanOrEqualTo(wearer.MinY);
        box.MinZ.ShouldBeLessThanOrEqualTo(wearer.MinZ);
        box.MaxX.ShouldBeGreaterThanOrEqualTo(wearer.MaxX);
        box.MaxY.ShouldBeGreaterThanOrEqualTo(wearer.MaxY);
        box.MaxZ.ShouldBeGreaterThanOrEqualTo(wearer.MaxZ);
    }

    /// <summary>That an empty placed box is not something to cull against.</summary>
    [TestCase(0f, 0f, 0f, 0f, 0f, 0f, false)]
    [TestCase(-1f, -1f, -1f, 1f, 1f, 1f, true)]
    [TestCase(-1f, -1f, 0f, 1f, 1f, 0f, false)]
    public void IsPlaced_ForABoxWithAndWithoutVolume_SaysSo(
        float minX, float minY, float minZ, float maxX, float maxY, float maxZ, bool expected)
    {
        WorldSpaceBounds.IsPlaced((minX, minY, minZ, maxX, maxY, maxZ)).ShouldBe(expected);
    }
}
