using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Content.Tests.Bsp;

/// <summary>
/// A world light the point cannot see takes no slot and is not folded: the light cache's per-light visibility trace (B666).
/// </summary>
/// <remarks>
/// *Read in disassembly*, `engine.dll` (TF2 x64). `FUN_1801b9570`, the no-shadow-cache attenuation, returns its ratio in
/// XMM0 (`0x1801b9930`, `0x1801b996b`; `FUN_1801b8e20`, the cached variant, at `0x1801b929d`/`0x1801b94e2`/`0x1801b9520`),
/// or 0.0 when the light is rejected. For types 0, 1, 2 and 4 (surface, point, SPOT, quake), after the distance and cone
/// ratio and the brightest-channel `r_worldlightmin` cutoff, and only when flags bit 0 is clear, it traces
/// `IEngineTrace::TraceRay` from the lit point to the light's origin, `MASK_OPAQUE` (`0x4081`), world only (filter vtable
/// `0x18035e800`), and returns 0.0 when `(1.0 - fraction) * distance > 8.0` (`0x18035c93c` = 1.0, `0x1803adc70` = 8.0).
/// `FUN_1801b60a0` adds nothing for a zero ratio: no slot, no fold into the cube.
///
/// The fraction here is a fake keyed on the END of the ray, the light's origin, so a trace cast the other way, or from a
/// different point, reads 1 and the test fails.
/// </remarks>
public sealed class LocalLightVisibilityConformanceTests
{
    /// <summary>A lamp blocked more than eight units short loses its slot, and the next lamp takes it.</summary>
    [Test]
    public void Strongest_TheBrightestLampBehindAWall_GivesItsSlotToTheFifth()
    {
        // Five lamps 64 units off; the first is the brightest, the fifth the dimmest. Untraced, the fifth is evicted.
        List<BspWorldLight> lights =
        [
            Lamp((64f, 0f, 0f), 900f),
            Lamp((-64f, 0f, 0f), 800f),
            Lamp((0f, 64f, 0f), 700f),
            Lamp((0f, -64f, 0f), 600f),
            Lamp((0f, 0f, 64f), 500f),
        ];

        Span<LocalLight> into = stackalloc LocalLight[LocalLights.MaximumLocalLights];

        // The wall stops the ray half way: 32 units of the 64 are blocked.
        Ranked.Strongest(lights, 0f, 0f, 0f, into, traceKeeps: Traced(lights, (64f, 0f, 0f), 0.5f)).ShouldBe(4);

        into[0].Red.ShouldBe(800f);
        into[1].Red.ShouldBe(700f);
        into[2].Red.ShouldBe(600f);
        into[3].Red.ShouldBe(500f, "the fifth lamp takes the occluded one's slot");
    }

    /// <summary>`> 8.0`, strictly: exactly eight units short keeps the lamp, 8.01 drops it.</summary>
    [TestCase(0.875f, 1)]
    [TestCase(1f - (8.01f / 64f), 0)]
    [TestCase(1f, 1)]
    [TestCase(0f, 0)]
    public void Strongest_ARayStoppingNearTheLamp_KeepsItWithinEightUnits(float fraction, int expected)
    {
        List<BspWorldLight> lights = [Lamp((0f, 0f, 64f), 500f)];
        Span<LocalLight> into = stackalloc LocalLight[LocalLights.MaximumLocalLights];

        Ranked.Strongest(lights, 0f, 0f, 0f, into, traceKeeps: Traced(lights, (0f, 0f, 64f), fraction)).ShouldBe(expected);
    }

    /// <summary>A spotlight is traced the same way: `FUN_1801b9570` takes types 0, 1, 2 and 4 down one path.</summary>
    [Test]
    public void Strongest_ASpotWhoseConeCoversThePointBehindAWall_TakesNoSlot()
    {
        // Aimed straight at the point from 64 units above: dot2 = 1, inside the cone. Its ratio alone admits it.
        List<BspWorldLight> lights =
        [
            new(
                (0f, 0f, 64f), (5000f, 5000f, 5000f), (0f, 0f, -1f), WorldLightKind.Spotlight,
                ConstantAttenuation: 0f, LinearAttenuation: 0f, QuadraticAttenuation: 1f,
                StopDot: 0.9f, StopDot2: 0.7f, Exponent: 1f),
        ];

        Span<LocalLight> into = stackalloc LocalLight[LocalLights.MaximumLocalLights];

        Ranked.Strongest(lights, 0f, 0f, 0f, into).ShouldBe(1, "the control: untraced, the spot reaches");
        Ranked.Strongest(lights, 0f, 0f, 0f, into, traceKeeps: Traced(lights, (0f, 0f, 64f), 0.5f)).ShouldBe(0);
    }

    /// <summary>An occluded light is not folded into the cube either: the ratio is zero, so `FUN_1801b60a0` adds nothing.</summary>
    [Test]
    public void Split_AFifthLampBehindAWall_LeavesTheCubeUnchanged()
    {
        // Four bright lamps take the slots; the fifth, dimmest, is folded when it can be seen and dropped when it cannot.
        List<BspWorldLight> lights =
        [
            Lamp((64f, 0f, 0f), 900f),
            Lamp((-64f, 0f, 0f), 800f),
            Lamp((0f, 64f, 0f), 700f),
            Lamp((0f, -64f, 0f), 600f),
            Lamp((0f, 0f, 64f), 500f),
        ];

        Span<LocalLight> into = stackalloc LocalLight[LocalLights.MaximumLocalLights];
        AmbientCube seen = default;
        AmbientCube hidden = default;

        LocalLights.Split(lights, 0f, 0f, 0f, into, ref seen).ShouldBe(4);
        LocalLights.Split(lights, 0f, 0f, 0f, into, ref hidden, traceKeeps: Traced(lights, (0f, 0f, 64f), 0.5f)).ShouldBe(4);

        // Folded onto +Z with n·d 1: 500 / 64² = 0.1220703.
        seen.PositiveZ.Red.ShouldBe(500f / 4096f, 1e-6f, "the control: seen, it is folded");
        hidden.ShouldBe(default(AmbientCube));
    }

    /// <summary>Of equal lamps the one already in keeps the slot (`0x1801ba2e0` evicts only a strictly weaker one): five of 700 seen, the fifth folded.</summary>
    [Test]
    public void Build_FiveEqualLampsOneBlocked_KeepsTheEarliestFourSeen()
    {
        List<BspWorldLight> lights =
        [
            Lamp((64f, 0f, 0f), 700f),
            Lamp((-64f, 0f, 0f), 700f),
            Lamp((0f, 64f, 0f), 700f),
            Lamp((0f, -64f, 0f), 700f),
            Lamp((0f, 0f, 64f), 700f),
            Lamp((0f, 0f, -64f), 700f),
        ];

        LightingState state = LocalLights.Build(lights, 0f, 0f, 0f, default, traceKeeps: index => index != 1);

        Enumerable.Range(0, state.Count).Select(slot => state.SlotAt(slot).Origin).ShouldBe(
            [(64f, 0f, 0f), (0f, 64f, 0f), (0f, -64f, 0f), (0f, 0f, 64f)]);
        state.Cube.NegativeZ.Red.ShouldBe(700f / 4096f, 1e-6f, "the fifth seen lamp is folded");
        state.Cube.NegativeX.Red.ShouldBe(0f, "the blocked one is neither");
    }

    /// <summary>
    /// A light whose ranking strength is NaN takes no slot, on both rankings: the luminance minimum is `>=`, which NaN fails.
    /// </summary>
    /// <remarks>
    /// Intensity (+∞, −∞, 0) passes the brightest-channel cutoff (its brightest is +∞) and has a Rec.601 luminance of
    /// 0.299·∞ − 0.587·∞ = NaN.
    /// </remarks>
    [TestCase(false)]
    [TestCase(true)]
    public void Strongest_ALightWhoseStrengthIsNaN_TakesNoSlot(bool traced)
    {
        List<BspWorldLight> lights =
        [
            Lamp((64f, 0f, 0f), 0f) with { Intensity = (float.PositiveInfinity, float.NegativeInfinity, 0f) },
            Lamp((-64f, 0f, 0f), 500f),
        ];

        Span<LocalLight> into = stackalloc LocalLight[LocalLights.MaximumLocalLights];

        Ranked.Strongest(lights, 0f, 0f, 0f, into, traceKeeps: traced ? static _ => true : null).ShouldBe(1);
        into[0].X.ShouldBe(-64f, "the finite lamp");
    }

    /// <summary>The rule itself: the ray is cast from the point to the light, and `(1 − fraction) · distance` is compared.</summary>
    [Test]
    public void TraceKeeps_TheRay_RunsFromThePointToTheLight()
    {
        BspWorldLight lamp = Lamp((10f, 20f, 30f), 500f);
        (float X, float Y, float Z)? from = null;
        (float X, float Y, float Z)? to = null;

        LocalLights.TraceKeeps(lamp, 1f, 2f, 3f, (start, end) => { (from, to) = (start, end); return 1f; }).ShouldBeTrue();

        from.ShouldBe((1f, 2f, 3f));
        to.ShouldBe((10f, 20f, 30f));
    }

    /// <summary>
    /// A visibility predicate over <paramref name="lights"/>: a fake trace from the origin whose fraction is
    /// <paramref name="fraction"/> on the ray that ends at <paramref name="blocked"/> and 1 on every other.
    /// </summary>
    private static Func<int, bool> Traced(
        List<BspWorldLight> lights, (float X, float Y, float Z) blocked, float fraction) =>
        index => LocalLights.TraceKeeps(
            lights[index], 0f, 0f, 0f, (from, to) => from == (0f, 0f, 0f) && to == blocked ? fraction : 1f);

    private static BspWorldLight Lamp((float X, float Y, float Z) origin, float bright) =>
        new(
            origin, (bright, bright, bright), (0f, 0f, 0f), WorldLightKind.Point,
            ConstantAttenuation: 0f, LinearAttenuation: 0f, QuadraticAttenuation: 1f, Radius: 100000f);
}
