using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// What a map's 3D skybox actually declares, and how much of the map belongs to it.
/// </summary>
/// <remarks>
/// **B152: the 3D skybox is drawn raw.** A TF2 map keeps a miniature copy of the surrounding
/// scenery far outside the level, and the engine draws it as a separate view scaled and offset by
/// the map's <c>sky_camera</c>. This viewer draws it at its literal size and position, so the
/// miniature room is simply out there in the world.
///
/// **Before building the transform, measure what it applies to.** The mechanism rests on three
/// facts about real maps, and every one of them is a number this reports rather than an assumption:
/// that a `sky_camera` exists, that its `scale` is what `base.fgd` says, and that its AREA is a
/// small share of the map's leaves — because the sky pass draws that area and the main pass draws
/// everything else (`viewrender.cpp:4877`). An area holding most of the map would mean the area is
/// not the discriminator and the whole approach is wrong.
///
/// <code>
///   sky-camera koth_harvest_final
///   sky-camera cp_fulgur
/// </code>
/// </remarks>
public sealed class SkyCameraProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "sky-camera";

    /// <inheritdoc/>
    public string Summary => "a map's 3D skybox: sky-camera <map>";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count == 0)
        {
            output.WriteLine("sky-camera <map>");
            return;
        }

        MapLocator locator = new(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder);

        if (locator.Find(arguments[0]) is not { } path)
        {
            output.WriteLine($"No map named '{arguments[0]}'.");
            return;
        }

        byte[] file = File.ReadAllBytes(path);

        IReadOnlyList<BspEntity> entities = BspEntities.ReadFrom(file);

        // **The 2D sky, which every map has and only some maps state.** Reported first because a
        // map with no `sky_camera` returns below and would otherwise say nothing about its sky at
        // all — and `sv_skyname`'s default means "stated none" is not "has none".
        string skyName = BspEntities.SkyName(entities);

        output.WriteLine(
            $"{Path.GetFileName(path)} skyname '{skyName}'" +
            $"{(skyName == BspEntities.DefaultSkyName ? " (sv_skyname's default — the map states none)" : string.Empty)}");

        foreach (string face in BspEntities.SkyFaces(skyName))
        {
            output.WriteLine($"    {face}");
        }

        if (BspEntities.SkyCamera(entities) is not { } sky)
        {
            output.WriteLine(
                $"{Path.GetFileName(path)} declares no sky_camera, so it has no 3D skybox. " +
                $"{entities.Count} entities read as the control.");

            return;
        }

        output.WriteLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"{Path.GetFileName(path)} sky_camera at " +
                $"({sky.Origin.X:F0}, {sky.Origin.Y:F0}, {sky.Origin.Z:F0}) scale {sky.Scale:F0}, " +
                $"of {entities.Count} entities"));

        BspLeafTree tree = BspLeafTree.Read(file);

        int area = tree.AreaAt(sky.Origin.X, sky.Origin.Y, sky.Origin.Z);

        // **The census that decides whether the area is the discriminator at all.** If the sky
        // camera's area holds most of the map's leaves then it is not naming the miniature room and
        // filtering by it would delete the level instead.
        Dictionary<int, int> byArea = [];

        for (int leaf = 0; leaf < tree.LeafCount; leaf++)
        {
            int of = tree.Area(leaf);

            byArea[of] = byArea.GetValueOrDefault(of) + 1;
        }

        output.WriteLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"  it sits in area {area}, which holds {byArea.GetValueOrDefault(area)} of " +
                $"{tree.LeafCount} leaves across {byArea.Count} areas"));

        foreach ((int of, int count) in byArea)
        {
            output.WriteLine(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"    area {of,3}: {count,6} leaves{(of == area ? "   <- the sky" : string.Empty)}"));
        }
    }
}
