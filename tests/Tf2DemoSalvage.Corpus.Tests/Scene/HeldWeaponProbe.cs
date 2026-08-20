using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>Does the weapon a player holds carry a model we could draw?</summary>
/// <remarks>
/// **The viewmodel's weapon is a client-side entity and is not in the demo.** `econ_entity.cpp:1153`
/// creates it with `InitializeAsClientEntity` and parents it to the viewmodel, taking its model from
/// the item definition rather than from the wire. So drawing a weapon in first person means finding
/// the model some other way.
///
/// The obvious other way is the weapon entity the player is already holding — `m_hActiveWeapon`,
/// which this project decodes. Whether that entity carries a model index is the question, and the
/// project's own memory says carried weapons send none.
/// </remarks>
public sealed class HeldWeaponProbe
{
    [Test]
    [Explicit("diagnostic")]
    public void HeldWeapons_WhatTheyCarry_IsReported()
    {
        string? path = Corpus.FilesWithSchema()
            .FirstOrDefault(file => Path.GetFileName(file).Contains("z1800", StringComparison.Ordinal));

        if (path is null)
        {
            Assert.Ignore("no z1800");
            return;
        }

        DemoTimeline timeline = TimelineCache.For(path);

        // **Selected by whether the track is ALIVE at the tick, not by entity index.** An entity
        // slot is reused when its occupant is destroyed, so a dictionary keyed by index keeps
        // whichever track was added last — which reported a sniper rifle as a bread crumpet, a
        // rocket launcher as a hat, and a knife as a particle material. Every one of those looked
        // like a decode fault and every one was this probe.
        string ModelAt(int tick, int entity) =>
            timeline.Props
                .Where(track => track.EntityIndex == entity && track.At(tick) is not null)
                .Select(track => track.ModelPath)
                .DefaultIfEmpty("(nothing alive in that slot)")
                .First();

        int held = 0;
        int withModel = 0;
        List<string> lines = [];

        foreach (int tick in (int[])[2883, 20000, 40000])
        {
            foreach (ScenePlayer player in timeline.PlayersAt(tick).Take(8))
            {
                if (player.ActiveWeapon is not { } weapon)
                {
                    continue;
                }

                held++;

                string model = ModelAt(tick, weapon);

                if (model.EndsWith(".mdl", StringComparison.OrdinalIgnoreCase))
                {
                    withModel++;
                }

                lines.Add(
                    $"tick {tick} entity {player.EntityIndex} class {player.PlayerClass}: " +
                    $"weapon entity {weapon} '{player.WeaponClass ?? "?"}' model {model}");
            }
        }

        TestContext.Out.WriteLine(string.Join(Environment.NewLine, lines.Take(24)));
        TestContext.Out.WriteLine($"HELD {held}, WITH A MODEL {withModel}");

        held.ShouldBeGreaterThan(0, "no player was holding anything, so nothing was measured");
    }
}
