namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// TF2's two condition proxies — <c>BurnLevel</c> and <c>YellowLevel</c> (B336).
/// </summary>
/// <remarks>
/// **The two most-run proxies in the game, and neither is evaluated here.** Measured with
/// `vmt-proxy` over the 30,684 shipped materials: `YellowLevel` on **7,570**, `BurnLevel` on
/// **6,718**, against `Sine` on 322 and `TextureScroll` on 283 — the two this project does run.
///
/// **They are published, in TF2's own client source** rather than in the SDK's shared shaders:
/// `CProxyBurnLevel` and `CProxyUrineLevel` are both in `c_tf_player.cpp`, exposed at `:1896` and
/// `:1978`. That is worth stating because `docs/CONFORMANCE.md` had the entity-state proxies filed
/// as needing a decompiler.
///
/// **Both rest at a NO-OP, which is why nothing looked wrong.** `YellowLevel` answers `(1,1,1)` —
/// white, a multiply by one — unless the player is in `TF_COND_URINE`, and `BurnLevel` answers 0
/// unless `TF_COND_BURNING`. So an unevaluated proxy and a player in neither condition produce the
/// same picture, and the difference appears only when somebody is set on fire or jarate'd. That is
/// the census trap this project already records for `$cloakPassEnabled`: a large number that shows
/// nothing until a specific thing happens on camera.
///
/// **The burn START TIME is client-derived, not networked**, and reconstructing it is faithful
/// rather than authored: `CTFPlayerShared::OnAddBurning` sets
/// `m_flBurnEffectStartTime = gpGlobals->curtime` when the condition is ADDED
/// (`tf_player_shared.cpp:7306`) and `OnRemoveBurning` clears it to 0 (`:6884`). The client has no
/// more information than the demo does — the tick the condition bit turns on — so computing it the
/// same way reproduces the client rather than inventing something.
///
/// Written before the implementation, so it is a statement about the engine rather than a
/// description of what got built.
/// </remarks>
public sealed class ConditionProxyConformanceTests
{
    /// <remarks>
    /// **`TF_BURNING_FLAME_LIFE` is 10 seconds** (`tf_shareddefs.h:665`) and the peak is at
    /// start + 0.3:
    ///
    /// <code>
    /// float flBurnPeakTime = flBurnStartTime + 0.3;
    /// if ( gpGlobals->curtime &lt; flBurnPeakTime )
    ///     flTempResult = RemapValClamped( curtime, flBurnStartTime, flBurnPeakTime, 0.0, 1.0 );
    /// else
    ///     flTempResult = RemapValClamped( curtime, flBurnPeakTime, flBurnStartTime + TF_BURNING_FLAME_LIFE, 1.0, 0.0 );
    /// </code>
    ///
    /// So it fades in over 0.3 s and out over the remaining 9.7 — a fast catch and a long
    /// smoulder, not a symmetric triangle. Reading it as symmetric would put the peak at 5 seconds
    /// and show a player barely alight at the moment they catch fire.
    /// </remarks>
    [Test]
    public void BurnLevel_AcrossTheFlamesLife_RisesInAThirdOfASecondAndFallsOverTen()
    {
        MaterialProxies.BurnLevel(since: 0f).ShouldBe(0f, 1e-4f, "nothing at the instant it starts");

        MaterialProxies.BurnLevel(since: 0.15f).ShouldBe(0.5f, 1e-4f, "half way up the ramp");
        MaterialProxies.BurnLevel(since: 0.3f).ShouldBe(1f, 1e-4f, "the peak, at 0.3 seconds");

        // The fall runs from the peak to ten seconds, so five seconds in is roughly half.
        MaterialProxies.BurnLevel(since: 5.15f).ShouldBe(0.5f, 1e-3f, "half way down the long tail");

        MaterialProxies.BurnLevel(since: 10f).ShouldBe(0f, 1e-4f, "out at the flame's life");
    }

    /// <remarks>
    /// **`RemapValClamped` clamps, so past the flame's life it stays at zero** rather than going
    /// negative — which would be a NEGATIVE detail blend and, depending on the shader, a hole.
    /// </remarks>
    [Test]
    public void BurnLevel_PastTheFlamesLife_StaysAtZeroRatherThanGoingNegative()
    {
        MaterialProxies.BurnLevel(since: 11f).ShouldBe(0f, 1e-4f);
        MaterialProxies.BurnLevel(since: 100f).ShouldBe(0f, 1e-4f);
    }

    /// <remarks>
    /// **A negative elapsed time is clamped too.** The engine reaches it through
    /// `RemapValClamped( curtime, start, peak, … )` with curtime below start, which happens on a
    /// seek backwards — and this project seeks constantly where the engine only streams forward.
    /// </remarks>
    [Test]
    public void BurnLevel_BeforeTheBurnStarted_IsZero()
    {
        MaterialProxies.BurnLevel(since: -1f).ShouldBe(0f, 1e-4f);
    }

    /// <remarks>
    /// **The jarate tint is a per-team constant, and the numbers are not colours** — `(6,9,2)` for
    /// RED and `(7,5,1)` for BLU are multipliers well above one, which is how the effect brightens
    /// into yellow rather than tinting toward it. Reading them as 0-255 colour and dividing would
    /// produce an almost-black player.
    /// </remarks>
    [Test]
    public void YellowLevel_APlayerInUrine_IsTheTeamsMultiplier()
    {
        MaterialProxies.YellowLevel(urine: true, isBlue: false).ShouldBe((6f, 9f, 2f));
        MaterialProxies.YellowLevel(urine: true, isBlue: true).ShouldBe((7f, 5f, 1f));
    }

    /// <remarks>
    /// **The control, and it is what makes the proxy invisible on 7,570 materials**: without the
    /// condition the result is white, so evaluating it and not evaluating it are the same picture.
    /// </remarks>
    [Test]
    public void YellowLevel_APlayerNotInUrine_IsWhite()
    {
        MaterialProxies.YellowLevel(urine: false, isBlue: false).ShouldBe((1f, 1f, 1f));
        MaterialProxies.YellowLevel(urine: false, isBlue: true).ShouldBe((1f, 1f, 1f));
    }
}
