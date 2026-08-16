using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// Every networked property this decoder looks for is one the engine actually sends.
/// </summary>
/// <remarks>
/// **A misspelled property name is invisible.** The lookup finds nothing, the value takes its
/// default, and every default here is a legal value — body zero is a body, skin zero is a skin,
/// yaw zero is a direction. So a typo behaves exactly like an entity that never sent the property,
/// which is the same silence that hid three fields this session, one layer further down.
///
/// **Valve declares the answer.** Send tables are written out in the game code —
/// <c>SendPropInt( SENDINFO(m_nBody), ANIMATION_BODY_BITS)</c> at
/// <c>server/baseanimating.cpp:237</c> — so the set of names an entity can carry is readable, and
/// every name this project asks for can be checked against it.
///
/// **This is a conformance test and its reference is the SDK**, not another part of this codebase.
/// It fails if someone renames a constant to something Source does not send, and it would have
/// failed the day it was written had any of the current names been wrong.
/// </remarks>
public sealed class SendPropConformanceTests
{
    [Test]
    public void EveryPropertyWeLookFor_IsOneTheEngineSends()
    {
        HashSet<string> declared = SentProperties();

        List<string> unknown = [];

        foreach ((string table, IReadOnlyList<string> properties) in EntityState.NetworkedProperties)
        {
            foreach (string property in properties)
            {
                // An indexed name is sent as its array, and the flattener splits it: m_vecOrigin[2]
                // is a component of m_vecOrigin rather than a property in its own right.
                string sent = property.Contains('[', StringComparison.Ordinal)
                    ? property[..property.IndexOf('[', StringComparison.Ordinal)]
                    : property;

                if (!declared.Contains(sent))
                {
                    unknown.Add($"{table}.{property}");
                }
            }
        }

        unknown.ShouldBeEmpty(
            "these names are looked for in the demo and no send table in the SDK declares them, so " +
            "they would silently find nothing: " + string.Join(", ", unknown));
    }

    [Test]
    public void TheKnownSpecialCases_AreNotMistakenForProperties()
    {
        // **moveparent is not a SendProp and must not be checked as one.** It is the flattened name
        // the engine gives a parent handle inside DT_BaseEntity's hierarchy, so it will never
        // appear in a SENDINFO. Stated here so the absence is a recorded fact rather than a gap
        // someone later "fixes" by renaming the constant to something that IS declared and breaks
        // the decode.
        HashSet<string> declared = SentProperties();

        declared.ShouldNotContain(
            "moveparent",
            "if this ever appears in a send table, the note in EntityState needs revisiting");
    }

    [Test]
    public void TheEyeAnglesWeReadAreSentAsAnArray()
    {
        // A player's view angles arrive as m_angEyeAngles with two components, which this project
        // reads as [0] and [1]. If the engine ever sent them as separate scalars the indexed form
        // would find nothing — and a player facing due east is a plausible picture.
        SentProperties().ShouldContain("m_angEyeAngles");
    }

    /// <summary>Every property name the engine's send tables declare.</summary>
    /// <remarks>
    /// Taken from <c>SENDINFO(...)</c> across the game code, which is where a send table names the
    /// member it transmits. The union across all classes is the right set: this project decodes
    /// generically off whatever schema a demo carries, so a name is legitimate if ANY class sends
    /// it.
    /// </remarks>
    private static HashSet<string> SentProperties()
    {
        string? root = Environment.GetEnvironmentVariable("SOURCE_SDK");

        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            root = @"F:\src\source-sdk-2013";
        }

        string game = Path.Combine(root, "src", "game");

        if (!Directory.Exists(game))
        {
            Assert.Ignore("source-sdk-2013 is not available; set SOURCE_SDK to run this.");
        }

        HashSet<string> names = new(StringComparer.Ordinal);

        Regex sendInfo = new(@"SENDINFO(?:_[A-Z]+)?\(\s*([A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled);

        foreach (string file in Directory.EnumerateFiles(game, "*.cpp", SearchOption.AllDirectories))
        {
            foreach (Match hit in sendInfo.Matches(File.ReadAllText(file)))
            {
                names.Add(hit.Groups[1].Value);
            }
        }

        // The instrument before its answer: an extraction that found nothing would pass every
        // assertion above by vacuum.
        names.Count.ShouldBeGreaterThan(200, "no SENDINFO declarations were found in the SDK");

        return names;
    }
}
