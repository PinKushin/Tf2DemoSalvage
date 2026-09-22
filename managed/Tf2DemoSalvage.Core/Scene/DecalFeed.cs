using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Core.Schema;

namespace Tf2DemoSalvage.Core.Scene;

/// <summary>Which temp entity a decal arrived as.</summary>
public enum SceneDecalKind
{
    /// <summary>`CTEWorldDecal`: an origin and a decal, on the world.</summary>
    World,

    /// <summary>`CTEDecal`: a ray, an entity and a hitbox — the world, a brush model, a static prop or a model.</summary>
    Entity,

    /// <summary>`CTEPlayerDecal`: a player's spray.</summary>
    Player,
}

/// <summary>One decal event as the demo carried it (B415).</summary>
/// <param name="Tick">The tick its packet arrived on.</param>
/// <param name="Kind">Which temp entity.</param>
/// <param name="Origin">`m_vecOrigin`, the decal's centre.</param>
/// <param name="Start">`m_vecStart`, where a `CTEDecal`'s ray begins; zero when unsent, as `C_TEDecal`'s constructor has it.</param>
/// <param name="Entity">`m_nEntity`: 0 for the world.</param>
/// <param name="Hitbox">`m_nHitbox`: on the world, a static prop's index plus one; 0 for none.</param>
/// <param name="Index">`m_nIndex`, into the <c>decalprecache</c> table; a spray has none.</param>
/// <param name="Player">`m_nPlayer`, a spray's owner; only a spray sends it.</param>
public readonly record struct SceneDecal(
    int Tick,
    SceneDecalKind Kind,
    (float X, float Y, float Z) Origin,
    (float X, float Y, float Z) Start,
    int Entity,
    int Hitbox,
    int Index,
    int Player);

/// <summary>Every decal a demo's temp entities carry, in fire order, and the table that names them (B415).</summary>
/// <remarks>
/// **Decals are state, not one-shots**: a decal stays until the engine's pool pushes it out, so a viewer at a tick
/// needs every decal before it and replays them into the world in order — not a window, as explosions are asked for.
/// </remarks>
public sealed class DecalFeed
{
    /// <summary>`CTEWorldDecal`.</summary>
    public const string WorldClassName = "CTEWorldDecal";

    /// <summary>`CTEDecal`.</summary>
    public const string EntityClassName = "CTEDecal";

    /// <summary>`CTEPlayerDecal`.</summary>
    public const string PlayerClassName = "CTEPlayerDecal";

    /// <summary>The string table <c>m_nIndex</c> points into.</summary>
    public const string TableName = "decalprecache";

    private readonly List<SceneDecal> _decals = [];

    /// <summary>Every decal event, in tick order.</summary>
    public IReadOnlyList<SceneDecal> All => _decals;

    /// <summary>The <c>decalprecache</c> table: a decal's name by its index.</summary>
    public NameTable Names { get; } = new();

    /// <summary>Takes one decoded temp entity if it is a decal.</summary>
    /// <param name="className">The class the effect's id resolved to.</param>
    /// <param name="effect">The decoded effect.</param>
    /// <param name="tick">The tick its packet arrived on.</param>
    /// <returns><c>true</c> when it was a decal and was recorded.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>An unsent field is the client class's constructor default, which is zero for every one of them.</remarks>
    public bool Record(string className, DecodedTempEntity effect, int tick)
    {
        ArgumentNullException.ThrowIfNull(className);
        ArgumentNullException.ThrowIfNull(effect);

        SceneDecalKind kind;

        switch (className)
        {
            case WorldClassName: kind = SceneDecalKind.World; break;
            case EntityClassName: kind = SceneDecalKind.Entity; break;
            case PlayerClassName: kind = SceneDecalKind.Player; break;
            default: return false;
        }

        (float X, float Y, float Z) origin = default;
        (float X, float Y, float Z) start = default;
        int entity = 0;
        int hitbox = 0;
        int index = 0;
        int player = 0;

        foreach (DecodedProperty property in effect.State)
        {
            switch (property.Definition.Property.Name)
            {
                case "m_vecOrigin": origin = property.Value.AsVector; break;
                case "m_vecStart": start = property.Value.AsVector; break;
                case "m_nEntity": entity = (int)property.Value.AsInt; break;
                case "m_nHitbox": hitbox = (int)property.Value.AsInt; break;
                case "m_nIndex": index = (int)property.Value.AsInt; break;
                case "m_nPlayer": player = (int)property.Value.AsInt; break;
                default: break;
            }
        }

        _decals.Add(new SceneDecal(tick, kind, origin, start, entity, hitbox, index, player));

        return true;
    }
}
