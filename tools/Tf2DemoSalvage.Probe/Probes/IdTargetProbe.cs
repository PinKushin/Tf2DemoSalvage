using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;
using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// The ticks of a point-of-view demo at which the recorder is looking at a player, so a frame with a target ID's name on
/// it can be chosen from the data rather than hunted for.
/// </summary>
/// <remarks>
/// **Runs the viewer's own `IdTargets`**, from the eye the viewer builds (`FreeCamera.AtEye`) and over hulls: nobody is posed here, so the trace
/// answers with each player's collision box, as the viewer's does for a player no pass has posed. A tick this names is
/// a candidate; the frame confirms it.
///
/// <code>
///   id-target tf2-2026-pub-pov-clean koth_harvest_final 6000 9000 33
/// </code>
/// </remarks>
public sealed class IdTargetProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "id-target";

    /// <inheritdoc/>
    public string Summary =>
        "ticks at which a POV demo's recorder looks at a player, by the viewer's own target ID over hulls: " +
        "id-target <demo> <map> [from] [to] [step]";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count < 2)
        {
            output.WriteLine("Usage: id-target <demo> <map> [from] [to] [step]");
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

        if (timeline.RecorderEntityIndex is not { } recorder || !timeline.HasRecordedView)
        {
            output.WriteLine("Not a point-of-view demo: no recorder, or no recorded view.");
            return;
        }

        GameContent game = GameContent.Open(folder, NullLoggerFactory.Instance);
        LoadedMap map = LoadedMap.Read(File.ReadAllBytes(mapPath), game, timeline, 256, NullLoggerFactory.Instance, mapPath);
        IdTargets targets = new(new BulletStrikes(new EntityModelSet(), NullLogger.Instance), NullLogger.Instance);

        int from = arguments.Count > 2 ? int.Parse(arguments[2], CultureInfo.InvariantCulture) : timeline.FirstTick;
        int to = arguments.Count > 3 ? int.Parse(arguments[3], CultureInfo.InvariantCulture) : timeline.LastTick;
        int step = arguments.Count > 4 ? int.Parse(arguments[4], CultureInfo.InvariantCulture) : 33;
        int looked = 0;
        int asked = 0;

        for (int tick = from; tick <= to; tick += step)
        {
            IReadOnlyList<ScenePlayer> players = timeline.PlayersAt(tick);

            if (players.FirstOrDefault(player => player.EntityIndex == recorder) is not { EntityIndex: > 0 } self ||
                (self.LifeState ?? 0) != 0 ||
                timeline.RecordedViewAt(tick) is not { } view)
            {
                continue;
            }

            asked++;

            // The viewer's own eye: the recorded origin is the feet, and `AtEye` adds the class's eye height.
            FreeCamera eye = FreeCamera.AtEye(view, self.PlayerClass ?? 0, ((self.Flags ?? 0) & 2) != 0, 16f / 9f);
            HudState state = new(InGame: true, HasLocalPlayer: true, HideHud: 0, Health: self.Health ?? 1, Alive: true)
            {
                LocalIndex = recorder,
                Team = self.Team ?? 0,
            };

            if (targets.With(state, tick, new IdTargetFrame(timeline, map, game, null, () => eye)).IdTarget is not { } seen ||
                players.FirstOrDefault(player => player.EntityIndex == seen) is not { EntityIndex: > 0 } other)
            {
                continue;
            }

            looked++;

            // How far the view's ray passes from the middle of his hull: a hull is wider than a body, so only a small
            // miss puts the crosshair on the model the viewer's posed hitboxes answer with.
            (float fx, float fy, float fz) = AngleVectors.Forward(eye.Angles.Pitch, eye.Angles.Yaw);
            System.Numerics.Vector3 forward = new(fx, fy, fz);
            System.Numerics.Vector3 toMiddle = new(other.X - eye.Origin.X, other.Y - eye.Origin.Y, other.Z + 41f - eye.Origin.Z);
            float along = System.Numerics.Vector3.Dot(toMiddle, forward);

            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"  tick {tick}: entity {seen}, team {other.Team} (recorder's {self.Team}), " +
                $"{along:0} units along the view, the ray {(toMiddle - (forward * along)).Length():0} from his middle"));
        }

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{Path.GetFileName(path)}: the recorder (entity {recorder}) looks at a player at {looked} of {asked} ticks asked, {from}-{to} every {step}"));
    }
}
