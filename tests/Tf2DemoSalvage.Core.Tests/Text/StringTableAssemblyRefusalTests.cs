using System;
using System.IO;

using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Primitives;
using Tf2DemoSalvage.Core.Text;

namespace Tf2DemoSalvage.Core.Tests.Text;

/// <summary>
/// What <see cref="StringTableAssembly"/> refuses in a hand-edited table line (B345).
/// </summary>
/// <remarks>
/// **This file had the same split as the other four.** It states the contract at one site —
/// `A string table line has no '{name}' field.` — and read three payloads with a bare
/// `Convert.FromHexString` and one field with a raw `fields["userbytes"]`, each raising a type
/// `DemoAssembly.Parse` does not catch and so cannot attach the offending line to.
///
/// **A string table is the biggest thing in a demo's signon**, so it is also the block somebody
/// editing a trace is most likely to be truncating or splicing — which is exactly when the message
/// has to say which line.
/// </remarks>
public sealed class StringTableAssemblyRefusalTests
{
    private const ushort Protocol = 24;

    /// <remarks>
    /// **The table's own payload**, which is the whole body carried as hex when the entries were
    /// not promoted to readable lines.
    /// </remarks>
    [Test]
    public void Assemble_ATablePayloadThatIsNotHexadecimal_IsRefused()
    {
        Refuse(
            "svc_createstringtable \"userinfo\" max=64 count=1 bits=8 userbytes=- userbits=0 " +
            "compressed=0 payload zz")
            .ShouldContain("zz", Case.Sensitive);
    }

    /// <remarks>
    /// **`userbytes` carries `-` for "no fixed size", so it could not go through the numeric
    /// helper** — and that exemption took the missing-field refusal with it, leaving a raw
    /// `KeyNotFoundException` naming the key and nothing else.
    /// </remarks>
    [Test]
    public void Assemble_AHeaderWithNoUserBytesField_NamesTheField()
    {
        Refuse(
            "svc_createstringtable \"userinfo\" max=64 count=1 bits=8 userbits=0 " +
            "compressed=0 payload 00")
            .ShouldContain("userbytes");
    }

    /// <remarks>An entry's own user data, which is a separate hex read from the table payload.</remarks>
    [Test]
    public void Assemble_AnEntryDataFieldThatIsNotHexadecimal_IsRefused()
    {
        Refuse(
            "svc_createstringtable \"userinfo\" max=64 count=1 bits=8 userbytes=- userbits=0 " +
            "compressed=0 {",
            "  entry index=0 follows=0 hist=0 copy=0 text=\"a\" data=zz",
            "}")
            .ShouldContain("zz", Case.Sensitive);
    }

    /// <remarks>
    /// **The control.** The same header with a valid payload must still assemble, so a refusal that
    /// rejected every table — rather than the malformed value in it — would fail here.
    /// </remarks>
    [Test]
    public void Assemble_AWellFormedTable_StillAssembles()
    {
        Should.NotThrow(
            () => Assemble(
                "svc_createstringtable \"userinfo\" max=64 count=1 bits=8 userbytes=- " +
                "userbits=0 compressed=0 payload 00"));
    }

    /// <summary>The refusal the given lines produce.</summary>
    private static string Refuse(string line, params string[] rest) =>
        Should.Throw<InvalidDataException>(() => Assemble(line, rest)).Message;

    /// <summary>Assembles one message, with the given lines beneath it.</summary>
    private static void Assemble(string line, params string[] rest)
    {
        int next = 0;

        MessageAssembly.Assemble(
            line,
            () => next < rest.Length ? rest[next++] : null,
            new BitWriter(),
            new NetDecodeState { NetworkProtocol = Protocol });
    }
}
