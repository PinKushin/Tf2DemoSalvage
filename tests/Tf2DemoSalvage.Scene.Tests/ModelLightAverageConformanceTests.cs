using System;
using System.Linq;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>A moving model's lighting averaged over time: `r_lightaverage`, `r_lightinterp` (B676).</summary>
/// <remarks>
/// *Read in decompile and disassembly*, `engine.dll` build 11191711, `D:/ghidra-proj/current`: `FUN_1800f4310`, called by
/// `FUN_1800f2dd0` when `*param_7` and `param_4` are both 0; `FUN_1800f2570` the first draw; `FUN_1800f4090` the cube; `FUN_1803366d0` is
/// `exp` (its 64-entry table and `64 / ln 2`); `FUN_1801b7680` the fold; `FUN_1800a4320` the client's time, tick times interval
/// plus a remainder. Every value below is arithmetic on those lines, predicted here, not read back from the code.
/// </remarks>
public sealed class ModelLightAverageConformanceTests
{
    private const int Entity = 7;
    private const string Model = "models/player/soldier.mdl";
    private const float Rate = 5f;

    /// <summary>`exp( -5 * 0.2 )`: every later draw here is 0.2 seconds after the last.</summary>
    private static readonly float F = MathF.Exp(-1f);

    private static readonly BspWorldLight LampA = new((10f, 0f, 0f), (40f, 20f, 10f), default, WorldLightKind.Point, QuadraticAttenuation: 1f);
    private static readonly BspWorldLight LampB = new((0f, 10f, 0f), (8f, 8f, 8f), default, WorldLightKind.Point, QuadraticAttenuation: 1f);

    private static AmbientCube Flat(float value) => new(
        (value, value, value), (value, value, value), (value, value, value),
        (value, value, value), (value, value, value), (value, value, value));

    private static LightingState State(float cube, params BspWorldLight[] lights)
    {
        LightingState state = new(Flat(cube));

        foreach (BspWorldLight light in lights)
        {
            state.Put(light);
        }

        return state;
    }

    private static ModelLightAverage.Averaged Draw(
        ModelLightAverage average, LightingState state, double seconds, SunLight? sun = null, int half = 0, string model = Model, Func<int, float>? style = null) =>
        average.Draw(Entity, model, state, state.Cube, sun, state.Lighting(style).Locals, 0f, 0f, 0f, seconds, half, style);

    private static ModelLightAverage Average(int limit = 4) => new() { Rate = Rate, Limit = limit };

    [Test]
    public void Draw_TheFirstDraw_IsTheStateAsItStands()
    {
        ModelLightAverage.Averaged drawn = Draw(Average(), State(0.5f, LampA), 10d);

        drawn.Cube.ShouldBe(Flat(0.5f));
        drawn.Locals.Single().Red.ShouldBe(40f);
    }

    /// <summary>`new - (new - old) * f`, from 1 to 0.5 at f = 1/e.</summary>
    [Test]
    public void Draw_AFifthOfASecondLater_MovesEachFaceByExpOfRateTimesDt()
    {
        ModelLightAverage average = Average();

        Draw(average, State(1f), 10d);

        Draw(average, State(0.5f), 10.2d).Cube.PositiveX.Red.ShouldBe(0.5f + (0.5f * F), 0.0001f);
    }

    /// <summary>With dt 0, f is 1: the history, whatever the state says.</summary>
    [Test]
    public void Draw_AtTheSameTime_IsTheHistory()
    {
        ModelLightAverage average = Average();

        Draw(average, State(1f, LampA), 10d);

        ModelLightAverage.Averaged drawn = Draw(average, State(0.25f, LampB), 10d);

        drawn.Cube.ShouldBe(Flat(1f));
        // In the state's order, then what left: B new at nothing of its colour, A gone at the whole of its own.
        drawn.Locals.Select(static local => (local.X, local.Red)).ShouldBe([(0f, 0f), (10f, 40f)]);
    }

    /// <summary>A clock a hair behind: dt is 0, and the stored time stays, so the next draw counts from the later one.</summary>
    [Test]
    public void Draw_AHairBehindTheStoredTime_HoldsAndKeepsTheStoredTime()
    {
        ModelLightAverage average = Average();

        Draw(average, State(1f), 10d);
        Draw(average, State(0.5f), 9.99d).Cube.ShouldBe(Flat(1f));

        Draw(average, State(0.5f), 10.2d).Cube.PositiveX.Red.ShouldBe(0.5f + (0.5f * F), 0.0001f);
    }

    [TestCase(30f, 8f)]
    [TestCase(-30f, 0f)]
    public void Draw_AFacePastItsRange_IsClampedToZeroToEightAndStoredSo(float wanted, float expected)
    {
        ModelLightAverage average = Average();

        Draw(average, State(1f), 10d);
        Draw(average, State(wanted), 10.2d).Cube.PositiveX.Red.ShouldBe(expected);

        // The clamped value is what was stored: at dt 0 it is read back.
        Draw(average, State(1f), 10.2d).Cube.PositiveX.Red.ShouldBe(expected);
    }

    /// <summary>
    /// **The blend is of the raw intensity and the style comes after** (`FUN_1800f2010` multiplies the slot's colour by the style's
    /// value over 264): a lamp whose style halves at once halves at once, with no easing, here at the same colour.
    /// </summary>
    [Test]
    public void Draw_ALampWhoseStyleChanged_TakesTheStyleWithoutEasing()
    {
        ModelLightAverage average = Average();

        Draw(average, State(0f, LampA), 10d, style: static _ => 1f);

        Draw(average, State(0f, LampA), 10.2d, style: static _ => 0.5f).Locals.Single().Red.ShouldBe(20f, 0.001f);
    }

    /// <summary>Matched by identity, not by slot: A moved from slot 0 to slot 1, and it blends from its own colour.</summary>
    [Test]
    public void Draw_ALightInAnotherSlot_BlendsFromItsOwnColour()
    {
        ModelLightAverage average = Average();
        BspWorldLight brighter = LampA with { Intensity = (80f, 80f, 80f) };

        Draw(average, State(0f, LampA with { Intensity = (40f, 40f, 40f) }, LampB), 10d);

        // The same light is the same pointer in the engine, so identity must survive a change of colour to be tested at all:
        // a dynamic light is the one kind whose colour changes under one identity; see the test below.
        ModelLightAverage.Averaged drawn = Draw(average, State(0f, LampB, brighter), 10.2d);

        drawn.Locals[0].Red.ShouldBe(8f, 0.001f, "B was in the history at 8");
        drawn.Locals[1].Red.ShouldBe(80f * (1f - F), 0.001f, "a light not in the history fades in from zero");
    }

    /// <summary>A dlight is a slot in `DAT_180775730` that the engine overwrites, so it moves and recolours as one pointer.</summary>
    [Test]
    public void Draw_ADynamicLightThatMovedAndRecoloured_BlendsAsOneLight()
    {
        ModelLightAverage average = Average();
        BspWorldLight there = new((5f, 0f, 0f), (10f, 10f, 10f), default, WorldLightKind.Point, QuadraticAttenuation: 1f, DynamicSlot: 3);
        BspWorldLight here = there with { Origin = (9f, 0f, 0f), Intensity = (20f, 20f, 20f) };

        Draw(average, State(0f, there), 10d);

        LocalLight drawn = Draw(average, State(0f, here), 10.2d).Locals.Single();

        drawn.X.ShouldBe(9f);
        drawn.Red.ShouldBe(20f - (10f * F), 0.001f);
    }

    /// <summary>Two dlights in different slots are two lights, even at one place with one colour.</summary>
    [Test]
    public void Draw_TwoDynamicSlotsAlike_AreTwoLights()
    {
        ModelLightAverage average = Average();
        BspWorldLight one = new((5f, 0f, 0f), (10f, 10f, 10f), default, WorldLightKind.Point, DynamicSlot: 3);

        Draw(average, State(0f, one), 10d);

        ModelLightAverage.Averaged drawn = Draw(average, State(0f, one with { DynamicSlot = 4 }), 10.2d);

        // The new one fades in; the old one, left, stays at f of its colour.
        drawn.Locals[0].Red.ShouldBe(10f * (1f - F), 0.001f);
        drawn.Locals[1].Red.ShouldBe(10f * F, 0.001f);
    }

    [Test]
    public void Draw_ALightThatLeftTheSlots_StaysAtFOfItsColourThenAtFOfThat()
    {
        ModelLightAverage average = Average();

        Draw(average, State(0f, LampA), 10d);

        Draw(average, State(0f), 10.2d).Locals.Single().Red.ShouldBe(40f * F, 0.001f);
        Draw(average, State(0f), 10.4d).Locals.Single().Red.ShouldBe(40f * F * F, 0.001f);
    }

    /// <summary>`1.0 &lt;= r² + g² + b²` on the STORED colour, at the boundary from both sides.</summary>
    [TestCase(1f, 1)]
    [TestCase(0.99f, 0)]
    public void Draw_ALeftLightByItsStoredLength_IsKeptOnlyAtOneOrMore(float red, int expected)
    {
        ModelLightAverage average = Average();

        Draw(average, State(0f, LampA with { Intensity = (red, 0f, 0f) }), 10d);

        Draw(average, State(0f), 10.2d).Locals.Count.ShouldBe(expected);
    }

    /// <summary>`if (3 &lt; n) break`: four slots hold, so a light that left has no room, and is forgotten.</summary>
    [Test]
    public void Draw_FourLightsInTheState_DropsTheOneThatLeft()
    {
        ModelLightAverage average = Average();
        BspWorldLight[] four = [.. Enumerable.Range(1, 4).Select(index => LampB with { Origin = (index, 0f, 0f) })];

        Draw(average, State(0f, LampA), 10d);

        ModelLightAverage.Averaged drawn = Draw(average, State(0f, four), 10.2d);

        drawn.Locals.Count.ShouldBe(4);
        drawn.Locals.Select(static local => local.X).ShouldNotContain(10f);

        // Forgotten: at dt 0 the four are all, with A nowhere.
        Draw(average, State(0f, four), 10.2d).Locals.Select(static local => local.X).ShouldNotContain(10f);
    }

    /// <summary>
    /// Past the draw's light limit (`min( r_worldlights, MaxNumLights )`) a light that left is folded at the model's point into what is
    /// DRAWN, not what is stored, and is still stored as a slot. Here the limit is 1, the light 10 units along +X at colour 40:
    /// a quadratic falloff of 1/100, so the +X face gains `40·f/100`.
    /// </summary>
    [Test]
    public void Draw_ALeftLightPastTheLimit_IsFoldedIntoTheDrawnCubeAlone()
    {
        ModelLightAverage average = Average(limit: 1);

        Draw(average, State(0f, LampB), 10d);
        Draw(average, State(0f, LampB, LampA), 10d);

        // A has come and is blended in over a second, so its stored colour is whole; then it leaves.
        Draw(average, State(0f, LampB, LampA), 11d);
        Draw(average, State(0f, LampB, LampA), 21d);

        ModelLightAverage.Averaged left = Draw(average, State(0f, LampB), 21.2d);

        left.Locals.Single().Y.ShouldBe(10f, "only one light is drawn");
        left.Cube.PositiveX.Red.ShouldBe(40f * F / 100f, 0.001f, "folded at f of its colour");
        left.Cube.NegativeX.Red.ShouldBe(0f, "a face turned away takes nothing");

        // The fold did not reach the store: at dt 0 the stored cube is zero, and A, still a stored slot, folds again.
        Draw(average, State(0f, LampB), 21.2d).Cube.PositiveX.Red.ShouldBe(40f * F * 1f / 100f, 0.001f);
    }

    /// <summary>The engine's fold traces (`FUN_1801baeb0`, flags 0): a light the point cannot see folds nothing.</summary>
    [Test]
    public void Draw_ALeftLightPastTheLimitBehindAWall_FoldsNothing()
    {
        ModelLightAverage average = new() { Rate = Rate, Limit = 1, Seen = static (_, _, _, _) => false };

        Draw(average, State(0f, LampB, LampA), 10d);
        Draw(average, State(0f, LampB), 10.2d).Cube.PositiveX.Red.ShouldBe(0f);
    }

    /// <summary>The sun past the limit: direction `-normal`, falloff and angle 1, so a face square to it gains its whole colour at f.</summary>
    [Test]
    public void Draw_TheSunPastTheLimit_IsFoldedOntoTheFaceTurnedToIt()
    {
        ModelLightAverage average = Average(limit: 1);

        Draw(average, State(0f, LampB), 10d, new SunLight(4f, 4f, 4f, 0f, 0f, -1f));

        ModelLightAverage.Averaged left = Draw(average, State(0f, LampB), 10.2d);

        left.Sun.ShouldBeNull("past the limit it is folded and not drawn");
        left.Cube.PositiveZ.Green.ShouldBe(4f * F, 0.001f);
        left.Cube.NegativeZ.Green.ShouldBe(0f);
    }

    /// <summary>The sun is a slot (`FUN_1800f2010` case 3), so it fades out as any light that left does, down the same direction.</summary>
    [Test]
    public void Draw_AModelWalkingOutOfTheSun_FadesItAtFOfItsColour()
    {
        ModelLightAverage average = Average();
        SunLight sun = new(2f, 2f, 2f, 0f, 0f, -1f);

        Draw(average, State(0f), 10d, sun);

        SunLight drawn = Draw(average, State(0f), 10.2d).Sun.ShouldNotBeNull();

        drawn.Red.ShouldBe(2f * F, 0.001f);
        drawn.DirectionZ.ShouldBe(-1f);
    }

    [Test]
    public void Draw_AModelWalkingIntoTheSun_FadesItInFromZero()
    {
        ModelLightAverage average = Average();

        Draw(average, State(0f), 10d);

        Draw(average, State(0f), 10.2d, new SunLight(2f, 2f, 2f, 0f, 0f, -1f)).Sun.ShouldNotBeNull().Red.ShouldBe(2f * (1f - F), 0.001f);
    }

    /// <summary>The length rule is on the raw colour, in the units the lump gives, so a sun under 1 is gone at once.</summary>
    [Test]
    public void Draw_ADimSunThatLeft_IsGoneAtOnce()
    {
        ModelLightAverage average = Average();

        Draw(average, State(0f), 10d, new SunLight(0.5f, 0.5f, 0.5f, 0f, 0f, -1f));

        Draw(average, State(0f), 10.2d).Sun.ShouldBeNull();
    }

    /// <summary>The sun takes one of the four: with it stored and four lamps in the state there is no room for it.</summary>
    [Test]
    public void Draw_TheSunThatLeftWithFourLamps_HasNoRoom()
    {
        ModelLightAverage average = Average();
        BspWorldLight[] four = [.. Enumerable.Range(1, 4).Select(index => LampB with { Origin = (index, 0f, 0f) })];

        Draw(average, State(0f), 10d, new SunLight(2f, 2f, 2f, 0f, 0f, -1f));

        Draw(average, State(0f, four), 10.2d).Sun.ShouldBeNull();
    }

    /// <summary>`r_lightaverage 0`, then `r_lightinterp 0`: both hand the state back untouched, and neither stores anything.</summary>
    [Test]
    public void Draw_WithEitherSwitchOff_IsTheStateAndStoresNothing()
    {
        ModelLightAverage off = new() { Enabled = false, Rate = Rate };
        ModelLightAverage zero = new() { Rate = 0f };

        foreach (ModelLightAverage average in new[] { off, zero })
        {
            Draw(average, State(1f, LampA), 10d);

            ModelLightAverage.Averaged drawn = Draw(average, State(0.5f), 10.2d);

            drawn.Cube.ShouldBe(Flat(0.5f));
            drawn.Locals.ShouldBeEmpty();
        }

        // Nothing was stored: turned on now, this is a first draw.
        off.Enabled = true;
        Draw(off, State(0.25f), 10.4d).Cube.ShouldBe(Flat(0.25f));
    }

    /// <summary>The instance goes with the model it was made for, so a new model is a first draw.</summary>
    [Test]
    public void Draw_AfterTheEntitysModelChanged_IsAFirstDraw()
    {
        ModelLightAverage average = Average();

        Draw(average, State(1f), 10d);

        Draw(average, State(0.5f), 10.2d, model: "models/player/medic.mdl").Cube.ShouldBe(Flat(0.5f));
    }

    /// <summary>
    /// **The clock alone does not start an instance again, however far back**: `dt` is 0 and the stored time stays. A skip back is the
    /// presenter's to say, by <see cref="ModelLightAverage.Clear"/> (`FramePresenterSeekTests`).
    /// </summary>
    [Test]
    public void Draw_TheClockFarBehind_HoldsTheHistory()
    {
        ModelLightAverage average = Average();

        Draw(average, State(1f), 100d);

        Draw(average, State(0.5f), 50d).Cube.ShouldBe(Flat(1f));
    }

    /// <summary>
    /// Two lump entries alike in every field are two pointers, so each finds its own colour and neither is left over. Compared by value, both
    /// took the first stored one and the second stayed behind as a light that left, on top of the two.
    /// </summary>
    [Test]
    public void Draw_TwoLumpLightsAlike_AreTwoLightsAndLeaveNoGhost()
    {
        ModelLightAverage average = Average();
        BspWorldLight one = LampA with { LumpIndex = 7 };
        BspWorldLight two = LampA with { LumpIndex = 8 };

        Draw(average, State(0f, one, two), 10d);

        ModelLightAverage.Averaged drawn = Draw(average, State(0f, one, two), 10.2d);

        drawn.Locals.Count.ShouldBe(2);
        drawn.Locals.Sum(static local => local.Red).ShouldBe(80f, 0.001f);
    }

    /// <summary>
    /// `FUN_1800f4310` copies a left light through its pointer, so a dlight's place is what its slot holds now, and only the colour is the
    /// stored one, at f.
    /// </summary>
    [Test]
    public void Draw_ADynamicLightThatLeftWhileItsSlotMoved_FadesAtTheSlotsPlaceFromItsStoredColour()
    {
        BspWorldLight[] slots = new BspWorldLight[8];
        ModelLightAverage average = new() { Rate = Rate, DynamicSlots = slots };
        BspWorldLight there = new((5f, 0f, 0f), (10f, 10f, 10f), default, WorldLightKind.Point, QuadraticAttenuation: 1f, DynamicSlot: 3);

        Draw(average, State(0f, there), 10d);

        slots[3] = there with { Origin = (9f, 0f, 0f), Intensity = (99f, 99f, 99f) };

        LocalLight drawn = Draw(average, State(0f), 10.2d).Locals.Single();

        drawn.X.ShouldBe(9f);
        drawn.Red.ShouldBe(10f * F, 0.001f);
    }

    /// <summary>
    /// `FUN_1800f4090` clamps its own copy of f and `FUN_1800f4310` keeps the raw one for the slots: at a rate of -5 f is e, so the cube takes
    /// the history whole and a slot extrapolates past it. The cvar has no bounds (`FUN_180283e80`).
    /// </summary>
    [Test]
    public void Draw_ANegativeRate_ClampsFInTheCubeAndExtrapolatesASlot()
    {
        ModelLightAverage average = new() { Rate = -5f };
        BspWorldLight dimmer = LampA with { LumpIndex = 1, Intensity = (20f, 20f, 20f) };
        BspWorldLight brighter = dimmer with { Intensity = (40f, 40f, 40f) };

        Draw(average, State(1f, dimmer), 10d);

        ModelLightAverage.Averaged drawn = Draw(average, State(0.5f, brighter), 10.2d);

        drawn.Cube.ShouldBe(Flat(1f));
        drawn.Locals.Single().Red.ShouldBe(40f - (20f * MathF.Exp(1f)), 0.001f);
    }

    /// <summary>A NaN rate is not 0, so it passes the gate, and NaN stays in the history, as both the engine's `COMISS` pairs let it.</summary>
    [Test]
    public void Draw_ANaNRate_PersistsInTheHistoryAsTheEnginesDoes()
    {
        ModelLightAverage average = new() { Rate = float.NaN };

        Draw(average, State(1f), 10d);

        Draw(average, State(0.5f), 10.2d).Cube.PositiveX.Red.ShouldBe(float.NaN);
        average.Rate = Rate;
        Draw(average, State(0.5f), 10.4d).Cube.PositiveX.Red.ShouldBe(float.NaN);
    }

    /// <summary>Forward, the engine's own arithmetic: at sixty seconds f is nothing, and what left is gone below length one.</summary>
    [Test]
    public void Draw_AJumpForward_IsTheNewState()
    {
        ModelLightAverage average = Average();

        Draw(average, State(1f, LampA), 10d);

        ModelLightAverage.Averaged drawn = Draw(average, State(0.5f), 70d);

        drawn.Cube.ShouldBe(Flat(0.5f));
        drawn.Locals[0].Red.ShouldBe(0f, 0.0001f, "kept one draw at f of its colour, nothing left of it");
        Draw(average, State(0.5f), 70.2d).Locals.ShouldBeEmpty();
    }

    /// <summary>A, B, B, A, each 0.2 seconds on, by the closed form: each step is `new - (new - last) * f`.</summary>
    [Test]
    public void Draw_AThenBThenBThenA_FollowsTheRecurrence()
    {
        ModelLightAverage average = Average();

        Draw(average, State(1f), 10d);
        float one = 0.25f - ((0.25f - 1f) * F);
        Draw(average, State(0.25f), 10.2d).Cube.PositiveZ.Blue.ShouldBe(one, 0.0001f);
        float two = 0.25f - ((0.25f - one) * F);
        Draw(average, State(0.25f), 10.4d).Cube.PositiveZ.Blue.ShouldBe(two, 0.0001f);
        float three = 1f - ((1f - two) * F);
        Draw(average, State(1f), 10.6d).Cube.PositiveZ.Blue.ShouldBe(three, 0.0001f);
    }

    /// <summary>Entities do not share a history.</summary>
    [Test]
    public void Draw_AnotherEntity_HasItsOwnHistory()
    {
        ModelLightAverage average = Average();

        Draw(average, State(1f), 10d);

        LightingState other = State(0.5f);

        average.Draw(Entity + 1, Model, other, other.Cube, null, [], 0f, 0f, 0f, 10.2d, 0, null).Cube.ShouldBe(Flat(0.5f));
    }

    /// <summary>
    /// The frame in flight reads the list it was handed from another thread (B581), so a draw for the other pose half must not write it.
    /// </summary>
    [Test]
    public void Draw_ForTheOtherPoseHalf_LeavesTheListThatFrameWasHanded()
    {
        ModelLightAverage average = Average();

        System.Collections.Generic.IReadOnlyList<LocalLight> first = Draw(average, State(0f, LampA), 10d, half: 0).Locals;

        Draw(average, State(0f, LampB), 10.2d, half: 1);

        first.Single().Red.ShouldBe(40f);
    }

    [Test]
    public void Clear_ALevelShutdown_MakesTheNextDrawAFirstDraw()
    {
        ModelLightAverage average = Average();

        Draw(average, State(1f), 10d);
        average.Clear();

        Draw(average, State(0.5f), 10.2d).Cube.ShouldBe(Flat(0.5f));
    }
}
