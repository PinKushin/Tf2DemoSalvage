namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// The one skip for a test that needs Direct3D 11 or its shader compiler and reaches them without
/// <see cref="OffscreenTarget.TryCreate"/> (whose null already says the same). Like "TF2 is not
/// installed", a missing platform is a property of the machine, not of the code (B217).
/// </summary>
internal static class Direct3DRequired
{
    internal static void OrIgnore()
    {
        if (!Direct3DApi.IsAvailable)
        {
            Assert.Ignore("Direct3D 11 is not available on this machine");
        }
    }
}
