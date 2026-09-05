using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// That a material animating its base texture arrives with every frame (B341).
/// </summary>
/// <remarks>
/// **The half B338 left out.** That entry read the frames and computed which one to show, and
/// stopped before uploading them — so nothing animated. This is the hop that makes it draw.
///
/// **`$basetexture` rather than `$detail`, deliberately, and the reason is visibility.** 152
/// shipped materials animate `$basetexture` through `$frame` and they animate UNCONDITIONALLY;
/// 6,735 animate `$detail`, whose blend factor is `BurnLevel` and therefore zero unless somebody is
/// on fire. The second group also nearly all animate ONE file — 121 frames of TF2's fire sheet — so
/// doing them per material would decode it thousands of times, and B338 records the cache keyed by
/// texture path that they need instead.
/// </remarks>
public sealed class AnimatedTextureWiringTests
{
    /// <remarks>
    /// **Counted with the majority as the control**, the shape every wiring test here uses. What
    /// the map happens to contain is Valve's to change, so the assertion is that the resolve runs
    /// and is selective — not that a particular material is present.
    /// </remarks>
    [Test]
    public void AnimationFrames_ARealMapsMaterials_ArriveOnlyWhereAProxyAsks()
    {
        if (Assets is not { } assets)
        {
            Assert.Ignore("the map or the game is not installed");
            return;
        }

        int animated = assets.AnimationFrames.Count(frames => frames is not null);
        int still = assets.AnimationFrames.Count(frames => frames is null);

        TestContext.Out.WriteLine(
            $"{animated} of {animated + still} materials animate their base texture, "
            + $"{assets.AnimationFrames.Sum(frames => frames?.Count ?? 0)} frames in all");

        assets.AnimationFrames.Count.ShouldBe(
            assets.Materials.Count, "the list is indexed by material and must be parallel to them");

        still.ShouldBeGreaterThan(
            animated, "the great majority of a map's materials animate nothing");
    }

    /// <remarks>
    /// **A resolved animation must have more than one frame**, which is the guard that keeps a
    /// proxy written for a whole family from producing a one-frame "animation" on the members that
    /// are still images. Many shipped materials run `AnimatedTexture` over a single-frame texture,
    /// and the engine refuses those at bind — <c>if ( numFrames &lt;= 0 )</c>; one frame needs no
    /// animating either way.
    /// </remarks>
    [Test]
    public void AnimationFrames_WhereverTheyResolve_HoldMoreThanOneFrame()
    {
        if (Assets is not { } assets)
        {
            Assert.Ignore("the map or the game is not installed");
            return;
        }

        foreach (IReadOnlyList<MapTexture>? frames in assets.AnimationFrames)
        {
            if (frames is not null)
            {
                frames.Count.ShouldBeGreaterThan(
                    1, "a one-frame animation is a still texture and must resolve to null");
            }
        }
    }

    /// <remarks>
    /// **The frames must actually DIFFER**, which is the only assertion here a broken frame offset
    /// cannot satisfy: a reader still returning frame zero produces a list of identical images and
    /// passes every count above. Skipped rather than failed when the map animates nothing, because
    /// what it contains is not this project's to pin.
    /// </remarks>
    [Test]
    public void AnimationFrames_TheFramesOfOneAnimation_AreDifferentPictures()
    {
        if (Assets is not { } assets)
        {
            Assert.Ignore("the map or the game is not installed");
            return;
        }

        IReadOnlyList<MapTexture>? animation =
            assets.AnimationFrames.FirstOrDefault(frames => frames is not null);

        if (animation is null)
        {
            Assert.Ignore("no material on this map animates its base texture");
            return;
        }

        // The top level of each frame — level 0 is the one drawn, and comparing it is what says
        // the frame offset moved rather than that the two are different objects.
        animation[0].Image.Levels[0].ToArray().ShouldNotBe(
            animation[1].Image.Levels[0].ToArray(),
            "consecutive frames of an animation are different pictures; identical bytes mean the "
            + "frame offset never moved");
    }

    private static MapAssets? Assets
    {
        get
        {
            if (GameInstall.Root is not { } tf ||
                !File.Exists(Path.Combine(tf, "maps", "cp_process_final.bsp")))
            {
                return null;
            }

            return MapCache.Load();
        }
    }
}
