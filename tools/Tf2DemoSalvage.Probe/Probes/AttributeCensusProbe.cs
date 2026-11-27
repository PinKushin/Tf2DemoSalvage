using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// Which demos NETWORK an econ attribute on a player's item — <c>m_AttributeList</c> or
/// <c>m_NetworkedDynamicAttributesForDemos</c> (<c>econ_item_view.cpp:191</c>) — beside a control attribute that must be found.
/// </summary>
/// <remarks>
/// Asked first for B69's head-origin branch: attribute 520, <c>particle_effect_use_head_origin</c>, which
/// <c>UpdateControlPoint</c> reads (<c>particle_property.cpp:558-593</c>) and no item definition carries statically.
/// **The control** is a second attribute the corpus is known to network (by default 134, <c>attach particle effect</c>, an
/// unusual's): a census that finds the control nowhere is a broken instrument, not an absence.
///
/// <code>
///   attribute-census                    520 against 134, every corpus demo, a sample every 660 ticks
///   attribute-census 520 134 2000       the same, a sample every 2000 ticks
/// </code>
///
/// Through the production path: <see cref="DemoTimeline.Build"/> and <c>DemoTimeline.PlayersAt</c>, each player's
/// <see cref="SceneItem.Wire"/> as the viewer resolves it. A sampled census: an item worn only between samples is missed.
/// </remarks>
public sealed class AttributeCensusProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "attribute-census";

    /// <inheritdoc/>
    public string Summary => "demos networking an econ attribute, with a control attribute: attribute-census [attr=520] [control=134] [stride=660]";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        int asked = arguments.Count > 0 ? int.Parse(arguments[0], CultureInfo.InvariantCulture) : 520;
        int control = arguments.Count > 1 ? int.Parse(arguments[1], CultureInfo.InvariantCulture) : 134;
        int stride = arguments.Count > 2 ? int.Parse(arguments[2], CultureInfo.InvariantCulture) : 660;
        (int Demos, int WithAsked, int WithControl, int Failed) totals = default;
        List<ScenePlayer> players = [];

        foreach (string path in DemoCorpus.Files(output))
        {
            DemoTimeline timeline;

            try
            {
                timeline = ProbeInstall.Timeline(File.ReadAllBytes(path), output);
            }
            catch (Exception failure) when (failure is InvalidDataException or IOException or ArgumentException or InvalidOperationException)
            {
                output.WriteLine($"  {Path.GetFileName(path)}: not read ({failure.GetType().Name})");
                totals.Failed++;
                continue;
            }

            HashSet<(int Entity, int Item)> withAsked = [];
            HashSet<(int Entity, int Item)> withControl = [];

            for (int tick = timeline.FirstTick; tick <= timeline.LastTick; tick += stride)
            {
                players.Clear();
                timeline.PlayersAt(tick, players);

                foreach (ScenePlayer player in players)
                {
                    foreach (SceneItem item in player.Items ?? [])
                    {
                        Count(item, asked, withAsked);
                        Count(item, control, withControl);
                    }
                }
            }

            totals.Demos++;
            totals.WithAsked += withAsked.Count > 0 ? 1 : 0;
            totals.WithControl += withControl.Count > 0 ? 1 : 0;

            if (withAsked.Count > 0 || withControl.Count > 0)
            {
                output.WriteLine(
                    $"  {Path.GetFileName(path)}: {asked} on {withAsked.Count} items, control {control} on {withControl.Count}"
                    + (withAsked.Count > 0 ? $" — {asked}: {string.Join(", ", withAsked.Select(each => $"entity {each.Entity} item {each.Item}"))}" : string.Empty));
            }
        }

        output.WriteLine(
            $"{totals.Demos} demos read, {totals.Failed} not: {asked} networked in {totals.WithAsked}; control {control} in {totals.WithControl}"
            + (totals.WithControl == 0 ? " — INSTRUMENT BROKEN: the control was found nowhere" : string.Empty));
    }

    /// <summary>Notes an item carrying the attribute in either networked list.</summary>
    private static void Count(SceneItem item, int attribute, HashSet<(int Entity, int Item)> into)
    {
        EconAttributeWire wire = item.Wire;

        if (wire.Local.Any(each => each.DefinitionIndex == attribute) ||
            wire.NetworkedForDemos.Any(each => each.DefinitionIndex == attribute))
        {
            into.Add((item.EntityIndex, item.DefinitionIndex ?? -1));
        }
    }
}
