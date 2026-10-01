using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Core.Container;

namespace Tf2DemoSalvage.Core.Text;

/// <summary>What <see cref="DemoAssembly.Parse"/> compiles: everything <see cref="DemoWriter.Write"/> takes.</summary>
/// <param name="Header">The demo's header, field for field as the text states it.</param>
/// <param name="Commands">The commands, in stream order.</param>
/// <param name="Tail">The bytes after the last whole command of a cut file; empty otherwise (B448).</param>
public sealed record AssembledDemo(
    DemoHeader Header, IReadOnlyList<DemoCommand> Commands, ReadOnlyMemory<byte> Tail)
{
    /// <summary>The header and commands alone, for a caller that never meets a cut file.</summary>
    /// <param name="header">The header.</param>
    /// <param name="commands">The commands.</param>
    public void Deconstruct(out DemoHeader header, out IReadOnlyList<DemoCommand> commands)
    {
        header = Header;
        commands = Commands;
    }
}
