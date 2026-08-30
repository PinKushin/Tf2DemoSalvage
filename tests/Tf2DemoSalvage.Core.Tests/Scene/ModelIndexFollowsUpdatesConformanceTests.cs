using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Schema;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// The engine re-applies an entity's model on EVERY update, not only when it is created.
/// </summary>
/// <remarks>
/// **<c>C_BaseEntity::PostDataUpdate</c>, <c>client/c_baseentity.cpp:2609</c>:**
///
/// <code>
///   // Deal with hierarchy. ...
///   HierarchySetParent(m_hNetworkMoveParent);
///   MarkMessageReceived();
///   // Make sure that the correct model is referenced for this entity
///   ValidateModelIndex();
///   // If this entity was new, then latch in various values no matter what.
///   if ( updateType == DATA_UPDATE_CREATED ) { ... }
/// </code>
///
/// **Both calls sit ABOVE the <c>DATA_UPDATE_CREATED</c> test, so both run every update.**
/// `ValidateModelIndex` ends in `SetModelByIndex( m_nModelIndex )` (<c>c_baseentity.cpp:2531</c>),
/// which re-points the entity at whatever the index now says. This project already follows the
/// first of the pair — `ScenePropTrack.AttachedTo` is assigned on every update, with a comment
/// citing the same lines — and fixed the second at construction. Half a mechanism.
///
/// **What that cost, measured on `cp_fulgur` before the fix.** An entity's creating update is a
/// delta against its class's INSTANCE BASELINE, so it carries only what differs; everything else
/// comes from the baseline, which is one representative entity's state. Slot 432 — the BLU spawn's
/// windowed door — was created from a two-property update:
///
/// <code>
///   Enter 432 serial 998 props 2  modelindex 1154 origin (3440 -2096 240)   &lt;- baseline
///   Enter 432 serial 998 props 11 modelindex 1177 origin (2 0 -59)          &lt;- the real values
/// </code>
///
/// Index 1154 is `models/props_gameplay/resupply_locker.mdl` and `(3440 -2096 240)` is
/// `prop_locker_blu_5`'s world origin, read straight out of `cp_fulgur`'s entity lump. The door was
/// therefore named a resupply cabinet for the rest of the recording, and nine other entities took
/// the same baseline's identity the same way. The owner's report was that the spawn gates and the
/// health cabinet do not draw.
///
/// **The baseline merge itself is correct** — `CL_CopyNewEntity` does exactly that, and the engine
/// simply overwrites the guess on the next update because it re-reads the index. Removing the merge
/// would trade this defect for B132's.
///
/// Synthetic, in `Core.Tests`, because this is a decode-and-behaviour claim with ground truth the
/// test put there (D38).
/// </remarks>
public sealed class ModelIndexFollowsUpdatesConformanceTests
{
    /// <summary>Class id of the prop this fixture networks.</summary>
    private const int PropClassId = 0;

    /// <summary>Slot the prop occupies.</summary>
    private const int PropEntityIndex = 7;

    /// <summary>Precache index of the model the entity is created with.</summary>
    private const int FirstModel = 1;

    /// <summary>Precache index of the model a later update gives it.</summary>
    private const int SecondModel = 2;

    private const string FirstPath = "models/props_gameplay/resupply_locker.mdl";
    private const string SecondPath = "models/props_gameplay/windowed_door.mdl";

    [Test]
    public void Build_APropWhoseModelIndexChanges_TakesTheNewModel()
    {
        // The measured shape, reduced: created carrying one model, corrected on the next update.
        DemoTimeline timeline = DemoTimeline.Build(
            Demo((0, FirstModel), (10, SecondModel)));

        ScenePropTrack track = timeline.Props.Single(
            track => track.EntityIndex == PropEntityIndex);

        track.ModelPath.ShouldBe(
            SecondPath,
            "PostDataUpdate calls ValidateModelIndex on every update, so the entity follows its "
            + "index rather than keeping whatever it was created with");
    }

    [Test]
    public void Build_APropWhoseModelIndexNeverChanges_KeepsIt()
    {
        // **The control, and it is not redundant.** A fix that simply took the LAST model seen
        // would pass the test above and this one; a fix that took the FIRST passes only this one.
        // Without it, "follows the index" and "always reports the second model" are the same
        // observation.
        DemoTimeline timeline = DemoTimeline.Build(
            Demo((0, FirstModel), (10, FirstModel)));

        timeline.Props
            .Single(track => track.EntityIndex == PropEntityIndex)
            .ModelPath
            .ShouldBe(FirstPath);
    }

    [Test]
    public void Build_APropWhoseModelIndexChanges_StaysOneTrack()
    {
        // **The other control, and it guards a fix in the opposite direction.** Ending the track
        // when the model changes would also make the first test pass, and it is wrong for the
        // reason `DemoTimeline` already records: `team_control_point.cpp:569` calls `SetModel` on
        // every capture, so a point changing hands would split into several objects. Identity is
        // the serial number; the model is just a property.
        DemoTimeline timeline = DemoTimeline.Build(
            Demo((0, FirstModel), (10, SecondModel)));

        timeline.Props
            .Count(track => track.EntityIndex == PropEntityIndex)
            .ShouldBe(1, "a model change is not a new entity");
    }

    /// <summary>A demo of one prop, sent once per entry with the model index given.</summary>
    /// <param name="frames">Tick and precache index for each snapshot; the first creates.</param>
    /// <returns>The demo's bytes.</returns>
    private static byte[] Demo(params (int Tick, int ModelIndex)[] frames)
    {
        DemoSchema schema = Schema();

        EntityDecoder decoder = new(
            schema, EntityDecoder.ClassIdBits(schema.ServerClasses.Count));

        // Index 0 is the engine's "no model" placeholder and is never named, which is why the two
        // real paths start at 1 — `ModelPrecache.Apply` skips an empty entry outright.
        List<DemoCommand> commands =
        [
            SyntheticDemo.Packet(
                SyntheticDemo.DefaultProtocol,
                0,
                SyntheticDemo.StringTable(
                    ModelPrecache.TableName,
                    [string.Empty, FirstPath, SecondPath],
                    maxEntries: 8)),
            SyntheticDemo.DataTables(schema),
        ];

        for (int index = 0; index < frames.Length; index++)
        {
            (int tick, int modelIndex) = frames[index];

            IReadOnlyList<FlatProperty> flat = decoder.FlattenedFor(PropClassId);

            List<DecodedProperty> properties =
            [
                Property(flat, "m_nModelIndex", PropertyValue.FromInt(modelIndex)),

                // An origin every time, so the track has a keyframe to hold and the test is not
                // measuring an entity the scene declined to place.
                Property(flat, "m_vecOrigin", PropertyValue.FromVectorXY(64f, 0f)),
                Property(flat, "m_vecOrigin[2]", PropertyValue.FromFloat(0f)),
            ];

            properties.Sort((left, right) => left.Index.CompareTo(right.Index));

            // **The same serial throughout, which is what makes this one entity.** A track ends on
            // a serial change (`ScenePropTrack.Continues`), so varying it here would test slot
            // reuse rather than a model change.
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

    /// <summary>One property, resolved to the flattened index the encoder needs.</summary>
    private static DecodedProperty Property(
        IReadOnlyList<FlatProperty> flat, string name, PropertyValue value)
    {
        int index = -1;

        for (int candidate = 0; candidate < flat.Count; candidate++)
        {
            if (string.Equals(
                flat[candidate].Property.Name, name, StringComparison.Ordinal))
            {
                index = candidate;
                break;
            }
        }

        if (index < 0)
        {
            throw new InvalidOperationException($"the fixture schema declares no {name}");
        }

        return new DecodedProperty(index, flat[index], value);
    }

    /// <summary>A schema of one prop class, carrying a model index and a position.</summary>
    /// <remarks>
    /// Deliberately minimal. `SyntheticPlayer.Schema` describes a `CTFPlayer`, and a player is not
    /// a prop: it lands in `PlayerTracks` rather than `Props`, which is the list this asks about.
    /// </remarks>
    private static DemoSchema Schema() => new(
        [
            new SendTable("DT_BaseEntity", NeedsDecoder: true,
            [
                new SendProperty(SendPropType.Int, "m_nModelIndex", 1, string.Empty, 0f, 0f, 13, 0),
                new SendProperty(
                    SendPropType.VectorXY, "m_vecOrigin", 1, string.Empty, -16384f, 16384f, 32, 0),
                new SendProperty(
                    SendPropType.Float, "m_vecOrigin[2]", 1, string.Empty, -16384f, 16384f, 32, 0),
            ]),
            new SendTable("DT_DynamicProp", NeedsDecoder: true,
            [
                new SendProperty(
                    SendPropType.DataTable, "baseentity", 1, "DT_BaseEntity", 0f, 0f, 0, 0),
            ]),
        ],
        [new ServerClass(PropClassId, "CDynamicProp", "DT_DynamicProp")]);
}
