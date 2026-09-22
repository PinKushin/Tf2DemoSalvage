using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Core.Schema;

namespace Tf2DemoSalvage.Core.Scene;

/// <summary>Which spark temp entity, which decides what the client draws.</summary>
public enum SparkKind
{
    /// <summary>`CTESparks`: `FX_ElectricSpark( origin, magnitude, trail, &amp;dir )`.</summary>
    Electric,

    /// <summary>`CTEMetalSparks`: `FX_MetalSpark( pos, dir, dir )`.</summary>
    Metal,

    /// <summary>`CTEArmorRicochet`: `FX_MetalSpark( pos, dir, dir )` and `FX_RicochetSound`.</summary>
    Ricochet,
}

/// <summary>One spark temp entity (B415).</summary>
/// <param name="Tick">The tick its packet arrived on.</param>
/// <param name="Kind">Which temp entity.</param>
/// <param name="Position">`m_vecOrigin`, or `m_vecPos`.</param>
/// <param name="Direction">`m_vecDir`.</param>
/// <param name="Magnitude">`m_nMagnitude`; zero for the metal kinds, which have none.</param>
/// <param name="TrailLength">`m_nTrailLength`; zero for the metal kinds.</param>
public readonly record struct SceneSpark(
    int Tick,
    SparkKind Kind,
    (float X, float Y, float Z) Position,
    (float X, float Y, float Z) Direction,
    int Magnitude,
    int TrailLength);

/// <summary>Every `CTESparks`, `CTEMetalSparks` and `CTEArmorRicochet`, in fire order (B415).</summary>
/// <remarks>
/// An unsent field is zero through its proxy. `C_TESparks` always passes `&amp;m_vecDir`, so an electric spark always
/// leans along its direction, even a zero one.
/// </remarks>
public sealed class SparkFeed
{
    private readonly List<SceneSpark> _sparks = [];

    /// <summary>Every spark, in fire order.</summary>
    public IReadOnlyList<SceneSpark> All => _sparks;

    /// <summary>Takes one decoded temp entity if it is a spark.</summary>
    /// <param name="className">The class the effect's id resolved to.</param>
    /// <param name="effect">The decoded effect.</param>
    /// <param name="tick">The tick its packet arrived on.</param>
    /// <returns><c>true</c> when it was one and was recorded.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public bool Record(string className, DecodedTempEntity effect, int tick)
    {
        ArgumentNullException.ThrowIfNull(className);
        ArgumentNullException.ThrowIfNull(effect);

        SparkKind kind;

        switch (className)
        {
            case "CTESparks": kind = SparkKind.Electric; break;
            case "CTEMetalSparks": kind = SparkKind.Metal; break;
            case "CTEArmorRicochet": kind = SparkKind.Ricochet; break;
            default: return false;
        }

        (float X, float Y, float Z) position = default, direction = default;
        int magnitude = 0, trail = 0;

        foreach (DecodedProperty property in effect.State)
        {
            PropertyValue value = property.Value;

            switch (property.Definition.Property.Name)
            {
                case "m_vecOrigin[0]": position.X = value.AsFloat; break;
                case "m_vecOrigin[1]": position.Y = value.AsFloat; break;
                case "m_vecOrigin[2]": position.Z = value.AsFloat; break;
                case "m_vecPos": position = value.AsVector; break;
                case "m_vecDir": direction = value.AsVector; break;
                case "m_nMagnitude": magnitude = (int)value.AsInt; break;
                case "m_nTrailLength": trail = (int)value.AsInt; break;
                default: break;
            }
        }

        _sparks.Add(new SceneSpark(tick, kind, position, direction, magnitude, trail));

        return true;
    }
}
