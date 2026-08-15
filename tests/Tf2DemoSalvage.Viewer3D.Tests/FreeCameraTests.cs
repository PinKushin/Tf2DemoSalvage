using Tf2DemoSalvage.Viewer3D;

namespace Tf2DemoSalvage.Viewer3D.Tests;

/// <summary>
/// A camera placed in the world, checked against places whose screen position is known.
/// </summary>
/// <remarks>
/// **Asserted on where a point lands, not on matrix entries.** A matrix test compares the code
/// against itself and passes for any self-consistent mistake — including a mirrored world, which is
/// the specific way a Source camera goes wrong, since the engine's Y axis points LEFT rather than
/// right and <c>AngleVectors</c> hands back RIGHT.
///
/// So each case puts a point somewhere unambiguous — straight ahead, above, to one side — and asks
/// which way it comes out on screen.
/// </remarks>
public sealed class FreeCameraTests
{
    /// <summary>Where a world point lands in normalised device coordinates.</summary>
    private static (float X, float Y, float Z) Project(
        FreeCamera camera, float x, float y, float z)
    {
        float[] m = camera.ToMatrix();

        float clipX = (x * m[0]) + (y * m[4]) + (z * m[8]) + m[12];
        float clipY = (x * m[1]) + (y * m[5]) + (z * m[9]) + m[13];
        float clipZ = (x * m[2]) + (y * m[6]) + (z * m[10]) + m[14];
        float clipW = (x * m[3]) + (y * m[7]) + (z * m[11]) + m[15];

        return (clipX / clipW, clipY / clipW, clipZ / clipW);
    }

    [Test]
    public void SomethingStraightAhead_LandsInTheMiddle()
    {
        // At zero angles a Source camera looks down +X. A point directly along it belongs dead
        // centre, whatever the field of view is.
        FreeCamera camera = new() { Origin = (0f, 0f, 0f), Angles = (0f, 0f, 0f) };

        (float x, float y, float _) = Project(camera, 100f, 0f, 0f);

        x.ShouldBe(0f, 1e-4f);
        y.ShouldBe(0f, 1e-4f);
    }

    [Test]
    public void SomethingHigher_LandsHigherUpTheScreen()
    {
        FreeCamera camera = new() { Origin = (0f, 0f, 0f), Angles = (0f, 0f, 0f) };

        (float _, float low, float _) = Project(camera, 100f, 0f, 0f);
        (float _, float high, float _) = Project(camera, 100f, 0f, 40f);

        high.ShouldBeGreaterThan(low);
    }

    [Test]
    public void SomethingToTheWorldsLeft_LandsOnTheLeftOfTheScreen()
    {
        // **The mirror check, and the reason this file asserts on positions.** Source's +Y axis
        // points LEFT, so a point at +Y must appear on the LEFT of the screen — negative X in
        // normalised device coordinates. Taking AngleVectors' second vector as "left" when it is
        // actually "right" flips exactly this and nothing else, which no matrix-shaped assertion
        // would notice.
        FreeCamera camera = new() { Origin = (0f, 0f, 0f), Angles = (0f, 0f, 0f) };

        (float x, float _, float _) = Project(camera, 100f, 40f, 0f);

        x.ShouldBeLessThan(0f);
    }

    [Test]
    public void TurningToFaceSomething_BringsItToTheMiddle()
    {
        // Yaw is counterclockwise about Z, so a point at +Y is reached by yawing +90 degrees.
        FreeCamera camera = new() { Origin = (0f, 0f, 0f), Angles = (0f, 90f, 0f) };

        (float x, float y, float _) = Project(camera, 0f, 100f, 0f);

        x.ShouldBe(0f, 1e-4f);
        y.ShouldBe(0f, 1e-4f);
    }

    [Test]
    public void LookingDown_PutsTheGroundAheadInTheMiddle()
    {
        // **The view this project has been missing.** Positive pitch is DOWNWARD in Valve's
        // convention — AngleVectors gives forward.z = -sin(pitch) — so a camera above the world
        // pitched to +90 looks straight down, which is the top-down view the viewer already had,
        // now reachable as one orientation among others rather than as the only one.
        FreeCamera camera = new() { Origin = (0f, 0f, 500f), Angles = (90f, 0f, 0f) };

        (float x, float y, float _) = Project(camera, 0f, 0f, 0f);

        x.ShouldBe(0f, 1e-4f);
        y.ShouldBe(0f, 1e-4f);
    }

    [Test]
    public void SomethingNearer_HasSmallerDepth()
    {
        // Depth has to increase away from the camera or everything sorts backwards.
        FreeCamera camera = new() { Origin = (0f, 0f, 0f), Angles = (0f, 0f, 0f) };

        (float _, float _, float near) = Project(camera, 100f, 0f, 0f);
        (float _, float _, float far) = Project(camera, 5000f, 0f, 0f);

        near.ShouldBeLessThan(far);
        near.ShouldBeGreaterThan(0f);
        far.ShouldBeLessThan(1f);
    }

    [Test]
    public void AnOrbitingCamera_KeepsItsFocusInTheMiddle()
    {
        // Whatever angle it is swung to, the thing being orbited stays dead centre -- that is what
        // makes it usable for looking at a player from the side.
        foreach (float pitch in new[] { 0f, 30f, 60f, 89f })
        {
            foreach (float yaw in new[] { 0f, 90f, 180f, 270f })
            {
                FreeCamera camera = FreeCamera.Orbiting((300f, -200f, 64f), pitch, yaw, 600f, 1.5f);

                (float x, float y, float depth) = Project(camera, 300f, -200f, 64f);

                x.ShouldBe(0f, 1e-3f);
                y.ShouldBe(0f, 1e-3f);
                depth.ShouldBeGreaterThan(0f);
                depth.ShouldBeLessThan(1f);
            }
        }
    }

    [Test]
    public void OrbitingAtNinetyDegrees_DoesNotCollapse()
    {
        // **Straight down is the degenerate case**, where forward is parallel to the world up axis
        // and the basis has no meaning. Clamped just inside it, as the engine clamps player pitch,
        // so the picture stays finite rather than vanishing at the one angle a map view wants.
        FreeCamera camera = FreeCamera.Orbiting((0f, 0f, 0f), 90f, 0f, 500f, 1.5f);

        (float x, float y, float _) = Project(camera, 0f, 0f, 0f);

        float.IsFinite(x).ShouldBeTrue();
        float.IsFinite(y).ShouldBeTrue();
    }

    [Test]
    public void AWiderFieldOfView_ShrinksWhatIsOnScreen()
    {
        // The control on the projection: the same point must move TOWARD the middle as the view
        // widens, and a field of view that did nothing would leave it exactly where it was.
        FreeCamera narrow = new() { Origin = (0f, 0f, 0f), Angles = (0f, 0f, 0f), FieldOfView = 50f };
        FreeCamera wide = new() { Origin = (0f, 0f, 0f), Angles = (0f, 0f, 0f), FieldOfView = 100f };

        (float _, float tall, float _) = Project(narrow, 100f, 0f, 40f);
        (float _, float small, float _) = Project(wide, 100f, 0f, 40f);

        small.ShouldBeLessThan(tall);
    }
}
