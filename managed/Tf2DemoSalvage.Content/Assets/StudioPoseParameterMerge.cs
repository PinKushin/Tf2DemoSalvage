using System;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Content.Assets;

/// <summary>
/// One shared pose parameter list across a model and everything it includes.
/// </summary>
/// <remarks>
/// **A sequence's <c>paramindex</c> is local to the group that owns it, and reading it against the
/// base model's list is how every player came to run backwards.** A player model declares almost no
/// pose parameters — <c>scout.mdl</c> has <c>body_pitch</c> and <c>body_yaw</c> and nothing else —
/// while <c>move_x</c> and <c>move_y</c> live in the animation model it includes. Asking the base
/// model for index 5 fell out of a two-entry list, which returned cell zero on both axes and
/// selected the run grid's <c>move_x = −1, move_y = −1</c> corner: the backward-left run, for every
/// moving player, forever. Nothing reported a fault because falling off the end of a list is a
/// legitimate answer to a question about a model that genuinely has no such parameter.
///
/// The engine builds a merged list when it loads a model with includes, in
/// <c>CVirtualModel::AppendPoseParameters</c> (<c>studio_virtualmodel.cpp:445</c>), and keeps a map
/// per group from local index to shared index — <c>masterPose</c>, read back by
/// <c>CStudioHdr::GetSharedPoseParameter</c>. This is that, and it follows the engine on three
/// points that are each easy to get subtly wrong:
///
/// <list type="bullet">
/// <item>Parameters are matched <b>by name, case-insensitively</b> (<c>stricmp</c>), not by index
/// or by identity — two models declare the same parameter in different positions and the name is
/// the only thing they agree on.</item>
/// <item>A duplicate <b>widens the shared range</b> to span both, taking the minimum and maximum
/// across all four endpoints. <c>body_pitch</c> is −45..45 in <c>scout.mdl</c> and −45..90 in
/// <c>scout_animations.mdl</c>; normalising against the narrower one puts every pitch at the wrong
/// fraction of its range.</item>
/// <item>The shared list is in <b>discovery order</b>, which is group order, so the base model's
/// parameters keep their own indices and included models append.</item>
/// </list>
/// </remarks>
public static class StudioPoseParameterMerge
{
    /// <summary>Merges each group's parameters into one list, with a map back per group.</summary>
    /// <param name="groups">Each group's own parameters, in group order with the base model first.</param>
    /// <returns>The shared list, and for each group a map from its local index to a shared one.</returns>
    public static (IReadOnlyList<StudioPoseParameter> Shared, IReadOnlyList<IReadOnlyList<int>> MasterPose)
        Merge(IReadOnlyList<IReadOnlyList<StudioPoseParameter>> groups)
    {
        ArgumentNullException.ThrowIfNull(groups);

        List<StudioPoseParameter> shared = [];
        List<IReadOnlyList<int>> masterPose = [];

        foreach (IReadOnlyList<StudioPoseParameter> group in groups)
        {
            int[] map = new int[group.Count];

            for (int local = 0; local < group.Count; local++)
            {
                StudioPoseParameter parameter = group[local];
                int found = IndexOfName(shared, parameter.Name);

                if (found < 0)
                {
                    shared.Add(parameter);
                    map[local] = shared.Count - 1;
                    continue;
                }

                // The widening. Nested over start AND end of both, as Valve writes it, which is
                // not the same as picking the wider of the two ranges: a parameter authored with
                // its end below its start would otherwise keep an inverted span.
                StudioPoseParameter existing = shared[found];

                shared[found] = existing with
                {
                    Start = MathF.Min(
                        MathF.Min(existing.Start, existing.End),
                        MathF.Min(parameter.Start, parameter.End)),
                    End = MathF.Max(
                        MathF.Max(existing.Start, existing.End),
                        MathF.Max(parameter.Start, parameter.End)),
                };

                map[local] = found;
            }

            masterPose.Add(map);
        }

        return (shared, masterPose);
    }

    /// <summary>Where a parameter of this name already sits in the shared list.</summary>
    /// <param name="shared">The list built so far.</param>
    /// <param name="name">The name being looked for.</param>
    /// <returns>Its index, or −1 when it is new.</returns>
    private static int IndexOfName(List<StudioPoseParameter> shared, string name)
    {
        for (int index = 0; index < shared.Count; index++)
        {
            if (string.Equals(shared[index].Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }
}
