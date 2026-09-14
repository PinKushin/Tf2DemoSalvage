namespace Tf2DemoSalvage.Animation.Animating;

/// <summary>An IVP material, as the contact routines call it (B369).</summary>
/// <remarks>
/// **Read from its call sites, not its implementation**: `FUN_18008fe70` calls slots 1 and 2 of each synapse's material and tests
/// the dword at `+0xc`. *Which vphysics class implements it, and what its slots return, is not read yet.*
/// </remarks>
public interface IIvpMaterial
{
    /// <summary>Slot 1, a double: the factor the other side's axis friction is multiplied by.</summary>
    public double FrictionFactor { get; }

    /// <summary>Slot 2, a double: this material's factor along its object's first axis.</summary>
    public double SecondFrictionFactor { get; }

    /// <summary>Whether the dword at <c>+0xc</c> is nonzero — whether this material has a friction along its object's first axis.</summary>
    public bool HasSecondFriction { get; }
}

/// <summary>The environment's material manager, <c>env+0xe8</c>, as the contact routines call it (B369).</summary>
/// <remarks>
/// **Read from its call sites, not its implementation** (`FUN_1800908d0`, `FUN_18008fe70`). *Which vphysics class implements it,
/// and how its slots combine two materials, is not read yet.*
/// </remarks>
public interface IIvpMaterialManager
{
    /// <summary>Slot 1: the material a triangle's material index names — called with the object, a null position and the index.</summary>
    /// <param name="collisionObject">The object the triangle belongs to.</param>
    /// <param name="index">The triangle's material index, never zero here.</param>
    /// <returns>The material.</returns>
    public IIvpMaterial MaterialAt(IvpCollisionObject collisionObject, int index);

    /// <summary>Slot 2: the pair's friction factor, narrowed into the contact point's <c>+0x78</c>.</summary>
    /// <param name="record">The contact record, its objects and materials already written.</param>
    /// <returns>The friction factor.</returns>
    public double FrictionFactor(IvpContactRecord record);

    /// <summary>Slot 3: the pair's elasticity, narrowed into the record's <c>+0x80</c>.</summary>
    /// <param name="record">The contact record, its objects and materials already written.</param>
    /// <returns>The elasticity.</returns>
    public double Elasticity(IvpContactRecord record);
}
