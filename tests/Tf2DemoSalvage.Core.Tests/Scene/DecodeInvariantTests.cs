using System;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// Rules the decoded scene must obey, each written after something broke by disobeying it.
/// </summary>
/// <remarks>
/// **Every test here is a defect that shipped, generalised into the rule that would have caught it.**
/// They share a shape worth naming: a value that is WRONG but legal — an angle a full turn out, an
/// entity index that is really a handle, a model index read as an array subscript. None of them
/// throws, all of them decode, and the picture that results is plausible enough to send someone
/// looking at the renderer.
///
/// **These are invariants, not examples.** A test asserting one particular yaw decodes correctly is
/// a fixture; a test asserting that no yaw ever leaves (−180, 180] is a rule the whole decoder has
/// to keep, and it fails for inputs nobody thought to write down.
/// </remarks>
public sealed class DecodeInvariantTests
{
    [Test]
    public void DecodeInvariants_EveryAngle_IsNormalisedToOneTurn()
    {
        // **The 220.997 against −139.003 defect.** The wire sends yaw as 0..360 and everything here
        // stores (−180, 180], so one direction was held as two numbers a full turn apart — and
        // anything comparing or interpolating them is wrong by 360 exactly at the wrap, which is
        // where a player spinning past south lives.
        //
        // Asserted as a rule over the whole circle rather than for the one value that was measured,
        // because the failure is at a boundary and a single example never sits on one.
        for (int degrees = -720; degrees <= 720; degrees += 5)
        {
            float normalised = DemoTimeline.NormalizeAngle(degrees);

            normalised.ShouldBeGreaterThan(-180.001f, $"{degrees} normalised out of range");
            normalised.ShouldBeLessThanOrEqualTo(180.001f, $"{degrees} normalised out of range");

            // And it must be the SAME direction, not merely a number in range: the two differ by a
            // whole number of turns.
            float turns = (degrees - normalised) / 360f;

            MathF.Abs(turns - MathF.Round(turns)).ShouldBeLessThan(
                0.001f, $"{degrees} was moved by something other than whole turns");
        }
    }

    [Test]
    public void DecodeInvariants_AnAngleAlreadyInRange_IsLeftAlone()
    {
        // The control. A normaliser that returned zero, or that wrapped everything by a turn, would
        // satisfy the range assertion above and destroy every angle in the file.
        DemoTimeline.NormalizeAngle(0f).ShouldBe(0f);
        DemoTimeline.NormalizeAngle(90f).ShouldBe(90f, 1e-4f);
        DemoTimeline.NormalizeAngle(-139.003f).ShouldBe(-139.003f, 1e-3f);
        DemoTimeline.NormalizeAngle(179.5f).ShouldBe(179.5f, 1e-3f);
    }

    [Test]
    public void DecodeInvariants_AnEntityHandle_IsMaskedAfterItsInvalidCheck()
    {
        // **RecvProxy_IntToEHandle, client/recvproxy.cpp:90.** A networked handle packs the entity
        // index into the low MAX_EDICT_BITS and a serial number above it, and the invalid value is
        // tested against the WHOLE word BEFORE masking. Masking first turns every invalid handle
        // into a plausible entity index — which is how 220 syringe projectiles were claimed as worn
        // items by their owner.
        const int edictBits = 11;

        // **Not −1.** INVALID_NETWORKED_EHANDLE_VALUE is all ones across the index AND the serial
        // number — 11 + 10 bits — which is what the engine compares against and what this project
        // stores. Writing this test against −1 would have passed for the wrong reason, since −1
        // masks to 2047 as well.
        int invalid = EntityState.NoHandle;

        invalid.ShouldBe((1 << (edictBits + 10)) - 1);

        // A real handle: entity 42 with a serial number above it.
        EntityState.Slot(42 | (7 << edictBits)).ShouldBe(42);

        // The invalid one answers "nothing", not 2047 — which is a legal index naming whatever
        // entity occupies that slot.
        EntityState.Slot(invalid).ShouldBeNull();
        (invalid & ((1 << edictBits) - 1)).ShouldBe(2047, "this is what masking first would answer");

        // And a property that was never sent is not a handle to anything.
        EntityState.Slot(null).ShouldBeNull();
    }

    [Test]
    public void DecodeInvariants_ADynamicModelIndex_IsDecodedFromItsNegativeForm()
    {
        // **ivmodelinfo.h:90.** A model index below −1 is dynamic: the table entry is
        // (−2 − index) >> 1, and the low bit says whether it is client-only or networked. Every TF2
        // cosmetic arrives this way, so reading a negative index as an ordinary one finds nothing
        // and draws nothing — silently, because a missing model is indistinguishable from an entity
        // that has none.
        //
        // Even is networked and lives in the DynamicModels string table; odd is client-only and
        // cannot be resolved from a demo at all.
        ModelPrecache.DynamicSlot(-2).ShouldBe(0);
        ModelPrecache.DynamicSlot(-4).ShouldBe(1);
        ModelPrecache.DynamicSlot(-6).ShouldBe(2);

        // Odd dynamic indices are client-only: −3 gives dynamic 1, which is odd.
        ModelPrecache.DynamicSlot(-3).ShouldBeNull("an odd dynamic index is client-only");
        ModelPrecache.DynamicSlot(-5).ShouldBeNull();

        // And an ordinary index is not touched by any of this.
        ModelPrecache.DynamicSlot(0).ShouldBeNull("a precache index is not a dynamic one");
        ModelPrecache.DynamicSlot(42).ShouldBeNull();
    }

    [Test]
    public void DecodeInvariants_AnAbsentProperty_MeansTheDefaultNotUnknown()
    {
        // **The delta format's own rule, and a trap this project has fallen into twice.** A demo
        // sends only what CHANGED, so a property that never appears is at its default — not
        // missing, not unknown. Reading absence as "we do not know" hides everyone who has not
        // died: LIFE_ALIVE is zero, so a living player never sends m_lifeState at all.
        ScenePlayer never = new(EntityIndex: 3, X: 0f, Y: 0f, Z: 0f, Team: 2, Health: 125, PlayerClass: 1);

        never.IsAlive.ShouldBeTrue("a player who never sent a life state is alive, not unknown");

        // The control: a player who DID send one is read from it.
        (never with { LifeState = 2 }).IsAlive.ShouldBeFalse();
    }
}
