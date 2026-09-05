using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Schema;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests;

/// <summary>
/// A demo carrying one animated prop, told to play chosen sequences at chosen ticks.
/// </summary>
/// <remarks>
/// **Chosen rather than found** (D38). A real recording has to be searched for a cabinet somebody
/// walked into, and the assertion then becomes whatever that search turned up; here the sequence
/// changes are the ones under test.
///
/// The schema mirrors the shape `cp_fulgur`'s own does, measured from its `dem_datatables`:
/// <c>m_nSequence</c> and <c>m_nNewSequenceParity</c> on <c>DT_BaseAnimating</c>, the cycle on
/// <c>DT_ServerAnimationData</c>, and <c>m_flAnimTime</c> in its own <c>DT_AnimTimeMustBeFirst</c> —
/// which is Valve's trick to force that field first on the wire, and the reason asking for it under
/// <c>DT_BaseEntity</c> silently matches nothing.
/// </remarks>
internal static class SyntheticProp
{
    /// <summary>Class id of the prop this fixture networks.</summary>
    public const int PropClassId = 0;

    /// <summary>Entity slot the prop occupies.</summary>
    public const int PropEntityIndex = 9;

    /// <summary>Precache index of the prop's model.</summary>
    public const int Model = 1;

    /// <summary>What that index names.</summary>
    public const string ModelPath = "models/props_gameplay/resupply_locker.mdl";

    /// <summary>A demo of one prop over several snapshots.</summary>
    /// <param name="frames">Tick, sequence and parity for each snapshot; the first creates.</param>
    /// <returns>The demo's bytes.</returns>
    public static byte[] Demo(params (int Tick, int Sequence, int Parity)[] frames)
    {
        ArgumentNullException.ThrowIfNull(frames);

        (int, int, int, int)[] widened = new (int, int, int, int)[frames.Length];

        for (int index = 0; index < frames.Length; index++)
        {
            // Frame reset held at zero: a server-animated prop's restart signal is the parity, and
            // a fixture that moved both could not tell which one the code under test read.
            widened[index] =
                (frames[index].Tick, frames[index].Sequence, frames[index].Parity, 0);
        }

        return Demo(clientSideAnimation: false, widened);
    }

    /// <summary>The same, saying whether the CLIENT advances the cycle.</summary>
    /// <param name="clientSideAnimation">
    /// <c>m_bClientSideAnimation</c>. Measured 1 on `cp_fulgur`'s spawn cabinets, which send no
    /// server cycle at all — so their restart signal is the frame-reset toggle rather than the
    /// sequence parity (`c_baseanimating.cpp:5021`).
    /// </param>
    /// <param name="frames">Tick, sequence and parity for each snapshot.</param>
    /// <returns>The demo's bytes.</returns>
    public static byte[] Demo(
        bool clientSideAnimation,
        params (int Tick, int Sequence, int Parity, int FrameReset)[] frames)
    {
        ArgumentNullException.ThrowIfNull(frames);

        (int, int, int, int, int)[] widened = new (int, int, int, int, int)[frames.Length];

        for (int index = 0; index < frames.Length; index++)
        {
            // No-interp parity held at zero, for the reason the frame reset is held at zero above:
            // a fixture that moved two signals at once could not say which one the code read.
            widened[index] = (
                frames[index].Tick,
                frames[index].Sequence,
                frames[index].Parity,
                frames[index].FrameReset,
                0);
        }

        return Demo(clientSideAnimation, widened);
    }

    /// <summary>The same, plus the discontinuity parity — <c>m_ubInterpolationFrame</c>.</summary>
    /// <param name="clientSideAnimation">Whether the client advances the cycle.</param>
    /// <param name="frames">
    /// Tick, sequence, sequence parity, frame reset and no-interp parity for each snapshot.
    /// </param>
    /// <returns>The demo's bytes.</returns>
    /// <remarks>
    /// **Its own knob, deliberately separable from the sequence parity** (B346). The engine treats
    /// them as different signals — one creates a transition, the other destroys the queue — so a
    /// fixture that could only move both together could not test either.
    /// </remarks>
    public static byte[] Demo(
        bool clientSideAnimation,
        params (int Tick, int Sequence, int Parity, int FrameReset, int NoInterp)[] frames)
    {
        ArgumentNullException.ThrowIfNull(frames);

        (int, int, int, int, int, int)[] widened = new (int, int, int, int, int, int)[frames.Length];

        for (int index = 0; index < frames.Length; index++)
        {
            // The chamber held settled — goal equal to the fixed current tube of zero — for the
            // reason the two knobs above are held still: a fixture that moved everything at once
            // could not say which signal the code read.
            widened[index] = (
                frames[index].Tick,
                frames[index].Sequence,
                frames[index].Parity,
                frames[index].FrameReset,
                frames[index].NoInterp,
                0);
        }

        return Demo(clientSideAnimation, widened);
    }

    /// <summary>The same, plus the grenade launcher's GOAL tube — <c>m_iGoalTube</c>.</summary>
    /// <param name="clientSideAnimation">Whether the client advances the cycle.</param>
    /// <param name="frames">
    /// Tick, sequence, sequence parity, frame reset, no-interp parity and goal tube per snapshot.
    /// </param>
    /// <returns>The demo's bytes.</returns>
    /// <remarks>
    /// **The current tube is fixed at zero and only the GOAL varies** (B348), because the signal
    /// under test is the moment the two stop being equal — which is what `OnDataChanged` stamps a
    /// clock on (<c>tf_weapon_grenadelauncher.cpp:626</c>). Varying both would let a reader that
    /// watched the wrong one still pass.
    /// </remarks>
    public static byte[] Demo(
        bool clientSideAnimation,
        params (int Tick, int Sequence, int Parity, int FrameReset, int NoInterp, int GoalTube)[]
            frames)
    {
        ArgumentNullException.ThrowIfNull(frames);

        DemoSchema schema = Schema();

        EntityDecoder decoder = new(
            schema, EntityDecoder.ClassIdBits(schema.ServerClasses.Count));

        List<DemoCommand> commands =
        [
            SyntheticDemo.Packet(
                SyntheticDemo.DefaultProtocol,
                0,
                ServerInfo(),
                SyntheticDemo.StringTable(
                    ModelPrecache.TableName, [string.Empty, ModelPath], maxEntries: 8)),
            SyntheticDemo.DataTables(schema),
        ];

        IReadOnlyList<FlatProperty> flat = decoder.FlattenedFor(PropClassId);

        for (int index = 0; index < frames.Length; index++)
        {
            (int tick, int sequence, int parity, int frameReset, int noInterp, int goalTube) =
                frames[index];

            List<DecodedProperty> properties =
            [
                Property(flat, "m_nModelIndex", PropertyValue.FromInt(Model)),
                Property(flat, "m_ubInterpolationFrame", PropertyValue.FromInt(noInterp)),

                // **A minigun state on every frame** (B347). Three is `AC_STATE_SPINNING`, chosen
                // because it is not the default: a zero would be indistinguishable from the value
                // never arriving, which is the case this fixture exists to tell apart.
                Property(flat, "m_iWeaponState", PropertyValue.FromInt(3)),

                // **The chamber's current tube is fixed at zero; only the goal varies** (B348), so
                // the moment they stop being equal is unambiguous.
                Property(flat, "m_iCurrentTube", PropertyValue.FromInt(0)),
                Property(flat, "m_iGoalTube", PropertyValue.FromInt(goalTube)),
                Property(flat, "m_nSequence", PropertyValue.FromInt(sequence)),
                Property(flat, "m_nNewSequenceParity", PropertyValue.FromInt(parity)),
                Property(
                    flat,
                    "m_bClientSideAnimation",
                    PropertyValue.FromInt(clientSideAnimation ? 1 : 0)),

                // **Its own knob, not derived from the parity.** A first version computed it as
                // `parity & 1`, which coupled the two signals — and the control that needed the
                // toggle to stay still while the parity moved could not be written at all.
                Property(flat, "m_bClientSideFrameReset", PropertyValue.FromInt(frameReset)),

                // **Cycle zero every time, because that is what the recording carries.** Measured
                // on `cp_fulgur`: every cabinet keyframe reads 0.00. The server states where the
                // animation begins and leaves the advancing to the client.
                Property(flat, "m_flCycle", PropertyValue.FromFloat(0f)),

                // A position, so the prop is placed and reaches the scene rather than being
                // dropped for having nowhere to be.
                Property(flat, "m_vecOrigin", PropertyValue.FromVectorXY(64f, 0f)),
                Property(flat, "m_vecOrigin[2]", PropertyValue.FromFloat(0f)),
            ];

            properties.Sort((left, right) => left.Index.CompareTo(right.Index));

            DecodedEntity prop = new(
                PropEntityIndex,
                PropClassId,
                SerialNumber: 3,
                index == 0 ? EntityUpdateType.Enter : EntityUpdateType.Delta,
                properties);

            byte[] body = decoder.EncodeEntities(
                [prop], [], isDelta: index > 0, 0, out int bits);

            commands.Add(SyntheticDemo.Packet(
                SyntheticDemo.DefaultProtocol,
                tick,
                new PacketEntitiesMessage(
                    MaxEntries: 64,
                    IsDelta: index > 0,
                    DeltaFromTick: index > 0 ? frames[index - 1].Tick : null,
                    BaselineIndex: false,
                    UpdatedEntries: 1,
                    LengthBits: bits,
                    UpdateBaseline: false,
                    Body: body)));
        }

        return SyntheticDemo.From(SyntheticDemo.DefaultProtocol, [.. commands]);
    }

    private static DecodedProperty Property(
        IReadOnlyList<FlatProperty> flat, string name, PropertyValue value)
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

    private static ServerInfoMessage ServerInfo() => new(
        NetworkProtocol: SyntheticDemo.DefaultProtocol,
        ServerCount: 1,
        IsSourceTv: true,
        IsDedicated: true,
        MapCrc: 0,
        MaxClasses: 1,
        MapHash: new byte[16],
        PlayerSlot: 0,
        MaxPlayers: 24,
        IntervalPerTick: 1f / 66.67f,
        Platform: 'w',
        GameDirectory: "tf",
        Map: "cp_fulgur",
        Skybox: "sky_tf2_04",
        ServerName: "synthetic",
        IsReplay: false);

    /// <summary>One prop class, with the tables a real <c>CDynamicProp</c> declares.</summary>
    private static DemoSchema Schema() => new(
        [
            new SendTable("DT_AnimTimeMustBeFirst", NeedsDecoder: true,
            [
                new SendProperty(SendPropType.Int, "m_flAnimTime", 1, string.Empty, 0f, 0f, 8, 0),
            ]),
            new SendTable("DT_BaseEntity", NeedsDecoder: true,
            [
                new SendProperty(SendPropType.Int, "m_nModelIndex", 1, string.Empty, 0f, 0f, 13, 0),
                new SendProperty(
                    SendPropType.VectorXY, "m_vecOrigin", 1, string.Empty, -16384f, 16384f, 32, 0),
                new SendProperty(
                    SendPropType.Float, "m_vecOrigin[2]", 1, string.Empty, -16384f, 16384f, 32, 0),
                // **The discontinuity parity, on DT_BaseEntity where the engine puts it** — a
                // teleport is a fact about the entity rather than about its animation (B346).
                // `NOINTERP_PARITY_MAX_BITS` is 3, matching `SendPropInt(SENDINFO(
                // m_ubInterpolationFrame), NOINTERP_PARITY_MAX_BITS, SPROP_UNSIGNED)`
                // (`baseentity.cpp:273`).
                new SendProperty(
                    SendPropType.Int, "m_ubInterpolationFrame", 1, string.Empty, 0f, 0f, 3, 0),
                new SendProperty(
                    SendPropType.DataTable, "animtime", 1, "DT_AnimTimeMustBeFirst", 0f, 0f, 0, 0),
            ]),
            // **The minigun's wind-up state** (B347), four bits unsigned as the engine sends it
            // (`tf_weapon_minigun.cpp:51`). On this fixture's one prop class because the question is
            // whether the value reaches a POSE, which is the same hop for every entity — not
            // whether a minigun is classified as one.
            new SendTable("DT_WeaponMinigun", NeedsDecoder: true,
            [
                new SendProperty(
                    SendPropType.Int, "m_iWeaponState", 1, string.Empty, 0f, 0f, 4, 0),
            ]),
            // **The grenade launcher's two tube numbers** (B348), both networked as the engine
            // sends them (`tf_weapon_grenadelauncher.cpp:59`). On this fixture's one prop class for
            // the same reason as the minigun's state above.
            new SendTable("DT_WeaponGrenadeLauncher", NeedsDecoder: true,
            [
                new SendProperty(
                    SendPropType.Int, "m_iCurrentTube", 1, string.Empty, 0f, 0f, 4, 0),
                new SendProperty(
                    SendPropType.Int, "m_iGoalTube", 1, string.Empty, 0f, 0f, 4, 0),
            ]),
            new SendTable("DT_ServerAnimationData", NeedsDecoder: true,
            [
                new SendProperty(SendPropType.Float, "m_flCycle", 0, string.Empty, 0f, 1f, 15, 0),
            ]),
            new SendTable("DT_BaseAnimating", NeedsDecoder: true,
            [
                new SendProperty(SendPropType.Int, "m_nSequence", 1, string.Empty, 0f, 0f, 12, 0),
                new SendProperty(
                    SendPropType.Int, "m_nNewSequenceParity", 1, string.Empty, 0f, 0f, 3, 0),
                new SendProperty(
                    SendPropType.Int, "m_bClientSideAnimation", 1, string.Empty, 0f, 0f, 1, 0),
                new SendProperty(
                    SendPropType.Int, "m_bClientSideFrameReset", 1, string.Empty, 0f, 0f, 1, 0),
                new SendProperty(
                    SendPropType.DataTable, "baseentity", 1, "DT_BaseEntity", 0f, 0f, 0, 0),
                new SendProperty(
                    SendPropType.DataTable, "serveranimdata", 1, "DT_ServerAnimationData", 0f, 0f, 0, 0),
            ]),
            new SendTable("DT_DynamicProp", NeedsDecoder: true,
            [
                new SendProperty(
                    SendPropType.DataTable, "baseanimating", 1, "DT_BaseAnimating", 0f, 0f, 0, 0),
                new SendProperty(
                    SendPropType.DataTable, "minigun", 1, "DT_WeaponMinigun", 0f, 0f, 0, 0),
                new SendProperty(
                    SendPropType.DataTable, "launcher", 1, "DT_WeaponGrenadeLauncher",
                    0f, 0f, 0, 0),
            ]),
        ],
        [new ServerClass(PropClassId, "CDynamicProp", "DT_DynamicProp")]);
}
