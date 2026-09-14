using System;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Animation.Animating;

/// <summary>A surface's physics parameters — <c>surfacephysicsparams_t</c> (<c>public/vphysics_interface.h:882</c>).</summary>
/// <param name="Friction">The friction factor, entry <c>+0x14</c>.</param>
/// <param name="Elasticity">The elasticity, entry <c>+0x18</c>.</param>
/// <param name="Density">The density in kg/m³, entry <c>+0x1c</c>.</param>
/// <param name="Thickness">The thickness of a sheet material in inches, entry <c>+0x20</c>.</param>
/// <param name="Dampening">The dampening, entry <c>+0x24</c>.</param>
public readonly record struct SurfacePhysicsParams(float Friction, float Elasticity, float Density, float Thickness, float Dampening);

/// <summary>One of vphysics' surface entries, which is its own IVP material — vtable <c>1800ec528</c> (B369).</summary>
/// <param name="name">The surface's name.</param>
/// <param name="physics">Its parameters.</param>
/// <param name="hasSecondFriction">The dword at <c>+0xc</c>.</param>
public sealed class VphysicsSurface(string name, SurfacePhysicsParams physics, bool hasSecondFriction) : IIvpMaterial
{
    /// <summary>The surface's name, found through the symbol at <c>+0x10</c>.</summary>
    public string Name { get; } = name ?? throw new ArgumentNullException(nameof(name));

    /// <summary>The parameters at <c>+0x14..0x27</c>.</summary>
    public SurfacePhysicsParams Physics { get; } = physics;

    /// <summary>Slot 1, <c>FUN_180019360</c>: <c>(double)+0x14</c>.</summary>
    public double FrictionFactor => Physics.Friction;

    /// <summary>Slot 2, <c>FUN_180007e40</c>: zero.</summary>
    public double SecondFrictionFactor => 0d;

    /// <summary>Slot 3, <c>FUN_1800192d0</c>: <c>(double)+0x18</c>.</summary>
    public double Elasticity => Physics.Elasticity;

    /// <inheritdoc />
    public bool HasSecondFriction { get; } = hasSecondFriction;
}

/// <summary>
/// vphysics' surface properties as IVP's material manager uses them — the manager at <c>180120be8</c> (vtable <c>1800ec560</c>)
/// and the half of the surface-props object at <c>180120b38</c> that it calls (B369, D172).
/// </summary>
/// <remarks>
/// **Read from the disassembly** (`docs/findings/51`, *vphysics' material manager and its materials*). The manager is the props
/// object's `+0xb0`, so the 128-word table its slot 1 reads is the one `SetWorldMaterialIndexTable` writes. **Every friction and
/// elasticity a contact gets comes from here**: the product of the two surfaces' values, clamped to `[0, 1]`, unless vphysics'
/// friction override zeroes it.
/// </remarks>
public sealed class VphysicsSurfaceProps : IIvpMaterialManager
{
    /// <summary><c>MATERIAL_INDEX_SHADOW</c>: what <see cref="GetSurfaceIndex"/> answers for <c>$MATERIAL_INDEX_SHADOW</c>.</summary>
    public const int ShadowIndex = 0xf000;

    private const string ShadowName = "$MATERIAL_INDEX_SHADOW";

    /// <summary><c>DAT_1800ec610</c>: the surface a missing material falls back to.</summary>
    private const string DefaultName = "default";

    /// <summary>The world table's size and the largest index it maps — <c>CMP R9D, 0x7f</c>.</summary>
    private const int WorldTableSize = 128;

    /// <summary><c>DAT_1800eb144</c>: <c>1e-4f</c>, below which a normal's squared length is no direction to test.</summary>
    private const float LeastNormalSquared = 1e-4f;

    /// <summary><c>DAT_1800ee1a8</c>: about <c>sin 15°</c>, a float widened, past which a normal lies along the core's x.</summary>
    private static readonly double AxisShare = BitConverter.Int64BitsToDouble(0x3fd0902de0000000);

    /// <summary><c>DAT_1800ea9b8</c>.</summary>
    private const double One = 1d;

    private readonly List<VphysicsSurface> _surfaces = [];
    private readonly ushort[] _worldTable = new ushort[WorldTableSize];

    /// <summary>Holds the parsed surfaces, in parse order, and fills the world table with its own indices (<c>FUN_180001840</c>).</summary>
    /// <param name="surfaces">The surfaces.</param>
    /// <exception cref="ArgumentNullException"><paramref name="surfaces"/> is null.</exception>
    public VphysicsSurfaceProps(IEnumerable<VphysicsSurface> surfaces)
    {
        ArgumentNullException.ThrowIfNull(surfaces);

        _surfaces.AddRange(surfaces);

        for (int index = 0; index < WorldTableSize; index++)
        {
            _worldTable[index] = (ushort)index;
        }
    }

    /// <summary>The surfaces, in parse order — the vector at <c>+0x70</c>, its count at <c>+0x80</c>.</summary>
    public IReadOnlyList<VphysicsSurface> Surfaces => _surfaces;

    /// <summary>The surface <see cref="ShadowIndex"/> stands for — <c>props+0x1cc</c>.</summary>
    /// <remarks>*Its writer is not read.*</remarks>
    public int ShadowSurface { get; set; }

    /// <summary>A surface's index by name — slot 3, <c>FUN_180018500</c>.</summary>
    /// <param name="name">The name.</param>
    /// <returns><see cref="ShadowIndex"/> for <c>$MATERIAL_INDEX_SHADOW</c>; the first surface of that name; otherwise <c>−1</c>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is null.</exception>
    public int GetSurfaceIndex(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (name.Length > 0 && name[0] == '$' && string.Equals(name, ShadowName, StringComparison.OrdinalIgnoreCase))
        {
            return ShadowIndex;
        }

        return _surfaces.FindIndex(surface => string.Equals(surface.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>A surface's material by index — slot 10, <c>FUN_1800181d0</c>.</summary>
    /// <param name="index">The surface index.</param>
    /// <returns>The surface, or null for a negative index or one past the vector.</returns>
    /// <remarks>
    /// <code>
    /// index > 0x7f:  index = index == 0xf000 ? props+0x1cc : 0     -- any other large index is surface zero, unsigned-tested
    /// index &lt; 0 or index > count − 1:  null
    /// </code>
    /// </remarks>
    public VphysicsSurface? GetIVPMaterial(int index)
    {
        if (index > WorldTableSize - 1)
        {
            if (index != ShadowIndex)
            {
                return _surfaces.Count > 0 ? _surfaces[0] : null;
            }

            index = ShadowSurface;
        }

        return index < 0 || index > _surfaces.Count - 1 ? null : _surfaces[index];
    }

    /// <summary>Sets the world's material table — slot 8, <c>FUN_180019220</c>: <c>min(size, 128)</c> words of the map.</summary>
    /// <param name="map">The map's surface index for each triangle material index.</param>
    /// <exception cref="ArgumentNullException"><paramref name="map"/> is null.</exception>
    public void SetWorldMaterialIndexTable(IReadOnlyList<int> map)
    {
        ArgumentNullException.ThrowIfNull(map);

        int count = Math.Min(map.Count, WorldTableSize);

        for (int index = 0; index < count; index++)
        {
            _worldTable[index] = unchecked((ushort)map[index]);
        }
    }

    /// <summary>Parses one surface-properties text into these surfaces — slot 1, <c>ParseSurfaceData</c> (<c>FUN_180018740</c>).</summary>
    /// <param name="text">The file's bytes; its end reads as the engine's terminator.</param>
    /// <exception cref="InvalidOperationException">A block closes onto an index that names no surface, where the engine writes through null.</exception>
    public void ParseSurfaceData(ReadOnlySpan<byte> text) => VphysicsSurfaceData.Parse(this, text);

    /// <summary>Whether the shadow surface has been made — the byte at <c>props+0x1c8</c>.</summary>
    internal bool ShadowMade { get; set; }

    /// <summary>Appends a surface — <c>FUN_1800185d0</c>.</summary>
    internal void Add(VphysicsSurface surface) => _surfaces.Add(surface);

    /// <summary>Puts a surface in another's place, as a closing block writes over an entry's parameters.</summary>
    internal void Replace(VphysicsSurface target, VphysicsSurface surface) => _surfaces[_surfaces.IndexOf(target)] = surface;

    /// <inheritdoc />
    /// <remarks>
    /// Slot 1, <c>FUN_180019370</c>: an index up to <c>0x7f</c> goes through the world table, then <see cref="GetIVPMaterial"/>, and a
    /// null answer falls back to the <c>default</c> surface's.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is negative, where the engine reads before its table.</exception>
    /// <exception cref="InvalidOperationException">Neither the index nor <c>default</c> names a surface, where the engine answers null.</exception>
    public IIvpMaterial MaterialAt(IvpCollisionObject collisionObject, int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);

        int surface = index < WorldTableSize ? _worldTable[index] : index;

        return GetIVPMaterial(surface) ?? GetIVPMaterial(GetSurfaceIndex(DefaultName)) ??
            throw new InvalidOperationException("Neither the material index nor 'default' names a surface.");
    }

    /// <inheritdoc />
    /// <remarks>
    /// Slot 2, <c>FUN_1800192e0</c>: zero when <see cref="FrictionIsOverridden"/>; otherwise <c>p = m₀.slot1 · m₁.slot1</c>, zero when
    /// <c>p</c> is negative or NaN (<c>COMISD</c>/<c>JC</c>), else <c>MINSD</c> with one.
    /// </remarks>
    public double FrictionFactor(IvpContactRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        if (FrictionIsOverridden(record))
        {
            return 0d;
        }

        double product = MaterialOf(record.FirstMaterial).FrictionFactor * MaterialOf(record.SecondMaterial).FrictionFactor;

        if (!(product >= 0d))
        {
            return 0d;
        }

        return product < One ? product : One;
    }

    /// <inheritdoc />
    /// <remarks>Slot 3, <c>FUN_1800192b0</c>: <c>m₀.slot3 · m₁.slot3</c>, <c>MAXSD</c> with zero — a NaN gives zero — then <c>MINSD</c> with one.</remarks>
    public double Elasticity(IvpContactRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        double product = MaterialOf(record.FirstMaterial).Elasticity * MaterialOf(record.SecondMaterial).Elasticity;

        product = product > 0d ? product : 0d;

        return product < One ? product : One;
    }

    /// <summary>vphysics' friction override — <c>FUN_180024080</c>, which zeroes a pair's friction.</summary>
    /// <param name="record">The contact record, its objects written.</param>
    /// <returns>Whether the friction is zeroed.</returns>
    /// <remarks>
    /// <code>
    /// either object's core +0x58 set, else false
    /// o = the first object whose physics object has bit 6 of +0x48, then the second's;  none → false
    /// (n.x² + n.y²) + n.z² ≥ 1e-4f, else false                              -- COMISS/JC: NaN is false
    /// |(double)n turned into o's core frame (FUN_180070620).x| > sin 15°      -- COMISD/JBE: NaN is false
    /// </code>
    /// </remarks>
    internal static bool FrictionIsOverridden(IvpContactRecord record)
    {
        IvpCollisionObject first = record.FirstObject ?? throw new InvalidOperationException("The record's objects are not written.");
        IvpCollisionObject second = record.SecondObject ?? throw new InvalidOperationException("The record's objects are not written.");

        if (!CoreOf(first).HasOffset58 && !CoreOf(second).HasOffset58)
        {
            return false;
        }

        IvpCollisionObject? flagged = first.PhysicsFlag48Bit6 ? first : null;

        if (flagged is null && second.PhysicsFlag48Bit6)
        {
            flagged = second;
        }

        if (flagged is null)
        {
            return false;
        }

        (float X, float Y, float Z) normal = record.Normal;
        float squared = (normal.X * normal.X) + (normal.Y * normal.Y) + (normal.Z * normal.Z);

        if (!(squared >= LeastNormalSquared))
        {
            return false;
        }

        return Math.Abs((double)CoreOf(flagged).CoreMatrix.RotateInverseNarrowed(normal).X) > AxisShare;
    }

    private static IIvpMaterial MaterialOf(IIvpMaterial? material) =>
        material ?? throw new InvalidOperationException("The record's materials are not written.");

    private static IvpRigidBody CoreOf(IvpCollisionObject collisionObject) =>
        collisionObject.Core ?? throw new InvalidOperationException("The record's object has no core.");
}
