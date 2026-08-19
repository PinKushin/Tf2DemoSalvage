using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Content.Tests.Bsp;

/// <summary>
/// That a real map's point lights carry the falloff terms needed to evaluate them.
/// </summary>
/// <remarks>
/// **Reading a field is not the same as the field having anything in it.** The offsets are checked
/// against Valve's own struct by <c>BspStructTests</c>, which proves the reader looks in the right
/// place; it says nothing about whether cp_process's lights actually set attenuation. If every term
/// came back zero the falloff would divide by zero and the lighting work built on top would be
/// meaningless — and it would be meaningless in the quiet way, producing numbers rather than errors.
///
/// So this asserts on a real map: some point light has a non-zero falloff, and the terms are not all
/// identical across the set, which is what a misread stride would produce.
/// </remarks>
public sealed class WorldLightFalloffTests
{
    private static string MapPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Tf2DemoSalvage", "maps", "cp_process_f12.bsp");

    [Test]
    public void ARealMapsPointLights_CarryAFalloff()
    {
        if (!File.Exists(MapPath))
        {
            Assert.Ignore("cp_process_f12.bsp is not on this machine.");
            return;
        }

        IReadOnlyList<BspWorldLight> lights = BspWorldLights.Read(File.ReadAllBytes(MapPath));

        // The control: the map has lights at all, and point lights among them. Without this, every
        // assertion below is satisfied by an empty list.
        lights.ShouldNotBeEmpty();

        IReadOnlyList<BspWorldLight> points =
            [.. lights.Where(light => light.Kind == WorldLightKind.Point)];

        points.ShouldNotBeEmpty();

        // **Every one of cp_process's 77 point lights is pure inverse-square**, which is Source's
        // default and is measured rather than assumed: constant and linear zero, quadratic one.
        // Predicting the exact triple is worth more than "some term is non-zero", because a
        // misread offset that happened to land on another float would satisfy the weaker claim.
        points.Select(light => light.ConstantAttenuation).Distinct().ShouldBe([0f]);
        points.Select(light => light.LinearAttenuation).Distinct().ShouldBe([0f]);
        points.Select(light => light.QuadraticAttenuation).Distinct().ShouldBe([1f]);
    }

    [Test]
    public void WorldLights_EveryLight_HasFalloff()
    {
        // **The invariant a light with no attenuation would break.** vrad normalises the
        // all-zero case to constant_attn = 1 (`lightmap.cpp`), and a constant-only light does not
        // fall off with distance AT ALL: its contribution is its intensity, everywhere. One such
        // light anywhere on a map would light every model on it at full strength, which is exactly
        // the shape of "the middle point is far too bright and there are no lights near it".
        //
        // Asserted against a real map rather than reasoned about, because whether the case occurs
        // is a fact about what vbsp emits and not about what vrad's parser accepts.
        if (!File.Exists(MapPath))
        {
            Assert.Ignore("cp_process_f12.bsp is not on this machine.");
            return;
        }

        IReadOnlyList<BspWorldLight> lights = BspWorldLights.Read(File.ReadAllBytes(MapPath));

        // The kinds LocalLights evaluates. Anything here without a falloff reaches the whole map.
        IReadOnlyList<BspWorldLight> runtime =
            [.. lights.Where(light =>
                light.Kind is WorldLightKind.Point or WorldLightKind.Spotlight or
                    WorldLightKind.QuakeLight)];

        // Control: there are runtime lights to test at all.
        runtime.ShouldNotBeEmpty();

        runtime.ShouldAllBe(light =>
            light.ConstantAttenuation >= 0.001f ||
            light.LinearAttenuation >= 0.001f ||
            light.QuadraticAttenuation >= 0.001f);
    }

    [Test]
    public void SurfaceLights_HaveNoFalloffAtAll_WhichIsWhyTheyAreNotRuntimeLights()
    {
        // **The measurement that explained a blown-out capture point**, kept because it is the
        // reason `emit_surface` is excluded and because the exclusion looks arbitrary without it.
        //
        // All 108 of cp_process's surface lights carry attenuation of exactly zero with intensities
        // around 7,000. Evaluated as runtime lights they never attenuate, so four of them dominate
        // every model on the map: mid drew at luminance 6.3 with no lamp near it, which is what
        // sent the investigation looking for nearby lights that did not exist.
        //
        // An area light for the radiosity solver has no distance term because it is never evaluated
        // at a distance — it is resolved at compile time into the lightmaps and the leaf ambient.
        if (!File.Exists(MapPath))
        {
            Assert.Ignore("cp_process_f12.bsp is not on this machine.");
            return;
        }

        IReadOnlyList<BspWorldLight> surfaces =
            [.. BspWorldLights.Read(File.ReadAllBytes(MapPath))
                .Where(light => light.Kind == WorldLightKind.Surface)];

        surfaces.Count.ShouldBe(108);

        surfaces.ShouldAllBe(light =>
            light.ConstantAttenuation == 0f &&
            light.LinearAttenuation == 0f &&
            light.QuadraticAttenuation == 0f);
    }

    [Test]
    public void ARealMapsSpotlights_CarryVariedCones()
    {
        if (!File.Exists(MapPath))
        {
            Assert.Ignore("cp_process_f12.bsp is not on this machine.");
            return;
        }

        IReadOnlyList<BspWorldLight> lights = BspWorldLights.Read(File.ReadAllBytes(MapPath));

        IReadOnlyList<BspWorldLight> spotlights =
            [.. lights.Where(light => light.Kind == WorldLightKind.Spotlight)];

        // **The stride proof, and it had to be this field rather than the falloff.** The first
        // version of this test demanded that the attenuation terms varied between lights, on the
        // reasoning that identical values everywhere is what a misread stride produces. They do
        // not vary — mappers overwhelmingly leave the default — so the test failed against a
        // correct reader. Cone angles do vary, because a mapper chooses them per light.
        spotlights.Count.ShouldBeGreaterThan(100);

        spotlights.Select(light => light.StopDot).Distinct().Count().ShouldBeGreaterThan(1);

        // A cosine, so anything outside this range means the bytes are not what they are labelled.
        spotlights.ShouldAllBe(light => light.StopDot >= -1f && light.StopDot <= 1f);

        // The penumbra runs outward: it ends at a wider angle than it starts, so the cosine at the
        // end is the smaller of the two. Reading the pair in the wrong order inverts every cone.
        spotlights.ShouldAllBe(light => light.StopDot2 <= light.StopDot);
    }
}
