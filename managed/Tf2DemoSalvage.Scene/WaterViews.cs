using System;
using System.Collections.Generic;
using System.Globalization;
using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Scene;

/// <summary><c>DrawFlags_t</c>, <c>game/client/iviewrender.h:21</c> — the subset a world view uses.</summary>
[Flags]
public enum ViewDraws
{
    /// <summary>No flag.</summary>
    None = 0,

    /// <summary><c>DF_RENDER_REFRACTION</c>.</summary>
    RenderRefraction = 0x1,

    /// <summary><c>DF_RENDER_REFLECTION</c>.</summary>
    RenderReflection = 0x2,

    /// <summary><c>DF_CLIP_Z</c>.</summary>
    ClipZ = 0x4,

    /// <summary><c>DF_CLIP_BELOW</c>.</summary>
    ClipBelow = 0x8,

    /// <summary><c>DF_RENDER_UNDERWATER</c>.</summary>
    RenderUnderWater = 0x10,

    /// <summary><c>DF_RENDER_ABOVEWATER</c>.</summary>
    RenderAboveWater = 0x20,

    /// <summary><c>DF_RENDER_WATER</c>.</summary>
    RenderWater = 0x40,

    /// <summary><c>DF_DRAWSKYBOX</c>.</summary>
    DrawSkybox = 0x800,

    /// <summary><c>DF_FUDGE_UP</c>.</summary>
    FudgeUp = 0x1000,

    /// <summary><c>DF_DRAW_ENTITITES</c> (Valve's spelling has three Ts).</summary>
    DrawEntities = 0x2000,

    /// <summary><c>DF_CLIP_SKYBOX</c>.</summary>
    ClipSkybox = 0x40000,
}

/// <summary><c>ClearFlags_t</c>, <c>public/view_shared.h:22</c>.</summary>
[Flags]
public enum ViewClears
{
    /// <summary>Nothing.</summary>
    None = 0,

    /// <summary><c>VIEW_CLEAR_COLOR</c>.</summary>
    Color = 0x1,

    /// <summary><c>VIEW_CLEAR_DEPTH</c>.</summary>
    Depth = 0x2,
}

/// <summary>The water views the engine composes a frame from (<c>viewrender.cpp:511-651</c>).</summary>
public enum WaterViewKind
{
    /// <summary><c>CSimpleWorldView</c> — no water, or only cheap water.</summary>
    Simple,

    /// <summary><c>CAboveWaterView::CReflectionView</c>.</summary>
    Reflection,

    /// <summary><c>CAboveWaterView::CRefractionView</c>.</summary>
    Refraction,

    /// <summary><c>CAboveWaterView</c> itself.</summary>
    Main,

    /// <summary><c>CAboveWaterView::CIntersectionView</c> — the water side of a near plane that crosses it.</summary>
    Intersection,

    /// <summary><c>CUnderWaterView::CRefractionView</c>.</summary>
    UnderRefraction,

    /// <summary><c>CUnderWaterView</c> itself.</summary>
    UnderMain,
}

/// <summary>Where a view draws.</summary>
public enum WaterViewTarget
{
    /// <summary>The frame.</summary>
    BackBuffer,

    /// <summary><c>_rt_WaterReflection</c>.</summary>
    Reflection,

    /// <summary><c>_rt_WaterRefraction</c>.</summary>
    Refraction,

    /// <summary>The frame, then copied into <c>_rt_WaterRefraction</c> (<c>viewrender.cpp:6203-6248</c>).</summary>
    BackBufferCopiedToRefraction,
}

/// <summary>Which fog a view draws under.</summary>
public enum WaterViewFog
{
    /// <summary><c>EnableWorldFog</c> — the map's <c>env_fog_controller</c>.</summary>
    World,

    /// <summary><c>SetFogVolumeState( fogInfo, false )</c> — the water's own fog, from the eye.</summary>
    Volume,

    /// <summary><c>SetFogVolumeState( fogInfo, true )</c> — the water's fog measured from its surface.</summary>
    VolumeHeight,
}

/// <summary><c>MaterialHeightClipMode_t</c>.</summary>
public enum HeightClipMode
{
    /// <summary><c>MATERIAL_HEIGHTCLIPMODE_DISABLE</c>.</summary>
    Disable,

    /// <summary><c>MATERIAL_HEIGHTCLIPMODE_RENDER_ABOVE_HEIGHT</c>.</summary>
    RenderAbove,

    /// <summary><c>MATERIAL_HEIGHTCLIPMODE_RENDER_BELOW_HEIGHT</c>.</summary>
    RenderBelow,
}

/// <summary>A view's height clip: what <c>CBaseWorldView::PushView</c> hands the material system.</summary>
/// <param name="Mode">Which side survives.</param>
/// <param name="Z">The clip height.</param>
public readonly record struct HeightClip(HeightClipMode Mode, float Z)
{
    /// <summary>No clip.</summary>
    public static HeightClip None => default;
}

/// <summary>The water cvars <c>DetermineWaterRenderInfo</c> and <c>PushView</c> read.</summary>
/// <param name="MatDrawWater"><c>mat_drawwater</c>, default 1.</param>
/// <param name="ForceExpensive"><c>r_waterforceexpensive</c>, default 0.</param>
/// <param name="ForceReflectEntities"><c>r_waterforcereflectentities</c>, default 0.</param>
/// <param name="DrawRefraction"><c>r_WaterDrawRefraction</c>, default 1.</param>
/// <param name="DrawReflection"><c>r_WaterDrawReflection</c>, default 1.</param>
/// <param name="MatClipZ"><c>mat_clipz</c>, default 1.</param>
public readonly record struct WaterConVars(
    bool MatDrawWater,
    bool ForceExpensive,
    bool ForceReflectEntities,
    bool DrawRefraction,
    bool DrawReflection,
    bool MatClipZ)
{
    /// <summary>The engine's defaults, <c>viewrender.cpp:151-160</c>.</summary>
    public static WaterConVars Defaults => new(true, false, false, true, true, true);
}

/// <summary>A <c>Water</c> material's parameters, as the view reads them after the shader initialised them.</summary>
/// <param name="IsTranslucent"><c>IMaterial::IsTranslucent</c>.</param>
/// <param name="ForceCheap"><c>$forcecheap</c> is defined and non-zero.</param>
/// <param name="ForceExpensive"><c>$forceexpensive</c> after <c>water.cpp</c>'s defaulting.</param>
/// <param name="ReflectTexture"><c>$reflecttexture</c> is a texture.</param>
/// <param name="RefractTexture"><c>$refracttexture</c> is a texture.</param>
/// <param name="ReflectEntities"><c>$reflectentities</c> is non-zero.</param>
public sealed record WaterMaterialParameters(
    bool IsTranslucent,
    bool ForceCheap,
    bool ForceExpensive,
    bool ReflectTexture,
    bool RefractTexture,
    bool ReflectEntities)
{
    /// <summary>Reads a material the way the view sees it once <c>water.cpp</c>'s <c>SHADER_INIT_PARAMS</c> ran.</summary>
    /// <param name="material">The water material.</param>
    /// <returns>Its parameters.</returns>
    /// <remarks>
    /// **An undeclared <c>$forceexpensive</c> is 1 on the PC** — <c>water.cpp</c>: <i>"By default, we're force
    /// expensive on dx9. NO WE DON'T!!!!"</i>, then <c>SetIntValue( 1 )</c> off the X360 regardless — and a material
    /// declaring both forces loses the expensive one. So by the time <c>DetermineWaterRenderInfo</c> asks
    /// <c>IsDefined()</c>, it always is.
    /// </remarks>
    public static WaterMaterialParameters From(VmtMaterial material)
    {
        ArgumentNullException.ThrowIfNull(material);

        static bool NonZero(string? text) =>
            text is not null &&
            float.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float value) &&
            (int)value != 0;

        bool cheap = NonZero(material.Value("$forcecheap"));
        bool expensive = material.Value("$forceexpensive") is null || NonZero(material.Value("$forceexpensive"));

        return new(
            material.IsTranslucent,
            cheap,
            expensive && !cheap,
            material.Value("$reflecttexture") is not null,
            material.Value("$refracttexture") is not null,
            NonZero(material.Value("$reflectentities")));
    }
}

/// <summary><c>WaterRenderInfo_t</c>: what the visible water costs this frame.</summary>
/// <param name="CheapWater"><c>m_bCheapWater</c>.</param>
/// <param name="Refract"><c>m_bRefract</c>.</param>
/// <param name="Reflect"><c>m_bReflect</c>.</param>
/// <param name="ReflectEntities"><c>m_bReflectEntities</c>.</param>
/// <param name="DrawWaterSurface"><c>m_bDrawWaterSurface</c>.</param>
/// <param name="OpaqueWater"><c>m_bOpaqueWater</c>.</param>
public readonly record struct WaterRenderInfo(
    bool CheapWater,
    bool Refract,
    bool Reflect,
    bool ReflectEntities,
    bool DrawWaterSurface,
    bool OpaqueWater)
{
    /// <summary><c>CViewRender::DetermineWaterRenderInfo</c>, <c>viewrender.cpp:2488</c>, PC branches.</summary>
    /// <param name="fogVolumeMaterial">The visible fog volume's material, null when no fog volume is visible.</param>
    /// <param name="distanceToWater"><c>m_flDistanceToWater</c>.</param>
    /// <param name="cheapWaterEndDistance"><c>m_flCheapWaterEndDistance</c>: 0.1 until a <c>water_lod_control</c>
    /// sets it (<c>viewrender.cpp:938</c>, <c>C_WaterLODControl.cpp:50</c>).</param>
    /// <param name="cvars">The cvars.</param>
    /// <returns>The info.</returns>
    /// <remarks>The DX level test at <c>:2544</c> is always passed: this renderer is DX9 class.</remarks>
    public static WaterRenderInfo Determine(
        WaterMaterialParameters? fogVolumeMaterial, float distanceToWater, float cheapWaterEndDistance, WaterConVars cvars)
    {
        WaterRenderInfo info = new(true, false, false, false, false, true);

        if (fogVolumeMaterial is not { } water)
        {
            return info;
        }

        if (!cvars.MatDrawWater)
        {
            return info with { OpaqueWater = false };
        }

        info = info with { DrawWaterSurface = true, OpaqueWater = !water.IsTranslucent };

        bool forceExpensive = cvars.ForceExpensive;

        if (water.ForceCheap)
        {
            forceExpensive = false;
        }
        else
        {
            forceExpensive = forceExpensive || water.ForceExpensive;
        }

        // "Unless expensive water is active, reflections are off."
        bool localReflection = forceExpensive && cvars.DrawReflection && water.ReflectTexture;

        // "Gary says: I'm reverting this change so that water LOD works on dx9 for ep2." — but not past a local
        // reflection, which is drawn at any distance.
        if ((distanceToWater >= cheapWaterEndDistance && !localReflection) || water.ForceCheap)
        {
            return info;
        }

        bool refract = cvars.DrawRefraction && water.RefractTexture;

        if (refract)
        {
            // "Refractive water can be seen through"
            info = info with { OpaqueWater = false };
        }

        bool reflectEntities = localReflection && (cvars.ForceReflectEntities || water.ReflectEntities);

        return info with
        {
            Refract = refract,
            Reflect = localReflection,
            ReflectEntities = reflectEntities,
            CheapWater = !localReflection && !refract,
        };
    }
}

/// <summary>What the frame knows about the visible water: the engine's <c>VisibleFogVolumeInfo_t</c>, as used here.</summary>
/// <param name="EyeInFogVolume"><c>m_bEyeInFogVolume</c>.</param>
/// <param name="DrawSkybox">The 2D skybox is drawn this frame (no 3D sky took its place).</param>
/// <param name="WaterHeight"><c>m_flWaterHeight</c>.</param>
/// <param name="ViewIntersectsWater"><c>DoesViewPlaneIntersectWater</c> — the near plane crosses the volume.</param>
public readonly record struct WaterFrame(bool EyeInFogVolume, bool DrawSkybox, float WaterHeight, bool ViewIntersectsWater);

/// <summary>One view of a frame.</summary>
/// <param name="Kind">Which of the engine's views.</param>
/// <param name="Draw">Its draw flags.</param>
/// <param name="Clear">What it clears.</param>
/// <param name="Target">Where it draws.</param>
/// <param name="Fog">Which fog it draws under.</param>
/// <param name="ClearToFogColor">The clear colour is the volume's fog colour, not black.</param>
/// <param name="Clip">Its height clip.</param>
public readonly record struct WaterView(
    WaterViewKind Kind,
    ViewDraws Draw,
    ViewClears Clear,
    WaterViewTarget Target,
    WaterViewFog Fog,
    bool ClearToFogColor,
    HeightClip Clip);

/// <summary>The engine's water views, composed into a frame.</summary>
/// <remarks>
/// **The hardware-clip-plane path.** <c>UseFastClipping()</c> is <c>mat_fastclip</c>, 0 by default, so every view
/// takes the <c>!m_bSoftwareUserClipPlane</c> branches and <c>CalcWaterEyeAdjustments</c> returns the water height
/// unadjusted (<c>viewrender.cpp:5802-5807</c>); the software intersection view never runs.
/// </remarks>
public static class WaterViews
{
    /// <summary><c>CViewRender::DrawWorldAndEntities</c>, <c>viewrender.cpp:2652</c>: the views, in draw order.</summary>
    /// <param name="info">The water's cost this frame.</param>
    /// <param name="frame">The visible water.</param>
    /// <param name="mainClear">The caller's clear flags, which only the simple view takes.</param>
    /// <param name="cvars">The cvars; <see cref="WaterConVars.Defaults"/> when omitted.</param>
    /// <returns>The views. Reflective glass (<c>:2678</c>) is not one of them: TF2 ships no such material.</returns>
    public static IReadOnlyList<WaterView> Plan(
        WaterRenderInfo info, WaterFrame frame, ViewClears mainClear, WaterConVars? cvars = null)
    {
        bool clipZ = (cvars ?? WaterConVars.Defaults).MatClipZ;
        float height = frame.WaterHeight;
        List<WaterView> views = [];

        void Add(WaterViewKind kind, ViewDraws draw, ViewClears clear, WaterViewTarget target, WaterViewFog fog,
            bool fogClear, float clipHeight) =>
            views.Add(new(kind, draw, clear, target, fog, fogClear, Clip(draw, clipHeight, clipZ)));

        if (info.CheapWater)
        {
            // CSimpleWorldView::Setup, :5705.
            ViewDraws draw = ViewDraws.DrawEntities;

            if (!info.OpaqueWater || frame.ViewIntersectsWater)
            {
                draw |= ViewDraws.RenderUnderWater | ViewDraws.RenderAboveWater;
            }
            else
            {
                draw |= frame.EyeInFogVolume ? ViewDraws.RenderUnderWater : ViewDraws.RenderAboveWater;
            }

            draw |= info.DrawWaterSurface ? ViewDraws.RenderWater : ViewDraws.None;
            draw |= !frame.EyeInFogVolume && frame.DrawSkybox ? ViewDraws.DrawSkybox : ViewDraws.None;

            // :5766-5781 — from inside the volume, clear to its fog colour under its fog.
            if (frame.EyeInFogVolume)
            {
                Add(WaterViewKind.Simple, draw, mainClear | ViewClears.Color, WaterViewTarget.BackBuffer,
                    WaterViewFog.Volume, true, 0f);
            }
            else
            {
                Add(WaterViewKind.Simple, draw, mainClear, WaterViewTarget.BackBuffer, WaterViewFog.World, false, 0f);
            }

            return views;
        }

        if (!frame.EyeInFogVolume)
        {
            // CAboveWaterView::Setup, :5869-5889.
            ViewDraws main = ViewDraws.RenderAboveWater | ViewDraws.DrawEntities;
            ViewClears mainClears = ViewClears.Depth;

            main |= frame.DrawSkybox ? ViewDraws.DrawSkybox : ViewDraws.None;
            main |= info.DrawWaterSurface ? ViewDraws.RenderWater : ViewDraws.None;
            main |= !info.Refract && !info.OpaqueWater ? ViewDraws.RenderUnderWater : ViewDraws.None;

            // CAboveWaterView::Draw, :5908.
            if (info.Reflect)
            {
                // CReflectionView::Setup, :5974-5988.
                ViewDraws reflection = ViewDraws.RenderReflection | ViewDraws.ClipZ | ViewDraws.ClipBelow |
                    ViewDraws.RenderAboveWater | ViewDraws.DrawSkybox;

                reflection |= info.ReflectEntities ? ViewDraws.DrawEntities : ViewDraws.None;

                Add(WaterViewKind.Reflection, reflection, ViewClears.Depth, WaterViewTarget.Reflection,
                    WaterViewFog.World, false, height);
            }

            bool intersects = false;

            if (info.Refract)
            {
                // CRefractionView::Setup, :6037-6041; Draw, :6060-6061.
                Add(WaterViewKind.Refraction,
                    ViewDraws.RenderRefraction | ViewDraws.ClipZ | ViewDraws.RenderUnderWater |
                    ViewDraws.FudgeUp | ViewDraws.DrawEntities,
                    ViewClears.Color | ViewClears.Depth, WaterViewTarget.Refraction, WaterViewFog.VolumeHeight,
                    true, height);

                intersects = frame.ViewIntersectsWater;
            }
            else if ((main & ViewDraws.DrawSkybox) == 0)
            {
                mainClears |= ViewClears.Color;
            }

            // :5937-5944 — hardware user clip planes, so the intersecting view clips its own world.
            if (intersects)
            {
                main |= ViewDraws.ClipZ | ViewDraws.ClipBelow;
            }

            Add(WaterViewKind.Main, main, mainClears, WaterViewTarget.BackBuffer, WaterViewFog.World, false, height);

            if (intersects)
            {
                // CIntersectionView, :6085 and :6094-6098.
                Add(WaterViewKind.Intersection,
                    ViewDraws.RenderUnderWater | ViewDraws.ClipZ | ViewDraws.DrawEntities,
                    ViewClears.None, WaterViewTarget.BackBuffer, WaterViewFog.VolumeHeight, true, height);
            }

            return views;
        }

        // CUnderWaterView::Setup, :6133-6147. "We're not drawing the 2d skybox under water since it's assumed to not be visible."
        ViewDraws under = ViewDraws.FudgeUp | ViewDraws.RenderUnderWater | ViewDraws.DrawEntities |
            ViewDraws.ClipZ;

        under |= info.DrawWaterSurface ? ViewDraws.RenderWater : ViewDraws.None;
        under |= !info.Refract && !info.OpaqueWater ? ViewDraws.RenderAboveWater : ViewDraws.None;

        if (info.Refract)
        {
            // CUnderWaterView::CRefractionView, :6206-6215 and :6225-6248: cleared to the volume's fog colour, drawn
            // under the world's fog, copied out to the refraction texture.
            ViewDraws refraction = ViewDraws.ClipZ | ViewDraws.ClipBelow | ViewDraws.RenderAboveWater |
                ViewDraws.DrawEntities;
            ViewClears clear = ViewClears.Depth;

            if (frame.DrawSkybox)
            {
                clear |= ViewClears.Color;
                refraction |= ViewDraws.DrawSkybox | ViewDraws.ClipSkybox;
            }

            Add(WaterViewKind.UnderRefraction, refraction, clear, WaterViewTarget.BackBufferCopiedToRefraction,
                WaterViewFog.World, true, height);
        }

        // CUnderWaterView::Draw, :6173-6183.
        Add(WaterViewKind.UnderMain, under, ViewClears.Depth, WaterViewTarget.BackBuffer, WaterViewFog.Volume,
            !info.Refract, height);

        return views;
    }

    /// <summary>The material the visible fog volume is drawn with: <c>m_pFogVolumeMaterial</c>.</summary>
    /// <param name="fog">The visible volume.</param>
    /// <param name="volumes">The map's water volumes.</param>
    /// <param name="waters">The map's water materials, by material index.</param>
    /// <param name="names">The map's material names, by the same index.</param>
    /// <returns>The material index, or −1 for none.</returns>
    /// <remarks>
    /// The surface's texinfo material; from inside the volume, that material's <c>$bottommaterial</c>
    /// (engine.dll <c>0x1800dffd0</c> with its second argument set, from <c>0x1800e01bc</c>). The engine finds the
    /// bottom material by name through the material system; here it is found by name in the map's table, so a
    /// bottom material no face uses is not found and the surface's is kept — a divergence, not the engine's.
    /// </remarks>
    public static int FogVolumeMaterial(
        FogVolumeInfo fog, IReadOnlyList<Content.Bsp.BspWaterVolume> volumes, IReadOnlyList<MapWater?> waters,
        IReadOnlyList<string> names)
    {
        ArgumentNullException.ThrowIfNull(volumes);
        ArgumentNullException.ThrowIfNull(waters);
        ArgumentNullException.ThrowIfNull(names);

        if (fog.Volume < 0 || fog.Volume >= volumes.Count)
        {
            return -1;
        }

        int surface = volumes[fog.Volume].SurfaceTexdata;

        if (!fog.EyeInFogVolume || surface < 0 || surface >= waters.Count || waters[surface]?.BottomMaterial is not { } bottom)
        {
            return surface;
        }

        string wanted = bottom.EndsWith(".vmt", StringComparison.OrdinalIgnoreCase) ? bottom[..^4] : bottom;

        for (int index = 0; index < names.Count; index++)
        {
            if (string.Equals(names[index].Replace('\\', '/'), wanted.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return surface;
    }

    /// <summary><c>CBaseWorldView::PushView</c>'s height clip, <c>viewrender.cpp:5297-5318</c>.</summary>
    private static HeightClip Clip(ViewDraws draw, float waterHeight, bool matClipZ)
    {
        if ((draw & ViewDraws.ClipZ) == 0 || !matClipZ)
        {
            return HeightClip.None;
        }

        // "float spread = 2.0f;" — up for DF_FUDGE_UP, down otherwise.
        float z = (draw & ViewDraws.FudgeUp) != 0 ? waterHeight + 2f : waterHeight - 2f;

        return new(
            (draw & ViewDraws.ClipBelow) != 0 ? HeightClipMode.RenderAbove : HeightClipMode.RenderBelow, z);
    }

    /// <summary>
    /// Whether a view draws a world sort group: <c>BuildEngineDrawWorldListFlags</c>, <c>viewrender.cpp:696</c>, and
    /// the engine's flag-per-group table (<c>DRAWWORLDLISTS_DRAW_*</c> = <c>1 &lt;&lt; MAT_SORT_GROUP_*</c>,
    /// <c>ivrenderview.h:36-58</c>).
    /// </summary>
    /// <param name="draw">The view's draw flags.</param>
    /// <param name="group">The sort group, <see cref="VisibleWorld.SortGroup"/>.</param>
    /// <returns>True to draw it.</returns>
    public static bool DrawsSortGroup(ViewDraws draw, int group) => group switch
    {
        0 => (draw & ViewDraws.RenderAboveWater) != 0,
        1 => (draw & ViewDraws.RenderUnderWater) != 0,
        2 => (draw & (ViewDraws.RenderAboveWater | ViewDraws.RenderUnderWater)) != 0,
        3 => (draw & ViewDraws.RenderWater) != 0,
        _ => false,
    };

    /// <summary><c>CBaseWorldView::AdjustView</c>'s reflection, <c>viewrender.cpp:5282-5284</c>.</summary>
    /// <param name="origin">The eye.</param>
    /// <param name="angles">Pitch, yaw, roll in degrees.</param>
    /// <param name="waterHeight">The water's height.</param>
    /// <returns>The mirrored eye.</returns>
    public static ((float X, float Y, float Z) Origin, (float Pitch, float Yaw, float Roll) Angles) Reflect(
        (float X, float Y, float Z) origin, (float Pitch, float Yaw, float Roll) angles, float waterHeight) =>
        ((origin.X, origin.Y, origin.Z - (2f * (origin.Z - waterHeight))), (-angles.Pitch, angles.Yaw, -angles.Roll));
}
