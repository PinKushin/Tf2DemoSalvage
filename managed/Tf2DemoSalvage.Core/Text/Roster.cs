using System.Collections.Generic;

using Tf2DemoSalvage.Core.Net;

namespace Tf2DemoSalvage.Core.Text;

/// <summary>
/// Accumulates the player roster from the messages that carry it, for outputs that walk a demo.
/// </summary>
/// <remarks>
/// Both the summary and the trace need "who is user id 7", and both were deriving it separately —
/// the summary from a completed scan, the trace not at all, which is why the trace printed
/// <c>userid 18</c> where the summary printed <c>userid=cutemobb(18)</c>. Two outputs of one file
/// disagreeing about who a kill belongs to.
///
/// **Which messages carry the roster is the part worth having in one place.** A create message is
/// the obvious one; an update is not, and missing it is RISKS B22 — mid-match joiners were
/// invisible for exactly that reason. An update names its table only by creation-order id, which
/// is why the decode state has to remember table names.
/// </remarks>
internal static class Roster
{
    /// <summary>The string table naming connected players.</summary>
    public const string UserInfoTable = "userinfo";

    /// <summary>Applies a message to the roster if it carries roster data.</summary>
    /// <param name="message">A decoded message.</param>
    /// <param name="state">Decode state, which knows string table names by id.</param>
    /// <param name="players">Roster keyed by entity index, updated in place.</param>
    public static void Observe(
        INetMessage message, NetDecodeState state, IDictionary<int, PlayerInfo> players)
    {
        switch (message)
        {
            case CreateStringTableMessage table when table.Name == UserInfoTable:
                RosterBuilder.Apply(table.Entries, players);
                break;

            // Mid-game joins arrive here, not in the create message (RISKS B22).
            case UpdateStringTableMessage update
                when state?.StringTableName(update.TableId) == UserInfoTable:
                RosterBuilder.Apply(update.Entries, players);
                break;

            default:
                break;
        }
    }

    /// <summary>Re-keys a roster by user id, which is what game events reference.</summary>
    /// <param name="players">Roster keyed by entity index.</param>
    /// <returns>The same players, keyed by user id.</returns>
    /// <remarks>
    /// **Two identifiers, and they are not interchangeable.** Entities are addressed by index;
    /// game events carry <c>user_id</c>. Using one where the other belongs attributes an event to
    /// the wrong player and nothing fails, because both are small integers and both are usually
    /// valid.
    /// </remarks>
    public static Dictionary<int, PlayerInfo> ByUserId(IReadOnlyDictionary<int, PlayerInfo> players)
    {
        Dictionary<int, PlayerInfo> byUserId = [];
        foreach (PlayerInfo player in players.Values)
        {
            byUserId[player.UserId] = player;
        }

        return byUserId;
    }
}
