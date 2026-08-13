using System;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Content.Tests.Bsp;

/// <summary>
/// Placing a model where the map says, facing the way the map says.
/// </summary>
/// <remarks>
/// **Euler angles are a convention, not a calculation**, so these tests check the convention rather
/// than the arithmetic. Which axis each component turns about, in which order, and which way is
/// positive — get any of those wrong and props stand in exactly the right places facing the wrong
/// way, which is a picture nobody can check without knowing the map by heart.
///
/// Each test names a specific expected vector rather than a property, because a prediction is what
/// distinguishes a right convention from a self-consistent wrong one. "The length is preserved"
/// holds for every rotation there is.
/// </remarks>
public sealed class PropTransformTests
{
    private const float Tolerance = 1e-4f;

    [Test]
    public void Apply_NoRotation_MovesTheVertexByTheOrigin()
    {
        PropTransform transform = new(Prop(x: 100f, y: 200f, z: 300f));

        (float x, float y, float z) = transform.Apply(1f, 2f, 3f);

        x.ShouldBe(101f, Tolerance);
        y.ShouldBe(202f, Tolerance);
        z.ShouldBe(303f, Tolerance);
    }

    [Test]
    public void Apply_AYawOfNinety_TurnsForwardIntoLeft()
    {
        // **The one that matters**, because yaw alone is what nearly every prop in a map uses.
        // Source measures yaw about the vertical axis, counter-clockwise seen from above, so the
        // model's +X axis lands on the world's +Y. A sign error here mirrors the map.
        PropTransform transform = new(Prop(yaw: 90f));

        (float x, float y, float z) = transform.Apply(1f, 0f, 0f);

        x.ShouldBe(0f, Tolerance);
        y.ShouldBe(1f, Tolerance);
        z.ShouldBe(0f, Tolerance);
    }

    [Test]
    public void Apply_AYawOfNinety_LeavesTheVerticalAlone()
    {
        // A control: turning about the vertical must not move anything vertically. This is what
        // separates "yaw is about Z" from "yaw is about some other axis", since the previous test
        // alone is satisfied by more than one wrong convention.
        PropTransform transform = new(Prop(yaw: 90f));

        (_, _, float z) = transform.Apply(0f, 0f, 5f);

        z.ShouldBe(5f, Tolerance);
    }

    [Test]
    public void Apply_APitchOfNinety_TurnsForwardIntoDown()
    {
        // Source's pitch is positive DOWNWARD - the matrix's third row is -sin(pitch) - which is
        // the opposite of the usual mathematical convention and exactly the sort of thing that
        // cannot be guessed.
        PropTransform transform = new(Prop(pitch: 90f));

        (float x, float y, float z) = transform.Apply(1f, 0f, 0f);

        x.ShouldBe(0f, Tolerance);
        y.ShouldBe(0f, Tolerance);
        z.ShouldBe(-1f, Tolerance);
    }

    [Test]
    public void Apply_ARollOfNinety_TurnsUpIntoLeftAndLeavesForwardAlone()
    {
        // Roll turns about the forward axis, so the forward axis itself is the fixed one. Naming
        // both halves makes this distinguish roll from pitch, which the moved vector alone does
        // not.
        PropTransform transform = new(Prop(roll: 90f));

        (float ux, float uy, float uz) = transform.Apply(0f, 0f, 1f);

        ux.ShouldBe(0f, Tolerance);
        uy.ShouldBe(-1f, Tolerance);
        uz.ShouldBe(0f, Tolerance);

        (float fx, float fy, float fz) = transform.Apply(1f, 0f, 0f);

        fx.ShouldBe(1f, Tolerance);
        fy.ShouldBe(0f, Tolerance);
        fz.ShouldBe(0f, Tolerance);
    }

    [Test]
    public void Apply_AScale_MultipliesTheOffsetAndNotTheOrigin()
    {
        // The scale applies to the model, not to where it stands. Scaling the origin would move
        // every prop away from the map's centre in proportion to its size, which looks like a
        // camera fault rather than a transform one.
        PropTransform transform = new(Prop(x: 1000f, scale: 2f));

        (float x, _, _) = transform.Apply(10f, 0f, 0f);

        x.ShouldBe(1020f, Tolerance);
    }

    [Test]
    public void Rotate_TakesTheRotationButNotTheOriginOrTheScale()
    {
        // A normal has direction and no position, so a placement far from the origin under a large
        // scale must leave it a unit vector. Applying the full transform to normals instead is a
        // classic slip, and it produces lighting that is wrong in proportion to how far the prop
        // stands from the map's centre.
        PropTransform transform = new(Prop(x: 5000f, y: -3000f, yaw: 90f, scale: 7f));

        (float x, float y, float z) = transform.Rotate(1f, 0f, 0f);

        x.ShouldBe(0f, Tolerance);
        y.ShouldBe(1f, Tolerance);
        z.ShouldBe(0f, Tolerance);
    }

    [Test]
    public void Apply_AnyRotation_PreservesLength()
    {
        // Weak on its own - true of every rotation there is - but it catches a matrix that is not
        // a rotation at all, which the named-vector tests above would miss if all three angles
        // interact.
        PropTransform transform = new(Prop(pitch: 33f, yaw: -117f, roll: 61f));

        (float x, float y, float z) = transform.Rotate(3f, -4f, 12f);

        MathF.Sqrt((x * x) + (y * y) + (z * z)).ShouldBe(13f, 1e-3f);
    }

    [Test]
    public void APlacedPropAndAPosedEntity_TransformIdentically()
    {
        // **One transform for both, because the engine has one.** A static prop from the map file
        // and a networked entity both reduce to an origin, a QAngle and a scale, and AngleMatrix
        // does not care which produced them. The map constructor delegates to the general one, so
        // this is the check that the delegation is faithful rather than a second implementation
        // that agrees today.
        //
        // A yaw-and-pitch case rather than a single axis: with one angle set, almost any wrong
        // axis order still agrees.
        PropTransform fromMap = new(Prop(x: 64f, y: -32f, z: 8f, pitch: 20f, yaw: 135f, scale: 2f));
        PropTransform fromPose = new(64f, -32f, 8f, pitch: 20f, yaw: 135f, roll: 0f, scale: 2f);

        (float mapX, float mapY, float mapZ) = fromMap.Apply(10f, 3f, -5f);
        (float poseX, float poseY, float poseZ) = fromPose.Apply(10f, 3f, -5f);

        poseX.ShouldBe(mapX, 1e-4f);
        poseY.ShouldBe(mapY, 1e-4f);
        poseZ.ShouldBe(mapZ, 1e-4f);
    }

    private static BspStaticProp Prop(
        float x = 0f,
        float y = 0f,
        float z = 0f,
        float pitch = 0f,
        float yaw = 0f,
        float roll = 0f,
        float scale = 1f) =>
        new("models/props/rock.mdl", x, y, z, pitch, yaw, roll, scale);
}
