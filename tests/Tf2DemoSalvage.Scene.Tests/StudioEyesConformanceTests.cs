using System;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>The studio render's eye and mouth material values and the client's view target (B725).</summary>
/// <remarks>
/// **Read** from TF2's x64 <c>studiorender.dll</c>: <c>R_StudioEyeballPosition</c> (<c>0x18001e1b0</c>) — origin by the
/// bone, up rotated, forward at the target, <c>right = forward × up</c>, <c>forward += 2 · zoffset · right</c>, right and
/// up re-crossed, <c>mat[i] = -iris_scale · (right | up)</c> with <c>mat[i][3] = 0.5 - org · mat[i]</c>; the mesh setup
/// (<c>0x18000f580</c>) sends <c>$eyeup</c> as the eye's own up; <c>R_MouthSetupVertexShader</c> (<c>0x18001df10</c>)
/// <c>$forward = -( bone · forward )</c>, <c>$illumfactor = clamp( weight, 0, 1 )</c>. From published source,
/// <c>C_TFPlayer::UpdateLookAt</c> (<c>c_tf_player.cpp:6210-6300</c>) and <c>C_BaseFlex::SetViewTarget</c>
/// (<c>c_baseflex.cpp:549-652</c>). Every prediction is *arithmetic* on those.
/// </remarks>
public sealed class StudioEyesConformanceTests
{
    private const float Tolerance = 1e-4f;

    private static readonly float[] Identity = [1f, 0f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 1f, 0f];

    /// <summary>A quarter turn about z (x to y), moved to (10, 20, 30).</summary>
    private static readonly float[] Turned = [0f, -1f, 0f, 10f, 1f, 0f, 0f, 20f, 0f, 0f, 1f, 30f];

    /// <remarks>
    /// An eye at the bone's origin looking along +x with up +z: right is +x × +z = (0, −1, 0), so with iris scale 2 the
    /// projection is U = −2 · right = (0, 2, 0) and V = −2 · up = (0, 0, −2), both offset 0.5.
    /// </remarks>
    [Test]
    public void Shading_AnEyeAtItsBoneLookingAlongX_ProjectsTheIrisByMinusScaleRightAndUp()
    {
        StudioEyeball eye = new(0, (0f, 0f, 0f), 0f, 0.5f, (0f, 0f, 1f), (1f, 0f, 0f), 2f);

        EyeShading shading = StudioEyes.Shading(eye, Identity, (100f, 0f, 0f));

        Near(shading.IrisU, (0f, 2f, 0f, 0.5f));
        Near(shading.IrisV, (0f, 0f, -2f, 0.5f));
    }

    /// <remarks>
    /// The bone turned a quarter about z and moved to (10, 20, 30): the eye's (1, 0, 0) is at (10, 21, 30). Looking along +y,
    /// right = +y × +z = (1, 0, 0) and up stays +z, so iris scale 1 gives U = (−1, 0, 0) with offset 0.5 + 10 and
    /// V = (0, 0, −1) with offset 0.5 + 30. `$eyeup` is the eye's own (0, 1, 0), not turned.
    /// </remarks>
    [Test]
    public void Shading_AnEyeOnATurnedMovedBone_PlacesTheOriginAndOffsetsTheProjection()
    {
        StudioEyeball eye = new(0, (1f, 0f, 0f), 0f, 0.5f, (0f, 0f, 1f), (1f, 0f, 0f), 1f);

        EyeShading shading = StudioEyes.Shading(eye, Turned, (10f, 121f, 30f));

        Near(shading.Origin, (10f, 21f, 30f));
        shading.Up.ShouldBe((0f, 0f, 1f), "the material gets the eye's own up");
        Near(shading.IrisU, (-1f, 0f, 0f, 10.5f));
        Near(shading.IrisV, (0f, 0f, -1f, 30.5f));
    }

    /// <remarks>
    /// `zoffset` 0.5 turns the aim by TWICE it along right: forward (1, 0, 0) + 1 · (0, −1, 0), normalised, is
    /// (√½, −√½, 0); right is then (−√½, −√½, 0) and U = −right. Once would give forward (1, −0.5, 0)/|…| and
    /// U = (0.4472, 0.8944, 0).
    /// </remarks>
    [Test]
    public void Shading_AnEyeWithAZOffset_TurnsItsAimByTwiceTheOffset()
    {
        StudioEyeball eye = new(0, (0f, 0f, 0f), 0.5f, 0.5f, (0f, 0f, 1f), (1f, 0f, 0f), 1f);

        EyeShading shading = StudioEyes.Shading(eye, Identity, (100f, 0f, 0f));

        float half = MathF.Sqrt(0.5f);
        Near(shading.IrisU, (half, half, 0f, 0.5f));
    }

    /// <remarks>The mouth's (1, 0, 0) turned a quarter is (0, 1, 0) and sent negated; a weight of 1.4 is clamped to 1.</remarks>
    [Test]
    public void Mouth_OnATurnedBone_IsTheNegatedForwardAndTheClampedWeight()
    {
        float[] weights = new float[8];
        weights[7] = 1.4f;

        MouthShading mouth = StudioEyes.Mouth(new StudioMouth(0, (1f, 0f, 0f), 7), Turned, weights);

        Near(mouth.Forward, (0f, -1f, 0f));
        mouth.IllumFactor.ShouldBe(1f);
    }

    /// <remarks>
    /// Facing +y (yaw 90): a player behind and one 400 units out are passed over; with nobody else it is 512 ahead; one 100
    /// units ahead and 50 aside is looked at, by its eyes.
    /// </remarks>
    [Test]
    public void LookAt_OtherPlayersAheadBehindAndFar_TakesTheFirstNearAndAheadElseLooksAhead()
    {
        ((float, float, float), (float, float, float)) behind = ((0f, -100f, 0f), (0f, -100f, 64f));
        ((float, float, float), (float, float, float)) far = ((0f, 400f, 0f), (0f, 400f, 64f));
        ((float, float, float), (float, float, float)) near = ((50f, 100f, 0f), (50f, 100f, 64f));

        Near(StudioEyes.LookAt((0f, 0f, 0f), (0f, 90f, 0f), [behind, far]), (0f, 512f, 0f));
        StudioEyes.LookAt((0f, 0f, 0f), (0f, 90f, 0f), [behind, far, near]).ShouldBe((50f, 100f, 64f));
    }

    /// <remarks>
    /// In an identity frame a target straight ahead is kept. One at (0, 100, 0) is first pushed to x = 6, giving a distance
    /// of √(6² + 100²); its direction's x is below cos 30°, so it is clamped to (0.866, 0.5, 0) at that distance.
    /// </remarks>
    [Test]
    public void ViewTarget_AheadAndFarToTheSide_KeepsOneAndClampsTheOtherToTheMaxDeflection()
    {
        Near(StudioEyes.ViewTarget((100f, 0f, 0f), Identity, 0f, 0f, 0.866f), (100f, 0f, 0f));

        float distance = MathF.Sqrt((6f * 6f) + (100f * 100f));
        float side = MathF.Sqrt(1f - (0.866f * 0.866f));

        Near(StudioEyes.ViewTarget((0f, 100f, 0f), Identity, 0f, 0f, 0.866f), (0.866f * distance, side * distance, 0f));
    }

    private static void Near((float X, float Y, float Z) actual, (float X, float Y, float Z) expected)
    {
        actual.X.ShouldBe(expected.X, Tolerance);
        actual.Y.ShouldBe(expected.Y, Tolerance);
        actual.Z.ShouldBe(expected.Z, Tolerance);
    }

    private static void Near((float X, float Y, float Z, float W) actual, (float X, float Y, float Z, float W) expected)
    {
        Near((actual.X, actual.Y, actual.Z), (expected.X, expected.Y, expected.Z));
        actual.W.ShouldBe(expected.W, Tolerance);
    }
}
