using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Core.Schema;

namespace Tf2DemoSalvage.Core.Scene;

/// <summary>Every explosion a demo carries, in the order they fired (B415).</summary>
/// <remarks>
/// **An explosion is a one-shot at a tick, which is a different lifetime from anything else the timeline holds.**
/// A player has a state at every tick and a prop has a track; a blast happens once and its effect then runs for its
/// own duration. So this is a list in tick order and a range search over it, rather than a per-tick sample — a
/// viewer asks what fired in a window and replays each from where it started.
///
/// **The same shape as `PlayerGestureFeed`, filled from the same walk.** `DemoTimeline` decodes each
/// `svc_TempEntities` body once and offers every effect in it to the feeds that want one; this takes
/// `CTETFExplosion` and leaves the rest alone.
///
/// **What is NOT here**: which particle system each blast draws. That needs the weapon scripts and
/// `VectorAngles`, both of which live above Core — see `Tf2DemoSalvage.Scene`. This layer decodes and nothing more,
/// which is why the only judgement it makes is the one encoded in the wire itself
/// (<see cref="SceneExplosion.InAir"/>).
/// </remarks>
public sealed class ExplosionFeed
{
    /// <summary>The server class this reads, as the demo's own class table names it.</summary>
    public const string EventClassName = "CTETFExplosion";

    /// <summary>`m_vecOrigin[0]`. The origin arrives as three floats, not as one vector.</summary>
    private const string OriginX = "m_vecOrigin[0]";

    /// <summary>`m_vecOrigin[1]`.</summary>
    private const string OriginY = "m_vecOrigin[1]";

    /// <summary>`m_vecOrigin[2]`.</summary>
    private const string OriginZ = "m_vecOrigin[2]";

    /// <summary>`m_vecNormal`, which IS one vector.</summary>
    private const string NormalProperty = "m_vecNormal";

    /// <summary>`m_iWeaponID`.</summary>
    private const string WeaponProperty = "m_iWeaponID";

    /// <summary>`entindex` — the send table's own name for it, without an `m_` prefix.</summary>
    private const string EntityProperty = "entindex";

    /// <summary>`m_iCustomParticleIndex`.</summary>
    private const string CustomParticleProperty = "m_iCustomParticleIndex";

    /// <summary>`m_nDefID`.</summary>
    private const string ItemProperty = "m_nDefID";

    /// <summary>`m_nSound`.</summary>
    private const string SoundProperty = "m_nSound";

    private readonly List<SceneExplosion> _blasts = [];

    /// <summary>Every blast recorded, in tick order.</summary>
    public IReadOnlyList<SceneExplosion> All => _blasts;

    /// <summary>Takes one decoded temp entity if it is an explosion.</summary>
    /// <param name="className">The class the effect's id resolved to.</param>
    /// <param name="effect">The decoded effect.</param>
    /// <param name="tick">The demo tick its packet arrived on.</param>
    /// <param name="isPlayer">
    /// Whether an entity index names a player in the client's entity list right now — <c>IsPlayer()</c> on what
    /// `C_BaseEntity::Instance` returns for it. Never asked about "no entity".
    /// </param>
    /// <returns><c>true</c> when it was an explosion and was recorded.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// **Absent fields fall to the constructor's defaults, which are `C_TETFExplosion`'s own.** A temp entity's
    /// properties are a delta against the class's defaults, so a blast that sends no `m_iCustomParticleIndex` means
    /// `INVALID_STRING_INDEX` rather than nothing — `docs/memory/sentinels-conflate-unknown-with-answer.md`.
    /// </remarks>
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
        int weapon = 0;
        int entity = SceneExplosion.NoEntity;
        int custom = SceneExplosion.NoCustomParticle;
        int item = SceneExplosion.NoItem;
        int sound = SceneExplosion.Special1;

        foreach (DecodedProperty property in effect.Properties)
        {
            switch (property.Definition.Property.Name)
            {
                case OriginX: x = property.Value.AsFloat; break;
                case OriginY: y = property.Value.AsFloat; break;
                case OriginZ: z = property.Value.AsFloat; break;
                case NormalProperty: normal = property.Value.AsVector; break;
                case WeaponProperty: weapon = (int)property.Value.AsInt; break;
                case EntityProperty: entity = Entity((int)property.Value.AsInt); break;
                case CustomParticleProperty: custom = (int)property.Value.AsInt; break;
                case ItemProperty: item = (int)property.Value.AsInt; break;
                case SoundProperty: sound = (int)property.Value.AsInt; break;
                default: break;
            }
        }

        // `INVALID_EHANDLE.Get()` is null, so "no entity" is never a player and is never looked up.
        bool struck = entity != SceneExplosion.NoEntity && isPlayer(entity);

        _blasts.Add(new SceneExplosion(tick, x, y, z, normal, weapon, entity, custom, struck, item, sound));

        return true;
    }

    /// <summary>Every blast that fired in a window of ticks, both ends included.</summary>
    /// <param name="fromTick">The first tick to include.</param>
    /// <param name="toTick">The last.</param>
    /// <param name="into">Cleared, then filled in tick order with each blast and its index in <see cref="All"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="into"/> is null.</exception>
    /// <remarks>
    /// **A range and not a lookup.** A blast's effect runs for a second or two after it fires, so a viewer at tick
    /// <c>n</c> wants everything back to <c>n</c> minus that duration — and it must get nothing rather than the
    /// nearest blast when none fired, or a quiet moment would show the last explosion of the match.
    ///
    /// **The INDEX comes back with the blast, because the renderer needs a stable identity for it.** A one-shot
    /// effect has to be recognised between frames or it restarts every frame, and two rockets landing on one tick
    /// are two effects. The index into <see cref="All"/> is exact and free; a hash of the blast's own fields would
    /// be neither.
    /// </remarks>
    public void Between(int fromTick, int toTick, ICollection<(int Index, SceneExplosion Blast)> into)
    {
        ArgumentNullException.ThrowIfNull(into);

        into.Clear();

        for (int at = FirstFrom(fromTick); at < _blasts.Count && _blasts[at].Tick <= toTick; at++)
        {
            into.Add((at, _blasts[at]));
        }
    }

    /// <summary>The index of the first blast at or after a tick.</summary>
    /// <remarks>
    /// A binary search rather than a scan: `demostf-cp_process_f12` carries 2,786 of these over one match, and this
    /// is asked once a frame.
    /// </remarks>
    private int FirstFrom(int tick)
    {
        int low = 0;
        int high = _blasts.Count;

        while (low < high)
        {
            int middle = low + ((high - low) / 2);

            if (_blasts[middle].Tick < tick)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }

    /// <summary>`RecvProxy_ExplosionEntIndex` — both encodings of "no entity" become one.</summary>
    private static int Entity(int sent) =>
        sent is SceneExplosion.InvalidEntityIndex or SceneExplosion.NoEntity
            ? SceneExplosion.NoEntity
            : sent;
}
