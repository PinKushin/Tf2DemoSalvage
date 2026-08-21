using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.SdkReference;

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
    public void SendProps_Moveparent_IsARealSendPropUnderAnAlias()
    {
        // **This test used to assert the opposite, and the opposite was false.** It read:
        //
        //   "moveparent is not a SendProp and must not be checked as one. It is the flattened name
        //    the engine gives a parent handle inside DT_BaseEntity's hierarchy, so it will never
        //    appear in a SENDINFO."
        //
        // It appears in one. `baseentity.cpp:287`:
        //
        //   SendPropEHandle( SENDINFO_NAME( m_hMoveParent, moveparent ) )
        //   #define SENDINFO_NAME(varName, remoteVarName)  #remoteVarName, ...
        //
        // The C++ member is `m_hMoveParent` and the WIRE name is `moveparent`. The scraper backing
        // this class captured only SENDINFO's first argument, so every aliased property was missing
        // from its denominator — and a previous author, finding `moveparent` absent, concluded it
        // was special and wrote that conclusion into a test that then defended it.
        //
        // **False negative, wrong conclusion, test certifying the conclusion.** Third instance of
        // that sequence found in this suite, and the most complete one: the search's limitation
        // became a recorded fact about the format.
        //
        // The scraper now reads the remote name too, so this asserts what is true. `movetype` and
        // `movecollide` are declared the same way on the same table.
        HashSet<string> declared = SentProperties();

        declared.ShouldContain("moveparent");
        declared.ShouldContain("movetype");
    }

    [Test]
    public void SendProps_TheEyeAngles_AreSentAsAnArray()
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
        if (!SourceSdk.Available)
        {
            Assert.Ignore(SourceSdk.Missing);
        }

        // **Through SourceSdk so the crawl happens once, not once per test.** src/game is thousands
        // of files and this sweep reads every one; three test methods asking independently read them
        // three times. The file list and the contents are both cached there, so the second sweep is
        // a regex over memory.
        HashSet<string> names = SourceSdk.Names(
            "src/game",
            "*.cpp",
            new Regex(
                // **The dot is load-bearing.** SENDINFO_STRUCTELEM( m_fog.start ) sends under the
                // expression it was handed, so the wire name contains a member access — and a
                // pattern matching only identifier characters captured `m_fog` and reported every
                // real fog property as unknown. Same family as wire-names-are-strings: the name is
                // whatever the macro stringifies, not whatever C++ would call the field.
                @"SENDINFO(?:_[A-Z]+)?\(\s*([A-Za-z_][A-Za-z0-9_.]*)",
                RegexOptions.Compiled,
                TimeSpan.FromSeconds(10)),
            recursive: true);

        // **`SENDINFO_NAME` sends under its SECOND argument, not its first**, and the pattern above
        // captures only the first — so every aliased property was missing from this denominator:
        //
        //   SendPropEHandle( SENDINFO_NAME( m_hMoveParent, moveparent ) )
        //   #define SENDINFO_NAME(varName, remoteVarName)  #remoteVarName, ...
        //
        // The C++ member is `m_hMoveParent`; the wire name is `moveparent`. This project reads
        // `moveparent`, correctly, and adding it to the inventory made this test report a defect
        // that did not exist — a denominator with a hole in it accuses correct code.
        //
        // `movetype` and `movecollide` are declared the same way on the same table, so anything
        // reading those later would have hit this too.
        names.UnionWith(SourceSdk.Names(
            "src/game",
            "*.cpp",
            new Regex(
                @"SENDINFO_NAME\(\s*[A-Za-z_][A-Za-z0-9_]*\s*,\s*([A-Za-z_][A-Za-z0-9_]*)",
                RegexOptions.Compiled,
                TimeSpan.FromSeconds(10)),
            recursive: true));

        // The instrument before its answer: an extraction that found nothing would pass every
        // assertion above by vacuum.
        names.Count.ShouldBeGreaterThan(200, "no SENDINFO declarations were found in the SDK");

        return names;
    }
}
