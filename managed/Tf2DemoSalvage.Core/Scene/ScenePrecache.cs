using System.Collections.Generic;

using Tf2DemoSalvage.Core.Net;

namespace Tf2DemoSalvage.Core.Scene;

/// <summary>
/// The <c>Scenes</c> string table: which compiled scene each <c>m_nSceneStringIndex</c> names (B351).
/// </summary>
/// <remarks>
/// **This is the only thing on the wire that says which taunt played.** `DT_SceneEntity` sends
/// `m_nSceneStringIndex` and nothing else about the animation
/// (<c>gameinterface.cpp:1448</c>), and the sequence the player's model plays is a string inside the
/// compiled scene — `LookupSequence( event-&gt;GetParameters() )` (<c>c_tf_player.cpp:9456</c>). The
/// item cannot substitute, because the server picks the scene at random from the item's list
/// (<c>tf_player.cpp:17391</c>).
///
/// **Measured present**: `tf2-2026-pub-pov-clean` carries the table with 4,431 entries, all of the
/// form <c>scenes/player/&lt;class&gt;/low/&lt;taunt&gt;.vcd</c>, and declares `CSceneEntity` with
/// `m_nSceneStringIndex`, `m_bIsPlayingBack`, `m_bPaused`, `m_flForceClientTime` and a 16-slot
/// `m_hActorList`.
///
/// **Kept separate from <see cref="ModelPrecache"/> rather than folded into a generic table store**,
/// for the reason that class already gives: entry 7 of one table and entry 7 of another are
/// different things, and one dictionary would have each quietly overwrite the other.
/// </remarks>
public sealed class ScenePrecache
{
    /// <summary>The table this reads. Updates name their table only by id, not by name.</summary>
    public const string TableName = "Scenes";

    private readonly Dictionary<int, string> _scenes = [];

    /// <summary>How many scenes the table has named so far.</summary>
    public int Count => _scenes.Count;

    /// <summary>Records a create or update message's entries.</summary>
    /// <param name="entries">Entries from the message; later ones replace earlier ones.</param>
    /// <remarks>
    /// **By the entry's own index, not its position in this list** — an update carries only what
    /// changed, each entry stating where it belongs, so numbering from zero would rewrite the front
    /// of the table with whatever happened to change.
    /// </remarks>
    public void Apply(IReadOnlyList<StringTableEntry> entries)
    {
        if (entries is null)
        {
            return;
        }

        foreach (StringTableEntry entry in entries)
        {
            // An entry with no text is a payload-only update to one already named; the scene's
            // filename does not change.
            if (entry.Index < 0 || string.IsNullOrEmpty(entry.Text))
            {
                continue;
            }

            _scenes[entry.Index] = entry.Text;
        }
    }

    /// <summary>The scene an index names.</summary>
    /// <param name="sceneIndex">The entity's <c>m_nSceneStringIndex</c>.</param>
    /// <returns>The scene's filename, or null when the table cannot name one.</returns>
    /// <remarks>
    /// **No guard on the number itself**, for the reason <see cref="ModelPrecache.Path"/> gives: a
    /// range check here would be a branch no input can reach, since nothing negative is ever stored.
    /// </remarks>
    public string? Path(int sceneIndex) =>
        _scenes.TryGetValue(sceneIndex, out string? scene) ? scene : null;
}
