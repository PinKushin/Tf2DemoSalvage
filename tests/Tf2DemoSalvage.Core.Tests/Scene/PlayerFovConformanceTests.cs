using System.Collections.Generic;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>`C_BasePlayer::GetFOV` (c_baseplayer.cpp:2435) and `GetDefaultFOV` (baseplayer_shared.cpp:1877) during demo playback.</summary>
/// <remarks>Player 1 is the recorder; player 2 a sniper zoomed to 20.</remarks>
public sealed class PlayerFovConformanceTests
{
    [TestCase(70f, 70f)]
    [TestCase(120f, 90f)]
    [TestCase(5f, 10f)]
    public void Get_WithDemoFovOverride_IsTheClampedOverrideWhateverTheZoom(float overrideFov, float expected) =>
        PlayerFov.Get(Sniper(), Nobody, isLocal: false, overrideFov, curTime: 0f).ShouldBe(expected);

    [Test]
    public void Get_NotZoomed_IsTheDefaultFov() =>
        PlayerFov.Get(Recorder(), Nobody, isLocal: false, 0f, 0f).ShouldBe(85f);

    [Test]
    public void Get_NoDefaultFov_IsTheRulesDefaultOf90() =>
        PlayerFov.Get(Recorder() with { DefaultFov = 0 }, Nobody, isLocal: false, 0f, 0f).ShouldBe(90f);

    [Test]
    public void Get_ADefaultAboveMaxFov_IsCappedAt90() =>
        PlayerFov.Get(Recorder() with { DefaultFov = 120 }, Nobody, isLocal: false, 0f, 0f).ShouldBe(90f);

    [Test]
    public void Get_Zoomed_IsTheZoom() =>
        PlayerFov.Get(Sniper(), Nobody, isLocal: false, 0f, 0f).ShouldBe(20f);

    [Test]
    public void Get_InEyeOnAPlayer_IsThatPlayersFov() =>
        PlayerFov.Get(Recorder() with { ObserverMode = ObserverModes.InEye, ObserverTarget = 2 }, Players, isLocal: true, 0f, 0f)
            .ShouldBe(20f);

    [Test]
    public void Get_InEyeOnAnObserver_IsOwnFov() =>
        PlayerFov.Get(
            Recorder() with { ObserverMode = ObserverModes.InEye, ObserverTarget = 2 },
            index => index == 2 ? Sniper() with { ObserverMode = ObserverModes.Roaming } : null,
            isLocal: true,
            0f,
            0f).ShouldBe(85f);

    [Test]
    public void Get_TheLocalPlayerMidZoom_LerpsOnTheSpline() =>
        PlayerFov.Get(Sniper(), Nobody, isLocal: true, 0f, curTime: 12.55f).ShouldBe(55f, 0.01f);

    [Test]
    public void Get_TheLocalPlayerAQuarterIn_IsSplineShapedNotLinear() =>
        PlayerFov.Get(Sniper(), Nobody, isLocal: true, 0f, curTime: 12.525f).ShouldBe(90f - (70f * 0.15625f), 0.01f);

    [Test]
    public void Get_TheLocalPlayerPastTheZoomTime_IsTheZoom() =>
        PlayerFov.Get(Sniper(), Nobody, isLocal: true, 0f, curTime: 12.7f).ShouldBe(20f);

    [Test]
    public void Get_AnotherPlayerMidZoom_DoesNotLerp() =>
        PlayerFov.Get(Sniper(), Nobody, isLocal: false, 0f, curTime: 12.55f).ShouldBe(20f);

    private static ScenePlayer? Nobody(int index) => null;

    private static ScenePlayer? Players(int index) => index == 2 ? Sniper() : null;

    private static ScenePlayer Recorder() => new(1, 0f, 0f, 0f, 2, 125, 2) { DefaultFov = 85 };

    private static ScenePlayer Sniper() =>
        new(2, 0f, 0f, 0f, 3, 125, 2) { Fov = 20, FovStart = 90, FovTime = 12.5f, FovRate = 0.1f, DefaultFov = 75 };
}
