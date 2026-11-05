using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>What the timeline holds of the objective resource at a tick (B709), through <see cref="DemoTimeline.ObjectivesAt"/>.</summary>
public sealed class ObjectivesProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "objectives";

    /// <inheritdoc/>
    public string Summary => "control points at a tick (B709): objectives <demo> <tick>";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments is ["material", { } material])
        {
            Scene.MapLocator maps = new(Presentation.MapProvider.SteamLibraryFile, Presentation.MapProvider.OwnMapsFolder);
            Content.Assets.GameArchives archives = Content.Assets.GameArchives.Open(maps.FindGameFolder()!);
            Scene.MapTexture? found = Scene.MapAssets.ResolveVguiMaterial(
                Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, material, Content.Assets.PakFile.Read(ReadOnlyMemory<byte>.Empty), archives, 4096);

            if (found is not { } texture)
            {
                output.WriteLine("unresolved");
                return;
            }

            byte[] pixels = texture.Image.ToRgba(texture.Width, texture.Height);
            int middle = ((texture.Height / 2 * texture.Width) + (texture.Width / 2)) * 4;

            output.WriteLine($"{texture.Width}x{texture.Height} {texture.Image.Format} translucent {texture.IsTranslucent} modulation {texture.Modulation} vgui {texture.Vgui} centre {pixels[middle]},{pixels[middle + 1]},{pixels[middle + 2]},{pixels[middle + 3]}");

            for (int level = 0; level < texture.Image.Levels.Count; level++)
            {
                int wide = Math.Max(1, texture.Width >> level);
                int tall = Math.Max(1, texture.Height >> level);
                byte[] mip = Content.Assets.VtfTexture.Expand(texture.Image.Levels[level].Span, texture.Image.Format, wide, tall);
                int centre = ((tall / 2 * wide) + (wide / 2)) * 4;

                output.WriteLine($"level {level} {wide}x{tall} centre {mip[centre]},{mip[centre + 1]},{mip[centre + 2]},{mip[centre + 3]}");
            }

            return;
        }

        if (arguments.Count < 2)
        {
            output.WriteLine("usage: objectives <demo> <tick>");
            return;
        }

        SceneObjectives? objectives = DemoTimeline.Build(File.ReadAllBytes(arguments[0])).ObjectivesAt(int.Parse(arguments[1], CultureInfo.InvariantCulture));

        if (objectives is null)
        {
            output.WriteLine("no objective resource");
            return;
        }

        output.WriteLine($"{objectives.Points.Count} points, layout '{objectives.CapLayout}', names {string.Join(", ", System.Linq.Enumerable.Select(objectives.IconNames, name => name.Key + "=" + name.Value))}");

        foreach (SceneControlPoint point in objectives.Points)
        {
            output.WriteLine(point.ToString());
        }
    }
}
