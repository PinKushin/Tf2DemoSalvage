using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Core.Schema;

namespace Tf2DemoSalvage.Core.Tests;

/// <summary>A demo carrying one <c>CBeam</c> on <c>DT_Beam</c>, the NOBASE table every corpus spotlight arrives on.</summary>
/// <remarks>
/// **The table is NOBASE and declares its own model index, origin, move parent and render state**
/// (`beam_shared.cpp:147-232`), which is the whole reason a beam never reached the viewer: nothing in it is
/// <c>DT_BaseEntity</c>'s. The values default to what <c>demostf-cp_process_f12-2026-08-08-2207</c>'s entity 530 enters
/// with — a <c>point_spotlight</c> shaft, <c>FBEAM_SHADEOUT | FBEAM_NOTILE</c>, render colour <c>0x40FFFFFF</c> at
/// <c>kRenderTransTexture</c> — so the fixture is the shape the corpus actually sends.
/// </remarks>
internal static class SyntheticBeam
{
    public const int BeamClassId = 0;

    public const int BeamEntityIndex = 530;

    public const string ModelPath = "sprites/glow_test02.vmt";

    public const string HaloPath = "sprites/light_glow03.vmt";

    /// <summary><c>FBEAM_SHADEOUT | FBEAM_NOTILE</c>, what every corpus spotlight sends.</summary>
    public const int SpotlightFlags = 0x280;

    /// <summary>White at alpha 64, packed as <c>color32</c> with red in the low byte.</summary>
    public const int SpotlightColour = 0x40FFFFFF;

    public static DemoSchema Schema() => new(
        [
            new SendTable("DT_Beam", NeedsDecoder: true,
            [
                UnsignedInt("m_nBeamType", bits: 3),
                UnsignedInt("m_nBeamFlags", bits: 17),
                UnsignedInt("m_nNumBeamEnts", bits: 5),
                UnsignedInt("m_nHaloIndex", bits: 16),
                Float("m_fHaloScale"),
                Float("m_fWidth"),
                Float("m_fEndWidth"),
                Float("m_fFadeLength"),
                Float("m_fSpeed"),
                UnsignedInt("m_nRenderMode", bits: 8),
                UnsignedInt("m_clrRender", bits: 32),
                Vector("m_vecEndPos"),
                UnsignedInt("m_nModelIndex", bits: 13),
                Vector("m_vecOrigin"),
                UnsignedInt("moveparent", bits: 21),
            ]),
        ],
        [new ServerClass(BeamClassId, "CBeam", "DT_Beam")]);

    /// <summary>The spotlight entering at the first tick and re-sent at each later one with a new origin.</summary>
    /// <param name="updates">Each tick and the origin the beam is sent with then.</param>
    /// <param name="speed">The wire's <c>m_fSpeed</c>, before the client's tenth.</param>
    /// <returns>The demo.</returns>
    public static byte[] Demo(IReadOnlyList<(int Tick, float X, float Y, float Z)> updates, float speed = 0f)
    {
        ArgumentNullException.ThrowIfNull(updates);

        DemoSchema schema = Schema();
        EntityDecoder decoder = new(schema, EntityDecoder.ClassIdBits(schema.ServerClasses.Count));

        List<DemoCommand> commands =
        [
            SyntheticDemo.Packet(
                SyntheticDemo.DefaultProtocol,
                0,
                ServerInfo(),
                SyntheticDemo.StringTable(
                    ModelPrecache.TableName, [string.Empty, ModelPath, HaloPath], maxEntries: 8)),
            SyntheticDemo.DataTables(schema),
        ];

        for (int index = 0; index < updates.Count; index++)
        {
            (int tick, float x, float y, float z) = updates[index];

            Dictionary<string, PropertyValue> values = new()
            {
                ["m_vecOrigin"] = PropertyValue.FromVector(x, y, z),
            };

            if (index == 0)
            {
                values["m_nBeamType"] = PropertyValue.FromInt(0);
                values["m_nBeamFlags"] = PropertyValue.FromInt(SpotlightFlags);
                values["m_nNumBeamEnts"] = PropertyValue.FromInt(2);
                values["m_nHaloIndex"] = PropertyValue.FromInt(2);
                values["m_fHaloScale"] = PropertyValue.FromFloat(60f);
                values["m_fWidth"] = PropertyValue.FromFloat(100f);
                values["m_fEndWidth"] = PropertyValue.FromFloat(30f);
                values["m_fFadeLength"] = PropertyValue.FromFloat(190f);
                values["m_fSpeed"] = PropertyValue.FromFloat(speed);
                values["m_nRenderMode"] = PropertyValue.FromInt(2);
                values["m_clrRender"] = PropertyValue.FromInt(SpotlightColour);
                values["m_vecEndPos"] = PropertyValue.FromVector(x + 32f, y - 1f, z - 197f);
                values["m_nModelIndex"] = PropertyValue.FromInt(1);
                values["moveparent"] = PropertyValue.FromInt((1 << 21) - 1);
            }

            DecodedEntity beam = Entity(decoder, values) with
            {
                UpdateType = index == 0 ? EntityUpdateType.Enter : EntityUpdateType.Delta,
            };

            byte[] body = decoder.EncodeEntities([beam], [], isDelta: index > 0, 0, out int bits);

            commands.Add(SyntheticDemo.Packet(
                SyntheticDemo.DefaultProtocol,
                tick,
                new PacketEntitiesMessage(
                    MaxEntries: 1024,
                    IsDelta: index > 0,
                    DeltaFromTick: index > 0 ? updates[index - 1].Tick : null,
                    BaselineIndex: false,
                    UpdatedEntries: 1,
                    LengthBits: bits,
                    UpdateBaseline: false,
                    Body: body)));
        }

        return SyntheticDemo.From(SyntheticDemo.DefaultProtocol, [.. commands]);
    }

    private static DecodedEntity Entity(EntityDecoder decoder, Dictionary<string, PropertyValue> values)
    {
        IReadOnlyList<FlatProperty> flat = decoder.FlattenedFor(BeamClassId);
        List<DecodedProperty> properties = [];

        foreach ((string name, PropertyValue value) in values)
        {
            int index = -1;

            for (int at = 0; at < flat.Count; at++)
            {
                if (string.Equals(flat[at].Property.Name, name, StringComparison.Ordinal))
                {
                    index = at;
                    break;
                }
            }

            if (index < 0)
            {
                throw new InvalidOperationException($"no flattened property named '{name}'");
            }

            properties.Add(new DecodedProperty(index, flat[index], value));
        }

        properties.Sort((left, right) => left.Index.CompareTo(right.Index));

        return new DecodedEntity(
            BeamEntityIndex, BeamClassId, SerialNumber: 7, EntityUpdateType.Enter, properties);
    }

    private static ServerInfoMessage ServerInfo() => new(
        NetworkProtocol: SyntheticDemo.DefaultProtocol,
        ServerCount: 1,
        IsSourceTv: true,
        IsDedicated: true,
        MapCrc: 0,
        MaxClasses: 1,
        MapHash: new byte[16],
        PlayerSlot: 0,
        MaxPlayers: 2,
        IntervalPerTick: 0.015f,
        Platform: 'w',
        GameDirectory: "tf",
        Map: "cp_process_f12",
        Skybox: "sky_tf2_04",
        ServerName: "synthetic",
        IsReplay: false);

    private static SendProperty UnsignedInt(string name, int bits) =>
        new(SendPropType.Int, name, 1 << 3, string.Empty, 0f, 0f, bits, 0);

    /// <summary>An unquantised float — <c>SPROP_NOSCALE</c> — so the test's values survive the wire exactly.</summary>
    private static SendProperty Float(string name) =>
        new(SendPropType.Float, name, 1 << 2, string.Empty, 0f, 0f, 32, 0);

    private static SendProperty Vector(string name) =>
        new(SendPropType.Vector, name, 0, string.Empty, -16384f, 16384f, 32, 0);
}
