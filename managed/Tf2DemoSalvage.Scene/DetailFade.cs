using System;

namespace Tf2DemoSalvage.Scene;

/// <summary>
/// How far detail sprites are drawn and how they fade out, for one view (B361).
/// </summary>
/// <param name="MaximumSquared">Beyond this squared distance a sprite is not drawn at all.</param>
/// <param name="FadeSquared">Inside this squared distance a sprite is fully opaque.</param>
/// <param name="Falloff">Alpha per unit of squared distance across the band between them.</param>
/// <remarks>
/// **`CDetailObjectSystem::BuildDetailObjectRenderLists`, `detailobjectsystem.cpp:2821`.** The
/// engine computes these three once per view and then applies them per object, which is why they
/// are a value here rather than three arguments passed down a loop.
///
/// **This is what makes detail sprites per-VIEW geometry rather than baked geometry.** A sprite's
/// alpha depends on where the eye is, so the quads cannot be built once at load and reused — which
/// is the same reason the screen-aligned orientations cannot, and why one change covers both.
///
/// **The distance is "calculated badly", in Valve's own words** (`detailobjectsystem.cpp:2756`):
/// it is the squared distance from the eye to the object's ORIGIN, with no regard for the sprite's
/// size, so a tall sprite pops rather than fading from the top down. Transcribed as written.
/// </remarks>
public readonly record struct DetailFade(
    float MaximumSquared, float FadeSquared, float Falloff)
{
    /// <summary>Prepares the fade for one view.</summary>
    /// <param name="distance"><c>cl_detaildist</c> — where detail props stop being drawn.</param>
    /// <param name="fade"><c>cl_detailfade</c> — how wide the band they fade across is.</param>
    /// <param name="factor">
    /// <c>GetFOVDistanceAdjustFactor()</c>, which is <c>localFOV / defaultFOV</c> and therefore
    /// exactly 1 for every view this project draws except a zoomed sniper's.
    /// </param>
    /// <returns>The three numbers the per-object test needs.</returns>
    /// <remarks>
    /// **The FOV factor divides the two limits at different points, and that is Valve's.** The
    /// maximum is squared and THEN divided; the fade is divided and then squared. So a factor of
    /// one half pushes the maximum out by two and the fade by four. At the default FOV the factor
    /// is 1 and the asymmetry is invisible, which is exactly how it would survive being tidied into
    /// symmetry.
    ///
    /// **`MIN( fade, maximum - 1 )` is what makes `cl_detaildist 0` draw nothing** rather than
    /// dividing by zero — `low.cfg` ships that value. The maximum is 0, the fade clamps to 0 and
    /// then to −1, the falloff is a finite 255, and no squared distance is below a maximum of zero.
    /// </remarks>
    public static DetailFade For(float distance, float fade, float factor = 1f)
    {
        float maximumSquared = distance * distance;
        float fadeSquared = distance - fade;

        maximumSquared /= factor;
        fadeSquared /= factor;

        fadeSquared = fadeSquared > 0f ? fadeSquared * fadeSquared : 0f;
        fadeSquared = MathF.Min(fadeSquared, maximumSquared - 1f);

        return new DetailFade(
            maximumSquared, fadeSquared, 255f / (maximumSquared - fadeSquared));
    }

    /// <summary>What the two detail cvars become when a level loads.</summary>
    /// <param name="distance">The config's <c>cl_detaildist</c>, captured before any map loaded.</param>
    /// <param name="fade">The config's <c>cl_detailfade</c>, captured with it.</param>
    /// <param name="controller">
    /// The map's <c>env_detail_controller</c> — its <c>fademindist</c> and <c>fademaxdist</c> — or
    /// null when the map carries none.
    /// </param>
    /// <returns>The pair to draw this level with.</returns>
    /// <remarks>
    /// **`CDetailObjectSystem::LevelInitPostEntity`, `detailobjectsystem.cpp:1524`.** The engine
    /// writes the two cvars itself at level load:
    ///
    /// <code>
    ///   if ( GetDetailController() )
    ///   {
    ///       cl_detailfade.SetValue( MIN( m_flDefaultFadeStart, GetDetailController()-&gt;m_flFadeStartDist ) );
    ///       cl_detaildist.SetValue( MIN( m_flDefaultFadeEnd, GetDetailController()-&gt;m_flFadeEndDist ) );
    ///   }
    ///   else
    ///   {
    ///       // revert to default values if the map doesn't specify
    ///       cl_detailfade.SetValue( m_flDefaultFadeStart );
    ///       cl_detaildist.SetValue( m_flDefaultFadeEnd );
    ///   }
    /// </code>
    ///
    /// **`MIN` runs one way only.** A map can cut a player's detail distance and can never raise
    /// it, so a mapper cannot force grass onto a machine whose owner set `cl_detaildist 0`. The
    /// pair it is taken against is captured once in `Init()` (`detailobjectsystem.cpp:370`) —
    /// before any map loads — which is what makes a config value survive a map that overrode it and
    /// what the <c>else</c> branch above restores.
    ///
    /// **`fademindist` becomes a WIDTH, and that is Valve's rather than a transcription slip.** A
    /// mapper reads the key as "the distance at which fading begins"; it is assigned to
    /// `cl_detailfade`, whose own help text is *"Distance across which detail props fade in"*. So a
    /// controller saying <c>fademindist 700, fademaxdist 1000</c> does not fade from 700 to 1000 —
    /// it gives a 1000-unit maximum with a 700-unit band, which starts at 300. Transcribed as
    /// written, because a "fix" here would disagree with the engine about every map that has one.
    ///
    /// **Measured: no map TF2 ships uses this** — 0 of 234, with `worldspawn` found in 234 of 234
    /// as the control (`detail-controller` probe). It is still live in the game: the string
    /// `env_detail_controller` is in both `tf/bin/x64/server.dll` and `client.dll`, so a community
    /// map that places one by hand gets the behaviour even though no FGD TF2 ships lists the class.
    /// </remarks>
    public static (float Distance, float Fade) ForLevel(
        float distance, float fade, (float FadeStart, float FadeEnd)? controller) =>
        controller is { } map
            ? (MathF.Min(distance, map.FadeEnd), MathF.Min(fade, map.FadeStart))
            : (distance, fade);

    /// <summary>How opaque a sprite at this squared distance is.</summary>
    /// <param name="squaredDistance">From the eye to the sprite's origin.</param>
    /// <returns>0 for a sprite too far to draw, 255 for one inside the fade start.</returns>
    /// <remarks>
    /// **`EnumerateLeaf`, `detailobjectsystem.cpp:2762`**, and the two comparisons are not the same
    /// direction: the maximum is a strict `&lt;` so a sprite exactly at it is dropped, and the fade
    /// start is a strict `&gt;` so a sprite exactly on it is fully opaque.
    ///
    /// **`SetAlpha` takes an `unsigned char`, so the product truncates.** Halfway across the band
    /// is 127 rather than 128, and rounding instead would be wrong by one everywhere in it.
    /// </remarks>
    public byte Alpha(float squaredDistance)
    {
        if (squaredDistance >= MaximumSquared)
        {
            return 0;
        }

        if (squaredDistance <= FadeSquared)
        {
            return 255;
        }

        // Clamped on the way to a byte because the arithmetic is Valve's rather than proven: a
        // falloff derived from a maximum a stranger's config chose can produce anything, and a
        // cast that overflows a byte wraps silently to a dark sprite rather than a bright one.
        return (byte)Math.Clamp(Falloff * (MaximumSquared - squaredDistance), 0f, 255f);
    }
}
