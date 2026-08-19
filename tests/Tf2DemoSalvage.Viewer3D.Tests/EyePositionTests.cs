using System;

namespace Tf2DemoSalvage.Viewer3D.Tests;

/// <summary>
/// Recovering the camera position from the view-projection matrix.
/// </summary>
/// <remarks>
/// **A round trip against a value this project did not compute twice.** <c>FreeCamera</c> is given
/// an <c>Origin</c> and produces a matrix from it through the ordinary projection maths; this reads
/// the position back out of that matrix. The two paths share no code, so agreement is evidence
/// rather than a tautology — which is the difference between this and a test that checks an
/// algebraic derivation against itself.
///
/// The derivation being checked: with row-vector convention <c>clip = world * VP</c>, a perspective
/// projection's clip <c>w</c> is the view depth, so the eye maps to <c>w = 0</c>, and its clip
/// <c>x</c> and <c>y</c> are zero because it lies on the view axis. The eye is therefore
/// <c>(0, 0, 1, 0) * VP⁻¹</c> after the homogeneous divide.
/// </remarks>
public sealed class EyePositionTests
{
    [Test]
    public void EyePosition_ACameraMatrix_RecoversItsOrigin()
    {
        // A position with three different, non-zero, non-round components. A transposed or dropped
        // axis cannot pass, and neither can returning the origin.
        (float X, float Y, float Z) origin = (1234.5f, -678.25f, 913.75f);

        FreeCamera camera = new()
        {
            Origin = origin,
            Angles = (17f, 143f, 0f),
            Aspect = 16f / 9f,
        };

        (float X, float Y, float Z)? eye = EyePosition.From(camera.ToMatrix());

        eye.ShouldNotBeNull();

        eye.Value.X.ShouldBe(origin.X, 0.5f);
        eye.Value.Y.ShouldBe(origin.Y, 0.5f);
        eye.Value.Z.ShouldBe(origin.Z, 0.5f);
    }

    [Test]
    public void EyePosition_MovingTheCamera_MovesTheRecoveredPosition()
    {
        // **The discriminator against a reader that returns something plausible but fixed.** A
        // camera looking the same way from two places gives two matrices that differ only in
        // translation, and the recovered positions must differ by exactly the same offset.
        FreeCamera first = new() { Origin = (0f, 0f, 0f), Angles = (0f, 0f, 0f) };
        FreeCamera second = new() { Origin = (100f, 200f, 300f), Angles = (0f, 0f, 0f) };

        (float X, float Y, float Z) a = EyePosition.From(first.ToMatrix())!.Value;
        (float X, float Y, float Z) b = EyePosition.From(second.ToMatrix())!.Value;

        (b.X - a.X).ShouldBe(100f, 0.5f);
        (b.Y - a.Y).ShouldBe(200f, 0.5f);
        (b.Z - a.Z).ShouldBe(300f, 0.5f);
    }

    [Test]
    public void EyePosition_TurningTheCamera_DoesNotMoveIt()
    {
        // The other half of the same control: rotation changes most of the matrix and must not
        // change the answer at all. A derivation that picked up a row of the rotation would pass
        // the translation test above and fail here.
        FreeCamera looking = new() { Origin = (500f, -250f, 128f), Angles = (0f, 0f, 0f) };
        FreeCamera turned = new() { Origin = (500f, -250f, 128f), Angles = (35f, 200f, 0f) };

        (float X, float Y, float Z) a = EyePosition.From(looking.ToMatrix())!.Value;
        (float X, float Y, float Z) b = EyePosition.From(turned.ToMatrix())!.Value;

        b.X.ShouldBe(a.X, 0.5f);
        b.Y.ShouldBe(a.Y, 0.5f);
        b.Z.ShouldBe(a.Z, 0.5f);
    }

    [Test]
    public void EyePosition_ASingularMatrix_HasNoPosition()
    {
        // **Null rather than the origin**, because a degenerate projection has no eye and returning
        // (0,0,0) would put every reflection on the map at map centre — a plausible picture rather
        // than an absent feature.
        EyePosition.From(new float[16]).ShouldBeNull();
    }

    [Test]
    public void EyePosition_AMatrixOfTheWrongLength_IsRejected()
    {
        Should.Throw<ArgumentException>(() => EyePosition.From(new float[15]));
        Should.Throw<ArgumentNullException>(() => EyePosition.From(null!));
    }
}
