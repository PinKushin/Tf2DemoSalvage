using System;

namespace Tf2DemoSalvage.Scene.Hud;

/// <summary>`CTFProgressBar` (game/client/tf/tf_time_panel.cpp:34): the round timer's dial, emptying counter-clockwise.</summary>
/// <remarks>
/// An `ImagePanel`, so its background still draws the `.res` image; `Paint` then draws the whole dial in the inactive
/// colour and, over it, the part of the circle still to run in the active colour — the warning colour once
/// `percent_warning` of the time has passed. The circle is cut into quarter and eighth wedges, each a four-cornered polygon
/// whose texture coordinates follow its corners.
/// </remarks>
public sealed class TfProgressBar : VguiImagePanel
{
    private const string Texture = "hud/objectives_timepanel_progressbar";

    /// <summary>`CTFProgressBar( parent, name )`, with its panel variables (tf_time_panel.h:38).</summary>
    /// <param name="parent">The parent, or null.</param>
    /// <param name="name">The panel's name, or null.</param>
    public TfProgressBar(VguiPanel? parent, string? name)
        : base(parent, name)
    {
        DeclareAnimationVar("color_active", VguiPanelVarType.Color, "TimerProgress.Active");
        DeclareAnimationVar("color_inactive", VguiPanelVarType.Color, "TimerProgress.InActive");
        DeclareAnimationVar("color_warning", VguiPanelVarType.Color, "TimerProgress.Active");
        DeclareAnimationVar("percent_warning", VguiPanelVarType.Real, "0.75");
    }

    /// <inheritdoc/>
    public override string ClassName => "CTFProgressBar";

    /// <summary>`m_flPercent`: how much of the time has passed, 0 to 1.</summary>
    public float Percentage { get; set; }

    /// <inheritdoc/>
    public override void Paint(IVguiSurface surface, VguiContext context)
    {
        ArgumentNullException.ThrowIfNull(surface);

        float wide = Wide;
        float tall = Tall;

        surface.DrawSetTexture(Texture);
        surface.DrawSetColor(GetColor("color_inactive"));
        Quad(surface, (0f, 0f, 0f, 0f), (wide, 0f, 1f, 0f), (wide, tall, 1f, 1f), (0f, tall, 0f, 1f));

        surface.DrawSetColor(Percentage < GetFloat("percent_warning") ? GetColor("color_active") : GetColor("color_warning"));

        const float CompleteCircle = 2f * MathF.PI;
        const float Degrees90 = CompleteCircle / 4f;
        float endAngle = CompleteCircle * (1f - Percentage);
        float halfWide = wide / 2f;
        float halfTall = tall / 2f;

        if (endAngle >= Degrees90 * 3f)
        {
            Quad(surface, (halfWide, 0f, 0.5f, 0f), (wide, 0f, 1f, 0f), (wide, tall, 1f, 1f), (halfWide, tall, 0.5f, 1f));
            Quad(surface, (0f, halfTall, 0f, 0.5f), (halfWide, halfTall, 0.5f, 0.5f), (halfWide, tall, 0.5f, 1f), (0f, tall, 0f, 1f));

            if (endAngle > Degrees90 * 3.5f)
            {
                float edge = MathF.Tan((Degrees90 * 4f) - endAngle);
                Quad(surface, (0f, 0f, 0f, 0f), (halfWide - (edge * halfTall), 0f, 0.5f - (edge * 0.5f), 0f), (halfWide, halfTall, 0.5f, 0.5f), (0f, halfTall, 0f, 0.5f));
            }
            else
            {
                float edge = MathF.Tan(endAngle - (Degrees90 * 3f));
                Quad(surface, (0f, halfTall, 0f, 0.5f), (0f, halfTall - (edge * halfWide), 0f, 0.5f - (edge * 0.5f)), (halfWide, halfTall, 0.5f, 0.5f), (0f, halfTall, 0f, 0.5f));
            }
        }
        else if (endAngle >= Degrees90 * 2f)
        {
            Quad(surface, (halfWide, 0f, 0.5f, 0f), (wide, 0f, 1f, 0f), (wide, tall, 1f, 1f), (halfWide, tall, 0.5f, 1f));

            if (endAngle > Degrees90 * 2.5f)
            {
                float edge = MathF.Tan((Degrees90 * 3f) - endAngle);
                Quad(surface, (halfWide, halfTall, 0.5f, 0.5f), (halfWide, tall, 0.5f, 1f), (0f, tall, 0f, 1f), (0f, halfTall + (edge * halfWide), 0f, 0.5f + (edge * 0.5f)));
            }
            else
            {
                float edge = MathF.Tan(endAngle - (Degrees90 * 2f));
                Quad(surface, (halfWide, halfTall, 0.5f, 0.5f), (halfWide, tall, 0.5f, 1f), (halfWide - (edge * halfTall), tall, 0.5f - (edge * 0.5f), 1f), (halfWide, halfTall, 0.5f, 0.5f));
            }
        }
        else if (endAngle >= Degrees90)
        {
            Quad(surface, (halfWide, 0f, 0.5f, 0f), (wide, 0f, 1f, 0f), (wide, halfTall, 1f, 0.5f), (halfWide, halfTall, 0.5f, 0.5f));

            if (endAngle > Degrees90 * 1.5f)
            {
                float edge = MathF.Tan((Degrees90 * 2f) - endAngle);
                Quad(surface, (halfWide, halfTall, 0.5f, 0.5f), (wide, halfTall, 1f, 0.5f), (wide, tall, 1f, 1f), (halfWide + (edge * halfTall), tall, 0.5f + (edge * 0.5f), 1f));
            }
            else
            {
                float edge = MathF.Tan(endAngle - Degrees90);
                Quad(surface, (halfWide, halfTall, 0.5f, 0.5f), (wide, halfTall, 1f, 0.5f), (wide, halfTall + (edge * halfWide), 1f, 0.5f + (edge * 0.5f)), (halfWide, halfTall, 0.5f, 0.5f));
            }
        }
        else if (endAngle > Degrees90 / 2f)
        {
            float edge = MathF.Tan(Degrees90 - endAngle);
            Quad(surface, (halfWide, 0f, 0.5f, 0f), (wide, 0f, 1f, 0f), (wide, halfTall - (edge * halfWide), 1f, 0.5f - (edge * 0.5f)), (halfWide, halfTall, 0.5f, 0.5f));
        }
        else
        {
            float edge = MathF.Tan(endAngle);
            Quad(surface, (halfWide, 0f, 0.5f, 0f), (halfWide + (edge * halfTall), 0f, 0.5f + (edge * 0.5f), 0f), (halfWide, halfTall, 0.5f, 0.5f), (halfWide, 0f, 0.5f, 0f));
        }
    }

    private static void Quad(
        IVguiSurface surface,
        (float X, float Y, float S, float T) a,
        (float X, float Y, float S, float T) b,
        (float X, float Y, float S, float T) c,
        (float X, float Y, float S, float T) d) =>
        surface.DrawTexturedPolygon([new(a.X, a.Y, a.S, a.T), new(b.X, b.Y, b.S, b.T), new(c.X, c.Y, c.S, c.T), new(d.X, d.Y, d.S, d.T)]);
}
