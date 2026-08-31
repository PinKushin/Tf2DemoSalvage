using System;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// <c>C_BaseEntity::ComputeFxBlend</c> — how an entity's alpha is arrived at each frame.
/// </summary>
/// <remarks>
/// **Written off <c>game/client/c_baseentity.cpp:3343</c> before any of it existed** (B221), so what
/// it asserts is the engine's behaviour rather than a description of what got built.
///
/// **Nothing on screen could fade until this existed.** `RenderGroups.For` has taken an alpha and a
/// render mode since the two-pass work (D114), and every caller passed `FullyOpaque` and `Normal`
/// because `m_clrRender`, `m_nRenderFX` and `m_nRenderMode` were not decoded at all. So a cloaked
/// spy drew fully solid, nothing was ever partly transparent, and no entity faded in or out.
///
/// **The shape is one switch on <c>m_nRenderFX</c>**, and it is worth knowing what kind of function
/// it is before reading the cases:
///
/// <code>
///   offset = ((int)index) * 363.0;   // "Use ent index to de-sync these fx"
///   switch( m_nRenderFX ) { ... }
///   blend = clamp( blend, 0, 255 );
///   // then a client-side fade multiplies it
/// </code>
///
/// **Four of the cases MUTATE the entity's stored alpha rather than only reading it** — the fades
/// and the solids call `SetRenderColorA`, so the entity creeps toward transparent or opaque one
/// frame at a time. Valve's comment on them is `// JAY: HACK for now -- not time based`, and that
/// is faithfully reproduced: they step per call, not per second. That is why
/// <see cref="FxBlend.Compute"/> returns the new alpha beside the blend instead of taking a byte and
/// giving back an int — a caller that dropped the returned alpha would leave those effects frozen.
///
/// **The time-based cases are functions of <c>gpGlobals->curtime</c>**, which for a viewer is
/// playback time. They are passed it rather than reading a clock, so a test can predict them.
/// </remarks>
public sealed class FxBlendConformanceTests
{
    /// <summary>The de-sync offset, <c>((int)index) * 363.0</c>.</summary>
    private const float Offset = 363f;

    [Test]
    public void Compute_NoEffectInNormalMode_IsFullyOpaque()
    {
        // The default case, and the reason nothing has needed this until now:
        //   if (m_nRenderMode == kRenderNormal) blend = 255; else blend = m_clrRender->a;
        FxBlendResult result = FxBlend.Compute(
            RenderFx.None, RenderModes.Normal, alpha: 90, entityIndex: 0, currentTime: 0f);

        result.Blend.ShouldBe(255, "kRenderNormal ignores the colour's alpha entirely");
    }

    [Test]
    public void Compute_NoEffectInATranslucentMode_TakesTheColoursAlpha()
    {
        // **The control for the case above, and the pair is the whole of the default branch.** An
        // implementation that always answered 255 would satisfy the first test and make every
        // translucent entity solid, which is exactly the bug B221 describes.
        FxBlendResult result = FxBlend.Compute(
            RenderFx.None, RenderModes.TransColor, alpha: 90, entityIndex: 0, currentTime: 0f);

        result.Blend.ShouldBe(90);
    }

    [Test]
    public void Compute_TheEntityIndex_DeSyncsTheEffectsBy363PerIndex()
    {
        // `offset = ((int)index) * 363.0` — Valve's comment is "Use ent index to de-sync these fx",
        // and without it every pulsing light on a map would beat in unison.
        //
        // Predicted exactly rather than "they differ": entity 1 at t=0 must equal entity 0 at
        // t = 363/2, because the case is sin(curtime * 2 + offset) and 2 * (363/2) = 363.
        FxBlendResult one = FxBlend.Compute(
            RenderFx.PulseSlow, RenderModes.TransColor, alpha: 100, entityIndex: 1, currentTime: 0f);

        FxBlendResult zero = FxBlend.Compute(
            RenderFx.PulseSlow, RenderModes.TransColor, alpha: 100, entityIndex: 0,
            currentTime: Offset / 2f);

        one.Blend.ShouldBe(zero.Blend);
    }

    [Test]
    public void Compute_PulseSlow_AddsATenthOfTheRangeOnASlowSine()
    {
        // blend = m_clrRender->a + 0x10 * sin( curtime * 2 + offset )
        // At curtime such that sin() == 1 the answer is alpha + 16.
        float quarterTurn = MathF.PI / 4f; // curtime * 2 = pi/2

        FxBlendResult result = FxBlend.Compute(
            RenderFx.PulseSlow, RenderModes.TransColor, alpha: 100, entityIndex: 0,
            currentTime: quarterTurn);

        result.Blend.ShouldBe(116, "0x10 is 16, and sin is at its maximum here");
    }

    [Test]
    public void Compute_PulseSlowWide_UsesTheWiderAmplitude()
    {
        // The wide variants swing by 0x40 rather than 0x10 — the only difference between the pairs,
        // and getting them the same way round is what this asserts.
        float quarterTurn = MathF.PI / 4f;

        FxBlendResult result = FxBlend.Compute(
            RenderFx.PulseSlowWide, RenderModes.TransColor, alpha: 100, entityIndex: 0,
            currentTime: quarterTurn);

        result.Blend.ShouldBe(164, "0x40 is 64");
    }

    [Test]
    public void Compute_PulseFastWider_IgnoresTheAlphaEntirely()
    {
        // **The odd one out, and it is easy to write as another `alpha +` case.**
        //   blend = ( 0xff * fabs(sin( curtime * 12 + offset ) ) )
        // No `m_clrRender->a` term at all, and an absolute value, so it never goes negative.
        float peak = MathF.PI / 24f; // curtime * 12 = pi/2

        FxBlendResult result = FxBlend.Compute(
            RenderFx.PulseFastWider, RenderModes.TransColor, alpha: 3, entityIndex: 0,
            currentTime: peak);

        result.Blend.ShouldBe(255, "0xff * |sin| at the peak, with the alpha playing no part");
    }

    [Test]
    public void Compute_FadeSlow_StepsTheStoredAlphaDownByOne()
    {
        // `// JAY: HACK for now -- not time based` — it steps per CALL, and it writes the entity's
        // alpha back through SetRenderColorA. The blend is the value AFTER the step.
        FxBlendResult result = FxBlend.Compute(
            RenderFx.FadeSlow, RenderModes.TransColor, alpha: 100, entityIndex: 0, currentTime: 0f);

        result.Alpha.ShouldBe((byte)99, "the entity's own alpha is decremented");
        result.Blend.ShouldBe(99, "and the blend is what it became, not what it was");
    }

    [Test]
    public void Compute_FadeFast_StepsDownByFourAndFloorsAtZero()
    {
        // if ( a > 3 ) SetRenderColorA( a - 4 ); else SetRenderColorA( 0 );
        // The guard is `> 3`, not `>= 4` — the same number, and worth pinning because an off-by-one
        // here leaves an entity stuck at 1..3 instead of reaching zero.
        FxBlend.Compute(RenderFx.FadeFast, RenderModes.TransColor, 100, 0, 0f)
            .Alpha.ShouldBe((byte)96);

        FxBlend.Compute(RenderFx.FadeFast, RenderModes.TransColor, 3, 0, 0f)
            .Alpha.ShouldBe((byte)0, "three is not greater than three, so it clamps to zero");
    }

    [Test]
    public void Compute_SolidSlow_StepsTheStoredAlphaUpTowards255()
    {
        FxBlend.Compute(RenderFx.SolidSlow, RenderModes.TransColor, 100, 0, 0f)
            .Alpha.ShouldBe((byte)101);

        FxBlend.Compute(RenderFx.SolidSlow, RenderModes.TransColor, 255, 0, 0f)
            .Alpha.ShouldBe((byte)255, "already opaque, and `< 255` is false");
    }

    [Test]
    public void Compute_SolidFast_StepsUpByFourAndCeilingsAt255()
    {
        // if ( a < 252 ) SetRenderColorA( a + 4 ); else SetRenderColorA( 255 );
        FxBlend.Compute(RenderFx.SolidFast, RenderModes.TransColor, 100, 0, 0f)
            .Alpha.ShouldBe((byte)104);

        FxBlend.Compute(RenderFx.SolidFast, RenderModes.TransColor, 252, 0, 0f)
            .Alpha.ShouldBe((byte)255, "252 is not less than 252, so it jumps straight to opaque");
    }

    [Test]
    public void Compute_StrobeSlow_IsTheAlphaOrNothing()
    {
        // blend = 20 * sin( curtime * 4 + offset ); if ( blend < 0 ) blend = 0; else blend = a;
        // The 20 never survives — it decides a SIGN and is then thrown away. An implementation that
        // returned the scaled sine would look like a dim strobe and be wrong everywhere.
        float positive = MathF.PI / 8f; // curtime * 4 = pi/2, sin > 0

        FxBlend.Compute(RenderFx.StrobeSlow, RenderModes.TransColor, 137, 0, positive)
            .Blend.ShouldBe(137, "a positive sine gives the alpha itself, not a fraction of it");

        // **Three eighths of pi, so `curtime * 4` is 3pi/2 and the sine is exactly -1.** The first
        // attempt wrote `3 * pi / 8 * 3`, which puts the argument at 14.14 radians — more than two
        // full turns round, where the sine is POSITIVE. The test failed and the code was right; an
        // angle outside [0, 2pi) is not a condition anyone can check by eye.
        float negative = 3f * MathF.PI / 8f;

        FxBlend.Compute(RenderFx.StrobeSlow, RenderModes.TransColor, 137, 0, negative)
            .Blend.ShouldBe(0);
    }

    [Test]
    public void Compute_StrobeWhoseWaveIsAFractionBelowZero_IsTheAlphaBecauseValveTruncatesFirst()
    {
        // **The sign test is on an INT, and that is the whole of B246.** Valve declares
        // `int blend` and assigns a double to it:
        //
        //     blend = 20 * sin( gpGlobals->curtime * 4 + offset );
        //     if ( blend < 0 ) blend = 0; else blend = m_clrRender->a;
        //
        // C++ truncates toward zero on that assignment, so a wave of −0.4 becomes **0**, which is
        // not less than zero — and the engine draws the entity at full alpha. Testing the float
        // instead draws it invisible.
        //
        // **The condition is chosen so correct and broken disagree, which is the only kind that
        // measures anything here.** At `curtime = 0.7904` the argument is 3.1616 radians, a hair
        // past pi, where the sine is −0.0200 and the wave is −0.400. Any input where the wave is
        // below −1 or above 0 predicts the SAME observation both ways, so the two tests above
        // cannot see this and never could.
        FxBlend.Compute(RenderFx.StrobeSlow, RenderModes.TransColor, 137, 0, 0.7904f)
            .Blend.ShouldBe(
                137,
                "20*sin(3.1616) is -0.400, which truncates to 0 — and 0 is not less than 0");
    }

    [Test]
    public void Compute_StrobeWhoseWaveIsAWholeUnitBelowZero_IsStillNothing()
    {
        // **The control, and it is what stops the fix going too far.** Truncation only rescues the
        // window between −1 and 0; at −1.0 or below the int is genuinely negative and the strobe is
        // off. An implementation that dropped the sign test altogether would pass the test above
        // and fail this one.
        //
        // `curtime = 0.8104` puts the argument at 3.2416 radians: sine −0.0999, wave −1.999,
        // truncated −1.
        FxBlend.Compute(RenderFx.StrobeSlow, RenderModes.TransColor, 137, 0, 0.8104f)
            .Blend.ShouldBe(0, "-1.999 truncates to -1, which IS less than zero");
    }

    [Test]
    public void Compute_FlickerSlow_SumsTwoSinesAtDifferentRates()
    {
        // blend = 20 * (sin( curtime * 2 ) + sin( curtime * 17 + offset ))
        // **Note where the offset goes**: on the SECOND term only. The first sine has no de-sync,
        // so every flickering entity shares its slow component and differs in its fast one — which
        // is what makes a row of broken lights look related rather than random.
        //
        // At curtime 0 with entity 0 both sines are 0, the sum is 0, and `blend < 0` is false — so
        // the answer is the alpha. That is the branch an implementation using `<= 0` would get
        // wrong, and zero is exactly where it happens.
        FxBlend.Compute(RenderFx.FlickerSlow, RenderModes.TransColor, 200, 0, 0f)
            .Blend.ShouldBe(200, "a sum of exactly zero is not less than zero");
    }

    [Test]
    public void Compute_ClampsTheResultToAByte()
    {
        // `blend = clamp( blend, 0, 255 )`. A wide pulse on an already-opaque entity overshoots.
        float peak = MathF.PI / 4f;

        FxBlend.Compute(RenderFx.PulseSlowWide, RenderModes.TransColor, 250, 0, peak)
            .Blend.ShouldBe(255, "250 + 64 is clamped rather than wrapped");
    }

    [Test]
    public void Compute_AClientSideFade_MultipliesTheBlendRatherThanReplacingIt()
    {
        //   float flBlend = blend / 255.0f;
        //   float flFade  = nFadeAlpha / 255.0f;
        //   blend = (int)( flBlend * flFade * 255.0f + 0.5f );
        // Note the + 0.5f: it ROUNDS rather than truncating.
        FxBlendResult result = FxBlend.Compute(
            RenderFx.None, RenderModes.Normal, alpha: 255, entityIndex: 0, currentTime: 0f,
            clientSideFade: 128);

        result.Blend.ShouldBe(128, "255 * (128/255), rounded");
    }

    [Test]
    public void Compute_AFullClientSideFade_ChangesNothing()
    {
        // **The control for the multiply, and it guards the common path.** 255 means "no fade", and
        // the engine skips the arithmetic entirely (`if ( nFadeAlpha != 255 )`) — an implementation
        // that always multiplied would be very slightly wrong everywhere through the rounding.
        FxBlend.Compute(RenderFx.None, RenderModes.Normal, 255, 0, 0f, clientSideFade: 255)
            .Blend.ShouldBe(255);
    }
}
