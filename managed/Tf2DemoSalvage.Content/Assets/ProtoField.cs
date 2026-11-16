using System;

namespace Tf2DemoSalvage.Content.Assets;

/// <summary>One protobuf wire field of a <see cref="ProtoMessage"/>.</summary>
/// <param name="Number">The field number.</param>
/// <param name="Wire">The wire type.</param>
/// <param name="Value">A varint's value; a fixed field's bits; a length-delimited field's length.</param>
/// <param name="Payload">A length-delimited field's bytes; empty otherwise.</param>
public readonly record struct ProtoField(int Number, int Wire, ulong Value, ReadOnlyMemory<byte> Payload);
