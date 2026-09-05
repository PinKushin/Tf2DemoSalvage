using System;
using System.IO;

using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Primitives;
using Tf2DemoSalvage.Core.Text;

namespace Tf2DemoSalvage.Core.Tests.Text;

/// <summary>
/// What <see cref="MessageAssembly.Assemble"/> refuses, and what it says when it does (B344).
/// </summary>
/// <remarks>
/// **These are the branches a corpus can never reach**, which is why they were the ones left
/// uncovered. Every demo in it is a valid recording, so the round-trip suites — which render a
/// message and parse the rendering back — only ever hand this well-formed text. The refusals are
/// reached by a HAND-EDITED trace, which is the thing this text form exists to allow: the whole
/// point of a readable dump is that somebody can change a line and reassemble it.
///
/// `docs/memory/most-of-a-decoder-is-untested.md` is about exactly this shape — real files take one
/// path, and the branches that matter are the ones they never take.
///
/// **The MESSAGE is asserted, not just the throw.** An error about malformed input has to carry the
/// input, and the file's own comment says so: *"state what was measured, not only that something
/// was wrong"*. A test that only checked the exception type would pass against an error that named
/// nothing, which is the difference between a person fixing their line and a person guessing.
/// </remarks>
public sealed class MessageAssemblyRefusalTests
{
    /// <summary>The protocol the other assembly tests use.</summary>
    private const int Protocol = 24;

    [Test]
    public void Assemble_AnEmptyLine_IsRefused()
    {
        Should.Throw<InvalidDataException>(() => Assemble(string.Empty))
            .Message.ShouldContain("empty line");
    }

    /// <remarks>
    /// **Whitespace is not a message either**, and it is the case an emptiness check written as
    /// `line.Length == 0` would let through — `Tokenize` returns nothing for it, which is what the
    /// guard actually tests.
    /// </remarks>
    [Test]
    public void Assemble_ALineOfOnlyWhitespace_IsRefused()
    {
        Should.Throw<InvalidDataException>(() => Assemble("   \t  "))
            .Message.ShouldContain("empty line");
    }

    /// <remarks>
    /// **The unknown name AND the whole line**, because a person hand-editing a trace has typed
    /// something and needs to see what was read — a message named `svc_setpuase` is a typo whose
    /// fix is obvious once quoted and invisible otherwise.
    /// </remarks>
    [Test]
    public void Assemble_AnUnknownMessageName_IsRefusedAndQuotesIt()
    {
        InvalidDataException failure =
            Should.Throw<InvalidDataException>(() => Assemble("svc_setpuase 1"));

        failure.Message.ShouldContain("svc_setpuase", Case.Sensitive);
        failure.Message.ShouldContain("Unknown message");
    }

    /// <remarks>
    /// **An entity block needs a SCHEMA, and saying which is the useful half.** Reassembling a
    /// trace whose `dem_datatables` was deleted — or reordered after the packets — reaches this,
    /// and the message names the command that is missing rather than reporting a parse error.
    /// </remarks>
    [Test]
    public void Assemble_PacketEntitiesWithNoSchema_NamesTheMissingDataTables()
    {
        InvalidDataException failure = Should.Throw<InvalidDataException>(
            () => Assemble("svc_packetentities delta -1 baseline 0 updated 1 max 64 {"));

        failure.Message.ShouldContain("dem_datatables");
    }

    /// <remarks>
    /// The same for temp entities, which take a different route to the same requirement — and a
    /// test on one alone would leave the other's guard unexercised.
    /// </remarks>
    [Test]
    public void Assemble_TempEntitiesWithNoSchema_NamesTheMissingDataTables()
    {
        InvalidDataException failure = Should.Throw<InvalidDataException>(
            () => Assemble("svc_tempentities count 1 {"));

        failure.Message.ShouldContain("dem_datatables");
    }

    /// <remarks>
    /// **A block that runs off the end of the file**, which is what a truncated trace or a lost
    /// closing brace gives. Without this guard the reader asks for line after line and gets null
    /// for ever.
    /// </remarks>
    [Test]
    public void Assemble_ABlockNeverClosed_IsRefusedRatherThanReadingForEver()
    {
        Should.Throw<InvalidDataException>(
            () => Assemble("svc_sounds reliable 0 count 1 {", () => null))
            .Message.ShouldContain("'}'");
    }

    /// <summary>Assembles one line, with no lines after it unless given.</summary>
    private static void Assemble(string line, Func<string?>? nextLine = null) =>
        MessageAssembly.Assemble(
            line,
            nextLine ?? (static () => null),
            new BitWriter(),
            new NetDecodeState { NetworkProtocol = Protocol });
}
