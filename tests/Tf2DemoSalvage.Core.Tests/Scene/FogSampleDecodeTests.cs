using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Schema;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// A fog controller written into a demo, decoded off the wire into the timeline's fog sample.
/// </summary>
/// <remarks>
/// **The send table is Valve's**, <c>fogcontroller.cpp:78-88</c>:
///
/// <code>
/// SendPropInt( SENDINFO_STRUCTELEM( m_fog.enable ), 1, SPROP_UNSIGNED ),
/// SendPropInt( SENDINFO_STRUCTELEM( m_fog.colorPrimary ), 32, SPROP_UNSIGNED ),
/// SendPropFloat( SENDINFO_STRUCTELEM( m_fog.start ), 0, SPROP_NOSCALE ),
/// SendPropFloat( SENDINFO_STRUCTELEM( m_fog.end ), 0, SPROP_NOSCALE ),
/// SendPropFloat( SENDINFO_STRUCTELEM( m_fog.maxdensity ), 0, SPROP_NOSCALE ),
/// </code>
///
/// — a <c>color32</c> (<c>FIELD_COLOR32</c>, keyvalue "fogcolor") sent as one unsigned 32-bit int, so its memory
/// order <c>r, g, b, a</c> puts red in the LOW byte. The values are not invented: each is what a 2026-10-29 trace
/// (<c>Cli -t -e</c>) read off a retired gcor recording, and each map's own keyvalues agree (<c>MapFogProbe</c>).
/// Those recordings left gcor that day (the owner's call) and are read from lcor by <c>FogDecodeTests</c>.
/// </remarks>
public sealed class FogSampleDecodeTests
{
    /// <summary><c>koth_viaduct</c>'s controller, from the 2011 SourceTV recording: 0x00DDAED5, "213 174 221".</summary>
    private const int ViaductColour = 14528213;

    /// <summary><c>cp_foundry</c>'s controller, from the 2013 SourceTV recording: 0x00867983, "131 121 134".</summary>
    private const int FoundryColour = 8812931;

    [Test]
    public void FogSamples_AColourOfThreeDistinctBytes_ReadsRedFromTheLowByte()
    {
        // Three distinct bytes, so the byte order is proved: read high-first, red would be 221 and blue 213.
        SceneFog fog = OnlyFog(Demo(enable: 1, ViaductColour, start: 0f, end: 6500f, density: 1f));

        fog.Red.ShouldBe(213f / 255f);
        fog.Green.ShouldBe(174f / 255f);
        fog.Blue.ShouldBe(221f / 255f);
        fog.Start.ShouldBe(0f);
        fog.End.ShouldBe(6500f);
        fog.MaxDensity.ShouldBe(1f);
    }

    [Test]
    public void FogSamples_ARangeStartingAwayFromTheCamera_KeepsItsStartAndEnd()
    {
        // Numbers no default produces: a reader that ignored m_fog.start would pass on a range from 0.
        SceneFog fog = OnlyFog(Demo(enable: 1, FoundryColour, start: 1707f, end: 4634f, density: 0.7f));

        fog.Start.ShouldBe(1707f);
        fog.End.ShouldBe(4634f);
        fog.MaxDensity.ShouldBe(0.7f);
        fog.Red.ShouldBe(131f / 255f);
        fog.Green.ShouldBe(121f / 255f);
        fog.Blue.ShouldBe(134f / 255f);
    }

    [Test]
    public void FogSamples_TheSameControllerWithFogDisabled_IsEmpty()
    {
        // The control: one property changed, and the sample the two tests above read is gone — so it came from this
        // entity's decoded values, not from a default the timeline holds anyway.
        DemoTimeline timeline = DemoTimeline.Build(Demo(enable: 0, ViaductColour, start: 0f, end: 6500f, density: 1f));

        timeline.FogControllersSeen.ShouldBe(1);
        timeline.FogSamples.ShouldBeEmpty();
    }

    private static SceneFog OnlyFog(byte[] demo)
    {
        DemoTimeline timeline = DemoTimeline.Build(demo);

        timeline.FogSamples.Count.ShouldBe(1);

        return timeline.FogSamples[0].Fog;
    }

    /// <summary>
    /// One <c>CFogController</c> entering at tick 66, its table as Valve declares it, and a player whose
    /// <c>m_PlayerFog.m_hCtrl</c> names it.
    /// </summary>
    /// <remarks>
    /// **The handle is how the view finds its fog** (B139): <c>C_BasePlayer::UpdateFogController</c>
    /// (<c>c_baseplayer.cpp:2802</c>) copies the controller the local player's handle names, and with none there is no
    /// fog however many controllers the map has. Sent in <c>DT_Local</c> as an Int of 21 bits (the viaduct STV's own
    /// <c>dem_datatables</c>, schema probe), the slot in the low 11 and the serial above.
    /// </remarks>
    private static byte[] Demo(int enable, int colour, float start, float end, float density)
    {
        DemoSchema schema = new(
            [
                new SendTable("DT_FogController", NeedsDecoder: true,
                [
                    new SendProperty(SendPropType.Int, "m_fog.enable", Unsigned, string.Empty, 0f, 0f, 1, 0),
                    new SendProperty(SendPropType.Int, "m_fog.colorPrimary", Unsigned, string.Empty, 0f, 0f, 32, 0),
                    NoScale("m_fog.start"),
                    NoScale("m_fog.end"),
                    NoScale("m_fog.maxdensity"),
                ]),
                new SendTable("DT_Local", NeedsDecoder: true,
                [
                    new SendProperty(SendPropType.Int, "m_PlayerFog.m_hCtrl", Unsigned, string.Empty, 0f, 0f, 21, 0),
                ]),
                new SendTable("DT_TFPlayer", NeedsDecoder: true,
                [
                    new SendProperty(SendPropType.DataTable, "localdata", 0, "DT_Local", 0f, 0f, 0, 0),
                ]),
            ],
            [
                new ServerClass(ControllerClass, "CFogController", "DT_FogController"),
                new ServerClass(PlayerClass, "CTFPlayer", "DT_TFPlayer"),
            ]);

        EntityDecoder decoder = new(schema, EntityDecoder.ClassIdBits(schema.ServerClasses.Count));
        IReadOnlyList<FlatProperty> controller = decoder.FlattenedFor(ControllerClass);

        List<DecodedProperty> fog =
        [
            Property(controller, "m_fog.enable", PropertyValue.FromInt(enable)),
            Property(controller, "m_fog.colorPrimary", PropertyValue.FromInt(colour)),
            Property(controller, "m_fog.start", PropertyValue.FromFloat(start)),
            Property(controller, "m_fog.end", PropertyValue.FromFloat(end)),
            Property(controller, "m_fog.maxdensity", PropertyValue.FromFloat(density)),
        ];

        List<DecodedProperty> local =
        [
            Property(
                decoder.FlattenedFor(PlayerClass),
                "m_PlayerFog.m_hCtrl",
                PropertyValue.FromInt(EntityIndex | (ControllerSerial << EdictBits))),
        ];

        // Ascending entity index: the encoder writes the gap to the next slot.
        byte[] body = decoder.EncodeEntities(
            [
                new DecodedEntity(PlayerIndex, PlayerClass, SerialNumber: 1, EntityUpdateType.Enter, local),
                new DecodedEntity(EntityIndex, ControllerClass, ControllerSerial, EntityUpdateType.Enter, fog),
            ],
            [],
            isDelta: false,
            0,
            out int bits);

        return SyntheticDemo.From(
            SyntheticDemo.DefaultProtocol,
            SyntheticDemo.DataTables(schema),
            SyntheticDemo.Packet(
                SyntheticDemo.DefaultProtocol,
                66,
                new PacketEntitiesMessage(
                    MaxEntries: 256,
                    IsDelta: false,
                    DeltaFromTick: null,
                    BaselineIndex: false,
                    UpdatedEntries: 2,
                    LengthBits: bits,
                    UpdateBaseline: false,
                    Body: body)));
    }

    /// <summary>The viaduct controller's entity slot in the 2011 recordings.</summary>
    private const int EntityIndex = 212;

    /// <summary>
    /// The controller's serial there: the recording's own handle reads <c>DT_Local.m_PlayerFog.m_hCtrl 1099988</c>,
    /// which is slot 212, serial 537.
    /// </summary>
    private const int ControllerSerial = 537;

    /// <summary>The player carrying the handle, in a player's slot.</summary>
    private const int PlayerIndex = 1;

    private const int ControllerClass = 0;

    private const int PlayerClass = 1;

    /// <summary><c>MAX_EDICT_BITS</c>: an entity handle's slot occupies the low eleven bits.</summary>
    private const int EdictBits = 11;

    /// <summary><c>SPROP_UNSIGNED</c>.</summary>
    private const int Unsigned = 1 << 0;

    /// <summary><c>SendPropFloat(..., 0, SPROP_NOSCALE)</c>: the float's 32 bits as they are.</summary>
    /// <remarks>Declared with 0 bits, as the viaduct STV's own <c>dem_datatables</c> declares it (schema probe).</remarks>
    private static SendProperty NoScale(string name) =>
        new(SendPropType.Float, name, 1 << 2, string.Empty, 0f, 0f, 0, 0);

    private static DecodedProperty Property(IReadOnlyList<FlatProperty> flat, string name, PropertyValue value)
    {
        for (int index = 0; index < flat.Count; index++)
        {
            if (string.Equals(flat[index].Property.Name, name, StringComparison.Ordinal))
            {
                return new DecodedProperty(index, flat[index], value);
            }
        }

        throw new InvalidOperationException($"the fixture schema declares no {name}");
    }
}
