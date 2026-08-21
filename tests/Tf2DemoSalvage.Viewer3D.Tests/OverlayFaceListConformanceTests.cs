using System;
using System.Text.RegularExpressions;

using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Viewer3D.Tests;

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
