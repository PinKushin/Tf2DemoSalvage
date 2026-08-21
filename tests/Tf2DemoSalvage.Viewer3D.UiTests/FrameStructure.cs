using System;
using System.Collections.Generic;
using System.Drawing;

namespace Tf2DemoSalvage.Viewer3D.UiTests;

/// <summary>
/// How much is actually IN a captured frame, as distinct from how bright it is.
/// </summary>
/// <remarks>
/// **The suite could not tell a scene from a wall, and that is why a wall went unnoticed.** The only
/// pixel assertion here counted pixels brighter than a threshold and required one in twenty — which
/// brown planks filling the viewport satisfy comfortably. The owner spotted it by looking at the
/// captures: "im pretty sure the SS's are not actually showing anything either, its looking at a
/// fning wall", against a suite reporting twelve of twelve.
///
/// **Brightness is the wrong variable.** A wall and a map are both lit; what separates them is
/// VARIETY. A surface a few feet from the camera fills the frame with one material at one distance,
/// so its pixels cluster; a view down a map carries sky, terrain, props and shadow, so they spread.
/// This counts how many coarse colour buckets a frame occupies, which is a direct measure of that.
///
/// Deliberately coarse — sixteen levels per channel — so that texture noise and compression do not
/// register as structure. The question is "how many distinguishable things are on screen", not "how
/// many colours".
/// </remarks>
internal static class FrameStructure
{
    /// <summary>Levels per channel, so a bucket is a visibly different colour rather than a shade.</summary>
    private const int Levels = 16;

    /// <summary>Sample every Nth pixel, which is plenty at viewport sizes and much faster.</summary>
    private const int Step = 4;

    /// <summary>How many distinct coarse colours a frame contains.</summary>
    /// <param name="picture">A capture read back from the swap chain.</param>
    /// <returns>The count of occupied buckets.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="picture"/> is null.</exception>
    public static int Colours(Bitmap picture)
    {
        ArgumentNullException.ThrowIfNull(picture);

        HashSet<int> buckets = [];

        for (int y = 0; y < picture.Height; y += Step)
        {
            for (int x = 0; x < picture.Width; x += Step)
            {
                Color pixel = picture.GetPixel(x, y);

                int key = (Bucket(pixel.R) << 8) | (Bucket(pixel.G) << 4) | Bucket(pixel.B);

                buckets.Add(key);
            }
        }

        return buckets.Count;
    }

    /// <summary>One channel, reduced to <see cref="Levels"/> steps.</summary>
    private static int Bucket(byte channel) => channel * Levels / 256;
}
