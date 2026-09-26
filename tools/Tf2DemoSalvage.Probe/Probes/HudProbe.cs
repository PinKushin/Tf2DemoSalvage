using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;
using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>The viewer's HUD viewport built from the install: its panels, and every quad one frame of it draws.</summary>
/// <remarks>
/// Runs <see cref="VguiHud"/> itself — the production path — on a 1920 × 1080 surface, with a GDI that makes no fonts,
/// so text is absent and every fill and texture is present. Stock files by default; `custom` reads what the viewer does.
/// </remarks>
public sealed class HudProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "hud";

    /// <inheritdoc/>
    public string Summary => "the HUD viewport from the install — its panels and one frame's quads (no text), stock unless asked: hud [playing] [custom]";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        bool custom = arguments.Contains("custom");
        GameArchives all = GameArchives.Open(new MapLocator(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder).FindGameFolder());
        GameArchives archives = custom ? all : all.WithoutCustom();
        HudState state = arguments.Contains("playing") ? new HudState(true, true, 0, 60, true, 125, 185, 1f) : default;

        // `hud <demo> <tick>`: the state HudStates reads there — the production route — instead of a made-up one.
        if (arguments.Count >= 2 && arguments[0].EndsWith(".dem", StringComparison.OrdinalIgnoreCase))
        {
            ItemSchema? items = archives.Read("scripts/items/items_game.txt") is { } schema ? ItemSchema.Read(schema) : null;

            state = HudStates.For(
                Tf2DemoSalvage.Core.Scene.DemoTimeline.Build(File.ReadAllBytes(arguments[0])),
                int.Parse(arguments[1], CultureInfo.InvariantCulture),
                new TfWeaponData(archives.Read),
                items is null ? null : new AttributeHooks(items));
        }

        output.WriteLine($"state: {state}");
        VguiSurfaceHost host = new(archives.Read, archives.FullPathOnDisk, new NoFonts(), _ => (64, 64));
        VguiHud hud = new(host);

        // Two frames: a panel's scheme pass runs children first, so what a parent's `.res` sets reaches them on the next.
        host.BeginFrame(1920, 1080);
        hud.Frame(state);
        host.BeginFrame(1920, 1080);
        hud.Frame(state with { CurTime = state.CurTime + 0.1f });

        output.WriteLine($"animation sequences: {hud.Viewport.Animations.SequenceCount}, running: {hud.Viewport.Animations.ActiveAnimationCount}");
        output.WriteLine("panels (name, class, x y wide tall, visible, bg):");
        Describe(output, hud.Viewport, 1);
        output.WriteLine($"quads: {host.List.Quads.Count}");

        foreach (VguiQuad quad in host.List.Quads)
        {
            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"  {quad.X0} {quad.Y0} {quad.X1} {quad.Y1} rgb {quad.Red} {quad.Green} {quad.Blue} a {quad.AlphaTopLeft} {quad.Texture ?? "(fill)"}"));
        }
    }

    private static void Describe(TextWriter output, VguiPanel panel, int depth)
    {
        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{new string(' ', depth * 2)}{panel.Name} {panel.ClassName} {panel.X} {panel.Y} {panel.Wide} {panel.Tall} {(panel.Visible ? "shown" : "hidden")} {panel.BgColor}"));

        foreach (VguiPanel child in panel.Children)
        {
            Describe(output, child, depth + 1);
        }
    }

    /// <summary>A GDI with no fonts: every handle stays empty, so no text is drawn.</summary>
    private sealed class NoFonts : IVguiGdi
    {
        public bool AddFontResource(string path) => false;

        public bool FamilyExists(string family) => false;

        public VguiGdiFont? CreateFont(string face, int tall, int weight, bool italic, bool underline, bool strikeout, int charset, int quality) => null;

        public void CreateBitmap(VguiGdiFont font, int wide, int tall)
        {
        }

        public (int A, int B, int C)? GetCharAbcWidths(VguiGdiFont font, char character) => null;

        public int? GetTextExtent(VguiGdiFont font, char character) => null;

        public VguiGrayGlyph? GetGlyphOutlineGray8(VguiGdiFont font, int character) => null;

        public byte[] DrawGlyph(VguiGdiFont font, char character, int penX, int clearWide, int clearTall) => [];
    }
}
