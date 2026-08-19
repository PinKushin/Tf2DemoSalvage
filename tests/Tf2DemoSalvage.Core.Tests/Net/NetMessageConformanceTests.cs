using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Core.Tests.Net;

/// <summary>
/// The network messages this decoder names, checked against the engine's own handler list.
/// </summary>
/// <remarks>
/// **The numbers are not in the SDK and the names are.** <c>protocol.h</c> and <c>netmessages.h</c>
/// are not shipped in source-sdk-2013, so the ids came from scanning client binaries (B3) and no
/// header can confirm them. <c>public/inetmsghandler.h</c> does declare a handler per message, so
/// the SET of messages is readable even though the numbering is not — and that is enough to check
/// the thing that was actually wrong here.
///
/// **What it caught.** This file's subject used to be documented as "ids 1, 9, 16, 20 and 22 are
/// unused at this protocol — a stream producing one is malformed". The engine declares handlers for
/// <c>SendTable</c> and <c>CrosshairAngle</c>, and B3 records a real demo carrying id 1. The gaps are
/// unimplemented, not absent, and the difference decides whether a demo that hits one is a defect in
/// this project or a corrupt file. Calling a legitimate demo corrupt is the expensive direction: it
/// ends the investigation.
///
/// **Two independent sources agreeing is the control here.** The numbering came from binaries; the
/// names come from published source. Neither can check itself.
/// </remarks>
public sealed class NetMessageConformanceTests
{
    /// <summary>Where the engine declares a handler for every message it processes.</summary>
    private const string Handlers = "src/public/inetmsghandler.h";

    /// <summary>
    /// The two names this project carries that the handler interface does not, with their reasons.
    /// </summary>
    /// <remarks>
    /// **Neither is a gap, and stating them is what keeps the main assertion strict.** <c>net_NOP</c>
    /// has nothing to process — it exists to pad a packet to a byte boundary — so no handler is
    /// declared for it. <c>net_File</c> is handled through <c>INetChannelHandler</c>, which is a
    /// different interface in the same header, because a file request is a channel-level concern
    /// rather than a game one.
    /// </remarks>
    private static readonly Dictionary<NetMessageType, string> NotInThisInterface = new()
    {
        [NetMessageType.Empty] = "net_NOP carries nothing to process, so no handler exists",
        [NetMessageType.File] = "net_File is handled by INetChannelHandler, not INetMessageHandler",
    };

    /// <summary>How this project spells a name against how the engine spells it.</summary>
    /// <remarks>
    /// Three differ, all cosmetically: <c>NetTick</c> keeps its prefix to say which half of the
    /// protocol it belongs to, and the other two differ only in capitalisation. Written out rather
    /// than normalised by rule, because a rule loose enough to fold these would also fold two names
    /// that genuinely differ.
    /// </remarks>
    private static readonly Dictionary<NetMessageType, string> SpelledDifferently = new()
    {
        [NetMessageType.NetTick] = "Tick",
        [NetMessageType.SignOnState] = "Signonstate",
        [NetMessageType.BspDecal] = "Bspdecal",
    };

    [SetUp]
    public void RequireTheSdk()
    {
        if (!SourceSdk.Available)
        {
            Assert.Ignore(SourceSdk.Missing);
        }
    }

    [Test]
    public void EveryMessageWeName_IsOneTheEngineDeclares()
    {
        HashSet<string> engine = Declared();

        List<string> unknown = [];

        foreach (NetMessageType message in Enum.GetValues<NetMessageType>())
        {
            if (NotInThisInterface.ContainsKey(message))
            {
                continue;
            }

            string name = SpelledDifferently.TryGetValue(message, out string? theirs)
                ? theirs
                : message.ToString();

            if (!engine.Contains(name))
            {
                unknown.Add($"{message} (looked for {name})");
            }
        }

        unknown.ShouldBeEmpty(
            "these are decoded as network messages and the engine declares no handler for them: " +
            string.Join(", ", unknown));
    }

    [Test]
    public void NetMessageNumbering_TheGaps_AreMessagesTheEngineDeclares()
    {
        // **The assertion the old comment would have failed.** If the missing ids were genuinely
        // unused, nothing would fill them. The engine declares SendTable and CrosshairAngle and this
        // project handles neither, which accounts for two of the five gaps and settles the question
        // of what they are: unimplemented.
        HashSet<string> engine = Declared();
        HashSet<string> ours = Handled();

        string[] unhandled =
        [
            .. engine.Where(name => !ours.Contains(name)).OrderBy(name => name, StringComparer.Ordinal),
        ];

        unhandled.ShouldContain("SendTable");
        unhandled.ShouldContain("CrosshairAngle");

        int gaps = Gaps().Count;

        unhandled.Length.ShouldBeLessThanOrEqualTo(
            gaps,
            $"the engine declares {unhandled.Length} messages this project does not handle " +
            $"({string.Join(", ", unhandled)}) but the numbering has only {gaps} gaps " +
            $"({string.Join(", ", Gaps())}), so at least one of them has nowhere to live");
    }

    [Test]
    public void NetMessageNumbering_TheGaps_AreWhereWeThinkTheyAre()
    {
        // Stated as a fact about the enum rather than a prose claim, because the prose version of
        // this was the thing that was wrong. B3's demo carries id 1; the rest are unimplemented
        // messages whose ids came from binaries.
        Gaps().ShouldBe(new[] { 1, 9, 16, 20, 22 });
    }

    [Test]
    public void NetMessageNumbering_TheHandlerList_WasActuallyRead()
    {
        // The control. Every assertion above passes trivially against an empty set.
        Declared().Count.ShouldBeGreaterThan(25, $"no handlers were extracted from {Handlers}");
    }

    /// <summary>Every message name the engine declares a handler for.</summary>
    private static HashSet<string> Declared()
    {
        string text = SourceSdk.Text(Handlers)
            ?? throw new InvalidOperationException($"{Handlers} is missing from the SDK checkout");

        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);

        // `= 0` is what separates the pure-virtual declarations from the two macro DEFINITIONS at
        // the top of the file, which capture the parameter name `name` and would otherwise be
        // counted as a message.
        foreach (Match hit in Regex.Matches(
            text,
            @"PROCESS_(?:NET|SVC)_MESSAGE\(\s*([A-Za-z0-9_]+)\s*\)\s*=\s*0",
            RegexOptions.None,
            TimeSpan.FromSeconds(10)))
        {
            names.Add(hit.Groups[1].Value);
        }

        return names;
    }

    /// <summary>Every engine name this project decodes, in the engine's spelling.</summary>
    private static HashSet<string> Handled()
    {
        IEnumerable<string> named = Enum.GetValues<NetMessageType>()
            .Where(message => !NotInThisInterface.ContainsKey(message))
            .Select(message => SpelledDifferently.TryGetValue(message, out string? theirs)
                ? theirs
                : message.ToString());

        return new HashSet<string>(named, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The ids between zero and the highest one, that this project does not name.</summary>
    private static List<int> Gaps()
    {
        HashSet<int> declared = [.. Enum.GetValues<NetMessageType>().Select(message => (int)message)];

        return [.. Enumerable.Range(0, declared.Max()).Where(id => !declared.Contains(id))];
    }
}
