using System;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene;

/// <summary>
/// One of the lights a model is drawn with, the fields of <c>LightDesc_t</c> the glint reads (<c>lightdesc.h:34-57</c>).
/// </summary>
/// <param name="Directional"><c>MATERIAL_LIGHT_DIRECTIONAL</c>: lighting along <paramref name="Direction"/>, from nowhere.</param>
/// <param name="Position"><c>m_Position</c>, for a point or spot light.</param>
/// <param name="Direction"><c>m_Direction</c>: the way a directional light travels.</param>
/// <param name="Colour"><c>m_Color</c>.</param>
/// <param name="Range"><c>m_Range</c>; 0 for none.</param>
/// <param name="Attenuation0"><c>m_Attenuation0</c>, the constant term.</param>
/// <param name="Attenuation1"><c>m_Attenuation1</c>, the linear term.</param>
/// <param name="Attenuation2"><c>m_Attenuation2</c>, the quadratic term.</param>
public readonly record struct GlintLight(
    bool Directional,
    (float X, float Y, float Z) Position,
    (float X, float Y, float Z) Direction,
    (float R, float G, float B) Colour,
    float Range,
    float Attenuation0,
    float Attenuation1,
    float Attenuation2);

/// <summary>One glint, the 20-byte record <c>0x18001d600</c> writes: where on the eye, in world units, and its colour.</summary>
/// <param name="X">Along the view's right from the eye's centre.</param>
/// <param name="Y">Along the view's up.</param>
/// <param name="Red">Its colour, the light's times its attenuation.</param>
/// <param name="Green">Green.</param>
/// <param name="Blue">Blue.</param>
public readonly record struct EyeGlintPoint(float X, float Y, float Red, float Green, float Blue);

/// <summary>The <c>Eyes</c> shader's glint, as TF2's studio render makes it per eye draw (B725 item 3).</summary>
/// <remarks>
/// **Read** from TF2's x64 <c>studiorender.dll</c> (disassembled; the studio render is closed). The eyeball mesh's setup
/// (<c>0x18000f7c1</c>) sets <c>$glintu</c>/<c>$glintv</c> (<see cref="Projection"/>, <c>0x18001dbe0</c>) and then
/// <c>0x18001e020</c> chooses the <c>$glint</c> texture: <c>vgui/black</c> where the eye is narrower than
/// <c>r_eyeglintlodpixels</c> (<see cref="Drawn"/>), else, with <c>r_glint_procedural 0</c> at DX 9, <c>_rt_eyeglint</c>
/// rendered by <c>0x18001f510</c> — black again if no light makes a glint (<see cref="Glints"/>), else one quad per glint
/// through <c>eyeglint_vs20</c>/<c>eyeglint_ps2x</c>, added into the 32×32 BGRA8888 target (<see cref="Rasterise"/>). The
/// <c>Eyes</c> pixel shader adds the sample times the ambient damping (<c>eyes_ps2x.fxc:56</c>). The derivation and the
/// evidence are <c>docs/RISKS.md</c> B725.
///
/// **The glints are in world units on a target that spans one unit, and the projection spans the eye's diameter**: the
/// pass centres a glint at <c>(x + 0.5) · 32</c> texels, so the target covers ±0.5 units, while <c>$glintu</c> maps
/// ±radius onto it. The two agree only for an eye of radius 0.5. Ported as read; the engine's own arithmetic, not ours.
/// </remarks>
public static class EyeGlint
{
    /// <summary>The target's side: <c>_rt_eyeglint</c> is 32×32 (<c>0x18001dcb0</c>).</summary>
    public const int Size = 32;

    /// <summary>The most glints <c>0x18001f510</c> asks for.</summary>
    public const int MaxGlints = 16;

    /// <summary><c>r_eyeglintlodpixels</c>'s default: narrower than this the eye takes <c>vgui/black</c>.</summary>
    public const float LodPixels = 20f;

    /// <summary><c>FLT_EPSILON</c>, which the attenuations without a constant term add (<c>0x180021680</c>'s table).</summary>
    private const float Epsilon = 1.1920929e-07f;

    /// <summary>What a light with no attenuation term at all gives, <c>0x180080ab0</c>.</summary>
    private const float Unattenuated = 8388608f;

    /// <summary>The cornea's radius over the eye's, <c>0x180080a98</c>, in double.</summary>
    private const double CorneaScale = 0.6666666666666666;

    /// <summary>The NDC of texel 0's edge in the pass, <c>0x180080a90</c>: <c>1 − 1/64</c>.</summary>
    private const float NdcOffset = 0.984375f;

    /// <summary>How far that puts the quad's edges inside the texel grid, in texels: <c>(1 − 0.984375) · 32 / 2</c> = ¼.</summary>
    private const float EdgeShift = (1f - NdcOffset) * Size / 2f;

    /// <summary><c>0x18001dbe0</c>: <c>$glintu</c> and <c>$glintv</c>, the eye's diameter across the target.</summary>
    /// <param name="origin">The eye's centre.</param>
    /// <param name="radius">Its radius.</param>
    /// <param name="right">The view's right.</param>
    /// <param name="up">The view's up.</param>
    /// <returns><c>(right · 0.5 / radius, 0.5 − origin · that)</c>, and the same with up.</returns>
    public static ((float X, float Y, float Z, float W) U, (float X, float Y, float Z, float W) V) Projection(
        (float X, float Y, float Z) origin, float radius, (float X, float Y, float Z) right, (float X, float Y, float Z) up)
    {
        float scale = 0.5f / radius;
        (float X, float Y, float Z) u = StudioEyes.Scale(right, scale);
        (float X, float Y, float Z) v = StudioEyes.Scale(up, scale);

        return ((u.X, u.Y, u.Z, 0.5f - StudioEyes.Dot(origin, u)), (v.X, v.Y, v.Z, 0.5f - StudioEyes.Dot(origin, v)));
    }

    /// <summary>
    /// <c>0x180021680</c>: a light's direction from a point, unnormalised, and its colour there — <c>m_Color</c> times the
    /// attenuation its <c>m_Flags &amp; 7</c> selects, one bit per nonzero term (<c>lightdesc.cpp:11-46</c>). No spot cone.
    /// </summary>
    /// <param name="light">The light.</param>
    /// <param name="point">The point.</param>
    /// <returns>The delta (<c>0x180021900</c>: <c>−direction</c> for a directional light) and the colour.</returns>
    public static ((float X, float Y, float Z) Delta, (float R, float G, float B) Colour) LightAt(
        in GlintLight light, (float X, float Y, float Z) point)
    {
        (float X, float Y, float Z) delta = light.Directional
            ? StudioEyes.Scale(light.Direction, -1f)
            : StudioEyes.Sub(light.Position, point);
        float atten = Attenuation(light, StudioEyes.Dot(delta, delta));

        return (delta, (light.Colour.R * atten, light.Colour.G * atten, light.Colour.B * atten));
    }

    /// <summary>
    /// <c>0x18001d600</c>: a glint where each light reflects off the cornea toward the view, and an iris glint where the
    /// light is outside the cornea's cap, up to <paramref name="into"/>'s length.
    /// </summary>
    /// <param name="origin">The eye's centre, <c>eyeballstate_t.org</c>.</param>
    /// <param name="forward">Its forward, <c>+0x44</c>.</param>
    /// <param name="radius">Its radius.</param>
    /// <param name="right">The view's right.</param>
    /// <param name="up">The view's up.</param>
    /// <param name="viewOrigin">The view's origin.</param>
    /// <param name="lights">The model's lights, in the render context's order.</param>
    /// <param name="into">Where the glints go; its length is the most written.</param>
    /// <returns>How many were written.</returns>
    public static int Glints(
        (float X, float Y, float Z) origin,
        (float X, float Y, float Z) forward,
        float radius,
        (float X, float Y, float Z) right,
        (float X, float Y, float Z) up,
        (float X, float Y, float Z) viewOrigin,
        ReadOnlySpan<GlintLight> lights,
        Span<EyeGlintPoint> into)
    {
        (float X, float Y, float Z) viewDelta = StudioEyes.Normalise(StudioEyes.Sub(viewOrigin, origin));

        // The iris half the eye's radius, the cornea two thirds of it; the cornea's centre is behind the eye's by the
        // difference of their caps' heights over the iris's rim.
        float iris = radius * 0.5f;
        float corneaRadius = (float)(radius * CorneaScale);
        float irisRatio = iris / radius;
        float corneaRatio = iris / corneaRadius;
        float eyeCap = MathF.Sqrt(1f - (irisRatio * irisRatio));
        float corneaCap = MathF.Sqrt(1f - (corneaRatio * corneaRatio));
        (float X, float Y, float Z) cornea = StudioEyes.Scale(forward, (eyeCap * radius) - (corneaCap * corneaRadius));
        float dx = StudioEyes.Dot(right, cornea);
        float dy = StudioEyes.Dot(up, cornea);
        (float X, float Y, float Z) corneaCentre = StudioEyes.Add(cornea, origin);
        int count = 0;

        foreach (GlintLight light in lights)
        {
            if (count >= into.Length)
            {
                break;
            }

            ((float X, float Y, float Z) delta, (float R, float G, float B) colour) = LightAt(light, corneaCentre);
            delta = StudioEyes.Normalise(delta);

            if (StudioEyes.Dot(delta, forward) <= 0f)
            {
                continue;
            }

            (float X, float Y, float Z) reflected = StudioEyes.Normalise(StudioEyes.Add(delta, viewDelta));
            into[count++] = new EyeGlintPoint(
                (StudioEyes.Dot(right, reflected) * corneaRadius) + dx,
                (StudioEyes.Dot(up, reflected) * corneaRadius) + dy,
                colour.R, colour.G, colour.B);

            if (count >= into.Length)
            {
                break;
            }

            // The iris glint, lit as the eye's centre is, where the light is not within the cornea's cap.
            ((float X, float Y, float Z) fromCentre, (float R, float G, float B) centreColour) = LightAt(light, origin);

            if (StudioEyes.Dot(StudioEyes.Normalise(fromCentre), forward) < eyeCap)
            {
                into[count++] = new EyeGlintPoint(
                    StudioEyes.Dot(right, reflected) * radius,
                    StudioEyes.Dot(up, reflected) * radius,
                    centreColour.R, centreColour.G, centreColour.B);
            }
        }

        return count;
    }

    /// <summary>
    /// <c>0x18001f510</c>'s pass, on the CPU: the target cleared black, then each glint's quad added ONE/ONE into its 8-bit
    /// texels, one quad at a time.
    /// </summary>
    /// <param name="glints">The glints, in order.</param>
    /// <param name="texels">32×32 RGBA, rows top first, written whole.</param>
    /// <remarks>
    /// The quad runs from texel <c>floor(c) − 2</c> to <c>floor(c) + 1</c> at NDC <c>2X/32 − 0.984375</c>, so its edges sit a
    /// quarter texel inside the texel grid; under D3D9's pixel centres, at whole coordinates, it covers <c>floor(c) − 1</c>
    /// to <c>floor(c) + 1</c> with an interpolated texel coordinate of <c>i − 0.25</c>. Each texel takes
    /// <c>eyeglint_ps2x.fxc</c>'s value, saturated by the target and added to what is there, and is stored as a byte before
    /// the next quad.
    /// </remarks>
    public static void Rasterise(ReadOnlySpan<EyeGlintPoint> glints, Span<byte> texels)
    {
        if (texels.Length < Size * Size * 4)
        {
            throw new ArgumentException("The glint target is 32 by 32 by 4 bytes.", nameof(texels));
        }

        texels[..(Size * Size * 4)].Clear();

        foreach (EyeGlintPoint glint in glints)
        {
            float centreX = (glint.X + 0.5f) * Size;
            float centreY = (glint.Y + 0.5f) * Size;
            int left = (int)Math.Floor(centreX);
            int top = (int)Math.Floor(centreY);

            for (int row = Math.Max(top - 1, 0); row <= Math.Min(top + 1, Size - 1); row++)
            {
                for (int column = Math.Max(left - 1, 0); column <= Math.Min(left + 1, Size - 1); column++)
                {
                    // D3D9 samples texel i at its whole coordinate, which the quad's edges a quarter in make i − ¼.
                    float intensity = Spot(column - EdgeShift - centreX, row - EdgeShift - centreY);
                    int at = ((row * Size) + column) * 4;

                    texels[at] = Add(texels[at], intensity * glint.Red);
                    texels[at + 1] = Add(texels[at + 1], intensity * glint.Green);
                    texels[at + 2] = Add(texels[at + 2], intensity * glint.Blue);
                    texels[at + 3] = byte.MaxValue;
                }
            }
        }
    }

    /// <summary>
    /// <c>0x18001e020</c>'s level of detail: whether the eye is as wide as <c>r_eyeglintlodpixels</c>, else it takes
    /// <c>vgui/black</c>.
    /// </summary>
    /// <param name="view">The view, as <c>ComputePixelWidthOfSphere</c> projects through it.</param>
    /// <param name="origin">The eye's centre.</param>
    /// <param name="radius">Its radius.</param>
    /// <returns>Whether it gets a glint.</returns>
    public static bool Drawn(ScreenFadeView view, (float X, float Y, float Z) origin, float radius) =>
        view.PixelWidthOfSphere(origin, radius) >= LodPixels;

    /// <summary>
    /// The view's right and its <see cref="ScreenFadeView"/>, from the view-projection the renderer holds: with
    /// <c>clip = world · VP</c>, columns 0, 1 and 3 are <c>right · [0][0]</c>, <c>up · [1][1]</c> and <c>forward</c>.
    /// </summary>
    /// <param name="viewProjection">Row-major, sixteen floats.</param>
    /// <param name="eye">The view's origin.</param>
    /// <param name="viewportHeight">The viewport's height in pixels.</param>
    /// <returns>The right, and the view for <see cref="Drawn"/>.</returns>
    public static ((float X, float Y, float Z) Right, ScreenFadeView Screen) View(
        ReadOnlySpan<float> viewProjection, (float X, float Y, float Z) eye, float viewportHeight)
    {
        if (viewProjection.Length < 16)
        {
            throw new ArgumentException("A view-projection matrix is sixteen floats.", nameof(viewProjection));
        }

        (float X, float Y, float Z) right = (viewProjection[0], viewProjection[4], viewProjection[8]);
        (float X, float Y, float Z) up = (viewProjection[1], viewProjection[5], viewProjection[9]);
        (float X, float Y, float Z) forward = (viewProjection[3], viewProjection[7], viewProjection[11]);
        float projectionY = MathF.Sqrt(StudioEyes.Dot(up, up));

        return (
            StudioEyes.Normalise(right),
            new ScreenFadeView(eye, StudioEyes.Normalise(forward), StudioEyes.Normalise(up), projectionY, viewportHeight));
    }

    /// <summary><c>eyeglint_ps2x.fxc:14-29</c>: five Gaussians a quarter texel apart, the centre's five times, times 4/9.</summary>
    private static float Spot(float u, float v) =>
        (Gauss(u - 0.25f, v - 0.25f) + Gauss(u + 0.25f, v - 0.25f) + (5f * Gauss(u, v)) + Gauss(u - 0.25f, v + 0.25f) +
         Gauss(u + 0.25f, v + 0.25f)) * (4f / 9f);

    /// <summary><c>GlintGaussSpotCoefficient</c>: <c>saturate( exp( −25 · |d|² ) )</c>.</summary>
    private static float Gauss(float u, float v) => Math.Clamp(MathF.Exp(-25f * ((u * u) + (v * v))), 0f, 1f);

    /// <summary>A ONE/ONE blend into an 8-bit channel: the source saturated, added, saturated, rounded to a byte.</summary>
    private static byte Add(byte stored, float source) =>
        (byte)MathF.Round(Math.Clamp((stored / 255f) + Math.Clamp(source, 0f, 1f), 0f, 1f) * 255f);

    /// <summary>The attenuation <c>0x180021680</c>'s table (<c>0x1800962d8</c>) picks by which terms are nonzero.</summary>
    private static float Attenuation(in GlintLight light, float distanceSquared)
    {
        if (light.Range != 0f && distanceSquared > light.Range * light.Range)
        {
            return 0f;
        }

        bool constant = light.Attenuation0 != 0f;
        bool linear = light.Attenuation1 != 0f;
        bool quadratic = light.Attenuation2 != 0f;

        if (!constant && !linear && !quadratic)
        {
            return Unattenuated;
        }

        float distance = MathF.Sqrt(distanceSquared);
        float denominator = constant ? light.Attenuation0 : 0f;

        if (linear)
        {
            denominator += distance * light.Attenuation1;
        }

        if (quadratic)
        {
            denominator += distanceSquared * light.Attenuation2;
        }

        // Without a constant term the table adds FLT_EPSILON in its place.
        return 1f / (constant ? denominator : denominator + Epsilon);
    }
}
