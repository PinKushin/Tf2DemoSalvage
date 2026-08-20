using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Schema;
using Tf2DemoSalvage.Core.Text;

namespace Tf2DemoSalvage.Core.Tests.Text;

/// <summary>
/// Trace options, and sound names resolved through the precache.
/// </summary>
/// <remarks>
/// **Converted from <c>CorpusTraceTests</c>.** The option tests only ever needed a demo with
/// entities in it, which is now something a test can write. The sound test needed a demo with both
/// a <c>soundprecache</c> table and a sound referencing it — impossible until
/// <c>svc_CreateStringTable</c> became writable, and the reason that conversion was blocked.
///
/// The sound one is the interesting case: the index comes out of a delta-coded bit stream and the
/// name out of a string table, by two paths that share no code. On a real demo that agreement is
/// evidence about the file; here it is a construction, so what it checks is narrower and sharper —
/// that the trace joins the two at all, and joins them at the right index.
/// </remarks>
public sealed class SyntheticTraceOptionTests
{
    [Test]
    public void Trace_WithoutEntityProperties_ShowsEntitiesWithACountInstead()
    {
        // Two options rather than one, because the expensive half is the properties: a 39 MB demo
        // expanded in full becomes gigabytes of text. Asking for entities without them is the
        // shape a reader uses to see what is present without drowning.
        string trace = Trace(
            PlayerDemo(),
            new DemoTraceOptions { IncludeEntities = true, IncludeEntityProperties = false });

        trace.ShouldContain("entity ");
        trace.ShouldContain("props ");

        // The properties themselves must be absent, which is the half that fails if the option is
        // read but not acted on.
        trace.ShouldNotContain("m_vecOrigin");
    }

    [Test]
    public void Trace_ASnapshotLimit_StopsExpandingAfterThatManySnapshots()
    {
        // The limit exists so a trace of a long demo stays readable. A limit that was accepted and
        // ignored would produce a larger file and still name the message, so the comparison is
        // against an unlimited run rather than against a fixed size.
        byte[] demo = PlayerDemoOverTicks();

        string limited = Trace(
            demo,
            new DemoTraceOptions { IncludeEntities = true, EntitySnapshotLimit = 1 });

        string more = Trace(demo, new DemoTraceOptions { IncludeEntities = true });

        limited.Length.ShouldBeLessThan(more.Length);

        // Still named, only not expanded — a limit that dropped the message entirely would also
        // be shorter, and that is a different behaviour.
        limited.ShouldContain("svc_packetentities");
    }

    [Test]
    public void Trace_ASound_IsNamedFromTheSoundPrecacheTable()
    {
        // **Two paths that share no code, joined by the trace.** The sound index arrives in a
        // delta-coded bit stream; the name arrives in svc_CreateStringTable. A trace that printed
        // the index would look reasonable and tell a reader nothing.
        //
        // Index 3 is chosen rather than 0 so an off-by-one or a first-entry fallback shows as the
        // wrong name rather than as the right one.
        string trace = Trace(SoundDemo(soundIndex: 3), new DemoTraceOptions());

        trace.ShouldContain("svc_sounds");
        trace.ShouldContain("weapons/rocket_shoot.wav");

        // And not a neighbour, which is what an index read one place off would produce.
        trace.ShouldNotContain("weapons/shotgun_shoot.wav");
    }

    [Test]
    public void Trace_TempEntitiesWithASchema_ExpandOneLinePerEffect()
    {
        // **A temp entity is a one-shot effect with its own class**, so the trace names the class
        // rather than counting bodies — a burst of three explosions and one of three tracers are
        // the same number and different scenes.
        //
        // Entity expansion is opt-in for the same reason it is for snapshots, so both halves are
        // asserted: named either way, expanded only when asked.
        byte[] demo = EffectDemo();

        string off = Trace(demo, new DemoTraceOptions());
        string on = Trace(demo, new DemoTraceOptions { IncludeEntities = true });

        off.ShouldContain("svc_tempentities");
        on.ShouldContain("svc_tempentities");

        // The class name comes from dem_datatables, so its presence is what says the schema
        // reached the effect decoder rather than only the snapshot one.
        on.ShouldContain("CBaseAnimating");
    }

    [Test]
    public void Trace_ATempEntityWithProperties_ShowsThemWhenPropertiesAreOn()
    {
        // The third state of the same option pair: entities on, properties off, which is the
        // shape a reader uses on a long demo. An effect keeps its class and loses its fields.
        byte[] demo = EffectDemo();

        string withProperties = Trace(demo, new DemoTraceOptions { IncludeEntities = true });

        string without = Trace(
            demo,
            new DemoTraceOptions { IncludeEntities = true, IncludeEntityProperties = false });

        withProperties.ShouldContain("m_fEffects");
        without.ShouldNotContain("m_fEffects");
        without.ShouldContain("CBaseAnimating");
    }

    /// <summary>A demo carrying a schema and two temp entities that share a class.</summary>
    private static byte[] EffectDemo() => SyntheticPlayer.DemoWithTempEntities();

    /// <summary>A demo with one positioned player and a schema.</summary>
    private static byte[] PlayerDemo() =>
        SyntheticPlayer.Demo(new Dictionary<string, PropertyValue>
        {
            ["m_vecOrigin"] = PropertyValue.FromVectorXY(512f, -1024f),
            ["m_vecOrigin[2]"] = PropertyValue.FromFloat(0f),
            ["m_lifeState"] = PropertyValue.FromInt(0),
        });

    /// <summary>A demo with several entity snapshots, so a limit has something to cut.</summary>
    private static byte[] PlayerDemoOverTicks() =>
        SyntheticPlayer.DemoOverTicks(
            1f / 66.67f, (100, 0f, 0f), (110, 64f, 0f), (120, 128f, 0f), (130, 192f, 0f));

    /// <summary>A demo whose sound references a precached path.</summary>
    private static byte[] SoundDemo(int soundIndex)
    {
        (byte[] body, int bits) = SoundEncoder.Encode(
            [
                new DecodedSound(
                    EntityIndex: 5, SoundNumber: soundIndex, Flags: 0, Channel: 6,
                    IsAmbient: false, IsSentence: false, SequenceNumber: 0, Volume: 1f,
                    SoundLevel: 75, Pitch: 100, DelaySeconds: 0f,
                    OriginX: 0f, OriginY: 0f, OriginZ: 0f, SpeakerEntity: -1,
                    SpecialDsp: 0,
                    Sent: SoundFields.Entity | SoundFields.SoundNumber),
            ],
            SyntheticDemo.DefaultProtocol);

        return SyntheticDemo.Containing(
            SyntheticDemo.DefaultProtocol,
            SyntheticDemo.StringTable(
                "soundprecache",
                [
                    "",
                    "weapons/shotgun_shoot.wav",
                    "ambient/water.wav",
                    "weapons/rocket_shoot.wav",
                ],
                maxEntries: 1024),
            new SoundsMessage(IsReliable: false, Count: 1, BodyBits: bits, Body: body));
    }

    private static string Trace(byte[] demo, DemoTraceOptions options)
    {
        DemoHeader header = DemoHeader.Parse(demo.AsSpan(0, DemoHeader.SizeBytes));
        List<DemoCommand> commands =
            [.. DemoCommandReader.Read(demo.AsMemory(DemoHeader.SizeBytes))];

        StringWriter text = new() { NewLine = "\n" };
        DemoTraceWriter.Write(text, "synthetic.dem", header, commands, options: options);
        return text.ToString();
    }
}
