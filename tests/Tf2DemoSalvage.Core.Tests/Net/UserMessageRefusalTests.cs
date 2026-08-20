using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Core.Net;

namespace Tf2DemoSalvage.Core.Tests.Net;

/// <summary>
/// What every user message layout does with a body that cannot be its own.
/// </summary>
/// <remarks>
/// **The refusal is the load-bearing half of this file, not the decode.** Every layout in
/// <c>UserMessageBody</c> is a hypothesis about a message this project has no header for, and the
/// only check available is that the layout consumes the body's stated length exactly. A layout that
/// accepted whatever it was given would report plausible numbers for the wrong message and nothing
/// downstream could tell — which is RISKS B16, measured: a message implemented from its struct's C
/// types rather than its read function desynchronised a packet while every number it produced
/// looked reasonable.
///
/// So the property tested here is stated over ALL names rather than one at a time: no registered
/// name decodes a body of the wrong size. Written per-message it would be forty near-identical
/// tests and a new layout would arrive with none of them; written this way a new layout is covered
/// the moment it is added, and a layout that forgets its length check fails here rather than in a
/// trace six months later.
///
/// The exception is stated rather than excluded: <c>HapMeleeContact</c> is registered at zero bytes,
/// so an empty body is its correct body.
/// </remarks>
public sealed class UserMessageRefusalTests
{
    /// <summary>A body far larger than any layout reads, so no layout can consume it exactly.</summary>
    private const int AbsurdBits = 4096;

    /// <summary>The one message whose registered width is zero.</summary>
    private const string EmptyBodied = "HapMeleeContact";

    [Test]
    public void Decode_ABodyFarLargerThanAnyLayout_IsRefusedByEveryName()
    {
        // 4096 bits is longer than the longest layout here by an order of magnitude, so a layout
        // that consumed it exactly would have to be reading fields nobody wrote. Uniform across
        // every name, which is what makes it a property rather than forty separate assertions.
        List<string> accepted = [];

        foreach ((int id, string name) in RegisteredNames())
        {
            UserMessage decoded = UserMessageBody.Decode(
                id, name, new byte[AbsurdBits / 8], AbsurdBits, Protocol);

            if (decoded.Fields is not null)
            {
                accepted.Add(name);
            }
        }

        accepted.ShouldBeEmpty();
    }

    [Test]
    public void Decode_AnEmptyBody_IsRefusedByEveryNameExceptTheZeroWidthOne()
    {
        // The other end of the same range. An empty body is what a truncated stream hands a
        // decoder, and a layout that read zero fields out of it and called that success would
        // report an event that never happened.
        List<string> accepted = [];

        foreach ((int id, string name) in RegisteredNames())
        {
            UserMessage decoded = UserMessageBody.Decode(id, name, [], 0, Protocol);

            if (decoded.Fields is not null)
            {
                accepted.Add(name);
            }
        }

        // Stated as an equality rather than a subset: a second zero-width message appearing here
        // is a finding either way, and a set comparison is what reports it.
        accepted.Distinct().ShouldBe([EmptyBodied]);
    }

    [Test]
    public void Decode_ARefusedBody_WithholdsTheNameAsWellAsTheFields()
    {
        // **A name is a claim and a refusing layout is evidence against it.** Reporting the name
        // while refusing the body would say "this is a Geiger whose contents we could not read",
        // when what the refusal actually establishes is that it is probably not a Geiger.
        UserMessage decoded = UserMessageBody.Decode(
            IdOf("Geiger"), "Geiger", new byte[AbsurdBits / 8], AbsurdBits, Protocol);

        decoded.Fields.ShouldBeNull();
        decoded.Name.ShouldBeNull();
    }

    [Test]
    public void Decode_AnIdThisProjectHasNoLayoutFor_KeepsItsNameAndReportsNoFields()
    {
        // **The control for the test above, and the distinction the whole file turns on.** "No
        // layout" and "a layout that refused" produce the same null field list and must produce
        // different names — otherwise the withheld name says nothing, because it would be
        // withheld for every message this project has never implemented.
        UserMessage decoded = UserMessageBody.Decode(
            IdOf(Unimplemented), Unimplemented, new byte[16], 128, Protocol);

        decoded.Fields.ShouldBeNull();
        decoded.Name.ShouldBe(Unimplemented);
    }

    [Test]
    public void Decode_ABodyOfTheRightSize_StillDecodes()
    {
        // **The sensitivity control.** Every assertion above is that decoding did NOT happen, and
        // a Decode that returned null unconditionally would pass all of them. One body of the
        // right width, decoded to the right value, is what says the refusals mean anything.
        UserMessage decoded = UserMessageBody.Decode(
            IdOf("Geiger"), "Geiger", [42], 8, Protocol);

        decoded.Name.ShouldBe("Geiger");
        decoded.Fields.ShouldNotBeNull()
            .ShouldContain(new KeyValuePair<string, object?>("range", 42));
    }

    /// <summary>A message id no layout in this project claims.</summary>
    /// <remarks>
    /// Chosen because it is genuinely opaque rather than merely unread: <c>MVMAnnouncement</c> is
    /// a Mann-vs-Machine wave notification whose body this project has never worked out.
    /// </remarks>
    private const string Unimplemented = "MVMAnnouncement";

    private const int Protocol = 24;

    /// <summary>Every id the protocol registers, paired with its name.</summary>
    private static IEnumerable<(int Id, string Name)> RegisteredNames()
    {
        for (int id = 0; id <= byte.MaxValue; id++)
        {
            if (UserMessageNames.Lookup(id, Protocol) is { } name)
            {
                yield return (id, name);
            }
        }
    }

    private static int IdOf(string name) =>
        RegisteredNames().First(entry =>
            string.Equals(entry.Name, name, StringComparison.Ordinal)).Id;
}
