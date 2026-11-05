using System;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Scene.Hud;

/// <summary>`vgui::CircularProgressBar` (vgui2/vgui_controls/CircularProgressBar.cpp): a texture swept as a pie from the top.</summary>
/// <param name="parent">The parent, or null.</param>
/// <param name="name">The panel's name, or null.</param>
public sealed class VguiCircularProgressBar(VguiPanel? parent, string? name) : VguiProgressBar(parent, name)
{
    private const float SegmentAngle = MathF.PI / 4f;

    // `Segments` (:191-201): each eighth's start angle, its two outer corners, and the way its edge sweeps.
    private static readonly (float MinRadians, float X1, float Y1, float X2, float Y2, int SwipeX, int SwipeY)[] Segments =
    [
        (0f, 0.5f, 0f, 1f, 0f, 1, 0),
        (MathF.PI * 0.25f, 1f, 0f, 1f, 0.5f, 0, 1),
        (MathF.PI * 0.5f, 1f, 0.5f, 1f, 1f, 0, 1),
        (MathF.PI * 0.75f, 1f, 1f, 0.5f, 1f, -1, 0),
        (MathF.PI, 0.5f, 1f, 0f, 1f, -1, 0),
        (MathF.PI * 1.25f, 0f, 1f, 0f, 0.5f, 0, -1),
        (MathF.PI * 1.5f, 0f, 0.5f, 0f, 0f, 0, -1),
        (MathF.PI * 1.75f, 0f, 0f, 0.5f, 0f, 1, 0),
    ];

    private string? _fgImage;
    private string? _bgImage;

    /// <inheritdoc/>
    public override string ClassName => "CircularProgressBar";

    /// <summary>`m_iProgressDirection`: `PROGRESS_CW` when true.</summary>
    public bool Clockwise { get; set; } = true;

    /// <summary>`m_bReverseProgress`.</summary>
    public bool ReverseProgress { get; set; }

    /// <summary>`SetFgImage`: under `vgui/`.</summary>
    /// <param name="image">The image name.</param>
    public void SetFgImage(string image) => _fgImage = "vgui/" + image;

    /// <summary>`SetBgImage`: under `vgui/`.</summary>
    /// <param name="image">The image name.</param>
    public void SetBgImage(string image) => _bgImage = "vgui/" + image;

    /// <inheritdoc/>
    /// <remarks>`ApplySettings` (:69): both images cleared, then `fg_image` and `bg_image`.</remarks>
    public override void ApplySettings(KeyValuesTree block, VguiContext context)
    {
        ArgumentNullException.ThrowIfNull(block);

        (_fgImage, _bgImage) = (null, null);

        if (block.Find("fg_image")?.Value is { Length: > 0 } fg)
        {
            SetFgImage(fg);
        }

        if (block.Find("bg_image")?.Value is { Length: > 0 } bg)
        {
            SetBgImage(bg);
        }

        base.ApplySettings(block, context);
    }

    /// <inheritdoc/>
    /// <remarks>`ApplySchemeSettings` (:95): its own colours and no border.</remarks>
    public override void ApplySchemeSettings(VguiContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        base.ApplySchemeSettings(context);
        FgColor = context.Scheme.GetColor("CircularProgressBar.FgColor", default);
        BgColor = context.Scheme.GetColor("CircularProgressBar.BgColor", default);
        Border = null;
    }

    /// <inheritdoc/>
    /// <remarks>`PaintBackground` (:147): the background image, else the foreground, whole.</remarks>
    public override void PaintBackground(IVguiSurface surface, VguiContext context)
    {
        ArgumentNullException.ThrowIfNull(surface);

        if ((_bgImage ?? _fgImage) is not { } image)
        {
            return;
        }

        surface.DrawSetTexture(image);
        surface.DrawSetColor(BgColor);
        surface.DrawTexturedRect(0, 0, Wide, Tall);
    }

    /// <inheritdoc/>
    /// <remarks>`Paint` (:163).</remarks>
    public override void Paint(IVguiSurface surface, VguiContext context)
    {
        ArgumentNullException.ThrowIfNull(surface);

        if (_fgImage is null)
        {
            return;
        }

        float end = ReverseProgress ? 1f - Progress : Progress;

        end = Clockwise ? end : 1f - end;

        surface.DrawSetTexture(_fgImage);
        surface.DrawSetColor(FgColor);

        Span<VguiVertex> triangles = stackalloc VguiVertex[8 * 3];
        int count = CircleSegment(end, Clockwise, Wide, Tall, triangles);

        for (int at = 0; at < count; at++)
        {
            surface.DrawTexturedPolygon(triangles.Slice(at * 3, 3));
        }
    }

    /// <summary>`DrawCircleSegment` (:209): the triangles, centre first, that fill to an end fraction.</summary>
    /// <param name="endProgress">The fraction of the circle.</param>
    /// <param name="clockwise">Whether it sweeps clockwise.</param>
    /// <param name="wide">The panel's width.</param>
    /// <param name="tall">The panel's height.</param>
    /// <param name="triangles">Room for eight triangles.</param>
    /// <returns>How many triangles were written.</returns>
    public static int CircleSegment(float endProgress, bool clockwise, int wide, int tall, Span<VguiVertex> triangles)
    {
        float halfWide = wide / 2f;
        float halfTall = tall / 2f;
        float endRadians = endProgress * MathF.PI * 2f;
        VguiVertex centre = new(halfWide, halfTall, 0.5f, 0.5f);
        int count = 0;

        for (int i = 0; i < 8; i++)
        {
            (float MinRadians, float X1, float Y1, float X2, float Y2, int SwipeX, int SwipeY) segment = Segments[i];
            VguiVertex edge;
            VguiVertex corner2 = new(halfWide + (wide * (segment.X2 - 0.5f)), halfTall + (tall * (segment.Y2 - 0.5f)), segment.X2, segment.Y2);
            VguiVertex corner1 = new(halfWide + (wide * (segment.X1 - 0.5f)), halfTall + (tall * (segment.Y1 - 0.5f)), segment.X1, segment.Y1);
            float inside;

            if (clockwise)
            {
                // `m_iStartSegment` is 0, so the wedge walked is segment `i`.
                if (endRadians <= segment.MinRadians)
                {
                    continue;
                }

                inside = endRadians - segment.MinRadians;
                float sweep = i % 2 == 1 ? SegmentAngle - inside : inside;

                edge = inside < SegmentAngle ? Partial(segment, sweep, i, wide, tall) : corner2;
                Write(triangles, ref count, centre, corner1, edge);
                continue;
            }

            float max = segment.MinRadians + SegmentAngle;

            if (endRadians >= max)
            {
                continue;
            }

            inside = max - endRadians;

            if (inside < SegmentAngle)
            {
                inside = SegmentAngle - inside;
                edge = Partial(segment, i % 2 == 1 ? SegmentAngle - inside : inside, i, wide, tall);
            }
            else
            {
                edge = corner1;
            }

            Write(triangles, ref count, centre, edge, corner2);
        }

        return count;
    }

    private static VguiVertex Partial((float MinRadians, float X1, float Y1, float X2, float Y2, int SwipeX, int SwipeY) segment, float angle, int i, int wide, int tall)
    {
        float halfWide = wide / 2f;
        float halfTall = tall / 2f;
        float tan = MathF.Tan(angle);
        float deltaX = i % 2 == 1 ? (halfWide - (halfTall * tan)) * segment.SwipeX : halfTall * tan * segment.SwipeX;
        float deltaY = i % 2 == 1 ? (halfTall - (halfWide * tan)) * segment.SwipeY : halfWide * tan * segment.SwipeY;

        return new VguiVertex(
            (segment.X1 * wide) + deltaX,
            (segment.Y1 * tall) + deltaY,
            segment.X1 + (deltaX / halfWide * 0.5f),
            segment.Y1 + (deltaY / halfTall * 0.5f));
    }

    private static void Write(Span<VguiVertex> triangles, ref int count, VguiVertex a, VguiVertex b, VguiVertex c)
    {
        triangles[count * 3] = a;
        triangles[(count * 3) + 1] = b;
        triangles[(count * 3) + 2] = c;
        count++;
    }
}
