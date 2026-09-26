using System.Collections.Generic;

using Tf2DemoSalvage.Core.Net;

namespace Tf2DemoSalvage.Core.Scene;

/// <summary>One game event as a client's listeners receive it, with who was in each slot when it arrived.</summary>
/// <param name="Tick">The tick of the packet that carried it.</param>
/// <param name="Name">The event's name, such as `player_death`.</param>
/// <param name="Values">Its fields.</param>
/// <param name="Roster">The `userinfo` table by slot at that moment — what `engine->GetPlayerForUserID` and
/// `GetPlayerInfo` answer while the event is being handled, since a slot can be reused later.</param>
public sealed record SceneGameEvent(int Tick, string Name, IReadOnlyDictionary<string, object?> Values, IReadOnlyDictionary<int, PlayerInfo> Roster)
{
    /// <summary>`IGameEvent::GetInt`: a whole-number field, or <paramref name="fallback"/> when absent.</summary>
    /// <param name="key">The field.</param>
    /// <param name="fallback">The default.</param>
    /// <returns>The value.</returns>
    public int GetInt(string key, int fallback = 0) => Values.GetValueOrDefault(key) switch
    {
        int whole => whole,
        short whole => whole,
        byte whole => whole,
        long whole => (int)whole,
        bool flag => flag ? 1 : 0,
        float real => (int)real,
        _ => fallback,
    };

    /// <summary>`IGameEvent::GetString`: a text field, or <paramref name="fallback"/> when absent.</summary>
    /// <param name="key">The field.</param>
    /// <param name="fallback">The default.</param>
    /// <returns>The value.</returns>
    public string GetString(string key, string fallback = "") => Values.GetValueOrDefault(key) as string ?? fallback;

    /// <summary>`engine->GetPlayerForUserID`: the slot holding that user id, or 0.</summary>
    /// <param name="userId">The user id.</param>
    /// <returns>The entity index.</returns>
    public int PlayerForUserId(int userId)
    {
        foreach ((int slot, PlayerInfo player) in Roster)
        {
            if (player.UserId == userId)
            {
                return slot;
            }
        }

        return 0;
    }
}
