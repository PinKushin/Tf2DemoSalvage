using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>One beam entity as the viewer's beam pass builds it from one camera: every corner it hands the renderer.</summary>
/// <remarks>
/// **Nothing is recomputed.** The wire values are the timeline's, the materials are the map load's, and the corners are
/// <see cref="EntityBeams.Build"/>'s own output — the colour printed is the colour the batch carries, after the
/// material's shader rule (<see cref="EntitySprites.ShaderInput"/>). The halo gate is the map's trace, both ways.
///
/// <code>
///   beam-view demostf-cp_process_f12-2026-08-08-2207 cp_process_f12 60000 676 146 -1831.6 728 -28.7 -36.5
/// </code>
///
/// The control is the shaft itself: a beam the tick holds must list at least one batch with six corners.
/// </remarks>
public sealed class BeamViewProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "beam-view";

    /// <inheritdoc/>
    public string Summary =>
        "one beam's batches as the viewer builds them from a camera: beam-view <demo> <map> <tick> <entity> <x y z pitch yaw>";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count < 9)
        {
            output.WriteLine(Summary);
            return;
        }

        MapLocator locator = new(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder);

        if (DemoCorpus.Find(arguments[0], output) is not { } path ||
            locator.FindGameFolder() is not { } folder ||
            locator.Find(arguments[1]) is not { } mapPath)
        {
            output.WriteLine("Demo, game or map not found.");
            return;
        }

        DemoTimeline timeline = DemoTimeline.Build(File.ReadAllBytes(path));
        GameContent game = GameContent.Open(folder, NullLoggerFactory.Instance);
        LoadedMap map = LoadedMap.Read(File.ReadAllBytes(mapPath), game, timeline, 256, NullLoggerFactory.Instance);

        List<SceneProp> props = [];
        timeline.PropsAt(Number(arguments[2]), props);

        int entity = int.Parse(arguments[3], CultureInfo.InvariantCulture);

        if (props.FirstOrDefault(prop => prop.EntityIndex == entity && prop.Pose.Beam is not null) is not { } prop ||
            map.Assets?.SpriteMaterials is not { } sprites)
        {
            output.WriteLine($"No beam entity {entity} at that tick, or no sprite materials.");
            return;
        }

        Vector3 eye = new(Number(arguments[4]), Number(arguments[5]), Number(arguments[6]));
        BeamView view = View(eye, Number(arguments[7]), Number(arguments[8]));
        SceneBeam beam = prop.Pose.Beam!;

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"entity {entity}: start ({prop.Pose.X} {prop.Pose.Y} {prop.Pose.Z}) end {beam.EndPosition} width {beam.Width} " +
            $"endWidth {beam.EndWidth} fadeLength {beam.FadeLength} haloScale {beam.HaloScale} flags 0x{beam.Flags:x} " +
            $"alpha {prop.Pose.RenderAlpha} colour {prop.Pose.RenderColor} mode {prop.Pose.RenderMode} fx {prop.Pose.RenderFx} " +
            $"hdrScale {beam.HdrColourScale} minDx {beam.MinDxLevel}"));

        foreach (string? material in (string?[])[prop.ModelPath, beam.HaloPath])
        {
            if (material is not null && sprites.TryGetValue(material, out EngineSprite sprite))
            {
                output.WriteLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"  material '{material}': shader {sprite.Shader} constant {sprite.ConstantColor} " +
                    $"ignoresVertexColors {sprite.IgnoresVertexColors} {sprite.Width}x{sprite.Height}"));
            }
        }

        bool Sees(Vector3 point) =>
            map.Level is not { } level ||
            (level.Trace((eye.X, eye.Y, eye.Z), (point.X, point.Y, point.Z), 0f).Fraction >= 1f &&
             level.Trace((point.X, point.Y, point.Z), (eye.X, eye.Y, eye.Z), 0f).Fraction >= 1f);

        output.WriteLine($"  halo gate (map trace, both ways): {Sees(new Vector3(prop.Pose.X, prop.Pose.Y, prop.Pose.Z))}");

        IReadOnlyList<ParticleBatch> batches = new EntityBeams().Build(
            [prop], view, sprites, one => one.Pose, (_, _, _) => null, Sees, currentTime: 0f, frameTime: 0f);

        foreach (ParticleBatch batch in batches)
        {
            output.WriteLine($"  batch blend {batch.Material.Blend} depth {batch.Material.Depth}: {batch.Corners.Count} corners");

            foreach (DetailSpriteVertex corner in batch.Corners)
            {
                output.WriteLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"    ({corner.X:0.0} {corner.Y:0.0} {corner.Z:0.0}) colour ({corner.Red:0.0000} {corner.Green:0.0000} " +
                    $"{corner.Blue:0.0000}) alpha {corner.Alpha:0.0000}"));
            }
        }

        output.WriteLine($"{batches.Count} batches");
    }

    private static float Number(string text) => float.Parse(text, CultureInfo.InvariantCulture);

    /// <summary><c>AngleVectors</c> for a pitch and a yaw, roll zero.</summary>
    private static BeamView View(Vector3 eye, float pitch, float yaw)
    {
        (float sp, float cp) = MathF.SinCos(float.DegreesToRadians(pitch));
        (float sy, float cy) = MathF.SinCos(float.DegreesToRadians(yaw));

        return new BeamView(eye, new Vector3(cp * cy, cp * sy, -sp), new Vector3(sy, -cy, 0f), new Vector3(sp * cy, sp * sy, cp));
    }
}
