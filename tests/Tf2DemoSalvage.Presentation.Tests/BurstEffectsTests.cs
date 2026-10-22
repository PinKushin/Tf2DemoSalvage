using System;
using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>
/// The medigun beams and overheal pluses a frame offers — `CWeaponMedigun::UpdateEffects` (tf_weapon_medigun.cpp:2419-2479)
/// and `C_TFPlayer::UpdateOverhealEffect` (c_tf_player.cpp:10056-10115) — as they left the view (D214).
/// </summary>
/// <remarks>
/// **These had no test while they lived in `MainForm`**, which nothing can construct without a window. Each asserts the
/// burst offered: its key, its system, its two control points and its ticks. The paused frame is the one B538 fixed —
/// the client simulates no particle while paused (`ParticleMgr()->Simulate( gpGlobals->frametime )`,
/// cdll_client_int.cpp:2234), so the effect stays where a PLAYING frame drew it.
/// </remarks>
public sealed class BurstEffectsTests
{
    private const int Medigun = 40;
    private const int Medic = 5;
    private const int Patient = 6;
    private const int Blue = 3;
    private const long HealBeamKeys = 7L << 32;
    private const long OverhealKeys = 9L << 32;

    private static readonly ParticleControlPoint Muzzle = ParticleControlPoint.Unoriented(new Vector3(10f, 20f, 60f));

    private static readonly ParticleSystem Beam = System("medicgun_beam_blue");
    private static readonly ParticleSystem Targeted = System("medicgun_beam_blue_targeted");
    private static readonly ParticleSystem Custom = System("medicgun_beam_custom");
    private static readonly ParticleSystem Pluses = System("overhealedplayer_blue_pluses");

    private static readonly Dictionary<string, ParticleSystem> Systems = new(StringComparer.Ordinal)
    {
        [Beam.Name] = Beam,
        [Targeted.Name] = Targeted,
        [Custom.Name] = Custom,
        [Pluses.Name] = Pluses,
    };

    private static readonly ScenePlayer MedicDrawn = new(Medic, 10f, 20f, 0f, Blue, 150, 5);
    private static readonly ScenePlayer PatientDrawn = new(Patient, 200f, 0f, 0f, Blue, 300, 6, Yaw: 90f);

    private static readonly Dictionary<int, ScenePlayer> Played = new()
    {
        [Medic] = MedicDrawn with { X = 15f },
        [Patient] = PatientDrawn with { X = 190f, Yaw = 45f },
    };

    private static readonly Func<int, Dictionary<int, ScenePlayer>> NobodyPlayed = static _ => [];

    private static readonly Func<SceneHealBeam, string?> NoCustom = static _ => null;

    private static ParticleSystem System(string name) =>
        new(name, [], [], [], [], [], new Dictionary<string, DmxValue>(StringComparer.Ordinal));

    private static SceneHealBeam Healing(int start = 100, int? end = null) =>
        new(Medigun, Patient, false, Blue, start, end) { Owner = Medic };

    /// <summary>The presenter over a posed medigun, with both players drawn on the tick.</summary>
    private static (BurstEffects Bursts, FakePosedEntities Models) Drawn(int tick)
    {
        FakePosedEntities models = new() { Posed = { Medigun }, Named = { [(Medigun, "muzzle")] = Muzzle } };
        BurstEffects bursts = new(new ParticleEffects(), models, new BulletStrikes(models, new RecordingLogger()), new RecordingLogger());

        bursts.DrawnPlayers([MedicDrawn, PatientDrawn], tick);

        return (bursts, models);
    }

    [Test]
    public void AddHealBeams_WhilePlaying_RunsFromTheMuzzleToFiftyAboveTheDrawnPatient()
    {
        (BurstEffects bursts, _) = Drawn(150);

        bursts.AddHealBeams([Healing()], null, NobodyPlayed, NoCustom, paused: false, healTargetMarker: false, 150, Systems);

        ParticleBurst beam = bursts.Bursts.ShouldHaveSingleItem();

        beam.Key.ShouldBe(HealBeamKeys);
        beam.Definition.ShouldBeSameAs(Beam);
        beam.At.ShouldBe(Muzzle);
        beam.End.ShouldBe(ParticleControlPoint.Facing(new Vector3(200f, 0f, 50f), 90f), "PATTACH_ABSORIGIN_FOLLOW, offset ( 0, 0, 50 ), on his own axes");
        beam.Tick.ShouldBe(100);
        beam.StopTick.ShouldBeNull();
    }

    [Test]
    public void AddHealBeams_WhilePaused_StaysWhereAPlayingFrameDrewBothEnds()
    {
        (BurstEffects bursts, _) = Drawn(150);

        bursts.AddHealBeams([Healing()], null, _ => Played, NoCustom, paused: true, healTargetMarker: false, 150, Systems);

        ParticleBurst beam = bursts.Bursts.ShouldHaveSingleItem();

        beam.At.At.ShouldBe(new Vector3(15f, 20f, 60f), "the muzzle, carried the five units its medic was drawn away");
        beam.End.ShouldBe(ParticleControlPoint.Facing(new Vector3(190f, 0f, 50f), 45f));
    }

    [Test]
    public void AddHealBeams_AnEarlierTickOfACatchUp_IsAskedOfThePlayedSampleForThatTick()
    {
        (BurstEffects bursts, _) = Drawn(150);
        List<int> asked = [];

        bursts.AddHealBeams(
            [Healing()],
            null,
            then =>
            {
                asked.Add(then);

                return Played;
            },
            NoCustom,
            paused: false,
            healTargetMarker: false,
            150,
            Systems);

        asked.ShouldBeEmpty("a playing frame draws at this frame's points and asks for no history");

        (ParticleControlPoint at, ParticleControlPoint? end) = bursts.Bursts[0].History.ShouldNotBeNull()(140);

        asked.ShouldBe([140]);
        at.At.ShouldBe(new Vector3(15f, 20f, 60f));
        end.ShouldBe(ParticleControlPoint.Facing(new Vector3(190f, 0f, 50f), 45f));
    }

    [Test]
    public void AddHealBeams_AMedigunNotPosedHere_OffersNoBeam()
    {
        (BurstEffects bursts, FakePosedEntities models) = Drawn(150);

        models.Named.Clear();
        bursts.AddHealBeams([Healing()], null, NobodyPlayed, NoCustom, paused: false, healTargetMarker: false, 150, Systems);

        bursts.Bursts.ShouldBeEmpty();
    }

    /// <summary>The first-person hands' copy of the medigun, `C_ViewmodelAttachmentModel`, and its own muzzle.</summary>
    private static readonly ParticleControlPoint HandsMuzzle =
        new(new Vector3(1f, 2f, 64f), new Vector3(0f, 0f, 1f), new Vector3(0f, -1f, 0f), new Vector3(1f, 0f, 0f));

    // Conformance (B539): a c_model weapon whose owner is not drawn (`!ShouldDrawThisPlayer()`, the view in his eyes) gets a
    // C_ViewmodelAttachmentModel parented to his viewmodel (econ_entity.cpp:1135-1178), and CEconEntity::GetAttachment answers
    // from it (econ_entity.cpp:1978-1984). The beam's control point 0 is `PATTACH_POINT_FOLLOW` on "muzzle", read through that
    // virtual (particle_property.cpp:599-623), so in the medic's eyes the beam starts at the hands' muzzle, on ITS axes, which
    // the path's bulge then follows.
    [Test]
    public void AddHealBeams_InTheMedicsEyes_StartsAtTheHandsMuzzleOnItsAxes()
    {
        (BurstEffects bursts, FakePosedEntities models) = Drawn(150);

        models.Named[(ViewmodelScene.WeaponEntityIndex, "muzzle")] = HandsMuzzle;
        bursts.AddHealBeams(
            [Healing()], null, NobodyPlayed, NoCustom, paused: false, healTargetMarker: false, 150, Systems,
            inEyesOf: Medic, handsHold: static _ => true);

        bursts.Bursts.ShouldHaveSingleItem().At.ShouldBe(HandsMuzzle);
    }

    // The attachment model belongs to THAT econ entity (econ_entity.cpp:1157, SetOuter( this )): hands holding another
    // weapon carry that weapon's model, whose muzzle is not the medigun's.
    [Test]
    public void AddHealBeams_InTheMedicsEyesHoldingAnotherWeapon_StartsAtTheWorldMuzzle()
    {
        (BurstEffects bursts, FakePosedEntities models) = Drawn(150);

        models.Named[(ViewmodelScene.WeaponEntityIndex, "muzzle")] = HandsMuzzle;
        bursts.AddHealBeams(
            [Healing()], null, NobodyPlayed, NoCustom, paused: false, healTargetMarker: false, 150, Systems,
            inEyesOf: Medic, handsHold: static _ => false);

        bursts.Bursts.ShouldHaveSingleItem().At.ShouldBe(Muzzle);
    }

    // particle_property.cpp:608-611: a failed GetAttachment falls back to C_BaseAnimating's own, the world medigun's.
    [Test]
    public void AddHealBeams_InTheMedicsEyesWithNoHandsModelPosed_FallsBackToTheWorldMuzzle()
    {
        (BurstEffects bursts, _) = Drawn(150);

        bursts.AddHealBeams(
            [Healing()], null, NobodyPlayed, NoCustom, paused: false, healTargetMarker: false, 150, Systems, inEyesOf: Medic);

        bursts.Bursts.ShouldHaveSingleItem().At.ShouldBe(Muzzle);
    }

    // The attachment model exists only while its owner is not drawn: in anyone else's eyes the world model's muzzle stands.
    [Test]
    public void AddHealBeams_InAnotherPlayersEyes_StartsAtTheWorldMuzzle()
    {
        (BurstEffects bursts, FakePosedEntities models) = Drawn(150);

        models.Named[(ViewmodelScene.WeaponEntityIndex, "muzzle")] = HandsMuzzle;
        bursts.AddHealBeams(
            [Healing()], null, NobodyPlayed, NoCustom, paused: false, healTargetMarker: false, 150, Systems,
            inEyesOf: Patient, handsHold: static _ => true);

        bursts.Bursts.ShouldHaveSingleItem().At.ShouldBe(Muzzle);
    }

    [Test]
    public void AddHealBeams_APatientTheFrameDidNotDraw_OffersNoBeam()
    {
        (BurstEffects bursts, _) = Drawn(150);

        bursts.DrawnPlayers([MedicDrawn], 150);
        bursts.AddHealBeams([Healing()], null, NobodyPlayed, NoCustom, paused: false, healTargetMarker: false, 150, Systems);

        bursts.Bursts.ShouldBeEmpty();
    }

    [TestCase(99, 0, TestName = "AddHealBeams_BeforeTheBeamStarts_OffersNone")]
    [TestCase(100, 1, TestName = "AddHealBeams_OnTheTickTheBeamStarts_OffersIt")]
    [TestCase(331, 1, TestName = "AddHealBeams_OneTickInsideTheFadeOfAStoppedBeam_StillOffersIt")]
    [TestCase(332, 0, TestName = "AddHealBeams_TwoSecondsAfterTheBeamStopped_OffersNone")]
    public void AddHealBeams_AroundABeamsLife_OffersItFromItsStartToTheEndOfItsFade(int tick, int expected)
    {
        (BurstEffects bursts, _) = Drawn(tick);

        bursts.AddHealBeams([Healing(100, 200)], null, NobodyPlayed, NoCustom, paused: false, healTargetMarker: false, tick, Systems);

        bursts.Bursts.Count.ShouldBe(expected, "a stopped beam fades for 132 ticks");

        if (expected == 1)
        {
            bursts.Bursts[0].StopTick.ShouldBe(200);
        }
    }

    /// <remarks>
    /// **A beam that is not live costs nothing** (B574): the history closure was allocated at the top of every
    /// iteration over all of the demo's beams (1,211 on f12), live or not — 173 MB per 20 s on the render thread
    /// (finding 89's measurement file). A frame where none is live must allocate nothing.
    /// </remarks>
    [Test]
    public void AddHealBeams_EveryBeamOutsideItsLife_AllocatesNothing()
    {
        (BurstEffects bursts, _) = Drawn(500);
        SceneHealBeam[] beams = [Healing(100, 200), Healing(600), Healing(100, 300)];
        bursts.AddHealBeams(beams, null, NobodyPlayed, NoCustom, paused: false, healTargetMarker: false, 500, Systems);

        long before = GC.GetAllocatedBytesForCurrentThread();
        bursts.AddHealBeams(beams, null, NobodyPlayed, NoCustom, paused: false, healTargetMarker: false, 500, Systems);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        allocated.ShouldBe(0);
        bursts.Bursts.ShouldBeEmpty();
    }

    [Test]
    public void AddHealBeams_ABeamMetBySeek_IsSteppedFromTwoSecondsBackAndKeepsThatUntilASeekBackward()
    {
        (BurstEffects bursts, _) = Drawn(1000);
        SceneHealBeam[] beams = [Healing()];

        bursts.AddHealBeams(beams, null, NobodyPlayed, NoCustom, paused: false, healTargetMarker: false, 1000, Systems);
        bursts.Bursts[^1].Tick.ShouldBe(868, "1000 less the 132 ticks its particles can live");

        bursts.AddHealBeams(beams, null, NobodyPlayed, NoCustom, paused: false, healTargetMarker: false, 1100, Systems);
        bursts.Bursts[^1].Tick.ShouldBe(868, "fixed at first sight, or playing on would restart it every frame");

        bursts.AddHealBeams(beams, null, NobodyPlayed, NoCustom, paused: false, healTargetMarker: false, 180, Systems);
        bursts.Bursts[^1].Tick.ShouldBe(100, "a seek backward forgets it, and the beam's own start is nearer than two seconds");
    }

    [TestCase(true, Medic, "medicgun_beam_blue_targeted", TestName = "AddHealBeams_TheRecordersOwnBeamWithTheMarkerOn_IsTheTargetedSystem")]
    [TestCase(false, Medic, "medicgun_beam_blue", TestName = "AddHealBeams_TheRecordersOwnBeamWithTheMarkerOff_IsThePlainSystem")]
    [TestCase(true, Patient, "medicgun_beam_blue", TestName = "AddHealBeams_AnotherMedicsBeamWithTheMarkerOn_IsThePlainSystem")]
    public void AddHealBeams_TheHealTargetMarker_IsOnlyTheRecordingMedicsOwn(bool marker, int recorder, string expected)
    {
        (BurstEffects bursts, _) = Drawn(150);

        bursts.AddHealBeams([Healing()], recorder, NobodyPlayed, NoCustom, paused: false, marker, 150, Systems);

        bursts.Bursts.ShouldHaveSingleItem().Definition.Name.ShouldBe(expected);
    }

    [Test]
    public void AddHealBeams_AnItemWithACustomParticleSystem_AddsItBesideTheBeamOnTheSamePoints()
    {
        (BurstEffects bursts, _) = Drawn(150);

        bursts.AddHealBeams(
            [Healing(), Healing()], null, NobodyPlayed, static _ => "medicgun_beam_custom", paused: false, healTargetMarker: false, 150, Systems);

        bursts.Bursts.Count.ShouldBe(4);
        bursts.Bursts[3].Key.ShouldBe(HealBeamKeys + 3, "two keys a beam: the second beam's custom system is 2 * 1 + 1");
        bursts.Bursts[3].Definition.ShouldBeSameAs(Custom);
        bursts.Bursts[3].At.ShouldBe(bursts.Bursts[2].At);
        bursts.Bursts[3].End.ShouldBe(bursts.Bursts[2].End);
    }

    [Test]
    public void HealBeamThen_NeitherPlayerInThePlayedSample_IsThisFramesMuzzleAndPatient()
    {
        ParticleControlPoint patient = ParticleControlPoint.Unoriented(new Vector3(1f, 2f, 3f));

        BurstEffects.HealBeamThen([], Healing(), Muzzle, new Vector3(10f, 20f, 0f), 0f, patient).ShouldBe((Muzzle, patient));
    }

    [Test]
    public void HealBeamThen_AMedicTheFrameDidNotDraw_KeepsTheMuzzleAndMovesOnlyThePatientsEnd()
    {
        (ParticleControlPoint at, ParticleControlPoint? end) =
            BurstEffects.HealBeamThen(Played, Healing(), Muzzle, null, 0f, ParticleControlPoint.Unoriented(Vector3.Zero));

        at.ShouldBe(Muzzle, "no lever arm to carry it by");
        end.ShouldBe(ParticleControlPoint.Facing(new Vector3(190f, 0f, 50f), 45f));
    }

    [Test]
    public void HealBeamThen_AMedicWhoTurned_SwingsTheMuzzleAboutHim()
    {
        Dictionary<int, ScenePlayer> turned = new() { [Medic] = MedicDrawn with { Yaw = 90f } };

        (ParticleControlPoint at, _) = BurstEffects.HealBeamThen(
            turned, Healing(), ParticleControlPoint.Unoriented(new Vector3(20f, 20f, 60f)), new Vector3(10f, 20f, 0f), 0f, Muzzle);

        // Ten units ahead of a medic at ( 10, 20 ) facing +x; a quarter turn left puts it ten units along +y.
        at.At.X.ShouldBe(10f, 0.001f);
        at.At.Y.ShouldBe(30f, 0.001f);
        at.At.Z.ShouldBe(60f, 0.001f);
    }

    private static readonly PlayerConditions Overhealed = new(1 << OverhealEffects.Overhealed, 0, 0, 0, 0);

    private static readonly (IReadOnlyList<ModelHitbox> Boxes, Vector3 PosedAt) Boxes =
        ([ModelHitbox.At(-Vector3.One, Vector3.One, new Vector3(200f, 0f, 40f))], new Vector3(200f, 0f, 0f));

    /// <summary>The patient overhealed from tick 990 on, and nobody before.</summary>
    private static IReadOnlyList<ScenePlayer> Recorded(int tick) =>
        tick >= 990 ? [PatientDrawn with { Conditions = Overhealed }] : [];

    private static (BurstEffects Bursts, FakePosedEntities Models) Overhealing()
    {
        (BurstEffects bursts, FakePosedEntities models) = Drawn(1000);

        models.Hitboxes[Patient] = Boxes;

        return (bursts, models);
    }

    [Test]
    public void AddOverheals_AnOverhealedPlayerPosedHere_HasHisTeamsPlusesOnHisDrawnOriginBornOnHisHitboxes()
    {
        (BurstEffects bursts, _) = Overhealing();

        bursts.AddOverheals(Recorded, null, NobodyPlayed, paused: false, inEyes: null, 1000, Systems);

        ParticleBurst pluses = bursts.Bursts.ShouldHaveSingleItem();

        pluses.Key.ShouldBe(OverhealKeys + (990L << 7) + Patient);
        pluses.Definition.ShouldBeSameAs(Pluses);
        pluses.At.ShouldBe(ParticleControlPoint.Facing(new Vector3(200f, 0f, 0f), 90f), "PATTACH_ABSORIGIN_FOLLOW");
        pluses.Tick.ShouldBe(990);
        pluses.End.ShouldBeNull();
        pluses.StopTick.ShouldBeNull();
        pluses.Model.ShouldNotBeNull()().ShouldBe(Boxes);
    }

    [Test]
    public void AddOverheals_ThePlayerWhoseEyesTheViewIsIn_ShowsNone()
    {
        (BurstEffects bursts, _) = Overhealing();

        bursts.AddOverheals(Recorded, null, NobodyPlayed, paused: false, inEyes: Patient, 1000, Systems);

        bursts.Bursts.ShouldBeEmpty("control point 0 is the camera: CNewParticleEffect::DrawModel, particles_new.cpp:541-571");
    }

    [Test]
    public void AddOverheals_APlayerNoPassHasPosed_IsNotOfferedYet()
    {
        (BurstEffects bursts, FakePosedEntities models) = Overhealing();

        models.Hitboxes.Clear();
        bursts.AddOverheals(Recorded, null, NobodyPlayed, paused: false, inEyes: null, 1000, Systems);

        bursts.Bursts.ShouldBeEmpty("caught up without its boxes it is born on the control point, a heap at his feet");
    }

    [Test]
    public void AddOverheals_APlayerTheFrameDidNotDraw_ShowsNone()
    {
        (BurstEffects bursts, _) = Overhealing();

        bursts.DrawnPlayers([MedicDrawn], 1000);
        bursts.AddOverheals(Recorded, null, NobodyPlayed, paused: false, inEyes: null, 1000, Systems);

        bursts.Bursts.ShouldBeEmpty();
    }

    [Test]
    public void AddOverheals_WhilePaused_StaysWhereAPlayingFrameDrewHim()
    {
        (BurstEffects bursts, _) = Overhealing();

        bursts.AddOverheals(Recorded, null, _ => Played, paused: true, inEyes: null, 1000, Systems);

        bursts.Bursts.ShouldHaveSingleItem().At.ShouldBe(ParticleControlPoint.Facing(new Vector3(190f, 0f, 0f), 45f));
    }

    [Test]
    public void AddOverheals_AnEarlierTickWithNoPlayedSampleOfHim_IsWhereThisFrameDrewHim()
    {
        (BurstEffects bursts, _) = Overhealing();

        bursts.AddOverheals(Recorded, null, NobodyPlayed, paused: false, inEyes: null, 1000, Systems);

        bursts.Bursts[0].History.ShouldNotBeNull()(995).ShouldBe((ParticleControlPoint.Facing(new Vector3(200f, 0f, 0f), 90f), null));
    }

    [Test]
    public void AddOverheals_TheRecordingPlayer_ShowsNone()
    {
        (BurstEffects bursts, _) = Overhealing();

        bursts.AddOverheals(Recorded, Patient, NobodyPlayed, paused: false, inEyes: null, 1000, Systems);

        bursts.Bursts.ShouldBeEmpty("IsLocalPlayer(): c_tf_player.cpp:10059");
    }
}
