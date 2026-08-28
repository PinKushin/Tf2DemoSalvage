using System;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// The six planes a camera sees through, and which boxes fall outside them.
/// </summary>
/// <remarks>
/// **Every prediction below is arithmetic done before the code ran.** A camera at the origin
/// looking down +X with a 90 degree horizontal field of view has side planes at exactly 45 degrees,
/// so their normals are `(±0.7071, ∓0.7071, 0)` and every distance is zero — the planes pass
/// through the eye. That is a case where the right answer can be written down rather than measured
/// out of the implementation, which is the only kind worth asserting here.
///
/// **The controls matter more than the positive cases.** A frustum test that only checks "a thing
/// in front survives" passes against a cull that never culls, which is the failure this work is
/// most likely to have. Each case below has a partner on the other side of a plane.
/// </remarks>
public sealed class ViewFrustumTests
{
    /// <summary>Looking down +X from the origin, 90 degrees across, square viewport.</summary>
    /// <remarks>
    /// Square rather than sixteen-by-nine so the vertical angle is also 90 and the four side planes
    /// are symmetric — an aspect ratio here would make every hand-computed number a different one
    /// per axis for no gain. The aspect ratio's own effect gets its own case below.
    /// </remarks>
    private static ViewFrustum Looking() =>
        ViewFrustum.PerspectiveFromAspect(
            origin: (0f, 0f, 0f),
            forward: (1f, 0f, 0f),
            right: (0f, -1f, 0f),
            up: (0f, 0f, 1f),
            nearZ: 7f,
            farZ: 1000f,
            fovX: 90f,
            aspect: 1f);

    /// <summary>A small box centred on a point.</summary>
    private static (float MinX, float MinY, float MinZ, float MaxX, float MaxY, float MaxZ) At(
        float x, float y, float z, float half = 1f) =>
        (x - half, y - half, z - half, x + half, y + half, z + half);

    private static bool Cull(ViewFrustum frustum, (float, float, float, float, float, float) box) =>
        frustum.Cull(box.Item1, box.Item2, box.Item3, box.Item4, box.Item5, box.Item6);

    [Test]
    public void Cull_ForABoxDeadAhead_KeepsIt()
    {
        Cull(Looking(), At(100f, 0f, 0f)).ShouldBeFalse();
    }

    /// <summary>That a box behind the camera is dropped — the control for the case above.</summary>
    /// <remarks>
    /// **The single case that separates a working cull from no cull at all.** Everything in front
    /// survives either way; only something outside can tell them apart.
    /// </remarks>
    [Test]
    public void Cull_ForABoxBehindTheCamera_DropsIt()
    {
        Cull(Looking(), At(-100f, 0f, 0f)).ShouldBeTrue();
    }

    /// <summary>That the near plane is honoured, not just the view direction.</summary>
    /// <remarks>
    /// Three units ahead is in FRONT of the camera and nearer than the 7-unit near plane, so a cull
    /// that tested only "is it forward" would keep it. Half-extent of one keeps the whole box inside
    /// four units, comfortably short of seven.
    /// </remarks>
    [Test]
    public void Cull_ForABoxNearerThanTheNearPlane_DropsIt()
    {
        Cull(Looking(), At(3f, 0f, 0f)).ShouldBeTrue();
    }

    /// <summary>That the far plane is honoured.</summary>
    [Test]
    public void Cull_ForABoxBeyondTheFarPlane_DropsIt()
    {
        Cull(Looking(), At(2000f, 0f, 0f)).ShouldBeTrue();
    }

    /// <summary>That a box straddling a plane survives, which is what makes culling safe.</summary>
    /// <remarks>
    /// **`BoxOnPlaneSide == 2` culls; 3 does not.** At 90 degrees across, the left plane runs
    /// diagonally, so a point at (100, 100) sits exactly on it. A box centred there straddles, and
    /// an implementation testing `!= 1` instead of `== 2` would drop it — along with everything else
    /// at the edge of the screen, which is the failure mode that looks like a narrowed field of
    /// view.
    /// </remarks>
    [Test]
    public void Cull_ForABoxStraddlingASidePlane_KeepsIt()
    {
        Cull(Looking(), At(100f, 100f, 0f, half: 5f)).ShouldBeFalse();
    }

    /// <summary>That a box well outside a side plane is dropped.</summary>
    /// <remarks>
    /// The partner to the case above: (100, 130) is thirty units past the diagonal, so the whole box
    /// is outside and the answer is 2 rather than 3.
    /// </remarks>
    [Test]
    public void Cull_ForABoxPastASidePlane_DropsIt()
    {
        Cull(Looking(), At(100f, 130f, 0f, half: 5f)).ShouldBeTrue();
    }

    /// <summary>That the side planes are the exact diagonals a 90 degree lens implies.</summary>
    /// <remarks>
    /// **Written down rather than measured.** With forward `(1,0,0)`, right `(0,-1,0)` and
    /// `tan(45°) = 1`, Valve's `VectorMA( right, flTanX, forward, normalPos )` gives `(1,-1,0)`,
    /// which normalises to `(0.7071, -0.7071, 0)` and is stored as FRUSTUM_LEFT. The reflection
    /// `normalPos - 2·right` is `(1, 1, 0)` → `(0.7071, 0.7071, 0)`, stored as FRUSTUM_RIGHT.
    ///
    /// **Both distances are zero**, because a side plane passes through the eye and the eye is at
    /// the origin here. That is `normalPos.Dot( origin )`, and a frustum built with the distance
    /// left out would pass every other test in this file — the planes would have the right
    /// orientation and the wrong position, which only shows once the camera moves.
    /// </remarks>
    [Test]
    public void Perspective_AtNinetyDegrees_PutsTheSidePlanesOnTheDiagonals()
    {
        ViewFrustum frustum = Looking();

        float root = 1f / MathF.Sqrt(2f);

        CullPlane left = frustum.Plane(ViewFrustum.Left);

        left.NormalX.ShouldBe(root, 0.0001);
        left.NormalY.ShouldBe(-root, 0.0001);
        left.NormalZ.ShouldBe(0f, 0.0001);
        left.Distance.ShouldBe(0f, 0.0001);

        CullPlane right = frustum.Plane(ViewFrustum.Right);

        right.NormalX.ShouldBe(root, 0.0001);
        right.NormalY.ShouldBe(root, 0.0001);
        right.NormalZ.ShouldBe(0f, 0.0001);
        right.Distance.ShouldBe(0f, 0.0001);
    }

    /// <summary>That the depth planes carry the eye's own offset along forward.</summary>
    /// <remarks>
    /// **This is the case a camera at the origin cannot test.** `flIntercept = origin · forward`,
    /// so an eye 500 units down +X puts the near plane at `7 + 500` and the far plane's distance at
    /// `-1000 - 500`. Built at the origin both intercepts are zero and an implementation that
    /// dropped the term entirely would look perfect.
    /// </remarks>
    [Test]
    public void Perspective_WithTheEyeAwayFromTheOrigin_OffsetsTheDepthPlanes()
    {
        ViewFrustum frustum = ViewFrustum.PerspectiveFromAspect(
            origin: (500f, 0f, 0f),
            forward: (1f, 0f, 0f),
            right: (0f, -1f, 0f),
            up: (0f, 0f, 1f),
            nearZ: 7f,
            farZ: 1000f,
            fovX: 90f,
            aspect: 1f);

        CullPlane near = frustum.Plane(ViewFrustum.Near);

        near.NormalX.ShouldBe(1f, 0.0001);
        near.Distance.ShouldBe(507f, 0.001);

        CullPlane far = frustum.Plane(ViewFrustum.Far);

        far.NormalX.ShouldBe(-1f, 0.0001);
        far.Distance.ShouldBe(-1500f, 0.001);
    }

    /// <summary>That a wide viewport narrows the vertical angle rather than widening it.</summary>
    /// <remarks>
    /// **`CalcFovY(90, 16/9) = 2·atan(tan(45°) / 1.778) = 58.72°`**, so the top and bottom planes
    /// sit at 29.36 degrees from the axis rather than 45. The distinguishing observation: a box at
    /// (100, 0, 80) is inside a square viewport's 45-degree cone and OUTSIDE a sixteen-by-nine one,
    /// because 80 exceeds `100 · tan(29.36°) = 56.2`.
    ///
    /// Asserting both halves, because an implementation that ignored the aspect ratio entirely
    /// would pass the first line and fail only the second.
    /// </remarks>
    [Test]
    public void Cull_ForABoxHighOnTheScreen_DependsOnTheAspectRatio()
    {
        ViewFrustum square = Looking();

        ViewFrustum wide = ViewFrustum.PerspectiveFromAspect(
            origin: (0f, 0f, 0f),
            forward: (1f, 0f, 0f),
            right: (0f, -1f, 0f),
            up: (0f, 0f, 1f),
            nearZ: 7f,
            farZ: 1000f,
            fovX: 90f,
            aspect: 16f / 9f);

        Cull(square, At(100f, 0f, 80f)).ShouldBeFalse("a square viewport sees 45 degrees up");
        Cull(wide, At(100f, 0f, 80f)).ShouldBeTrue("a wide one sees only 29.4 degrees up");
    }

    /// <summary>That an out-of-range field of view becomes ninety, as Valve's does.</summary>
    /// <remarks>
    /// **A substitution, not a clamp** — `CalcFovY`'s own comment is `// error, set to 90`. So 200
    /// degrees gives the same vertical angle as 90 does, rather than something near 179. Asserted
    /// against the ordinary value rather than against a literal, so the test states the relationship
    /// Valve's code states.
    /// </remarks>
    [TestCase(0.5f)]
    [TestCase(200f)]
    public void VerticalFieldOfView_ForAnAngleOutsideValvesRange_IsWhatNinetyGives(float fovX)
    {
        ViewFrustum.VerticalFieldOfView(fovX, 16f / 9f)
            .ShouldBe(ViewFrustum.VerticalFieldOfView(90f, 16f / 9f), 0.0001);
    }

    /// <summary>That ninety degrees across a sixteen-by-nine screen is 58.72 up and down.</summary>
    [Test]
    public void VerticalFieldOfView_AtNinetyOnAWideScreen_IsFiftyEightPointSeven()
    {
        ViewFrustum.VerticalFieldOfView(90f, 16f / 9f).ShouldBe(58.7155f, 0.001);
    }

    /// <summary>That a frustum nobody built draws everything rather than nothing.</summary>
    /// <remarks>
    /// **The safe direction for a value type that can exist without a constructor.** Culling
    /// everything would be a black screen, which reads as a far deeper fault than a slow one.
    /// </remarks>
    [Test]
    public void Cull_ForAFrustumNeverBuilt_KeepsEverything()
    {
        ViewFrustum none = default;

        none.IsBuilt.ShouldBeFalse();
        Cull(none, At(-100f, 0f, 0f)).ShouldBeFalse();
    }

    [Test]
    public void Plane_ForAFrustumNeverBuilt_Throws()
    {
        ViewFrustum none = default;

        Should.Throw<InvalidOperationException>(() => none.Plane(ViewFrustum.Near));
    }

    [TestCase(-1)]
    [TestCase(6)]
    public void Plane_ForAnIndexOutsideTheSix_Throws(int index)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => Looking().Plane(index));
    }

    /// <summary>That the three-valued side test answers 1, 2 and 3 where Valve's does.</summary>
    /// <remarks>
    /// **The boundary is the case worth pinning.** A box lying exactly ON the plane answers 1, not
    /// 3, because the front bit is `dist1 >= dist` and the back bit is a strict `dist2 &lt; dist`.
    /// A plane at X = 10 with its normal down +X: a box spanning 10..12 is in front (1), one
    /// spanning 8..10 straddles (3) because its minimum is strictly below, and one spanning 5..8 is
    /// behind (2).
    /// </remarks>
    [TestCase(10f, 12f, 1)]
    [TestCase(8f, 10f, 3)]
    [TestCase(5f, 8f, 2)]
    [TestCase(8f, 12f, 3)]
    public void SideOf_AcrossAPlane_MatchesValvesThreeAnswers(float minX, float maxX, int expected)
    {
        CullPlane plane = new(1f, 0f, 0f, 10f);

        plane.SideOf(minX, -1f, -1f, maxX, 1f, 1f).ShouldBe(expected);
    }

    /// <summary>That a negative normal component picks the opposite corner.</summary>
    /// <remarks>
    /// **What Valve's eight-case `switch (p->signbits)` exists to do.** With the normal down −X, the
    /// corner furthest along it is the box's MINIMUM x, not its maximum. A reader that always used
    /// `maxs` would answer 3 here where the truth is 2 — and would do so only for planes with a
    /// negative component, which for a view frustum is half of them.
    ///
    /// Plane: normal (−1,0,0), distance −10, so the volume is everything with x ≤ 10. A box at
    /// 12..14 is wholly outside it.
    /// </remarks>
    [Test]
    public void SideOf_ForANegativeNormal_ChoosesTheOppositeCorner()
    {
        CullPlane plane = new(-1f, 0f, 0f, -10f);

        plane.SideOf(12f, -1f, -1f, 14f, 1f, 1f).ShouldBe(2);
        plane.SideOf(2f, -1f, -1f, 4f, 1f, 1f).ShouldBe(1);
    }
}
