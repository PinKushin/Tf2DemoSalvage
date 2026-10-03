using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Content.Tests.Bsp;

/// <summary>
/// <see cref="LocalLights.Split"/> — the four local lights a model draws with, and every other light
/// reaching it folded into its ambient cube, as the engine's lightcache does.
/// </summary>
/// <remarks>
/// Read from `engine.dll` (TF2 x64), `FUN_1801b60a0`, which adds one world light to a lighting state:
///
/// - **ranked by Rec.601 luminance times falloff** — `0.299 R + 0.587 G + 0.114 B` from the constants
///   at `0x18047be38`, `MULSS XMM6, XMM7` at `0x1801b6356` — not by the brightest channel;
/// - a light whose luminance strength is below `r_worldlightmin` (`COMISS … [RAX + 0x54]`, `JC
///   0x1801b64f5`) skips the slots and goes to the fold, except `emit_surface` (type 0);
/// - with the slots full (`r_worldlights`, capped by the hardware's light count), `FUN_1801b89a0`
///   picks the weakest slot STRICTLY weaker than the newcomer; that light is evicted and the evicted
///   one — or the newcomer when none is weaker — is folded (`FUN_1801b5db0`);
/// - the fold adds `max(0, n·d) · falloff · intensity` to each of the six faces (`FUN_1801b5db0`'s loop,
///   `0.0 < fVar7` guard), with d the unit direction to the light.
///
/// `istudiorender.h` says the same in a comment: the cube is "ambient, and lights that aren't in
/// locallight[]". The port used to drop them (filed under B424 as "an evicted light is dropped, not
/// folded into the cube").
/// </remarks>
public sealed class LocalLightFoldTests
{
    [Test]
    public void Split_WithSixLamps_FoldsTheTwoWeakestIntoTheCube()
    {
        List<BspWorldLight> lights = [];

        foreach (int distance in new[] { 600, 100, 500, 200, 400, 300 })
        {
            lights.Add(Lamp(distance, (1000f, 1000f, 1000f)));
        }

        Span<LocalLight> into = stackalloc LocalLight[LocalLights.MaximumLocalLights];
        AmbientCube cube = default;

        LocalLights.Split(lights, 0f, 0f, 0f, into, ref cube).ShouldBe(4);

        into[3].X.ShouldBe(400f);

        // 1000 / 500² + 1000 / 600², all on +X: the two lamps lie along +X, so every other face's
        // n·d is zero or negative and takes nothing.
        float folded = (1000f / (500f * 500f)) + (1000f / (600f * 600f));

        cube.PositiveX.Red.ShouldBe(folded, 1e-7f);
        cube.PositiveX.Blue.ShouldBe(folded, 1e-7f);
        cube.NegativeX.ShouldBe((0f, 0f, 0f));
        cube.PositiveY.ShouldBe((0f, 0f, 0f));
        cube.PositiveZ.ShouldBe((0f, 0f, 0f));
    }

    [Test]
    public void Split_ABlueLampAgainstFourGreen_RanksByLuminanceAndFoldsTheBlue()
    {
        // At one distance: blue 1000 has the brightest channel (1000 against 600) but the lower
        // luminance (114 against 352.2), so the engine evicts it. A max-channel ranking keeps it.
        List<BspWorldLight> lights =
        [
            Lamp(100f, (0f, 0f, 1000f)),
            Lamp(100f, (0f, 600f, 0f)),
            Lamp(100f, (0f, 600f, 0f)),
            Lamp(100f, (0f, 600f, 0f)),
            Lamp(100f, (0f, 600f, 0f)),
        ];

        Span<LocalLight> into = stackalloc LocalLight[LocalLights.MaximumLocalLights];
        AmbientCube cube = default;

        LocalLights.Split(lights, 0f, 0f, 0f, into, ref cube).ShouldBe(4);

        foreach (LocalLight chosen in into)
        {
            chosen.Blue.ShouldBe(0f, "the blue lamp is the weakest by luminance and is folded");
        }

        cube.PositiveX.ShouldBe((0f, 0f, 1000f / (100f * 100f)));
    }

    [Test]
    public void Split_ALampBelowWorldLightMinByLuminanceOnly_IsFoldedNotChosen()
    {
        // Blue 1 at 50 units: brightest channel 1 / 2500 = 0.0004 passes the trace function's
        // r_worldlightmin (0.0002, max channel, `FUN_1801b8e20`); luminance 0.114 / 2500 fails the
        // ranking's, so it is folded rather than given a slot.
        List<BspWorldLight> lights = [Lamp(50f, (0f, 0f, 1f))];

        Span<LocalLight> into = stackalloc LocalLight[LocalLights.MaximumLocalLights];
        AmbientCube cube = default;

        LocalLights.Split(lights, 0f, 0f, 0f, into, ref cube).ShouldBe(0);

        cube.PositiveX.ShouldBe((0f, 0f, 1f / 2500f));
    }

    private static BspWorldLight Lamp(float x, (float Red, float Green, float Blue) intensity) =>
        new()
        {
            Origin = (x, 0f, 0f),
            Intensity = intensity,
            Kind = WorldLightKind.Point,
            ConstantAttenuation = 0f,
            LinearAttenuation = 0f,
            QuadraticAttenuation = 1f,
            Radius = 0f,
        };
}
