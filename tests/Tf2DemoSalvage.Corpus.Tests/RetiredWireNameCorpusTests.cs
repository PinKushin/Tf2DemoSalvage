using System;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Schema;

namespace Tf2DemoSalvage.Core.Tests;

/// <summary>
/// The pre-2013 wire name for the model scale, proved against the clients that sent it.
/// </summary>
/// <remarks>
/// **This is the exclusion list in `SendTableConformanceTests` earning its keep.** That test checks
/// every property this decoder looks for against the table the SDK declares it in, and
/// <c>m_flModelWidthScale</c> is in no send table there — the 2013 server calls it
/// <c>m_flModelScale</c>. The engine keeps only a receive-side record,
/// <c>RecvPropFloat(RECVINFO_NAME(m_flModelScale, m_flModelWidthScale))</c>
/// (<c>c_baseanimating.cpp:181</c>, "for demo compatibility only"), and a receive table has no
/// block structure to say which TABLE the name belongs to.
///
/// **So the pair comes from the demos, and this is the file that says so** (B271). An exclusion
/// justified by prose is an exclusion nobody re-checks; one pinned to real bytes goes red the day
/// it stops being true.
///
/// **This belongs in the corpus and not in `Core.Tests`, which is the D38 test.** The question is
/// "what did the 2007 client's own schema declare", and a synthetic fixture cannot answer it — it
/// would only restate what the fixture's author believed. Only these files know.
/// </remarks>
public sealed class RetiredWireNameCorpusTests
{
    /// <summary>The property, and the table the era clients declare it in.</summary>
    private const string Property = "m_flModelWidthScale";

    /// <summary>What TF2 renamed it to, no earlier than the 2013 build.</summary>
    private const string Modern = "m_flModelScale";

    /// <summary>The table both names live in.</summary>
    private const string Table = "DT_BaseAnimating";

    [Test]
    public void Schema_OnEveryPre2013EraSpecimen_DeclaresTheRetiredScaleName()
    {
        foreach (string name in new[]
        {
            "tf2-2007-build3258-pov-cp_granary",
            "tf2-2008-build3420-pov-cp_granary",
            "tf2-2009-build3862-pov-cp_badlands",
            "tf2-2011-build4604-pov-koth_viaduct",
        })
        {
            DemoSchema schema = SchemaOf(name);

            Declares(schema, Property).ShouldBeTrue(
                $"{name} is a pre-2013 recording and its client sent the model scale as " +
                $"{Property}; this is the pair SendTableConformanceTests excludes from the SDK " +
                $"check, and it is only legitimate while these files say so");

            // **The control, and it is what makes the assertion above mean something.** If both
            // names were present the exclusion would be unnecessary and the "era split" claim
            // false — the reader would simply find the modern one.
            Declares(schema, Modern).ShouldBeFalse(
                $"{name} must NOT also declare {Modern}, or there was never a rename to handle");
        }
    }

    /// <remarks>
    /// **The other half of the split**, without which the first test is compatible with "every demo
    /// declares the old name" — including modern ones, which would mean the fallback in
    /// <c>EntityState.ModelScale</c> is doing all the work and the primary lookup none.
    /// </remarks>
    [Test]
    public void Schema_OnThePost2013Specimens_DeclaresOnlyTheModernScaleName()
    {
        foreach (string name in new[]
        {
            "tf2-2013-build1729296-stv-cp_foundry",
            "z1800",
        })
        {
            DemoSchema schema = SchemaOf(name);

            Declares(schema, Modern).ShouldBeTrue($"{name} is 2013 or later");
            Declares(schema, Property).ShouldBeFalse(
                $"{name} is 2013 or later and its client had stopped sending {Property}");
        }
    }

    /// <summary>Whether a demo's own schema puts a property in <see cref="Table"/>.</summary>
    private static bool Declares(DemoSchema schema, string property) =>
        schema.Tables
            .Where(table => string.Equals(table.Name, Table, StringComparison.Ordinal))
            .SelectMany(table => table.Properties)
            .Any(declared => string.Equals(declared.Name, property, StringComparison.Ordinal));

    /// <summary>The entity schema a demo carries, which is this project's whole premise.</summary>
    private static DemoSchema SchemaOf(string name)
    {
        byte[] bytes = File.ReadAllBytes(Corpus.Demo(name));

        ushort protocol = (ushort)DemoHeader.Parse(bytes).NetworkProtocol;

        foreach (DemoCommand command in
            DemoCommandReader.Read(bytes.AsMemory(DemoHeader.SizeBytes)))
        {
            if (command.Type == DemoCommandType.DataTables)
            {
                return SendTableParser.Parse(command.Payload.Span, protocol);
            }
        }

        throw new InvalidDataException($"{name} carries no dem_datatables.");
    }
}
