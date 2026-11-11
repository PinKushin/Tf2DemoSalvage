using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Scene;

/// <summary>The watcher's `r_ambientboost`, `r_ambientmin` and `r_ambientfraction` (B672).</summary>
/// <param name="Enabled">`r_ambientboost`.</param>
/// <param name="Minimum">`r_ambientmin`.</param>
/// <param name="Fraction">`r_ambientfraction`.</param>
public readonly record struct AmbientBoostSettings(bool Enabled, float Minimum, float Fraction)
{
    /// <summary>As `engine.dll` registers them.</summary>
    public static AmbientBoostSettings Default => new(true, 0.3f, 0.1f);
}

/// <summary>
/// `r_ambientboost`: a model whose cube is swamped by its direct lights draws its cube brighter (`engine.dll` `FUN_1800f2dd0`, B672).
/// </summary>
/// <remarks>
/// **Read in decompile** on build 11191711 (PE stamp `0x6AC41507`), `FUN_1800f2dd0` after the state and its average, before the cube
/// is handed to the studio render (`DAT_1807aace8` vtable `+0x80`). With `STUDIO_ITEM_BLINK` clear, at least one light in the drawn
/// state, the model's `0x800` (`FUN_1800fe8d0` copies it from studiohdr `0x10000`, `STUDIOHDR_FLAGS_AMBIENT_BOOST`) and `r_ambientboost`
/// nonzero:
/// - each face's luminance is `0.59 g + 0.3 r + 0.11 b`; `avg` is their mean, and `max` the largest, starting from the first at 0;
/// - `direct` sums each light's raw intensity at the same weights over `constant + linear d + quadratic d²` from the illumination
///   point, or over 1 when that is at or under `1e-5`: no cone, no style, no range;
/// - at `r_ambientmin` or above, or with `r_ambientfraction · direct` at or under `avg`, the cube goes as it is;
/// - otherwise every channel is scaled by `min( r_ambientfraction · direct / max, 5 )`. **The 5 is a constant** (`r_ambientfactor` is
///   read by nothing here).
///
/// **Only the cube sent to the render is scaled.** The lights the render takes (`FUN_1800f2010`) and the average's stored history are
/// the state's own. Defaults as `FUN_180006890`, `FUN_180006950` and `FUN_180006910` register them: 1, 0.3, 0.1.
/// </remarks>
public sealed class AmbientBoost
{
    /// <summary>The largest scale: `0x180374cc8`.</summary>
    public const float MaximumScale = 5f;

    /// <summary>At or below this an attenuation is taken as 1.</summary>
    private const float SmallestAttenuation = 1e-5f;

    /// <summary>The three cvars, read at each draw as the engine reads them.</summary>
    public AmbientBoostSettings Settings { get; set; } = AmbientBoostSettings.Default;

    /// <summary>The map's sky light, whose origin and falloff the sun's slot carries; null weighs the sun at 1.</summary>
    public BspWorldLight? Sky { get; set; }

    /// <summary>The cube a model draws.</summary>
    /// <param name="studioFlags">The model's own studiohdr flags.</param>
    /// <param name="cube">The state's cube, after its average.</param>
    /// <param name="sun">The sun in the state, or null.</param>
    /// <param name="locals">The state's other lights.</param>
    /// <param name="x">The illumination point.</param>
    /// <param name="y">The illumination point.</param>
    /// <param name="z">The illumination point.</param>
    /// <returns>The cube, scaled when the boost fires.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="locals"/> is null.</exception>
    public AmbientCube Apply(int studioFlags, AmbientCube cube, SunLight? sun, IReadOnlyList<LocalLight> locals, float x, float y, float z)
    {
        ArgumentNullException.ThrowIfNull(locals);

        if (!Settings.Enabled || (studioFlags & StudioModelFlags.AmbientBoost) == 0 || (locals.Count == 0 && sun is null))
        {
            return cube;
        }

        float face0 = Luminance(cube.PositiveX);
        float face1 = Luminance(cube.NegativeX);
        float face2 = Luminance(cube.PositiveY);
        float face3 = Luminance(cube.NegativeY);
        float face4 = Luminance(cube.PositiveZ);
        float face5 = Luminance(cube.NegativeZ);

        // `MAXSS` in the disassembly's order: the first against 0, then each in turn, keeping the earlier on a tie.
        float max = Math.Max(face0, 0f);
        max = face1 <= max ? max : face1;
        max = face2 <= max ? max : face2;
        max = face3 <= max ? max : face3;
        max = face4 <= max ? max : face4;
        max = face5 <= max ? max : face5;

        float average = (face3 + face4 + face5 + face1 + face0 + face2) * 0.16666667f;
        float direct = 0f;

        foreach (LocalLight light in locals)
        {
            direct += Weighed(light.Intensity, (light.X, light.Y, light.Z), light.Constant, light.Linear, light.Quadratic, x, y, z);
        }

        if (sun is { } reaching)
        {
            BspWorldLight sky = Sky ?? new BspWorldLight(default, default, default, WorldLightKind.SkyLight);
            direct += Weighed(
                (reaching.Red, reaching.Green, reaching.Blue),
                sky.Origin,
                sky.ConstantAttenuation,
                sky.LinearAttenuation,
                sky.QuadraticAttenuation,
                x,
                y,
                z);
        }

        if (Settings.Minimum <= average)
        {
            return cube;
        }

        direct *= Settings.Fraction;

        if (direct <= average)
        {
            return cube;
        }

        float scale = direct / max;

        if (scale >= MaximumScale)
        {
            scale = MaximumScale;
        }

        return new AmbientCube(
            Scaled(cube.PositiveX, scale),
            Scaled(cube.NegativeX, scale),
            Scaled(cube.PositiveY, scale),
            Scaled(cube.NegativeY, scale),
            Scaled(cube.PositiveZ, scale),
            Scaled(cube.NegativeZ, scale));
    }

    private static float Luminance((float Red, float Green, float Blue) face) => (face.Green * 0.59f) + (face.Red * 0.3f) + (face.Blue * 0.11f);

    private static float Weighed(
        (float Red, float Green, float Blue) intensity,
        (float X, float Y, float Z) origin,
        float constant,
        float linear,
        float quadratic,
        float x,
        float y,
        float z)
    {
        float dx = origin.X - x;
        float dy = origin.Y - y;
        float dz = origin.Z - z;
        float squared = (dy * dy) + (dx * dx) + (dz * dz);
        float attenuation = (MathF.Sqrt(squared) * linear) + constant + (squared * quadratic);
        float over = attenuation > SmallestAttenuation ? 1f / attenuation : 1f;

        return (over * intensity.Green * 0.59f) + (over * intensity.Red * 0.3f) + (over * intensity.Blue * 0.11f);
    }

    private static (float Red, float Green, float Blue) Scaled((float Red, float Green, float Blue) face, float scale) =>
        (face.Red * scale, face.Green * scale, face.Blue * scale);
}
