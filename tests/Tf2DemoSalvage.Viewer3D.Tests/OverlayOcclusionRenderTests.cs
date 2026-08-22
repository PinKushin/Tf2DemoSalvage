using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Viewer3D;

namespace Tf2DemoSalvage.Viewer3D.Tests;

/// <summary>
/// A prop standing in front of a marked wall hides the marking — measured in pixels.
/// </summary>
/// <remarks>
/// **This is the test the other conformance suites should have been.** Every one written for B135
/// asserted something about the SDK — "does <c>materialsystem_config.h</c> contain
/// <c>-262144</c>" — and the owner named the flaw exactly:
///
/// > "the conf tests have to test our code against valves or its really not testing anything
/// > because im pretty sure valve tested their code themselves, a lot, so us retesting the
/// > unchanging sdk is worthless."
///
/// Right, and it is why none of them caught anything: an SDK checkout does not change, so those
/// tests cannot fail for a reason that matters here. `ScenePassOrderConformanceTests` even says so
/// in its own remarks — "it cannot go red on ours" — and was left that way.
///
/// **So this one renders and measures.** Valve's rule, from `CBaseWorldView::DrawExecute` at
/// `game/client/viewrender.cpp:5487`, is that the world and its overlays are drawn before opaque
/// renderables, and that a prop therefore occludes a marking on the wall behind it. The rule is the
/// citation; the assertion is on our own pixels.
///
/// It fails against every arrangement this project had before B135 closed: props batched with the
/// world (so a biased overlay beat them), an overlay writing depth (so it occluded the prop), and a
/// constant depth bias (so the overlay drew through). One test, three defects.
/// </remarks>
public sealed class OverlayOcclusionRenderTests
{
    /// <summary>Map assets, because the shader alpha-tests on a real texture.</summary>
    private static MapAssets? Assets
    {
        get
        {
            string tf = "F:/SteamLibrary/steamapps/common/Team Fortress 2/tf";
            string map = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Tf2DemoSalvage", "maps", "cp_process_f12.bsp");

            if (!System.IO.Directory.Exists(tf) || !System.IO.File.Exists(map))
            {
                return null;
            }

            return MapAssets.Load(
                System.IO.File.ReadAllBytes(map), GameArchives.Open(tf), maximumTextureSize: 256);
        }
    }

    /// <summary>A full-view quad at one depth, carrying a vertex colour that names it.</summary>
    /// <remarks>
    /// **The colour is the instrument, and the first version of this test had none.** Asserting that
    /// the centre pixel was non-black could not tell the prop from the wall behind it, so the test
    /// passed identically with the depth bias that caused the defect restored — a measurement of
    /// "something drew" where the variable is "which thing drew".
    ///
    /// Drawn through the category view, whose shader returns the vertex colour directly
    /// (<c>if (surfaceColours.x > 0.5f) return float4(input.vc, 1.0f);</c>), so each surface can be
    /// identified by the pixel it leaves.
    /// </remarks>
    private static (List<WorldVertex> Vertices, WorldBatch Batch) Quad(
        float depth,
        int material,
        int firstVertex,
        (float Red, float Green, float Blue) colour,
        float half = 1f)
    {
        (float r, float g, float b) = colour;

        // **Wound front-facing, because the overlay pass culls back faces now.** The obvious
        // anticlockwise order made the marking vanish entirely and the test failed on its CONTROL —
        // the corner showed the wall rather than the marking — which is the control doing its job.
        // Real overlay fragments are wound from the BSP face and survive the cull; only this
        // hand-built fixture did not, so the winding is stated here rather than assumed.
        List<WorldVertex> vertices =
        [
            new(-half, -half, depth, 0f, 0f, 0f, 0f, 0f, r, g, b),
            new(half, half, depth, 1f, 1f, 0f, 0f, 0f, r, g, b),
            new(half, -half, depth, 1f, 0f, 0f, 0f, 0f, r, g, b),
            new(-half, -half, depth, 0f, 0f, 0f, 0f, 0f, r, g, b),
            new(-half, half, depth, 0f, 1f, 0f, 0f, 0f, r, g, b),
            new(half, half, depth, 1f, 1f, 0f, 0f, 0f, r, g, b),
        ];

        return (vertices, new WorldBatch(material, firstVertex, vertices.Count));
    }

    [Test]
    public void Render_AMarkingBehindAWall_DoesNotShowThrough()
    {
        // **The case a depth bias actually decides, and the two tests above cannot.** With depth
        // writes off a marking never writes, so a prop drawn afterwards tests against the WALL and
        // wins whatever bias the marking carried. The bias only decides whether the marking itself
        // clears geometry standing in front of it.
        //
        // The owner saw this on cp_process as signage floating in mid-air and REDSTONE CARGO
        // readable through its own silo.
        //
        // **The GAP is the condition, and this test has had it wrong in both directions.** Valve's
        // -262144 against a 24-bit buffer is 0.015625 of the depth range. An occluder 0.4 in front
        // is far beyond its reach, so the first version passed with the defect restored. It was then
        // narrowed to 0.005 — a third of the bias — which made it sensitive and made it assert that
        // this renderer must NOT do what Valve's constant does. That case is real and is measured by
        // the test below, as the trade rather than as a defect.
        //
        // Here the gap is 0.05, three times the bias, which is the arrangement the visible defect
        // was: signage floating clear of a silo, not a marking bleeding through a hairline gap.
        using OffscreenTarget? target = OffscreenTarget.TryCreate(64, 64);

        if (target is null)
        {
            Assert.Ignore("no Direct3D on this machine");
            return;
        }

        if (Assets is not { } assets)
        {
            Assert.Ignore("the map or the game is not installed");
            return;
        }

        (List<WorldVertex> wall, WorldBatch wallBatch) =
            Quad(0.9f, material: 0, firstVertex: 0, colour: (0f, 0f, 1f));

        (List<WorldVertex> mark, WorldBatch markBatch) =
            Quad(0.9f, material: DecalMaterial(assets), firstVertex: 6, colour: (1f, 0f, 0f));

        // The occluder goes in the WORLD batch, so it is in the depth buffer before the overlay
        // pass runs — the same position a wall between the camera and a marked surface occupies.
        (List<WorldVertex> occluder, WorldBatch occluderBatch) =
            Quad(0.85f, material: 0, firstVertex: 12, colour: (0f, 1f, 0f), half: 0.5f);

        List<WorldVertex> all = [.. wall, .. mark, .. occluder];

        target.Clear(0f, 0f, 0f);

        target.DrawWorld(
            all, [wallBatch, occluderBatch], Identity, assets, surfaceColours: true,
            decals: [markBatch]);

        (int red, int green, int blue) = target.PixelAt(32, 32);

        Winner(red, green, blue).ShouldBe(
            "prop",
            "the occluder is in front of the marking, so the marking must not draw through it; " +
            "a constant depth bias on the overlay pass is what lets it");

        // The control: outside the occluder the marking IS what should be there, so a marking that
        // simply never drew could not pass the assertion above.
        (int cornerRed, int cornerGreen, int cornerBlue) = target.PixelAt(4, 4);

        Winner(cornerRed, cornerGreen, cornerBlue).ShouldBe(
            "marking", "outside the occluder the marking on the wall is what draws");
    }

    [Test]
    public void Render_AnOccluderNearerThanTheBias_LosesToTheMarkingAsValvesConstantIntends()
    {
        // **The trade `SHADER_POLYOFFSET_DECAL` makes, recorded as behaviour rather than as a bug.**
        // A depth bias moves a marking toward the camera by a fixed fraction of the depth range, so
        // anything standing in front of it by LESS than that fraction is beaten. That is not a
        // defect in this renderer; it is what the constant is for, and Valve accepts it in exchange
        // for markings that do not z-fight with the surfaces they lie on.
        //
        // 0.005 against a bias of 0.015625 — a third of it. The sibling test above uses 0.05, three
        // times the bias, and asserts the opposite outcome. **Two conditions either side of one
        // threshold is what makes this a measurement of the bias rather than of the fixture**, and
        // it is why the pair is worth more than either alone: a renderer with no bias at all passes
        // the sibling and fails this one.
        //
        // Under perspective, which is what Valve draws with and what D49 moves this project to, a
        // 0.005 slice of NDC depth is a fraction of a world unit near the camera — so the case this
        // test describes is a hairline, not the signage-floating-off-a-silo the owner reported.
        using OffscreenTarget? target = OffscreenTarget.TryCreate(64, 64);

        if (target is null)
        {
            Assert.Ignore("no Direct3D on this machine");
            return;
        }

        if (Assets is not { } assets)
        {
            Assert.Ignore("the map or the game is not installed");
            return;
        }

        (List<WorldVertex> wall, WorldBatch wallBatch) =
            Quad(0.9f, material: 0, firstVertex: 0, colour: (0f, 0f, 1f));

        (List<WorldVertex> mark, WorldBatch markBatch) =
            Quad(0.9f, material: DecalMaterial(assets), firstVertex: 6, colour: (1f, 0f, 0f));

        (List<WorldVertex> occluder, WorldBatch occluderBatch) =
            Quad(0.895f, material: 0, firstVertex: 12, colour: (0f, 1f, 0f), half: 0.5f);

        List<WorldVertex> all = [.. wall, .. mark, .. occluder];

        target.Clear(0f, 0f, 0f);

        target.DrawWorld(
            all, [wallBatch, occluderBatch], Identity, assets, surfaceColours: true,
            decals: [markBatch]);

        (int red, int green, int blue) = target.PixelAt(32, 32);

        Winner(red, green, blue).ShouldBe(
            "marking",
            "an occluder closer than the bias is beaten by it — remove the constant bias and this " +
            "reports the prop, which is how the pair distinguishes a biased pass from an unbiased one");

        // The control: the marking has to be drawing at all for the assertion above to mean it won
        // a contest rather than that the occluder simply was not there.
        (int cornerRed, int cornerGreen, int cornerBlue) = target.PixelAt(4, 4);

        Winner(cornerRed, cornerGreen, cornerBlue).ShouldBe(
            "marking", "outside the occluder the marking on the wall is what draws");
    }

    /// <summary>A material the map declares as a marking — one that carries <c>$decal</c>.</summary>
    /// <remarks>
    /// **A real one, because the depth state is now a property of the MATERIAL (B135).** An overlay
    /// whose material is not decal-flagged gets the opaque state — depth writes on, compared with
    /// `Less` — and therefore loses to its own wall at equal depth and never draws. That is correct
    /// behaviour under the engine's arrangement, and it is what this test discovered when it was
    /// pointed at an arbitrary material index: the CONTROL failed, reporting the wall where the
    /// marking should have been.
    ///
    /// cp_process's wall stripes carry `"$decal" "1"` — verified against the shipped VMTs by
    /// <c>OverlayMaterialProbe</c> — so the map itself supplies the fixture.
    /// </remarks>
    private static int DecalMaterial(MapAssets assets)
    {
        for (int index = 0; index < assets.Materials.Count; index++)
        {
            if (assets.Materials[index].Name.Contains("stripe", StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        throw new InvalidOperationException(
            "cp_process declares stripe materials; without one this test measures nothing");
    }

    /// <summary>Which of the three surfaces a pixel came from, by its colour.</summary>
    private static string Winner(int red, int green, int blue) =>
        (red, green, blue) switch
        {
            ( > 128, < 128, < 128) => "marking",
            ( < 128, > 128, < 128) => "prop",
            ( < 128, < 128, > 128) => "wall",
            _ => $"none of the three ({red},{green},{blue})",
        };

    private static float[] Identity =>
    [
        1f, 0f, 0f, 0f,
        0f, 1f, 0f, 0f,
        0f, 0f, 1f, 0f,
        0f, 0f, 0f, 1f,
    ];

    [Test]
    public void Render_APropInFrontOfAMarkedWall_HidesTheMarking()
    {
        using OffscreenTarget? target = OffscreenTarget.TryCreate(64, 64);

        if (target is null)
        {
            Assert.Ignore("no Direct3D on this machine");
            return;
        }

        if (Assets is not { } assets)
        {
            Assert.Ignore("the map or the game is not installed");
            return;
        }

        // A wall far away, a marking ON that wall at the same depth, and a prop between it and the
        // camera covering the middle of the view. Valve's order says the prop wins there.
        (List<WorldVertex> wall, WorldBatch wallBatch) =
            Quad(0.9f, material: 0, firstVertex: 0, colour: (0f, 0f, 1f));

        (List<WorldVertex> mark, WorldBatch markBatch) =
            Quad(0.9f, material: DecalMaterial(assets), firstVertex: 6, colour: (1f, 0f, 0f));

        // Half-size so the marking stays visible around its edges — without that, "the prop covers
        // everything" and "the prop is drawn correctly" are the same picture.
        (List<WorldVertex> prop, WorldBatch propBatch) =
            Quad(0.5f, material: 2, firstVertex: 12, colour: (0f, 1f, 0f), half: 0.5f);

        List<WorldVertex> all = [.. wall, .. mark, .. prop];

        target.Clear(0f, 0f, 0f);

        target.DrawWorld(
            all, [wallBatch], Identity, assets, surfaceColours: true,
            decals: [markBatch], props: [propBatch]);

        // **The centre is the PROP's**, at depth 0.5, in front of both. Named by colour rather than
        // by brightness, so "the prop won" is distinguishable from "the marking won" — which is
        // exactly what the first version of this test could not tell apart.
        (int red, int green, int blue) = target.PixelAt(32, 32);

        Winner(red, green, blue).ShouldBe(
            "prop",
            "a prop in front of the wall must occlude the marking on it: props are drawn after the " +
            "overlays, as the engine draws them (viewrender.cpp:5487)");

        // **The control, without which "the prop won" and "only the prop drew" are one
        // observation.** A corner is outside the prop, so the marking is what should be there — and
        // if the marking were missing entirely the test above would pass for the wrong reason.
        (int cornerRed, int cornerGreen, int cornerBlue) = target.PixelAt(4, 4);

        Winner(cornerRed, cornerGreen, cornerBlue).ShouldBe(
            "marking", "outside the prop, the marking on the wall is what draws");
    }

    [Test]
    public void Render_AMarkingOnAWall_DoesNotWriteDepthOverIt()
    {
        // **The second half of the same rule, and the one B135 actually broke.** An overlay that
        // writes depth puts a value in the buffer that the wall never had — with a bias, one NEARER
        // than the wall — so everything drawn afterwards tests against a surface that is not there.
        // Valve's decal shaders say EnableDepthWrites( false ) in one line
        // (DecalModulate_dx9.cpp:66); this asserts our pixels agree.
        using OffscreenTarget? target = OffscreenTarget.TryCreate(64, 64);

        if (target is null)
        {
            Assert.Ignore("no Direct3D on this machine");
            return;
        }

        if (Assets is not { } assets)
        {
            Assert.Ignore("the map or the game is not installed");
            return;
        }

        // Marking at 0.9, prop at 0.89 — one hundredth of the depth range in front, which a
        // constant bias of 1.6% clears comfortably. That is the margin the defect had.
        (List<WorldVertex> wall, WorldBatch wallBatch) =
            Quad(0.9f, material: 0, firstVertex: 0, colour: (0f, 0f, 1f));

        (List<WorldVertex> mark, WorldBatch markBatch) =
            Quad(0.9f, material: DecalMaterial(assets), firstVertex: 6, colour: (1f, 0f, 0f));

        (List<WorldVertex> prop, WorldBatch propBatch) =
            Quad(0.89f, material: 2, firstVertex: 12, colour: (0f, 1f, 0f), half: 0.5f);

        List<WorldVertex> all = [.. wall, .. mark, .. prop];

        target.Clear(0f, 0f, 0f);

        target.DrawWorld(
            all, [wallBatch], Identity, assets, surfaceColours: true,
            decals: [markBatch], props: [propBatch]);

        Winner(target.PixelAt(32, 32).Red, target.PixelAt(32, 32).Green, target.PixelAt(32, 32).Blue)
            .ShouldBe(
                "prop",
                "a prop a hundredth of the depth range in front of a marking must draw over it; " +
                "if the marking wins, the overlay pass is writing depth or carrying a constant bias");
    }
}
