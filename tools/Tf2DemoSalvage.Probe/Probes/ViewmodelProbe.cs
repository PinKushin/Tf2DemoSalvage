using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// Every weapon the recorder holds, as a list of CHANGES with the tick each began.
/// </summary>
/// <remarks>
/// **Written to answer "which tick shows the thing you saw".** The owner: *"the red sleeve was seen
/// when i switched to the solly shotgun, so if you look for those ticks you can see it"* — and
/// finding that by scrubbing is minutes of somebody's attention for a question the timeline can
/// answer outright.
///
/// Reports the OWNER as well as the model, because the viewmodel's owner is what decides its skin
/// family — `CEconItemView::GetSkin( iTeam, … )` takes `pOwner->GetTeamNumber()` — and a viewmodel
/// that names no owner cannot be team-coloured at all (B242).
///
/// <code>
///   viewmodels tf2-2026-pub-pov-clean
///   viewmodels tf2-2026-pub-pov-clean shotgun
/// </code>
/// </remarks>
public sealed class ViewmodelProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "viewmodels";

    /// <inheritdoc/>
    public string Summary =>
        "what a player holds in first person, tick by change: viewmodels <demo> [player] [substring]";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count == 0)
        {
            output.WriteLine("viewmodels <demo> [player entity] [model substring]");
            return;
        }

        string? path = DemoCorpus.Find(arguments[0], output);
        if (path is null)
        {
            output.WriteLine($"No demo named '{arguments[0]}'.");
            return;
        }

        // Either order, and either one alone: a number is a player, anything else is a substring.
        // The old form `viewmodels <demo> shotgun` therefore still means what it did.
        string filter = string.Empty;
        int? asked = null;

        for (int index = 1; index < arguments.Count; index++)
        {
            if (int.TryParse(
                    arguments[index],
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out int player))
            {
                asked = player;
            }
            else
            {
                filter = arguments[index];
            }
        }

        DemoTimeline timeline = DemoTimeline.Build(File.ReadAllBytes(path));

        // **The recorder is the wrong subject on a SourceTV demo, and the probe ANSWERED rather
        // than failing** — entity 1 is the SourceTV, which holds nothing, so this reported "the
        // recorder never held anything matching that" for every STV file in the corpus. That is
        // half the corpus, and the half where first-person parity has to be checked at all: a POV
        // recording carries one viewmodel and never names an owner, while an STV recording carries
        // one per player and names every one, which is the case `DemoTimeline.Viewmodel`'s owner
        // rule exists for. An instrument that reports absence for a whole class of input is the
        // shape `docs/memory/an-empty-search-needs-a-control.md` is about.
        if ((asked ?? timeline.RecorderEntityIndex) is not { } follower)
        {
            output.WriteLine(
                "The demo names no recorder, so name a player: viewmodels <demo> <player entity>.");
            return;
        }

        TimelineViewmodels viewmodels = new(timeline);

        output.WriteLine(
            $"{Path.GetFileName(path)} player {follower.ToString(CultureInfo.InvariantCulture)}"
            + (asked is null ? " (the recorder)" : string.Empty)
            + $", ticks {timeline.FirstTick.ToString(CultureInfo.InvariantCulture)}"
            + $"-{timeline.LastTick.ToString(CultureInfo.InvariantCulture)}, filter '{filter}'");

        string was = string.Empty;
        int changes = 0;

        for (int tick = timeline.FirstTick; tick <= timeline.LastTick; tick++)
        {
            if (viewmodels.MainHandAt(tick, follower) is not { } weapon)
            {
                continue;
            }

            // Keyed on the item as well as the model, because the whole point of asking is often
            // an attachment: a festivized scattergun and a plain one name the same `.mdl`.
            string now = weapon.ModelPath
                + "|"
                + (weapon.WeaponItem?.ToString(CultureInfo.InvariantCulture) ?? "-");

            if (string.Equals(now, was, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            was = now;

            if (filter.Length > 0
                && !weapon.ModelPath.Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // The team of the owner is what the skin family comes from, so an absent owner is the
            // interesting case rather than a detail. Item and attributes ride alongside because
            // they are what the attachments delegate keys on (B252) — reported as the viewmodel
            // sample CARRIES them, not recomputed from the weapon entity by a second route.
            output.WriteLine(
                $"  tick {tick,7}  owner "
                + $"{weapon.OwnerEntityIndex?.ToString(CultureInfo.InvariantCulture) ?? "none",5}"
                + $"  item {weapon.WeaponItem?.ToString(CultureInfo.InvariantCulture) ?? "-",6}"
                + $"  {Attributes(weapon.WeaponEcon),-28}  "
                + weapon.ModelPath);

            if (++changes > 60)
            {
                output.WriteLine("  ... more changes than are worth printing");
                return;
            }
        }

        if (changes == 0)
        {
            output.WriteLine("  that player never held anything matching that");
        }
    }

    /// <summary>The attribute definition indices the sample carries, as they arrived.</summary>
    /// <remarks>
    /// Reported by INDEX rather than by resolved name on purpose: naming them would need the item
    /// schema, and a probe that resolves is a probe that can disagree with the thing it is
    /// measuring. The index is what the wire said.
    /// </remarks>
    private static string Attributes(EconAttributeWire? econ)
    {
        if (econ is not { } wire)
        {
            return "no attributes";
        }

        IReadOnlyList<EconAttributeValue> list =
            wire.NetworkedForDemos.Count > 0 ? wire.NetworkedForDemos : wire.Local;

        StringBuilder text = new("attrs [");

        for (int index = 0; index < list.Count; index++)
        {
            if (index > 0)
            {
                text.Append(' ');
            }

            text.Append(list[index].DefinitionIndex.ToString(CultureInfo.InvariantCulture));
        }

        return text.Append(']').ToString();
    }
}
