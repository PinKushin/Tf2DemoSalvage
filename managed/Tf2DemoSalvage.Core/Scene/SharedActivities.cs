using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Tf2DemoSalvage.Core.Scene;

/// <summary>
/// The engine's shared activities — `ActivityList_RegisterSharedActivities`, each at its `Activity` enum value (B437).
/// </summary>
/// <remarks>
/// **An activity crosses the wire as its list index** — a voice command's `m_nData` is the server's
/// `ActivityList_IndexForName` — and `REGISTER_SHARED_ACTIVITY( ACT_X )` puts each shared name at its enum value
/// (`activitylist.cpp:108-137`), so the list is `ai_activity.h`'s enum in order from `ACT_RESET` at 0. The names are
/// data (`Data/shared-activities.txt`), compared with the SDK both ways by `SharedActivitiesConformanceTests`. The
/// enum is append-only across eras (`docs/findings/25-gesture-layer.md`), so an older demo's indices name the same
/// activities. An index past the list is a PRIVATE activity, numbered by whichever models the server loaded first,
/// and no list can name it.
/// </remarks>
public static class SharedActivities
{
    private static readonly string[] Names = Load();

    private static readonly FrozenSet<string> Shared = Names.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>`ActivityList_NameForIndex` for a shared activity.</summary>
    /// <param name="index">The activity's index.</param>
    /// <returns>Its name, or null for `ACT_INVALID` or a private index.</returns>
    public static string? NameOf(int index) => index >= 0 && index < Names.Length ? Names[index] : null;

    /// <summary>Whether a name is a shared activity, which `ActivityList_IndexForName` always finds.</summary>
    /// <param name="name">The activity's name.</param>
    /// <returns>Whether it is in the shared list; the engine compares without case (`ListFromString`).</returns>
    public static bool IsShared(string name) => Shared.Contains(name);

    private static string[] Load()
    {
        using Stream stream = typeof(SharedActivities).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"{ResourceName} is not embedded");
        using StreamReader reader = new(stream);

        List<string> names = [];

        while (reader.ReadLine() is { } line)
        {
            if (line.Length > 0)
            {
                names.Add(line);
            }
        }

        return [.. names];
    }

    private const string ResourceName = "Tf2DemoSalvage.Core.shared-activities.txt";
}
