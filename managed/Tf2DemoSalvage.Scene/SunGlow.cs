using System;
using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene;

/// <summary>
/// The sun's glow — <c>C_Sun</c>'s two <c>C_SunGlowOverlay</c>s drawn by <c>CGlowOverlay::Draw</c> (B378).
/// </summary>
/// <remarks>
/// *Read from published source*: `game/client/c_sun.cpp`, `c_sun.h`, `glow_overlay.cpp`, `game/server/sun.cpp`.
///
/// **Two overlays a sun, in this order**: the core (<c>m_Overlay</c>, its own size and colour) and the outer glow
/// (<c>m_GlowOverlay</c>, <c>SetModulateByDot</c>: six times the size, faded out over the tenth of a dot product either
/// side of looking straight at it). Each owns a pixel-visibility query; both ask at the far plane along the sun's
/// direction with a proxy sized in screen space (`glow_overlay.cpp:205-216`), and the answer scales the colour.
///
/// **Main view only, as the engine draws it** — see <see cref="PixelVisibility"/>'s remarks for why the reflection and
/// monitor views draw no sun glow.
///
/// **Not modeled, each named:** <c>m_skyObstructionScale</c> is 1 — the engine updates it from a SECOND query issued
/// in the 3D skybox's own view (<c>UpdateSkyOverlays</c>, `viewrender.cpp:4937`), so scenery in the 3D skybox does
/// not hide the sun here; the main view's query sees the main world only, as the engine's does. <c>R_CullSphere</c>
/// is not run: it only saves the draw of a quad the rasteriser clips anyway. <c>$hdrcolorscale</c> is carried and not
/// applied, as for every entity sprite. And the depth state is <c>light_glow02_add_noz</c>'s <c>$ignorez</c>, taken
/// for every sun: no shipped map names another material
/// (ponytail: a custom sun material without $ignorez would draw untested here; read the flag when one appears).
/// </remarks>
public sealed class SunGlow
{
    /// <summary><c>m_flProxyRadius = 0.05f; // about 1/20th of the screen</c> (`c_sun.cpp:105`, `:124`).</summary>
    public const float ProxyRadius = 0.05f;

    /// <summary><c>flOverlayDist</c> in <c>CalcBasis</c> (`glow_overlay.cpp:330`).</summary>
    public const float OverlayDistance = 100f;

    private readonly List<ParticleBatch> _batches = [];

    /// <summary>The query key of a sun's core overlay — its own <c>m_queryHandle</c>, never an entity index.</summary>
    /// <param name="entity">The sun.</param>
    /// <returns>A negative key.</returns>
    public static int CoreKey(int entity) => -1 - (2 * entity);

    /// <summary>The query key of a sun's outer glow.</summary>
    /// <param name="entity">The sun.</param>
    /// <returns>A negative key, distinct from every core's.</returns>
    public static int GlowKey(int entity) => -2 - (2 * entity);

    /// <summary><c>C_Sun::OnDataChanged</c>'s two colours (`c_sun.cpp:60-89`).</summary>
    /// <param name="render"><c>m_clrRender</c>.</param>
    /// <param name="overlay"><c>m_clrOverlay</c>.</param>
    /// <returns>The core's colour, renormalized to its largest component, and the outer glow's.</returns>
    public static (Vector3 Core, Vector3 Glow) Colours(
        (byte Red, byte Green, byte Blue) render, (byte Red, byte Green, byte Blue) overlay)
    {
        float maximum = Math.Max(render.Red, Math.Max(render.Green, render.Blue));

        // "This is an error, set to pure white".
        Vector3 core = maximum <= 0f
            ? Vector3.One
            : new Vector3(render.Red, render.Green, render.Blue) / maximum;

        Vector3 glow = overlay.Red != 0 || overlay.Green != 0 || overlay.Blue != 0
            ? new Vector3(overlay.Red, overlay.Green, overlay.Blue) / 255f
            : core;

        return (core, glow);
    }

    /// <summary><c>C_SunGlowOverlay::CalcSpriteColorAndSize</c> (`c_sun.h:166-183`).</summary>
    /// <param name="modulateByDot"><c>m_bModulateByDot</c> — the outer glow's, not the core's.</param>
    /// <param name="dot">The view's forward dotted with the direction to the sun.</param>
    /// <param name="size">The sprite's size (horizontal and vertical are the same for a sun).</param>
    /// <param name="colour">The sprite's colour.</param>
    /// <param name="obstruction"><c>m_flGlowObstructionScale</c>.</param>
    /// <returns>Its drawn sizes and colour.</returns>
    public static (float Horizontal, float Vertical, Vector3 Colour) CalcSpriteColorAndSize(
        bool modulateByDot, float dot, float size, Vector3 colour, float obstruction)
    {
        if (!modulateByDot)
        {
            return (size, size, colour * obstruction);
        }

        // `RemapVal( flDot, 1.0f, 0.9f, 0.75f, 0.0f )`, clamped to [0, 0.75].
        float alpha = Math.Clamp(0.75f + ((dot - 1f) / (0.9f - 1f) * (0f - 0.75f)), 0f, 0.75f);

        return (size * 6f, size * 6f, colour * alpha * obstruction);
    }

    /// <summary><c>CGlowOverlay::CalcBasis</c> (`glow_overlay.cpp:322-343`).</summary>
    /// <param name="eye"><c>CurrentViewOrigin()</c>.</param>
    /// <param name="toGlow">The unit direction to the sun.</param>
    /// <param name="horizontal">The drawn width.</param>
    /// <param name="vertical">The drawn height.</param>
    /// <returns>The quad's centre, a hundred units out, and its scaled right and up.</returns>
    public static (Vector3 Base, Vector3 Right, Vector3 Up) CalcBasis(
        Vector3 eye, Vector3 toGlow, float horizontal, float vertical)
    {
        Vector3 right = Normalize(Vector3.Cross(toGlow, Vector3.UnitZ));
        Vector3 up = Normalize(Vector3.Cross(right, toGlow));

        return (eye + (toGlow * OverlayDistance), right * horizontal, up * vertical);
    }

    /// <summary>What <c>UpdateGlowObstruction</c> asks for an overlay in the sky (`glow_overlay.cpp:207-215`).</summary>
    /// <param name="key">The overlay's query key.</param>
    /// <param name="eye"><c>CurrentViewOrigin()</c>.</param>
    /// <param name="direction"><c>m_vDirection</c>.</param>
    /// <param name="farZ">The view's <c>zFar</c>.</param>
    /// <param name="aspect"><c>CalcGlowAspect()</c>.</param>
    /// <returns>A screen-space query at the far plane.</returns>
    public static GlowQuery Query(int key, Vector3 eye, Vector3 direction, float farZ, float aspect) =>
        new(key, eye + (direction * (farZ * 0.999f)), ProxyRadius, aspect, SizeInScreenspace: true);

    /// <summary><c>CGlowOverlay::DrawOverlays</c> for every sun: the quads the renderer draws.</summary>
    /// <param name="suns">The suns the client holds.</param>
    /// <param name="eye">The view origin.</param>
    /// <param name="forward">The view forward.</param>
    /// <param name="farZ">The view's far plane.</param>
    /// <param name="sprites">The loaded sprite materials, by model path.</param>
    /// <param name="visible"><c>PixelVisibility_FractionVisible</c>.</param>
    /// <returns>One batch per drawn overlay; the list is reused.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public IReadOnlyList<ParticleBatch> Build(
        IReadOnlyList<SceneSun> suns,
        Vector3 eye,
        Vector3 forward,
        float farZ,
        IReadOnlyDictionary<string, EngineSprite> sprites,
        Func<GlowQuery, float> visible)
    {
        ArgumentNullException.ThrowIfNull(suns);
        ArgumentNullException.ThrowIfNull(sprites);
        ArgumentNullException.ThrowIfNull(visible);

        _batches.Clear();

        foreach (SceneSun sun in suns)
        {
            // `m_bOn` clear: `Deactivate()` on both, and `DrawOverlays` skips an inactive overlay.
            if (!sun.On)
            {
                continue;
            }

            (Vector3 core, Vector3 glow) = Colours(sun.Render, sun.Overlay);
            Vector3 toGlow = Normalize(new Vector3(sun.Direction.X, sun.Direction.Y, sun.Direction.Z));
            float dot = Vector3.Dot(toGlow, forward);

            Draw(CoreKey(sun.Entity), modulateByDot: false, sun.Size, core, sun.Material);
            Draw(GlowKey(sun.Entity), modulateByDot: true, sun.OverlaySize, glow, sun.OverlayMaterial);

            void Draw(int key, bool modulateByDot, float size, Vector3 colour, string? material)
            {
                // `CalcGlowAspect()`: horizontal over vertical, which are one size for a sun.
                float obstruction = visible(Query(key, eye, toGlow, farZ, 1f));

                // `if( m_flGlowObstructionScale == 0 ) return;`
                if (obstruction <= 0f || material is null || !sprites.TryGetValue(material, out EngineSprite sprite))
                {
                    return;
                }

                (float horizontal, float vertical, Vector3 drawn) =
                    CalcSpriteColorAndSize(modulateByDot, dot, size, colour, obstruction);

                // "If we're alpha'd out, then don't bother".
                if (drawn.LengthSquared() < 0.00001f)
                {
                    return;
                }

                (Vector3 at, Vector3 right, Vector3 up) = CalcBasis(eye, toGlow, horizontal, vertical);

                // `builder.Color4f( VectorExpand(vColor), 1 )`, through the material's own shader.
                (Vector3 shaded, float alpha) = EntitySprites.ShaderInput(sprite, RenderModes.Normal, drawn, 1f);

                List<DetailSpriteVertex> corners = new(6);

                // Unit extents: the corners are `vBasePt ± vRight ± vUp`, texture (0,0) at `- vRight + vUp`.
                EntitySprites.Corners(at, right, up, new SpriteExtents(-1f, 1f, 1f, -1f), 1f, shaded, alpha, corners);

                _batches.Add(new ParticleBatch(corners, sprite.Material with { Depth = SpriteDepth.Off }));
            }
        }

        return _batches;
    }

    /// <summary><c>VectorNormalize</c>: a zero vector stays zero.</summary>
    private static Vector3 Normalize(Vector3 vector)
    {
        float length = vector.Length();

        return length > 0f ? vector / length : vector;
    }
}
