using System;
using System.Linq;

using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Fonts.Tests;

/// <summary><see cref="VguiWin32Font"/> over real GDI: what only a real face can answer.</summary>
/// <remarks>
/// The rules are pinned against a fake in `Scene.Tests`; these check the adapter makes the calls that produce them.
/// Arial is on every Windows install, CI's included.
/// </remarks>
public sealed class GdiVguiTests
{
    [Test]
    public void FamilyExists_AnInstalledFamilyAndAnInventedOne_AreTrueAndFalse()
    {
        using GdiVgui gdi = new();

        gdi.FamilyExists("Arial").ShouldBeTrue("the control: a family every Windows has");
        gdi.FamilyExists("No Such Family 7f3a").ShouldBeFalse();
    }

    [Test]
    public void GetCharRgba_Antialiased_IsWhiteInkFromTheGrayOutline()
    {
        using GdiVgui gdi = new();
        (byte[] rgba, _) = Render(gdi, VguiFont.Antialias);

        Pixels(rgba).Count(pixel => pixel.Alpha == 255).ShouldBeGreaterThan(0, "an A has solid ink at 20 tall");

        // GGO_GRAY8 always covers edges partially; ExtTextOut at this size follows Arial's gasp table and draws aliased —
        // so partial alpha is what says the gray path ran.
        Pixels(rgba).Count(pixel => pixel.Alpha is > 0 and < 255).ShouldBeGreaterThan(0);
        Pixels(rgba).Where(pixel => pixel.Alpha > 0).ShouldAllBe(pixel => pixel.Red == 255 && pixel.Green == 255 && pixel.Blue == 255);
    }

    [Test]
    public void GetCharRgba_NotAntialiased_WeighsItsBitmapIntoAlpha()
    {
        using GdiVgui gdi = new();
        (byte[] rgba, _) = Render(gdi, 0);

        Pixels(rgba).Count(pixel => pixel.Alpha > 0).ShouldBeGreaterThan(0);
        Pixels(rgba).ShouldAllBe(pixel => pixel.Alpha == (byte)(int)((pixel.Red * 0.34f) + (pixel.Green * 0.55f) + (pixel.Blue * 0.11f)));
    }

    [Test]
    public void GetCharRgba_Outlined_RingsTheInkInOpaqueBlack()
    {
        using GdiVgui gdi = new();
        (byte[] rgba, _) = Render(gdi, VguiFont.Antialias | VguiFont.Outline);

        Pixels(rgba).Count(pixel => pixel is { Red: 0, Green: 0, Blue: 0, Alpha: 255 }).ShouldBeGreaterThan(0);
    }

    /// <summary>'A' in Arial at 20 tall, in a cell as wide as its widened `b` and as tall as the font.</summary>
    private static (byte[] Rgba, int Wide) Render(GdiVgui gdi, int flags)
    {
        VguiWin32Font font = VguiWin32Font.Create(gdi, new VguiFont("Arial", 20, 400, 0, 0, flags, 1f, 1f))
            ?? throw new InvalidOperationException("Arial did not create.");

        font.GetCharAbcWidths('A');

        int wide = font.GetCharAbcWidths('A').B;
        byte[] rgba = new byte[wide * font.Height * 4];

        font.GetCharRgba('A', wide, font.Height, rgba);

        return (rgba, wide);
    }

    private static (byte Red, byte Green, byte Blue, byte Alpha)[] Pixels(byte[] rgba) =>
        [.. Enumerable.Range(0, rgba.Length / 4).Select(index => (rgba[index * 4], rgba[(index * 4) + 1], rgba[(index * 4) + 2], rgba[(index * 4) + 3]))];
}
