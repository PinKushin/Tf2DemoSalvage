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
