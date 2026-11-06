using System;

namespace Tf2DemoSalvage.Scene.Hud;

/// <summary>World positions in HUD space: `GetVectorInHudSpace` (game/client/cdll_util.cpp:552).</summary>
public static class HudSpace
{
    /// <summary>Where a world point lands on the screen, through `HudTransform`'s `FrustumTransform` (view_scene.cpp:55).</summary>
    /// <param name="worldToScreen">`engine->WorldToScreenMatrix()`, row-major with the translation in the last row.</param>
    /// <param name="point">The world position, any offset already added.</param>
    /// <param name="screenWide">`ScreenWidth()`.</param>
    /// <param name="screenTall">`ScreenHeight()`.</param>
    /// <returns>The position, and whether the view faces it; a point behind is stomped to (-640, -640) (:565-571).</returns>
    public static (bool OnScreen, int X, int Y) GetVectorInHudSpace(float[] worldToScreen, (float X, float Y, float Z) point, int screenWide, int screenTall)
    {
        ArgumentNullException.ThrowIfNull(worldToScreen);

        float[] m = worldToScreen;
        float screenX = (point.X * m[0]) + (point.Y * m[4]) + (point.Z * m[8]) + m[12];
        float screenY = (point.X * m[1]) + (point.Y * m[5]) + (point.Z * m[9]) + m[13];
        float w = (point.X * m[3]) + (point.Y * m[7]) + (point.Z * m[11]) + m[15];

        // `FrustumTransform`: "behind" when w is under 0.001.
        if (w < 0.001f)
        {
            return (false, -640, -640);
        }

        float invW = 1f / w;

        return (true, Truncate(0.5f * (1f + (screenX * invW)) * screenWide), Truncate(0.5f * (1f - (screenY * invW)) * screenTall));
    }

    /// <summary>C's float-to-int as x64 compiles it (`cvttss2si`): toward zero, and `INT_MIN` for a value out of range or NaN.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The int; .NET's own cast saturates where the instruction gives `INT_MIN`.</returns>
    public static int Truncate(float value) =>
        value is >= -2147483648f and < 2147483648f ? (int)value : int.MinValue;
}
