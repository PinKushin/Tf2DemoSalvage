using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Schema;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// A coach's apparent team is the STUDENT's — <c>C_TFPlayer::IsEnemyPlayer</c> (B313).
/// </summary>
/// <remarks>
/// **<c>c_tf_player.cpp:10136</c>**, and the engine repeats it where the disguise colour is chosen
/// (`:5394`) under the comment *"if we are coaching, use the team of the student"*:
///
/// <code>
///   int iMyApparentTeam = GetTeamNumber();
///   if ( m_bIsCoaching &amp;&amp; m_hStudent )
///       iMyApparentTeam = m_hStudent->GetTeamNumber();
/// </code>
///
/// **A coach has no team of their own that means anything.** They sit on `TEAM_SPECTATOR` while
/// attached to a player, so without the substitution the recorder's team answers "neither" and every
/// player in a coached recording reads as friendly — the enemies the coach can see included.
///
/// **Synthetic, because the corpus cannot reach this** (D38, and here there is no choice):
/// `m_bIsCoaching` is 0 on all 33 of its appearances in `z1800`, and no coaching demo exists here.
/// A hand-built schema and two entities is what makes the branch executable at all — and this
/// entry was filed as implemented-but-unexercised until it was written.
///
/// **Through the real decode types**, not a hand-set property bag: the table, the flattened list and
/// `EntityStateTable.Apply` are production's, so a key spelled wrong here fails exactly as it would
/// on a demo. That is the point — the risk in four lines transcribed from the engine is the two
/// property NAMES, and only a fixture that goes through the decoder can test them.
/// </remarks>
public sealed class CoachingTeamConformanceTests
{
    [Test]
    public void Coached_WhenTheRecorderIsCoaching_AnswersWithTheStudentsTeam()
    {
        EntityStateTable entities = Table(coaching: 1, student: StudentIndex, studentTeam: Blu);

        entities.TryGet(RecorderIndex, out EntityState? recorder).ShouldBeTrue();

        DemoTimeline.Coached(entities, recorder, null)
            .ShouldBe(Blu, "the coach's apparent team is whoever they are attached to");
    }

    /// <remarks>
    /// **The control, and it is what says the FLAG did it.** The same student handle with
    /// `m_bIsCoaching` clear must answer null, so the caller falls through to the recorder's own
    /// team — a substitution that ignored the flag would pass the test above and fail here.
    /// </remarks>
    [Test]
    public void Coached_WhenTheRecorderIsNotCoaching_AnswersNothing()
    {
        EntityStateTable entities = Table(coaching: 0, student: StudentIndex, studentTeam: Blu);

        entities.TryGet(RecorderIndex, out EntityState? recorder).ShouldBeTrue();

        DemoTimeline.Coached(entities, recorder, null).ShouldBeNull();
    }

    /// <remarks>
    /// **The invalid handle is the case every ordinary demo carries**, and it is the one a masked
    /// dereference gets wrong. `m_hStudent` sits at 2097151 — 21 bits of ones — whose low eleven
    /// mask to **2047**, a perfectly ordinary-looking slot. A reader that masked would resolve every
    /// non-coaching player to entity 2047 rather than to nothing (B231), which is why this goes
    /// through `EntityStateTable.Resolve`.
    ///
    /// **Slot 2047 is OCCUPIED in this fixture, and without that the test cannot fail.** A sabotage
    /// exposed it: with the slot empty, masking gives 2047, the lookup finds nobody, and the answer
    /// is null — the same null the correct code returns, for a different reason. Correct and broken
    /// agreed, which is the wrong-CONDITION fault. Putting a player there with a DIFFERENT team
    /// separates them: masking now answers RED, and resolving still answers nothing.
    /// </remarks>
    [Test]
    public void Coached_WithTheInvalidStudentHandle_AnswersNothingRatherThanEntity2047()
    {
        EntityStateTable entities = Table(coaching: 1, student: InvalidHandle, studentTeam: Blu);

        entities.TryGet(MaskedSlot, out EntityState? bystander).ShouldBeTrue(
            "the control: slot 2047 is occupied, so masking has something wrong to find");

        bystander.Integer("DT_BaseEntity.m_iTeamNum").ShouldBe(
            Red, "and it is on the OTHER team, so a masked answer is distinguishable");

        DemoTimeline.Coached(entities, Recorder(entities), null)
            .ShouldBeNull("2097151 names nothing; masking it would answer with slot 2047's team");
    }

    /// <summary>The recorder, which every test needs and none should assert about.</summary>
    private static EntityState Recorder(EntityStateTable entities)
    {
        entities.TryGet(RecorderIndex, out EntityState? recorder).ShouldBeTrue();

        return recorder;
    }

    /// <summary>The recorder's entity slot, and the student's.</summary>
    private const int RecorderIndex = 1;

    private const int StudentIndex = 2;

    /// <summary>`INVALID_EHANDLE_INDEX` — 21 bits of ones, as a demo carries it.</summary>
    private const int InvalidHandle = (1 << 21) - 1;

    /// <summary>What the invalid handle's low eleven bits name — <c>2097151 &amp; 2047</c>.</summary>
    private const int MaskedSlot = 2047;

    private const int Blu = 3;

    private const int Red = 2;

    private const int ClassId = 0;

    private const int Serial = 5;

    /// <summary>A table holding a recorder and a student, applied through the real decoder.</summary>
    /// <remarks>
    /// **The student's handle carries its SERIAL**, because `Resolve` checks it against the slot's
    /// occupant — a handle built from the index alone would dereference to nothing and every test
    /// here would pass for the wrong reason.
    /// </remarks>
    private static EntityStateTable Table(int coaching, int student, int studentTeam)
    {
        EntityDecoder decoder = Decoder();
        EntityStateTable entities = new(decoder);

        entities.SetClassName(ClassId, "CTFPlayer");

        // The student first, so the slot it names is occupied when the recorder's handle is read.
        entities.Apply(new DecodedEntity(
            StudentIndex, ClassId, Serial, EntityUpdateType.Enter,
            [Property(decoder, "m_iTeamNum", studentTeam)]));

        // **A bystander at slot 2047, on the other team.** This is what the invalid handle masks
        // to, and without somebody standing there a masked dereference and a correct one both
        // answer null — the same observation for different reasons, which no assertion can split.
        entities.Apply(new DecodedEntity(
            MaskedSlot, ClassId, Serial, EntityUpdateType.Enter,
            [Property(decoder, "m_iTeamNum", Red)]));

        int handle = student == InvalidHandle
            ? InvalidHandle
            : student | (Serial << 11);

        entities.Apply(new DecodedEntity(
            RecorderIndex, ClassId, Serial, EntityUpdateType.Enter,
            [
                Property(decoder, "m_bIsCoaching", coaching),
                Property(decoder, "m_hStudent", handle),
            ]));

        return entities;
    }

    /// <summary>One property, found in the flattened list the decoder built.</summary>
    private static DecodedProperty Property(EntityDecoder decoder, string name, int value)
    {
        IReadOnlyList<FlatProperty> flat = decoder.FlattenedFor(ClassId);

        for (int index = 0; index < flat.Count; index++)
        {
            if (string.Equals(flat[index].Property.Name, name, StringComparison.Ordinal))
            {
                return new DecodedProperty(index, flat[index], PropertyValue.FromInt(value));
            }
        }

        throw new InvalidOperationException($"the fixture's schema has no {name}");
    }

    /// <summary>A player class carrying the coaching pair and a team.</summary>
    /// <remarks>
    /// **The table names matter and are the thing under test.** `m_bIsCoaching` and `m_hStudent`
    /// live in `DT_TFLocalPlayerExclusive` — confirmed against a real demo's send tables with the
    /// `schema` probe — so the fixture nests that table inside the player exactly as the game does.
    /// </remarks>
    private static EntityDecoder Decoder()
    {
        DemoSchema schema = new(
            [
                new SendTable("DT_TFLocalPlayerExclusive", NeedsDecoder: true,
                [
                    new SendProperty(SendPropType.Int, "m_bIsCoaching", 1, string.Empty, 0f, 0f, 1, 0),
                    new SendProperty(SendPropType.Int, "m_hStudent", 1, string.Empty, 0f, 0f, 21, 0),
                ]),
                new SendTable("DT_BaseEntity", NeedsDecoder: true,
                [
                    new SendProperty(SendPropType.Int, "m_iTeamNum", 1, string.Empty, 0f, 0f, 3, 0),
                ]),
                new SendTable("DT_TFPlayer", NeedsDecoder: true,
                [
                    new SendProperty(
                        SendPropType.DataTable, "baseclass", 1, "DT_BaseEntity", 0f, 0f, 0, 0),
                    new SendProperty(
                        SendPropType.DataTable, "tflocaldata", 1,
                        "DT_TFLocalPlayerExclusive", 0f, 0f, 0, 0),
                ]),
            ],
            [new ServerClass(ClassId, "CTFPlayer", "DT_TFPlayer")]);

        return new EntityDecoder(schema, EntityDecoder.ClassIdBits(schema.ServerClasses.Count));
    }
}
