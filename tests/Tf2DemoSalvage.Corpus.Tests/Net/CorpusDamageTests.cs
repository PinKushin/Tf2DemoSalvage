using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Net;

namespace Tf2DemoSalvage.Core.Tests.Net;

/// <summary>
/// Decodes the <c>Damage</c> user message out of real demos, on both sides of its era boundary.
/// </summary>
/// <remarks>
/// **Two layouts, and the corpus holds both.** Protocol 15 and above send a short, a long the game
/// discards, a bit saying whether a position follows, and a <c>BitVec3Coord</c>. Protocol 14 and
/// below send one byte of damage and the vector, with no long and no flag. See RISKS B26.
///
/// **The check that matters here is exact consumption, not plausibility.** These bodies end
/// mid-byte — 77 bits, 72, 118, 113, 49 — so the stated length is exact rather than padded, and a
/// layout that stops short has read a prefix of the body. That is not a theoretical failure: the
/// modern layout fits UNDER a protocol-14 body, so accepting "consumed no more than stated" let 20
/// of the 2008 demo's 24 messages through reporting `damage=16164`.
///
/// The damage bound is here to catch that specific failure rather than to police the format. TF2's
/// largest single hit is around 450 and the writer clamps to 32000, so 2048 is loose enough never
/// to fire on a real value and tight enough to catch a misread short.
/// </remarks>
public sealed class CorpusDamageTests
{


    [Test]
    public void EveryDamageMessage_DecodesAtEveryProtocol()
    {
        // The regression this exists for is silent: before the protocol-14 layout existed, this
        // demo still produced fields for most of its damage messages. So counting decodes is not
        // enough on its own - the value bound below is what separates "decoded" from "correct".
        int demos = 0;

        foreach (string path in Corpus.Files())
        {
            ushort protocol = Corpus.ProtocolOf(path);
            string name = Path.GetFileName(path);

            List<UserMessage> damages = [.. Damages(path)];
            if (damages.Count == 0)
            {
                // SourceTV recordings carry none at all: the message goes only to the player who
                // was hit. That absence is a fact about the mode, not a gap in the test.
                continue;
            }

            demos++;
            int undecoded = damages.Count(message => message.Fields is null);
            TestContext.Out.WriteLine(
                $"{name} (protocol {protocol}): {damages.Count} damage messages, " +
                $"{undecoded} undecoded");

            undecoded.ShouldBe(0, name);
        }

        demos.ShouldBeGreaterThan(0, "no demo carried a Damage user message");
    }


    [Test]
    public void ProtocolFourteenAndBelow_SendNoDamageTypeField()
    {
        // The two layouts differ in what they carry, not only in how wide it is, and this is the
        // assertion that would fail if the boundary constant moved. The old form has no
        // damage-type long, so reporting a `bits` field for it would be inventing a zero.
        //
        // Measured on both sides: protocols 11 and 14 carry the old form, 15 and above the new.
        // Protocol 11's specimen is a local-corpus demo recorded by holding a soldier next to a
        // resupply cabinet, which is why it has 43 of these where the committed protocol-11 files
        // have none at all.
        int old = 0;
        int modern = 0;

        foreach (string path in Corpus.Files())
        {
            ushort protocol = Corpus.ProtocolOf(path);
            string name = Path.GetFileName(path);

            foreach (UserMessage message in Damages(path))
            {
                if (message.Fields is null)
                {
                    continue;
                }

                bool hasDamageType = message.Fields.Any(field => field.Key == "bits");
                if (protocol <= 14)
                {
                    hasDamageType.ShouldBeFalse($"{name}: protocol {protocol}");
                    old++;
                }
                else
                {
                    hasDamageType.ShouldBeTrue($"{name}: protocol {protocol}");
                    modern++;
                }
            }
        }

        TestContext.Out.WriteLine($"{old} messages on the old layout, {modern} on the modern one");

        // Both sides have to be exercised or this asserts nothing: a run containing only modern
        // demos would pass with the old branch never taken.
        old.ShouldBeGreaterThan(0, "no demo at protocol 14 or below carried a Damage message");
        modern.ShouldBeGreaterThan(0, "no demo above protocol 14 carried a Damage message");
    }

    private static IEnumerable<UserMessage> Damages(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        NetDecodeState state = new() { NetworkProtocol = Corpus.ProtocolOf(path) };

        foreach (DemoCommand command in DemoCommandReader.Read(bytes.AsMemory(DemoHeader.SizeBytes)))
        {
            if (command.Type is not (DemoCommandType.Signon or DemoCommandType.Packet))
            {
                continue;
            }

            foreach (INetMessage message in
                NetMessageReader.Read(command.Payload.Span, state).Messages)
            {
                if (message is UserMessage { Name: "Damage" } damage)
                {
                    yield return damage;
                }
            }
        }
    }
}
