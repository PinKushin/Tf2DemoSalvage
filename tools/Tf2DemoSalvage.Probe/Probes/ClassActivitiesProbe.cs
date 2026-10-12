using System;
using System.Collections.Generic;
using System.IO;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>Whether a class model declares the activities named, and what its script says of the jump (B523).</summary>
/// <remarks>
/// **Through `PlayerClassModels`, which is what the decode asks** — `DeclaresActivity` is the same call
/// `ClassAnimation.HasCrouchWalk` makes, and `ScriptOf` the same script the timeline's `HandleJumping` reads. The
/// question it was written for: the decode said a soldier's script sets `DontDoNewJump`, so `HandleJumping` leaves
/// `ACT_MP_JUMP` (`tf_playeranimstate.cpp:1528`) — does `soldier.mdl` have a sequence for what the weapon table makes
/// of it? It does not, which is what exposed the reading as wrong: the key is commented out in the shipped script.
///
/// <code>
///   class-activities 3 ACT_MP_JUMP_PRIMARY ACT_MP_JUMP_START_PRIMARY ACT_MP_RUN_PRIMARY
/// </code>
///
/// **The control is in the call**: name one activity the model must have (the run) beside the one in doubt.
/// </remarks>
public sealed class ClassActivitiesProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "class-activities";

    /// <inheritdoc/>
    public string Summary =>
        "whether a class model declares activities, and its script's jump flags: class-activities <class 1-9> <ACT_...>...";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count < 1 || !int.TryParse(arguments[0], out int playerClass))
        {
            output.WriteLine(Summary);
            return;
        }

        if (new MapLocator(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder)
                .FindGameFolder() is not { } game)
        {
            output.WriteLine("No TF2 install found, so there is nothing to read.");
            return;
        }

        PlayerClassModels classes = PlayerClassModels.Read(GameArchives.Open(game).Read);
        ClassAnimationScript script = classes.ScriptOf(playerClass);

        output.WriteLine(
            $"class {playerClass}: model {classes.Model(playerClass) ?? "(none)"}, " +
            $"DontDoAirwalk {script.DontDoAirwalk}, DontDoNewJump {script.DontDoNewJump}");

        for (int index = 1; index < arguments.Count; index++)
        {
            output.WriteLine(
                $"  {arguments[index]}: {(classes.DeclaresActivity(playerClass, arguments[index]) ? "declared" : "NOT declared")}");
        }
    }
}
