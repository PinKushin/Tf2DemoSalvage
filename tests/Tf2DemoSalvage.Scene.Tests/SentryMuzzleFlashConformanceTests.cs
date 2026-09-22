namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>`TF_3rdPersonMuzzleFlashCallback_SentryGun` (`tf_fx_muzzleflash.cpp:157`) (B415).</summary>
public sealed class SentryMuzzleFlashConformanceTests
{
    [TestCase(1, "muzzle_sentry")]
    [TestCase(2, "muzzle_sentry2")]
    [TestCase(3, "muzzle_sentry2")]
    [TestCase(0, "muzzle_sentry")]
    [TestCase(4, "muzzle_sentry")]
    public void System_AnUpgradeLevel_IsTheCallbacksSwitch(int level, string expected)
    {
        // `switch( m_fFlags ) { case 1: default: "muzzle_sentry"; case 2: case 3: "muzzle_sentry2"; }` — the level-3
        // sentry shares level 2's flash, and anything unexpected takes level 1's.
        SentryMuzzleFlash.System(level).ShouldBe(expected);
    }
}
