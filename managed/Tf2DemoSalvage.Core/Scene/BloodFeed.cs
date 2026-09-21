using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Core.Schema;

namespace Tf2DemoSalvage.Core.Scene;

/// <summary>One `CTETFBlood`: where a player was hit, and the way the blood sprays (B415).</summary>
/// <param name="Tick">The tick its packet arrived on.</param>
/// <param name="Origin">`m_vecOrigin`, the damage position.</param>
/// <param name="Normal">`m_vecNormal`: the server sends `−vecDir`, back towards the shooter.</param>
/// <param name="Entity">`entindex`, the bleeding player; −1 for none.</param>
/// <param name="IsPlayer">Whether that entity was a player when the blood arrived — `dynamic_cast&lt;C_TFPlayer*&gt;`.</param>
public readonly record struct SceneBlood(
    int Tick, (float X, float Y, float Z) Origin, (float X, float Y, float Z) Normal, int Entity, bool IsPlayer);

/// <summary>Every `CTETFBlood` a demo carries, in fire order (B415).</summary>
/// <remarks>
/// `DT_TETFBlood` is `m_vecOrigin[0..2]` as three floats, `m_vecNormal`, and `entindex` through
/// `RecvProxy_BloodEntIndex`, which turns a negative index into no entity (`tf_fx_blood.cpp:165`). Every field the
/// constructor zeroes, so an unsent one is zero — an unsent `entindex` names entity 0, the world, which is not a player.
/// </remarks>
public sealed class BloodFeed
{
    /// <summary>The server class this reads.</summary>
    public const string EventClassName = "CTETFBlood";

    private readonly List<SceneBlood> _blood = [];

    /// <summary>Every blood event, in tick order.</summary>
    public IReadOnlyList<SceneBlood> All => _blood;

    /// <summary>Takes one decoded temp entity if it is blood.</summary>
    /// <param name="className">The class the effect's id resolved to.</param>
    /// <param name="effect">The decoded effect.</param>
    /// <param name="tick">The tick its packet arrived on.</param>
    /// <param name="isPlayer">Whether an entity index names a player in the client's entity list right now.</param>
    /// <returns><c>true</c> when it was blood and was recorded.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public bool Record(string className, DecodedTempEntity effect, int tick, Func<int, bool> isPlayer)
    {
        ArgumentNullException.ThrowIfNull(className);
        ArgumentNullException.ThrowIfNull(effect);
        ArgumentNullException.ThrowIfNull(isPlayer);

        if (!string.Equals(className, EventClassName, StringComparison.Ordinal))
        {
            return false;
        }

        float x = 0f;
        float y = 0f;
        float z = 0f;
        (float X, float Y, float Z) normal = default;
        int entity = 0;

        foreach (DecodedProperty property in effect.Properties)
        {
            switch (property.Definition.Property.Name)
            {
                case "m_vecOrigin[0]": x = property.Value.AsFloat; break;
                case "m_vecOrigin[1]": y = property.Value.AsFloat; break;
                case "m_vecOrigin[2]": z = property.Value.AsFloat; break;
                case "m_vecNormal": normal = property.Value.AsVector; break;
                case "entindex": entity = (int)property.Value.AsInt; break;
                default: break;
            }
        }

        entity = entity < 0 ? -1 : entity;

        _blood.Add(new SceneBlood(tick, (x, y, z), normal, entity, entity >= 0 && isPlayer(entity)));

        return true;
    }
}
