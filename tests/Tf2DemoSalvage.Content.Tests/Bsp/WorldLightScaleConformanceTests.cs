using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Content.Tests.Bsp;

/// <summary>
/// What scale a <c>dworldlight_t</c>'s intensity is stored in, and what that makes it at runtime.
/// </summary>
/// <remarks>
/// **vrad divides by 255 on the way out, and the reader must undo it.** `lightmap.cpp:1647`, under a
/// comment of Valve's asking why the scale is what it is:
///
/// <code>
/// VectorScale( dl->light.intensity, (1.0 / 255.0), wl->intensity );
/// </code>
///
/// The value vrad works in is 0–255 linear — it builds it as <c>pow( r / 255, 2.2 ) * 255</c> and
/// then scales by the falloff denominator at a hundred units — so the LUMP holds a 0–1 number. That
/// is the counterpart of the unexplained 255 in <c>ColorRGBExp32ToVector</c>: divide on write,
/// multiply on read, and Valve flags both.
///
/// **Which makes the runtime arithmetic simpler than it looks.** An ambient cube reaches the shader
/// as <c>linear / 255</c> — proved separately by `AmbientCubeScaleConformanceTests`, where the
/// lightmap and cube medians agree at 0.214 against 0.2358. A world light's contribution in vrad's
/// own units is <c>stored * 255 / falloff</c>, so in the cube's units it is <c>stored / falloff</c>,
/// with no scale at all.
///
/// **This project applied 1/255 instead**, which is that factor the wrong way round, and it is why
/// the direct term measured 0.007 where the bounce measured 0.24. The reasoning recorded for it —
/// that "an ambient cube is normalised to 0–1 on decode" and so the two are 255 apart — describes a
/// normalisation that does not exist: the cube lands near 0–1 because its exponents are negative,
/// not because anything divides it.
///
/// **Twelve tests agreed with the wrong constant** because every one supplies its own intensity and
/// writes the divide into its expected value. `LocalLights`' own remarks say why that could not
/// work: "a test that supplies its own intensity has no opinion about what units a map uses". The
/// unit that decides this is the map, so this suite asserts against vrad's arithmetic instead.
/// </remarks>
public sealed class WorldLightScaleConformanceTests
{
    /// <summary>An ambient cube of nothing, so the light's contribution is all that is read back.</summary>
    private static AmbientCube Dark => default;

    [Test]
    public void AddTo_APointLightAtItsAuthoredDistance_ContributesTheAuthoredBrightness()
    {
        // **The whole conversion in one case, taken from vrad rather than chosen.** A mapper writes
        // `_light "255 255 255 400"`. vrad computes pow(255/255, 2.2) * 255 * (400/255) = 400,
        // scales it by the falloff at a hundred units (100² = 10,000 for pure inverse square), then
        // divides by 255 on export. So the lump holds 400 * 10000 / 255.
        const float Authored = 400f;
        const float Stored = Authored * 100f * 100f / 255f;

        BspWorldLight lamp = new(
            Origin: (0f, 0f, 100f),
            Intensity: (Stored, Stored, Stored),
            Normal: (0f, 0f, -1f),
            Kind: WorldLightKind.Point,
            QuadraticAttenuation: 1f);

        // Read a hundred units below it, on a surface facing straight up at the light — so the
        // strength term is one and what is left is the intensity and the falloff.
        AmbientCube lit = LocalLights.AddTo(Dark, [lamp], 0f, 0f, 0f);

        // **At a hundred units a light is worth exactly what the mapper typed**, which is what
        // vrad's "scale intensity for unit 100 distance" comment means, expressed in the cube's
        // space by dividing the authored 0–255 value by 255.
        lit.PositiveZ.Red.ShouldBe(Authored / 255f, 0.01f);
    }

    [Test]
    public void AddTo_TheSameLightTwiceAsFarAway_IsQuarteredByInverseSquare()
    {
        // The control on the falloff, and it is what makes the case above a measurement of the
        // SCALE rather than of a coincidence: a wrong constant would move both of these together,
        // while a wrong falloff moves only this one.
        const float Authored = 400f;
        const float Stored = Authored * 100f * 100f / 255f;

        BspWorldLight lamp = new(
            Origin: (0f, 0f, 200f),
            Intensity: (Stored, Stored, Stored),
            Normal: (0f, 0f, -1f),
            Kind: WorldLightKind.Point,
            QuadraticAttenuation: 1f);

        AmbientCube lit = LocalLights.AddTo(Dark, [lamp], 0f, 0f, 0f);

        lit.PositiveZ.Red.ShouldBe(Authored / 255f / 4f, 0.01f);
    }

    [Test]
    public void AddTo_ALampOverheadInARoom_OutweighsATypicalBouncedCube()
    {
        // **The claim the picture depends on, stated as a number.** A lamp a hundred and twenty-odd
        // units above a player is the ordinary case in a lit interior, and on koth_harvest the
        // bounced cube there measures about 0.11 while the map's lightmaps run to 0.94 on surfaces
        // near a lamp. A direct term that cannot outweigh the bounce leaves every model lit as
        // though it were in shade, which is B95 exactly.
        const float Authored = 700f;
        const float Stored = Authored * 100f * 100f / 255f;

        BspWorldLight lamp = new(
            Origin: (0f, 0f, 127f),
            Intensity: (Stored, Stored, Stored),
            Normal: (0f, 0f, -1f),
            Kind: WorldLightKind.Point,
            QuadraticAttenuation: 1f);

        AmbientCube lit = LocalLights.AddTo(Dark, [lamp], 0f, 0f, 0f);

        lit.PositiveZ.Red.ShouldBeGreaterThan(
            0.11f,
            "a lamp overhead must outweigh the bounce, or models stay lit as though in shade");
    }
}
