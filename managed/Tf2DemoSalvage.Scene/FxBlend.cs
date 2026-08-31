using System;

// **Scene rather than Render, and the layering decides it.** `ComputeFxBlend` is `C_BaseEntity`
// behaviour — an entity works out its own alpha — and `Tf2DemoSalvage.Render` references
// `Tf2DemoSalvage.Scene` rather than the other way round, so a blend computed in Render could never
// reach the `ModelInstance` that carries it. `RenderModes` moved here with it for the same reason
// `const.h` is in `public/`: the entity and the leaf system both read those values, and `RenderGroups`
// in Render can still see them from here.
namespace Tf2DemoSalvage.Scene;

/// <summary>The <c>RenderMode_t</c> values, <c>public/const.h:351</c>.</summary>
/// <remarks>
/// **All eleven, and it used to be two.** The old note in `RenderGroups` said *"two of eleven,
/// because two are all that `GetRenderGroup` tests"* — right about the grouping and wrong as a
/// general rule, because `ComputeFxBlend`'s default branch tests <see cref="Normal"/> against
/// everything else. A real match carries <see cref="Glow"/>, <see cref="TransAdd"/> and 118
/// entities at <see cref="None"/> (`CorpusRenderModeTests`), so the other nine are facts about the
/// data rather than claims about what this project handles.
///
/// Sent as 8 bits unsigned, `baseentity.cpp:277`.
/// </remarks>
public static class RenderModes
{
    /// <summary><c>kRenderNormal</c> — the entity's own materials decide, and nothing else.</summary>
    public const int Normal = 0;

    /// <summary><c>kRenderTransColor</c> — <c>c*a+dest*(1-a)</c>.</summary>
    public const int TransColor = 1;

    /// <summary><c>kRenderTransTexture</c> — <c>src*a+dest*(1-a)</c>.</summary>
    public const int TransTexture = 2;

    /// <summary><c>kRenderGlow</c> — <c>src*a+dest</c>, no Z checks, fixed size in screen space.</summary>
    public const int Glow = 3;

    /// <summary><c>kRenderTransAlpha</c> — <c>src*srca+dest*(1-srca)</c>.</summary>
    public const int TransAlpha = 4;

    /// <summary><c>kRenderTransAdd</c> — <c>src*a+dest</c>.</summary>
    public const int TransAdd = 5;

    /// <summary><c>kRenderEnvironmental</c> — *"not drawn, used for environmental effects"*.</summary>
    public const int Environmental = 6;

    /// <summary><c>kRenderTransAddFrameBlend</c> — blends between animation frames.</summary>
    public const int TransAddFrameBlend = 7;

    /// <summary><c>kRenderTransAlphaAdd</c> — <c>src + dest*(1-a)</c>.</summary>
    public const int TransAlphaAdd = 8;

    /// <summary><c>kRenderWorldGlow</c> — as <see cref="Glow"/>, but not fixed in screen space.</summary>
    public const int WorldGlow = 9;

    /// <summary><c>kRenderNone</c> — *"Don't render."*</summary>
    public const int None = 10;
}

/// <summary>The <c>kRenderFx_*</c> values, <c>public/const.h:368</c>.</summary>
/// <remarks>
/// **Sent as 8 bits unsigned** (`baseentity.cpp:276`), so every value fits and none is
/// unrepresentable on the wire.
///
/// Declared in full rather than only the ones handled: a value this project does not implement still
/// has to be recognisable in a log, and the `default` branch is where they land — which is Valve's
/// arrangement too, since `kRenderFxNone` and `kRenderFxClampMinScale` share it explicitly.
/// </remarks>
public static class RenderFx
{
    /// <summary>No effect; the alpha comes from the render mode.</summary>
    public const int None = 0;

    /// <summary>Pulse by 0x10 at two radians a second.</summary>
    public const int PulseSlow = 1;

    /// <summary>Pulse by 0x10 at eight radians a second.</summary>
    public const int PulseFast = 2;

    /// <summary>Pulse by 0x40 at two radians a second.</summary>
    public const int PulseSlowWide = 3;

    /// <summary>Pulse by 0x40 at eight radians a second.</summary>
    public const int PulseFastWide = 4;

    /// <summary>Step the stored alpha down by one each call.</summary>
    public const int FadeSlow = 5;

    /// <summary>Step the stored alpha down by four each call.</summary>
    public const int FadeFast = 6;

    /// <summary>Step the stored alpha up by one each call.</summary>
    public const int SolidSlow = 7;

    /// <summary>Step the stored alpha up by four each call.</summary>
    public const int SolidFast = 8;

    /// <summary>On or off at four radians a second.</summary>
    public const int StrobeSlow = 9;

    /// <summary>On or off at sixteen radians a second.</summary>
    public const int StrobeFast = 10;

    /// <summary>On or off at thirty-six radians a second.</summary>
    public const int StrobeFaster = 11;

    /// <summary>Two summed sines, slow.</summary>
    public const int FlickerSlow = 12;

    /// <summary>Two summed sines, fast.</summary>
    public const int FlickerFast = 13;

    /// <summary>Not handled by <c>ComputeFxBlend</c>; falls to the default branch.</summary>
    public const int NoDissipation = 14;

    /// <summary>Flicker with the distance fade turned off.</summary>
    public const int Distort = 15;

    /// <summary><see cref="Distort"/> plus a distance fade.</summary>
    public const int Hologram = 16;

    /// <summary>Not handled by <c>ComputeFxBlend</c>; falls to the default branch.</summary>
    public const int Explode = 17;

    /// <summary>Not handled by <c>ComputeFxBlend</c>; falls to the default branch.</summary>
    public const int GlowShell = 18;

    /// <summary>Shares the default branch with <see cref="None"/>, explicitly.</summary>
    public const int ClampMinScale = 19;

    /// <summary>Not handled by <c>ComputeFxBlend</c>; falls to the default branch.</summary>
    public const int EnvRain = 20;

    /// <summary>Not handled by <c>ComputeFxBlend</c>; falls to the default branch.</summary>
    public const int EnvSnow = 21;

    /// <summary>Not handled by <c>ComputeFxBlend</c>; falls to the default branch.</summary>
    public const int Spotlight = 22;

    /// <summary>Not handled by <c>ComputeFxBlend</c>; falls to the default branch.</summary>
    public const int Ragdoll = 23;

    /// <summary>0xff times the absolute sine at twelve radians a second; ignores the alpha.</summary>
    public const int PulseFastWider = 24;
}

/// <summary>What one frame's <see cref="FxBlend.Compute"/> produced.</summary>
/// <param name="Blend">The drawn alpha, nought to 255 — Valve's <c>m_nRenderFXBlend</c>.</param>
/// <param name="Alpha">
/// The entity's stored alpha afterwards. Equal to what went in for every effect except the fades
/// and the solids, which step it — so a caller must write this back, or those four effects freeze
/// at their first frame.
/// </param>
public readonly record struct FxBlendResult(int Blend, byte Alpha);

/// <summary>
/// How an entity's drawn alpha is arrived at each frame.
/// </summary>
/// <remarks>
/// **<c>C_BaseEntity::ComputeFxBlend</c>, <c>game/client/c_baseentity.cpp:3343</c>** — transcribed
/// rather than approximated (D121). Until this existed nothing on screen could fade: `m_clrRender`,
/// `m_nRenderFX` and `m_nRenderMode` were not decoded at all, so `RenderGroups.For` received
/// `FullyOpaque` from every caller and a cloaked spy drew fully solid (B221).
///
/// **The entity index de-syncs the effects** — `offset = ((int)index) * 363.0`, with Valve's own
/// comment saying why. Without it every pulsing light on a map beats in unison.
///
/// **Four cases MUTATE the entity's alpha rather than only reading it.** The fades and solids call
/// `SetRenderColorA`, stepping the entity one notch toward transparent or opaque per CALL — Valve's
/// comment is `// JAY: HACK for now -- not time based` and that is reproduced exactly, quirk
/// included. It is why <see cref="Compute"/> hands back the new alpha beside the blend: a caller
/// that stored only the blend would leave those effects frozen at their first frame.
///
/// **Not a departure, but worth knowing**: Valve caches the result per frame
/// (`m_nFXComputeFrame == gpGlobals->framecount`) and this does not. The cache exists because the
/// engine calls it from several places in one frame and the mutating cases must step ONCE; a caller
/// here holds the returned alpha, so stepping once is the caller's business and a second call would
/// be a second frame. Calling this twice for one frame is therefore a caller bug, exactly as it
/// would be in the engine without the guard.
/// </remarks>
public static class FxBlend
{
    /// <summary>Fully opaque, and the value <c>kRenderNormal</c> answers with.</summary>
    public const int FullyOpaque = 255;

    /// <summary>The per-entity de-sync, <c>((int)index) * 363.0</c>.</summary>
    public const float IndexOffset = 363f;

    /// <summary>Computes one frame's alpha for an entity.</summary>
    /// <param name="renderFx">The entity's <c>m_nRenderFX</c>.</param>
    /// <param name="renderMode">Its <c>m_nRenderMode</c>, which only the default branch reads.</param>
    /// <param name="alpha">Its <c>m_clrRender.a</c> going in.</param>
    /// <param name="entityIndex">Its index, which de-syncs the periodic effects.</param>
    /// <param name="currentTime">Playback seconds — the engine's <c>gpGlobals->curtime</c>.</param>
    /// <param name="distanceAlongView">
    /// How far in front of the camera the entity is, for <see cref="RenderFx.Hologram"/> only —
    /// <c>DotProduct( origin - CurrentViewOrigin(), CurrentViewForward() )</c>. Ignored by every
    /// other effect, and by <see cref="RenderFx.Distort"/>, which forces it to 1.
    /// </param>
    /// <param name="distortJitter">
    /// <c>random->RandomInt(-32,31)</c>, supplied rather than drawn here so that a viewer replaying
    /// the same tick twice draws it the same way — and so a test can predict it. The engine's own
    /// value is a fresh random each frame.
    /// </param>
    /// <param name="clientSideFade">
    /// <c>GetClientSideFade()</c>, which is 255 when nothing is fading the entity by distance.
    /// </param>
    /// <returns>The blend and the entity's alpha afterwards.</returns>
    public static FxBlendResult Compute(
        int renderFx,
        int renderMode,
        byte alpha,
        int entityIndex,
        float currentTime,
        float distanceAlongView = 1f,
        int distortJitter = 0,
        byte clientSideFade = 255)
    {
        // "Use ent index to de-sync these fx" — and a float, because the engine's is a double
        // promoted through `sin`. At index 2047 this is 743,061, which a float still holds exactly.
        float offset = entityIndex * IndexOffset;

        int blend;

        switch (renderFx)
        {
            case RenderFx.PulseSlowWide:
                blend = alpha + (int)(0x40 * MathF.Sin((currentTime * 2f) + offset));
                break;

            case RenderFx.PulseFastWide:
                blend = alpha + (int)(0x40 * MathF.Sin((currentTime * 8f) + offset));
                break;

            case RenderFx.PulseFastWider:
                // No alpha term at all, and an absolute value — the odd one out of the pulses.
                blend = (int)(0xff * MathF.Abs(MathF.Sin((currentTime * 12f) + offset)));
                break;

            case RenderFx.PulseSlow:
                blend = alpha + (int)(0x10 * MathF.Sin((currentTime * 2f) + offset));
                break;

            case RenderFx.PulseFast:
                blend = alpha + (int)(0x10 * MathF.Sin((currentTime * 8f) + offset));
                break;

            // "JAY: HACK for now -- not time based". These step per call and write the alpha back.
            case RenderFx.FadeSlow:
                alpha = alpha > 0 ? (byte)(alpha - 1) : (byte)0;
                blend = alpha;
                break;

            case RenderFx.FadeFast:
                // `> 3`, not `>= 4`. The same number, and an off-by-one leaves an entity stuck
                // between 1 and 3 instead of reaching zero.
                alpha = alpha > 3 ? (byte)(alpha - 4) : (byte)0;
                blend = alpha;
                break;

            case RenderFx.SolidSlow:
                alpha = alpha < 255 ? (byte)(alpha + 1) : (byte)255;
                blend = alpha;
                break;

            case RenderFx.SolidFast:
                alpha = alpha < 252 ? (byte)(alpha + 4) : (byte)255;
                blend = alpha;
                break;

            // The 20 decides a SIGN and is then discarded — the strobes are the alpha or nothing,
            // never a fraction of it.
            case RenderFx.StrobeSlow:
                blend = Switched(20f * MathF.Sin((currentTime * 4f) + offset), alpha);
                break;

            case RenderFx.StrobeFast:
                blend = Switched(20f * MathF.Sin((currentTime * 16f) + offset), alpha);
                break;

            case RenderFx.StrobeFaster:
                blend = Switched(20f * MathF.Sin((currentTime * 36f) + offset), alpha);
                break;

            // **The offset is on the SECOND term only**, so every flickering entity shares its slow
            // component and differs in its fast one.
            case RenderFx.FlickerSlow:
                blend = Switched(
                    20f * (MathF.Sin(currentTime * 2f) + MathF.Sin((currentTime * 17f) + offset)),
                    alpha);
                break;

            case RenderFx.FlickerFast:
                blend = Switched(
                    20f * (MathF.Sin(currentTime * 16f) + MathF.Sin((currentTime * 23f) + offset)),
                    alpha);
                break;

            case RenderFx.Hologram:
            case RenderFx.Distort:
                {
                    // "Turn off distance fade" for Distort, which is the only difference between
                    // the two cases.
                    float distance = renderFx == RenderFx.Distort ? 1f : distanceAlongView;

                    if (distance <= 0f)
                    {
                        blend = 0;
                    }
                    else
                    {
                        // Note the assignment: the engine overwrites the entity's alpha with 180
                        // before reading it, so a hologram's authored alpha is discarded.
                        alpha = 180;

                        blend = distance <= 100f
                            ? alpha
                            : (int)((1.0f - ((distance - 100f) * (1.0f / 400.0f))) * alpha);

                        blend += distortJitter;
                    }
                }

                break;

            // **Valve lists `kRenderFxNone` and `kRenderFxClampMinScale` explicitly here**, sharing
            // the default. C# rejects an empty case that falls into `default` (S3458), so they are
            // documented rather than written — the behaviour is identical, and stating it keeps the
            // fact that Valve considered them and chose this branch.
            default:
                blend = renderMode == RenderModes.Normal ? FullyOpaque : alpha;
                break;
        }

        blend = Math.Clamp(blend, 0, 255);

        // "Look for client-side fades". Skipped entirely at 255, which matters: the multiply rounds,
        // so applying it unconditionally would be very slightly wrong on every ordinary entity.
        if (clientSideFade != 255)
        {
            blend = (int)((blend / 255.0f * (clientSideFade / 255.0f) * 255.0f) + 0.5f);
            blend = Math.Clamp(blend, 0, 255);
        }

        return new FxBlendResult(blend, alpha);
    }

    /// <summary>The strobe and flicker shape: a sign test that discards its own magnitude.</summary>
    /// <remarks>
    /// **The cast is not tidying — it is the test Valve performs** (B246). `blend` is declared
    /// <c>int</c> and the wave is assigned to it before the comparison:
    ///
    /// <code>
    ///   blend = 20 * sin( gpGlobals->curtime * 4 + offset );
    ///   if ( blend &lt; 0 ) blend = 0; else blend = m_clrRender->a;
    /// </code>
    ///
    /// C++ truncates toward zero on that assignment, so a wave anywhere in (−1, 0) becomes 0 —
    /// which is not less than zero, and the engine draws the entity at FULL alpha. Comparing the
    /// float instead draws it invisible for about 1.6 % of every cycle, on all five effects that
    /// come through here.
    ///
    /// C#'s <c>(int)</c> on a float truncates toward zero too, so the one cast reproduces it
    /// exactly rather than approximating it.
    /// </remarks>
    private static int Switched(float wave, byte alpha) => (int)wave < 0 ? 0 : alpha;
}
