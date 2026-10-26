namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// `CBasePlayer::GetFOVDistanceAdjustFactor` (`baseplayer_shared.cpp:1786-1799`): <c>localFOV / defaultFOV</c>, and 1
/// when they are equal or the default is below 0.001. `BuildDetailObjectRenderLists` divides the detail fade by it
/// (`detailobjectsystem.cpp:2813-2825`), which is how a sniper's zoom pushes the grass out (B580).
/// </summary>
[TestFixture]
public sealed class FovDistanceFactorConformanceTests
{
    [TestCase(20f, 90, 20f / 90f)]
    [TestCase(90f, 90, 1f)]
    [TestCase(75f, 90, 75f / 90f)]
    [TestCase(20f, 0, 1f)]
    public void DistanceAdjustFactor_LocalAndDefaultFov_IsTheEnginesRatio(float local, int defaultFov, float expected)
    {
        ViewFov.DistanceAdjustFactor(local, defaultFov).ShouldBe(expected);
    }
}
