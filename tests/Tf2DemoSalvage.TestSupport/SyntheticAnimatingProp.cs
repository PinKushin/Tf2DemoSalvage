using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Schema;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests;

/// <summary>A demo of one animating prop whose updates set its model, pose parameters, cycle and sequence parity (B870).</summary>
/// <remarks>
/// Shared by the model-reset conformance tests and the window check's wiring test, which both need an update that SETS a
/// model — the moment `OnNewModel` runs in the engine.
/// </remarks>
internal static class SyntheticAnimatingProp
{
    /// <summary>The prop's slot.</summary>
    public const int EntityIndex = 7;

    private const int ClassId = 0;

    /// <summary>One update per frame: tick, model precache index (into <paramref name="models"/>), pose, cycle, parity.</summary>
    /// <param name="models">The model precache table; index 0 is the engine's "no model" and should be empty.</param>
    /// <param name="frames">The updates; the first creates the entity.</param>
    /// <returns>The demo's bytes.</returns>
    public static byte[] Demo(
        IReadOnlyList<string> models, params (int Tick, int ModelIndex, float[] Pose, float? Cycle, int? Parity)[] frames) =>
        Demo(models, animationLag: null, frames);

    /// <summary>The same, each update also stating an animation time <paramref name="animationLag"/> ticks before it.</summary>
    /// <param name="models">The model precache table.</param>
    /// <param name="animationLag">
    /// How far `m_flAnimTime` trails the packet tick, encoded as `SendProxy_AnimTime` does — an offset from the entity's
    /// network base (`EntityState.NetworkBase`); null sends no animation time, so it is the arrival.
    /// </param>
    /// <param name="frames">The updates; the first creates the entity.</param>
    /// <returns>The demo's bytes.</returns>
    public static byte[] Demo(
        IReadOnlyList<string> models,
        int? animationLag,
        params (int Tick, int ModelIndex, float[] Pose, float? Cycle, int? Parity)[] frames) =>
        Demo(models, animationLag, kinds: null, frames);

    /// <summary>The same, each update of the kind given: an `Enter`, a `Delta` or a `Leave` (B871).</summary>
    /// <param name="models">The model precache table.</param>
    /// <param name="animationLag">How far `m_flAnimTime` trails the packet tick; null for none.</param>
    /// <param name="kinds">One per frame; null for `Enter` first and `Delta` after. A `Leave` carries no properties.</param>
    /// <param name="frames">The updates.</param>
    /// <returns>The demo's bytes.</returns>
    public static byte[] Demo(
        IReadOnlyList<string> models,
        int? animationLag,
        IReadOnlyList<EntityUpdateType>? kinds,
        params (int Tick, int ModelIndex, float[] Pose, float? Cycle, int? Parity)[] frames) =>
        Demo(models, animationLag, kinds, fullPackets: null, frames);

    /// <summary>The same, with the frames whose packet is full (non-delta) named; the first always is.</summary>
    /// <param name="models">The model precache table.</param>
    /// <param name="animationLag">How far `m_flAnimTime` trails the packet tick; null for none.</param>
    /// <param name="kinds">One per frame; null for `Enter` first and `Delta` after.</param>
    /// <param name="fullPackets">The indices of the frames sent in a full packet besides the first; null for none.</param>
    /// <param name="frames">The updates.</param>
    /// <returns>The demo's bytes.</returns>
    public static byte[] Demo(
        IReadOnlyList<string> models,
        int? animationLag,
        IReadOnlyList<EntityUpdateType>? kinds,
        IReadOnlySet<int>? fullPackets,
        params (int Tick, int ModelIndex, float[] Pose, float? Cycle, int? Parity)[] frames)
    {
        if (kinds is not null && kinds.Count != frames.Length)
        {
            throw new ArgumentException($"{kinds.Count} kinds for {frames.Length} frames; give one per frame", nameof(kinds));
        }

        DemoSchema schema = Schema();

        EntityDecoder decoder = new(schema, EntityDecoder.ClassIdBits(schema.ServerClasses.Count));

        List<DemoCommand> commands =
        [
            SyntheticDemo.Packet(
                SyntheticDemo.DefaultProtocol,
                0,
                SyntheticDemo.StringTable(ModelPrecache.TableName, [.. models], maxEntries: 8)),
            SyntheticDemo.DataTables(schema),
        ];

        for (int index = 0; index < frames.Length; index++)
        {
            (int tick, int modelIndex, float[] pose, float? cycle, int? parity) = frames[index];

            IReadOnlyList<FlatProperty> flat = decoder.FlattenedFor(ClassId);

            List<DecodedProperty> properties =
            [
                Property(flat, "m_nModelIndex", PropertyValue.FromInt(modelIndex)),
                Property(flat, "m_vecOrigin", PropertyValue.FromVectorXY(64f, 0f)),
                Property(flat, "m_vecOrigin[2]", PropertyValue.FromFloat(0f)),
            ];

            for (int parameter = 0; parameter < pose.Length; parameter++)
            {
                properties.Add(Property(flat, $"{parameter:000}", PropertyValue.FromFloat(pose[parameter])));
            }

            if (animationLag is { } lag)
            {
                // Beside a simulation time on the packet tick, as a real entity sends both: the decode carries the
                // animation time onto the demo's axis through the simulation time's base.
                int networkBase = EntityState.NetworkBase(tick, EntityIndex);
                properties.Add(Property(flat, "m_flAnimTime", PropertyValue.FromInt((tick - lag - networkBase) & 0xff)));
                properties.Add(Property(flat, "m_flSimulationTime", PropertyValue.FromInt((tick - networkBase) & 0xff)));
            }

            if (cycle is { } stated)
            {
                properties.Add(Property(flat, "m_flCycle", PropertyValue.FromFloat(stated)));
            }

            if (parity is { } counter)
            {
                properties.Add(Property(flat, "m_nNewSequenceParity", PropertyValue.FromInt(counter)));
            }

            properties.Sort((left, right) => left.Index.CompareTo(right.Index));

            EntityUpdateType kind = kinds?[index] ?? (index == 0 ? EntityUpdateType.Enter : EntityUpdateType.Delta);

            DecodedEntity prop = new(
                EntityIndex,
                ClassId,
                SerialNumber: kind == EntityUpdateType.Enter ? 3 : 0,
                kind,
                kind == EntityUpdateType.Leave ? [] : properties);

            bool isDelta = index > 0 && fullPackets?.Contains(index) != true;

            byte[] body = decoder.EncodeEntities([prop], [], isDelta, 0, out int bits);

            PacketEntitiesMessage entities = new(
                MaxEntries: 64,
                IsDelta: isDelta,
                DeltaFromTick: isDelta ? frames[index - 1].Tick : null,
                BaselineIndex: false,
                UpdatedEntries: 1,
                LengthBits: bits,
                UpdateBaseline: false,
                Body: body);

            // A tick-encoded time decodes against the SERVER tick, which `net_Tick` carries (`EntityState.NoteTickEncodedTimes`).
            commands.Add(animationLag is null
                ? SyntheticDemo.Packet(SyntheticDemo.DefaultProtocol, tick, entities)
                : SyntheticDemo.Packet(SyntheticDemo.DefaultProtocol, tick, new NetTickMessage(tick, 0, 0), entities));
        }

        return SyntheticDemo.From(SyntheticDemo.DefaultProtocol, [.. commands]);
    }

    private static DecodedProperty Property(IReadOnlyList<FlatProperty> flat, string name, PropertyValue value)
    {
        for (int candidate = 0; candidate < flat.Count; candidate++)
        {
            if (string.Equals(flat[candidate].Property.Name, name, StringComparison.Ordinal))
            {
                return new DecodedProperty(candidate, flat[candidate], value);
            }
        }

        throw new InvalidOperationException($"the fixture schema declares no {name}");
    }

    /// <summary>One animating prop class: a model index, a position, two pose parameters, a cycle and a parity.</summary>
    private static DemoSchema Schema() => new(
        [
            new SendTable("DT_BaseEntity", NeedsDecoder: true,
            [
                new SendProperty(SendPropType.Int, "m_nModelIndex", 1, string.Empty, 0f, 0f, 13, 0),
                new SendProperty(SendPropType.Int, "m_flSimulationTime", 1, string.Empty, 0f, 0f, 8, 0),
                new SendProperty(SendPropType.VectorXY, "m_vecOrigin", 1, string.Empty, -16384f, 16384f, 32, 0),
                new SendProperty(SendPropType.Float, "m_vecOrigin[2]", 1, string.Empty, -16384f, 16384f, 32, 0),
            ]),
            new SendTable("m_flPoseParameter", NeedsDecoder: false,
            [
                new SendProperty(SendPropType.Float, "000", 1, string.Empty, 0f, 1f, 32, 0),
                new SendProperty(SendPropType.Float, "001", 1, string.Empty, 0f, 1f, 32, 0),
            ]),
            new SendTable("DT_AnimTimeMustBeFirst", NeedsDecoder: false,
            [
                new SendProperty(SendPropType.Int, "m_flAnimTime", 1, string.Empty, 0f, 0f, 8, 0),
            ]),
            new SendTable("DT_BaseAnimating", NeedsDecoder: false,
            [
                new SendProperty(SendPropType.Int, "m_nNewSequenceParity", 1, string.Empty, 0f, 0f, 3, 0),
            ]),
            new SendTable("DT_ServerAnimationData", NeedsDecoder: false,
            [
                new SendProperty(SendPropType.Float, "m_flCycle", 1, string.Empty, 0f, 1f, 32, 0),
            ]),
            new SendTable("DT_DynamicProp", NeedsDecoder: true,
            [
                new SendProperty(SendPropType.DataTable, "AnimTimeMustBeFirst", 1, "DT_AnimTimeMustBeFirst", 0f, 0f, 0, 0),
                new SendProperty(SendPropType.DataTable, "baseentity", 1, "DT_BaseEntity", 0f, 0f, 0, 0),
                new SendProperty(SendPropType.DataTable, "m_flPoseParameter", 1, "m_flPoseParameter", 0f, 0f, 0, 0),
                new SendProperty(SendPropType.DataTable, "baseanimating", 1, "DT_BaseAnimating", 0f, 0f, 0, 0),
                new SendProperty(SendPropType.DataTable, "serveranimdata", 1, "DT_ServerAnimationData", 0f, 0f, 0, 0),
            ]),
        ],
        [new ServerClass(ClassId, "CDynamicProp", "DT_DynamicProp")]);
}
