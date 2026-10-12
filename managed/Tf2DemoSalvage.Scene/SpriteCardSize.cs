using System;
using System.Globalization;

namespace Tf2DemoSalvage.Scene;

/// <summary>SpriteCard's size clamps and fades by distance — `spritecard_vsxx.fxc:199-229` (B529).</summary>
/// <param name="Minimum">`$minsize`: the least radius, as a fraction of the distance to the eye.</param>
/// <param name="Maximum">`$maxsize`: the greatest.</param>
/// <param name="StartFade">`$startfadesize`: the fraction past which the card fades.</param>
/// <param name="EndFade">`$endfadesize`: the fraction past which it is not drawn.</param>
/// <param name="MaximumDistance">`$maxdistance`: how far away it is drawn at all.</param>
/// <param name="FarFadeInterval">`$farfadeinterval`: the stretch before that over which it fades.</param>
/// <remarks>
/// The defaults are the ones `spritecard.cpp:104-108` writes into unset parameters, at which nothing a card can
/// reasonably be is touched: a radius twenty times its distance.
/// </remarks>
public readonly record struct SpriteCardSize(
    float Minimum = 0f,
    float Maximum = 20f,
    float StartFade = 10f,
    float EndFade = 20f,
    float MaximumDistance = 100000f,
    float FarFadeInterval = 400f)
{
    /// <summary>The shader's own defaults.</summary>
    public static SpriteCardSize Default => new(0f);

    /// <summary>One card's drawn radius and the factor its tint, alpha included, is multiplied by.</summary>
    /// <param name="radius">The particle's radius.</param>
    /// <param name="distance">From the card's centre to the eye.</param>
    /// <returns>The radius and the fade; both zero for a card that is not drawn.</returns>
    public (float Radius, float Fade) Apply(float radius, float distance)
    {
        float fade = 1f;

        radius = MathF.Max(radius, Minimum * distance);

        // The fade is taken on the radius BEFORE the maximum clamps it, which is the shader's order.
        if (radius > StartFade * distance)
        {
            if (radius > EndFade * distance)
            {
                return (0f, 0f);
            }

            fade = 1f - ((radius - (StartFade * distance)) / ((EndFade - StartFade) * distance));
        }

        float startFar = MathF.Max(1f, MaximumDistance - FarFadeInterval);
        float far = 1f - Math.Clamp((distance - startFar) / (MaximumDistance - startFar), 0f, 1f);

        return far <= 0f ? (0f, 0f) : (MathF.Min(radius, Maximum * distance), fade * far);
    }

    /// <summary>The sizes a material states, for a SpriteCard; the defaults for any other shader.</summary>
    /// <param name="shader">The material's shader.</param>
    /// <param name="value">A material variable by name, or null when unset.</param>
    /// <returns>The sizes.</returns>
    public static SpriteCardSize Of(string? shader, Func<string, string?> value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (!string.Equals(shader, "SpriteCard", StringComparison.OrdinalIgnoreCase))
        {
            return Default;
        }

        float Stated(string name, float otherwise) =>
            float.TryParse(value(name), NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed) ? parsed : otherwise;

        SpriteCardSize unset = Default;

        return new SpriteCardSize(
            Stated("$minsize", unset.Minimum),
            Stated("$maxsize", unset.Maximum),
            Stated("$startfadesize", unset.StartFade),
            Stated("$endfadesize", unset.EndFade),
            Stated("$maxdistance", unset.MaximumDistance),
            Stated("$farfadeinterval", unset.FarFadeInterval));
    }
}
