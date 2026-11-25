using System.Collections.Generic;
using System.Globalization;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// The adapter that lets the scene ask a demo for a moment — and tell it one thing back.
/// </summary>
/// <remarks>
/// **Every other member here is a pass-through and this file does not test those.** It exists for
/// <c>OnNewModel</c>, which is the only call that travels from the scene INTO the demo: the model
/// says which pose parameters wrap, and the interpolator that needs to know sits a layer below
/// models (B269). Nothing else in the chain can fail silently, and this hop can — an adapter that
/// dropped the fact would leave a sentry sweeping the long way round with every test still green.
/// </remarks>
public sealed class TimelineMomentsTests
{
    /// <summary>
    /// A demo whose server holds every holiday off from tick 1 and forces <paramref name="holiday"/> at tick 10:
    /// `tf_force_holidays_off` is read before the calendar, so tick 5 answers no on any date (B848 review 1, Fable).
    /// </summary>
    private static DemoTimeline HolidayForcedAtTen(int holiday) =>
        DemoTimeline.Build(Core.Tests.SyntheticPlayer.DemoWithMessagesAt(
            (1, [new Core.Net.SetConVarMessage([new("tf_force_holidays_off", "1")])]),
            (10, [new Core.Net.SetConVarMessage(
                [new("tf_force_holidays_off", "0"), new("tf_forced_holiday", holiday.ToString(CultureInfo.InvariantCulture))])])));

    /// <remarks>
    /// **The holiday vision reads the server's ConVars as of its tick** (B848), as the client applies each `net_SetConVar`
    /// as its packet is read: holidays off until tick 10, then Halloween forced, so tick 5 has no flag and tick 15 has it,
    /// whatever the date (review 1, Fable: the calendar control failed on Halloween and full-moon days).
    /// </remarks>
    [Test]
    public void HolidayVisionFlagsAt_BeforeAndAfterTheServerForcedHalloween_FollowsEachTick()
    {
        TimelineMoments moments = new(HolidayForcedAtTen(2));

        (moments.HolidayVisionFlagsAt(5), moments.HolidayVisionFlagsAt(15)).ShouldBe((0, VisionVisibility.HalloweenFlag));
    }

    /// <remarks>
    /// **The ropes' Christmas check reads the server's ConVars as of its tick** (B848 review 1, Fable, Sonnet):
    /// `TF_IsHolidayActive` reads `tf_forced_holiday` when asked (`tf_gamerules.cpp:1030-1060`). Christmas (3) forced at
    /// tick 10.
    /// </remarks>
    [Test]
    public void ChristmasActiveAt_BeforeAndAfterTheServerForcedChristmas_FollowsEachTick()
    {
        DemoTimeline timeline = HolidayForcedAtTen(3);

        (TimelineMoments.ChristmasActiveAt(timeline, 5), TimelineMoments.ChristmasActiveAt(timeline, 15)).ShouldBe((false, true));
    }

    /// <remarks>
    /// **A shot's fixed spread reads `tf_use_fixed_weaponspreads` as of its tick** (B848 review 1, Fable, Sonnet):
    /// `IsFixedWeaponSpreadEnabled` (`tf_fx_shared.cpp:415-434`) is read as `FX_FireBullets` runs.
    /// </remarks>
    [Test]
    public void FixedWeaponSpreadAt_BeforeAndAfterTheServerTurnedItOn_FollowsEachTick()
    {
        DemoTimeline timeline = DemoTimeline.Build(Core.Tests.SyntheticPlayer.DemoWithMessagesAt(
            (10, [new Core.Net.SetConVarMessage([new("tf_use_fixed_weaponspreads", "1")])])));

        (TimelineMoments.FixedWeaponSpreadAt(timeline, 5), TimelineMoments.FixedWeaponSpreadAt(timeline, 15)).ShouldBe((false, true));
    }

    /// <remarks>
    /// **A match group's description overrides the ConVar** (B848 review 1, Fable): `IsFixedWeaponSpreadEnabled` takes
    /// `BUsesFixedWeaponSpread()` whenever `GetMatchGroupDescription` finds one (`tf_fx_shared.cpp:419-423`). Only the ladder
    /// description sets it (`tf_match_description_comp.cpp:42`), which 6v6 ladder (2) and the event placeholder (8) use;
    /// MvM (0, 1) and casual 12v12 (7) keep the base's false (`tf_match_description.h:167`); a group nothing registers,
    /// and invalid (-1), has none and leaves the ConVar.
    /// </remarks>
    [TestCase(2, false, true)]
    [TestCase(8, false, true)]
    [TestCase(0, true, false)]
    [TestCase(1, true, false)]
    [TestCase(7, true, false)]
    [TestCase(5, true, true)]
    [TestCase(-1, true, true)]
    [TestCase(-1, false, false)]
    public void FixedWeaponSpreadFor_AMatchGroup_OverridesTheConVarWhereItHasADescription(int group, bool conVar, bool expected) =>
        TimelineMoments.FixedWeaponSpreadFor(group, conVar).ShouldBe(expected);

    [Test]
    public void OnNewModel_ForAnEntityWithATrack_TeachesItWhichParametersWrap()
    {
        ScenePropTrack track = new(entityIndex: 7, "models/buildables/sentry3.mdl");

        track.Add(0, new ScenePose());

        TimelineMoments moments = new(DemoTimeline.ForTracks([track]));

        moments.OnNewModel(7, [false, true], staticProp: false);

        track.PoseParameterLoops.Count.ShouldBe(2);
        track.PoseParameterLoops[0].ShouldBeFalse();
        track.PoseParameterLoops[1].ShouldBeTrue();
    }

    /// <remarks>
    /// **The control, and it is not a defensive nicety.** The model set knows about entities the
    /// timeline may not have a track for — a viewmodel, an entity resolved from the item schema —
    /// and it tells the source about every one of them. Throwing there would take out the frame.
    /// </remarks>
    [Test]
    public void OnNewModel_ForAnEntityWithNoTrack_DoesNothing()
    {
        ScenePropTrack track = new(entityIndex: 7, "models/buildables/sentry3.mdl");

        track.Add(0, new ScenePose());

        TimelineMoments moments = new(DemoTimeline.ForTracks([track]));

        moments.OnNewModel(999, [true], staticProp: false);

        track.PoseParameterLoops.ShouldBeEmpty("the wrong entity's track must be left alone");
    }

    /// <remarks>
    /// **B389, and the same fault the `cycle` probe had.** An edict index does not name a track —
    /// the engine recycles slots, so `TrackFor(entityIndex)` answers about whichever occupant was
    /// written last. A model resolving during a frame at tick 150 was stamping a track that does not
    /// begin until tick 400: the entity being drawn kept the pose-parameter history of a model it
    /// never had, and one that does not exist yet got looping flags from the past. Neither threw.
    ///
    /// **The frame's own tick is what decides**, which is also what makes it Valve's rule: the engine
    /// stamps `gpGlobals->curtime` because `OnNewModel` happens to the entity being simulated NOW.
    /// </remarks>
    [Test]
    public void OnNewModel_AnIndexWhoseSlotWasReused_TeachesTheOccupantAliveAtTheFrame()
    {
        ScenePropTrack early = new(entityIndex: 7, "models/props_well/main_entrance_door.mdl");
        early.Add(100, new ScenePose());
        early.Add(280, new ScenePose());
        early.End(300);

        ScenePropTrack late = new(entityIndex: 7, "models/player/scout.mdl");
        late.Add(400, new ScenePose());
        late.Add(500, new ScenePose());

        TimelineMoments moments = new(DemoTimeline.ForTracks([early, late]));

        // The frame being drawn, which is the only thing that says which occupant this is about.
        moments.PropsAt(150, new List<SceneProp>());

        moments.OnNewModel(7, [false, true], staticProp: true);

        early.PoseParameterLoops.Count.ShouldBe(2);
        early.StaticPropModel.ShouldBeTrue();

        late.PoseParameterLoops.ShouldBeEmpty(
            "the occupant that does not arrive until tick 400 was not the one whose model resolved");
        late.StaticPropModel.ShouldBeFalse();
    }

    /// <remarks>
    /// **The other direction, so the test above cannot pass by always picking the first.** The same
    /// two tracks, a later frame, and the stamp must land on the other one.
    /// </remarks>
    [Test]
    public void OnNewModel_AFrameInsideTheSecondOccupant_TeachesThatOneInstead()
    {
        ScenePropTrack early = new(entityIndex: 7, "models/props_well/main_entrance_door.mdl");
        early.Add(100, new ScenePose());
        early.End(300);

        ScenePropTrack late = new(entityIndex: 7, "models/player/scout.mdl");
        late.Add(400, new ScenePose());
        late.Add(500, new ScenePose());

        TimelineMoments moments = new(DemoTimeline.ForTracks([early, late]));

        moments.PropsAt(450, new List<SceneProp>());

        moments.OnNewModel(7, [true], staticProp: false);

        late.PoseParameterLoops.Count.ShouldBe(1);
        early.PoseParameterLoops.ShouldBeEmpty();
    }

    /// <remarks>
    /// **A later occupant of the slot with the SAME model is told too** (B726, the Fable review). The engine makes a new
    /// `C_BaseAnimating` for it and calls `OnNewModel` on that (`c_baseanimating.cpp:1124-1178`); the model set reports a
    /// model once per index and path, so the source must carry what the model said to every occupant that draws it — a
    /// rocket, a pickup, a rebuilt building. Both occupants are a locker whose sequence 4 loops: told at tick 150 for the
    /// first, drawn at tick 305 as the second, 0.9 then 0.1 halfway is the loop's 0. A second occupant never told would
    /// read the no-model answer, `Lerp`'s 0.5.
    /// </remarks>
    [Test]
    public void PropsAt_ASecondOccupantWithTheSameModel_IsToldWhatTheModelSaid()
    {
        const string Locker = "models/props_gameplay/resupply_locker.mdl";
        int delay = ScenePropTrack.DelayTicksFor(ScenePropTrack.Tf2TickInterval);

        ScenePropTrack early = new(entityIndex: 7, Locker);
        early.Add(100, new ScenePose { Sequence = 4 });
        early.End(200);

        ScenePropTrack late = new(entityIndex: 7, Locker);
        late.Add(300, new ScenePose { Cycle = 0.9f, Sequence = 4 });
        late.Add(310, new ScenePose { Cycle = 0.1f, Sequence = 4 });

        TimelineMoments moments = new(DemoTimeline.ForTracks([early, late]));

        moments.PropsAt(150, new List<SceneProp>());
        moments.OnNewModel(7, [], staticProp: false, sequenceLoops: sequence => sequence == 4);

        moments.PropsAt(305 + delay, new List<SceneProp>());

        late.At(305d + delay).ShouldNotBeNull().Cycle.ShouldBe(0f, 1e-4f);
    }

    /// <remarks>
    /// **The wiring, which is the only level that could catch this one** (B395).
    /// `RagdollBodygroupConformanceTests` proves `RagdollProps.Fill` computes a corpse's
    /// `m_nBody` when it is handed an appearance and a part table; it says nothing about whether
    /// anything ever hands them over. A supplier nobody sets is exactly the shape this project has
    /// shipped three no-ops through, every one with a green suite
    /// (`docs/memory/output-level-assertion-or-it-is-not-done.md`).
    ///
    /// **Both suppliers are read through the source's own `PropsAt`**, so the assertion fails if
    /// either is dropped on the way to `Fill` — which is the hop that has no other test.
    /// </remarks>
    [Test]
    public void PropsAt_ForACorpse_HandsTheAppearanceAndPartTableToTheBodygroupPass()
    {
        SceneRagdoll corpse = new(
            EntityIndex: 40,
            Serial: 1,
            PlayerClass: 5,
            Team: SceneTeams.Blu,
            X: 0f,
            Y: 0f,
            Z: 0f,
            Gib: false,
            Burning: false,
            FeignDeath: false,
            WasDisguised: false,
            FirstTick: 100,
            LastTick: 200,
            Carried: [new SceneCarriedItem(Hat, Weapon: false, Deployed: false)]);

        TimelineMoments moments = new(DemoTimeline.ForTracks([], corpses: [corpse]))
        {
            ClassModels = () => _ => "models/player/medic.mdl",
            Appearance = () => new HidesTheHead(),
            Bodygroups = () => new OnePart(),
        };

        List<SceneProp> scene = [];

        moments.PropsAt(150, scene);

        // **The corpse itself, which `RagdollProps.Fill` appends before its wearables.**
        scene.ShouldNotBeEmpty("the corpse should have been drawn at all");

        scene[0].Pose.Body.ShouldBe(
            HeadGroup,
            "neither supplier reached RagdollProps.Fill, so every corpse draws at body 0");
    }

    /// <summary>The item the wiring fixture wears.</summary>
    private const int Hat = 30700;

    /// <summary>The part it hides, and so the body a corpse wearing it must have.</summary>
    private const int HeadGroup = 2;

    private sealed class HidesTheHead : IPlayerAppearance
    {
        /// <inheritdoc/>
        public ItemBodygroups BodygroupsOf(int itemDefinitionIndex, int team) =>
            itemDefinitionIndex == Hat
                ? new ItemBodygroups(new Dictionary<string, int>(), false, HeadGroup, 1)
                : ItemBodygroups.None;

        /// <inheritdoc/>
        public string? ModelOf(int playerClass) => null;

        /// <inheritdoc/>
        public string? WeaponSuffix(string? weaponClass, int? playerClass, int? weaponItem) => null;


        /// <inheritdoc/>
        public string? Hands(int playerClass) => null;

        /// <inheritdoc/>
        public SceneTaunt? TauntForScene(string scene) => null;
    }

    private sealed class OnePart : IModelBodygroups
    {
        /// <inheritdoc/>
        public int FindBodygroup(string modelPath, string group) => -1;

        /// <inheritdoc/>
        public int SetBodygroup(string modelPath, int group, int value, int body) =>
            group < 0 ? body : body + group;
    }
}
