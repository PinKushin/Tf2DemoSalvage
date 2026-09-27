using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>`CTFPlayerShared::GetMaxBuffedHealth` (tf_player_shared.cpp:2235), client side.</summary>
public sealed class HudStateMaxBuffedHealthTests
{
    [TestCase(150, 150, 100, 225)]
    [TestCase(125, 125, 100, 185)]
    [TestCase(150, 150, 260, 260)]
    [TestCase(100, 300, 100, 300)]
    public void GetMaxBuffedHealth_BuffingBaseMaxAndHealth_FloorsToFiveButNeverBelowEither(int buffing, int max, int health, int expected) =>
        HudState.GetMaxBuffedHealth(buffing, max, health).ShouldBe(expected);
}
