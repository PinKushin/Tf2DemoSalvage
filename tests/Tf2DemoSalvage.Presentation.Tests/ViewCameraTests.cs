using System;

using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>Which camera the world is drawn through.</summary>
/// <remarks>
/// **Untested until the orthographic camera was removed** (D98), which is itself part of why the
/// dead branch survived: `Matrix` took five arguments, one of them a projection reachable by exactly
/// one path, and nothing asserted what that path did.
/// </remarks>
public sealed class ViewCameraTests
{
    [Test]
    public void Matrix_InFirstPersonWithAnEye_LooksThroughIt()
    {
        FreeCamera eye = At(10f, 20f, 30f);

        ViewCamera.Matrix(firstPerson: true, eye, Free()).ShouldBe(eye.ToMatrix());
    }

    [Test]
    public void Matrix_InFirstPersonWithNoEye_FallsBackToTheFreeCamera()
    {
        // **D98's decision, and the reason this test exists.** A demo can lose its subject — the
        // recorded view runs out, or a spectated player leaves — and this used to fall back to an
        // ORTHOGRAPHIC overhead projection, a mode nobody chose and nobody could name. It now falls
        // back to the view the viewer can always offer.
        FreeCamera free = Free();

        ViewCamera.Matrix(firstPerson: true, eye: null, free).ShouldBe(free.ToMatrix());
    }

    [Test]
    public void Matrix_OutOfFirstPerson_UsesTheFreeCameraEvenWhenAnEyeExists()
    {
        // **The control on the first case.** With an eye available but first person off, the eye
        // must be ignored — otherwise "first person" would describe nothing, since the eye's mere
        // existence would decide the view.
        FreeCamera free = Free();

        ViewCamera.Matrix(firstPerson: false, At(10f, 20f, 30f), free).ShouldBe(free.ToMatrix());
    }

    [Test]
    public void Matrix_WithNoFreeCamera_Refuses()
    {
        Should.Throw<ArgumentNullException>(
            () => ViewCamera.Matrix(firstPerson: false, eye: null, free: null!));
    }

    private static FreeCamera Free() => At(0f, 0f, 64f);

    private static FreeCamera At(float x, float y, float z) =>
        new() { Origin = (x, y, z), Angles = (0f, 0f, 0f), Aspect = 16f / 9f };
}
