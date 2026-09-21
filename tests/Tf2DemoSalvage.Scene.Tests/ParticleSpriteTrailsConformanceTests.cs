using System;
using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary><c>render_sprite_trail</c> — <c>C_OP_RenderSpriteTrail</c>, read out of <c>client.dll</c> (B415).</summary>
/// <remarks>
/// **The op ships only in `particles.lib`, statically linked into the client**, so every number here comes from the
/// disassembly of `client-live-x86.dll` (Ghidra project `tf2usermsg`), not from a guess and not from the SDK. The
/// whole trace — the unpack table that names the members, the vtable that names `Render`, and each constant — is in
/// `docs/findings/58`.
///
/// **One quad per particle, not a ribbon.** `GetParticlesToRender` answers four vertices and six indices a particle,
/// and the per-particle builder (<c>FUN_107b8d30</c>) is:
///
/// <code>
/// fade   = age &lt; m_flLengthFadeInTime ? age / m_flLengthFadeInTime : 1           // this+0x5c
/// delta  = PREV_XYZ − XYZ                                                      // points back along travel
/// inv    = rsqrt( |delta|² + 1e-10 )                                           // rsqrtss + one Newton step
/// length = fade · (1/dt) · inv · |delta|² · TRAIL_LENGTH                       // speed × seconds
/// if ( length &gt; 0 ) {
///     if ( m_flMaxLength &lt;= length ) length = m_flMaxLength;                  // this+0x60
///     if ( length &lt;= m_flMinLength ) length = m_flMinLength;                  // this+0x64
///     dir   = delta · inv · length
///     width = min( radius, length )
///     side  = unit( cross( XYZ − camera, dir ) )
///     v0 = XYZ + side·width·0.5   v1 = XYZ − side·width·0.5   v2 = v1 + dir   v3 = v0 + dir
/// }
/// </code>
///
/// **Every test builds its particle by hand and knows the answer because it put the inputs there.** The camera sits
/// at the origin and the particle 100 units down +X, moving along +Y, so the side vector is ±Z and every corner is a
/// round number.
/// </remarks>
public sealed class ParticleSpriteTrailsConformanceTests
{
    /// <summary>The step the particle last moved by, which sets its speed.</summary>
    private const float Step = 0.1f;

    /// <summary>`rsqrtss` plus one Newton step is good to about a part in 10⁷; a corner 50 units out is within this.</summary>
    private const float Close = 1e-3f;

    /// <remarks>
    /// **The whole quad, corner by corner.** A particle 100 units away moving 10 units per 0.1 s — 100 units a second —
    /// with a trail of half a second is 50 units long, and 8 wide from its radius. The quad starts AT the particle and
    /// runs back the way it came: <c>delta</c> is <c>PREV_XYZ − XYZ</c>, so it points behind it.
    /// </remarks>
    [Test]
    public void Build_AMovingParticle_IsOneQuadRunningBackAlongItsPath()
    {
        List<DetailSpriteVertex> corners = Build(Particle(radius: 8f, trail: 0.5f));

        corners.Count.ShouldBe(6, "one quad as two triangles");

        // v0, v1, v2 then v0, v2, v3 — the engine's indices 0,1,2 / 0,2,3.
        ShouldBeAt(corners[0], 100f, 0f, -4f);
        ShouldBeAt(corners[1], 100f, 0f, 4f);
        ShouldBeAt(corners[2], 100f, -50f, 4f);
        ShouldBeAt(corners[3], 100f, 0f, -4f);
        ShouldBeAt(corners[4], 100f, -50f, 4f);
        ShouldBeAt(corners[5], 100f, -50f, -4f);
    }

    /// <remarks>
    /// **The texture runs head to tail**: the engine gives <c>v0</c> and <c>v1</c> the rectangle's bottom edge and the two
    /// tail corners its top, with the default rectangle <c>{0, 0, 1, 1}</c> (<c>DAT_10c37d10</c>) when there is no sheet.
    /// </remarks>
    [Test]
    public void Build_WithNoSheet_MapsTheWholeTextureHeadToTail()
    {
        List<DetailSpriteVertex> corners = Build(Particle(radius: 8f, trail: 0.5f));

        (corners[0].U, corners[0].V).ShouldBe((0f, 1f));
        (corners[1].U, corners[1].V).ShouldBe((1f, 1f));
        (corners[2].U, corners[2].V).ShouldBe((1f, 0f));
        (corners[5].U, corners[5].V).ShouldBe((0f, 0f));
    }

    /// <remarks>**The length is speed times seconds, clamped from above** — here 100 × 5 = 500, held at 122.</remarks>
    [Test]
    public void Build_ALengthPastTheMaximum_IsClampedToIt()
    {
        List<DetailSpriteVertex> corners = Build(Particle(radius: 8f, trail: 5f), maxLength: 122f);

        corners[2].Y.ShouldBe(-122f, Close);
    }

    /// <remarks>
    /// **And from below, but only once it is positive.** 100 × 0.01 = 1 unit, raised to the minimum of 2. The raise
    /// sits INSIDE <c>if ( length &gt; 0 )</c>, which is the next test's subject.
    /// </remarks>
    [Test]
    public void Build_ALengthUnderTheMinimum_IsRaisedToIt()
    {
        List<DetailSpriteVertex> corners = Build(Particle(radius: 8f, trail: 0.01f), minLength: 2f);

        corners[2].Y.ShouldBe(-2f, Close);
    }

    /// <remarks>
    /// **A particle at rest is a quad of zero area, not a missing one — and that is the engine's arithmetic, not a
    /// shortcut.** This test first asserted "draws nothing", reasoning that a zero length fails
    /// <c>if ( length &gt; 0 )</c>. It does not fail: the <c>1e-10</c> guard makes <c>inv · |delta|²</c> about
    /// <c>1e-5</c>, so the branch IS taken and the minimum IS applied. What makes the quad invisible is that both
    /// <c>dir</c> (<c>delta</c> is zero) and <c>side</c> (a cross with zero) come out zero, so all four corners sit on the
    /// particle. The builder emits six vertices there, exactly as `client.dll` does.
    /// </remarks>
    [Test]
    public void Build_AParticleAtRest_IsAQuadOfZeroArea()
    {
        List<DetailSpriteVertex> corners = Build(Particle(radius: 8f, trail: 0.5f, moved: 0f), minLength: 2f);

        corners.Count.ShouldBe(6);

        foreach (DetailSpriteVertex corner in corners)
        {
            ShouldBeAt(corner, 100f, 0f, 0f);
        }
    }

    /// <remarks>
    /// **What genuinely fails the positive test is a length FADE of zero** — a particle on the step it was born, with a
    /// fade-in time, has an age of zero and so a length of exactly zero. No quad, degenerate or otherwise.
    /// </remarks>
    [Test]
    public void Build_AParticleBornThisStepWithAFadeIn_DrawsNothing()
    {
        ParticleStore store = new();

        int index = store.Add(new Vector3(100f, 0f, 0f), lives: 10f);

        store.Move(index, new Vector3(100f, 0f, 0f), new Vector3(100f, -10f, 0f));
        store.Resize(index, 8f);
        store.Stretch(index, 0.5f);

        Build(store, lengthFadeIn: 0.4f).ShouldBeEmpty();
    }

    /// <remarks>
    /// **The length fades in over <c>length fade in time</c>**, off the particle's age: at 0.1 s into a 0.4 s fade it
    /// is a quarter of its full 50.
    /// </remarks>
    [Test]
    public void Build_AYoungParticle_FadesItsLengthIn()
    {
        List<DetailSpriteVertex> corners = Build(Particle(radius: 8f, trail: 0.5f), lengthFadeIn: 0.4f);

        corners[2].Y.ShouldBe(-12.5f, Close);
    }

    /// <remarks>
    /// **The width is the radius OR the length, whichever is less** — <c>if ( length &lt; width ) width = length</c> in
    /// the builder. So a fat particle moving slowly is a square rather than a bar wider than it is long.
    /// </remarks>
    [Test]
    public void Build_ARadiusWiderThanTheLength_IsNarrowedToIt()
    {
        List<DetailSpriteVertex> corners = Build(Particle(radius: 80f, trail: 0.5f));

        corners[0].Z.ShouldBe(-25f, Close);
        corners[1].Z.ShouldBe(25f, Close);
    }

    /// <remarks>
    /// **A transparent particle draws nothing**, which is the engine's own skip — its render list carries alpha as a
    /// byte and the builder returns on zero.
    /// </remarks>
    [Test]
    public void Build_ATransparentParticle_DrawsNothing()
    {
        ParticleStore store = Particle(radius: 8f, trail: 0.5f);
        store.Fade(0, 0f);

        Build(store).ShouldBeEmpty();
    }

    /// <remarks>The tint rides through as the sprite path carries it, 0..255 in the store and 0..1 on the vertex.</remarks>
    [Test]
    public void Build_ATintedParticle_CarriesItsColourAndAlpha()
    {
        ParticleStore store = Particle(radius: 8f, trail: 0.5f);
        store.Fade(0, 0.5f);

        DetailSpriteVertex corner = Build(store)[0];

        (corner.Red, corner.Green, corner.Blue, corner.Alpha).ShouldBe((1f, 1f, 1f, 0.5f));
        corner.Blend.ShouldBe(0f, "the trail writes one frame and no second to mix toward");
    }

    /// <summary>A particle 100 units down +X, having moved <paramref name="moved"/> units along +Y in one step.</summary>
    private static ParticleStore Particle(float radius, float trail, float moved = 10f)
    {
        ParticleStore store = new();

        int index = store.Add(new Vector3(100f, 0f, 0f), lives: 10f);

        store.Move(index, new Vector3(100f, 0f, 0f), new Vector3(100f, -moved, 0f));
        store.Resize(index, radius);
        store.Stretch(index, trail);
        store.Tick(Step);

        return store;
    }

    private static List<DetailSpriteVertex> Build(
        ParticleStore store, float minLength = 0f, float maxLength = 2000f, float lengthFadeIn = 0f)
    {
        List<DetailSpriteVertex> corners = [];

        ParticleSpriteTrails.Build(
            store, camera: Vector3.Zero, corners, sheet: [], rate: 0.1f, minLength, maxLength, lengthFadeIn);

        return corners;
    }

    private static void ShouldBeAt(DetailSpriteVertex corner, float x, float y, float z)
    {
        corner.X.ShouldBe(x, Close);
        corner.Y.ShouldBe(y, Close);
        corner.Z.ShouldBe(z, Close);
    }
}
