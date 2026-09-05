namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// <c>CBaseAnimatedTextureProxy</c>, which picks a frame from the clock (B338).
/// </summary>
/// <remarks>
/// **The largest unimplemented proxy in the game: 7,027 shipped materials**, measured with
/// `vmt-proxy`. 6,735 of those animate `$detail` through `$detailframe` at 30 frames a second, and
/// the file they animate is `effects/tiledfire/fireLayeredSlowTiled512.vtf` — TF2's fire overlay,
/// **121 frames** of it. So this and `BurnLevel` are one effect: `BurnLevel` decides how much fire
/// to show and this decides which frame of it.
///
/// **It is TIME-driven, not entity-driven**, which is why it belongs with `Sine` and
/// `TextureScroll` rather than with the entity-state proxies:
/// `CAnimatedTextureProxy::GetAnimationStartTime` returns **0**
/// (`animatedtextureproxy.cpp:25-28`), so the animation runs off absolute time and every material
/// sharing the texture shows the same frame.
///
/// <code>
/// float deltaTime = gpGlobals->curtime - startTime;
/// float frame     = m_FrameRate * deltaTime;
/// int   intFrame  = ((int)frame) % numFrames;
/// </code>
///
/// (`baseanimatedtextureproxy.cpp:100-110`.) The rate defaults to **15** and TF2's own materials
/// almost all state 30.
/// </remarks>
public sealed class AnimatedTextureConformanceTests
{
    [Test]
    public void Frame_AtAGivenTimeAndRate_IsTheProductTruncated()
    {
        // 30 frames a second, a third of a second in: frame 10.
        MaterialProxies.AnimationFrame(seconds: 1d / 3d, rate: 30f, frames: 121).ShouldBe(10);

        // Truncated, not rounded — `(int)frame`. At 0.999 of a frame it is still the frame before.
        MaterialProxies.AnimationFrame(seconds: 0.999d / 30d, rate: 30f, frames: 121).ShouldBe(0);
    }

    /// <remarks>
    /// **It wraps by MODULO, so the sheet loops** — and the number it wraps at is the texture's own
    /// frame count, which is why the reader had to start reporting one.
    /// </remarks>
    [Test]
    public void Frame_PastTheLastFrame_WrapsToTheStart()
    {
        MaterialProxies.AnimationFrame(seconds: 121d / 30d, rate: 30f, frames: 121).ShouldBe(0);
        MaterialProxies.AnimationFrame(seconds: 124d / 30d, rate: 30f, frames: 121).ShouldBe(3);
    }

    /// <remarks>
    /// **Time before the start is clamped, not wrapped**: `if (deltaTime &lt; 0.0f) deltaTime =
    /// 0.0f`. A negative modulo in C# is negative, so leaving this out gives a NEGATIVE frame index
    /// — which reads off the front of the file rather than off the end.
    ///
    /// The engine reaches this only through a clock that never goes backwards; this project SEEKS,
    /// so it reaches it whenever anybody scrubs.
    /// </remarks>
    [Test]
    public void Frame_BeforeTheAnimationStarted_IsTheFirstFrame()
    {
        MaterialProxies.AnimationFrame(seconds: -2d, rate: 30f, frames: 121).ShouldBe(0);
    }

    /// <remarks>
    /// **A texture declaring no frames is refused**, as the engine refuses it:
    /// `if ( numFrames &lt;= 0 ) { Assert( !"0 frames in material calling animated texture proxy" );
    /// return; }`. Returning 0 rather than dividing by it, because a modulo by zero throws.
    /// </remarks>
    [Test]
    public void Frame_ForATextureDeclaringNoFrames_IsRefusedRatherThanDivided()
    {
        MaterialProxies.AnimationFrame(seconds: 5d, rate: 30f, frames: 0).ShouldBe(0);
        MaterialProxies.AnimationFrame(seconds: 5d, rate: 30f, frames: -1).ShouldBe(0);
    }

    /// <remarks>
    /// **A one-frame texture is a still one**, and the modulo says so without a special case.
    /// </remarks>
    [Test]
    public void Frame_ForAStillTexture_IsAlwaysZero()
    {
        MaterialProxies.AnimationFrame(seconds: 99d, rate: 30f, frames: 1).ShouldBe(0);
    }

    /// <remarks>
    /// **The rate is what the material states, and the two TF2 uses are far apart** — 30 on the
    /// fire overlay and 15 as the proxy's own default when a material states none
    /// (`m_FrameRate = pKeyValues->GetFloat( "animatedTextureFrameRate", 15 )`). At one second in,
    /// those are frames 30 and 15 of a 121-frame sheet, which are different pictures.
    /// </remarks>
    [Test]
    public void Frame_AtTheDefaultRateAgainstTF2sRate_Differ()
    {
        MaterialProxies.AnimationFrame(seconds: 1d, rate: 30f, frames: 121).ShouldBe(30);
        MaterialProxies.AnimationFrame(seconds: 1d, rate: 15f, frames: 121).ShouldBe(15);
    }
}
