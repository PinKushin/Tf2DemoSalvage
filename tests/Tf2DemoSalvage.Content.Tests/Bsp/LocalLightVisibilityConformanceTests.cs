using System;
using System.Buffers;
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
        LocalLights.Strongest(lights, 0f, 0f, 0f, into, traceKeeps: Traced(lights, (64f, 0f, 0f), 0.5f)).ShouldBe(4);

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

        LocalLights.Strongest(lights, 0f, 0f, 0f, into, traceKeeps: Traced(lights, (0f, 0f, 64f), fraction)).ShouldBe(expected);
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

        LocalLights.Strongest(lights, 0f, 0f, 0f, into).ShouldBe(1, "the control: untraced, the spot reaches");
        LocalLights.Strongest(lights, 0f, 0f, 0f, into, traceKeeps: Traced(lights, (0f, 0f, 64f), 0.5f)).ShouldBe(0);
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

    /// <summary>
    /// Ranking first and tracing strongest-down takes the same lights, in the same slots, as tracing every light, which is
    /// what the engine does (and what `Split`, which folds, still does). Ties included: two lamps share a strength.
    /// </summary>
    [Test]
    public void Strongest_SixLampsTheStrongestBlocked_TakesTheSameLightsAsTracingEveryOne()
    {
        // All 64 units off, strengths 900, 800, 700, 700, 600, 500, 400 / 4096. The 900 and the first 700 are behind a wall.
        List<BspWorldLight> lights =
        [
            Lamp((64f, 0f, 0f), 900f),
            Lamp((-64f, 0f, 0f), 800f),
            Lamp((0f, 64f, 0f), 700f),
            Lamp((0f, -64f, 0f), 700f),
            Lamp((0f, 0f, 64f), 600f),
            Lamp((0f, 0f, -64f), 500f),
            Lamp((45.254833f, 45.254833f, 0f), 400f),
        ];

        HashSet<int> blocked = [0, 2];
        List<int> traced = [];
        Func<int, bool> keeps = index =>
        {
            traced.Add(index);
            return !blocked.Contains(index);
        };

        Span<LocalLight> lazy = stackalloc LocalLight[LocalLights.MaximumLocalLights];
        LocalLights.Strongest(lights, 0f, 0f, 0f, lazy, traceKeeps: keeps).ShouldBe(4);

        // Strongest-down: 900 (blocked), 800, 700 (blocked), 700, 600, 500 — and the 400 is never traced.
        traced.ShouldBe([0, 1, 2, 3, 4, 5]);

        // Tracing every one: what the lamps the trace keeps rank to, untraced, and `Split`'s slots.
        List<BspWorldLight> seen = [.. lights.Where((_, index) => !blocked.Contains(index))];
        Span<LocalLight> every = stackalloc LocalLight[LocalLights.MaximumLocalLights];
        LocalLights.Strongest(seen, 0f, 0f, 0f, every).ShouldBe(4);

        Span<LocalLight> split = stackalloc LocalLight[LocalLights.MaximumLocalLights];
        AmbientCube cube = default;
        LocalLights.Split(lights, 0f, 0f, 0f, split, ref cube, traceKeeps: index => !blocked.Contains(index)).ShouldBe(4);

        lazy.ToArray().ShouldBe(every.ToArray());
        lazy.ToArray().ShouldBe(split.ToArray());
        lazy.ToArray().Select(lamp => (lamp.X, lamp.Y, lamp.Z)).ShouldBe(
            [(-64f, 0f, 0f), (0f, -64f, 0f), (0f, 0f, 64f), (0f, 0f, -64f)]);
    }

    /// <summary>
    /// Past the stack's bound the ranking rents instead (<see cref="LocalLights.StackCandidates"/>), and takes the same
    /// lights as the folding ranking, which traces every one: 80 lamps whose brightness is a shuffle of 80 steps, so the
    /// list arrives out of order and the rented path must sort it, every third blocked.
    /// </summary>
    [Test]
    public void Strongest_MoreCandidatesThanTheStackHolds_TakesTheSameLightsAsTracingEveryOne()
    {
        const int Count = LocalLights.StackCandidates + 16;
        List<BspWorldLight> lights = [];

        // 37 is coprime with 80, so `37 · lamp mod 80` visits every step once, in no sorted order. The lamps sit a
        // thousandth of a unit apart, which names each one and moves its strength by far less than a brightness step.
        for (int lamp = 0; lamp < Count; lamp++)
        {
            lights.Add(Lamp((64f, lamp * 0.001f, 0f), Bright(lamp)));
        }

        Func<int, bool> keeps = static index => index % 3 != 0;
        float[] expected = [.. Enumerable.Range(0, Count).Where(index => keeps(index)).OrderByDescending(Bright).Take(4).Select(index => index * 0.001f)];

        Span<LocalLight> lazy = stackalloc LocalLight[LocalLights.MaximumLocalLights];
        LocalLights.Strongest(lights, 0f, 0f, 0f, lazy, traceKeeps: keeps).ShouldBe(4);

        Span<LocalLight> split = stackalloc LocalLight[LocalLights.MaximumLocalLights];
        AmbientCube cube = default;
        LocalLights.Split(lights, 0f, 0f, 0f, split, ref cube, traceKeeps: keeps).ShouldBe(4);

        lazy.ToArray().ShouldBe(split.ToArray());
        lazy.ToArray().Select(lamp => lamp.Y).ShouldBe(expected, "the control: the four brightest unblocked lamps, wherever they sat in the list");

        static float Bright(int lamp) => 1000f + ((37 * lamp % Count) * 10f);
    }

    /// <summary>Of equal lamps the earlier keeps the slot, as `Insert` keeps it: five of 700, the fifth not taken.</summary>
    [Test]
    public void Strongest_FiveEqualLampsOneBlocked_KeepsTheEarliestFourSeen()
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

        Span<LocalLight> lazy = stackalloc LocalLight[LocalLights.MaximumLocalLights];
        LocalLights.Strongest(lights, 0f, 0f, 0f, lazy, traceKeeps: index => index != 1).ShouldBe(4);

        Span<LocalLight> split = stackalloc LocalLight[LocalLights.MaximumLocalLights];
        AmbientCube cube = default;
        LocalLights.Split(lights, 0f, 0f, 0f, split, ref cube, traceKeeps: index => index != 1).ShouldBe(4);

        lazy.ToArray().ShouldBe(split.ToArray());
        lazy.ToArray().Select(lamp => (lamp.X, lamp.Y, lamp.Z)).ShouldBe(
            [(64f, 0f, 0f), (0f, 64f, 0f), (0f, -64f, 0f), (0f, 0f, 64f)]);
    }

    /// <summary>
    /// A traced ranking allocates nothing once warm: a static prop under a live dlight ranks this way every frame
    /// (`StaticPropFullLightingAt` → `WithStyleZero`).
    /// </summary>
    /// <remarks>
    /// If this ever fails again, capture the allocation BY TYPE first (an `EventListener` on the runtime's allocation
    /// events, or `dotnet-counters`/`dotnet-trace`) before changing anything: the one recorded failure was never explained.
    /// </remarks>
    [Test]
    public void Strongest_TracedAndWarm_AllocatesNothing()
    {
        List<BspWorldLight> lights =
        [
            Lamp((64f, 0f, 0f), 900f),
            Lamp((-64f, 0f, 0f), 800f),
            Lamp((0f, 64f, 0f), 700f),
            Lamp((0f, -64f, 0f), 700f),
            Lamp((0f, 0f, 64f), 600f),
            Lamp((0f, 0f, -64f), 500f),
        ];

        Func<int, bool> keeps = static index => index % 2 == 1;
        Span<LocalLight> into = stackalloc LocalLight[LocalLights.MaximumLocalLights];

        LocalLights.Strongest(lights, 0f, 0f, 0f, into, traceKeeps: keeps).ShouldBe(3, "the control: lamps 1, 3 and 5 are kept");

        // **Warm until the steady state, the condition this test is about** (B667's flake, second occurrence in B717's gate):
        // in a full run one 100-call window during warm-up allocated 1,504 B once and every window after it 0, a one-off
        // during warm-up rather than a per-call cost. So warm in windows of 100 until one allocates nothing; a ranking that
        // allocates per call never gets there, and fails here with what its last window cost.
        long lastWindow = -1;

        for (int window = 0; window < 100 && lastWindow != 0; window++)
        {
            long windowAt = GC.GetAllocatedBytesForCurrentThread();

            for (int call = 0; call < 100; call++)
            {
                LocalLights.Strongest(lights, 0f, 0f, 0f, into, traceKeeps: keeps);
            }

            lastWindow = GC.GetAllocatedBytesForCurrentThread() - windowAt;
        }

        lastWindow.ShouldBe(0L, "no warm-up window of 100 calls allocated nothing");

        // **A revert guard, not a model of the flake this test once showed** (7,920 B in one full run, B667; its source is
        // unidentified). The ranking keeps a short list on the stack; were it to rent again, an emptied pool would make it
        // allocate. This thread holds the array a renting ranking would take, so a return to the pool reads 152 B and fails.
        // No forced collection: one was tried and on its own trims nothing recently used, so it added a stall for every
        // parallel test and no condition.
        (float Strength, int Index)[] held = ArrayPool<(float Strength, int Index)>.Shared.Rent(lights.Count);

        // Measured in ten windows of 100, so a failure says whether it was one window or every one.
        Span<long> windows = stackalloc long[10];
        long before = GC.GetAllocatedBytesForCurrentThread();

        for (int window = 0; window < windows.Length; window++)
        {
            long windowAt = GC.GetAllocatedBytesForCurrentThread();

            for (int call = 0; call < 100; call++)
            {
                LocalLights.Strongest(lights, 0f, 0f, 0f, into, traceKeeps: keeps);
            }

            windows[window] = GC.GetAllocatedBytesForCurrentThread() - windowAt;
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        ArrayPool<(float Strength, int Index)>.Shared.Return(held);

        allocated.ShouldBe(0L, $"by window of 100 calls: {string.Join(", ", windows.ToArray())}");
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

        LocalLights.Strongest(lights, 0f, 0f, 0f, into, traceKeeps: traced ? static _ => true : null).ShouldBe(1);
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
