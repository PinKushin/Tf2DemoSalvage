using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Core.Schema;

namespace Tf2DemoSalvage.Core.Scene;

/// <summary>Every hitscan shot a demo carries, in the order they fired (B415).</summary>
/// <remarks>
/// **The same shape as <see cref="ExplosionFeed"/>**, filled from the same walk over `svc_TempEntities`: a shot is a
/// one-shot at a tick whose tracers run on afterwards, so it is a list in fire order asked for by window.
///
/// **The shooter is captured when the shot arrives**, because that is when `FX_FireBullets` asks: whether the player
/// exists, their team, and what they were holding. Asked later, a player who died or switched weapons in the
/// meantime would give a different answer.
/// </remarks>
public sealed class ShotFeed
{
    /// <summary>The server class this reads.</summary>
    public const string EventClassName = "CTEFireBullets";

    private readonly List<SceneShot> _shots = [];

    /// <summary>Every shot recorded, in tick order.</summary>
    public IReadOnlyList<SceneShot> All => _shots;

    /// <summary>Takes one decoded temp entity if it is a shot.</summary>
    /// <param name="className">The class the effect's id resolved to.</param>
    /// <param name="effect">The decoded effect.</param>
    /// <param name="tick">The demo tick its packet arrived on.</param>
    /// <param name="shooter">The player at an entity index as the client's list has them now, or null.</param>
    /// <returns><c>true</c> when it was a shot and was recorded.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>An unsent field is the client's default: mode 0, not critical, player 0.</remarks>
    public bool Record(string className, DecodedTempEntity effect, int tick, Func<int, ShotShooter?> shooter)
    {
        ArgumentNullException.ThrowIfNull(className);
        ArgumentNullException.ThrowIfNull(effect);
        ArgumentNullException.ThrowIfNull(shooter);

        if (!string.Equals(className, EventClassName, StringComparison.Ordinal))
        {
            return false;
        }

        (float X, float Y, float Z) origin = default;
        float pitch = 0f;
        float yaw = 0f;
        int weapon = 0;
        int mode = 0;
        int seed = 0;
        int player = 0;
        float spread = 0f;
        bool critical = false;

        foreach (DecodedProperty property in effect.State)
        {
            switch (property.Definition.Property.Name)
            {
                case "m_vecOrigin": origin = property.Value.AsVector; break;
                case "m_vecAngles[0]": pitch = property.Value.AsFloat; break;
                case "m_vecAngles[1]": yaw = property.Value.AsFloat; break;
                case "m_iWeaponID": weapon = (int)property.Value.AsInt; break;
                case "m_iMode": mode = (int)property.Value.AsInt; break;
                case "m_iSeed": seed = (int)property.Value.AsInt; break;
                case "m_iPlayer": player = (int)property.Value.AsInt; break;
                case "m_flSpread": spread = property.Value.AsFloat; break;
                case "m_bCritical": critical = property.Value.AsInt != 0; break;
                default: break;
            }
        }

        // `FX_FireBullets( NULL, m_iPlayer + 1, … )` — the wire carries the player one below the entity.
        int entity = player + 1;

        _shots.Add(new SceneShot(tick, entity, origin, pitch, yaw, weapon, mode, seed, spread, critical, shooter(entity)));

        return true;
    }

    /// <summary>Every shot that fired in a window of ticks, both ends included, with its index in <see cref="All"/>.</summary>
    /// <param name="fromTick">The first tick to include.</param>
    /// <param name="toTick">The last.</param>
    /// <param name="into">Cleared, then filled in tick order.</param>
    public void Between(int fromTick, int toTick, ICollection<(int Index, SceneShot Shot)> into) =>
        TickWindow.Between(_shots, static shot => shot.Tick, fromTick, toTick, into);
}
