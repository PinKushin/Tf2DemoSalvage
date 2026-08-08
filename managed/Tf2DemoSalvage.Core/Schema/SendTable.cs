using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Tf2DemoSalvage.Core.Schema;

/// <summary>
/// Property types a SendProp can hold. Five bits on the wire; values are the SDK's
/// <c>SendPropType</c> enum order.
/// </summary>
[SuppressMessage("Design", "CA1028:Enum storage should be Int32",
    Justification = "byte matches the on-disk field, which is 5 bits wide.")]
[SuppressMessage("Design", "CA1008:Enums should have zero value",
    Justification = "Int is genuinely 0 on the wire; a None member would be a fiction.")]
[SuppressMessage("Naming", "CA1720:Identifier contains type name",
    Justification = "Int, Float and String are Valve's own names for these wire types.")]
public enum SendPropType : byte
{
    /// <summary>Integer, width given by the property's bit count.</summary>
    Int = 0,

    /// <summary>Float, possibly range-encoded or coordinate-compressed.</summary>
    Float = 1,

    /// <summary>Three floats.</summary>
    Vector = 2,

    /// <summary>Two floats; the third is derived.</summary>
    VectorXY = 3,

    /// <summary>Length-prefixed string.</summary>
    String = 4,

    /// <summary>Repeats another property a fixed number of times.</summary>
    Array = 5,

    /// <summary>Nests another table, which is how inheritance is expressed.</summary>
    DataTable = 6,
}

/// <summary>One networked property definition.</summary>
/// <param name="Type">How the value is encoded.</param>
/// <param name="Name">Property name, e.g. <c>m_iHealth</c>.</param>
/// <param name="Flags">The 16 networked <c>SPROP_</c> flags.</param>
/// <param name="ReferencedTable">
/// The nested table for a <see cref="SendPropType.DataTable"/> property, or the table an
/// exclusion removes from. Empty otherwise.
/// </param>
/// <param name="LowValue">Range minimum, for range-encoded numerics.</param>
/// <param name="HighValue">Range maximum, for range-encoded numerics.</param>
/// <param name="BitCount">Width of the encoded value, where one applies.</param>
/// <param name="ElementCount">Element count for an <see cref="SendPropType.Array"/>.</param>
public readonly record struct SendProperty(
    SendPropType Type,
    string Name,
    int Flags,
    string ReferencedTable,
    float LowValue,
    float HighValue,
    int BitCount,
    int ElementCount)
{
    /// <summary>Flag marking a property that removes an inherited one.</summary>
    public const int ExcludeFlag = 1 << 6;

    /// <summary>Flag that reorders the flattened property list. See <c>RISKS.md</c> B4.</summary>
    public const int ChangesOftenFlag = 1 << 10;

    /// <summary>Whether this entry removes an inherited property rather than adding one.</summary>
    public bool IsExcluded => (Flags & ExcludeFlag) != 0;

    /// <summary>Whether this property is sorted forward when the list is flattened.</summary>
    public bool ChangesOften => (Flags & ChangesOftenFlag) != 0;
}

/// <summary>One SendTable: a named, ordered list of properties.</summary>
/// <param name="Name">Table name, e.g. <c>DT_TFPlayer</c>.</param>
/// <param name="NeedsDecoder">Whether the server flagged this table as needing a decoder.</param>
/// <param name="Properties">Properties in wire order. The order is part of the contract.</param>
public sealed record SendTable(
    string Name,
    bool NeedsDecoder,
    IReadOnlyList<SendProperty> Properties);
