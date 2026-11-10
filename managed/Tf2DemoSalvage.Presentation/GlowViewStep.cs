using System.Collections.Generic;

using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Presentation;

/// <summary>Which kind of sub-view a <see cref="GlowViewStep"/> is.</summary>
internal enum GlowViewKind
{
    /// <summary>A water view, by its place in <see cref="RenderableViews.Water"/>: a monitor's or the main view's.</summary>
    Water,

    /// <summary>A monitor's world view, by its place in <see cref="RenderableViews.Monitors"/>.</summary>
    Monitor,

    /// <summary>The 3D skybox view.</summary>
    Sky,
}

/// <summary>
/// One sub-view of the frame as its glows are asked and issued (B716): every view reads its glows while it draws and issues
/// its proxies at the end of its <c>DrawExecute</c> (`viewrender.cpp:5556-5560`), before the next view draws. Views sharing a
/// view id share one query per glow (`c_pixel_visibility.cpp:634-655`), so the order is the frame's draw order.
/// </summary>
/// <param name="Kind">The kind of view.</param>
/// <param name="Index">Its place among its kind; zero for the sky.</param>
internal readonly record struct GlowViewStep(GlowViewKind Kind, int Index)
{
    /// <summary>
    /// The frame's sub-views in draw order (`RenderView`, `viewrender.cpp:2074-2081`): each monitor's water views then its
    /// world view, in <c>DrawMonitors</c>' walk; the 3D sky; the main view's water views. The main view is last and not a step.
    /// </summary>
    /// <param name="views">The frame's views.</param>
    /// <param name="into">Filled with the steps, cleared first.</param>
    public static void Of(in RenderableViews views, List<GlowViewStep> into)
    {
        into.Clear();

        int water = 0;
        IReadOnlyList<MonitorRenderableView> monitors = views.Monitors ?? [];

        for (int at = 0; at < monitors.Count; at++)
        {
            for (int end = water + monitors[at].WaterViews; water < end; water++)
            {
                into.Add(new GlowViewStep(GlowViewKind.Water, water));
            }

            into.Add(new GlowViewStep(GlowViewKind.Monitor, at));
        }

        if (views.SkyCamera is not null)
        {
            into.Add(new GlowViewStep(GlowViewKind.Sky, 0));
        }

        for (int count = views.Water?.Count ?? 0; water < count; water++)
        {
            into.Add(new GlowViewStep(GlowViewKind.Water, water));
        }
    }
}
