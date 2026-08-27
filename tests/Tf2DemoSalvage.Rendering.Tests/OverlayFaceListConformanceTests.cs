using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// An overlay's face list is decided by the mapper, and vbsp tests nothing about orientation.
/// </summary>
/// <remarks>
/// **B68 established this and the code kept a filter that contradicts it.** That entry closed under
/// the title "an overlay's face list is what to CLIP against, not a list to choose from", and the
/// loop that clips still refused any face whose normal was more than about 25 degrees off the
/// overlay's basis. Measured on cp_process_f12: 108 of 634 named faces refused, every one of them on
/// <c>overlays/stripe_red</c> or <c>concrete/stripe_blue</c> — the red and blue wall stripes, and
/// exactly what the owner reported as missing from faces they belong on.
///
/// **vbsp puts no such condition on the list.** <c>Overlay_AddFaceToLists</c>,
/// <c>utils/vbsp/overlay.cpp:171</c>, adds a face because it came from a side the mapper assigned
/// the overlay to:
///
/// <code>
/// void Overlay_AddFaceToLists( int iFace, side_t *pSide )
/// {
///     int nOverlayIdCount = pSide->aOverlayIds.Count();
///     for( int iOverlayId = 0; iOverlayId &lt; nOverlayIdCount; ++iOverlayId )
///     {
///         mapoverlay_t *pMapOverlay = &amp;g_aMapOverlays.Element( pSide->aOverlayIds[iOverlayId] );
///         if ( pMapOverlay )
///         {
///             if( pMapOverlay->aFaceList.Find( iFace ) == -1 )
///             {
///                 pMapOverlay->aFaceList.AddToTail( iFace );
///             }
///         }
///     }
/// }
/// </code>
///
/// There is no normal in that function, no dot product, and no angle. The only test is "has this
/// face already been added". The side list comes from the mapper's own selection in Hammer, so the
/// face list is a statement of intent, not a set of candidates.
///
/// **What this test cannot say.** It establishes that the LIST is complete, not that this project's
/// projection draws every entry correctly. A face at 90 degrees to the overlay projects onto its own
/// plane as a line, so it contributes nothing whatever the list says — two such faces exist on
/// cp_process. That is a separate limit, recorded in `MapWorld` rather than papered over with an
/// orientation threshold that also refuses the 90 faces at 45 degrees.
/// </remarks>
public sealed class OverlayFaceListConformanceTests
{
    /// <summary>cp_process_f12, which is where the 108 refused faces were counted.</summary>
    private static string MapPath => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Tf2DemoSalvage", "maps", "cp_process_f12.bsp");

    [Test]
    public void ClipFaceToOverlay_AFaceAtAnAngleToTheBasis_StillProducesAFragment()
    {
        // **The half this file was missing: Valve's rule above is the citation, and this is the
        // measurement on our own clipper.** The test below reads vbsp and can only ever confirm
        // that vbsp still says what it says — it could not have caught the filter that contradicted
        // it, which lived here and refused 108 of cp_process's 634 named faces.
        //
        // The condition is chosen so that a reinstated threshold has to fail it. `|dot|` between
        // 0.2 and 0.9 is roughly 25 to 78 degrees off the overlay's basis — the band the old filter
        // rejected wholesale, and where 90 of those 108 faces sat, on chamfered corners the
        // projection handles perfectly well.
        if (!System.IO.File.Exists(MapPath))
        {
            Assert.Ignore("cp_process_f12.bsp is not on this machine");
            return;
        }

        byte[] bytes = System.IO.File.ReadAllBytes(MapPath);

        Dictionary<int, BspSurface> byFace = [];

        foreach (BspSurface surface in BspSurfaces.Read(bytes))
        {
            byFace[surface.FaceIndex] = surface;
        }

        int angled = 0;
        int drawn = 0;
        int flat = 0;
        int chamfered = 0;
        int chamferedDrawn = 0;

        foreach (BspOverlay overlay in BspOverlays.Read(bytes))
        {
            foreach (int face in overlay.Faces)
            {
                if (!byFace.TryGetValue(face, out BspSurface? piece))
                {
                    continue;
                }

                float dot = Math.Abs(
                    (overlay.BasisNormal.X * piece.Normal.X) +
                    (overlay.BasisNormal.Y * piece.Normal.Y) +
                    (overlay.BasisNormal.Z * piece.Normal.Z));

                bool produced =
                    MapWorldBuilder.ClipFaceToOverlay(piece, overlay, overlay.WorldCorners).Count >= 3;

                if (dot > 0.9f)
                {
                    // The control: faces square-on to the overlay must draw, or a clipper that
                    // returns nothing at all would satisfy every assertion about the angled ones.
                    flat++;

                    if (produced)
                    {
                        continue;
                    }
                }

                if (dot is > 0.2f and <= 0.9f)
                {
                    angled++;

                    if (produced)
                    {
                        drawn++;
                    }
                    if (Math.Abs(dot - 0.7071f) < 0.01f)
                    {
                        chamfered++;

                        if (produced)
                        {
                            chamferedDrawn++;
                        }
                    }
                }
            }
        }

        // The condition has to exist on this map, or everything below is vacuous.
        angled.ShouldBeGreaterThan(
            50, "cp_process has ~90 named faces at roughly 45 degrees; without them this proves nothing");

        flat.ShouldBeGreaterThan(0, "and some square-on ones, as the control");

        TestContext.Out.WriteLine(
            $"angled {angled}, drawn {drawn}; at 45 degrees {chamfered}, drawn {chamferedDrawn}");

        // **The assertion, and the first draft of it was wrong in an instructive way.** It demanded
        // that EVERY angled face produce a fragment, and 11 of 102 did not — all of them at exactly
        // |dot| 0.707. That reads like an orientation defect and is not one: **the great majority
        // of faces at that same angle DO draw**, so the angle cannot be what refuses them.
        //
        // What refuses them is the overlay's quad not covering that part of the face, which is the
        // engine's behaviour too — it clips the face to the overlay's volume, and a face outside
        // the volume contributes nothing. "The mapper assigned this overlay to that side" does not
        // promise the projection lands on every square inch of it.
        //
        // So the property to assert is the one an orientation test would violate: a threshold
        // refuses a CONTIGUOUS BAND regardless of position, so under a reinstated filter at 0.9
        // both counts below go to exactly zero. That is what makes this sensitive; demanding
        // totality made it sensitive to something else.
        chamfered.ShouldBeGreaterThan(
            50, "cp_process's chamfered corners are the population this is about");

        chamferedDrawn.ShouldBeGreaterThan(
            chamfered / 2,
            "vbsp puts no orientation test on the face list, so neither may this clipper — a face "
            + "the mapper assigned the overlay to is one to clip against, not one to choose from "
            + "(B68). A filter at |dot| > 0.9 makes this zero");

        drawn.ShouldBeGreaterThan(
            angled / 2,
            "and the same across the whole angled band, not just at 45 degrees");
    }

    [Test]
    public void OverlayFaceList_AsVbspBuildsIt_TestsNothingAboutOrientation()
    {
        if (!SourceSdk.Available)
        {
            Assert.Ignore(SourceSdk.Missing);
            return;
        }

        string text = SourceSdk.Text("src/utils/vbsp/overlay.cpp")
            ?? throw new InvalidOperationException("vbsp/overlay.cpp is missing from the SDK");

        Match body = new Regex(
            @"void Overlay_AddFaceToLists\([^)]*\)(?s).{0,900}?\n\}",
            RegexOptions.Compiled,
            TimeSpan.FromSeconds(10)).Match(text);

        body.Success.ShouldBeTrue("Overlay_AddFaceToLists was not found in vbsp");

        // The positive: membership comes from the side's overlay ids, which is the mapper's choice.
        body.Value.ShouldContain("aOverlayIds");
        body.Value.ShouldContain("aFaceList.AddToTail");

        // The negative, which is the claim this test exists for. If vbsp ever starts filtering by
        // orientation, the renderer may too — and this reddens rather than leaving a comment that
        // quietly describes something no longer true.
        foreach (string absent in new[] { "normal", "Normal", "DotProduct", "dot(" })
        {
            body.Value.ShouldNotContain(
                absent,
                Case.Sensitive,
                $"vbsp's face list would be filtered by '{absent}', so ours could be too");
        }
    }
}
