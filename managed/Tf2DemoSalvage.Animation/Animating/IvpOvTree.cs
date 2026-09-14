using System;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Animation.Animating;

/// <summary>An object's sphere in the OV tree — the 0x50-byte node <c>FUN_18009d7b0</c> builds at <c>object+0xd8</c> (B369).</summary>
public sealed class IvpOvNode
{
    /// <summary>The sphere's centre, <c>+0x20</c> — the broad phase writes the core's extrapolated position narrowed.</summary>
    public (float X, float Y, float Z) Center { get; set; }

    /// <summary>The radius the node was filed with, <c>+0x30</c>; <c>−1</c> until it is filed.</summary>
    public float Radius { get; internal set; } = -1f;

    /// <summary>The cell holding it, <c>+0x10</c>, or null.</summary>
    public IvpOvCell? Cell { get; internal set; }
}

/// <summary>A cell of the OV tree: an integer key at a level, its parent, its child cells and its nodes (B369).</summary>
/// <remarks>
/// The 0x40 bytes `FUN_18009ecb0` and its helpers build: the key at <c>+0x0</c> (<c>x</c>, <c>y</c>, <c>z</c>, the level
/// <c>+0xc</c> and the exponent <c>+0x10</c>, one above it), the parent at <c>+0x18</c>, the children at <c>+0x20</c> and the
/// nodes at <c>+0x30</c>. A cell at level <c>L</c> spans <c>x·2^L</c> to <c>(x + 3)·2^L</c>: cells overlap by design.
/// </remarks>
public sealed class IvpOvCell
{
    internal IvpOvCell(int x, int y, int z, int level, int exponent)
    {
        X = x;
        Y = y;
        Z = z;
        Level = level;
        Exponent = exponent;
    }

    /// <summary>The key's <c>x</c>, <c>+0x0</c>.</summary>
    public int X { get; }

    /// <summary>The key's <c>y</c>, <c>+0x4</c>.</summary>
    public int Y { get; }

    /// <summary>The key's <c>z</c>, <c>+0x8</c>.</summary>
    public int Z { get; }

    /// <summary>The level, <c>+0xc</c> — what the cell hash compares with <c>x</c>, <c>y</c> and <c>z</c>.</summary>
    public int Level { get; }

    /// <summary>The exponent, <c>+0x10</c>: the level plus one, never compared.</summary>
    public int Exponent { get; }

    /// <summary>The parent cell, <c>+0x18</c>.</summary>
    public IvpOvCell? Parent { get; internal set; }

    /// <summary>The child cells, <c>+0x20</c>, in the order they were added less the removals.</summary>
    public IReadOnlyList<IvpOvCell> Children => ChildList;

    /// <summary>The nodes filed here, <c>+0x30</c>.</summary>
    public IReadOnlyList<IvpOvNode> Nodes => NodeList;

    internal List<IvpOvCell> ChildList { get; } = [];

    internal List<IvpOvNode> NodeList { get; } = [];

    internal (int X, int Y, int Z, int Level) Key => (X, Y, Z, Level);
}

/// <summary>IVP's OV tree — the environment's <c>+0x28</c>, the sphere hierarchy the broad phase files every object in (B369).</summary>
/// <remarks>
/// **Read from the disassembly** (`docs/findings/51`, *The broad phase*): the insert `FUN_18009ecb0` with its key
/// `FUN_18009df30`, growth `FUN_18009e920`, descent `FUN_18009eb20` and path `FUN_18009e730`; the overlap walks `FUN_18009e3d0`
/// and `FUN_18009e630`; and the removal `FUN_18009efc0`. The cell hash (`FUN_1800b5c10`, `FUN_180071f90`, `FUN_1800722b0`,
/// `FUN_180072570`) is a lookup by <c>(x, y, z, level)</c>, which nothing iterates, so a dictionary answers it the same. Pinned by
/// the `vphysics-ov-tree` probe (`IvpOvTreeConformanceTests`).
/// </remarks>
public sealed class IvpOvTree
{
    /// <summary>The key's lowest exponent, <c>CMOVL</c> against <c>−40</c>.</summary>
    private const int LowestExponent = -40;

    private readonly Dictionary<(int X, int Y, int Z, int Level), IvpOvCell> _cells = [];

    /// <summary>The root cell, <c>+0x58</c>, or null for an empty tree.</summary>
    public IvpOvCell? Root { get; private set; }

    /// <summary>How many cells the hash holds.</summary>
    public int CellCount => _cells.Count;

    /// <summary>Files a node — <c>FUN_18009ecb0</c>.</summary>
    /// <param name="node">The node, its centre set; null answers zero.</param>
    /// <param name="radius">The radius to file it with.</param>
    /// <param name="outerRadius">A larger radius taken instead when it fits one level up.</param>
    /// <param name="overlapping">Where the nodes within the two radii go, or null to find none.</param>
    /// <returns>The radius the node was filed with.</returns>
    /// <exception cref="InvalidOperationException">A key would read past the level table, as only a non-finite or vast radius does.</exception>
    /// <remarks>
    /// <code>
    /// key = FUN_18009df30;  node+0x30 = (float)its radius;  the cell by key — found, the node joins it
    /// else a new cell with the node:  no root → it is the root, hashed, and nothing is searched
    ///     the root grown until it holds the cell (level at least the cell's, and x·2^d ≤ cx ≤ x·2^d + 2^(d+1) − 2 per axis)
    ///     the same level → the node joins the root and the new cell is dropped
    ///     else the cell hashed, hung under its deepest containing cell through new cells a level at a time
    /// a list → FUN_18009e3d0(node, root, the node's cell)
    /// </code>
    /// </remarks>
    public double Insert(IvpOvNode? node, double radius, double outerRadius, ICollection<IvpOvNode>? overlapping)
    {
        if (node is null)
        {
            return 0d;
        }

        ((int X, int Y, int Z, int Level, int Exponent) key, double used) = CellKey(node.Center, radius, outerRadius);
        node.Radius = (float)used;

        if (!_cells.TryGetValue((key.X, key.Y, key.Z, key.Level), out IvpOvCell? cell))
        {
            cell = new IvpOvCell(key.X, key.Y, key.Z, key.Level, key.Exponent);
            cell.NodeList.Add(node);
            node.Cell = cell;

            if (Root is null)
            {
                Root = cell;
                _cells.Add(cell.Key, cell);

                return used;
            }

            while (Root.Level < cell.Level || !Contains(Root, cell, Root.Level - cell.Level))
            {
                Grow(cell);
            }

            if (Root.Level == cell.Level)
            {
                Root.NodeList.Add(node);
                node.Cell = Root;
                cell = Root;
            }
            else
            {
                _cells.Add(cell.Key, cell);
                CreatePath(Descend(Root, cell), cell);
            }
        }
        else
        {
            cell.NodeList.Add(node);
            node.Cell = cell;
        }

        if (overlapping is not null)
        {
            Collect(node, Root!, cell, overlapping);
        }

        return used;
    }

    /// <summary>Takes a node out — <c>FUN_18009efc0</c>.</summary>
    /// <param name="node">The node; one filed nowhere is left alone.</param>
    /// <exception cref="ArgumentNullException"><paramref name="node"/> is null.</exception>
    /// <exception cref="InvalidOperationException">The node or a cell is missing from where it is filed, where the engine would corrupt its lists.</exception>
    /// <remarks>
    /// Out of its cell's nodes, the last match; then while the cell holds no nodes and no children: the root cleared when it has no
    /// parent, the cell out of the hash and out of its parent's children, and the parent taken next.
    /// </remarks>
    public void Remove(IvpOvNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        IvpOvCell? cell = node.Cell;

        if (cell is null)
        {
            return;
        }

        node.Cell = null;
        RemoveLast(cell.NodeList, node);

        while (cell is not null && cell.NodeList.Count == 0 && cell.ChildList.Count == 0)
        {
            if (cell.Parent is null)
            {
                Root = null;
            }

            if (!_cells.Remove(cell.Key))
            {
                throw new InvalidOperationException("A cell missing from the hash is removed, where the engine asserts.");
            }

            IvpOvCell? parent = cell.Parent;

            if (parent is not null)
            {
                RemoveLast(parent.ChildList, cell);
            }

            cell = parent;
        }
    }

    /// <summary>A sphere's cell key and the radius it is filed with — <c>FUN_18009df30</c>.</summary>
    /// <param name="center">The centre.</param>
    /// <param name="radius">The radius.</param>
    /// <param name="outerRadius">A larger radius tried one level up.</param>
    /// <returns>The key and the radius used.</returns>
    /// <exception cref="InvalidOperationException">The level walk would read past the level table.</exception>
    /// <remarks>
    /// <code>
    /// e = exponent(r + r) + 1, at least −40;  scale = 2^(1 − e)
    /// per axis lo = floor((float)((c − r)·scale)), hi = ceil((float)((c + r)·scale)) — CVTTSS2SI and a sign correction —
    ///     until hi ≤ lo + 2 on every axis, e rising;  key = (lo, e − 1, e)
    /// !(r ≥ r₂) → the same with r₂ at scale 2^(−e):  every axis fits → key = (lo₂, e, e + 1), r₂ used;  else r
    /// </code>
    /// **`y`'s high edge adds the centre to the radius, the others the radius to the centre**, visible only to a pair of NaNs.
    /// </remarks>
    internal static ((int X, int Y, int Z, int Level, int Exponent) Key, double Radius) CellKey(
        (float X, float Y, float Z) center, double radius, double outerRadius)
    {
        double x = center.X;
        double y = center.Y;
        double z = center.Z;
        int exponent = Math.Max((int)((BitConverter.DoubleToInt64Bits(IvpMath.Addsd(radius, radius)) >> 52) & 0x7ff) - 0x3fe, LowestExponent);
        double lowX = x - radius;
        double highX = IvpMath.Addsd(x, radius);
        double lowY = y - radius;
        double highY = IvpMath.Addsd(radius, y);
        double lowZ = z - radius;
        double highZ = IvpMath.Addsd(z, radius);
        int keyX;
        int keyY;
        int keyZ;

        while (true)
        {
            double scale = LevelTable(41 - exponent);

            keyX = Floor(IvpMath.Mulsd(lowX, scale));
            keyY = Floor(IvpMath.Mulsd(lowY, scale));
            keyZ = Floor(IvpMath.Mulsd(lowZ, scale));

            if (Fits(keyX, Ceiling(IvpMath.Mulsd(highX, scale))) && Fits(keyY, Ceiling(IvpMath.Mulsd(highY, scale))) &&
                Fits(keyZ, Ceiling(IvpMath.Mulsd(highZ, scale))))
            {
                break;
            }

            exponent++;
        }

        if (radius >= outerRadius)
        {
            return ((keyX, keyY, keyZ, exponent - 1, exponent), radius);
        }

        double outerScale = LevelTable(40 - exponent);
        int outerX = Floor(IvpMath.Mulsd(x - outerRadius, outerScale));
        int outerY = Floor(IvpMath.Mulsd(y - outerRadius, outerScale));
        int outerZ = Floor(IvpMath.Mulsd(z - outerRadius, outerScale));

        if (Fits(outerX, Ceiling(IvpMath.Mulsd(IvpMath.Addsd(x, outerRadius), outerScale))) &&
            Fits(outerY, Ceiling(IvpMath.Mulsd(IvpMath.Addsd(y, outerRadius), outerScale))) &&
            Fits(outerZ, Ceiling(IvpMath.Mulsd(IvpMath.Addsd(z, outerRadius), outerScale))))
        {
            return ((outerX, outerY, outerZ, exponent, exponent + 1), outerRadius);
        }

        return ((keyX, keyY, keyZ, exponent - 1, exponent), radius);
    }

    /// <summary>The level table, <c>18012d680[index]</c>: <c>2^(index − 40)</c>, and the zeros either side of it.</summary>
    private static double LevelTable(int index) => index switch
    {
        >= 0 and <= 80 => Math.ScaleB(1d, index - 40),
        81 or -1 => 0d,
        _ => throw new InvalidOperationException($"The OV tree would read its level table at index {index}, outside what vphysics.dll fills."),
    };

    private static bool Fits(int low, int high) => high <= unchecked(low + 2);

    /// <summary><c>CVTTSS2SI</c> of the narrowed value, corrected down for a negative fraction, truncated again.</summary>
    private static int Floor(double value)
    {
        float single = (float)value;
        int truncated = Truncate(single);

        if (truncated != int.MinValue && HasFraction(single, truncated))
        {
            truncated -=BitConverter.SingleToInt32Bits(single) < 0 ? 1 : 0;

            return Truncate(truncated);
        }

        return truncated;
    }

    /// <summary><c>CVTTSS2SI</c> of the narrowed value, corrected up for a positive fraction, truncated again.</summary>
    private static int Ceiling(double value)
    {
        float single = (float)value;
        int truncated = Truncate(single);

        if (truncated != int.MinValue && HasFraction(single, truncated))
        {
            truncated += BitConverter.SingleToInt32Bits(single) < 0 ? 0 : 1;

            return Truncate(truncated);
        }

        return truncated;
    }

    /// <summary>Whether a finite value and its truncation differ, <c>UCOMISS</c> and <c>JNE</c>: a zero difference only when equal.</summary>
    private static bool HasFraction(float single, int truncated) => Math.Abs(single - truncated) > 0f;

    /// <summary><c>CVTTSS2SI</c>: truncation, and <c>int.MinValue</c> for a NaN or anything an int cannot hold.</summary>
    private static int Truncate(float value) => value is >= -2147483648f and < 2147483648f ? (int)value : int.MinValue;

    /// <summary>Whether a cell <paramref name="shift"/> levels up holds another: <c>x·2^d ≤ ix ≤ x·2^d + 2^(d+1) − 2</c> per axis.</summary>
    private static bool Contains(IvpOvCell outer, IvpOvCell inner, int shift) =>
        inner.X >= outer.X << shift && inner.Y >= outer.Y << shift && inner.Z >= outer.Z << shift &&
        inner.X <= Span(outer.X, shift) && inner.Y <= Span(outer.Y, shift) && inner.Z <= Span(outer.Z, shift);

    private static int Span(int coordinate, int shift) => unchecked((coordinate << shift) - 2 + (2 << shift));

    /// <summary>A parent grown over the root — <c>FUN_18009e920</c>.</summary>
    /// <remarks>
    /// Coordinates halved toward zero; one that was odd and negative is taken down one, and one that was even is taken down one
    /// when the inserted cell's world coordinate, <c>x·2^level</c>, lies below the parent's, <c>x·2^(level+1)</c>.
    /// </remarks>
    private void Grow(IvpOvCell inserted)
    {
        IvpOvCell root = Root!;
        double parentScale = LevelTable(41 + root.Level);
        double cellScale = LevelTable(40 + inserted.Level);
        IvpOvCell parent = new(
            Halve(root.X, inserted.X, cellScale, parentScale),
            Halve(root.Y, inserted.Y, cellScale, parentScale),
            Halve(root.Z, inserted.Z, cellScale, parentScale),
            root.Level + 1,
            root.Exponent + 1);

        parent.ChildList.Add(root);
        root.Parent = parent;
        _cells.Add(parent.Key, parent);
        Root = parent;
    }

    private static int Halve(int coordinate, int inserted, double cellScale, double parentScale)
    {
        int half = coordinate / 2;
        int remainder = coordinate % 2;

        // COMISD and JNC over integers times powers of two, which are never NaN.
        if (remainder == -1 || (remainder == 0 && IvpMath.Mulsd(inserted, cellScale) < IvpMath.Mulsd(half, parentScale)))
        {
            return half - 1;
        }

        return half;
    }

    /// <summary>The deepest existing cell holding a target, from a cell down — <c>FUN_18009eb20</c>.</summary>
    private static IvpOvCell Descend(IvpOvCell from, IvpOvCell target)
    {
        IvpOvCell current = from;
        bool descended = true;

        while (descended)
        {
            descended = false;
            int shift = current.Level - target.Level - 1;

            foreach (IvpOvCell child in current.ChildList)
            {
                if (Contains(child, target, shift))
                {
                    current = child;
                    descended = true;
                    break;
                }
            }
        }

        return current;
    }

    /// <summary>New cells from a parent down to a target, a level at a time, the target hung under the last — <c>FUN_18009e730</c>.</summary>
    private void CreatePath(IvpOvCell parent, IvpOvCell target)
    {
        int gap = parent.Exponent - target.Exponent;

        while (gap != 1)
        {
            IvpOvCell step = new(
                ChildCoordinate(parent.X, target.X, gap),
                ChildCoordinate(parent.Y, target.Y, gap),
                ChildCoordinate(parent.Z, target.Z, gap),
                parent.Level - 1,
                parent.Exponent - 1)
            {
                Parent = parent,
            };

            parent.ChildList.Add(step);
            _cells.Add(step.Key, step);
            gap = step.Exponent - target.Exponent;
            parent = step;
        }

        target.Parent = parent;
        parent.ChildList.Add(target);
    }

    private static int ChildCoordinate(int parent, int target, int gap)
    {
        int doubled = unchecked(parent + parent);

        if (target >= unchecked((parent + 1) << gap))
        {
            return unchecked(doubled + 2);
        }

        return target >= unchecked((doubled + 1) << (gap - 1)) ? unchecked(doubled + 1) : doubled;
    }

    /// <summary>Every node within the two radii, from a cell down toward a target — <c>FUN_18009e3d0</c>.</summary>
    private static void Collect(IvpOvNode node, IvpOvCell cell, IvpOvCell target, ICollection<IvpOvNode> found)
    {
        Near(node, cell, found);

        bool above = cell.Exponent - 1 > target.Exponent;
        int shift = above ? cell.Level - target.Level - 1 : target.Level - cell.Level + 1;

        for (int index = 0; index < cell.ChildList.Count; index++)
        {
            IvpOvCell child = cell.ChildList[index];

            if (ReferenceEquals(child, target))
            {
                CollectAll(node, child, found);
            }
            else if (above ? Overlaps(target, child, shift) : Overlaps(child, target, shift))
            {
                Collect(node, child, target, found);
            }
        }
    }

    /// <summary>Whether a finer cell and a coarser one <paramref name="shift"/> levels up overlap: strictly, per axis.</summary>
    private static bool Overlaps(IvpOvCell fine, IvpOvCell coarse, int shift) =>
        unchecked(fine.X + 2) > coarse.X << shift && unchecked(fine.Y + 2) > coarse.Y << shift && unchecked(fine.Z + 2) > coarse.Z << shift &&
        fine.X < unchecked(coarse.X + 2) << shift && fine.Y < unchecked(coarse.Y + 2) << shift && fine.Z < unchecked(coarse.Z + 2) << shift;

    /// <summary>Every node within the two radii in a cell's whole subtree, its children last first — <c>FUN_18009e630</c>.</summary>
    private static void CollectAll(IvpOvNode node, IvpOvCell cell, ICollection<IvpOvNode> found)
    {
        Near(node, cell, found);

        for (int index = cell.ChildList.Count - 1; index >= 0; index--)
        {
            CollectAll(node, cell.ChildList[index], found);
        }
    }

    /// <summary>A cell's nodes, last first, whose centres lie within the two radii: <c>((Δy² + Δx²) + Δz²) ≤ (r + r')²</c>.</summary>
    private static void Near(IvpOvNode node, IvpOvCell cell, ICollection<IvpOvNode> found)
    {
        for (int index = cell.NodeList.Count - 1; index >= 0; index--)
        {
            IvpOvNode other = cell.NodeList[index];
            double x = other.Center.X - node.Center.X;
            double y = other.Center.Y - node.Center.Y;
            double z = other.Center.Z - node.Center.Z;
            double sum = IvpMath.Addss(other.Radius, node.Radius);
            double distance = IvpMath.Addsd(IvpMath.Addsd(IvpMath.Mulsd(y, y), IvpMath.Mulsd(x, x)), IvpMath.Mulsd(z, z));

            if (distance > IvpMath.Mulsd(sum, sum))
            {
                continue;
            }

            found.Add(other);
        }
    }

    private static void RemoveLast<T>(List<T> list, T item)
        where T : class
    {
        int index = list.FindLastIndex(candidate => ReferenceEquals(candidate, item));

        if (index < 0)
        {
            throw new InvalidOperationException("An OV tree entry missing from its list is removed, where the engine would corrupt the list.");
        }

        list.RemoveAt(index);
    }
}
